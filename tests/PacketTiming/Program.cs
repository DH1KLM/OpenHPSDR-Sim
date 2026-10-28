using System;
using Patroclus.Avalonia.ViewModels;

int combinations = 0;
foreach (int rate in new[] { 48000, 96000, 192000, 384000, 768000, 1536000 })
foreach (int samples in new[] { 126, 72, 50, 38, 30, 26, 22, 20, 238 })
{
    long previous = 0;
    for (long milliseconds = 0; milliseconds <= 60000; milliseconds++)
    {
        long count = PacketTiming.DuePackets(rate, samples, milliseconds);
        long delivered = count * samples * 1000;
        long required = (long)rate * milliseconds;
        if (count < previous || delivered > required || required - delivered >= samples * 1000L)
            throw new Exception($"Unbounded sample drift: {rate} Hz, {samples} samples, {milliseconds} ms.");
        previous = count;
    }
    combinations++;
}
if (PacketTiming.DuePackets(48000, 238, 60000) != 12100
    || PacketTiming.DuePackets(48000, 126, 60000) != 22857
    || PacketTiming.DuePackets(192000, 30, 60000) != 384000
    || PacketTiming.DuePackets(48000, 126, 60000, 1001) >= PacketTiming.DuePackets(48000, 126, 60000))
    throw new Exception("P1/P2 reference rates or clock adjustment differ.");
Console.WriteLine($"PASS: {combinations} P1/P2 rate/packet combinations over 60 seconds; cumulative error stays below one packet.");
var microphonePacket = Protocol2Microphone.CreatePacket(0x01020304);
if (microphonePacket.Length != 132 || microphonePacket[0] != 1 || microphonePacket[1] != 2
    || microphonePacket[2] != 3 || microphonePacket[3] != 4)
    throw new Exception("P2 microphone packet must be a BE sequence followed by 64 PCM16 samples.");
for (int i = 4; i < microphonePacket.Length; i++)
    if (microphonePacket[i] != 0) throw new Exception("No hardware microphone: samples must be silent.");
for (long ms = 0; ms <= 60000; ms++)
{
    long samples = Protocol2Microphone.DuePackets(ms) * 64;
    if (samples > ms * 48 || ms * 48 - samples >= 64)
        throw new Exception("P2 microphone pacing must stay within one 64-sample packet.");
}
if (Protocol2Microphone.DuePackets(1000) != 750 || Protocol2Microphone.DuePackets(60000) != 45000)
    throw new Exception("P2 microphone must clock Thetis TX DSP at exactly 48000 samples/s.");
Console.WriteLine("PASS: P2 microphone packets are 132 bytes / 64 samples; 750 packets/s and 48000 samples/s, with no cumulative drift.");
