using System;

namespace Patroclus.Avalonia.ViewModels
{
    // Thetis consumes 64 microphone samples per P2 packet, including when VAC
    // supplies the actual microphone audio. These packets pace its TX DSP.
    public static class Protocol2Microphone
    {
        public const int SampleRate = 48000;
        public const int SamplesPerPacket = 64;
        public const int PacketLength = 4 + SamplesPerPacket * 2;

        public static long DuePackets(long elapsedMilliseconds)
            => PacketTiming.DuePackets(SampleRate, SamplesPerPacket, elapsedMilliseconds);

        public static byte[] CreatePacket(uint sequence)
        {
            var packet = new byte[PacketLength];
            packet[0] = (byte)(sequence >> 24); packet[1] = (byte)(sequence >> 16);
            packet[2] = (byte)(sequence >> 8); packet[3] = (byte)sequence;
            // Hardware microphone remains silent. VAC audio is supplied by the client.
            return packet;
        }
    }
}
