# Leecharr

**High-Performance BitTorrent & Media Downloader** &mdash; purpose-built for the Servarr (\*arr) ecosystem.

[![CI/CD](https://github.com/dmzoneill/Leecharr/workflows/CICD/badge.svg)](https://github.com/dmzoneill/Leecharr/actions)
[![GitHub Release](https://img.shields.io/github/v/release/dmzoneill/Leecharr?color=brightgreen)](https://github.com/dmzoneill/Leecharr/releases/latest)
[![License](https://img.shields.io/github/license/dmzoneill/Leecharr?color=blue)](https://github.com/dmzoneill/Leecharr/blob/main/LICENSE)

## What is Leecharr?

**Leecharr** is a high-performance BitTorrent and media downloader purpose-built for the Servarr (`*arr`) ecosystem (Sonarr, Radarr, Lidarr, Prowlarr, Readarr).

Unlike conventional standalone clients that treat downloads as raw filenames and technical progress bars, Leecharr **deeply enriches active downloads with metadata, artwork, and stream specifications** directly from Sonarr, Radarr, and Lidarr &mdash; providing high-res movie posters, TV show banners, season hierarchy, episode stills, 4K UHD/HDR10+/Dolby Vision/Atmos stream details, and cast overviews in a unified Servarr dark interface.

## Quick Start

```bash
docker run -d \
  --name leecharr \
  -p 7889:7889 \
  -v leecharr-config:/config \
  -v leecharr-downloads:/downloads \
  --restart unless-stopped \
  feeditout/leecharr:latest
```

Open **<http://localhost:7889>**

## Docker Compose / Podman Compose

```yaml
services:
  leecharr:
    image: feeditout/leecharr:latest
    container_name: leecharr
    ports:
      - "7889:7889"
    volumes:
      - leecharr-config:/config
      - leecharr-downloads:/downloads
    restart: unless-stopped
    environment:
      - TZ=UTC
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7889/api/v1/system/status"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 30s

volumes:
  leecharr-config:
  leecharr-downloads:
```

```bash
docker compose up -d
# or
podman-compose up -d
```

## Volumes

| Path         | Purpose                                      |
| ------------ | -------------------------------------------- |
| `/config`    | Database, settings, media cache, logs        |
| `/downloads` | Downloaded media files, torrent data payload |

## Environment

| Variable             | Default   | Description      |
| -------------------- | --------- | ---------------- |
| `LEECHARR__APP_DATA` | `/config` | Config directory |
| `TZ`                 | `UTC`     | Timezone         |

## Ports

| Port   | Purpose                                                                   |
| ------ | ------------------------------------------------------------------------- |
| `7889` | Web UI + REST API v1 + qBittorrent WebAPI + Deluge RPC + Transmission RPC |

## Key Features

- *_🌟 Deep *arr Media Enrichment*_ &mdash; 100% exact correlation with Sonarr, Radarr, and Lidarr libraries; displays high-res posters, season banners, episode screenshots, audio codecs (Dolby Atmos, TrueHD, FLAC), HDR metadata (Dolby Vision, HDR10+), and cast overviews.
- **⚡ Pure C# .NET 10 BitTorrent Engine** &mdash; full-fidelity downloader powered by MonoTorrent with rarest-first piece picker, sequential download mode (head/tail priority for instant media inspection/streaming), endgame mode, and non-blocking async disk I/O with dynamic write cache.
- **🔌 Drop-In Client API Compatibility** &mdash; simultaneous support on port `7889` for:
  - **qBittorrent WebAPI v2** (`/api/v2/*`)
  - **Deluge JSON-RPC** (`/json`)
  - **Transmission RPC** (`/transmission/rpc`)
  - **Native REST API v1 & SignalR Hub** (`/api/v1/*`, `/signalr/messages`)
- **🔍 Direct Indexer & Integrated Search** &mdash; native Torznab/Newznab integration, interactive search & browse with Freeleech badges, and dynamic auto-synchronization with Prowlarr.
- **🛡️ Network & VPN Kill Switch** &mdash; network interface binding (`tun0`, `wg0`) with automated socket halt on VPN disconnection, plus SOCKS5/HTTP proxy support.
- **⚙️ Complete 15-Tab Settings Suite** &mdash; General, Web UI, Notifications, Seeding, BitTorrent, Network, Peer Protocol, Protocols, Scheduler (24x7 hourly 3-tier matrix), Indexers, Connections, Download Clients, Simulation, Tracker Server, and Advanced.

## Tech Stack

- .NET 10 / ASP.NET Core (Kestrel)
- React 18 + TypeScript 5
- MonoTorrent BitTorrent Engine
- TagLibSharp & Pure EBML Media Inspector
- SQLite (default) / PostgreSQL via Dapper & FluentMigrator
- SignalR real-time event streaming

## Tags

- `latest` &mdash; most recent stable release
- `x.y.z` &mdash; specific semantic release version

## Also Available

- **GHCR**: `ghcr.io/dmzoneill/leecharr:latest`
- **Website**: [www.leecharr.net](https://www.leecharr.net)
- **Source**: [github.com/dmzoneill/Leecharr](https://github.com/dmzoneill/Leecharr)

## License

Apache License 2.0
