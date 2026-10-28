using System;
using System.Net;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Net.Sockets;
using Patroclus.Avalonia.Audio;
using Patroclus.Avalonia.ViewModels;

var radio = new FakeHermesNewProtocol();
// A reused mixer must start empty, including after its size changes.
var mixer = new receiver("buffer reuse"); mixer.generators[0].SetDefaults(1000);
var referenceMix = new byte[238 * 6];
mixer.GenerateSignal(referenceMix, 0, 6, 238, 0, 1.0 / 48000);
for (int pass = 0; pass < 4; pass++)
{
    var small = new byte[30 * 6]; mixer.GenerateSignal(small, 0, 6, 30, 0, 1.0 / 48000);
    var repeated = new byte[238 * 6]; mixer.GenerateSignal(repeated, 0, 6, 238, 0, 1.0 / 48000);
    if (!referenceMix.SequenceEqual(repeated) || !small.SequenceEqual(referenceMix.Take(small.Length)))
        throw new Exception("Reused IQ mixer retained stale samples or changed amplitude");
}
Console.WriteLine("PASS: reused IQ mixing buffer preserves signal across repeated and resized blocks.");
var general = new byte[60];
var endpoint = new IPEndPoint(IPAddress.Loopback, 50000);
radio.handleGeneralPacket(new receivedPacket { received = general, endPoint = endpoint });
if (radio.Rx0Port != 1035 || radio.RxSpecificPort != 1025
    || radio.TxSpecificPort != 1026 || radio.HighPriorityFromPCPort != 1027)
    throw new Exception("Zero entries must select default P2 ports.");
general[17] = 0x2e; general[18] = 0xe0;
radio.handleGeneralPacket(new receivedPacket { received = general, endPoint = endpoint });
if (radio.Rx0Port != 12000) throw new Exception("Explicit IQ port must survive.");
general[17] = 0; general[18] = 0;
radio.handleGeneralPacket(new receivedPacket { received = general, endPoint = endpoint });
if (radio.Rx0Port != 1035) throw new Exception("A following default request must reset the IQ port.");
Console.WriteLine("PASS: production General handler selects defaults, preserves explicit IQ port and resets to defaults.");

// Verify passive monitoring is wired to the actual handlers, without opening sockets.
general[58] = 1; general[59] = 3;
radio.handleGeneralPacket(new receivedPacket { received = general, endPoint = endpoint });
radio.Monitor.Refresh();
if (radio.Monitor.Decoder.Snapshot().Values["paDisabled"].Active)
    throw new Exception("General handler did not update monitor.");
var legacyProbe = new byte[63]; legacyProbe[0] = 0xef; legacyProbe[1] = 0xfe; legacyProbe[2] = 2;
var streamPeerField = typeof(FakeHermesNewProtocol).GetField("ClientIpEndPoint", BindingFlags.NonPublic | BindingFlags.Instance);
foreach (var probe in new[] { legacyProbe, legacyProbe.Take(60).ToArray(), new byte[1444] })
{
    radio.handleGeneralPacket(new receivedPacket { received = probe, endPoint = new IPEndPoint(IPAddress.Loopback, 50001) });
    if (!endpoint.Equals(streamPeerField.GetValue(radio))) throw new Exception("Foreign/malformed General packet replaced stream peer");
    var state = radio.Monitor.Decoder.Snapshot().Values;
    if (state["paDisabled"].Active || !state["alex0"].Active) throw new Exception("P1 discovery/malformed packet changed PA/Alex state");
    if (radio.Monitor.Decoder.Protocol2General(probe)) throw new Exception("Passive decoder accepted foreign/malformed General packet");
}
Console.WriteLine("PASS: P1 discovery and malformed packets cannot change P2 stream endpoint or PA/Alex state.");
var hp = new byte[1444];
hp[1432] = 0x80; // LPF 17/15m: Alex0 bit 31
hp[1435] = 0x02; // HPF13: Alex0 bit 1
hp[1443] = 17;
typeof(FakeHermesNewProtocol).GetMethod("handleHighPriorityPacket", BindingFlags.NonPublic | BindingFlags.Instance)
    .Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
