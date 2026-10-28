# Client control monitor

The emulator is based on Patroclus-p2-default-ports (Alan Hopper, MIT).
The UI, status messages and tooltips are in English.

- **Client controls / LEDs** displays received LPF, HPF/BPF, attenuation,
  antennas, PA disable, PTT and digital output commands.
- **Hardware / sample rate / OC** displays model-specific microphone options,
  requested DDC/TX sample rates and observed OC RX/TX patterns by band.
- **Receivers / signal generator** retains the original IQ signal generators.
- **Always on top** toggles the window's topmost state.
- **TX audio recording** produces playable WAV files. See [recording](tx-audio-recording.md).
- **Mute RX during TX** defaults to on: receiver IQ goes silent on PTT, while
  packet and microphone clocks continue. PTT release restores RX signals.
  Turning it off restores P2 test signals / P1 legacy TX loopback. It is
  independent of Thetis/Nereus display DUP and is not PureSignal feedback.

Green means a received active bit. LEDs start dark. Unknown and received OFF
states remain distinguishable through details/tooltips. Displays are passive;
filter or attenuation commands do not alter the simulated RF signal.

HPSDR does not transmit the application name. Client identification uses the
IP address, source UDP port, protocol and recent RUN/control traffic.

The client does not transmit its complete OC setup table. The OC band table
stores the last observed RX/TX pattern per nominal frequency band since profile
selection. Enable **Ext Control** in Thetis; setting checkboxes alone does not
activate outputs. Raw diagnostics show P1 C2 or P2 byte 1401; 0xFE decodes to
all seven OC pins ON (mask 0x7F). TX pins are reported during transmission.
Band assignment uses RX1/TX frequency in P1 and the first enabled DDC/TX0 in P2.
CTUN can make the hardware frequency differ from the client VFO display.

Only 7000/8000-family and Saturn G2 profiles show a second RX filter bank;
100D, 200D and G2E do not. Received ALEX1 raw data is not evidence of a second
physical bank. Profile identities do not implement complete model-specific RF hardware.

P1 PA disable is bank 0x12 C3 bit 7 (1 = disabled). P2 General byte 58 bit 0
has opposite polarity (0 = disabled). This matches Thetis CmdGeneral's inversion.
P1 does not explicitly report Alex presence. Hermes Lite uses different fields;
unsupported hardware options stay unknown.

The signal engine and packet timing are inherited. Reported Aether audio
dropouts have not been investigated as part of these extensions.
