using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using System.Windows.Data;
using System.Windows.Input;

namespace Patroclus.Avalonia.ViewModels
{
    public class FakeHermesNewProtocol : FakeRadio
    {
        private udpConnection generalClient;
        private udpConnection rxSpecificClient;
        private udpConnection txSpecificClient;
        private udpConnection highPriorityClient;
        private udpConnection tx0IQClient;
        private udpConnection rxAudioClient;

        const int maxReceivers = 80;
        private bool usePhaseWord = false;
        private int recordingSampleRate = 192000;
        private bool recordingPtt;

        Dictionary<receiver, UdpClient> rxClients = new Dictionary<receiver, UdpClient>();

        receiver[] receiversByIdx = new receiver[maxReceivers];

        private Thread handleCommsThread;

        public int port { get; set; } //Port for the Client to use
        public RadioProfile Profile { get; set; } = RadioProfile.All[1];


        IPEndPoint ClientIpEndPoint;

        const byte sync = 0x7f;
        const int max24int = 0x7fffff;
        const int min24int = -0x800000;


        byte[] databuf = new byte[1444];
        uint seqNo = 0;
        uint micSeqNo = 0;

        double timebase = 0.0;
        private readonly Stopwatch streamClock = new Stopwatch();
        bool running = false;

        double clk = 122880000;
        private volatile bool closing = false;

        private int _RxSpecificPort = 1025;
        public int RxSpecificPort
        {
            get { return _RxSpecificPort; }
            set { this.RaiseAndSetIfChanged(ref _RxSpecificPort, value); }
        }

        private int _TxSpecificPort = 1026;
        public int TxSpecificPort
        {
            get { return _TxSpecificPort; }
            set { this.RaiseAndSetIfChanged(ref _TxSpecificPort, value); }
        }
        private int _HighPriorityFromPCPort = 1027;
        public int HighPriorityFromPCPort
        {
            get { return _HighPriorityFromPCPort; }
            set { this.RaiseAndSetIfChanged(ref _HighPriorityFromPCPort, value); }
        }

        private int _HighPriorityToPCPort = 1025;
        public int HighPriorityToPCPort
        {
            get { return _HighPriorityToPCPort; }
            set { this.RaiseAndSetIfChanged(ref _HighPriorityToPCPort, value); }
        }
        private int _ReceiverAudioPort = 1028;
        public int ReceiverAudioPort
        {
            get { return _ReceiverAudioPort; }
            set { this.RaiseAndSetIfChanged(ref _ReceiverAudioPort, value); }
        }
        private int _Tx0IQPort = 1029;
        public int Tx0IQPort
        {
            get { return _Tx0IQPort; }
            set { this.RaiseAndSetIfChanged(ref _Tx0IQPort, value); }
        }

        private int _Rx0Port = 1035;
        public int Rx0Port
        {
            get { return _Rx0Port; }
            set { this.RaiseAndSetIfChanged(ref _Rx0Port, value); }
        }

        private int _MicSamplesPort = 1026;
        public int MicSamplesPort
        {
            get { return _MicSamplesPort; }
            set { this.RaiseAndSetIfChanged(ref _MicSamplesPort, value); }
        }

        private int _WidebandADC0Port = 1027;
        public int WidebandADC0Port
        {
            get { return _WidebandADC0Port; }
            set { this.RaiseAndSetIfChanged(ref _WidebandADC0Port, value); }
        }
        private int _clockError = 1000;
        public int clockError
        {
            get { return _clockError; }
            set
            {
                //prevent system trying to correct timing from original start time

          //      resetTransmission();
                this.RaiseAndSetIfChanged(ref _clockError, value);
            }
        }
        /*
Wideband Enable [7:0]
Wideband Samples per packet [15:8]
Wideband sample size 
Wideband update rate 
Pure Signal - Rx(n) to use for off air signal
Pure Signal - Rx(n) to use for DAC signal
Pure Signal Sampling Rate [15:8]
Pure Signal Sampling Rate [7:0]
Envelope PWM_max
Envelope PWM_max
Envelope PWM_min
Envelope PWM_min
Bits - [0]Time stamp, [1]VITA-49, [2]VNA mode
*/

