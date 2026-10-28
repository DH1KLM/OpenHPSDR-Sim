using System;
using System.Collections.Generic;

namespace Patroclus.Avalonia.ViewModels
{
    // Model names are local labels. HPSDR discovery advertises a board, not a model.
    // Reference: ramdor/Thetis, Console/enums.cs and HPSDR/clsRadioDiscovery.cs.
    public sealed class RadioProfile
    {
        public string Name { get; }
        public int Protocol { get; }
        public byte BoardId { get; }
        public byte FirmwareVersion { get; }
        public string BoardName { get; }
        // Physical filter hardware is model-specific, not implied by a second ADC
        // or by the presence of an ALEX1 word in the network packet.
        public bool HasSecondRxFilterBank => Name.StartsWith("ANAN-7000DLE", StringComparison.Ordinal)
            || Name.StartsWith("ANAN-8000DLE", StringComparison.Ordinal) || BoardName == "Saturn";
        public string Description => $"{BoardName} | Protocol {Protocol} | Board ID {BoardId}";

        public RadioProfile(string name, int protocol, byte boardId, string boardName,
                            byte firmwareVersion = 30)
        {
            if (protocol != 1 && protocol != 2)
                throw new ArgumentOutOfRangeException(nameof(protocol));
            Name = name;
            Protocol = protocol;
            BoardId = boardId;
            BoardName = boardName;
            FirmwareVersion = firmwareVersion;
        }

        public override string ToString() => $"{Name} (P{Protocol})";

        public byte[] CreateDiscoveryResponse()
        {
            var response = new byte[60];
            if (Protocol == 1)
            {
                response[0] = 0xef;
                response[1] = 0xfe;
                response[2] = 2;
                response[8] = 1; // locally simulated MAC 00:00:00:00:00:01
                response[9] = FirmwareVersion;
                response[10] = BoardId;
            }
            else
            {
                response[4] = 2;
                response[10] = 1;
                response[11] = BoardId;
                response[12] = 2; // protocol supported; firmware is byte 13
                response[13] = FirmwareVersion;
                response[20] = 7; // preserve emulator's advertised DDC count
                response[21] = 1;
            }
            return response;
        }

        public static IReadOnlyList<RadioProfile> All { get; } = CreateProfiles();

        private static IReadOnlyList<RadioProfile> CreateProfiles()
        {
            var profiles = new List<RadioProfile>
            {
                new RadioProfile("HPSDR Hermes", 1, 1, "Hermes"),
                new RadioProfile("HPSDR Hermes", 2, 1, "Hermes"),
                new RadioProfile("Hermes Lite", 1, 6, "Hermes Lite"),
                new RadioProfile("Hermes Lite 2", 1, 6, "Hermes Lite", 40)
            };
            // P1 and P2 board numbers differ for Angelia, Orion and Orion MkII.
            AddBoth(profiles, "ANAN-10", 1, 1, "Hermes");
            AddBoth(profiles, "ANAN-10E", 2, 2, "Hermes II");
            AddBoth(profiles, "ANAN-100", 1, 1, "Hermes");
            AddBoth(profiles, "ANAN-100B", 2, 2, "Hermes II");
            AddBoth(profiles, "ANAN-100D", 4, 3, "Angelia");
            AddBoth(profiles, "ANAN-200D", 5, 4, "Orion");
            AddBoth(profiles, "ANAN-7000DLE", 10, 5, "Orion MkII");
            AddBoth(profiles, "ANAN-7000DLE MKII", 10, 5, "Orion MkII");
            AddBoth(profiles, "ANAN-7000DLE MKIII", 10, 5, "Orion MkII compatible");
            AddBoth(profiles, "ANAN-8000DLE", 10, 5, "Orion MkII");
            AddBoth(profiles, "ANAN-8000DLE MKII", 10, 5, "Orion MkII compatible");
            profiles.Add(new RadioProfile("ANAN-G2", 2, 10, "Saturn"));
            profiles.Add(new RadioProfile("ANAN-G2 Ultra", 2, 10, "Saturn"));
            profiles.Add(new RadioProfile("ANAN-G2-1K", 2, 10, "Saturn"));
            profiles.Add(new RadioProfile("ANAN-G2E", 2, 20, "Hermes C10"));
            AddBoth(profiles, "Red Pitaya", 10, 5, "Orion MkII compatible");
            AddBoth(profiles, "ANVELINA Pro3", 10, 5, "Orion MkII compatible");
            return profiles.AsReadOnly();
        }

        private static void AddBoth(List<RadioProfile> profiles, string name,
                                    byte p1Board, byte p2Board, string boardName)
        {
            profiles.Add(new RadioProfile(name, 1, p1Board, boardName));
            profiles.Add(new RadioProfile(name, 2, p2Board, boardName));
        }
    }
}
