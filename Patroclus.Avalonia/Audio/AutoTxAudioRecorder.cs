using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Patroclus.Avalonia.Audio
{
    // Preserves PTT/data ordering without opening files or flushing WAV headers
    // on the UDP thread. Each transmission gets a separate file.
    public sealed class AutoTxAudioRecorder
    {
        private readonly object gate = new();
        private readonly TxAudioRecorder recorder = new();
        private BlockingCollection<Action> commands;
        private Thread worker;
        private volatile bool armed;
        private bool transmitting;
        private string directory, error;
        private TxAudioMode mode;
        public bool IsArmed => armed;
        public event Action<string> RecordingCompleted
        {
            add => recorder.RecordingCompleted += value;
            remove => recorder.RecordingCompleted -= value;
        }
        public string Status
        {
            get
            {
                string currentError, folder; bool enabled, tx;
                lock (gate) { currentError = error; folder = directory; enabled = armed; tx = transmitting; }
                if (currentError != null) return "Auto recording error: " + currentError;
                string capture = recorder.Status;
                if (capture.StartsWith("Recording error:", StringComparison.Ordinal)) return "Auto · " + capture;
                if (!enabled) return folder == null ? "Auto recording off" : "Auto recording off · " + capture;
                return tx ? "Auto · " + capture : "Auto armed · waiting for TX · " + folder;
            }
        }

        public void Arm(string folder, TxAudioMode selectedMode, bool alreadyTransmitting)
        {
            Disarm();
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Select an existing recording folder.");
            lock (gate)
            {
                directory = Path.GetFullPath(folder); mode = selectedMode; error = null;
                transmitting = false; commands = new BlockingCollection<Action>(256); armed = true;
                worker = new Thread(Run) { IsBackground = true, Name = "Automatic TX recording" };
                worker.Start();
            }
            SetTransmitting(alreadyTransmitting);
        }
        public void Disarm()
        {
            Thread current;
            lock (gate) { armed = false; transmitting = false; commands?.CompleteAdding(); current = worker; }
            current?.Join();
            lock (gate) { worker = null; commands?.Dispose(); commands = null; }
        }
        public void SetTransmitting(bool tx)
        {
            lock (gate)
            {
                if (!armed || tx == transmitting) return;
                transmitting = tx;
                if (!tx) { Enqueue(() => recorder.Stop()); return; }
                // Capture time/mode at the PTT edge, not later on the disk thread.
                string filename = $"TX_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{mode}.wav";
                string folder = directory;
                TxAudioMode capturedMode = mode;
                Enqueue(() =>
                {
                    string path = Path.Combine(folder, filename);
                    if (File.Exists(path)) path = Path.Combine(folder, Path.GetFileNameWithoutExtension(filename) + "_" + Guid.NewGuid().ToString("N") + ".wav");
                    recorder.Start(path, capturedMode);
                });
            }
        }
        public void Protocol1(byte[] data, int start, int samples, bool tx)
        {
            lock (gate)
            {
                if (!armed || !transmitting || !tx || data == null || start < 0 || samples < 0 || (long)start + samples * 8L > data.Length) return;
                var copy = new byte[samples * 8]; Buffer.BlockCopy(data, start, copy, 0, copy.Length);
                Enqueue(() => recorder.Protocol1(copy, 0, samples, true));
            }
        }
        public void Protocol2(byte[] data, int rate, bool tx)
        {
            lock (gate)
            {
                if (!armed || !transmitting || !tx || data == null || data.Length != 1444) return;
                var copy = (byte[])data.Clone();
                Enqueue(() => recorder.Protocol2(copy, rate, true));
            }
        }
        private void Enqueue(Action command)
        {
            if (commands.TryAdd(command)) return;
            error = "Input buffer overrun; automatic recording disabled.";
            armed = false; commands.CompleteAdding();
        }
        private void Run()
        {
            try { foreach (var command in commands.GetConsumingEnumerable()) command(); }
            catch (Exception ex)
            {
                lock (gate) { error = ex.Message; armed = false; commands.CompleteAdding(); }
            }
            finally { recorder.Stop(); }
        }
    }
}
