<div align="center">
  <center>
    <a href="https://www.leecharr.net" target="_blank" rel="noopener noreferrer">
      <img src="https://raw.githubusercontent.com/dmzoneill/Leecharr/main/logo/leecharr-skull.svg" alt="Leecharr Skull" width="140"/>
      <br/>
      <img src="https://raw.githubusercontent.com/dmzoneill/Leecharr/main/logo/leecharr-text.svg" alt="Leecharr" width="220"/>
    </a>
    <p>
      <strong>High-Performance BitTorrent &amp; Media Downloader</strong> &mdash; purpose-built for the Servarr (*arr) ecosystem.
    </p>
  </center>
</div>

<div align="center">
  <table align="center">
    <tr>
      <td align="center"><a href="https://www.leecharr.net"><img src="https://img.shields.io/badge/website-leecharr.net-ffd166" alt="Website"></a></td>
      <td align="center"><a href="https://github.com/dmzoneill/Leecharr/actions/workflows/main.yml"><img src="https://github.com/dmzoneill/Leecharr/workflows/CICD/badge.svg" alt="CI/CD"></a></td>
      <td align="center"><a href="https://github.com/dmzoneill/Leecharr/releases/latest"><img src="https://img.shields.io/github/v/release/dmzoneill/Leecharr?color=brightgreen&label=release" alt="Latest Release"></a></td>
      <td align="center"><a href="https://github.com/dmzoneill/Leecharr/blob/main/LICENSE"><img src="https://img.shields.io/github/license/dmzoneill/Leecharr?color=blue" alt="License"></a></td>
      <td align="center"><a href="https://hub.docker.com/r/feeditout/leecharr"><img src="https://img.shields.io/docker/pulls/feeditout/leecharr?color=blue&logo=docker" alt="Docker Pulls"></a></td>
    </tr>
    <tr>
      <td align="center"><a href="https://ghcr.io/dmzoneill/leecharr"><img src="https://img.shields.io/badge/ghcr.io-leecharr-blue?logo=github" alt="GHCR"></a></td>
      <td align="center"><img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10"></td>
      <td align="center"><img src="https://img.shields.io/badge/React-19-61DAFB?logo=react" alt="React 19"></td>
      <td align="center"><img src="https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript" alt="TypeScript"></td>
      <td></td>
    </tr>
  </table>
</div>

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

**Leecharr** is a modern, high-performance BitTorrent and media downloader purpose-built for the Servarr (`*arr`) ecosystem (Sonarr, Radarr, Lidarr, Prowlarr, Readarr).

Unlike conventional standalone clients (Deluge, qBittorrent, Transmission) that present torrents as raw filenames and technical progress bars, Leecharr **deeply enriches active downloads with metadata and artwork** directly from Sonarr, Radarr, and Lidarr &mdash; giving you movie posters, TV show banners, episode screenshots, artist fanart, media stream specs, and cast overviews in a unified, beautiful Servarr interface.

### Key Highlights
- **🌟 Deep *arr Media Enrichment**: Automatic metadata and artwork correlation by release title, info hash, or `*arr` download ID with posters, fanart, season banners, and stream specs (4K, HDR10+, Dolby Vision, Dolby Atmos).
- **⚡ Pure C# .NET 10 BitTorrent Engine**: Rarest-First, Endgame mode, sequential streaming mode for instant video preview, non-blocking asynchronous disk I/O (64MB&ndash;512MB RAM write cache), and fast resume SQLite checkpoints.
- **🔌 Full Client Interoperability**: Built-in RPC and WebAPI emulation for Deluge (JSON-RPC), qBittorrent (WebAPI v2), and Transmission (RPC with form-urlencoded and URL path normalization) &mdash; acts as a drop-in replacement for existing download tools.
- **🌐 Complete BitTorrent Protocol Stack**: HTTP/UDP tracker announce &amp; scrape (BEP 3, BEP 15, BEP 12 Multi-Tracker), Peer Wire protocol, MSE/PE Diffie-Hellman RC4 encryption, DHT (BEP 5), PEX (BEP 11), `ut_metadata` (BEP 9), Fast Extension (BEP 6), LPD (BEP 14), and uTP transport (BEP 29).
- **📡 Sub-Second Real-Time Updates**: Native Leecharr REST API v1 and SignalR WebSocket push for real-time speed pulses, piece maps, and swarm events.
- **🔔 Multi-Channel Notifications**: Discord, Telegram, Gotify, Pushover, Apprise, Email/SMTP, and Generic Webhooks with media artwork attachments.

