using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Patroclus.Avalonia.Audio
{
    public enum TxAudioMode { SSB, AM, FM, USB, LSB }

    // A passive TX-IQ tap. File I/O is isolated from the UDP processing thread.
    public sealed class TxAudioRecorder
    {
        private readonly object gate = new();
        private BlockingCollection<short[]> queue;
        private Thread writerThread;
        private volatile bool recording;
        private string error, file;
        private long samplesWritten, dropped;
        private TxAudioMode mode;
        private int sourceRate, count;
        private double sum, previousI, previousQ, dcInput, dcOutput;
        public bool IsRecording => recording;
        // Raised on the writer thread, after the header is finalized and the file is closed.
        public event Action<string> RecordingCompleted;
        public string Status
        {
            get
            {
                lock (gate)
                {
                    if (error != null) return "Recording error: " + error;
                    if (file == null) return "Ready · WAV 48 kHz / 16-bit / mono";
                    return $"{(recording ? "Recording" : "Saved")}: {Path.GetFileName(file)} · {Interlocked.Read(ref samplesWritten) / 48000.0:F1} s" +
                        (dropped > 0 ? $" · WARNING: {dropped} audio blocks dropped" : "");
                }
            }
        }

        public void Start(string path, TxAudioMode demodulation)
        {
            Stop();
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.Write(new byte[44], 0, 44);
            lock (gate)
            {
                file = path; error = null; samplesWritten = 0; dropped = 0;
                sourceRate = 0; count = 0; sum = previousI = previousQ = dcInput = dcOutput = 0;
                mode = demodulation;
                queue = new BlockingCollection<short[]>(256);
                recording = true;
                writerThread = new Thread(() => WriteWave(stream, path)) { IsBackground = true, Name = "TX audio WAV writer" };
                writerThread.Start();
            }
        }

        public void Stop()
        {
            Thread worker;
            lock (gate)
            {
                recording = false;
                queue?.CompleteAdding();
                worker = writerThread;
            }
            worker?.Join();
            lock (gate) { writerThread = null; queue?.Dispose(); queue = null; }
        }

        public void Protocol1(byte[] data, int start, int samples, bool transmitting)
        {
            if (!recording) return;
            lock (gate)
            {
                if (!recording) return;
                if (!transmitting) { ResetSignal(); return; }
                if (data == null || start < 0 || samples < 0 || (long)start + samples * 8L > data.Length) return;
                SetRate(48000);
                var output = new short[samples]; int written = 0;
                for (int n = 0; n < samples; n++)
                {
                    int p = start + n * 8;
                    double i = (short)((data[p + 4] << 8) | data[p + 5]) / 32768.0;
                    double q = (short)((data[p + 6] << 8) | data[p + 7]) / 32768.0;
                    Sample(i, q, output, ref written);
                }
                Enqueue(output, written);
            }
        }

        public void Protocol2(byte[] data, int sampleRate, bool transmitting)
        {
            if (!recording) return;
            lock (gate)
            {
                if (!recording) return;
                if (!transmitting) { ResetSignal(); return; }
                if (data == null || data.Length != 1444 || sampleRate < 48000 || sampleRate > 1536000 || sampleRate % 48000 != 0) return;
                SetRate(sampleRate);
                var output = new short[240]; int written = 0;
                for (int p = 4; p < 1444; p += 6)
                    Sample(Read24(data, p), Read24(data, p + 3), output, ref written);
                Enqueue(output, written);
            }
        }

        private static double Read24(byte[] d, int p)
            => ((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8)) / 2147483648.0;
        private void SetRate(int rate)
        {
            if (sourceRate == rate) return;
            sourceRate = rate; ResetSignal();
        }
        private void ResetSignal() { count = 0; sum = previousI = previousQ = dcInput = dcOutput = 0; }
        public void Pause()
        {
            if (!recording) return;
            lock (gate) ResetSignal();
        }
        private void Sample(double i, double q, short[] output, ref int written)
        {
            double audio = i;
            if (mode == TxAudioMode.AM) audio = Math.Sqrt(i * i + q * q);
            if (mode == TxAudioMode.FM)
                audio = Math.Atan2(q * previousI - i * previousQ, i * previousI + q * previousQ) * sourceRate / (2 * Math.PI * 5000);
            previousI = i; previousQ = q;
            // TX speech is baseband-limited by the client. Average down to 48 kHz.
            sum += audio; count++;
            int factor = sourceRate / 48000;
            if (count < factor) return;
            double value = sum / count; sum = 0; count = 0;
            // 20 Hz DC blocker removes AM carrier and constant offsets.
            double filtered = value - dcInput + 0.997385 * dcOutput;
            dcInput = value; dcOutput = filtered;
            output[written++] = (short)Math.Round(Math.Clamp(filtered, -1, 1) * 32767);
        }
        private void Enqueue(short[] output, int length)
        {
            if (length == 0) return;
            if (length != output.Length) Array.Resize(ref output, length);
            if (!queue.TryAdd(output)) dropped++;
        }
        private void WriteWave(FileStream stream, string path)
        {
            try
            {
                using (stream)
                using (var writer = new BinaryWriter(stream))
                {
                    try
                    {
                        foreach (var block in queue.GetConsumingEnumerable())
                        {
                            if (Interlocked.Read(ref samplesWritten) + block.Length > (uint.MaxValue - 36L) / 2)
                                throw new IOException("WAV size limit reached; start a new recording.");
                            foreach (short sample in block) writer.Write(sample);
                            Interlocked.Add(ref samplesWritten, block.Length);
                        }
                    }
                    finally
                    {
                        uint bytes = (uint)(Interlocked.Read(ref samplesWritten) * 2);
                        stream.Position = 0;
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36u + bytes);
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16u);
                        writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(48000u);
                        writer.Write(96000u); writer.Write((ushort)2); writer.Write((ushort)16);
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                    }
                }
            }
            catch (Exception ex) { lock (gate) { error = ex.Message; recording = false; } return; }
            // Notification failures must not terminate the audio worker or invalidate a saved WAV.
            var handlers = RecordingCompleted;
            if (handlers != null)
                foreach (Action<string> handler in handlers.GetInvocationList())
                    try { handler(path); } catch { }
        }
    }
}
