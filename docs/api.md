# Leecharr REST API V1 & Client Compatibility

Base URL: `http://localhost:9899`

---

## 1. Native Leecharr REST API (`/api/v1`)

### Torrents (`/api/v1/torrent`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/torrent` | List all torrents with current speed, progress, and media metadata |
| `GET` | `/api/v1/torrent/{id}` | Get torrent by ID with files, peers, trackers, and media metadata |
| `POST` | `/api/v1/torrent` | Add torrent by magnet link or info hash |
| `POST` | `/api/v1/torrent/upload` | Upload `.torrent` file (multipart form data) |
| `PUT` | `/api/v1/torrent/{id}` | Update torrent configuration & speed limits |
| `DELETE` | `/api/v1/torrent/{id}` | Remove torrent (`?deleteFiles=true/false`) |
| `POST` | `/api/v1/torrent/{id}/start` | Start / resume torrent download |
| `POST` | `/api/v1/torrent/{id}/pause` | Pause torrent download |
| `POST` | `/api/v1/torrent/{id}/recheck` | Force re-check and piece hash verification |
| `POST` | `/api/v1/torrent/{id}/announce` | Force immediate tracker announce |
| `GET` | `/api/v1/torrent/{id}/files` | List files in torrent with progress & priorities |
| `PUT` | `/api/v1/torrent/{id}/files/{fileId}/priority` | Set priority for specific file (*Skip, Low, Normal, High*) |
| `GET` | `/api/v1/torrent/{id}/pieces` | Get piece completion bitfield array |
| `GET` | `/api/v1/torrent/{id}/peers` | List connected peers with client names & speeds |
| `GET` | `/api/v1/torrent/{id}/trackers` | List configured tracker tiers & status |

### Categories (`/api/v1/category`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/category` | List all user-configured categories (e.g. `tv`, `movies`, `music`) |
| `POST` | `/api/v1/category` | Create category with custom download path & seeding rules |
| `PUT` | `/api/v1/category/{id}` | Update category |
| `DELETE` | `/api/v1/category/{id}` | Delete category |

### Media Enrichment (`/api/v1/media`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/media/{torrentId}` | Get media metadata (poster, backdrop, synopsis, cast, stream specs) |
| `POST` | `/api/v1/media/{torrentId}/refresh` | Force re-query `*arr` instance for updated metadata |

### *arr Connections (`/api/v1/arrconnections`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/arrconnections` | List configured Sonarr, Radarr, Lidarr connections |
| `POST` | `/api/v1/arrconnections` | Create new connection |
| `PUT` | `/api/v1/arrconnections/{id}` | Update connection |
| `DELETE` | `/api/v1/arrconnections/{id}` | Delete connection |
| `POST` | `/api/v1/arrconnections/{id}/test` | Test connectivity & API key |
| `POST` | `/api/v1/arrconnections/sync` | Trigger full synchronization |

---

## 2. Download Client Compatibility Adapters

Sonarr, Radarr, Lidarr, and Prowlarr can connect to Leecharr immediately using these endpoints:

### qBittorrent WebAPI v2 Compatibility (`/api/v2/*`)

| Endpoint | Method | Supported Actions |
| :--- | :--- | :--- |
| `/api/v2/auth/login` | POST | Authenticates `*arr` client session |
| `/api/v2/app/version` | GET | Returns emulated qBittorrent version string |
| `/api/v2/app/webapiVersion` | GET | Returns WebAPI version `2.8.3` |
| `/api/v2/torrents/info` | GET | Filter by category, tag, hash; returns status, progress, ETA, speeds, savepath |
| `/api/v2/torrents/add` | POST | Handles magnet links, `.torrent` uploads, category assignment, savepath |
| `/api/v2/torrents/delete` | POST | Deletes torrents with optional file deletion |
| `/api/v2/torrents/pause` | POST | Pauses torrents |
| `/api/v2/torrents/resume` | POST | Resumes torrents |
| `/api/v2/torrents/files` | GET | Returns file list with sizes, progress, priorities |
| `/api/v2/torrents/categories` | GET | Returns category list and paths |
| `/api/v2/torrents/setCategory` | POST | Reassigns category |
| `/api/v2/sync/maindata` | GET | Delta sync for fast updates |

### Transmission RPC Compatibility (`/transmission/rpc`)

- Protocol: JSON-RPC with `X-Transmission-Session-Id` header negotiation.
- Supported methods: `session-get`, `session-set`, `torrent-get` (all fields: name, id, hashString, status, totalSize, percentDone, rateDownload, rateUpload, files), `torrent-add`, `torrent-remove`, `torrent-start`, `torrent-stop`.

### Deluge JSON-RPC Compatibility (`/json`)

- Protocol: Deluge daemon / web JSON-RPC format.
- Supported methods: `auth.login`, `core.add_torrent_magnet`, `core.add_torrent_file`, `core.get_torrents_status`, `core.remove_torrent`, `core.pause_torrent`, `core.resume_torrent`.

---

## 3. SignalR Real-Time Hub

- **Hub Endpoint:** `/signalr/messages`
- **Client Handler:** `receiveMessage`
- **Payload Format:** `{ "name": "<event>", "body": <data> }`
- **Broadcast Events:**
  - `torrent` &mdash; State changes (status, progress, speeds)
  - `speedPulse` &mdash; Aggregate and per-torrent speed ticks (1s interval)
  - `pieceMap` &mdash; Piece completion bitmap updates
  - `peerSwarm` &mdash; Peer connect/disconnect events
  - `mediaEnriched` &mdash; Rich metadata attachments from Sonarr/Radarr/Lidarr
