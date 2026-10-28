using System;
using System.Collections.Generic;
using ReactiveUI;

namespace Patroclus.Avalonia.ViewModels
{
    public sealed class ControlLamp : ViewModelBase
    {
        public string Key { get; }
        public string Label { get; }
        private string color = "#334155", detail = "not received";
        public string Color { get => color; private set => this.RaiseAndSetIfChanged(ref color, value); }
        public string Detail { get => detail; private set => this.RaiseAndSetIfChanged(ref detail, value); }
        private string compactDetail = "?";
        public string CompactDetail { get => compactDetail; private set => this.RaiseAndSetIfChanged(ref compactDetail, value); }
        public string DisplayText => string.IsNullOrEmpty(CompactDetail) ? Label : Label + " · " + CompactDetail;
        public ControlLamp(string key, string label) { Key = key; Label = label; }
        public void Update(ControlReading reading)
        {
            Color = reading.Active ? "#22C55E" : "#334155";
            Detail = reading.Detail;
            CompactDetail = reading.Detail == "ON" || reading.Detail == "OFF" ? "" : reading.Detail;
            this.RaisePropertyChanged(nameof(DisplayText));
        }
    }

    public sealed class ControlGroup
    {
        public string Title { get; }
        public bool IsRaw { get; }
        public List<ControlLamp> Lamps { get; } = new();
        public ControlGroup(string title, bool isRaw = false) { Title = title; IsRaw = isRaw; }
        public ControlGroup Add(string key, string label) { Lamps.Add(new ControlLamp(key, label)); return this; }
    }

    public sealed class OcBandRow
    {
        public string Band { get; }
        public List<ControlLamp> RxPins { get; } = new();
        public List<ControlLamp> TxPins { get; } = new();
        public OcBandRow(string band)
        {
            Band = band;
            for (int i = 0; i < 7; i++)
            {
                RxPins.Add(new ControlLamp($"oc.{band}.rx.{i}", (i + 1).ToString()));
                TxPins.Add(new ControlLamp($"oc.{band}.tx.{i}", (i + 1).ToString()));
            }
        }
    }

    public sealed class ControlMonitor : ViewModelBase
    {
        public RadioControlDecoder Decoder { get; }
        public ClientSession Session { get; } = new();
        public string ProtocolLabel { get; }
        private string clientEndpoint = "No client", clientStatus = "Waiting for a start command", clientColor = "#94A3B8";
        public string ClientEndpoint { get => clientEndpoint; private set => this.RaiseAndSetIfChanged(ref clientEndpoint, value); }
        public string ClientStatus { get => clientStatus; private set => this.RaiseAndSetIfChanged(ref clientStatus, value); }
        public string ClientColor { get => clientColor; private set => this.RaiseAndSetIfChanged(ref clientColor, value); }
        public List<ControlGroup> Groups { get; } = new();
        private long displayedRevision = -1;
        private string activity = "No control data received yet.";
        public string Activity { get => activity; private set => this.RaiseAndSetIfChanged(ref activity, value); }
        public string FilterDescription { get; }
        public ControlGroup HardwareOptions { get; }
        public string HardwareDescription { get; }
        public List<OcBandRow> OcRows { get; } = new();
        public int[] PinNumbers { get; } = { 1, 2, 3, 4, 5, 6, 7 };
        private string sampleRate = "Not received yet", txSampleRate = "Not received yet", ocObservation = "No OC pattern with a known frequency received yet";
        public string SampleRate { get => sampleRate; private set => this.RaiseAndSetIfChanged(ref sampleRate, value); }
        private string receiveFrequencyKhz = "— — —";
        public string ReceiveFrequencyKhz { get => receiveFrequencyKhz; private set => this.RaiseAndSetIfChanged(ref receiveFrequencyKhz, value); }
        public string TxSampleRate { get => txSampleRate; private set => this.RaiseAndSetIfChanged(ref txSampleRate, value); }
        public string OcObservation { get => ocObservation; private set => this.RaiseAndSetIfChanged(ref ocObservation, value); }
        private string ocWire = "OC input: no control data received yet";
        public string OcWire { get => ocWire; private set => this.RaiseAndSetIfChanged(ref ocWire, value); }

