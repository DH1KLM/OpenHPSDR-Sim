using System;
using System.Diagnostics;
using System.Net;

namespace Patroclus.Avalonia.ViewModels
{
    public sealed record ClientConnection(string Endpoint, string Status, bool Active);

    // UDP has no connection handshake or client application name. Observe RUN and
    // incoming control traffic; silence is reported without stopping the emulator.
    public sealed class ClientSession
    {
        private readonly object gate = new();
        private readonly Func<TimeSpan> clock;
        private IPEndPoint endpoint;
        private TimeSpan lastSeen;
        private bool running;
        public ClientSession(Func<TimeSpan> clock = null)
        {
            var stopwatch = Stopwatch.StartNew();
            this.clock = clock ?? (() => stopwatch.Elapsed);
        }
        public void ObserveRun(IPEndPoint sender, bool run)
        {
            if (sender == null) return;
            lock (gate)
            {
                if (run)
                    endpoint = new IPEndPoint(sender.Address, sender.Port);
                else if (endpoint == null) return;
                running = run;
                lastSeen = clock();
            }
        }
        public void Touch(IPEndPoint sender)
        {
            lock (gate)
                if (endpoint != null && endpoint.Equals(sender) && running) lastSeen = clock();
        }
        public ClientConnection Snapshot()
        {
            lock (gate)
            {
                if (endpoint == null) return new ClientConnection("No client", "Waiting for a start command", false);
                if (!running) return new ClientConnection(endpoint.ToString(), "Stopped · last client", false);
                bool active = clock() - lastSeen < TimeSpan.FromSeconds(5);
                return new ClientConnection(endpoint.ToString(), active ? "Active" : "No client traffic for at least 5 s", active);
            }
        }
    }
}

