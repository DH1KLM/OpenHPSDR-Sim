using System;
using System.Diagnostics;

namespace Patroclus.Avalonia.Audio
{
    public sealed class TxSignalMonitor
    {
        private readonly object gate = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private bool transmitting;
        private long packets;
        private double peak;
        private TimeSpan lastPacket;

        public void SetTransmitting(bool value)
        {
            lock (gate)
            {
                if (value && !transmitting) { packets = 0; peak = 0; lastPacket = clock.Elapsed; }
                transmitting = value;
            }
        }
        public string Status
        {
            get
            {
                lock (gate)
                {
                    if (!transmitting) return "TX input: PTT off";
                    if (packets == 0) return "TX input: PTT on · waiting for TX-IQ packets";
                    if ((clock.Elapsed - lastPacket).TotalSeconds > 1)
                        return $"TX input: PTT on · {packets} packets · no recent TX-IQ data";
                    if (peak == 0) return $"TX input: PTT on · {packets} packets · SILENCE from client (all IQ samples zero)";
                    return FormattableString.Invariant($"TX input: PTT on · {packets} packets · peak {20 * Math.Log10(peak):F1} dBFS");
                }
            }
        }
        public void Protocol1(byte[] data, int start, int samples, bool tx)
        {
            SetTransmitting(tx);
            if (!tx || data == null || start < 0 || samples <= 0 || (long)start + samples * 8L > data.Length) return;
            double maximum = 0;
            for (int s = 0; s < samples; s++)
            {
                int p = start + s * 8;
                maximum = Math.Max(maximum, Math.Abs((short)((data[p + 4] << 8) | data[p + 5]) / 32768.0));
                maximum = Math.Max(maximum, Math.Abs((short)((data[p + 6] << 8) | data[p + 7]) / 32768.0));
            }
            Observe(maximum);
        }
        public void Protocol2(byte[] data)
        {
            lock (gate) if (!transmitting) return;
            if (data == null || data.Length != 1444) return;
            double maximum = 0;
            for (int p = 4; p < data.Length; p += 3)
                maximum = Math.Max(maximum, Math.Abs(((data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8)) / 2147483648.0));
            Observe(maximum);
        }
        private void Observe(double maximum)
        {
            lock (gate) { packets++; peak = maximum; lastPacket = clock.Elapsed; }
        }
    }
}
