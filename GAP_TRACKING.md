# Leecharr vs. qBittorrent Gap Tracking & Implementation Roadmap

This document tracks all features identified from qBittorrent that are missing or disconnected in Leecharr, organized top-down by implementation phase. Progress is updated as each item is addressed.

---

## Progress Overview

| Phase | Description | Total Tasks | Completed | In Progress | Pending |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Phase 1** | Engine Core Wire-Up & Disconnects | 3 | 3 | 0 | 0 |
| **Phase 2** | Queue & Storage Management | 3 | 3 | 0 | 0 |
| **Phase 3** | Networking & Security Hardening | 3 | 3 | 0 | 0 |
| **Phase 4** | Advanced Protocol & Torrent Management | 3 | 3 | 0 | 0 |
| **Phase 5** | Parity Beyond Core Engine (qBittorrent-Only & Partial) | 5 | 5 | 0 | 0 |
| **Total** | | **17** | **17** | **0** | **0** |

---

## Top-Down Task List

### Phase 1: Engine Core Wire-Up & Disconnects

- [x] **TASK-01: Wire Custom PiecePicker into MonoTorrent Engine**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** libtorrent rarest-first, streaming, first/last piece priority, piece extent affinity.
  - **Leecharr Implementation:** `PiecePicker` is now dynamically initialized on `MonoTorrentDownloadTask` for all torrents, synchronized with peer availability on connect/disconnect events, updated on piece hashing pass/fail, and synced with file priority changes in `SetFilePriorityAsync`. `IDownloadTask` exposes `Picker`, and `PieceAvailability` delegates to `Picker.GetAvailability()`. `PiecePickerStrategy` configuration is honored for streaming mode.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/NzbDrone.Core/BitTorrent/PiecePicker.cs`
    - `src/NzbDrone.Core/BitTorrent/IDownloadEngine.cs`
    - `src/Leecharr.Core.Test/BitTorrent/MonoTorrentDownloadEngineTest.cs`

- [x] **TASK-02: Connect IBlocklistService to Swarm Peer Connections**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** `FilterParserThread` builds `lt::ip_filter` applied atomically to all peer handshakes and connections.
  - **Leecharr Implementation:** Injected `IBlocklistService` into `MonoTorrentDownloadEngine`. Swarm peer connections in `OnPeerConnected` now inspect `e.Peer.Uri.Host` against `blocklistService.IsIpBlocked(peerIp)`; blacklisted peers are immediately severed, disposed, tracked in `BlockedPeersCount`, and excluded from `GetPeers()` and swarm availability. `TorrentEngineMetrics` now reports `BlockedPeersCount`.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/NzbDrone.Core/BitTorrent/TorrentEngineMetrics.cs`
    - `src/NzbDrone.Core/Network/Blocklist/IBlocklistService.cs`
    - `src/Leecharr.Core.Test/BitTorrent/MonoTorrentDownloadEngineTest.cs`

- [x] **TASK-03: Implement SignalR Telemetry Broadcaster (`speedPulse` & `pieceMapUpdated`)**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** `/api/v2/sync/maindata` 1-second delta synchronization.
  - **Leecharr Implementation:** Integrated 1-second high-frequency telemetry ticker in `AppLifetime` broadcasting `speedPulse` payload (uploadSpeed, downloadSpeed, progress, uploaded, downloaded, ratio, eta, status, seeders, leechers) matching `App.tsx` SignalR contract. Implemented `PieceMapSignalREventHandler` responding to `PieceVerifiedEvent` and broadcasting instant `pieceMapUpdated` events to connected UI clients.
  - **Files:**
    - `src/NzbDrone.Host/AppLifetime.cs`
    - `src/NzbDrone.SignalR/PieceMapSignalREventHandler.cs`
    - `src/Leecharr.Core.Test/SignalR/TelemetryBroadcasterTest.cs`

---

## Phase 2: Queue & Storage Management

