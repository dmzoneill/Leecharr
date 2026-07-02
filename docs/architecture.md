# Leecharr Architecture

## System Overview

Leecharr is a rich media BitTorrent and download client built on the Servarr (`*arr`) application framework (Sonarr, Radarr, Lidarr, Seedarr). It pairs a multi-threaded C# BitTorrent download engine with deep, bi-directional media metadata enrichment.

```mermaid
graph TB
    subgraph "Leecharr Application"
        subgraph "Frontend"
            UI["React 18 SPA<br/>TypeScript + Webpack 5 / Vite"]
        end

        subgraph "API Layer"
            API["REST API V1<br/>ASP.NET Core Controllers"]
            COMPAT["Download Client RPC Adapters<br/>(Deluge / qB / Transmission)"]
            SR["SignalR Hub<br/>/signalr/messages"]
        end

        subgraph "Core Domain"
            MDE["Media Enrichment & Cache Engine"]
            TE["Torrent Downloader Engine"]
            PP["Piece Picker (Rarest/Sequential/Endgame)"]
            DIO["Async Disk I/O & Cache"]
            TC["Tracker Manager (HTTP/UDP/Multi)"]
            PWP["Peer Protocol & Encryption (MSE/PE)"]
            DHT["DHT & Magnet Resolver"]
        end

        subgraph "Infrastructure"
            DB[("SQLite / PostgreSQL<br/>Dapper + FluentMigrator")]
            CFG["Configuration<br/>XML + DB"]
            CMD["Command System<br/>Worker Pool"]
            EVT["Event Aggregator"]
            SCH["Task Scheduler"]
        end
    end

    subgraph "External"
        TR["BitTorrent Trackers<br/>HTTP + UDP"]
        PE["Peers<br/>TCP + uTP"]
        DHT_NET["DHT Network"]
        ARR["Sonarr / Radarr / Lidarr<br/>API v3 + Webhooks"]
    end

    UI <-->|HTTP + WebSocket| API
    UI <-->|Real-time| SR
    API --> TE
    API --> MDE
    COMPAT --> TE
    TE --> DB
    MDE --> DB
    TE --> PP
    TE --> DIO
    TE --> TC
    TE --> PWP
    TE --> DHT
    TC --> TR
    PWP --> PE
    DHT --> DHT_NET
    MDE --> ARR
    CMD --> EVT
    SCH --> CMD
```

## Project Dependency Graph

```mermaid
graph LR
    Console[Leecharr.Console] --> Host[Leecharr.Host]
    Host --> ApiV1[Leecharr.Api.V1]
    Host --> SignalR[Leecharr.SignalR]
    ApiV1 --> Http[Leecharr.Http]
    Http --> Core[Leecharr.Core]
    SignalR --> Common[Leecharr.Common]
    Core --> Common
    Http --> Common
```

| Project | Directory | Responsibility |
| :--- | :--- | :--- |
| `Leecharr.Common` | `NzbDrone.Common/` | DI container (DryIoc), logging (NLog), HTTP utilities, disk operations, serialization, caching |
| `Leecharr.Core` | `NzbDrone.Core/` | Domain logic: torrent downloading, piece picker, async disk I/O, trackers, peers, protocols, media enrichment, *arr sync |
| `Leecharr.SignalR` | `NzbDrone.SignalR/` | SignalR hub for sub-second browser updates (progress, speed pulses, piece maps, swarms) |
| `Leecharr.Http` | `Leecharr.Http/` | REST framework, middleware, auth, versioned routing |
| `Leecharr.Api.V1` | `Leecharr.Api.V1/` | API controllers & download client compatibility RPC endpoints |
| `Leecharr.Host` | `NzbDrone.Host/` | Kestrel web server, startup pipeline, middleware registration |
| `Leecharr.Console` | `NzbDrone.Console/` | Console entry point, restart loop |
| `Leecharr.Frontend` | `Leecharr.Frontend/` | React 18 SPA (TypeScript, Webpack 5 / Vite) |

## Dependency Injection (DryIoc)

- **Interfaces** registered as **Singleton** (single instance across application lifetime).
- **Concrete classes** registered as **Transient** (new instance per resolution).
- Conventional auto-wiring (`ITorrentService` + `TorrentService` auto-matched by container scanner).

## Database Layer

Entity persistence is managed via **Dapper** with **FluentMigrator** migrations supporting SQLite (default) and PostgreSQL.
All models extend `ModelBase` (Id) or `ProviderDefinition` (Id, Name, Implementation, ConfigContract, Settings, Enable, Priority).
