using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Patroclus.Avalonia.ViewModels;

const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
using var sink = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
sink.Client.ReceiveTimeout = 1500;
var endpoint = (IPEndPoint)sink.Client.LocalEndPoint;
var peer = new IPEndPoint(IPAddress.Any, 0);
void Check(bool value, string message) { if (!value) throw new Exception(message); }
receiver TestReceiver()
{
    var rx = new receiver("test") { bandwidth = 48000 };
    rx.generators[0].SetDefaults(1000);
    return rx;
}
void Drain() { while (sink.Available > 0) sink.Receive(ref peer); }
var p2 = new FakeHermesNewProtocol
{
    port = 0, RxSpecificPort = 0, TxSpecificPort = 0, HighPriorityFromPCPort = 0,
    Tx0IQPort = 0, ReceiverAudioPort = 0
};
var rx2 = TestReceiver(); p2.receivers.Add(rx2);
using var rxSource = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
((Dictionary<receiver, UdpClient>)typeof(FakeHermesNewProtocol).GetField("rxClients", Fields).GetValue(p2)).Add(rx2, rxSource);
bool p2Started = false;
string autoFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "openhpsdr-udp-auto-" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(autoFolder);
try
{
    p2.start(); p2Started = true;
    var general = new byte[60]; general[37] = 8;
    p2.handleGeneralPacket(new receivedPacket { received = general, endPoint = endpoint });
    var control = (udpConnection)typeof(FakeHermesNewProtocol).GetField("highPriorityClient", Fields).GetValue(p2);
    var controlDestination = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)control.Client.Client.LocalEndPoint).Port);
    uint controlSequence = 0;
    void Ptt(bool tx)
    {
        var command = new byte[1444]; command[4] = tx ? (byte)3 : (byte)1;
        command[0] = (byte)(controlSequence >> 24); command[1] = (byte)(controlSequence >> 16);
        command[2] = (byte)(controlSequence >> 8); command[3] = (byte)controlSequence++;
        sink.Send(command, command.Length, controlDestination);
    }
    void ReadP2(bool muted)
    {
        int iqFrames = 0, microphones = 0; int previous = -1;
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 2000 && (iqFrames < 3 || microphones < 2))
        {
            var data = sink.Receive(ref peer);
            if (data.Length == 132) { microphones++; continue; }
            if (data.Length != 1444) continue;
            int sequence = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
            bool zero = !data.Skip(16).Any(b => b != 0);
            if (zero != muted) { iqFrames = 0; continue; } // discard frames queued before the PTT edge
            if (iqFrames > 0) Check(sequence == previous + 1, "P2 sequence stopped or jumped during switch");
            previous = sequence; iqFrames++;
            Check(data[13] == 24 && data[14] == 0 && data[15] == 238, "P2 IQ header was damaged");
        }
        Check(iqFrames >= 3 && microphones >= 2, "P2 RX packets / microphone clock stopped during TX");
    }
    Check(p2.MuteRxDuringTx, "P2 default must mute RX on PTT");
    p2.EnableAutoRecording(autoFolder, Patroclus.Avalonia.Audio.TxAudioMode.USB);
    Ptt(false); ReadP2(false);
    p2.RefreshRecording();
    Check(p2.StreamTimingStatus.Contains("Stream timing:") && !p2.StreamTimingStatus.Contains("Starting"), "P2 scheduling/timing diagnostic did not initialize");
    // Aether keeps discovering both protocols from a separate UDP socket during RX.
    // Discovery must reply there without redirecting the active IQ/mic streams.
    using (var discovery = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
    {
        discovery.Client.ReceiveTimeout = 1500;
        var generalSocket = (udpConnection)typeof(FakeHermesNewProtocol).GetField("generalClient", Fields).GetValue(p2);
        var destination = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)generalSocket.Client.Client.LocalEndPoint).Port);
        var sequenceField = typeof(FakeHermesNewProtocol).GetField("seqNo", Fields);
        uint sequenceBefore = (uint)sequenceField.GetValue(p2);
        var p2Probe = new byte[60]; p2Probe[4] = 2;
        var p1Probe = new byte[63]; p1Probe[0] = 0xef; p1Probe[1] = 0xfe; p1Probe[2] = 2;
        for (int sweep = 0; sweep < 3; sweep++)
        {
            discovery.Send(p2Probe, p2Probe.Length, destination);
            var reply = discovery.Receive(ref peer);
            Check(reply.Length == 60 && reply[4] == 3, "Discovery must advertise the running P2 radio");
            discovery.Send(p1Probe, p1Probe.Length, destination);
            ReadP2(false);
        }
        Check(endpoint.Equals(typeof(FakeHermesNewProtocol).GetField("ClientIpEndPoint", Fields).GetValue(p2)), "Discovery redirected the active stream");
        Check((uint)sequenceField.GetValue(p2) >= sequenceBefore, "Discovery reset the outgoing status sequence");
        Check(discovery.Available == 0, "IQ/microphone stream leaked to discovery socket");
    }
    Console.WriteLine("PASS: repeated P1/P2 discovery during live RX preserves stream destination, status sequence and IQ/microphone delivery.");
    Ptt(true); ReadP2(true);
    Ptt(true); Ptt(true); ReadP2(true);
    p2.RefreshRecording();
    Check(p2.ControlInputStatus.Contains("flags 0x03") && !p2.ControlInputStatus.Contains("received none"), "Real UDP control input was not tracked");
    p2.MuteRxDuringTx = false; ReadP2(false);
    p2.MuteRxDuringTx = true; ReadP2(true);
    Ptt(false); ReadP2(false);
    p2.DisableAutoRecording();
    Check(System.IO.Directory.GetFiles(autoFolder).Length == 1, "Real UDP PTT must create one automatic recording for the complete TX hold");
    Check(rx2.timebase > 0 && rx2.packetCount > 0, "RX timebase did not advance");
}
finally
{
    if (p2Started) p2.Stop();
    foreach (string file in System.IO.Directory.GetFiles(autoFolder)) System.IO.File.Delete(file);
    System.IO.Directory.Delete(autoFolder);
}
Drain();