---

## ⚡ Quick Start

### Single Container Run (`podman run` / `docker run`)

**Option 1: Docker Hub**
```bash
podman run -d \
  --name leecharr \
  -p 7889:7889 \
  -p 7890:7890/tcp \
  -p 7890:7890/udp \
  -v leecharr-config:/config \
  -v leecharr-downloads:/downloads \
  --restart unless-stopped \
  feeditout/leecharr:latest
```

**Option 2: GitHub Container Registry (GHCR)**
```bash
podman run -d \
  --name leecharr \
  -p 7889:7889 \
  -p 7890:7890/tcp \
  -p 7890:7890/udp \
  -v leecharr-config:/config \
  -v leecharr-downloads:/downloads \
  --restart unless-stopped \
  ghcr.io/dmzoneill/leecharr:latest
```

Open **http://localhost:7889** in your browser.

---

### Container Compose (`podman-compose.yml` / `compose.yaml`)

```yaml
services:
  leecharr:
    # Option 1 (Docker Hub):
    image: feeditout/leecharr:latest
    # Option 2 (GHCR):
    # image: ghcr.io/dmzoneill/leecharr:latest
    container_name: leecharr
    restart: unless-stopped
    ports:
      - "7889:7889"          # Web UI, REST API, & Client RPC Emulation
      - "7890:7890/tcp"      # BitTorrent Peer Wire (Inbound TCP)
      - "7890:7890/udp"      # BitTorrent DHT & uTP (Inbound UDP)
    volumes:
      - /opt/leecharr/config:/config
      - /opt/leecharr/downloads:/downloads
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7889/api/v1/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 15s
```

Run with Container Compose:
```bash
podman-compose up -d
# or: docker compose up -d
```

---

## 📁 Storage Volumes & Port Parameters

| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **`-p 7889:7889`** | Port | `7889` | Web UI, REST API v1, SignalR push notifications, Deluge/qBittorrent/Transmission RPC, and Swagger |
| **`-p 7890:7890/tcp`** | Port | `7890` | BitTorrent Peer Wire inbound connection listener |
| **`-p 7890:7890/udp`** | Port | `7890` | BitTorrent DHT node and uTP packet exchange listener |
| **`-v /config`** | Volume | `/config` | Application database (`leecharr.db`), configuration settings (`config.xml`), logs, and certificates |
| **`-v /downloads`** | Volume | `/downloads` | Active and completed download storage directory |
| **`-e PUID / PGID`** | Env | `1000:1000` | User and Group ID for internal filesystem permissions |
| **`-e TZ`** | Env | `UTC` | Timezone for scheduler matrix, logs, and automated backup cron jobs |

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

### Traefik (Docker Labels)
```yaml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.leecharr.rule=Host(`leecharr.yourdomain.com`)"
  - "traefik.http.routers.leecharr.entrypoints=websecure"
  - "traefik.http.routers.leecharr.tls.certresolver=letsencrypt"
  - "traefik.http.services.leecharr.loadbalancer.server.port=7889"
```

---

## 🛠️ Supported Architectures & Tags

Multi-architecture builds are automatically published to both Docker Hub and GitHub Packages Container Registry (GHCR):

| Architecture | Docker Hub Tag Example | GHCR Tag Example | Status |
| :--- | :--- | :--- | :--- |
| **`linux/amd64`** | `feeditout/leecharr:latest` | `ghcr.io/dmzoneill/leecharr:latest` | ✅ Verified Stable |
| **`linux/arm64`** | `feeditout/leecharr:latest` | `ghcr.io/dmzoneill/leecharr:latest` | ✅ Verified Stable |

---

## 🔗 Links & Resources

- **Official Website**: [www.leecharr.net](https://www.leecharr.net)
- **Source Code**: [github.com/dmzoneill/Leecharr](https://github.com/dmzoneill/Leecharr)
- **Changelog**: [CHANGELOG.md](https://github.com/dmzoneill/Leecharr/blob/main/CHANGELOG.md)
- **GitHub Container Registry**: [ghcr.io/dmzoneill/leecharr](https://ghcr.io/dmzoneill/leecharr)
- **Documentation**: [docs/](https://github.com/dmzoneill/Leecharr/tree/main/docs)
- **License**: [Apache License 2.0](https://github.com/dmzoneill/Leecharr/blob/main/LICENSE)
