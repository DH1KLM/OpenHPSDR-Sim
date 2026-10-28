namespace Patroclus.Avalonia.ViewModels
{
    internal static class PacketTiming
    {
        //DH1KLM: Divide after accumulating samples so nonintegral packet rates keep their fractional remainder.
        internal static long DuePackets(int sampleRate, int samplesPerPacket,
                                        long elapsedMilliseconds, int clockScale = 1000)
        {
            if (sampleRate <= 0 || samplesPerPacket <= 0 || elapsedMilliseconds <= 0 || clockScale <= 0)
                return 0;
            return (long)sampleRate * elapsedMilliseconds / ((long)samplesPerPacket * clockScale);
        }
    }
}
