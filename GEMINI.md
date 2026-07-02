# Leecharr &mdash; AI & Developer Guide (GEMINI.md)

## Project Overview

**Leecharr** is a high-performance BitTorrent and media downloader purpose-built for the Servarr (`*arr`) ecosystem (Sonarr, Radarr, Lidarr, Readarr, Prowlarr).

It provides:
1. **A Full-Fidelity C# BitTorrent Engine (Extensible Downloader Core):** Real multi-threaded download engine with rarest-first piece picker, sequential downloading, endgame mode, async non-blocking disk I/O with write caching, sparse file allocation, resume checkpoints, MSE/PE encryption, and BEP protocol suite (HTTP/UDP trackers, DHT, PEX, `ut_metadata`, Fast extension, LPD, uTP). Designed with an extensible `IDownloadEngine` abstraction for future protocol modules (e.g., Usenet/NZB).
2. **Deep Media Enrichment:** Automatically correlates torrents with Sonarr, Radarr, and Lidarr media libraries, fetching high-res posters, fanart backdrops, season banners, episode stills, media stream specs (4K/HDR/Atmos), overviews, ratings, and cast into a unified Servarr web interface.
3. **Pure C# Media & Container Inspector:** In-engine metadata extractor for MKV, MP4, AVI, FLAC, MP3 extracting video resolution, codecs, audio channels, HDR metadata, and subtitle tracks without external CLI dependencies.
4. **Rich REST API & Client Compatibility Layers:** Exposes a rich native REST API v1 + SignalR hub, alongside drop-in compatibility adapters for **qBittorrent WebAPI v2**, **Transmission RPC**, and **Deluge JSON-RPC** so Sonarr, Radarr, and Lidarr can connect out-of-the-box.

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

## Elaborated Technical Requirements

### 1. Extensible Downloader Core (`IDownloadEngine`)
- **Primary Provider (BitTorrent):** Full C# BitTorrent implementation (Swarm, PiecePicker, AsyncDiskEngine, Trackers, DHT, uTP, MSE/PE).
- **Future Protocol Extension:** Abstract session/task layer (`IDownloadEngine`, `IDownloadSession`, `IDownloadTask`) enabling future Usenet (NZB) or Direct HTTP/Debrid providers without altering UI or media enrichment pipelines.

### 2. Storage & Category Management
- **User-Defined Categories:** Configurable category registry (e.g. `tv`, `movies`, `music`, `anime`, custom).
- **Category Paths:** Each category defines its custom destination path (e.g. `/downloads/tv`, `/downloads/movies`).
- **Sparse Allocation (Default):** Instant, non-blocking file creation using sparse file allocation to prevent disk bottlenecking during torrent startup.
- **Seeding Rules by Category:** Custom target seed ratio and seed time per category before automatic pausing/stopping.

### 3. Deep Media Enrichment & Correlation
- **100% Exact Correlation:** Pushed downloads from Sonarr/Radarr/Lidarr contain correlation identifiers (category + download hash/transaction ID) to map 1:1 with media library entities.
- **Scene Release Name Heuristic:** Regular expression parser extracts Title, Year, Season/Episode, Resolution, Audio Codec for manual and magnet additions.
- **Artwork & Metadata Cache:** Local storage for high-res posters, banners, and fanart (`/config/MediaCache/`) with TTL management and automatic pruning upon torrent deletion.
- **Pure C# Container Inspector:** Analyzes file headers for MKV, MP4, AVI, FLAC, MP3 to extract stream metadata (resolution, video codec, HDR profiles, audio channel layouts, subtitle tracks) with zero external binary requirements.

### 4. Download Client API Compatibility Strategy
To allow Sonarr, Radarr, Lidarr, and Prowlarr to immediately connect to Leecharr:
1. **qBittorrent WebAPI v2 Adapter (`/api/v2/*`):** Primary compatibility target supporting `/api/v2/auth/login`, `/api/v2/torrents/info`, `/api/v2/torrents/add`, `/api/v2/torrents/delete`, `/api/v2/torrents/pause`, `/api/v2/torrents/resume`, `/api/v2/torrents/files`, `/api/v2/sync/maindata`.
2. **Transmission RPC Adapter (`/transmission/rpc`):** JSON-RPC adapter for universal client compatibility.
3. **Deluge JSON-RPC Adapter (`/json`):** Deluge web/daemon RPC emulation.
4. **Native Leecharr REST API v1 (`/api/v1/*`):** First-class rich REST API with media entities, artwork links, stream specs, and SignalR real-time event broadcasting.

### 5. UI Scope (MVP vs Extended)
- **MVP UI Scope:**
  - Media Poster Grid View with status overlays & badges.
  - Media Banner / Season hierarchy view.
  - High-density data table with column customizer.
  - Interactive Piece Map visualizer & Peer Swarm Inspector.
  - Torrent File Tree with selective download checkboxes and file priorities (*Skip, Low, Normal, High*).
  - Category, Speed Schedule, and Connection Settings tabs.
- **Extended Features (Post-MVP):**
  - In-browser HTML5 video/audio streaming player.
