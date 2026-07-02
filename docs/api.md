# Leecharr REST API V1

Base URL: `http://localhost:9899/api/v1`

## API Endpoints

### Torrents (`/api/v1/torrent`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/torrent` | List all torrents with current speed & progress |
| `GET` | `/torrent/{id}` | Get torrent by ID with files and media metadata |
| `POST` | `/torrent` | Add torrent by magnet link or info hash |
| `POST` | `/torrent/upload` | Upload `.torrent` file (multipart form data) |
| `PUT` | `/torrent/{id}` | Update torrent configuration & speed limits |
| `DELETE` | `/torrent/{id}` | Remove torrent (`?deleteFiles=true/false`) |
| `POST` | `/torrent/{id}/start` | Start / resume torrent download |
| `POST` | `/torrent/{id}/pause` | Pause torrent download |
| `POST` | `/torrent/{id}/recheck` | Force re-check and piece hash verification |
| `POST` | `/torrent/{id}/announce` | Force immediate tracker announce |
| `GET` | `/torrent/{id}/files` | List files in torrent with progress & priorities |
| `PUT` | `/torrent/{id}/files/{fileId}/priority` | Set priority for specific file |
| `GET` | `/torrent/{id}/pieces` | Get bitfield / piece map array |
| `GET` | `/torrent/{id}/peers` | List connected peers with client names & speeds |
| `GET` | `/torrent/{id}/trackers` | List configured tracker tiers & status |

### Media Enrichment (`/api/v1/media`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/media/{torrentId}` | Get media metadata (poster, backdrop, synopsis, cast) |
| `POST` | `/media/{torrentId}/refresh` | Force re-query `*arr` instance for updated metadata |

### *arr Connections (`/api/v1/arrconnections`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/arrconnections` | List configured Sonarr, Radarr, Lidarr connections |
| `POST` | `/arrconnections` | Create new connection |
| `PUT` | `/arrconnections/{id}` | Update connection |
| `DELETE` | `/arrconnections/{id}` | Delete connection |
| `POST` | `/arrconnections/{id}/test` | Test connectivity & API key |
| `POST` | `/arrconnections/sync` | Trigger full synchronization |

### Download Client Compatibility Endpoints

| Protocol | Path | Description |
| :--- | :--- | :--- |
| **qBittorrent WebAPI v2** | `/api/v2/*` | Supports `/api/v2/torrents/*`, `/api/v2/app/*`, `/api/v2/sync/*` |
| **Deluge JSON-RPC** | `/json` | Supports Deluge daemon RPC methods (`core.*`, `web.*`) |
| **Transmission RPC** | `/transmission/rpc` | Supports Transmission JSON-RPC commands |

### System & Health (`/api/v1/system`, `/api/v1/health`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/system/status` | Application version, runtime, OS, paths |
| `GET` | `/system/diskspace` | Free & total storage on download paths |
| `GET` | `/system/tasks` | Background scheduled task status |
| `GET` | `/health` | Health checks (storage, network, permissions) |

## SignalR Real-Time Hub

- Hub Route: `/signalr/messages`
- Client Handler: `receiveMessage`
- Broadcast Events:
  - `torrent` &mdash; State changes (status, progress, speeds)
  - `speedPulse` &mdash; Global & per-torrent throughput tick (1s interval)
  - `pieceMap` &mdash; Bitmap piece completion changes
  - `peerSwarm` &mdash; Peer connect/disconnect events
  - `mediaEnriched` &mdash; Notification when metadata is resolved