        public ControlMonitor(RadioProfile profile)
        {
            ProtocolLabel = "Protocol " + profile.Protocol;
            if (profile.Protocol == 1) TxSampleRate = "Not transmitted separately (P1)";
            bool bpf = profile.BoardName.StartsWith("Orion MkII") || profile.BoardName == "Saturn" || profile.BoardName == "Hermes C10";
            Decoder = new RadioControlDecoder(bpf);
            bool lite = profile.BoardId == 6 && profile.Protocol == 1;
            HardwareOptions = new ControlGroup("Hardware Options");
            HardwareDescription = $"{profile.Name} · {ProtocolLabel} · Received options only; ? means unknown.";
            if (!lite)
            {
                HardwareOptions.Add("micBoost", "Mic Boost").Add("lineIn", "Line In");
                bool orionMic = profile.BoardName == "Orion" || (bpf && profile.Name != "Red Pitaya" && profile.BoardName != "Hermes C10");
                if (orionMic)
                    HardwareOptions.Add("micRing", "Mic").Add("micBias", "Bias").Add("micPtt", "Mic PTT");
                if (profile.BoardName == "Saturn") HardwareOptions.Add("micXlr", "Input");
                HardwareOptions.Add("hardwareAlex", profile.Protocol == 2 ? "Alex Enable" : "Alex (not transmitted)");
                if (profile.Name == "HPSDR Hermes")
                {
                    HardwareOptions.Add("apollo", profile.Protocol == 1 ? "Apollo" : "Apollo (not decoded)");
                    if (profile.Protocol == 1)
                        HardwareOptions.Add("apolloFilter", "Apollo Filter")
                            .Add("apolloTuner", "Apollo Tuner").Add("apolloTune", "Apollo ATU");
                }
            }
            else HardwareDescription += " Hermes Lite uses different hardware fields.";
            foreach (var band in RadioControlDecoder.OcBands) OcRows.Add(new OcBandRow(band));
            FilterDescription = lite ? "Hermes Lite: Alex fields are device-specific; filter states remain unknown."
                : bpf ? "RX: Orion MkII / Saturn BPF mapping. Displays received relay bits."
                : "RX: classic Alex HPF mapping. Displays received relay bits.";
            Groups.Add(new ControlGroup("Operation")
                .Add("run", "RUN").Add("ptt", "PTT / MOX").Add("drive", "TX Drive")
                .Add("paDisabled", "HF PA disabled").Add("manual", "P1 manual filters")
                .Add("pureSignal", "P1 PureSignal").Add("alex0", "P2 Alex 0 enabled"));
            if (profile.HasSecondRxFilterBank) Groups[0].Add("alex1", "P2 Alex 1 enabled");
            string[] lpf = { "30 / 20 m", "60 / 40 m", "80 m", "160 m", "6 m", "12 / 10 m", "17 / 15 m" };
            var lowPass = new ControlGroup("TX / RX low-pass filters (LPF)");
            for (int i = 0; i < lpf.Length; i++) lowPass.Add("lpf" + i, lpf[i]);
            Groups.Add(lowPass);
            string[] rx = bpf ? new[] { "20 / 15 m", "12 / 10 m", "40 / 30 m", "80 / 60 m", "160 m", "Bypass", "6 m / LNA" }
                : new[] { "13 MHz HPF", "20 MHz HPF", "9.5 MHz HPF", "6.5 MHz HPF", "1.5 MHz HPF", "Bypass", "6 m Preamp" };
            for (int bank = 0; bank < (profile.HasSecondRxFilterBank ? 2 : 1); bank++)
            {
                var group = new ControlGroup($"RX filter bank {bank + 1} ({(bpf ? "BPF" : "HPF")})");
                for (int i = 0; i < rx.Length; i++) group.Add("rx" + bank + "." + i, rx[i]);
                Groups.Add(group);
            }
            var attenuation = new ControlGroup("Attenuation / ADC")
                .Add("adc0", "ADC 0 Step ATT").Add("adc1", "ADC 1 Step ATT")
                .Add("att10", bpf ? "RX Master (Bit 14)" : "Alex ATT 10 dB");
            if (!bpf) attenuation.Add("att20", "Alex ATT 20 dB");
            attenuation.Add("preamp0", "Preamp ADC 0").Add("preamp1", "Preamp ADC 1")
                .Add("dither0", "Dither ADC 0").Add("dither1", "Dither ADC 1")
                .Add("random0", "Random ADC 0").Add("random1", "Random ADC 1");
            Groups.Add(attenuation);
            Groups.Add(new ControlGroup("Antennas / routing")
                .Add("ant0", "ANT 1").Add("ant1", "ANT 2").Add("ant2", "ANT 3")
                .Add("ext1", "EXT 1").Add("ext2", "EXT 2").Add("xvtr", "XVTR RX")
                .Add("rxout", "RX OUT"));
            if (profile.HasSecondRxFilterBank) Groups[Groups.Count - 1].Add("rx2gnd", "RX 2 GND");
            var outputs = new ControlGroup("Outputs");
            for (int i = 0; i < 7; i++) outputs.Add("oc" + i, "OC " + (i + 1));
            for (int i = 0; i < 4; i++) outputs.Add("user" + i, "Digital " + (i + 1));
            Groups.Add(outputs);
            Groups.Add(new ControlGroup("Received protocol raw words", true).Add("raw0", "ALEX0 raw data").Add("raw1", "ALEX1 raw data"));
        }

