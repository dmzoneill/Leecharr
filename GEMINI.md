# Leecharr &mdash; AI & Developer Guide (GEMINI.md)

## Project Overview

**Leecharr** is a high-performance BitTorrent and media downloader purpose-built for the Servarr (`*arr`) ecosystem (Sonarr, Radarr, Lidarr, Readarr, Prowlarr).

It provides:
1. **A Full-Fidelity C# BitTorrent Engine:** Real multi-threaded download engine with rarest-first piece picker, sequential download for instant media streaming, endgame mode, async non-blocking disk I/O with write caching, sparse file allocation, resume checkpoints, MSE/PE encryption, and BEP protocol suite (HTTP/UDP trackers, DHT, PEX, `ut_metadata`, Fast extension, LPD, uTP).
2. **Deep Media Enrichment:** Automatically correlates torrents with Sonarr, Radarr, and Lidarr media libraries, fetching high-res posters, fanart backdrops, season banners, episode stills, media stream specs (4K/HDR/Atmos), overviews, ratings, and cast into a unified Servarr web interface.
3. **Download Client Compatibility Layers:** Exposes Deluge JSON-RPC, qBittorrent WebAPI v2, and Transmission RPC adapters alongside native REST API v1 so existing applications can connect without modification.

---

## Build & Test Commands

### Prerequisites
- .NET 10 SDK (`dotnet --version` &ge; 10.0.100)
- Node.js 20+ & npm (`node -v` &ge; 20.x, `npm -v` &ge; 10.x)
- Make (optional, for running Makefile targets)

### Core Build Commands
```bash
# Restore NuGet packages
dotnet restore src/Leecharr.sln

# Build entire solution (Release configuration)
dotnet build src/Leecharr.sln -c Release

# Run backend console host
dotnet run --project src/NzbDrone.Console/Leecharr.Console.csproj

# Run backend with debug logging
dotnet run --project src/NzbDrone.Console/Leecharr.Console.csproj -- --log-level=debug
```

### Test Commands
```bash
# Run all unit tests
dotnet test src/Leecharr.sln -c Release --no-build

# Run specific test class
dotnet test src/Leecharr.sln --filter "FullyQualifiedName~PiecePickerTest"

# Run tests with code coverage collection
dotnet test src/Leecharr.sln --collect:"XPlat Code Coverage"
```

### Makefile Shortcuts
```bash
make setup          # Restore .NET and frontend npm dependencies
make test-setup     # Build solution (Release)
make test           # Run unit tests with coverage
make build          # setup + test-setup
make publish        # Publish standalone binaries to _output/
make frontend       # Build frontend production bundle
make clean          # Clean build artifacts (_temp, _output, _tests)
```

---

## Repository & Project Architecture

```
Leecher/
├── GEMINI.md                        # This developer & AI guide
├── README.md                        # Project landing page
├── Makefile                         # Build & test automation targets
├── Containerfile                    # Multi-stage container build
├── version                          # Semantic version string
├── docs/                            # Deep architectural & protocol specifications
│   ├── architecture.md              # System design, DI, request pipelines
│   ├── domain-model.md              # ER diagrams, torrent state lifecycle
│   ├── media-enrichment.md          # Sonarr/Radarr/Lidarr matching & asset cache
│   ├── protocols.md                 # BitTorrent BEPs, MSE/PE, uTP, DHT
│   ├── api.md                       # REST API v1 & RPC compatibility specs
│   └── development.md               # Local development setup
└── src/
    ├── Directory.Build.props        # Centralized build properties & analyzer rules
    ├── stylecop.json                # StyleCop analyzer settings
    ├── Leecharr.sln                 # .NET 10 solution file
    ├── NzbDrone.Common/             # Shared utilities (DryIoc DI, NLog, HTTP, disk)
    ├── NzbDrone.Core/               # Base *arr framework (Datastore, migrations, commands, tasks)
    ├── Leecharr.Core/               # Torrent engine, piece picker, async disk I/O, media enrichment
    ├── NzbDrone.SignalR/            # SignalR real-time messaging hub (/signalr/messages)
    ├── Leecharr.Http/               # REST framework, auth middleware, model binders
    ├── Leecharr.Api.V1/             # REST controllers & RPC compatibility adapters
    ├── NzbDrone.Host/               # Kestrel startup pipeline & middleware configuration
    ├── NzbDrone.Console/            # Console application entry point & restart loop
    ├── Leecharr.Frontend/           # React 18 SPA (TypeScript 5, Webpack 5 / Vite)
    └── Leecharr.Core.Test/          # NUnit 4.6 unit tests
```