radio.Monitor.Refresh();
var values = radio.Monitor.Decoder.Snapshot().Values;
if (!values["lpf6"].Active || !values["rx0.0"].Active || !values["adc0"].Detail.StartsWith("17 dB"))
    throw new Exception("P2 High Priority handler did not update filter/ATT monitor.");
var p1 = new FakeHermes();
p1.handleCommandControl(0x12, 87, 0x40, 0x10, 4);
p1.Monitor.Refresh();
values = p1.Monitor.Decoder.Snapshot().Values;
if (!values["lpf2"].Active || !values["rx0.4"].Active)
    throw new Exception("P1 C&C handler did not update monitor.");
var fresh = new ControlMonitor(RadioProfile.All[0]);
fresh.Refresh();
if (fresh.Groups[0].Lamps[0].Detail != "not received")
    throw new Exception("New profile must start unknown.");
Console.WriteLine("PASS: production P1/P2 handlers feed filter/ATT monitor; fresh profile starts unknown.");

hp[4] = 1;
var highPriority = typeof(FakeHermesNewProtocol).GetMethod("handleHighPriorityPacket", BindingFlags.NonPublic | BindingFlags.Instance);
highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
radio.Monitor.Refresh();
if (radio.Monitor.ClientEndpoint != "127.0.0.1:50000" || radio.Monitor.ClientStatus != "Active")
    throw new Exception("P2 RUN did not identify active client.");
hp[4] = 0;
highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
radio.Monitor.Refresh();
if (!radio.Monitor.ClientStatus.StartsWith("Stopped")) throw new Exception("P2 STOP did not release active client.");
var start = new receivedPacket { received = new byte[] { 0xef, 0xfe, 4, 1 }, endPoint = endpoint };
p1.handlePacket(start);
p1.Monitor.Refresh();
if (p1.Monitor.ClientEndpoint != "127.0.0.1:50000" || p1.Monitor.ClientStatus != "Active")
    throw new Exception("P1 START did not identify active client.");
start.received[3] = 0; p1.handlePacket(start); p1.Monitor.Refresh();
if (!p1.Monitor.ClientStatus.StartsWith("Stopped")) throw new Exception("P1 STOP did not release active client.");
Console.WriteLine("PASS: production P1/P2 start and stop update client endpoint/status.");

var txOptions = new byte[60]; txOptions[50] = 0x38; txOptions[15] = 192;
typeof(FakeHermesNewProtocol).GetMethod("handleTxSpecificPacket", BindingFlags.NonPublic | BindingFlags.Instance)
    .Invoke(radio, new object[] { new receivedPacket { received = txOptions, endPoint = endpoint } });
radio.Monitor.Refresh();
if (radio.Monitor.TxSampleRate != "192 kS/s" || !radio.Monitor.Decoder.Snapshot().Values["micXlr"].Active)
    throw new Exception("P2 TX-specific handler did not feed Hardware Options / TX sample rate.");
var saturnProfile = RadioProfile.All.First(p => p.BoardName == "Saturn");
var saturn = new ControlMonitor(saturnProfile);
foreach (var profile in RadioProfile.All)
{
    bool expectedBank2 = profile.Name.StartsWith("ANAN-7000DLE") || profile.Name.StartsWith("ANAN-8000DLE")
        || new[] { "ANAN-G2", "ANAN-G2 Ultra", "ANAN-G2-1K" }.Contains(profile.Name);
    var modelMonitor = new ControlMonitor(profile);
    bool bank2Visible = modelMonitor.Groups.SelectMany(g => g.Lamps).Any(l => l.Key == "rx1.0");
    if (profile.HasSecondRxFilterBank != expectedBank2 || bank2Visible != expectedBank2
        || modelMonitor.Groups.SelectMany(g => g.Lamps).Any(l => l.Key == "rx2gnd") != expectedBank2)
        throw new Exception(profile + ": incorrect second physical RX filter bank.");
}
if (!saturn.HardwareOptions.Lamps.Any(l => l.Key == "micXlr") || !saturn.HardwareOptions.Lamps.Any(l => l.Key == "micRing"))
    throw new Exception("Saturn must show balanced input and microphone routing.");
if (fresh.HardwareOptions.Lamps.Any(l => l.Key == "micXlr" || l.Key == "micRing"))
    throw new Exception("Hermes must not claim Saturn/Orion microphone switches.");
