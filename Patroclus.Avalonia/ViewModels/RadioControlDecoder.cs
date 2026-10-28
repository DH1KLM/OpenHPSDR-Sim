using System;
using System.Collections.Generic;

namespace Patroclus.Avalonia.ViewModels
{
    // Passive observation only: none of these values feed back into the IQ engine.
    public sealed record ControlReading(bool Active, string Detail);

    public sealed class RadioControlDecoder
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, ControlReading> readings = new();
        private readonly bool bpf;
        private long revision;
        private DateTime lastReceived;
        private double? rxFrequency, txFrequency;
        private bool? phaseWords;
        private int? firstDdc;
        public static readonly string[] OcBands = { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", "2m", "GEN" };

        public RadioControlDecoder(bool bandPassFilters) { bpf = bandPassFilters; }

        public (long Revision, DateTime Received, Dictionary<string, ControlReading> Values) Snapshot()
        {
            lock (gate) return (revision, lastReceived, new Dictionary<string, ControlReading>(readings));
        }

        private void Set(string key, bool active, string detail = null)
            => readings[key] = new ControlReading(active, detail ?? (active ? "ON" : "OFF"));
        private void Bits(string prefix, uint value, int count)
        {
            for (int i = 0; i < count; i++) Set(prefix + i, (value & (1u << i)) != 0);
        }
        private void Att(string key, byte value, bool enabled)
            => Set(key, enabled, $"{value & 31} dB / {(enabled ? "enabled" : "disabled")}");
        private void Received() { revision++; lastReceived = DateTime.UtcNow; }
        private void RxFilters(int bank, byte mask)
        {
            Bits("rx" + bank + ".", (uint)(mask & 127), 7);
        }

        public void Protocol1(byte c0, byte c1, byte c2, byte c3, byte c4, bool hermesLite = false)
        {
            lock (gate)
            {
                Set("ptt", (c0 & 1) != 0);
                int address = c0 & 0xfe;
                uint frequency = ((uint)c1 << 24) | ((uint)c2 << 16) | ((uint)c3 << 8) | c4;
                if (address == 2) txFrequency = frequency;
                if (address == 4) rxFrequency = frequency;
                if (address == 0) Set("sampleRate", true, (48 << (c1 & 3)) + " kS/s (P1)");
                // Hermes Lite reuses control fields; do not label these as Alex relays.
                if (!hermesLite)
                switch (c0 & 0xfe)
                {
                    case 0:
                        Set("att10", (c3 & 1) != 0);
                        if (!bpf) Set("att20", (c3 & 2) != 0);
                        Set("preamp0", (c3 & 4) != 0);
                        Set("dither0", (c3 & 8) != 0);
                        Set("random0", (c3 & 16) != 0);
                        int input = (c3 >> 5) & 3;
                        Set("ext1", input == 2); Set("ext2", input == 1); Set("xvtr", input == 3);
                        Set("rxout", (c3 & 128) != 0);
                        for (int i = 0; i < 3; i++) Set("ant" + i, (c4 & 3) == i);
                        Bits("oc", (uint)(c2 >> 1), 7);
                        Set("ocWire", (c2 >> 1) != 0, $"P1: C2=0x{c2:X2} → OC=0x{c2 >> 1:X2} · {((c0 & 1) != 0 ? "TX" : "RX")}");
                        ObserveOc((byte)(c2 >> 1), (c0 & 1) != 0, (c0 & 1) != 0 ? txFrequency : rxFrequency);
                        break;
                    case 0x12:
                        Set("drive", c1 != 0, c1 + " / 255");
                        Set("manual", (c2 & 64) != 0);
                        Set("micBoost", (c2 & 1) != 0);
                        Set("lineIn", (c2 & 2) != 0);
                        Set("apolloFilter", (c2 & 4) != 0);
                        Set("apolloTuner", (c2 & 8) != 0);
                        Set("apolloTune", (c2 & 16) != 0);
                        Set("apollo", (c2 & 32) != 0);
                        Set("paDisabled", (c3 & 128) != 0);
                        RxFilters(0, c3); Bits("lpf", c4, 7);
                        Set("raw0", true, $"C0=12 C1={c1:X2} C2={c2:X2} C3={c3:X2} C4={c4:X2}");
                        break;
                    case 0x14:
                        Att("adc0", c4, (c4 & 32) != 0);
                        Set("pureSignal", (c2 & 64) != 0);
                        Set("preamp0", (c1 & 1) != 0); Set("preamp1", (c1 & 2) != 0);
                        Bits("user", (uint)(c3 & 15), 4);
                        MicOptions((c1 & 16) != 0, (c1 & 32) != 0, (c1 & 64) == 0);
                        break;
                    case 0x16:
                        Att("adc1", c1, (c1 & 32) != 0);
                        break;
                    case 0x24:
                        RxFilters(1, c1); Set("rx2gnd", (c1 & 128) != 0);
                        Set("raw1", true, $"C0=24 C1={c1:X2} C2={c2:X2} C3={c3:X2} C4={c4:X2}");
                        break;
                }
                Received();
            }
        }

        public void Protocol1Run(bool running)
        {
            lock (gate) { Set("run", running); if (!running) Set("ptt", false); Received(); }
        }

        public bool Protocol2General(byte[] data)
        {
            if (data == null || data.Length != 60 || data[4] != 0
                || (data[0] == 0xef && data[1] == 0xfe)) return false;
            lock (gate)
            {
                // P2 General carries PA enable (Thetis sends !tx.pa), unlike
                // P1 bank 0x12 C3 bit 7 which carries the disable flag directly.
                Set("paDisabled", (data[58] & 1) == 0);
                phaseWords = (data[37] & 8) != 0;
                Set("hardwareAlex", (data[59] & 3) != 0);
                Set("alex0", (data[59] & 1) != 0); Set("alex1", (data[59] & 2) != 0);
                Received(); return true;
            }
        }

        public bool Protocol2Rx(byte[] data)
        {
            if (data == null || data.Length < 7) return false;
            lock (gate)
            {
                Set("dither0", (data[5] & 1) != 0); Set("dither1", (data[5] & 2) != 0);
                Set("random0", (data[6] & 1) != 0); Set("random1", (data[6] & 2) != 0);
                if (data.Length >= 1444)
                {
                    firstDdc = null;
                    var rates = new List<string>();
                    for (int i = 0; i < 80; i++)
                    {
                        if ((data[7 + i / 8] & (1 << (i % 8))) == 0) continue;
                        firstDdc ??= i;
                        int rate = (data[18 + i * 6] << 8) | data[19 + i * 6];
                        rates.Add($"DDC{i}: {rate} kS/s");
                    }
                    Set("sampleRate", rates.Count > 0, rates.Count > 0 ? string.Join(" · ", rates) : "No DDCs enabled");
                }
                Received(); return true;
            }
        }

        public bool Protocol2Tx(byte[] data)
        {
            if (data == null || data.Length < 60) return false;
            lock (gate)
            {
                byte mic = data[50];
                Set("lineIn", (mic & 1) != 0); Set("micBoost", (mic & 2) != 0);
                MicOptions((mic & 8) != 0, (mic & 16) != 0, (mic & 4) == 0);
                Set("micXlr", (mic & 32) != 0, (mic & 32) != 0 ? "XLR" : "3.5 mm");
                int rate = (data[14] << 8) | data[15];
                Set("txSampleRate", rate > 0, rate + " kS/s");
                Received(); return true;
            }
        }

        private void MicOptions(bool ring, bool bias, bool pttEnabled)
        {
            Set("micRing", ring, ring ? "Ring" : "Tip");
            Set("micBias", bias); Set("micPtt", pttEnabled);
        }

        private void ObserveOc(byte mask, bool tx, double? hz)
        {
            if (!hz.HasValue || hz <= 0) return;
            string band = BandForFrequency(hz.Value);
            string mode = tx ? "tx" : "rx";
            string detail = FormattableString.Invariant($"{band} {(tx ? "TX" : "RX")} · Hardware frequency {hz.Value / 1000000:F4} MHz · Pins 0x{mask & 127:X2}");
            Set("ocObservation", true, detail);
            for (int pin = 0; pin < 7; pin++)
                Set($"oc.{band}.{mode}.{pin}", (mask & (1 << pin)) != 0,
                    $"Pin {pin + 1}: {((mask & (1 << pin)) != 0 ? "ON" : "OFF")} · {detail}");
        }

        public static string BandForFrequency(double hz)
        {
            (double Low, double High)[] limits = {
                (1.8, 2.0), (3.5, 4.0), (5.0, 5.5), (7.0, 7.3), (10.1, 10.15),
                (14, 14.35), (18.068, 18.168), (21, 21.45), (24.89, 24.99), (28, 29.7), (50, 54), (144, 148)
            };
            for (int i = 0; i < limits.Length; i++)
                if (hz >= limits[i].Low * 1000000 - 1 && hz <= limits[i].High * 1000000 + 1) return OcBands[i];
            return "GEN";
        }

        public bool Protocol2HighPriority(byte[] data)
        {
            if (data == null || data.Length < 1444) return false;
            lock (gate)
            {
                Set("run", (data[4] & 1) != 0); Set("ptt", (data[4] & 2) != 0);
                Set("drive", data[345] != 0, data[345] + " / 255");
                uint alex0 = Read32(data, 1432), alex1 = Read32(data, 1428);
                AlexRx(0, alex0); AlexRx(1, alex1);
                uint lpf = ((alex0 >> 20) & 15) | ((alex0 >> 25) & 112);
                Bits("lpf", lpf, 7);
                Set("att10", (alex0 & (1u << 14)) != 0);
                if (!bpf) Set("att20", (alex0 & (1u << 13)) != 0);
                Set("ext1", (alex0 & (1u << 9)) != 0);
                Set("ext2", (alex0 & (1u << 10)) != 0);
                Set("xvtr", (alex0 & (1u << 8)) != 0);
                Set("rxout", (alex0 & (1u << 11)) != 0);
                for (int i = 0; i < 3; i++) Set("ant" + i, (alex0 & (1u << (24 + i))) != 0);
                Set("rx2gnd", (alex1 & (1u << 8)) != 0);
                Att("adc0", data[1443], true); Att("adc1", data[1442], true);
                Set("preamp0", (data[1403] & 1) != 0); Set("preamp1", (data[1403] & 2) != 0);
                Bits("oc", (uint)(data[1401] >> 1), 7); Bits("user", (uint)(data[1402] & 15), 4);
                Set("ocWire", (data[1401] >> 1) != 0, $"P2: Byte 1401=0x{data[1401]:X2} → OC=0x{data[1401] >> 1:X2} · {((data[4] & 2) != 0 ? "TX" : "RX")}");
                double? FrequencyAt(int offset) => !phaseWords.HasValue ? null : phaseWords.Value ? Read32(data, offset) * (122880000.0 / 4294967296.0) : Read32(data, offset);
                txFrequency = FrequencyAt(329);
                rxFrequency = firstDdc.HasValue ? FrequencyAt(9 + firstDdc.Value * 4) : null;
                bool tx = (data[4] & 2) != 0;
                ObserveOc((byte)(data[1401] >> 1), tx, tx ? txFrequency : rxFrequency);
                Set("raw0", true, $"ALEX0 0x{alex0:X8}"); Set("raw1", true, $"ALEX1 0x{alex1:X8}");
                Received(); return true;
            }
        }

        private void AlexRx(int bank, uint word)
        {
            uint mask = ((word >> 1) & 3) | ((word >> 2) & 28) |
                        ((word >> 7) & 32) | ((word << 3) & 64);
            RxFilters(bank, (byte)mask);
        }
        private static uint Read32(byte[] d, int offset)
            => ((uint)d[offset] << 24) | ((uint)d[offset + 1] << 16) | ((uint)d[offset + 2] << 8) | d[offset + 3];
    }
}

