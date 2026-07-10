# Leecharr &mdash; AI & Developer Guide (GEMINI.md)

## Project Overview

**Leecharr** is a high-performance BitTorrent and media downloader purpose-built for the Servarr (`*arr`) ecosystem (Sonarr, Radarr, Lidarr, Readarr, Prowlarr).

It provides:
1. **A Full-Fidelity C# BitTorrent Engine (Extensible Downloader Core):** Real multi-threaded download engine powered by **MonoTorrent** wrapped behind `IDownloadEngine`, featuring rarest-first piece picker, sequential downloading (head/tail priority for instant media inspection), endgame mode, async non-blocking disk I/O with dynamic write caching, sparse file allocation, resume checkpoints, MSE/PE encryption, and full BEP protocol suite (HTTP/UDP trackers, DHT, PEX, `ut_metadata`, Fast extension, LPD, uTP). Designed with an extensible `IDownloadEngine` abstraction for future protocol modules (e.g., Usenet/NZB, Debrid).
2. **Deep Media Enrichment & 100% Exact Correlation:** Automatically correlates downloads with Sonarr, Radarr, and Lidarr media libraries, fetching high-res posters, fanart backdrops, season banners, episode stills, media stream specs (4K UHD/HDR10+/Dolby Vision/Atmos), overviews, ratings, and cast into a unified Servarr web interface.
3. **Pure C# Media & Container Inspector:** In-engine metadata extractor combining **TagLibSharp** with custom EBML binary parsers for MKV, MP4, AVI, FLAC, MP3 extracting video resolution, codecs, audio channels, HDR metadata, and subtitle tracks without external CLI dependencies.
4. **Rich REST API & Client Compatibility Layers:** Exposes a native REST API v1 + SignalR hub, alongside drop-in compatibility adapters for **qBittorrent WebAPI v2**, **Transmission RPC**, and **Deluge JSON-RPC** running simultaneously on port `7889` so Sonarr, Radarr, and Lidarr connect out-of-the-box.
5. **Deluge Feature & Architecture Parity:** Full support for Deluge's 60+ status metrics, queue management policies, storage allocation modes, 24x7 3-tier speed scheduling matrix, and core plugin capabilities (Label, AutoAdd, Blocklist, Execute, Extractor, Scheduler, Stats).
6. **Direct Indexer Support & Integrated Search:** Native Torznab and Newznab client integration with multi-indexer search, Freeleech filtering, one-click grab, background RSS sync rules, and dynamic Prowlarr auto-synchronization.
7. **Network & VPN Safety:** Network interface binding with an automated VPN Kill Switch (`tun0`, `wg0`) and global SOCKS5/HTTP proxy support with Anonymous mode.

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

# Run backend console host (Port 7889)
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
│   ├── architecture.md              # System design, DI, request pipelines, libraries
│   ├── domain-model.md              # ER diagrams, torrent state lifecycle
│   ├── media-enrichment.md          # Sonarr/Radarr/Lidarr matching & asset cache
│   ├── protocols.md                 # BitTorrent BEPs, MSE/PE, uTP, DHT, VPN Kill Switch
│   ├── deluge-requirements.md       # Deluge architecture, status keys, plugins & RPC
│   ├── webhooks.md                  # Servarr webhook triggers, payload schemas & delivery
│   ├── indexers.md                  # Torznab/Newznab indexer client, search & Prowlarr sync
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
- **Primary Provider (BitTorrent / MonoTorrent):** Powered by **MonoTorrent** wrapped behind `IDownloadEngine`, providing complete, battle-tested BEP compliance (Swarm, PiecePicker, Trackers, DHT, uTP LEDBAT, MSE/PE, Fast extension, PEX).
- **Future Protocol Extension:** Abstract session/task layer (`IDownloadEngine`, `IDownloadSession`, `IDownloadTask`) enabling future Usenet (NZB) or Direct HTTP/Debrid providers without altering UI or media enrichment pipelines.
- **Unified Multi-Protocol Queue:** Single download queue presenting media cards, progress, and protocol badges (`Torrent`, `Usenet`, `Debrid`).

