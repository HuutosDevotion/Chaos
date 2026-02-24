# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build everything
dotnet build Chaos.sln

# Run server (starts SignalR on :5000, UDP voice on :9000)
dotnet run --project src/Chaos.Server

# Run client (Windows only - WPF)
dotnet run --project src/Chaos.Client

# Build only client (useful when server is running and locks Shared DLL)
dotnet build src/Chaos.Client/Chaos.Client.csproj

# Publish self-contained single-file executables
dotnet publish src/Chaos.Server/Chaos.Server.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/Server
dotnet publish src/Chaos.Client/Chaos.Client.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/Client
```

**Note:** WPF template requires `--framework net8.0` (not `net8.0-windows`) when scaffolding new projects.

## Architecture

Three projects targeting net8.0, connected via shared DTOs:

**Chaos.Server** — ASP.NET Core host running two parallel communication layers:
- **SignalR Hub** (`ChatHub.cs`) on TCP port 5000: text chat, channel management, voice presence tracking. Text channels use SignalR groups (`text_{channelId}`); voice presence broadcasts to all clients.
- **UDP Voice Relay** (`VoiceRelay.cs`) as a BackgroundService on port 9000: receives audio packets and forwards to all other users in the same voice channel. Packet format: `[4B userId][4B channelId][audioData]`. Registration uses magic bytes `"RGST"`.

**Chaos.Client** — WPF desktop app using MVVM:
- `MainViewModel` is the single ViewModel, owns `ChatService` and `VoiceService`
- `ChatService` wraps SignalR `HubConnection` and exposes C# events
- `VoiceService` manages NAudio capture (`WaveInEvent`) and playback (`WaveOutEvent` + `BufferedWaveProvider`), plus a UDP client for sending/receiving audio
- Voice and text are independent — user can be in a voice channel while viewing any text channel
- All UI updates marshal to dispatcher via `Application.Current.Dispatcher.BeginInvoke()`

**Chaos.Shared** — DTOs (`ChannelDto`, `MessageDto`, `VoiceMemberDto`, etc.) and `ChannelType` enum, referenced by both Server and Client.

## Voice Protocol

Audio format: raw PCM, 16kHz sample rate, 16-bit, mono. 40ms buffer per frame.

Each client generates a random `voiceUserId` (1-100000) on startup. This ID is sent both via SignalR (for username↔ID mapping) and embedded in UDP packet headers (for routing). Speaking detection: peak sample level > 0.02f threshold.

Client resolves server hostname preferring IPv4 — the UDP socket is created matching the resolved address family. The server catches `SocketException.ConnectionReset` (Windows ICMP port unreachable) when clients disconnect.

## Database

SQLite via EF Core, file `chaos.db` in server working directory. Uses `EnsureCreated()` (no migrations). Seed data: "general" (text), "random" (text), "Voice Chat" (voice).

## Key Conventions

- `RelayCommand` implements `ICommand` with `CommandManager.RequerySuggested` for auto-invalidation
- WPF converters in `Converters/` folder for bool↔visibility and channel type→icon
- Dark theme colors defined as static resources in `App.xaml` (Discord-style palette)
- No authentication — users pick a username on connect
- Server CORS is wide open (AllowAnyOrigin)
