using System;
using System.Threading.Tasks;
using System.Net;
using Patroclus.Avalonia.ViewModels;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
bool Active(RadioControlDecoder d, string key) => d.Snapshot().Values[key].Active;
void Word(byte[] p, int offset, uint word)
{
    p[offset] = (byte)(word >> 24); p[offset + 1] = (byte)(word >> 16);
    p[offset + 2] = (byte)(word >> 8); p[offset + 3] = (byte)word;
}
var p1 = new RadioControlDecoder(false);
Check(p1.Snapshot().Values.Count == 0, "No packet means unknown.");
int[] rxBits = { 1, 2, 4, 5, 6, 12, 3 };
int[] lpfBits = { 20, 21, 22, 23, 29, 30, 31 };
var p2 = new RadioControlDecoder(false);
var hp = new byte[1444];
for (int bank = 0; bank < 2; bank++)
for (int bit = 0; bit < 7; bit++)
{
    byte mask = (byte)(1 << bit);
    if (bank == 0) p1.Protocol1(0x12, 0, 0x40, mask, 0);
    else p1.Protocol1(0x24, mask, 0, 0, 0);
    Array.Clear(hp, 0, hp.Length);
    Word(hp, bank == 0 ? 1432 : 1428, 1u << rxBits[bit]);
    p2.Protocol2HighPriority(hp);
    for (int other = 0; other < 7; other++)
    {
        string key = "rx" + bank + "." + other;
        Check(Active(p1, key) == (bit == other), "P1 RX mapping " + key);
        Check(Active(p2, key) == (bit == other), "P2 RX mapping " + key);
    }
}
for (int bit = 0; bit < 7; bit++)
{
    p1.Protocol1(0x12, 73, 0x40, 0, (byte)(1 << bit));
    Array.Clear(hp, 0, hp.Length); Word(hp, 1432, 1u << lpfBits[bit]);
    p2.Protocol2HighPriority(hp);
    for (int other = 0; other < 7; other++)
    {
        Check(Active(p1, "lpf" + other) == (bit == other), "P1 LPF isolation");
        Check(Active(p2, "lpf" + other) == (bit == other), "P2 LPF isolation including bit 31");
    }
}
p1.Protocol1(0x15, 3, 64, 5, 32 | 17);
Check(Active(p1, "ptt") && Active(p1, "adc0") && Active(p1, "pureSignal"), "P1 MOX/flags");
Check(p1.Snapshot().Values["adc0"].Detail.StartsWith("17 dB"), "ADC0 ATT value");
p1.Protocol1(0x16, 23, 0, 0, 0);
Check(!Active(p1, "adc1") && p1.Snapshot().Values["adc1"].Detail.StartsWith("23 dB"), "Disabled ATT retains value");
p1.Protocol1(0, 0, 0xaa, 0x1b, 2);
Check(Active(p1, "att10") && Active(p1, "att20") && Active(p1, "dither0") && Active(p1, "random0"), "P1 general flags");
Check(Active(p1, "ant2") && !Active(p1, "ant0"), "P1 antenna");
p1.Protocol1Run(false); Check(!Active(p1, "run") && !Active(p1, "ptt"), "P1 stop");
Array.Clear(hp, 0, hp.Length);
hp[4] = 3; hp[345] = 87; hp[1443] = 17; hp[1442] = 23;
hp[1401] = 0xaa; hp[1402] = 5; hp[1403] = 3;
Word(hp, 1432, (1u << 14) | (1u << 13) | (1u << 25) | (1u << 9));
p2.Protocol2HighPriority(hp);
Check(Active(p2, "run") && Active(p2, "ptt") && Active(p2, "ant1") && Active(p2, "ext1"), "P2 operation/routing");
Check(p2.Snapshot().Values["drive"].Detail == "87 / 255", "P2 drive");
Check(p2.Snapshot().Values["adc1"].Detail.StartsWith("23 dB"), "P2 ADC byte order");
Check(Active(p2, "oc0") && !Active(p2, "oc1") && Active(p2, "user2"), "Output ordering");
var general = new byte[60]; general[58] = 1; general[59] = 3;
p2.Protocol2General(general);
Check(!Active(p2, "paDisabled") && Active(p2, "alex0") && Active(p2, "alex1"), "P2 General PA enabled");
general[58] = 0; p2.Protocol2General(general);
Check(Active(p2, "paDisabled"), "P2 PA disable is active low");
general[58] = 0xfe; p2.Protocol2General(general);
Check(Active(p2, "paDisabled"), "P2 PA enable uses only bit zero");
p1.Protocol1(0x12, 0, 0, 0x80, 0);
Check(Active(p1, "paDisabled"), "P1 PA disable is active high");
p1.Protocol1(0x12, 0, 0, 0x7f, 0);
Check(!Active(p1, "paDisabled"), "P1 PA enabled ignores other C3 bits");
general[4] = 2; long revision = p2.Snapshot().Revision;
Check(!p2.Protocol2General(general) && p2.Snapshot().Revision == revision, "Discovery is not settings");
p2.Protocol2Rx(new byte[] { 0, 0, 0, 0, 2, 1, 2 });
Check(Active(p2, "dither0") && !Active(p2, "dither1") && Active(p2, "random1"), "RX flags");
revision = p2.Snapshot().Revision;
for (int length = 0; length < 1444; length++)
    Check(!p2.Protocol2HighPriority(new byte[length]), "Truncated HP " + length);