var p1 = new FakeHermes { port = 0, bandwidth = 48000 };
p1.receivers.Add(TestReceiver());
bool p1Started = false;
try
{
    p1.start(); p1Started = true;
    p1.handlePacket(new receivedPacket { received = new byte[] { 0xef, 0xfe, 4, 1 }, endPoint = endpoint });
    void ReadP1(bool muted)
    {
        int frames = 0, previous = -1; var timer = Stopwatch.StartNew();
        while (frames < 3 && timer.ElapsedMilliseconds < 2000)
        {
            var data = sink.Receive(ref peer);
            if (data.Length != 1032) continue;
            bool zero = true;
            foreach (int block in new[] { 16, 528 })
                for (int s = 0; s < 63; s++)
                    for (int b = 0; b < 6; b++) zero &= data[block + s * 8 + b] == 0;
            if (zero != muted) { frames = 0; continue; }
            int sequence = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
            if (frames > 0) Check(sequence == previous + 1, "P1 sequence stopped during PTT");
            Check(data[8] == 0x7f && data[520] == 0x7f, "P1 control headers damaged");
            previous = sequence; frames++;
        }
        Check(frames >= 3, "P1 stream stopped or mute state wrong");
    }
    ReadP1(false);
    p1.RefreshRecording();
    Check(p1.StreamTimingStatus.Contains("Stream timing:") && !p1.StreamTimingStatus.Contains("Starting"), "P1 scheduling/timing diagnostic did not initialize");
    using (var discovery = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
    {
        discovery.Client.ReceiveTimeout = 1500;
        var streamSocket = (UdpClient)typeof(FakeHermes).GetField("client", Fields).GetValue(p1);
        var destination = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)streamSocket.Client.LocalEndPoint).Port);
        var probe = new byte[63]; probe[0] = 0xef; probe[1] = 0xfe; probe[2] = 2;
        int lastSequence = -1;
        for (int sweep = 0; sweep < 4; sweep++)
        {
            discovery.Send(probe, probe.Length, destination);
            var reply = discovery.Receive(ref peer);
            Check(reply.Length == 60 && reply[2] == 3, "P1 discovery must advertise running status");
            // Short EP2 data must be ignored without terminating the communication worker.
            var truncated = new byte[] { 0xef, 0xfe, 1, 2 };
            discovery.Send(truncated, truncated.Length, destination);
            for (int frame = 0; frame < 12; frame++)
            {
                var data = sink.Receive(ref peer);
                Check(data.Length == 1032, "P1 IQ stream stopped during discovery");
                int sequence = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
                if (lastSequence >= 0) Check(sequence == lastSequence + 1, "P1 discovery reset or interrupted IQ sequence");
                lastSequence = sequence;
            }
        }
        Check(discovery.Available == 0, "P1 IQ leaked to the discovery socket");
    }
    Console.WriteLine("PASS: repeated P1 discovery preserves continuous IQ sequence and stream delivery.");
    p1.handleCommandControl(0x13, 0, 0, 0, 0); ReadP1(true);
    p1.handleCommandControl(0x12, 0, 0, 0, 0); ReadP1(false);
}
finally { if (p1Started) p1.Stop(); }
Console.WriteLine("PASS: live local UDP P1/P2 RX -> muted TX -> RX; packet sequence, headers and P2 microphone cadence preserved; P2 duplex override works.");
