# Aether RX interruptions during discovery

The user observed alternating `P2 Alex 0 enabled` and `HF PA disabled` states
coinciding with interruptions during reception. Microphone and PTT were not involved.

The local Aether source sends P1 and P2 discovery probes during each discovery
sweep (`AnanDiscovery::sweepNow`). The P1 probe is 63 bytes, starts EF FE 02,
and otherwise contains zeroes (`p1::discoveryRequest`). Its byte 4 is therefore
zero, just like a P2 General command. Aether also refreshes its real P2 General
configuration every 500 ms (`P2Client::takeGeneralRefresh`).

The previous emulator accepted any packet of at least 60 bytes with byte 4 zero
as General. A P1 probe consequently cleared Alex enables, indicated PA disabled,
and replaced the active stream destination with the discovery socket endpoint.
The next genuine General refresh restored the configuration and destination.
Additionally, P2 discovery probes replaced the stream destination and reset the
outgoing high-priority status sequence. Both discovery paths could interrupt RX.

The correction:

- Require exactly 60 bytes for P2 General/discovery and reject the P1 signature.
- Apply the same validation in the passive General decoder.
- Reply to P2 discovery without changing the active stream destination or sequence.
- Advertise discovery status 3 while running, 2 while idle.

Validation: `tests/Protocol2Ports` checks rejection of 63-byte and 60-byte P1
probes and oversized packets, preserving PA/Alex state and stream destination.
`tests/RxTxSwitching` sends repeated P1/P2 probes through real local UDP sockets
during RX, verifies running discovery replies and continued IQ/microphone delivery
to the original client, no stream packets to the discovery socket, and no status
sequence reset. Both test programs passed. The user confirmed on 2026-10-28
that the original interruptions were no longer visible or audible with Aether.

## Separate observation under PC load

The user subsequently reported brief audible artifacts and S-meter disturbances
when starting other programs/windows or during background work; Thetis did not
show the same behavior. This is separate from the confirmed discovery defect.

Source inspection shows that the emulator's P2 communication worker uses normal
thread priority and Sleep(1), then catches up overdue IQ in batches of at most
16 packets. Its signal conversion also allocates a new double array per packet.
These are potential contributors to scheduling jitter and GC pauses, not proof
of the observed cause. The P1 worker already uses AboveNormal priority.

The local Thetis ChannelMaster network.c ReadThreadMain registers its receive
thread with Windows MMCSS as Pro Audio (fallback: TIME_CRITICAL). The local Aether
P2 path detects sequence gaps and passes them to the DSP; a gap resets the automatic
AGC floor estimator and discards any staged partial spectrum frame. This does not
establish that a sequence gap actually occurred during the reported artifacts.

No live packet timing/loss trace has yet been collected for this second issue.
The distinction to measure is delayed but contiguous packets versus actual missing
sequence numbers versus a client-side DSP/audio scheduling stall. The S-meter
observation alone does not distinguish these cases. Initial source inspection
was followed by the targeted scheduling and allocation changes below.

## P1 stops after a few seconds

The user's P1 comparison also showed artifacts, followed after 3–5 seconds by a
frozen panadapter and packet loss diagnostics. Source inspection found that P1
discovery reset both outgoing IQ and bandscope sequences to 1 during an active
stream. Aether's P1Client discards backward/duplicate sequence numbers without
advancing its expected sequence. Repeated discovery can therefore keep IQ below
that expectation indefinitely. This provides a concrete explanation for the
freeze, distinct from the still-unmeasured artifacts under load.

P1 discovery now preserves both sequences, the stream peer and running status,
and replies with running status 3 when appropriate. The receive loop also retains
the actual datagram length instead of stale bytes in reused buffers; incomplete
EP2 frames are ignored. The real UDP regression test sends repeated P1 discovery
and truncated EP2 packets while checking continued consecutive IQ packets to the
original endpoint. The user confirmed that this P1 freeze was fixed; brief load-related
artifacts remained in both protocols.

## Load-related streaming revision

Both communication workers now register with Windows MMCSS `Pro Audio` at high
multimedia priority and request 1 ms timer resolution for their lifetime. If MMCSS
registration/priority fails, the worker uses AboveNormal thread priority. The prior
priority, timer request and MMCSS registration are released when the worker exits.
Non-Windows systems retain their default scheduling. This follows the scheduling
approach used by the local Thetis ChannelMaster receive thread; it does not change
the priority of the entire process.

P1 and P2 signal mixing now reuse cleared buffers rather than allocating a double
array for every IQ block. Regression tests verify identical IQ after reuse and
block-size changes; actual local UDP P1/P2 reception, discovery and PTT tests pass.

The UI `Stream timing` diagnostic shows the scheduling mode, maximum communication
loop gap during RUN, number of loops over 10 ms, and maximum IQ catch-up duration.
Counters cover the selected radio instance's lifetime. Catch-up excludes the first
due packet, since a single pending packet is normal. These are sender-side timing
observations, not a network packet-loss measurement. Correlate increases with the
audible artifacts and Aether's own loss counter. Late packets are still delivered
with continuous sample/sequence progression; no backlog is deliberately discarded.
Live confirmation of whether this revision reduces the remaining artifacts is pending.

The user reported that the artifacts still occurred after this revision. Increasing
sender scheduling priority and reducing allocation therefore did not resolve the issue.

Further source inspection found a receiver-side difference: Aether's P1Client and
P2Client create/bind QUdpSocket without requesting ReceiveBufferSizeSocketOption,
and AnanBackend puts the UDP client and DSP on the same thread with a direct IQ
connection. DSP work delays draining the socket. Thetis network.c requests
0xfa000 (1,024,000) bytes for SO_RCVBUF. This is a candidate explanation for different
resilience to scheduling stalls and sender bursts, not a measured buffer overflow.

The available Aether log aethersdr-20261028-132311.log records P1 at 192 ksps with
five frame slots. This geometry gives 30 samples per 1032-byte datagram, 6400 IQ
datagrams/s, or 6,604,800 payload bytes/s. For illustration only, a 64 KiB receive
buffer holds less than 10 ms at that rate before accounting for datagram overhead;
the active socket's granted buffer size has not been measured. The log also reports
main-thread watchdog stalls, but those do not prove that its separate I/O thread
stalled at the same instant. Correlation with sender timing and live loss counters
is still required. No Aether source changes have been made.