- [x] **TASK-04: Implement Active Queue Concurrency Limiter**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Queueing system (`maxActiveDownloads`, `maxActiveUploads`, `maxActiveTorrents`, slow torrent ignore rules).
  - **Leecharr Implementation:** Added `IQueueManagerService` / `QueueManagerService` handling queue ordering, throttling active downloads and uploads against `MaxActiveDownloads`, `MaxActiveUploads`, and `MaxActiveTorrents`, enforcing `IgnoreSlowTorrents` with configurable rate thresholds. Integrated into `TorrentService.SetQueuePositionAsync`, event handlers (`TorrentStatusChangedEvent`, `TorrentAddedEvent`, `TorrentDeletedEvent`), and `AppLifetime` periodic evaluation.
  - **Files:**
    - `src/NzbDrone.Core/Torrents/IQueueManagerService.cs`
    - `src/NzbDrone.Core/Torrents/QueueManagerService.cs`
    - `src/NzbDrone.Core/Torrents/TorrentService.cs`
    - `src/NzbDrone.Host/AppLifetime.cs`
    - `src/NzbDrone.Core/Configuration/ConfigService.cs`
    - `src/Leecharr.Core.Test/Torrents/QueueManagerServiceTest.cs`

- [x] **TASK-05: Configurable Share Limit Actions (Pause, Remove, Remove with Data)**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Share limit action choices (Stop, Remove, Remove with Content, Enable Super Seeding).
  - **Leecharr Implementation:** Extended `Torrent.cs` with `ShareLimitAction` (persisted via Migration 16) and `ConfigService` with `GlobalShareLimitAction`. Supported `maxRatioAction` in `QBittorrentApiController.SetShareLimits` and REST API. Automated check in `AppLifetime` now dynamically executes the configured action (`Pause`, `Remove`, `RemoveWithData`, or `SuperSeeding`).
  - **Files:**
    - `src/NzbDrone.Core/Configuration/ConfigService.cs`
    - `src/NzbDrone.Core/Torrents/Torrent.cs`
    - `src/NzbDrone.Core/Datastore/Migration/016_add_torrent_share_limit_action.cs`
    - `src/NzbDrone.Host/AppLifetime.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Api.V1/Torrents/TorrentResource.cs`
    - `src/Leecharr.Api.V1/Torrents/TorrentResourceMapper.cs`
    - `src/Leecharr.Core.Test/Torrents/ShareLimitActionTest.cs`

- [x] **TASK-06: Incomplete Files Extension (`.!leech`)**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Appends `.!qB` to unfinished files to prevent media scanners from indexing incomplete files.
  - **Leecharr Implementation:** Extended `IConfigService` with `AppendIncompleteExtension` and `IncompleteExtension` (`.!leech`). Configured `EngineSettingsBuilder.UsePartialFiles` in `MonoTorrentDownloadEngine`. Extended `StoragePathService` with `StripIncompleteExtensions` invoked upon moving to completed and on torrent completion (`TorrentState.Seeding`). Exposed `incomplete_files_ext` in `QBittorrentApiController.GetPreferences`.
  - **Files:**
    - `src/NzbDrone.Core/Configuration/ConfigService.cs`
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/NzbDrone.Core/Download/StoragePathService.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Core.Test/Download/StoragePathServiceTest.cs`

---

## Phase 3: Networking & Security Hardening

- [x] **TASK-07: Implement SOCKS5 / HTTP Proxy Socket Tunneling**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** SOCKS5 with authentication, proxy peer connections, DNS leak protection.
  - **Leecharr Implementation:** Implemented RFC 1928 SOCKS5 (with user/pass auth RFC 1929 and domain-based addressing for DNS leak protection) and RFC 7231 HTTP CONNECT tunneling in `ProxyTunnelBindingProvider`. Wired `Factories.Default.WithHttpClientCreator` in `MonoTorrentDownloadEngine` with `WebProxy` and credentials so outbound tracker announcements route through configured proxies.
  - **Files:**
    - `src/NzbDrone.Core/Network/Binding/IProxyTunnelBindingProvider.cs`
    - `src/NzbDrone.Core/Network/Binding/ProxyTunnelBindingProvider.cs`
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/Leecharr.Core.Test/Network/ProxyTunnelBindingProviderTest.cs`