---

## Architectural Conventions & Rules

### 1. NzbDrone / Servarr Pattern
- Keep directory names (`NzbDrone.Common`, `NzbDrone.Core`, `NzbDrone.Host`, `NzbDrone.Console`, `NzbDrone.SignalR`) for standard framework layers, and `Leecharr.*` for application-specific layers.
- `Directory.Build.props` dynamically maps root namespaces to project directories.

### 2. Dependency Injection (DryIoc)
- Interfaces (`IFooService`) are registered as **Singleton** (one instance per application lifetime).
- Concrete implementations (`FooService`) are resolved as **Transient** (new instance per resolution).
- Auto-registration: DryIoc scans loaded assemblies and matches `IFooService` &rarr; `FooService` automatically without manual registration code.

### 3. Database Layer & Migrations
- Lightweight ORM using **Dapper**.
- Migrations managed via **FluentMigrator** for SQLite (default) and PostgreSQL.
- Migrations are sequentially numbered (`001_initial_setup.cs`, `002_add_torrents.cs`).
- **CRITICAL:** Existing migrations must never be modified once created. New columns or tables require a new migration file.
- Persistent models inherit from `ModelBase` (`Id` PK) or `ProviderDefinition` (for pluggable provider settings).

### 4. Asynchronous Commands & Events
- Use `IExecute<TCommand>` for background work triggered by API, scheduler, or internal events.
- Use `IHandle<TEvent>` and `IEventAggregator.PublishEvent` to publish and handle decoupled events.
- Long-running operations should be handled via the 3-worker Command Queue.

### 5. Real-Time SignalR Messaging
- Single hub endpoint: `/signalr/messages`.
- Event name on client: `receiveMessage`.
- Format: `{ "name": "<resourceName>", "body": <payloadObject> }`.
- RestControllers inherit `RestControllerWithSignalR<TResource, TModel>` to automatically broadcast CRUD events.
- High-frequency speed pulses and piece map bitmap changes use dedicated lightweight events (`speedPulse`, `pieceMapUpdated`).

### 6. Code Style & Analyzers
- StyleCop.Analyzers is enabled with `TreatWarningsAsErrors=true` and `EnforceCodeStyleInBuild=true`.
- Always put `System.*` using directives first, outside the namespace declaration.
- Prefer `var` when type is apparent.
- Async methods must end in `Async` if public library API, or follow standard Servarr async patterns.

---

## Requirements Capture & Elaboration Areas

When elaborating requirements with the user, focus on the following core domains:

### Area 1: Torrent Download Engine Mechanics
- Piece picker algorithms (Rarest-first calculation, sequential buffer sizing for streaming, endgame trigger thresholds).
- Disk caching strategy (Fixed vs dynamic memory buffer, flush intervals, sparse allocation vs fallocate).
- Fast-resume serialization format and checkpoint frequency.
- Bitfield and piece availability tracking in multi-peer swarms.

### Area 2: Media Enrichment & *arr Integration
- Sonarr/Radarr/Lidarr API v3 sync schedule & webhook payload handling.
- Release title regex parsing rules and media matching heuristics.
- Local thumbnail and artwork caching policies (sizes, formats, cleanup rules).
- Season pack file hierarchy mapping (parsing season/episode file trees).
- Media stream info extraction (FFprobe / MediaInfo integration or pure C# container header parsing).

### Area 3: Protocol & Network Features
- MSE/PE stream encryption settings and negotiation fallback.
- BEP 29 (uTP) implementation details and LEDBAT congestion tuning.
- BEP 5 (DHT) bootstrap nodes, K-bucket persistence, and token rotation.
- Proxy support (SOCKS5, HTTP proxy) and private tracker protection.

### Area 4: Compatibility Adapters & APIs
- Deluge JSON-RPC method subset needed for full Sonarr/Radarr/Prowlarr compatibility.
- qBittorrent WebAPI v2 endpoint coverage.
- Transmission RPC command mappings.
- Custom Leecharr REST API v1 endpoints and schema.

### Area 5: User Interface & Experience
- Media poster grid view card layout, badges, and progress animations.
- Interactive piece map visualizer (dynamic canvas vs high-density SVG/grid).
- Peer swarm map with GeoIP country flags and client identification icons.
- Torrent file inspector with selective download checkboxes, priorities, and "Stream Now" preview player.
