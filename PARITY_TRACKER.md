# Seedarr & Leecharr Cross-Repository Parity Tracker

This document tracks cross-system architectural parity, feature synchronization, and standardizations across both **Seedarr** and **Leecharr**.

---

## Parity Workpackages & Progress Status

- [x] **Workpackage 01: Containerization, Entrypoint & DevOps Parity**
  - [x] **01.1** `container-entrypoint.sh`: PUID/PGID support, umask handling, and gosu unprivileged execution.
  - [x] **01.2** `Containerfile`: gosu runtime package in Seedarr, CHANGELOG.md inclusion in Leecharr.
  - [x] **01.3** `podman-compose.yml`: PUID, PGID, UMASK environment variables for seedarr service.
  - [x] **01.4** `Makefile`: Standardize `lint` (prettier + dotnet format) and `format` targets.
  - [x] **01.5** `checks.sh`: Super-Linter suppression environment variable parity for CI dispatch.

- [x] **Workpackage 02: Shared Modals Architecture Parity**
  - [x] **02.1 Add Torrent Modal & Wizard**: Align Seedarr with Leecharr's modular tabbed architecture (File, Magnet, Indexer Search, Torrent Creation), file inspection, magnet parsing, category and folder targets.
  - [x] **02.2 Media Player Modal**: Align in-browser streaming player, video/audio codec error handling, external player protocols (`vlc://`, `mpv://`), direct download fallback, and subtitle controls.
  - [x] **02.3 Folder Browser Modal**: Standardize directory tree navigation, breadcrumb pathing, inline new folder creation, loading/error states, and accessibility focus trap.
  - [x] **02.4 Confirmation & Delete Modals**: Unify `useModalRegistration` and `useFocusTrap` integration, Escape dismissal hierarchy, and ReadOnly permission enforcement.

- [x] **Workpackage 03: Backend REST API & DTO Contract Parity**
  - [x] **03.1 System Diagnostics Endpoints**: Standardize `/api/v1/health` (async CancellationToken support), `/api/v1/system/resources` (host, engine, subsystems, per-torrent telemetry), `/api/v1/diskspace` (`FileSystemType`, `IsReadOnly`, `refresh`), `/api/v1/filesystem` (mkdir, validate).
  - [x] **03.2 Torrent Lifecycle & Batch Actions Endpoints**: Standardize `/api/v1/torrents` plural/singular route parity, `/api/v1/torrents/bulk` partial failure granularity (`SucceededIds`, `FailedIds`, `Category`).
  - [x] **03.3 Settings & Configuration Endpoints**: Standardize vacuum maintenance aliases (`/api/v1/system/database/vacuum` and `/api/v1/system/maintenance/vacuum`).
  - [x] **03.4 OpenAPI / Swagger Schema Validation**: Align Swagger UI dark theme injection (`/swagger-custom.css` endpoint and `SwaggerTheme.cs`) across both solutions.

- [x] **Workpackage 04: Torrent Detail Drawer & Visualization Parity**
  - [x] **04.1 Piece Map Canvas Visualizer**: Unify HTML5 canvas visualizer supporting Linear Bar and Grid Block modes, streaming bitfield updates, and piece index tooltips.
  - [x] **04.2 Files Tab Tree & Selective Download Priorities**: Hierarchical file and folder tree navigation with selective download checkboxes and file priorities (Skip, Low, Normal, High).
  - [x] **04.3 Peers Tab Swarm Flags & Protocol Inspection**: Virtualized peer table, GeoIP country flags, client identification badges, encryption status, and BEP 27 protocol flags.
  - [x] **04.4 Logs Tab & CLI Shell Scoping**: Torrent-scoped logging view and embedded terminal shell integration.

- [x] **Workpackage 05: SignalR Real-Time Protocol & Store Lifecycle Parity**
  - [x] **05.1 Event Topic Normalization & Dispatching**: Normalize hub event names (`torrent_updated`, `speed_update`, `task_progress`, `health_warning`) across both solutions.
  - [x] **05.2 Reconnection Banners & Offline Detection**: Consistent visual indicator for disconnected, connecting, and reconnected SignalR states.
  - [x] **05.3 Zustand Store Hydration & Persistence**: Standardize localStorage schema keys, optimistic state updates, and hydration guards.

- [x] **Workpackage 06: Notifications & Servarr Integrations Parity**
  - [x] **06.1 Notification Provider Templates & Tokens**: Standardize message formatting and token variables (`{Torrent.Title}`, `{Torrent.Size}`) for Discord, Telegram, Webhook, Pushover, Apprise.
  - [x] **06.2 Torznab/Newznab Indexer Connectors**: Standardize test connection routines, query syntax, category mappings, and RSS sync intervals.
  - [x] **06.3 External Client Synchronization & Import**: Parity for remote download client bridging (qBittorrent, Deluge, Transmission).

