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

- [ ] **Workpackage 03: Backend REST API & DTO Contract Parity**
  - [ ] **03.1 System Diagnostics Endpoints**: Standardize `/api/v1/health`, `/api/v1/status`, `/api/v1/task`, `/api/v1/log`, `/api/v1/backup`, `/api/v1/update`, `/api/v1/diskspace`.
  - [ ] **03.2 Torrent Lifecycle & Batch Actions Endpoints**: Standardize `/api/v1/queue`, `/api/v1/torrent`, `/api/v1/history`, `/api/v1/command` for batch operations (pause, resume, delete, force recheck, queue reorder).
  - [ ] **03.3 Settings & Configuration Endpoints**: Standardize `/api/v1/config/host`, `/api/v1/config/ui`, `/api/v1/downloadclient`, `/api/v1/indexer`, `/api/v1/notification`, `/api/v1/tag`.
  - [ ] **03.4 OpenAPI / Swagger Schema Validation**: Generate and synchronize complete Swagger / OpenAPI v3 endpoint definitions and schema models.

- [ ] **Workpackage 04: Torrent Detail Drawer & Visualization Parity**
  - [ ] **04.1 Piece Map Canvas Visualizer**: Unify HTML5 canvas visualizer supporting Linear Bar and Grid Block modes, streaming bitfield updates, and piece index tooltips.
  - [ ] **04.2 Files Tab Tree & Selective Download Priorities**: Hierarchical file and folder tree navigation with selective download checkboxes and file priorities (Skip, Low, Normal, High).
  - [ ] **04.3 Peers Tab Swarm Flags & Protocol Inspection**: Virtualized peer table, GeoIP country flags, client identification badges, encryption status, and BEP 27 protocol flags.
  - [ ] **04.4 Logs Tab & CLI Shell Scoping**: Torrent-scoped logging view and embedded terminal shell integration.

- [ ] **Workpackage 05: SignalR Real-Time Protocol & Store Lifecycle Parity**
  - [ ] **05.1 Event Topic Normalization & Dispatching**: Normalize hub event names (`torrent_updated`, `speed_update`, `task_progress`, `health_warning`) across both solutions.
  - [ ] **05.2 Reconnection Banners & Offline Detection**: Consistent visual indicator for disconnected, connecting, and reconnected SignalR states.
  - [ ] **05.3 Zustand Store Hydration & Persistence**: Standardize localStorage schema keys, optimistic state updates, and hydration guards.

- [ ] **Workpackage 06: Notifications & Servarr Integrations Parity**
  - [ ] **06.1 Notification Provider Templates & Tokens**: Standardize message formatting and token variables (`{Torrent.Title}`, `{Torrent.Size}`) for Discord, Telegram, Webhook, Pushover, Apprise.
  - [ ] **06.2 Torznab/Newznab Indexer Connectors**: Standardize test connection routines, query syntax, category mappings, and RSS sync intervals.
  - [ ] **06.3 External Client Synchronization & Import**: Parity for remote download client bridging (qBittorrent, Deluge, Transmission).

- [ ] **Workpackage 07: Code Quality, Linters & CI/CD Pipelines**
  - [ ] **07.1 GitHub Actions Multi-Arch CI Matrix**: Standardize multi-arch container image workflows (`linux/amd64`, `linux/arm64`) and release draft automation.
  - [ ] **07.2 StyleCop, Prettier, ESLint & Pre-Commit Rules**: Align C# Analyzer rules (`stylecop.json`), ESLint configurations, and Prettier formatting rules.
  - [ ] **07.3 Release & Versioning Automation**: Synchronize Makefile targets, `version` file bumping, and CHANGELOG generation.

- [ ] **Workpackage 08: Test Automation Parity**
  - [ ] **08.1 Frontend Vitest Component Suites**: Automated tests for critical modals, virtualized tables, and navigation.
  - [ ] **08.2 Backend Unit & Integration Tests**: Parity across `NzbDrone.Core.Test` and `NzbDrone.Integration.Test`.
  - [ ] **08.3 Mock Swarms & Fixtures**: Reusable test fixtures and integration test stacks.
