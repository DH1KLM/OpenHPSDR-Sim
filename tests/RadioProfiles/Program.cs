using System;
using System.Linq;
using Patroclus.Avalonia.ViewModels;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Main()
    {
        var expected = new[]
        {
            ("ANAN-10", 1, 1), ("ANAN-10E", 2, 2),
            ("ANAN-100", 1, 1), ("ANAN-100B", 2, 2),
            ("ANAN-100D", 4, 3), ("ANAN-200D", 5, 4),
            ("ANAN-7000DLE", 10, 5), ("ANAN-7000DLE MKII", 10, 5),
            ("ANAN-7000DLE MKIII", 10, 5), ("ANAN-8000DLE", 10, 5),
            ("ANAN-8000DLE MKII", 10, 5),
            ("Red Pitaya", 10, 5), ("ANVELINA Pro3", 10, 5)
        };
        foreach (var model in expected)
        {
            Check(RadioProfile.All.Single(p => p.Name == model.Item1 && p.Protocol == 1).BoardId == model.Item2,
                  model.Item1 + " P1 mapping");
            Check(RadioProfile.All.Single(p => p.Name == model.Item1 && p.Protocol == 2).BoardId == model.Item3,
                  model.Item1 + " P2 mapping");
        }
        foreach (var name in new[] { "ANAN-G2", "ANAN-G2 Ultra", "ANAN-G2-1K", "ANAN-G2E" })
        {
            var profile = RadioProfile.All.Single(p => p.Name == name);
            Check(profile.Protocol == 2, name + " must use P2");
            Check(profile.BoardId == (name == "ANAN-G2E" ? 20 : 10), name + " board");
        }
        Check(RadioProfile.All[0].Name == "HPSDR Hermes" && RadioProfile.All[0].Protocol == 1, "default changed");
        Check(RadioProfile.All.Single(p => p.Name == "Hermes Lite 2").FirmwareVersion == 40, "Lite 2 identity");
        Check(RadioProfile.All.Select(p => p.ToString()).Distinct().Count() == RadioProfile.All.Count, "duplicate selection labels");
        foreach (var profile in RadioProfile.All)
        {
            var packet = profile.CreateDiscoveryResponse();
            Check(packet.Length == 60, "discovery length");
            var expectedPacket = new byte[60];
            if (profile.Protocol == 1)
            {
                expectedPacket[0] = 0xef; expectedPacket[1] = 0xfe; expectedPacket[2] = 2;
                expectedPacket[8] = 1; expectedPacket[9] = profile.FirmwareVersion;
                expectedPacket[10] = profile.BoardId;
            }
            else
            {
                expectedPacket[4] = 2; expectedPacket[10] = 1;
                expectedPacket[11] = profile.BoardId;
                expectedPacket[12] = 2; expectedPacket[13] = profile.FirmwareVersion;
                expectedPacket[20] = 7; expectedPacket[21] = 1;
                Check(packet[0] == 0 && packet[1] == 0 && packet[2] == 0 && packet[3] == 0, "P2 header");
            }
            Check(packet.SequenceEqual(expectedPacket), profile + " discovery bytes");
            packet[10] = 255;
            Check(profile.CreateDiscoveryResponse()[10] != 255, "shared response buffer");
        }
        Console.WriteLine($"PASS: {RadioProfile.All.Count} profiles, model/board mappings and discovery packets");
    }
}
