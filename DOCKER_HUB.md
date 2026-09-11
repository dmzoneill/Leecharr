<p align="center">
  <a href="https://www.leecharr.net" target="_blank" rel="noopener noreferrer">
    <img src="https://raw.githubusercontent.com/dmzoneill/Leecharr/main/logo/leecharr-skull.svg" alt="Leecharr Skull" width="140"/>
    <br/>
    <img src="https://raw.githubusercontent.com/dmzoneill/Leecharr/main/logo/leecharr-text.svg" alt="Leecharr" width="220"/>
  </a>
</p>

<p align="center">
  <strong>High-Performance BitTorrent & Media Downloader</strong> &mdash; purpose-built for the Servarr (*arr) ecosystem.
</p>

<p align="center">
  <a href="https://hub.docker.com/r/feeditout/leecharr"><img src="https://img.shields.io/docker/pulls/feeditout/leecharr?color=blue&logo=docker&style=flat-square" alt="Docker Pulls"></a>
  <a href="https://hub.docker.com/r/feeditout/leecharr"><img src="https://img.shields.io/docker/image-size/feeditout/leecharr/latest?color=blue&style=flat-square" alt="Docker Image Size"></a>
  <img src="https://img.shields.io/badge/arch-amd64%20%7C%20arm64-blue?style=flat-square" alt="Architectures">
  <a href="https://github.com/dmzoneill/Leecharr/releases/latest"><img src="https://img.shields.io/github/v/release/dmzoneill/Leecharr?color=brightgreen&label=release&style=flat-square" alt="Latest Release"></a>
  <a href="https://github.com/dmzoneill/Leecharr/actions/workflows/main.yml"><img src="https://github.com/dmzoneill/Leecharr/workflows/CICD/badge.svg?style=flat-square" alt="CI/CD Status"></a>
  <a href="https://github.com/dmzoneill/Leecharr/blob/main/LICENSE"><img src="https://img.shields.io/github/license/dmzoneill/Leecharr?color=blue&style=flat-square" alt="License"></a>
  <a href="https://www.leecharr.net"><img src="https://img.shields.io/badge/website-leecharr.net-5b8def?style=flat-square" alt="Website"></a>
</p>

---

<p align="center">
  <img src="https://raw.githubusercontent.com/dmzoneill/Leecharr/main/logo/ss.png" alt="Leecharr Web UI Screenshot" width="100%"/>
</p>

---

## 🚀 What's Changed in this Version

{{CHANGELOG}}

