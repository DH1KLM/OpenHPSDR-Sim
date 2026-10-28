# RX / TX switching investigation

2026-10-28, static comparison with the locally available Thetis and NereusSDR
sources. This is source evidence, not confirmation of the user's live settings.

## Emulator before the switching correction

In FakeHermesNewProtocol.handleComms, every running DDC unconditionally calls
rx.GenerateSignal. PTT updates recordingPtt, recording and diagnostics, but does
not alter the generated receive samples. Thus the P2 emulator continues its
receive test tones while keyed. It has no implemented RF-feedback path.

FakeHermes (P1) behaves differently: handleComms chooses TX-IQ loopback instead
of the generators while txing. That implementation repeats 48 kS/s samples for
higher receive rates and is not a complete hardware emulation.

## Thetis

`D:\GitHub\Thetis\Project Files\Source\Console\console.cs:24837` and the
PANADAPTER branch around line 24874 select the TX analyzer via
`GetPixels(cmaster.inid(1, 0), ...)` when local MOX is active and display duplex
is off. Otherwise they select an RX analyzer, subject to RX2 / TX-on-VFOB routing.
`display.cs:2499` defines that receiver-specific local MOX decision.

The P1 network duplex flag is always set by networkproto1.c (C4 |= 0x04),
even though display duplex is separately user-controlled. Consequently the
protocol duplex bit cannot be used as a reliable proxy for the DUP UI setting.

## NereusSDR

`src/gui/MoxDisplayController.cpp:450-475` explicitly keeps the receiver with
display duplex enabled and calls beginTransmitView only when it is disabled.
applyDisplayDuplex at lines 253-278 can switch those paths while keyed.
`src/gui/TxDisplaySource.cpp:45-85` connects TxDisplayFeed to the TX trace.
`src/core/TxAnalyzer.cpp:286-292` uses the local TX DSP siphon at 96 kHz.
`src/core/TxChannel.cpp:3910-3935` sends TX IQ and emits the local MON siphon.
P1RadioConnection also initializes its protocol duplex flag to true, independent
of the display setting.

## Conclusion

The observed continuing RX test signal exposes a real limitation in P2. It
does not by itself explain missing local TX speech or spectrum: both clients
have a separate TX analyzer / monitor path and a separate display-duplex choice.
Stopping all UDP traffic on PTT would be incorrect; sample clocks and required
mic/feedback streams must continue. A half-duplex simulation should replace
ordinary RX RF content with silence, while an explicit full-duplex/feedback
mode would need different routing. Muting alone is not TX speech generation.

For a live failure, distinguish: actual MOX/PTT and TX-IQ level in the emulator,
the client's DUP setting, and which VFO/pan is TX-bound. Packet-clock correctness
and microphone routing remain separate from receive signal muting.

## Implemented correction

`Mute RX during TX` defaults to enabled for P1 and P2. On PTT, P2 clears only
the receiver-IQ payload; P1 clears the sample payload of both 512-byte blocks.
Packet headers, sequence numbers, sample timing, microphone packets and control
messages remain active. Releasing PTT restores the original test generators.
The status line explicitly reports when the receiver IQ is muted.

Disabling the option restores original behavior: P2 continues its RX generators,
P1 uses the inherited TX-IQ loopback. This is separate from client display DUP
and does not implement PureSignal or a hardware RF feedback channel.

`tests/RxTxSwitching` starts the actual P1/P2 network workers on ephemeral ports
and receives their UDP output. It verifies RX signal, silent IQ while keyed,
nonzero IQ after unkeying, preserved sequence/headers, continuing P2 microphone
packets, and enabling/disabling the P2 override while keyed. Tests pass locally.
The live Thetis/Nereus speech issue still needs verification after this change.
