using System;
using System.Collections.Generic;
using System.Text;
using Patroclus.Avalonia.Audio;
using ReactiveUI;
using System.Threading;

namespace Patroclus.Avalonia.ViewModels
{
    public class FakeRadio : ViewModelBase
    {
        public ControlMonitor Monitor { get; protected set; }
        public TxAudioRecorder Recorder { get; } = new();
        public AutoTxAudioRecorder AutoRecorder { get; } = new();
        private bool autoRecordingEnabled;
        public bool AutoRecordingEnabled { get => autoRecordingEnabled; private set => this.RaiseAndSetIfChanged(ref autoRecordingEnabled, value); }
        public string AutoRecordButtonText => AutoRecordingEnabled ? "Auto record TX: ON" : "Auto record TX: OFF";
        private string autoRecordingStatus = "Auto recording off";
        public string AutoRecordingStatus { get => autoRecordingStatus; private set => this.RaiseAndSetIfChanged(ref autoRecordingStatus, value); }
        public bool CanStartManualRecording => !IsRecording && !AutoRecordingEnabled;
        public bool CanChangeRecordingMode => !IsRecording && !AutoRecordingEnabled;
        public void EnableAutoRecording(string folder, TxAudioMode mode)
        {
            if (Recorder.IsRecording) throw new InvalidOperationException("Stop the manual recording first.");
            AutoRecorder.Arm(folder, mode, Volatile.Read(ref transmitActive)); RefreshRecording();
        }
        public void DisableAutoRecording() { AutoRecorder.Disarm(); RefreshRecording(); }
        public TxSignalMonitor TxSignal { get; } = new();
        private bool muteRxDuringTx = true;
        protected bool transmitActive;
        public bool MuteRxDuringTx
        {
            get => Volatile.Read(ref muteRxDuringTx);
            set
            {
                if (MuteRxDuringTx == value) return;
                Volatile.Write(ref muteRxDuringTx, value);
                this.RaisePropertyChanged();
            }
        }
        private string rxTxStatus = "RX: test signals enabled";
        public string RxTxStatus { get => rxTxStatus; private set => this.RaiseAndSetIfChanged(ref rxTxStatus, value); }
        private string txInputStatus = "TX input: PTT off";
        public string TxInputStatus { get => txInputStatus; private set => this.RaiseAndSetIfChanged(ref txInputStatus, value); }
        private string recordingStatus = "Ready · WAV 48 kHz / 16-bit / mono";
        private bool isRecording;
        public string RecordingStatus { get => recordingStatus; private set => this.RaiseAndSetIfChanged(ref recordingStatus, value); }
        public bool IsRecording { get => isRecording; private set => this.RaiseAndSetIfChanged(ref isRecording, value); }
        protected virtual string ControlStatus => "";
        private string controlInputStatus = "";
        protected string streamScheduling = "Starting";
        private long lastStreamLoop, maxLoopGapTicks, maxBacklogMicroseconds, slowLoops;
        private string streamTimingStatus = "";
        public string StreamTimingStatus { get => streamTimingStatus; private set => this.RaiseAndSetIfChanged(ref streamTimingStatus, value); }
        protected void ObserveStreamLoop(bool running)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long gap = lastStreamLoop == 0 ? 0 : now - lastStreamLoop;
            lastStreamLoop = now;
            if (!running) return;
            if (gap > Interlocked.Read(ref maxLoopGapTicks)) Interlocked.Exchange(ref maxLoopGapTicks, gap);
            if (gap > System.Diagnostics.Stopwatch.Frequency / 100) Interlocked.Increment(ref slowLoops);
        }
        protected void ObserveStreamBacklog(long packets, int samples, int rate)
        {
            if (packets <= 1 || rate <= 0) return;
            long delay = (packets - 1) * samples * 1000000L / rate;
            if (delay > Interlocked.Read(ref maxBacklogMicroseconds)) Interlocked.Exchange(ref maxBacklogMicroseconds, delay);
        }
        public string ControlInputStatus { get => controlInputStatus; private set => this.RaiseAndSetIfChanged(ref controlInputStatus, value); }
        public void RefreshRecording()
        {
            ControlInputStatus = ControlStatus;
            StreamTimingStatus = $"Stream timing: {streamScheduling} · max loop gap {Interlocked.Read(ref maxLoopGapTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1} ms · loops >10 ms {Interlocked.Read(ref slowLoops)} · max IQ catch-up {Interlocked.Read(ref maxBacklogMicroseconds) / 1000.0:F1} ms (since start)";
            AutoRecordingEnabled = AutoRecorder.IsArmed;
            AutoRecordingStatus = AutoRecorder.Status;
            RecordingStatus = AutoRecordingEnabled ? AutoRecorder.Status : Recorder.Status;
            IsRecording = Recorder.IsRecording; TxInputStatus = TxSignal.Status;
            this.RaisePropertyChanged(nameof(CanStartManualRecording));
            this.RaisePropertyChanged(nameof(CanChangeRecordingMode));
            this.RaisePropertyChanged(nameof(AutoRecordButtonText));
            RxTxStatus = !Volatile.Read(ref transmitActive) ? "RX: test signals enabled"
                : MuteRxDuringTx ? "TX: receiver IQ muted · sample clocks continue"
                : "TX: original RX / loopback behavior enabled";
        }
        public virtual void Stop()
        {
            AutoRecorder.Disarm();
            Recorder.Stop();
        }
    }
}