- [x] **TASK-08: IPv6 Dual-Stack Swarm Listener & Discovery**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Dual-stack endpoints (`0.0.0.0` and `[::]`), IPv6 local peer discovery.
  - **Leecharr Implementation:** Extended `IConfigService` with `EnableIPv6` (default `true`). Configured `MonoTorrentDownloadEngine` to resolve IPv6 addresses from bound network interfaces and open dual-stack listening endpoints (`"ipv4"` and `"ipv6"`) on `ListenEndPoints` when `EnableIPv6 && Socket.OSSupportsIPv6`.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/NzbDrone.Core/Configuration/ConfigService.cs`
    - `src/Leecharr.Core.Test/BitTorrent/MonoTorrentDownloadEngineTest.cs`

- [x] **TASK-09: WebUI CSRF Protection & Host Header Validation**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Strict CSRF validation on Origin / Referer / Sec-Fetch-Site and Host header domain whitelisting.
  - **Leecharr Implementation:** Implemented `HostHeaderValidationMiddleware` for DNS rebinding protection and `CsrfProtectionMiddleware` validating Sec-Fetch-Site, Origin, and Referer headers against state-changing HTTP requests while providing bypasses for API keys and basic auth. Configured options `CsrfProtectionEnabled`, `HostHeaderValidationEnabled`, and `AllowedHosts` in `ConfigService`, and registered middlewares in `Startup.cs`.
  - **Files:**
    - `src/Leecharr.Http/Security/CsrfProtectionMiddleware.cs`
    - `src/Leecharr.Http/Security/HostHeaderValidationMiddleware.cs`
    - `src/NzbDrone.Host/Startup.cs`
    - `src/NzbDrone.Core/Configuration/ConfigService.cs`
    - `src/Leecharr.Core.Test/Http/SecurityMiddlewareTest.cs`

---

## Phase 4: Advanced Protocol & Torrent Management

- [x] **TASK-10: Per-Torrent File & Folder Renaming Support**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** `renameFile` and `renameFolder` in WebAPI and GUI.
  - **Leecharr Implementation:** Added `RenameFileAsync` and `RenameFolderAsync` to `IDownloadEngine`, `MonoTorrentDownloadEngine`, and `TorrentService`, utilizing MonoTorrent's `MoveFileAsync` to dynamically relocate individual files and directory trees without torrent re-creation. Exposed endpoints in `QBittorrentApiController` (`torrents/renameFile`, `torrents/renameFolder`) and synchronized paths with `TorrentFileRepository`.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/IDownloadEngine.cs`
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/NzbDrone.Core/Torrents/ITorrentService.cs`
    - `src/NzbDrone.Core/Torrents/TorrentService.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Core.Test/Torrents/TorrentServiceTest.cs`

- [x] **TASK-11: Super Seeding & Web Seeds Pass-Through to Engine**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** BEP 16 Initial Seeding toggle and BEP 17/19 HTTP web seeds.
  - **Leecharr Implementation:** Wired `torrent.InitialSeeding` directly into MonoTorrent's `TorrentSettingsBuilder.AllowInitialSeeding`. Added `SetSuperSeedingAsync` to `IDownloadEngine`, `MonoTorrentDownloadEngine`, and `TorrentService`. Exposed `torrents/setSuperSeeding` endpoint in `QBittorrentApiController` and mapped `super_seeding` state in torrent generic properties and info dictionaries. Ensured HTTP webseeds (`&ws=`) and torrent `HttpSeeds` flow through MonoTorrent's engine.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/IDownloadEngine.cs`
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/NzbDrone.Core/Torrents/ITorrentService.cs`
    - `src/NzbDrone.Core/Torrents/TorrentService.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Core.Test/Torrents/TorrentServiceTest.cs`

- [x] **TASK-12: BitTorrent v2 / Hybrid (BEP 52) Engine Assessment & Roadmap**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Full v1, v2, and hybrid torrent support via libtorrent 2.x.
  - **Leecharr Implementation:** Assessed MonoTorrent 3.0.2 BitTorrent v2 and BEP 52 capabilities: MonoTorrent natively supports v1/v2 hybrid torrents (`V1V2Hybrid`, `InfoHashes.V1OrV2`, `PieceHashesV2`, `TryLoadV2HashesFromCache`), while Leecharr's multi-engine architecture also integrates `LibTorrentDownloadEngine` (libtorrent 2.0.10 C++) and `EmbeddedTransmissionEngine` for pure v2 Merkle trees. Updated `MonoTorrentDownloadEngine.Capabilities.SupportsV2Torrents = true` and verified with unit tests.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/NzbDrone.Core/BitTorrent/TorrentEngineCapabilities.cs`
    - `src/Leecharr.Core.Test/BitTorrent/MonoTorrentDownloadEngineTest.cs`

---

## Phase 5: Parity Beyond Core Engine (qBittorrent-Only & Partial Features)