> 📖 **[View Complete Version Changelog on GitHub](https://github.com/dmzoneill/Leecharr/blob/main/CHANGELOG.md)**

---

## 💡 What is Leecharr?

**Leecharr** is a high-performance BitTorrent and media downloader purpose-built for the Servarr (`*arr`) ecosystem (Sonarr, Radarr, Lidarr, Prowlarr, Readarr).

Unlike conventional standalone clients that treat downloads as raw filenames and technical progress bars, Leecharr **deeply enriches active downloads with metadata, artwork, and stream specifications** directly from Sonarr, Radarr, and Lidarr &mdash; providing high-res movie posters, TV show banners, season hierarchy, episode stills, 4K UHD/HDR10+/Dolby Vision/Atmos stream details, and cast overviews in a unified Servarr interface.

### Key Highlights
- **⚡ Pure .NET 10 BitTorrent Engine**: High-throughput MonoTorrent engine with rarest-first piece picker, sequential download mode, and async non-blocking disk I/O.
- **🔌 Drop-In Client Compatibility**: Simultaneous RPC endpoints on port `7889` for:
  - **qBittorrent WebAPI v2** (`/api/v2/*`)
  - **Deluge JSON-RPC** (`/json`)
  - **Transmission RPC** (`/transmission/rpc`)
  - **Native Leecharr REST API v1 & SignalR** (`/api/v1/*`, `/signalr/messages`)
- **🔍 Direct Indexer & Integrated Search**: Native Torznab/Newznab search & browse with Freeleech badges and Prowlarr auto-sync.
- **🛡️ Network & VPN Kill Switch**: Interface binding (`tun0`, `wg0`) with automated socket halt on VPN disconnect, plus SOCKS5/HTTP proxy support.
- **🔔 Notification Triggers**: Outbound webhooks and alerts for Discord, Telegram, Pushover, Gotify, Email, and Custom Shell Scripts.
- **📁 Advanced Categories & Save Paths**: Automatic destination folder sorting, ratio goals, and per-category speed limits.

---

## ⚡ Quick Start

### Single Container Run (`docker run`)

```bash
docker run -d \
  --name leecharr \
  -p 7889:7889 \
  -p 7890:7890/tcp \
  -p 7890:7890/udp \
  -v leecharr-config:/config \
  -v leecharr-downloads:/downloads \
  --restart unless-stopped \
  feeditout/leecharr:latest
```

Open **http://localhost:7889** in your browser.

---

### Docker Compose (`compose.yaml` / `docker-compose.yml`)

```yaml
services:
  leecharr:
    image: feeditout/leecharr:latest
    container_name: leecharr
    restart: unless-stopped
    ports:
      - "7889:7889"             # Web UI, REST API, & Client RPC endpoints
      - "7890:7890/tcp"         # BitTorrent Incoming Peer Connections (TCP)
      - "7890:7890/udp"         # BitTorrent Incoming Peer Connections (uTP/UDP)
    volumes:
      - /opt/leecharr/config:/config
      - /opt/leecharr/downloads:/downloads
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7889/ping"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 15s
```

---

## 📁 Storage Volumes & Port Parameters

| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **`-p 7889:7889`** | Port | `7889` | Web UI, REST API v1, qBittorrent, Deluge & Transmission RPC adapters |
| **`-p 7890:7890`** | Port | `7890` | BitTorrent incoming peer communication port (TCP & UDP/uTP) |
| **`-v /config`** | Volume | `/config` | Application database (`leecharr.db`), runtime settings (`config.xml`), and logs |
| **`-v /downloads`** | Volume | `/downloads` | Default destination directory for completed and in-progress downloads |
| **`-e PUID / PGID`** | Env | `1000:1000` | User and Group ID for download filesystem permissions |
| **`-e TZ`** | Env | `UTC` | Timezone for scheduler matrix and automated logs |

---

## 🌐 Reverse Proxy Configuration

### Nginx
```nginx
server {
    listen 80;
    server_name leecharr.yourdomain.com;

    location / {
        proxy_pass http://127.0.0.1:7889;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # WebSocket / SignalR support
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 86400;
    }
}
```

---

## 🛠️ Supported Architectures & Tags

Multi-architecture builds are automatically published to both Docker Hub and GitHub Packages Container Registry (GHCR):

| Architecture | Tag Example | Status |
| :--- | :--- | :--- |
| **`linux/amd64`** | `feeditout/leecharr:latest`, `feeditout/leecharr:1.0.121` | ✅ Verified Stable |
| **`linux/arm64`** | `feeditout/leecharr:latest`, `feeditout/leecharr:1.0.121` | ✅ Verified Stable |

---

## 🔗 Links & Resources

- **Official Website**: [www.leecharr.net](https://www.leecharr.net)
- **Source Code**: [github.com/dmzoneill/Leecharr](https://github.com/dmzoneill/Leecharr)
- **Changelog**: [CHANGELOG.md](https://github.com/dmzoneill/Leecharr/blob/main/CHANGELOG.md)
- **GitHub Container Registry**: [ghcr.io/dmzoneill/leecharr](https://ghcr.io/dmzoneill/leecharr)
- **License**: [Apache License 2.0](https://github.com/dmzoneill/Leecharr/blob/main/LICENSE)