var liteProfile = RadioProfile.All.First(p => p.BoardId == 6);
if (new ControlMonitor(liteProfile).HardwareOptions.Lamps.Count != 0)
    throw new Exception("Hermes Lite must not claim unsupported standard microphone fields.");
radio.Rx0Port = 0; // DDC0 uses an ephemeral test socket, never the live emulator's ports.
var rateAndDdc = new byte[1444]; rateAndDdc[7] = 1; rateAndDdc[19] = 192;
typeof(FakeHermesNewProtocol).GetMethod("handleRxSpecificPacket", BindingFlags.NonPublic | BindingFlags.Instance)
    .Invoke(radio, new object[] { new receivedPacket { received = rateAndDdc, endPoint = endpoint } });
radio.Monitor.Refresh();
if (radio.Monitor.SampleRate != "DDC0: 192 kS/s") throw new Exception("RX-specific sample rate monitor not wired.");
// Release the DDC socket opened by the production handler.
rateAndDdc[7] = 0;
typeof(FakeHermesNewProtocol).GetMethod("handleRxSpecificPacket", BindingFlags.NonPublic | BindingFlags.Instance)
    .Invoke(radio, new object[] { new receivedPacket { received = rateAndDdc, endPoint = endpoint } });
Console.WriteLine("PASS: TX/RX-specific handlers feed Hardware Options and sample rates; model-specific controls verified.");

// Screenshot regression: all OC LEDs start dark, and only transmitted bits light up.
var ocDisplay = new ControlMonitor(RadioProfile.All[0]);
if (ocDisplay.OcRows.SelectMany(row => row.RxPins.Concat(row.TxPins)).Any(pin => pin.Color != "#334155"))
    throw new Exception("Every OC LED must start dark.");
ocDisplay.Decoder.Protocol1(4, 0x00, 0x37, 0xc5, 0x58); // 3,655,000 Hz hardware frequency
ocDisplay.Decoder.Protocol1(0, 2, 0, 0, 0);
ocDisplay.Refresh();
var eighty = ocDisplay.OcRows.First(row => row.Band == "80m");
if (eighty.RxPins.Concat(eighty.TxPins).Any(pin => pin.Color != "#334155")
    || eighty.TxPins.Any(pin => pin.Detail != "not received"))
    throw new Exception("Known OFF and unobserved OC pins must both stay dark, with unknown retained in tooltip.");
ocDisplay.Decoder.Protocol1(0, 2, 0x82, 0, 0); ocDisplay.Refresh();
if (eighty.RxPins[0].Color != "#22C55E" || eighty.RxPins[6].Color != "#22C55E" || eighty.RxPins[1].Color != "#334155")
    throw new Exception("OC pin LEDs do not match mask.");
p1.handleCommandControl(4, 0x00, 0x36, 0xee, 0x80); // 3.6 MHz
p1.handleCommandControl(0, 2, 0xfe, 0, 0); p1.Monitor.Refresh();
if (p1.Monitor.OcRows.First(row => row.Band == "80m").RxPins.Any(pin => pin.Color != "#22C55E")
    || !p1.Monitor.OcWire.Contains("OC=0x7F"))
    throw new Exception("P1 production handler must show all seven OC pins for C2=0xFE.");
general[37] = 0;
radio.handleGeneralPacket(new receivedPacket { received = general, endPoint = endpoint });
rateAndDdc[7] = 1;
radio.Monitor.Decoder.Protocol2Rx(rateAndDdc);
hp[4] = 1; hp[9] = 0x00; hp[10] = 0x36; hp[11] = 0xee; hp[12] = 0x80;
hp[1401] = 0xfe;
highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
radio.Monitor.Refresh();
if (radio.Monitor.OcRows.First(row => row.Band == "80m").RxPins.Any(pin => pin.Color != "#22C55E")
    || !radio.Monitor.OcWire.Contains("OC=0x7F"))
    throw new Exception("P2 production handler must show all seven OC pins for byte 1401=0xFE.");
hp[1401] = 0;
highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
radio.Monitor.Refresh();
if (radio.Monitor.OcRows.First(row => row.Band == "80m").RxPins.Any(pin => pin.Color != "#334155"))
    throw new Exception("Clearing transmitted OC mask must extinguish every pin.");
Console.WriteLine("PASS: all OC LEDs start dark; P1/P2 production handlers light pins 1-7 for mask 0x7F and clear them for zero.");

