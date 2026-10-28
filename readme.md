# OpenHPSDR-Sim

HPSDR-Netzwerkemulator auf Basis von Patroclus-p2-default-ports (Alan Hopper, MIT).
Unterstützt Protocol 1 und Protocol 2, synthetische IQ-Signale und einen passiven
Monitor für vom Client gesendete Betriebsparameter.
Die Oberfläche ist englisch. TX-Sprache lässt sich als WAV aufnehmen: Modus
SSB/AM/FM wählen, **Record WAV…**, senden, anschließend **Stop**.
[Hinweise zur Aufnahme](docs/tx-audio-recording.md).
Optional startet **Auto record TX** pro PTT-Sendung automatisch eine WAV-Datei
mit Datum, Uhrzeit und ausgewählter Betriebsart. Die Automatik ist beim Start aus.

## Starten

`Start-OpenHPSDR-Sim.cmd` startet die aktuelle Anwendung unter `run`.
Dafür wird die .NET-6-Laufzeit benötigt. Nur eine Emulatorinstanz gleichzeitig
starten (UDP-Port 1024). „Immer im Vordergrund“ ist oben rechts schaltbar.

## Entwickeln

- `Patroclus.Avalonia/`: aktive Anwendung; Projektname und Namespaces bleiben für
  Ressourcen- und XAML-Kompatibilität erhalten.
- `tests/`: Prüfprogramme für Decoder, Hardwareprofile, P2-Ports und Paket-Timing.
- `docs/`: Funktionsbeschreibung und Prüfstand.
- `run/`: aktuelle ausführbare Windows-x64-Version einschließlich benötigter Bibliotheken.

Solution: `OpenHPSDR-Sim.sln`.

Build der startbaren Anwendung:

```powershell
dotnet publish Patroclus.Avalonia/Patroclus.Avalonia.csproj -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o ./run
```

Prüfungen:

```powershell
dotnet run --project tests/ControlMonitor/ControlMonitor.csproj -c Release
dotnet run --project tests/Protocol2Ports/Protocol2Ports.csproj -c Release
dotnet run --project tests/RadioProfiles/RadioProfiles.csproj -c Release
dotnet run --project tests/PacketTiming/PacketTiming.csproj -c Release
dotnet run --project tests/TxAudio/TxAudio.csproj -c Release
dotnet run --project tests/RxTxSwitching/RxTxSwitching.csproj -c Release
```

[Funktionsbeschreibung und Grenzen](docs/client-control-monitor.md) ·
[Prüfstand](docs/validation-client-monitor.md) · [MIT-Lizenz](LICENSE.txt)

Die OC-Bandtabelle enthält beobachtete Zustände, keine vollständige Kopie der
Client-Konfiguration. Die Untersuchung der Aether-Aussetzer ist nicht Bestandteil
der bisherigen Erweiterungen.