Check(!p2.Protocol2HighPriority(null) && !p2.Protocol2General(new byte[59]) && !p2.Protocol2Rx(new byte[6]), "Short/null input");
Check(p2.Snapshot().Revision == revision, "Malformed packets preserve state");
var saved = p2.Snapshot(); saved.Values.Clear();
Check(p2.Snapshot().Values.Count > 0, "Snapshots must not mutate live state");
var lite = new RadioControlDecoder(false);
lite.Protocol1(0x12, 255, 255, 255, 255, true);
Check(lite.Snapshot().Values.Count == 1, "Hermes Lite must not pretend Alex relays");
var modern = new RadioControlDecoder(true); modern.Protocol2HighPriority(hp);
Check(!modern.Snapshot().Values.ContainsKey("att20"), "MkII routing is not legacy ATT");
Parallel.For(0, 1000, i => { p2.Protocol2HighPriority(hp); p2.Snapshot(); });
Check(p2.Snapshot().Revision == revision + 1000, "Concurrent capture consistency");
TimeSpan now = TimeSpan.Zero;
var session = new ClientSession(() => now);
var clientA = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 50000);
var clientB = new IPEndPoint(IPAddress.Parse("192.0.2.2"), 51000);
Check(!session.Snapshot().Active && session.Snapshot().Endpoint == "No client", "No false initial connection");
session.Touch(clientA);
Check(session.Snapshot().Endpoint == "No client", "Traffic without RUN is not a connection");
session.ObserveRun(clientA, true);
Check(session.Snapshot().Active && session.Snapshot().Endpoint == "192.0.2.1:50000", "RUN identifies exact endpoint");
clientA.Port = 12345;
Check(session.Snapshot().Endpoint == "192.0.2.1:50000", "Endpoint is copied");
clientA.Port = 50000;
now = TimeSpan.FromSeconds(4); session.Touch(clientB);
now = TimeSpan.FromSeconds(5);
Check(!session.Snapshot().Active, "Other client traffic cannot refresh active connection");
session.Touch(clientA);
Check(session.Snapshot().Active, "Control traffic restores activity");
session.ObserveRun(clientB, true);
Check(session.Snapshot().Endpoint == "192.0.2.2:51000", "New RUN replaces endpoint");
session.ObserveRun(clientB, false); session.Touch(clientB);
Check(!session.Snapshot().Active && session.Snapshot().Status.StartsWith("Stopped"), "STOP remains stopped despite traffic");
Check(new ClientSession().Snapshot().Endpoint == "No client", "New profile resets session");
var options1 = new RadioControlDecoder(false);
for (byte rate = 0; rate < 4; rate++)
{
    options1.Protocol1(0, rate, 0, 0, 0);
    Check(options1.Snapshot().Values["sampleRate"].Detail == (48 << rate) + " kS/s (P1)", "P1 sample rate code");
}
for (int bit = 0; bit < 6; bit++)
{
    options1.Protocol1(0x12, 0, (byte)(1 << bit), 0, 0);
    string[] keys = { "micBoost", "lineIn", "apolloFilter", "apolloTuner", "apolloTune", "apollo" };
    for (int other = 0; other < keys.Length; other++)
        Check(Active(options1, keys[other]) == (bit == other), "P1 Hardware Options isolation " + keys[other]);
}
for (int bits = 0; bits < 8; bits++)
{
    options1.Protocol1(0x14, (byte)(bits << 4), 0, 0, 0);
    Check(Active(options1, "micRing") == ((bits & 1) != 0), "P1 Mic Tip/Ring");
    Check(Active(options1, "micBias") == ((bits & 2) != 0), "P1 Mic Bias");
    Check(Active(options1, "micPtt") == ((bits & 4) == 0), "P1 PTT disable bit inversion");
}
Check(!options1.Snapshot().Values.ContainsKey("hardwareAlex"), "P1 does not report Alex presence");
var options2 = new RadioControlDecoder(false);
var txPacket = new byte[60]; txPacket[14] = 0; txPacket[15] = 192;
for (int bit = 0; bit < 6; bit++)
{
    txPacket[50] = (byte)(1 << bit); options2.Protocol2Tx(txPacket);
    string[] keys = { "lineIn", "micBoost", "micPtt", "micRing", "micBias", "micXlr" };
    for (int other = 0; other < keys.Length; other++)
        Check(Active(options2, keys[other]) == (other == 2 ? bit != other : bit == other), "P2 Hardware Options isolation " + keys[other]);
}
Check(options2.Snapshot().Values["txSampleRate"].Detail == "192 kS/s", "P2 TX rate uses BE kS/s");
long beforeShortTx = options2.Snapshot().Revision;
Check(!options2.Protocol2Tx(new byte[59]) && !options2.Protocol2Tx(null) && options2.Snapshot().Revision == beforeShortTx, "Short TX packet preserves state");
var ratesPacket = new byte[1444];
ratesPacket[7] = 0x0c; // physical DDC2 + DDC3
ratesPacket[30] = 0; ratesPacket[31] = 192;
ratesPacket[36] = 1; ratesPacket[37] = 128;
options2.Protocol2Rx(ratesPacket);
Check(options2.Snapshot().Values["sampleRate"].Detail == "DDC2: 192 kS/s · DDC3: 384 kS/s", "P2 only enabled physical DDC rates");