string txCapture = Path.Combine(Path.GetTempPath(), "openhpsdr-handler-" + Guid.NewGuid().ToString("N") + ".wav");
try
{
    p1.Recorder.Start(txCapture, TxAudioMode.SSB);
    var txFrame = new byte[1032]; txFrame[11] = 1;
    p1.handleTXIQandAudio(txFrame, 16, 63); p1.Recorder.Stop();
    if (new FileInfo(txCapture).Length != 44 + 63 * 2) throw new Exception("P1 TX handler did not record IQ payload.");
    hp[4] = 3;
    highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
    radio.Recorder.Start(txCapture, TxAudioMode.SSB);
    typeof(FakeHermesNewProtocol).GetMethod("handleTxIQPacket", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(radio, new object[] { new receivedPacket { received = new byte[1444], endPoint = endpoint }, 0 });
    radio.Recorder.Stop();
    if (new FileInfo(txCapture).Length != 44 + 60 * 2) throw new Exception("P2 TX handler did not record 192 kHz IQ at 48 kHz audio.");
    Console.WriteLine("PASS: actual P1/P2 TX-IQ handlers feed WAV recording with PTT gating and correct sample counts.");
}
finally { p1.Recorder.Stop(); radio.Recorder.Stop(); if (File.Exists(txCapture)) File.Delete(txCapture); }

using (var microphoneSink = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
using (var microphoneSource = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    typeof(FakeHermesNewProtocol).GetField("txSpecificClient", flags)
        .SetValue(radio, new udpConnection { Client = microphoneSource });
    typeof(FakeHermesNewProtocol).GetField("ClientIpEndPoint", flags)
        .SetValue(radio, microphoneSink.Client.LocalEndPoint);
    typeof(FakeHermesNewProtocol).GetField("micSeqNo", flags).SetValue(radio, 0u);
    typeof(FakeHermesNewProtocol).GetMethod("sendMicData", flags).Invoke(radio, null);
    microphoneSink.Client.ReceiveTimeout = 2000;
    var microphonePeer = new IPEndPoint(IPAddress.Any, 0);
    var onWire = microphoneSink.Receive(ref microphonePeer);
    if (onWire.Length != 132 || onWire.Take(4).Any(b => b != 0)
        || onWire.Skip(4).Any(b => b != 0))
        throw new Exception("Production P2 microphone sender emitted an invalid datagram.");
    typeof(FakeHermesNewProtocol).GetMethod("sendMicData", flags).Invoke(radio, null);
    onWire = microphoneSink.Receive(ref microphonePeer);
    if (onWire.Length != 132 || onWire[3] != 1) throw new Exception("P2 microphone sequence did not increment.");
}
Console.WriteLine("PASS: production P2 microphone sender emits consecutive UDP packets matching Thetis' expected 132-byte format.");

string automaticFolder = Path.Combine(Path.GetTempPath(), "openhpsdr-auto-handlers-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(automaticFolder);
try
{
    p1.handleCommandControl(0x12, 0, 0, 0, 0);
    p1.EnableAutoRecording(automaticFolder, TxAudioMode.USB);
    if (!p1.AutoRecordingEnabled || p1.CanStartManualRecording) throw new Exception("Automatic/manual recording controls conflict.");
    // PTT falls in the second half of a P1 datagram. The first half must be
    // recorded before processing the falling edge in the second control header.
    var mixedFrame = new byte[1032]; mixedFrame[0] = 0xef; mixedFrame[1] = 0xfe;
    mixedFrame[2] = 1; mixedFrame[3] = 2;
    mixedFrame[11] = 0x13; mixedFrame[523] = 0x12;
    p1.handlePacket(new receivedPacket { received = mixedFrame, endPoint = endpoint });
    p1.DisableAutoRecording();
    string usbFile = Directory.GetFiles(automaticFolder).Single(p => p.Contains("_USB"));
    if (new FileInfo(usbFile).Length != 44 + 63 * 2) throw new Exception("P1 automatic recording lost the final TX half-frame.");
    hp[4] = 1;
    highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
    radio.EnableAutoRecording(automaticFolder, TxAudioMode.LSB);
    hp[4] = 3;
    highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
    typeof(FakeHermesNewProtocol).GetMethod("handleTxIQPacket", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(radio, new object[] { new receivedPacket { received = new byte[1444], endPoint = endpoint }, 0 });
    hp[4] = 1;
    highPriority.Invoke(radio, new object[] { new receivedPacket { received = hp, endPoint = endpoint } });
    radio.DisableAutoRecording();
    string lsbFile = Directory.GetFiles(automaticFolder).Single(p => p.Contains("_LSB"));
    if (new FileInfo(lsbFile).Length != 44 + 60 * 2) throw new Exception("P2 automatic PTT capture has incorrect duration.");
    if (Directory.GetFiles(automaticFolder).Length != 2) throw new Exception("PTT controls created unexpected recording files.");
    Console.WriteLine("PASS: actual P1/P2 PTT and IQ handlers create dated USB/LSB auto recordings; P1 half-frame edge and UI interlock verified.");
}
finally
{
    p1.AutoRecorder.Disarm(); radio.AutoRecorder.Disarm();
    foreach (string file in Directory.GetFiles(automaticFolder)) File.Delete(file);
    Directory.Delete(automaticFolder);
}

// Continuous TX data must never prevent the next PTT edge being processed.
var busy = new FakeHermesNewProtocol();
var connectionFields = new[] { "generalClient", "rxSpecificClient", "txSpecificClient", "highPriorityClient", "tx0IQClient", "rxAudioClient" };
foreach (string field in connectionFields)
    typeof(FakeHermesNewProtocol).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
        .SetValue(busy, new udpConnection { msgQueue = new System.Collections.Concurrent.ConcurrentQueue<receivedPacket>() });
udpConnection QueueFor(string field) => (udpConnection)typeof(FakeHermesNewProtocol)
    .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(busy);
void BusyControl(uint sequence, bool tx)
{
    var data = new byte[1444]; data[0] = (byte)(sequence >> 24); data[1] = (byte)(sequence >> 16);
    data[2] = (byte)(sequence >> 8); data[3] = (byte)sequence; data[4] = tx ? (byte)3 : (byte)1;
    QueueFor("highPriorityClient").msgQueue.Enqueue(new receivedPacket { received = data, endPoint = endpoint });
}
var busyIq = new byte[1444]; busyIq[4] = 0x40;
for (int i = 0; i < 4096; i++) QueueFor("tx0IQClient").msgQueue.Enqueue(new receivedPacket { received = busyIq, endPoint = endpoint });
string busyFolder = Path.Combine(Path.GetTempPath(), "openhpsdr-ptt-priority-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(busyFolder);
try
{
    busy.EnableAutoRecording(busyFolder, TxAudioMode.USB);
    var processTurn = typeof(FakeHermesNewProtocol).GetMethod("HandleIncomingPackets", BindingFlags.Instance | BindingFlags.NonPublic);
    BusyControl(10, true); processTurn.Invoke(busy, null);
    if (!busy.Monitor.Decoder.Snapshot().Values["ptt"].Active || QueueFor("tx0IQClient").msgQueue.Count != 4096 - 64)
        throw new Exception("PTT on must be applied before a bounded batch of TX IQ.");
    BusyControl(12, false); processTurn.Invoke(busy, null);
    if (busy.Monitor.Decoder.Snapshot().Values["ptt"].Active || QueueFor("tx0IQClient").msgQueue.Count != 4096 - 128)
        throw new Exception("PTT off was blocked by remaining TX traffic.");
    BusyControl(11, true); processTurn.Invoke(busy, null);
    if (busy.Monitor.Decoder.Snapshot().Values["ptt"].Active)
        throw new Exception("Delayed stale PTT-on packet must not reactivate TX after release.");
    busy.DisableAutoRecording();
    string recorded = Directory.GetFiles(busyFolder).Single();
    if (new FileInfo(recorded).Length != 44 + 64 * 60 * 2)
        throw new Exception("Automatic recording did not preserve the PTT burst under TX backlog.");
    Console.WriteLine("PASS: PTT on/off and automatic capture stay responsive with 4096 queued TX packets; per-turn budget and stale-control rejection verified.");
}
finally
{
    busy.AutoRecorder.Disarm();
    foreach (string file in Directory.GetFiles(busyFolder)) File.Delete(file);
    Directory.Delete(busyFolder);
}


