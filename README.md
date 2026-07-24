# Leecharr

<p align="center">
  <img src="logo/leecharr-skull.svg" alt="Leecharr" width="200"/>
  <br/>
  <img src="logo/leecharr-text.svg" alt="Leecharr" width="200"/>
</p>

<p align="center">
  <strong>High-Performance BitTorrent & Media Downloader</strong> &mdash; purpose-built for the Servarr (*arr) ecosystem
</p>

<p align="center">
  <a href="https://www.leecharr.net"><img src="https://img.shields.io/badge/website-leecharr.net-ffd166?logo=data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0id2hpdGUiIGQ9Ik0xMiAyQzYuNDggMiAyIDYuNDggMiAxMnM0LjQ4IDEwIDEwIDEwIDEwLTQuNDggMTAtMTBTMTcuNTIgMiAxMiAyek0xMSAxOS45M2MtMy45NS0uNDktNy03LjctNy03LjkzIDAtLjYyLjA4LTEuMjEuMjEtMS43OWwuMTcuMjYgNC44NCA0Ljg0djFjMCAxLjEuOSAyIDIgMnYxLjkzem02LjktMi41NGMtLjI2LS44MS0xLTEuMzktMS45LTEuMzloLTF2LTNjMC0uNTUtLjQ1LTEtMS0xaC02di0yaDJjLjU1IDAgMS0uNDUgMS0xVjdoMmMxLjEgMCAyLS45IDItMnYtLjQxYzIuOTMgMS4xOSA1IDQuMDYgNSA3LjQxIDAgMi4wOC0uOCAzLjk3LTIuMSA1LjM5eiIvPjwvc3ZnPg==" alt="Website"></a>
  <a href="https://github.com/dmzoneill/Leecharr/actions/workflows/main.yml"><img src="https://github.com/dmzoneill/Leecharr/workflows/CICD/badge.svg" alt="CI/CD"></a>
  <a href="https://github.com/dmzoneill/Leecharr/releases/latest"><img src="https://img.shields.io/github/v/release/dmzoneill/Leecharr?color=brightgreen&label=release" alt="Latest Release"></a>
  <a href="https://github.com/dmzoneill/Leecharr/blob/main/LICENSE"><img src="https://img.shields.io/github/license/dmzoneill/Leecharr?color=blue" alt="License"></a>
  <a href="https://hub.docker.com/r/feeditout/leecharr"><img src="https://img.shields.io/docker/pulls/feeditout/leecharr?color=blue&logo=docker" alt="Docker Pulls"></a>
  <a href="https://ghcr.io/dmzoneill/leecharr"><img src="https://img.shields.io/badge/ghcr.io-leecharr-blue?logo=github" alt="GHCR"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/React-18-61DAFB?logo=react" alt="React 18">
  <img src="https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript" alt="TypeScript">
</p>

---

## What is Leecharr?

**Leecharr** is a modern, high-performance BitTorrent and media downloader purpose-built for the Servarr (`*arr`) ecosystem (Sonarr, Radarr, Lidarr, Prowlarr, Readarr).

Unlike conventional standalone clients (Deluge, qBittorrent, Transmission) that present torrents as raw filenames and technical progress bars, Leecharr **deeply enriches active downloads with metadata and artwork** directly from Sonarr, Radarr, and Lidarr &mdash; giving you movie posters, TV show banners, episode screenshots, artist fanart, media stream specs, and cast overviews in a unified, beautiful Servarr interface.

---

## Key Features

### 🌟 Deep *arr Media Enrichment
- **Automatic Media Correlation:** Matches torrents by release title, info hash, or `*arr` download ID to pull rich media metadata.
- **Visual Media Experience:** Displays high-res posters, fanart backdrops, season banners, and episode titles.
- **Season Pack Hierarchy:** Automatically groups multi-file TV season packs by Show &rarr; Season &rarr; Episode.
- **Media Stream Info:** Shows resolution (4K, 1080p), HDR format (Dolby Vision, HDR10+), audio codecs (Dolby Atmos, TrueHD, FLAC), and subtitle tracks.

### ⚡ Pure C# .NET 10 BitTorrent Engine
- **Rarest-First & Endgame Mode:** Optimal swarm health and piece distribution.
- **Sequential Download Mode:** Enables instant **video streaming and file previewing** while actively downloading.
- **Per-File Priority Management:** Skip unwanted files or prioritize specific files.
- **Non-Blocking Async Disk I/O:** Write/read caching (64MB–512MB) and sparse pre-allocation to prevent disk bottlenecking.
- **Fast Resume Persistence:** Checkpoints piece bitfields and states to SQLite for instant startup without full re-hashing.
- **MSE/PE Stream Encryption:** Diffie-Hellman 768-bit key exchange + RC4 stream cipher.
- **BEP Protocol Support:** HTTP & UDP Trackers (BEP 3, BEP 15, BEP 12 Multi-Tracker), DHT (BEP 5), PEX (BEP 11), `ut_metadata` (BEP 9), Fast Extension (BEP 6), LPD (BEP 14), and uTP (BEP 29).

### 🔌 Download Client Compatibility
- **Deluge JSON-RPC Adapter:** Connects seamlessly to tools expecting Deluge daemon.
- **qBittorrent WebAPI v2 Adapter:** Acts as a drop-in qBittorrent client for existing apps.
- **Transmission RPC Adapter:** Compatible with Transmission remote clients.
- **Native Leecharr REST API v1 & SignalR:** Sub-second push for speed pulses, piece maps, and swarm events.

---

## Documentation

- [Architecture Guide](docs/architecture.md)
- [Domain Model](docs/domain-model.md)
- [Media Enrichment Specification](docs/media-enrichment.md)
- [BitTorrent Protocols](docs/protocols.md)
- [REST API Reference](docs/api.md)
- [Development & Setup Guide](docs/development.md)

---

## License

Distributed under the **Apache License 2.0**.
