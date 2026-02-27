# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build everything
dotnet build Chaos.sln

# Run server (starts SignalR on :5000, UDP voice on :9000, UDP screen share on :9001)
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
- **SignalR Hub** (`ChatHub.cs`) on TCP port 5000: text chat, channel management, voice presence tracking, screen share signaling. Text channels use SignalR groups (`text_{channelId}`); voice/screen share presence broadcasts to all clients.
- **UDP Voice Relay** (`VoiceRelay.cs`) as a BackgroundService on port 9000: receives audio packets and forwards to all other users in the same voice channel. Packet format: `[4B userId][4B channelId][audioData]`. Registration uses magic bytes `"RGST"`.
- **UDP Screen Share Relay** (`ScreenShareRelay.cs`) as a BackgroundService on port 9001: receives JPEG video frames and forwards to all other registered users in the same channel. Registration uses magic bytes `"VDEO"`.

**Chaos.Client** — WPF desktop app using MVVM:
- `MainViewModel` is the single ViewModel, owns `ChatService`, `VoiceService`, and `ScreenShareService`
- `ChatService` wraps SignalR `HubConnection` and exposes C# events
- `VoiceService` manages NAudio capture (`WaveInEvent`) and playback (`WaveOutEvent` + `BufferedWaveProvider`), plus a UDP client for sending/receiving audio
- Voice and text are independent — user can be in a voice channel while viewing any text channel
- All UI updates marshal to dispatcher via `Application.Current.Dispatcher.BeginInvoke()`

**Chaos.Shared** — DTOs (`ChannelDto`, `MessageDto`, `VoiceMemberDto`, etc.) and `ChannelType` enum, referenced by both Server and Client.

## Screen Share Protocol

Screen sharing uses the same relay pattern as voice on UDP port 9001 (`ScreenShareRelay.cs`). Users must be in a voice channel to share their screen.

**Capture:** P/Invoke GDI (`BitBlt` + `GetDesktopWindow`) — zero external dependencies. Frames are JPEG-encoded via WPF's `JpegBitmapEncoder`.

**Packet format:** `[4B userId][4B channelId][4B frameId][1B flags][payload]`. Flags: `0x00` = single frame (payload is jpegData), `0x01` = fragment (payload is `[2B chunkIdx][2B totalChunks][chunkData]`). Frames larger than 60KB are fragmented. Registration uses magic bytes `"VDEO"`, unregistration uses `"BYE!"`.

**Quality presets:** Low (480p/10fps/q40), Medium (720p/15fps/q55), High (1080p/30fps/q70).

**Signaling:** `ChatHub` tracks streaming state on `ConnectedUser.IsScreenSharing`. Events: `UserStartedScreenShare`, `UserStoppedScreenShare`. Stream viewers register with the relay to receive frames.

**UI:** "Share Screen" button in voice connection bar opens a quality picker dialog. Streaming users show a red "LIVE" badge. Viewers click the badge to watch. Stream displays inline above chat with a pop-out window option (`StreamViewerWindow`).

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
