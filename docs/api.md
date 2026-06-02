# Leecharr REST API V1 & Client Compatibility

Base URL: `http://localhost:7889`

---

## 1. Native Leecharr REST API (`/api/v1`)

### Torrents (`/api/v1/torrents`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/torrents` | List all torrents with current speed, progress, and media metadata |
| `GET` | `/api/v1/torrents/{id}` | Get torrent by ID with files, peers, trackers, and media metadata |
| `POST` | `/api/v1/torrents` | Add torrent by magnet link or multipart `.torrent` upload |
| `PUT` | `/api/v1/torrents/{id}` | Update torrent configuration & speed limits |
| `DELETE` | `/api/v1/torrents/{id}` | Remove torrent (`?deleteFiles=true/false`) |
| `POST` | `/api/v1/torrents/{id}/resume` | Resume torrent download |
| `POST` | `/api/v1/torrents/{id}/pause` | Pause torrent download |
| `POST` | `/api/v1/torrents/{id}/recheck` | Force re-check and piece hash verification |
| `GET` | `/api/v1/torrents/{id}/files` | List files in torrent with progress & priorities |

### Categories (`/api/v1/categories`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/categories` | List all user-configured categories (e.g. `tv`, `movies`, `music`) |
| `GET` | `/api/v1/categories/{id}` | Get category by ID |
| `POST` | `/api/v1/categories` | Create category with custom download path & seeding rules |
| `PUT` | `/api/v1/categories/{id}` | Update category |
| `DELETE` | `/api/v1/categories/{id}` | Delete category |

### Media Metadata & Artwork (`/api/v1/media`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/media/{torrentId}` | Get media metadata (poster, backdrop, synopsis, stream specs) |
| `GET` | `/api/v1/media/artwork/{torrentId}/{type}` | Serve cached high-res artwork (`poster` or `backdrop`) |

### Indexers (`/api/v1/indexer`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/indexer` | List all configured Torznab/Newznab indexers |
| `POST` | `/api/v1/indexer` | Add new indexer definition |
| `PUT` | `/api/v1/indexer/{id}` | Update indexer configuration |
| `DELETE` | `/api/v1/indexer/{id}` | Delete indexer |
| `POST` | `/api/v1/indexer/{id}/test` | Test connectivity (`t=caps`) and API key |
| `POST` | `/api/v1/indexer/sync-prowlarr` | Sync all indexers from connected Prowlarr instance |

### Search & Grab (`/api/v1/search`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/search` | Multi-indexer search (`?query=...&category=...&freeleechOnly=...`) |
| `POST` | `/api/v1/search/grab` | One-click download of selected release into Leecharr |

### Notifications & Webhooks (`/api/v1/notifications`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/notifications` | List configured notification connections (Webhooks, Discord, etc.) |
| `POST` | `/api/v1/notifications` | Create new connection definition |
| `PUT` | `/api/v1/notifications/{id}` | Update connection |
| `DELETE` | `/api/v1/notifications/{id}` | Delete connection |
| `POST` | `/api/v1/notifications/{id}/test` | Send test payload to verify webhook delivery |

### System (`/api/v1/system`)

| Method | Path | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/system/status` | Runtime environment info, version, OS, start time |

---

## 2. Client Compatibility Adapters

Sonarr, Radarr, Lidarr, and Prowlarr can connect to Leecharr out-of-the-box using any of the following adapters:

### 1. qBittorrent WebAPI v2 Compatibility (`/api/v2/*`)

| Endpoint | Method | Supported Actions |
| :--- | :--- | :--- |
| `/api/v2/auth/login` | POST | Authenticates session with cookie `SID` |
| `/api/v2/auth/logout` | POST | Terminates session |
| `/api/v2/app/version` | GET | Returns `v4.4.2` |
| `/api/v2/app/webapiVersion` | GET | Returns WebAPI version `2.8.3` |
| `/api/v2/torrents/info` | GET | Filter by category, hash; returns status, progress, ETA, speeds, savepath |
| `/api/v2/torrents/add` | POST | Handles magnet links, `.torrent` uploads, category assignment, savepath |
| `/api/v2/torrents/delete` | POST | Deletes torrents with optional file deletion |
| `/api/v2/torrents/pause` | POST | Pauses torrents |
| `/api/v2/torrents/resume` | POST | Resumes torrents |
| `/api/v2/torrents/files` | GET | Returns file list with sizes, progress, priorities |
| `/api/v2/torrents/categories` | GET | Returns category list and paths |
| `/api/v2/torrents/createCategory` | POST | Creates new category |
| `/api/v2/sync/maindata` | GET | Full / delta sync for real-time monitoring |
| `/api/v2/transfer/info` | GET | Global download/upload throughput statistics |

### 2. Deluge JSON-RPC Compatibility (`/json`)

Detailed in [docs/deluge-requirements.md](file:///home/daoneill/src/usr/seedarr/Leecher/docs/deluge-requirements.md).

- **Endpoint:** `POST /json`
- **Authentication:** `auth.login(password)`
- **Query Status:** `core.get_torrents_status(filter_dict, keys_list)` & `web.get_torrents_status(filter_dict, keys_list)`
- **Add Torrents:** `core.add_torrent_magnet(uri, options)`, `core.add_torrent_file(filename, filedump, options)`, `core.add_torrent_url(url, options)`
- **Control:** `core.pause_torrent`, `core.resume_torrent`, `core.remove_torrent(id, remove_data)`, `core.force_recheck`, `core.set_torrent_options`
- **Config & Filters:** `core.get_config`, `core.set_config`, `core.get_filter_tree`, `core.get_session_status`, `core.get_free_space`

### 3. Transmission RPC Compatibility (`/transmission/rpc`)

- **Protocol:** JSON-RPC with `X-Transmission-Session-Id` header negotiation.
- **Methods:** `session-get`, `session-set`, `torrent-get` (name, id, hashString, status, totalSize, percentDone, rateDownload, rateUpload, files), `torrent-add`, `torrent-remove`, `torrent-start`, `torrent-stop`.

---

## 3. SignalR Real-Time Hub

- **Hub Endpoint:** `/signalr/messages`
- **Client Event Name:** `receiveMessage`
- **Format:** `{ "name": "<resourceName>", "body": <payloadObject> }`
- **Event Channels:**
  - `torrent` &mdash; Entity state changes (status, progress, speeds)
  - `category` &mdash; Category CRUD events
  - `speedPulse` &mdash; Real-time global and per-torrent speed metrics (1s tick)
  - `pieceMapUpdated` &mdash; Piece verification bitmap changes