- [x] **Workpackage 07: Code Quality, Linters & CI/CD Pipelines**
  - [x] **07.1 GitHub Actions Multi-Arch CI Matrix**: Standardize multi-arch container image workflows (`linux/amd64`, `linux/arm64`), concurrency cancellations, and release draft automation.
  - [x] **07.2 StyleCop, Prettier, ESLint & Pre-Commit Rules**: Align C# Analyzer rules (`stylecop.json`, `.editorconfig`), ESLint configurations, and Prettier formatting rules.
  - [x] **07.3 Release & Versioning Automation**: Synchronize Makefile targets (`quality-report`, `bump-patch`, `bump-minor`, `bump-major`), `version` file bumping, and CHANGELOG generation.

- [x] **Workpackage 08: Test Automation Parity**
  - [x] **08.1 Frontend Test Automation**: Automated unit and utility test suites (`tsx --test`) for magnet parsing, milestone calculation, HnR clearance, buffer deficit tracking, fuzzy search, arr instance linking, media player URL & badge extraction, and role permissions.
  - [x] **08.2 Backend Unit & Integration Tests**: Test parity across `NzbDrone.Core.Test` and `Leecharr.Core.Test`, including system resource telemetry, host process metrics, engine stats, and torrent streaming controller fixtures.
  - [x] **08.3 Mock Swarms & Fixtures**: Reusable test fixtures, mock swarm piece picking, and streaming controller test suites across both solutions.

- [x] **Workpackage 09: Advanced Filtering, Settings Protection & Security Parity**
  - [x] **09.1 Filter Pipeline & Tag Match Modes**: Unified filter predicate utility (`filterUtils.ts` and `filterUtils.test.ts`), supporting AND/OR tag matching modes, untagged filtering, and multi-tag filtering across both table and grid views.
  - [x] **09.2 Unsaved Settings Form Guard**: Standardized `SettingsDirtyContext` and unsaved changes badge/prompt across all settings tabs and subpages, with 20-locale dictionary synchronization.
  - [x] **09.3 Peer IP Blocklist API & Security UI Parity**: Parity for IP blocklist engine endpoints (`/api/v1/blocklist`, `/api/v1/blocklist/sync`, `/api/v1/blocklist/test`) and security settings card featuring auto-update interval scheduling, manual blocklist synchronization, and interactive IP diagnostics test utility.

- [x] **Workpackage 10: Portable Package Import/Export & Diagnostics Health Check Parity**
  - [x] **10.1 Portable Package Engine & REST Endpoints**: Implemented cross-solution `.leecharr` and `.seedarr` tar/gzip package packaging and extraction engines (`PackageManifest.cs`, `PackagePathTranslator.cs`, `PackageExportService.cs`, `PackageImportService.cs`) and ASP.NET Core controllers (`/api/v1/packages/export`, `/api/v1/packages/import`).
  - [x] **10.2 Frontend Package Import Modal & Context Export**: Standardized `ImportPackageModal.tsx` drag-and-drop wizard, cross-platform path remapping (Windows ↔ Linux), `📦 Export Package` in `TorrentContextMenu.tsx`, and `📦 Import Package` in `TorrentToolbar.tsx` with full 20-language i18n synchronization.
  - [x] **10.3 Critical System Diagnostics Health Checks**: Ported `DatabaseIntegrityCheck.cs` (SQLite PRAGMA quick_check verification), `AppFolderPermissionsCheck.cs` (AppData, logs, backups write permission auditing), and `FileDescriptorExhaustionCheck.cs` (`IFileDescriptorProvider` / Linux `/proc/self/fd` soft limit exhaustion detection).

- [x] **Workpackage 11: Dedicated Tracker Telemetry, Embedded Server & Subsystem Matrix Parity**
  - [x] **11.1 Tracker Metrics Engine & Telemetry Controllers**: Ported SQLite migration `037_add_tracker_metrics.cs` to Leecharr with tables `TrackerMetrics` and `TrackerMetricSnapshots`. Implemented `ITrackerMetricService`, `TrackerMetricService`, channel-based non-blocking snapshot queuing, P50/P95/P99 latency calculations, hourly traffic bucketing, and REST endpoints `/api/v1/trackermetrics` (`/summary`, `{id}`, `{id}/history`, `{id}/reset`).
  - [x] **11.2 Embedded Tracker Server Swarms & Peer Inspection**: Enriched Leecharr's `EmbeddedTrackerController` with `internalTorrents` count, full metadata enrichment (posters, backdrops, ratings, genres, size, ratio) linked against `ITorrentRepository` and `IDownloadHistoryRepository`, and added `/api/v1/trackerserver/torrents/{infoHash}/peers` inspection endpoint.
  - [x] **11.3 Pluggable Subsystems Matrix & Runtime Hot-Swapping**: Ported `SubsystemsController.cs` (`/api/v1/subsystems`, `/metrics`, `{subsystemId}/probe`, `{subsystemId}/switch`) and `SubsystemsTab.tsx` to Seedarr with full category chips, provider cards, capabilities chips, and atomic hot-swap confirmation modal, synchronized across all 20 locale dictionaries.