### 2. Storage & Category Management
- **Incomplete vs Completed Directory Architecture:** Torrents download into a dedicated temporary/incomplete folder (`/downloads/incomplete`) with sparse file allocation and are automatically moved to their designated category destination path (e.g., `/downloads/tv`, `/downloads/movies`) upon 100% download verification.
- **User-Defined Categories / Labels:** Configurable category registry (e.g. `tv`, `movies`, `music`, `anime`, custom).
- **Sparse Allocation (Default):** Instant, non-blocking file creation using sparse file allocation to prevent disk bottlenecking during torrent startup. Full pre-allocation option available.
- **Dynamic Write Cache:** Scales dynamically from 128 MB up to 1 GB based on system RAM and download speed with dirty block coalescing.
- **Seeding Rules & Automation:** When a torrent reaches its category target seed ratio or seed time limit, it automatically pauses/stops seeding and fires an `OnSeedGoalReached` webhook notification for external automations while preserving data on disk for Servarr imports.

### 3. Deep Media Enrichment & Correlation
- **100% Exact Correlation:** Pushed downloads from Sonarr/Radarr/Lidarr contain correlation identifiers (category + download hash/transaction ID) to map 1:1 with media library entities.
- **TagLib# & Pure EBML Inspection:** Combines **TagLibSharp** with custom EBML binary parsers for MKV, MP4, AVI, FLAC, MP3 extracting video resolution, codecs, audio channels, HDR metadata (Dolby Vision, HDR10+), and subtitle tracks with zero external binary requirements.
- **Artwork & Metadata Cache:** Local storage for high-res posters, banners, and fanart (`/config/MediaCache/`) with immediate automated cleanup upon torrent deletion.