        // Called on the UI thread at 10 Hz; packet handlers only update the decoder.
        public void Refresh()
        {
            // Refresh even without new decoder values so silence becomes visible.
            var connection = Session.Snapshot();
            ClientEndpoint = connection.Endpoint;
            ClientStatus = connection.Status;
            ClientColor = connection.Active ? "#22C55E" : "#94A3B8";
            var snapshot = Decoder.Snapshot();
            if (snapshot.Revision == displayedRevision) return;
            displayedRevision = snapshot.Revision;
            ReceiveFrequencyKhz = snapshot.RxFrequencyHz.HasValue
                ? (Math.Round(snapshot.RxFrequencyHz.Value, MidpointRounding.AwayFromZero) / 1000.0)
                    .ToString("#,##0.000", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ')
                : "— — —";
            foreach (var group in Groups)
                foreach (var lamp in group.Lamps)
                    if (snapshot.Values.TryGetValue(lamp.Key, out var reading)) lamp.Update(reading);
            foreach (var lamp in HardwareOptions.Lamps)
                if (snapshot.Values.TryGetValue(lamp.Key, out var reading)) lamp.Update(reading);
            foreach (var row in OcRows)
            {
                foreach (var lamp in row.RxPins)
                    if (snapshot.Values.TryGetValue(lamp.Key, out var reading)) lamp.Update(reading);
                foreach (var lamp in row.TxPins)
                    if (snapshot.Values.TryGetValue(lamp.Key, out var reading)) lamp.Update(reading);
            }
            if (snapshot.Values.TryGetValue("sampleRate", out var rate)) SampleRate = rate.Detail;
            if (snapshot.Values.TryGetValue("txSampleRate", out var txRate)) TxSampleRate = txRate.Detail;
            if (snapshot.Values.TryGetValue("ocObservation", out var oc)) OcObservation = oc.Detail;
            if (snapshot.Values.TryGetValue("ocWire", out var wire)) OcWire = wire.Detail;
            if (snapshot.Revision > 0)
                Activity = $"Last control data: {snapshot.Received.ToLocalTime():HH:mm:ss.fff} · Update {snapshot.Revision}";
        }
    }
}