// P1 table only learns an OC mask when its frequency is known and the OC bank arrives.
var oc1 = new RadioControlDecoder(false);
oc1.Protocol1(0, 0, 0xaa, 0, 0);
Check(!oc1.Snapshot().Values.ContainsKey("ocObservation"), "Unknown frequency cannot invent OC band");
void Frequency1(byte address, uint hz) => oc1.Protocol1(address, (byte)(hz >> 24), (byte)(hz >> 16), (byte)(hz >> 8), (byte)hz);
Frequency1(4, 14200000); oc1.Protocol1(0, 0, 0xaa, 0, 0);
Frequency1(2, 7100000); oc1.Protocol1(1, 0, 0x54, 0, 0);
for (int pin = 0; pin < 7; pin++)
{
    Check(Active(oc1, $"oc.20m.rx.{pin}") == (pin % 2 == 0), "P1 RX OC pins by band");
    Check(Active(oc1, $"oc.40m.tx.{pin}") == (pin % 2 == 1), "P1 TX OC pins by band");
}
Check(!oc1.Snapshot().Values.ContainsKey("oc.20m.tx.0"), "Unvisited TX band remains unknown");
// P2 frequency flags and first enabled DDC determine RX band, not inactive DDC0.
var oc2 = new RadioControlDecoder(false);
general[4] = 0; general[37] = 8; oc2.Protocol2General(general);
oc2.Protocol2Rx(ratesPacket);
uint Phase(double hz) => (uint)Math.Round(hz * 4294967296.0 / 122880000.0);
var ocHp = new byte[1444]; ocHp[1401] = 0x82;
Word(ocHp, 9, Phase(3800000)); Word(ocHp, 17, Phase(14200000)); Word(ocHp, 329, Phase(7100000));
oc2.Protocol2HighPriority(ocHp);
Check(Active(oc2, "oc.20m.rx.0") && Active(oc2, "oc.20m.rx.6"), "P2 RX phase words / sparse DDC / pins 1 and 7");
Check(!oc2.Snapshot().Values.ContainsKey("oc.80m.rx.0"), "Inactive DDC0 must not select OC band");
ocHp[4] = 2; ocHp[1401] = 0; oc2.Protocol2HighPriority(ocHp);
Check(!Active(oc2, "oc.40m.tx.0"), "P2 zero OC mask is known OFF");
general[37] = 0; oc2.Protocol2General(general);
Word(ocHp, 329, 50100000); oc2.Protocol2HighPriority(ocHp);
Check(!Active(oc2, "oc.6m.tx.0"), "P2 direct Hz flag");
var unknownFormat = new RadioControlDecoder(false); unknownFormat.Protocol2Rx(ratesPacket); unknownFormat.Protocol2HighPriority(ocHp);
Check(!unknownFormat.Snapshot().Values.ContainsKey("ocObservation"), "Unknown phase/Hz format stays unknown");
Check(RadioControlDecoder.BandForFrequency(1799999.99) == "160m", "Phase quantization at band edge");
Check(RadioControlDecoder.BandForFrequency(145000000) == "2m" && RadioControlDecoder.BandForFrequency(1000000) == "GEN", "2m / general classification");
Console.WriteLine($"PASS: {checks} P1/P2 checks including Hardware Options, sample rates and RX/TX band OC history.");