        public FakeHermesNewProtocol()
        {
            Monitor = new ControlMonitor(Profile);
   //         BindingOperations.CollectionRegistering += BindingOperations_CollectionRegistering;

        }

    //    void BindingOperations_CollectionRegistering(object sender, CollectionRegisteringEventArgs e)
   //     {
      //      BindingOperations.EnableCollectionSynchronization(receivers, _receiversLock);
    //    }
        private object _receiversLock = new object();

        private ObservableCollection<receiver> _receivers = new ObservableCollection<receiver>();
        public ObservableCollection<receiver> receivers
        {
            get { return _receivers; }
            set { this.RaiseAndSetIfChanged(ref _receivers, value); }
        }

        private int _bandwidth = 192000;
        public int bandwidth
        {
            get { return _bandwidth; }
            set { this.RaiseAndSetIfChanged(ref _bandwidth, value); }
        }

        private int _txNCO = 0;
        public int txNCO
        {
            get { return _txNCO; }
            set { this.RaiseAndSetIfChanged(ref _txNCO, value); }
        }

        private bool _duplex = false;
        public bool duplex
        {
            get { return _duplex; }
            set { this.RaiseAndSetIfChanged(ref _duplex, value); }
        }
        private bool _adc1clip = false;
        public bool adc1clip
        {
            get { return _adc1clip; }
            set { this.RaiseAndSetIfChanged(ref _adc1clip, value); }
        }

        private string _status = "Off";
        public string status
        {
            get { return _status; }
            set { this.RaiseAndSetIfChanged(ref _status, value); }
        }
        private int _packetsSent = 0;
        public int packetsSent
        {
            get { return _packetsSent; }
            set { this.RaiseAndSetIfChanged(ref _packetsSent, value); }
        }
        private int _packetsReceived = 0;
        public int packetsReceived
        {
            get { return _packetsReceived; }
            set { this.RaiseAndSetIfChanged(ref _packetsReceived, value); }
        }