### 4. Download Client API Compatibility Strategy
To allow Sonarr, Radarr, Lidarr, and Prowlarr to immediately connect to Leecharr out-of-the-box, all compatibility adapters are **active simultaneously on the main port (`7889`)**:
1. **qBittorrent WebAPI v2 Adapter (`/api/v2/*`):** Primary compatibility target supporting `/api/v2/auth/login`, `/api/v2/torrents/info`, `/api/v2/torrents/add`, `/api/v2/torrents/delete`, `/api/v2/torrents/pause`, `/api/v2/torrents/resume`, `/api/v2/torrents/files`, `/api/v2/sync/maindata`.
2. **Deluge JSON-RPC Adapter (`/json`):** Full compatibility supporting `auth.login`, `core.get_torrents_status`, `core.add_torrent_file`, `core.add_torrent_magnet`, `core.remove_torrent`, `core.pause_torrent`, `core.resume_torrent`, `core.set_torrent_options`, `core.get_config`, `core.get_filter_tree`, `web.get_torrents_status`. (See [docs/deluge-requirements.md](file:///home/daoneill/src/usr/seedarr/Leecher/docs/deluge-requirements.md)).
3. **Transmission RPC Adapter (`/transmission/rpc`):** JSON-RPC adapter for universal client compatibility.
4. **Native Leecharr REST API v1 (`/api/v1/*`):** First-class rich REST API with media entities, artwork links, stream specs, and SignalR real-time event broadcasting.

### 5. Deluge Feature & Plugin Parity
Leecharr provides direct parity with the core capabilities of major Deluge plugins:
- **AutoAdd:** Single global watch folder for `.torrent` files with automated category/path mapping via scene release regex matching.
- **Blocklist & GeoIP:** IP blocklist filtering and **MaxMind.GeoIP2** integration with automated monthly database refresh for peer country flags in the swarm inspector.
- **Execute:** Post-event webhooks and custom local shell script execution (`.sh`, `.py`, `.bat`) with `TORRENT_ID`, `TORRENT_NAME`, `TORRENT_PATH`, `TORRENT_CATEGORY`, `TORRENT_INFOHASH` environment variables.
- **Extractor:** Optional auto-extraction of compressed archives (rar/zip/7z) via **SharpCompress** upon download completion (disabled by default; preserves archive files for seeding).
- **Scheduler:** 24x7 hourly 3-tier speed throttling schedule (Normal, Throttled, Paused/Suspended).
- **Stats:** Circular buffer metrics for bandwidth, cache efficiency, and swarm health.

### 6. Webhook & Notification Connection System
Full Servarr-standard notification and webhook connection support (See [docs/webhooks.md](file:///home/daoneill/src/usr/seedarr/Leecher/docs/webhooks.md)):
- **Configurable Event Triggers:** `OnGrab` (download added), `OnDownloadComplete` (finished downloading), `OnMediaInspected` (specs parsed), `OnExtractComplete` (archive unpacked), `OnSeedGoalReached` (ratio/time met), `OnTorrentDeleted`, `OnHealthIssue` (tracker error, disk full), `OnHealthRestored`, `OnManualInteractionRequired` (stalled), `OnApplicationUpdate`, `OnTest`.
- **Rich JSON Payloads:** Dispatches complete torrent metadata, enriched Sonarr/Radarr/Lidarr titles, season/episode details, 4K/HDR/Atmos stream specifications, file trees, and artwork URLs.
- **Reliable Dispatch Pipeline:** Polly resilience with exponential backoff (2s, 4s, 8s), 10s timeouts, SSRF safe URL validation, and HTTP Basic Authentication / custom headers.

### 7. Direct Indexer Support & Integrated Search
Native Torznab and Newznab client integration with interactive discovery UI (See [docs/indexers.md](file:///home/daoneill/src/usr/seedarr/Leecher/docs/indexers.md)):
- **Multi-Protocol Indexer Providers:** Support for Torznab (BitTorrent) and Newznab (Usenet) indexer endpoints (`t=caps`, `t=search`, `t=tvsearch`, `t=movie`).
- **Prowlarr Dynamic Synchronization:** Automatically synchronizes and imports configured indexers directly from a linked Prowlarr instance on startup, hourly schedule, and via webhook.
- **Interactive Search & Browse:** Full-text multi-indexer search with category chips, min-seeder filtering, and Freeleech badges (`downloadvolumefactor == 0`).
- **Automated RSS Grab Rules:** Background RSS polling with regex matchers, minimum seed thresholds, and size filters.
- **One-Click Grab:** Direct addition of search results into download queue with pre-mapped categories and pre-download file selection.

### 8. Network, VPN & Proxy Security
- **Network Interface Binding & Kill Switch:** Ability to bind BitTorrent sockets to specific network interfaces (`tun0`, `wg0`), immediately halting all socket and tracker traffic if the interface drops.
- **SOCKS5 / HTTP Proxy:** Support for proxying peer transfers, tracker announces, and indexer requests + Anonymous mode.
- **Strict BEP 27 Private Tracker Policy:** Automatically disables DHT, PEX, and LPD for private torrents with customizable client emulation presets (`Leecharr`, `qBittorrent`, `Deluge`, `Transmission`).
- **Authentication Modes:** Standard Servarr Authentication (`None`, `DisabledForLocalAddresses`, `Forms`, `Basic`, `External`).

### 9. Bandwidth Limiting Hierarchy & Queueing
- **4-Level Hierarchy:** Global Limits &rarr; 24x7 Weekly Speed Schedule &rarr; Category Limits &rarr; Per-Torrent Overrides.
- **Queue Priority:** Interactive drag-and-drop manual queue reordering with category-based priority weighting.

### 10. UI Scope (MVP vs Extended)
- **MVP UI Scope:**
  - Media Poster Grid View with status overlays & badges.
  - Media Banner / Season hierarchy view.
  - High-density data table with column customizer.
  - Interactive Piece Map visualizer & Peer Swarm Inspector (Country flags, Client icons, Protocol badges [TCP/uTP], Encryption status [RC4], and Peer Flags [D/U/S/I/C/H/E/P]).
  - Torrent File Tree with selective download checkboxes and file priorities (*Skip, Low, Normal, High*).
  - Direct Indexer Search & Browse modal with one-click download.
  - Category, Speed Schedule, Webhook Connections, Indexer, and Network Settings tabs.
- **Extended Features (Post-MVP):**
  - In-browser HTML5 video/audio streaming player.