- [x] **TASK-13: Built-in Torrent Creator Service & API**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Built-in Torrent Creator (`/api/v2/torrents/create`) supporting piece size selection (16 KB to 64 MB), private flags (BEP 27), comment, creator, announces, web seeds (`httpseeds` / `url-list`), and file padding.
  - **Leecharr Implementation:** Implemented `ITorrentCreationService` & `TorrentCreationService` leveraging MonoTorrent's `TorrentCreator` and `TorrentFileSource`. Supports single file and multi-file directory sources with custom or recommended piece length, BEP 27 private flag, multi-tier announces, and web seeds. Exposed `POST /api/v2/torrents/create` in `QBittorrentApiController` and added unit test coverage in `TorrentCreationServiceTest.cs`.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/Creation/ITorrentCreationService.cs`
    - `src/NzbDrone.Core/BitTorrent/Creation/TorrentCreationService.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Core.Test/BitTorrent/TorrentCreationServiceTest.cs`

- [x] **TASK-14: qBittorrent Search API Adapter (`/api/v2/search/*` bridged to Leecharr Indexers)**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Search Engine WebAPI (`/api/v2/search/start`, `/api/v2/search/status`, `/api/v2/search/results`, `/api/v2/search/stop`, `/api/v2/search/delete`, `/api/v2/search/plugins`, `/api/v2/search/categories`).
  - **Leecharr Implementation:** Created `IQBittorrentSearchService` and `QBittorrentSearchService` aggregating queries across all search-enabled Torznab indexers via `ITorznabClient`. Implemented complete qBittorrent search API surface in `QBittorrentApiController`, supporting background execution, job cancellation, pagination (`limit`/`offset`), and plugin/category listing. Added test coverage in `QBittorrentSearchServiceTest.cs`.
  - **Files:**
    - `src/NzbDrone.Core/Indexers/Search/IQBittorrentSearchService.cs`
    - `src/NzbDrone.Core/Indexers/Search/QBittorrentSearchService.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Core.Test/Indexers/QBittorrentSearchServiceTest.cs`

- [x] **TASK-15: Embedded HTTP Tracker Service (`MonoTorrent.TrackerServer` + `/announce` & `/scrape`)**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Embedded HTTP tracker listener (`BitTorrent::Tracker`) on port 9000 for local peering and private distribution.
  - **Leecharr Implementation:** Implemented `IEmbeddedTrackerService` and `EmbeddedTrackerService` managing in-memory swarm state, supporting compact peer lists (IPv4/IPv6), scrape queries, automatic stale peer reaping, and private tracker mode. Created `EmbeddedTrackerController` serving `/announce` and `/scrape` with raw binary query string parsing and mapped `enable_embedded_tracker` and `embedded_tracker_port` in `QBittorrentApiController`. Added unit tests in `EmbeddedTrackerServiceTest.cs`.
  - **Files:**
    - `src/NzbDrone.Core/BitTorrent/Tracker/IEmbeddedTrackerService.cs`
    - `src/NzbDrone.Core/BitTorrent/Tracker/EmbeddedTrackerService.cs`
    - `src/Leecharr.Api.V1/Tracker/EmbeddedTrackerController.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Core.Test/BitTorrent/EmbeddedTrackerServiceTest.cs`

- [x] **TASK-16: NAT-PMP (RFC 6886) UDP Port Forwarding Provider & Fallback Chain**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Multi-protocol port mapping: UPnP-IGD ➔ NAT-PMP (Apple Airport/pfSense/OPNsense) ➔ PCP (RFC 6887).
  - **Leecharr Implementation:** Implemented `INatPmpPortMapperService` and `NatPmpPortMapperService` adhering to RFC 6886 (gateway port 5351 UDP binary protocol). Supports default gateway auto-discovery, external IP queries (opcode 0), and TCP/UDP port mapping with lease renewals. Wired background probe into `MonoTorrentDownloadEngine.StartAsync` and covered with unit tests in `NatPmpPortMapperServiceTest.cs`.
  - **Files:**
    - `src/NzbDrone.Core/Network/PortMapping/INatPmpPortMapperService.cs`
    - `src/NzbDrone.Core/Network/PortMapping/NatPmpPortMapperService.cs`
    - `src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs`
    - `src/Leecharr.Core.Test/Network/NatPmpPortMapperServiceTest.cs`

