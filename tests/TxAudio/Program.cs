using System;
using System.IO;
using System.Linq;
using Patroclus.Avalonia.Audio;

var directory = Path.Combine(Path.GetTempPath(), "openhpsdr-tx-audio-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
void Verify(string path, int expectedSamples, bool tone)
{
    using var reader = new BinaryReader(File.OpenRead(path));
    Check(new string(reader.ReadChars(4)) == "RIFF", "RIFF header");
    Check(reader.ReadUInt32() == reader.BaseStream.Length - 8, "Final RIFF size");
    Check(new string(reader.ReadChars(8)) == "WAVEfmt ", "PCM format header");
    Check(reader.ReadUInt32() == 16 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 1, "PCM mono");
    Check(reader.ReadUInt32() == 48000 && reader.ReadUInt32() == 96000, "Output sample rate / byte rate");
    Check(reader.ReadUInt16() == 2 && reader.ReadUInt16() == 16, "16 bit block size");
    Check(new string(reader.ReadChars(4)) == "data" && reader.ReadUInt32() == expectedSamples * 2, "Correct duration / finalized data size");
    double re = 0, im = 0, mean = 0;
    for (int i = 0; i < expectedSamples; i++)
    {
        double value = reader.ReadInt16() / 32768.0;
        re += value * Math.Cos(2 * Math.PI * 1000 * i / 48000);
        im += value * Math.Sin(2 * Math.PI * 1000 * i / 48000);
        mean += value;
    }
    if (tone)
    {
        double amplitude = 2 * Math.Sqrt(re * re + im * im) / expectedSamples;
        Check(amplitude > 0.12 && amplitude < 0.32, "Demodulated 1 kHz audio amplitude: " + amplitude);
        Check(Math.Abs(mean / expectedSamples) < 0.01, "AM carrier / DC rejected");
    }
}
try
{
    var signal = new TxSignalMonitor();
    Check(signal.Status.Contains("PTT off"), "Initial TX diagnostic state");
    signal.SetTransmitting(true);
    Check(signal.Status.Contains("waiting"), "PTT without IQ packets");
    var iq = new byte[1444]; signal.Protocol2(iq);
    Check(signal.Status.Contains("SILENCE"), "Client zeros distinguished from missing data");
    iq[4] = 0x40; signal.Protocol2(iq);
    Check(signal.Status.Contains("peak -6.0 dBFS"), "P2 TX sample level decoding");
    signal.SetTransmitting(false); signal.SetTransmitting(true);
    Check(signal.Status.Contains("waiting"), "New transmission resets counters");
    var p1Level = new byte[8]; p1Level[4] = 0x40;
    signal.Protocol1(p1Level, 0, 1, true);
    Check(signal.Status.Contains("peak -6.0 dBFS"), "P1 TX sample level decoding");
    var recorder = new TxAudioRecorder();
    int completed = 0;
    bool completedFilesReady = true;
    void Completed(string saved)
    {
        try
        {
            using var file = new FileStream(saved, FileMode.Open, FileAccess.Read, FileShare.None);
            using var reader = new BinaryReader(file);
            completedFilesReady &= new string(reader.ReadChars(4)) == "RIFF" && reader.ReadUInt32() == file.Length - 8;
        }
        catch { completedFilesReady = false; }
        completed++;
    }
    recorder.RecordingCompleted += Completed;
    recorder.Stop();
    Check(completed == 0, "Stopping before recording must not report a completed file");
    string path = Path.Combine(directory, "p1.wav");
    recorder.Start(path, TxAudioMode.SSB);
    for (int packet = 0; packet < 800; packet++)
    {
        var data = new byte[63 * 8];
        for (int s = 0; s < 63; s++)
        {
            double phase = 2 * Math.PI * 1000 * (packet * 63 + s) / 48000;
            short i = (short)(Math.Cos(phase) * 0.2 * 32767), q = (short)(Math.Sin(phase) * 0.2 * 32767);
            int p = s * 8; data[p] = 0x7f; data[p + 1] = 0xff; // unrelated speaker audio must be ignored
            data[p + 4] = (byte)(i >> 8); data[p + 5] = (byte)i;
            data[p + 6] = (byte)(q >> 8); data[p + 7] = (byte)q;
        }
        recorder.Protocol1(data, 0, 63, true);
        // Feed in bounded bursts; this test checks audio, not deliberate queue overflow.
        if (packet % 64 == 63) System.Threading.Thread.Sleep(10);
    }
    recorder.Stop(); Verify(path, 50400, true);
    foreach (var mode in new[] { TxAudioMode.SSB, TxAudioMode.USB, TxAudioMode.LSB, TxAudioMode.AM, TxAudioMode.FM })
    {
        path = Path.Combine(directory, mode + ".wav"); recorder.Start(path, mode);
        double fmPhase = 0;
        for (int packet = 0; packet < 800; packet++)
        {
            var data = new byte[1444];
            for (int s = 0; s < 240; s++)
            {
                double tone = Math.Sin(2 * Math.PI * 1000 * (packet * 240 + s) / 192000);
                double phase = 2 * Math.PI * 1000 * (packet * 240 + s) / 192000;
                fmPhase += 2 * Math.PI * 5000 * 0.2 * tone / 192000;
                double i = mode == TxAudioMode.AM ? 0.5 + 0.2 * tone : 0.2 * Math.Cos(mode == TxAudioMode.FM ? fmPhase : phase);
                double q = mode == TxAudioMode.AM ? 0 : 0.2 * Math.Sin(mode == TxAudioMode.FM ? fmPhase : phase);
                int p = 4 + s * 6;
                int iv = (int)(i * 8388607), qv = (int)(q * 8388607);
                data[p] = (byte)(iv >> 16); data[p + 1] = (byte)(iv >> 8); data[p + 2] = (byte)iv;
                data[p + 3] = (byte)(qv >> 16); data[p + 4] = (byte)(qv >> 8); data[p + 5] = (byte)qv;
            }
            recorder.Protocol2(data, 192000, true);
            if (packet % 64 == 63) System.Threading.Thread.Sleep(10);
        }
        recorder.Stop(); Verify(path, 48000, true);
    }
    path = Path.Combine(directory, "no-ptt.wav"); recorder.Start(path, TxAudioMode.SSB);
    recorder.Protocol2(new byte[1444], 192000, false);
    recorder.Protocol1(new byte[504], 0, 63, false);
    recorder.Protocol2(new byte[2], 192000, true);
    recorder.Stop(); recorder.Stop(); Verify(path, 0, false);
    Check(!recorder.IsRecording && !recorder.Status.StartsWith("Recording error"), "Final recorder state");
    Check(completed == 7 && completedFilesReady, "Each manual WAV must notify once, after finalization and exclusive file access is possible");
    string autoFolder = Path.Combine(directory, "auto"); Directory.CreateDirectory(autoFolder);
    var automatic = new AutoTxAudioRecorder();
    automatic.RecordingCompleted += Completed;
    try
    {
        Check(!automatic.IsArmed, "Automatic recording must default to OFF");
        automatic.SetTransmitting(true); automatic.Protocol2(new byte[1444], 192000, true);
        Check(Directory.GetFiles(autoFolder).Length == 0, "Disabled auto recording must never create files");
        automatic.Arm(autoFolder, TxAudioMode.USB, false);
        automatic.SetTransmitting(false); automatic.Protocol1(new byte[504], 0, 63, false);
        automatic.SetTransmitting(true); automatic.SetTransmitting(true);
        automatic.Protocol1(new byte[504], 0, 63, true);
        automatic.SetTransmitting(false); automatic.SetTransmitting(false);
        automatic.SetTransmitting(true);
        automatic.Protocol1(new byte[504], 0, 63, true);
        automatic.Protocol1(new byte[504], 0, 63, true);
        automatic.SetTransmitting(false); automatic.Disarm();
        var files = Directory.GetFiles(autoFolder).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Check(files.Length == 2, "Each PTT burst must create exactly one file despite repeated PTT packets");
        Check(files.All(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),
            @"^TX_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}_USB(?:_[0-9a-f]+)?\.wav$")), "Filename must include recording date, time and mode");
        Verify(files[0], 63, false); Verify(files[1], 126, false);
        automatic.Arm(autoFolder, TxAudioMode.LSB, true); // arming during an existing transmission
        automatic.Protocol2(new byte[1444], 192000, true); automatic.Disarm();
        Verify(Directory.GetFiles(autoFolder).Single(p => p.Contains("_LSB")), 60, false);
        Check(!automatic.IsArmed, "Disarm must finalize active transmission and stay off");
        automatic.Disarm();
        Check(completed == 10 && completedFilesReady, "Each automatic WAV must notify once after closing; repeated disarm must not notify again");
    }
    finally
    {
        automatic.Disarm();
        foreach (string file in Directory.GetFiles(autoFolder)) File.Delete(file);
        Directory.Delete(autoFolder);
    }
    Console.WriteLine("PASS: auto recording defaults OFF; PTT start/stop, distinct files, repeated controls, arm during TX, finalization and date/time/mode filenames.");
    Console.WriteLine("PASS: P1/P2 TX IQ -> playable 48 kHz mono WAV; SSB/AM/FM 1 kHz tone, duration, DC, PTT gating, header finalization and restart.");
}
finally
{
    foreach (string path in Directory.GetFiles(directory)) File.Delete(path);
    Directory.Delete(directory);
}