        public void start()
        {
            Monitor = new ControlMonitor(Profile);
            generalClient = new udpConnection() { Client = connect(port), msgQueue = new ConcurrentQueue<receivedPacket>() };
            generalClient.Client.BeginReceive(new AsyncCallback(incomming), generalClient);

            rxSpecificClient = new udpConnection() { Client = connect(RxSpecificPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            rxSpecificClient.Client.BeginReceive(new AsyncCallback(incomming), rxSpecificClient);

            txSpecificClient = new udpConnection() { Client = connect(TxSpecificPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            txSpecificClient.Client.BeginReceive(new AsyncCallback(incomming), txSpecificClient);

            highPriorityClient = new udpConnection() { Client = connect(HighPriorityFromPCPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            highPriorityClient.Client.BeginReceive(new AsyncCallback(incomming), highPriorityClient);

            tx0IQClient = new udpConnection() { Client = connect(Tx0IQPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            tx0IQClient.Client.BeginReceive(new AsyncCallback(incomming), tx0IQClient);

            rxAudioClient = new udpConnection() { Client = connect(ReceiverAudioPort), msgQueue = new ConcurrentQueue<receivedPacket>() };
            rxAudioClient.Client.BeginReceive(new AsyncCallback(incomming), rxAudioClient);


            handleCommsThread = new Thread(handleComms);
            handleCommsThread.IsBackground = true;
            handleCommsThread.Start();

        }
        //DH1KLM: Zero General port-table entries select Protocol 2 default ports.
        private static int DecodePort(byte[] packet, int offset, int defaultPort)
        {
            int port = (packet[offset] << 8) | packet[offset + 1];
            return port == 0 ? defaultPort : port;
        }

        private UdpClient connect(int port)
        {
            UdpClient client = new UdpClient(port);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                byte[] inValue = new byte[] { 0 };
                byte[] outValue = new byte[] { 0 };
                client.Client.IOControl(SIO_UDP_CONNRESET, inValue, outValue);
            }
            return client;
        }
        public override void Stop()
        {
            closing = true;
            handleCommsThread?.Join();
           // handleCommsThread.Abort();
            generalClient.Client.Close();
            rxSpecificClient.Client.Close();
            txSpecificClient.Client.Close();
            highPriorityClient.Client.Close();
            tx0IQClient.Client.Close();
            rxAudioClient.Client.Close();
            foreach (var rxClient in rxClients.Values) rxClient.Close();
            rxClients.Clear();

            base.Stop();
        }
        long actualPacketCount = 0;
        private long lastControlArrival, lastControlHandled, lastControlDelayMs;
        private byte lastControlFlags;
        private uint lastControlSequence;
        private bool controlSequenceKnown;
        private IPEndPoint controlPeer;
        protected override string ControlStatus
        {
            get
            {
                long arrival = Interlocked.Read(ref lastControlArrival), handled = Interlocked.Read(ref lastControlHandled);
                string age = handled == 0 ? "none" : $"{(Stopwatch.GetTimestamp() - handled) * 1000 / Stopwatch.Frequency} ms";
                string inputAge = arrival == 0 ? "none" : $"{(Stopwatch.GetTimestamp() - arrival) * 1000 / Stopwatch.Frequency} ms";
                return $"P2 control: flags 0x{lastControlFlags:X2} · seq {lastControlSequence} · received {inputAge} ago / handled {age} ago · delay {Interlocked.Read(ref lastControlDelayMs)} ms · queues HP {highPriorityClient?.msgQueue?.Count ?? 0}, TX {tx0IQClient?.msgQueue?.Count ?? 0}";
            }
        }

        private void Drain(udpConnection connection, Action<receivedPacket> handler, int budget)
        {
            for (int i = 0; i < budget && !closing && connection.msgQueue.TryDequeue(out var packet); i++) handler(packet);
        }

        private void HandleIncomingPackets()
        {
            // Never wait for a busy IQ/audio queue to become empty. Revisit PTT
            // between bounded batches, even when the client sends continuously.
            Drain(generalClient, handleGeneralPacket, 8);
            Drain(highPriorityClient, handleHighPriorityPacket, 64);
            Drain(rxSpecificClient, handleRxSpecificPacket, 8);
            Drain(txSpecificClient, handleTxSpecificPacket, 8);
            for (int batch = 0; batch < 8 && !closing; batch++)
            {
                Drain(highPriorityClient, handleHighPriorityPacket, 16);
                Drain(tx0IQClient, p => handleTxIQPacket(p, 0), 8);
            }
            Drain(rxAudioClient, handleRXAudioPacket, 32);
        }

        void handleComms()
        {
            using var scheduling = new StreamThreadScheduling();
            streamScheduling = scheduling.Description;
            while (!closing)
            {
                ObserveStreamLoop(running);
                HandleIncomingPackets();
                bool pending = !generalClient.msgQueue.IsEmpty || !highPriorityClient.msgQueue.IsEmpty
                    || !rxSpecificClient.msgQueue.IsEmpty || !txSpecificClient.msgQueue.IsEmpty
                    || !tx0IQClient.msgQueue.IsEmpty || !rxAudioClient.msgQueue.IsEmpty;
                //send any output
                if (running)
                {
                    
                    adc1clip = false;
                    int channels = receivers.Count();
                    int nSamples = 238;
                   
                    //DH1KLM: A monotonic elapsed clock keeps IQ pacing independent of wall-clock adjustments.
                    long totalTime = streamClock.ElapsedMilliseconds;

                    double ttimebase = timebase;

                    for (int ri = 0; ri < receivers.Count; ri++)
                    {
                        if (!running) break;
                        
                        var rx = receivers[ri];
                        if (rx != null)
                        {
                            double timeStep = 1.0 / rx.bandwidth;

                            long nPacketsCalculated = PacketTiming.DuePackets(rx.bandwidth, nSamples, totalTime);

                            long packetsToSend = nPacketsCalculated - rx.packetCount;
                            ObserveStreamBacklog(packetsToSend, nSamples, rx.bandwidth);
                            if (packetsToSend > 16) pending = true;
                            packetsToSend = Math.Min(16, packetsToSend);



                            for (int i = 0; i < packetsToSend; i++)
                            {
                                if (i % 4 == 0) Drain(highPriorityClient, handleHighPriorityPacket, 16);
                                if (!running) break;
                                int seqNo = rx.seq;
                                rx.seq++;
                                //sequence no
                                databuf[0] = (byte)(seqNo >> 24);
                                databuf[1] = (byte)((seqNo >> 16) & 0xff);
                                databuf[2] = (byte)((seqNo >> 8) & 0xff);
                                databuf[3] = (byte)(seqNo & 0xff);
                                //timestamp
                                /*     databuf[4] = (byte)(rx.timestamp>>56);
                                     databuf[5] = (byte)((rx.timestamp >> 48 ) & 0xff);
                                     databuf[6] = (byte)((rx.timestamp >> 40) & 0xff);
                                     databuf[7] = (byte)((rx.timestamp >> 32) & 0xff);
                                     databuf[8] = (byte)((rx.timestamp >> 24) & 0xff);
                                     databuf[9] = (byte)((rx.timestamp >> 16) & 0xff);
                                     databuf[10] = (byte)((rx.timestamp >> 8) & 0xff);
                                     databuf[11] = (byte)(rx.timestamp  & 0xff);

                                     rx.timestamp += (ulong)(clk * nSamples / rx.bandwidth);
                                     */
                                //bits per sample
                                databuf[12] = (byte)(0);
                                databuf[13] = (byte)(24);

                                //no of samples
                                databuf[14] = (byte)(nSamples >> 8);
                                databuf[15] = (byte)(nSamples & 0xff);



                                GenerateReceiverSignal(rx, databuf, nSamples, timeStep);
                                rxClients[rx].Send(databuf, databuf.Length, ClientIpEndPoint);
                                rx.packetCount++;
                                packetsSent++;
                                rx.timebase += nSamples * timeStep;
                            }
                        }
                        //seqNo++;
                        //actualPacketCount++;
                         
                    }
                //    timebase = ttimebase;
                    //  if (highPriorityToPC != null) 
                    if (running) sendHighPriorityToPC();

                    long micPacketsToSend = Protocol2Microphone.DuePackets(totalTime) - micSeqNo;
                    if (micPacketsToSend > 32) pending = true;
                    for (long i = 0; running && i < Math.Min(32, micPacketsToSend); i++) sendMicData();
                }


                Thread.Sleep(pending ? 0 : 1);
            }
        }
        // Silence only the IQ payload. Packet headers, sequence, receiver clocks,
        // microphone packets and high-priority status continue on their normal paths.
        private void GenerateReceiverSignal(receiver rx, byte[] output, int samples, double timeStep)
        {
            if (recordingPtt && MuteRxDuringTx)
            {
                Array.Clear(output, 16, samples * 6);
                rx.adcClip = false;
            }
            else rx.GenerateSignal(output, 16, 6, samples, rx.timebase, timeStep);
        }

        byte[] hpbuf = new byte[60];

        void sendHighPriorityToPC()
        {
            hpbuf[0] = (byte)(seqNo >> 24);
            hpbuf[1] = (byte)((seqNo >> 16) & 0xff);
            hpbuf[2] = (byte)((seqNo >> 8) & 0xff);
            hpbuf[3] = (byte)(seqNo & 0xff);

            hpbuf[5] = adc1clip ? (byte)1 : (byte)0;


            rxSpecificClient.Client.Send(hpbuf, hpbuf.Length, ClientIpEndPoint);
            seqNo++;

        }

        void sendMicData()
        {
            byte[] micbuf = Protocol2Microphone.CreatePacket(micSeqNo);

            txSpecificClient.Client.Send(micbuf, micbuf.Length, ClientIpEndPoint);
            micSeqNo++;

        }

        private void incomming(IAsyncResult res)
        {
            udpConnection con = res.AsyncState as udpConnection;

            IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, port);
            byte[] received = null;
            packetsReceived++;
            try
            {
                received = con.Client.EndReceive(res, ref RemoteIpEndPoint);
            }
            catch (Exception)
            {

            }
            try
            {
                if (received != null)
                {
                    Monitor.Session.Touch(RemoteIpEndPoint);
                    if (ReferenceEquals(con, highPriorityClient)) Interlocked.Exchange(ref lastControlArrival, Stopwatch.GetTimestamp());
                    con.msgQueue.Enqueue(new receivedPacket() { received = received, endPoint = RemoteIpEndPoint, receivedAtTicks = Stopwatch.GetTimestamp() });
                }
                // Enqueue before rearming so callbacks on the same socket cannot
                // reorder PTT edges when several datagrams are already buffered.
                con.Client.BeginReceive(new AsyncCallback(incomming), con);
            }
            catch (Exception)
            {
            }
        }

        IPAddress udpBroadcast = new IPAddress(new byte[] { 255, 255, 255, 255 });
        public void handleGeneralPacket(receivedPacket packet)
        {
            // P1 and P2 discovery share UDP 1024. A P1 probe has byte 4 == 0,
            // but is not a P2 General command and must never replace the stream peer.
            if (packet?.received == null || packet.received.Length != 60
                || (packet.received[0] == 0xef && packet.received[1] == 0xfe)) return;
            Monitor.Decoder.Protocol2General(packet.received);
            //     Console.Out.WriteLine("gp");
            byte[] received = packet.received;
            if (packet.endPoint.Address.Equals(udpBroadcast))
            {

            }
            //old style discovery
            else if (received[4] == 2)
            {
                //discovery
                byte[] response = Profile.CreateDiscoveryResponse();
                response[4] = running ? (byte)3 : (byte)2;

                status = "Discovered";
                generalClient.Client.Send(response, response.Length, packet.endPoint);
                packetsSent++;

                Console.WriteLine("disc");
            }
            else if (received[4] == 0)
            {
                RxSpecificPort = DecodePort(received, 5, 1025);
                TxSpecificPort = DecodePort(received, 7, 1026);

                HighPriorityFromPCPort = DecodePort(received, 9, 1027);
                //   if (highPriorityToPC == null) highPriorityToPC = new UdpClient(HighPriorityToPCPort);

                Rx0Port = DecodePort(received, 17, 1035);

                //   usePhaseWord = (received[37] & 8) != 0;

                usePhaseWord = true;
                ClientIpEndPoint = packet.endPoint;


            }


        }
        private void handleRxSpecificPacket(receivedPacket packet)
        {
            if (packet?.received == null || packet.received.Length < 1444) return;
            Monitor.Decoder.Protocol2Rx(packet.received);
            //     Console.Out.WriteLine("rxsp");
            int nReceivers = 0;
            byte[] received = packet.received;

            int adcs = received[4];


            for (int f = 0; f < 10; f++)
            {
                int mask = 1;
                for (int i = 0; i < 8; i++)
                {
                    int idx = f * 8 + i;
                    if ((received[7 + f] & mask) != 0)
                    {
                        nReceivers++;
                        if (receiversByIdx[idx] == null)
                        {
                            receiversByIdx[idx] = new receiver("RX" + idx);
                            Dispatcher.UIThread.InvokeAsync(new Action(() => {
                                    receivers.Add(receiversByIdx[idx]);
                                }));
                            rxClients.Add(receiversByIdx[idx], new UdpClient(Rx0Port + idx));
                        }
                    }
                    else
                    {
                        if (receiversByIdx[idx] != null)
                        {
                          //  lock (_receiversLock)
                          //  {
                                var rx = receiversByIdx[idx];
                               // receivers.Remove(rx);
                                Dispatcher.UIThread.InvokeAsync(new Action(() => {
                                    receivers.Remove(rx);
                                }));

                                rxClients[rx].Close();
                                rxClients.Remove(rx);
                                receiversByIdx[idx] = null;
                           // }
                        }
                    }
                    if (receiversByIdx[idx] != null)
                    {
                        int srate = (received[18 + idx * 6] << 8) + received[19 + idx * 6];
                        receiversByIdx[idx].bandwidth = srate * 1000;
                    }

                    mask <<= 1;
                }
            }
            // find sync'd and multiplexed receivers
         //   byte mux = received[1443];

        }
        private void handleRXAudioPacket(receivedPacket packet)
        {
            //            Console.Out.WriteLine("rxap");
        }
        private void handleTxSpecificPacket(receivedPacket packet)
        {
            Monitor.Decoder.Protocol2Tx(packet?.received);
            if (packet?.received?.Length >= 60)
                recordingSampleRate = ((packet.received[14] << 8) | packet.received[15]) * 1000;
            //              Console.Out.WriteLine("txsp");
        }
        private void handleTxIQPacket(receivedPacket packet, int adc)
        {
            TxSignal.Protocol2(packet?.received);
            Recorder.Protocol2(packet?.received, recordingSampleRate, recordingPtt);
            AutoRecorder.Protocol2(packet?.received, recordingSampleRate, recordingPtt);
            //             Console.Out.WriteLine("txiq");
        }
        private void handleHighPriorityPacket(receivedPacket packet)
        {
            if (packet?.received == null || packet.received.Length < 1444) return;
            uint sequence = ((uint)packet.received[0] << 24) | ((uint)packet.received[1] << 16)
                | ((uint)packet.received[2] << 8) | packet.received[3];
            bool samePeer = controlPeer != null && controlPeer.Equals(packet.endPoint);
            if (controlSequenceKnown && samePeer && sequence != 0 && unchecked((int)(sequence - lastControlSequence)) <= 0) return;
            controlSequenceKnown = true; controlPeer = packet.endPoint;
            lastControlSequence = sequence; lastControlFlags = packet.received[4];
            long handledAt = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref lastControlHandled, handledAt);
            Interlocked.Exchange(ref lastControlDelayMs, packet.receivedAtTicks == 0 ? 0 : (handledAt - packet.receivedAtTicks) * 1000 / Stopwatch.Frequency);
            Monitor.Decoder.Protocol2HighPriority(packet.received);
            //         Console.Out.WriteLine("hpp");

            byte[] received = packet.received;
            bool run = ((received[4] & 0x01) != 0);
            recordingPtt = (received[4] & 3) == 3;
            Volatile.Write(ref transmitActive, recordingPtt);
            AutoRecorder.SetTransmitting(recordingPtt);
            TxSignal.SetTransmitting(recordingPtt);
            if (!recordingPtt) Recorder.Pause();
            Monitor.Session.ObserveRun(packet.endPoint, run);
            if (run != running)
            {
                if (run)
                {
                    resetTransmission();
                    running = true;
                    status = "Running";
                }
                else
                {
                    running = false;
                    status = "Off";
                }
            }
            bool ptt0 = ((received[4] & 0x02) != 0);
            if (ptt0)
            {
                //   Console.Out.WriteLine("ptt");
            }
            int rxi = 9;
            for (int i = 0; i < maxReceivers; i++)
            {
                if (receiversByIdx[i] != null)
                {
                    if (usePhaseWord)
                    {
                        //phase_word[31:0] = 2^32 * frequency(Hz)/DSP clock frequency (Hz) 

                        int phaseword = (((int)received[rxi]) << 24) + (((int)received[rxi + 1]) << 16) + (((int)received[rxi + 2]) << 8) + (int)received[rxi + 3];

                        receiversByIdx[i].vfo = (int)(phaseword * clk / 4294967296.0);
                    }
                    else receiversByIdx[i].vfo = (((int)received[rxi]) << 24) + (((int)received[rxi + 1]) << 16) + (((int)received[rxi + 2]) << 8) + (int)received[rxi + 3];

                    receiversByIdx[i].generators[0].SetDefaults(receiversByIdx[i].vfo);
                    receiversByIdx[i].generators[1].SetDefaults(receiversByIdx[i].vfo + 10000);
                }
                rxi += 4;
            }
            int txi = 329;
            if (usePhaseWord)
            {
                int phaseword = (((int)received[txi]) << 24) + (((int)received[txi + 1]) << 16) + (((int)received[txi + 2]) << 8) + (int)received[txi + 3];
                txNCO = (int)(phaseword * clk / 4294967296.0);
            }
            else txNCO = (((int)received[txi]) << 24) + (((int)received[txi + 1]) << 16) + (((int)received[txi + 2]) << 8) + (int)received[txi + 3];
        }
        void resetTransmission()
        {
            streamClock.Restart();
            actualPacketCount = 0;
            micSeqNo = 0;
            foreach (receiver rx in receivers)
            {
                rx.seq = 0;
                rx.packetCount = 0;
                rx.timebase = 0;
            }
        }
    }
    public class udpConnection
    {
        public UdpClient Client { get; set; }
        public ConcurrentQueue<receivedPacket> msgQueue { get; set; }
    }
}