- [x] **TASK-17: Power Management & OS Actions on Queue Completion (`systemctl` / `shutdown`)**
  - **Status:** `COMPLETED`
  - **qBittorrent Equivalent:** Auto-shutdown / sleep / hibernate when downloads or all torrents complete (`Application::allTorrentsFinished`).
  - **Leecharr Implementation:** Implemented `IPowerManagementService` and `PowerManagementService` supporting cross-platform power commands (`systemctl poweroff`/`suspend`/`hibernate` on Linux, `shutdown /s` on Windows, `pmset`/`osascript` on macOS, and container safety exit). Added settings `AutoShutdownAction` and `AutoShutdownCondition` in `ConfigService`, mapped `auto_shutdown_on_downloads_finished` in `QBittorrentApiController.GetPreferences`, and integrated automated triggering in `AppLifetime` periodic evaluation. Added unit tests in `PowerManagementServiceTest.cs`.
  - **Files:**
    - `src/NzbDrone.Core/System/IPowerManagementService.cs`
    - `src/NzbDrone.Core/System/PowerManagementService.cs`
    - `src/NzbDrone.Host/AppLifetime.cs`
    - `src/NzbDrone.Core/Configuration/ConfigService.cs`
    - `src/Leecharr.Api.V1/QBittorrent/QBittorrentApiController.cs`
    - `src/Leecharr.Core.Test/SystemServices/PowerManagementServiceTest.cs`

---

## Web UI Coverage & Integration Status

All 17 features now have end-to-end Web UI controls, REST resource mappers, and state bindings:

| Feature | Backend Controller / Service | Web UI Component / Page | Status |
| :--- | :--- | :--- | :--- |
| **Custom PiecePicker (TASK-01)** | `MonoTorrentDownloadEngine` / `PiecePicker` | `SpeedGraphTab.tsx`, `TransferBar.tsx` | Complete |
| **IP Blocklist (TASK-02)** | `MonoTorrentDownloadEngine` / `IBlocklistService` | `SecuritySettingsTab.tsx` (IP Filters) | Complete |
| **SignalR Live Telemetry (TASK-03)** | `AppLifetime` / `PieceMapSignalREventHandler` | `StatusBar.tsx`, `SpeedGraphTab.tsx`, `PieceMap.tsx` | Complete |
| **Queue Manager (TASK-04)** | `QueueManagerService` / `TorrentController` | `QueueSettingsTab.tsx` (Queue limits) | Complete |
| **Share Limit Actions (TASK-05)** | `QueueManagerService` / `TorrentService` | `QueueSettingsTab.tsx` & `OptionsTab.tsx` | Complete |
| **Incomplete File Extension (TASK-06)**| `StoragePathService` / `ConfigService` | `StorageSettingsTab.tsx` (Custom extension) | Complete |
| **SOCKS5 / HTTP Proxy (TASK-07)** | `ProxyTunnelBindingProvider` | `NetworkSettingsTab.tsx` (Proxy configuration) | Complete |
| **IPv6 Dual-Stack (TASK-08)** | `MonoTorrentDownloadEngine` | `NetworkSettingsTab.tsx` (IPv6 swarm toggle) | Complete |
| **CSRF & Host Header Security (TASK-09)**| `SecurityMiddleware` / `ConfigService` | `SecuritySettingsTab.tsx` (CSRF & Host Whitelist) | Complete |
| **File & Folder Renaming (TASK-10)** | `QBittorrentApiController` / `MonoTorrentEngine`| `FilesTab.tsx` (Inline pencil & Rename dialog) | Complete |
| **Alternative Speed Limits (TASK-11)** | `RateLimitService` / `SpeedLimitScheduleService` | `SpeedLimitSettingsTab.tsx`, `App.tsx` | Complete |
| **Execution on Completion (TASK-12)** | `ExternalScriptNotification` / `NotificationService` | `SettingsNotificationsTab.tsx` | Complete |
| **Torrent Creator (TASK-13)** | `TorrentCreationService` / `QBittorrentApiController` | `AddTorrentForm.tsx` & `AddTorrentModal.tsx` | Complete |
| **qBittorrent Search Plugins (TASK-14)** | `QBittorrentSearchService` | `AddTorrentForm.tsx` & `IndexerSearchModal.tsx`| Complete |
| **Embedded Tracker (TASK-15)** | `EmbeddedTrackerService` / `EmbeddedTrackerController` | `SettingsBitTorrentTab.tsx` | Complete |
| **NAT-PMP Port Forwarding (TASK-16)** | `NatPmpPortMapperService` | `NetworkSettingsTab.tsx` (UPnP & NAT-PMP) | Complete |
| **Auto-Shutdown Power Mgmt (TASK-17)** | `PowerManagementService` / `AppLifetime` | `QueueSettingsTab.tsx` (Power management card) | Complete |


