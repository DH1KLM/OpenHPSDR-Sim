# Validation

2026-10-27: application build succeeds. All five test programs pass:

- ControlMonitor: 1898 checks for P1/P2 decoding, PA polarity, OC patterns,
  sample rates, microphone flags, client state and malformed input.
- Protocol2Ports: port defaults, production handler integration, model-specific
  filter controls and TX audio recording sample counts.
- RadioProfiles: 34 device identities and discovery responses.
- PacketTiming: 54 rate/packet combinations over 60 seconds of simulated time.
- TxAudio: WAV headers/duration, SSB/AM/FM tone recovery, DC rejection, PTT and restart.
  Automatic recording: default OFF, repeated PTT controls, one file per burst,
  arm during TX, disarm/finalization, USB/LSB and date/time/mode filenames.
  Production P1/P2 handlers were verified, including a PTT falling edge between
  the two blocks of one P1 datagram (first TX block must still be recorded).

The user confirmed OC response and corrected PA-disable behavior in live use.
The latest English UI and live microphone recording have not yet been visually
or audibly verified. Build warnings remain for the inherited .NET 6 framework
and legacy code; NuGet advisory lookup was unavailable in the local sandbox.
