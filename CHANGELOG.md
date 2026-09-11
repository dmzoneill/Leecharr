# Changelog

All notable changes to **Leecharr** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [v1.5.1](https://github.com/dmzoneill/Leecharr/releases/tag/v1.5.1) - 2026-09-11

### 🐛 Bug Fixes
- fix(engine,health): periodically collect LOH piece buffers during hash check and prevent false memory exhaustion alerts

## [v1.5.0](https://github.com/dmzoneill/Leecharr/releases/tag/v1.5.0) - 2026-09-11

### ✨ Features
- feat(download,storage): add auto-recheck on completion setting, fix force-recheck completed path alignment, and prevent sparse completion move

## [v1.4.4](https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.4) - 2026-09-11

### 🐛 Bug Fixes
- fix(i18n): remove irregular unicode whitespace and format frontend styles
- fix(update,ui): add timeout and fallback for update check, fix i18n translation types and formatting
- fix(core,api): enhance process execution safety, udp tracker scraping loop, and upsert retry integrity
- fix(ui,i18n): improve modal focus safety, restore translation parameters, and fix light theme contrast

### 🔧 Maintenance & Improvements
- ci: rerun with one-liner changelog updater
- ci: rerun with valid dispatch.yaml indentation
- ci: trigger CI workflow with fixed version bumper
- docs: add CHANGELOG.md, overhaul DOCKER_HUB.md, and expand update ingestion

## [v1.0.121](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.121) - 2026-09-10

### ✨ Features
- feat(frontend): implement slim icon sidebar and user profile dropdown menu

---

## [v1.0.120](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.120) - 2026-09-10

### ✨ Features
- feat(frontend): transition to monochromatic slate-blue theme and align topbar actions (#704, #705)

### 🔧 Maintenance & Improvements
- style: remove all zero-width and irregular unicode whitespace in translation memory
- style: fix irregular whitespace in translation memory
- style: format frontend files with prettier

---

## [v1.0.119](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.119) - 2026-09-09

### 🐛 Bug Fixes
- fix(ci): format README with prettier and revert workflow input

### 🔧 Maintenance & Improvements
- ci: disable MARKDOWN_PRETTIER and SPELL_CODESPELL in checks.sh
- ci: disable VALIDATE_MARKDOWN_PRETTIER in workflow
- docs(readme): add footer layout with website url
- docs(logo): add application screenshot asset
- docs(readme): add application screenshot before key features section

---

## [v1.0.118](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.118) - 2026-09-09

### 🐛 Bug Fixes
- fix(backup): purge stale WAL/SHM on restore and merge staged WAL in staging directory

### 🔧 Maintenance & Improvements
- chore(container): retain all engines and runtimes while optimizing size with lean runtime libraries
- chore(container): reduce image size from 350MB to 195MB with Alpine runtime and lean migrations

---

## [v1.0.117](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.117) - 2026-09-09

### 🔧 Maintenance & Improvements
- refactor(frontend): improve TypeScript type safety and error typing

---

## [v1.0.116](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.116) - 2026-09-09

### 🐛 Bug Fixes
- fix(engine,deluge): serialize vpn transitions, update completed working path, and parse deluge options
- fix(engine): support dual-stack DNS fallback, safe engine stop, and picker endgame caching

### 🔧 Maintenance & Improvements
- refactor(frontend): decompose DownloadHistory into modular subcomponents
- refactor(host): consolidate AppLifetime dependencies into parameter object
- refactor(common): improve drive inspection error handling and container type filtering
- refactor(aria2): consolidate shared JSON-RPC and XML-RPC execution logic
- refactor(frontend): improve TypeScript type safety and error typing
- refactor(trackerboost): decompose HarvestFromProwlarrAsync and extract JSON parser
- build(quality): add zero-token quality-report target with jscpd, type-coverage, and roslynator
- refactor(media-inspection): decompose InspectMatroska into modular EBML element parsers
- refactor(frontend): decompose AddTorrentForm into tabbed subcomponents
- refactor(deluge): decompose ProcessSingleRpcAsync into domain dispatchers
- refactor(qbit): simplify QBitTorrentSnapshot and decompose AddTorrents method
- refactor(transmission): decompose MapTorrentToTransmission into domain mappers
- refactor(sabnzbd): decompose monolithic HandleApi into mode sub-handlers

---

## [v1.0.115](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.115) - 2026-09-08

### 🐛 Bug Fixes
- fix(tests): block both telemetry and guaranteed workers in SignalRMessageBroadcasterTest
- fix(routing): remove top-level route collisions in FloodApiController to preserve SPA fallback routing for /torrents

---

## [v1.0.114](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.114) - 2026-09-08

### 🐛 Bug Fixes
- fix(db): generate direct integer IN clause in TorrentFileRepository.GetByTorrentIds to ensure correct grouping
- fix(db): wrap SQL IN clause in parentheses for SQLite Dapper compatibility and ensure default tracker boost bootstrapping
- fix(ci): resolve unit and integration test failures across backup, deluge, qbittorrent, and di services
- fix(tests): eliminate hardcoded Thread.Sleep in ArchiveExtractorEventHandlerTest (fixes #697)
- fix(health): eliminate sync-over-async PerformChecks method from IHealthCheckService (fixes #703)
- fix(ai): eliminate sync-over-async blocking methods from IAiService and DynamicAiProxy (fixes #700)
- fix(queue): move event dispatch outside queueLock and guard against recursive self-events (fixes #699)
- fix(qbittorrent): encapsulate AddTorrents parameters into QBitAddTorrentsRequest form model (fixes #696)
- fix(indexers): encapsulate IndexerController search endpoints into IndexerSearchRequest model (fixes #694)
- fix(indexers): encapsulate TorznabClient.SearchAsync parameters into TorznabSearchCriteria (fixes #687)
- fix(rpc): decompose monolithic switch dispatchers into dedicated handler routines (fixes #686)
- fix(core): move event publishing outside async switchLock in dynamic proxies (fixes #691)
- fix(trackerboost): eliminate constructor DB side-effects and encapsulate mutable state in ITrackerBoostStateStore (fixes #693)
- fix(backup): eliminate sync-over-async and deadlock from unconsumed stdout in pg_dump/psql routines (fixes #690)
- fix(frontend): consolidate HTTP communication onto ApiClient and remove fetchJson (fixes #685)
- fix(organizer): eliminate static delegation anti-pattern and utilize injected IFileNameSanitizer and IPathTruncator in FileNameBuilder (fixes #698)
- fix(frontend): consolidate telemetry aggregation loops into useAggregatedTorrentMetrics hook (fixes #684)
- fix(terminal): prioritize LinuxPtySession native POSIX PTY on Linux platforms (fixes #702)
- fix(api): eliminate synchronous File.Exists disk stats in DTO resource mapping (fixes #683)
- fix(auth): replace direct HttpClient instantiation with ISafeHttpClientService and SSRF validation in IdentityProviderService (fixes #701)
- fix(auth): enforce bounded capacity and background expiration sweeper in AuthRateLimiter (fixes #688)
- fix(torrents): introduce reference-counted keyed lock for DeleteAsync (fixes #679)
- fix(qbittorrent): batch load torrent files in polling loops to eliminate N+1 queries (fixes #682)
- fix(vpn): implement IDisposable lifecycle and unhook static NetworkChange events in VpnKillSwitchService (fixes #680)
- fix(api): eliminate database write in GetTrackers query (fixes #681)
- fix(engine): invoke BlockCancelled outside monitor lock in PiecePicker (fixes #678)

### 🔧 Maintenance & Improvements
- refactor(core): reduce constructor bloat and class coupling across core services (fixes #695)
- refactor(notifications): decompose monolithic NotificationEventHandler God Class into IEpisodicParser, NotificationPayloadBuilder, and EmailNotificationSender (fixes #689)

---

## [v1.0.113](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.113) - 2026-09-08

### ✨ Features
- feat(api): implement missing qbittorrent endpoints for reannounce, sequential download, piece priority, and rename (fixes #391)

### 🐛 Bug Fixes
- fix(ci): await terminal ws background tasks and remove artificial session timeout
- fix(ci): add default test timeout in runsettings, non-blocking control pipe, and cancel handling in tests
- fix(ci): fix Swagger NonAction, BackupController restore WAL purge, and AppLifetimeTest shutdown order
- fix(filebrowser): suppress duplicate internal delete modal and use single ConfirmModal
- fix(frontend): bind StatusBar, Activity, Statistics and SpeedGraph to live useTorrentStore telemetry (fixes #677)
- fix(ci): resolve test regressions, missing migration 028, and FloodApiController action routing
- fix(api): populate live tracker statistics and resolve GeoIP peer countries in Flood API (fixes #675, fixes #676)
- fix(frontend): add missing health filter selector in RadarView toolbar (fixes #674)
- fix(datastore): expose ExecuteWithRetry in BasicRepository and wrap derived repo queries with SQLite lock retry policy
- fix(notifications): support query-string formatted settings in SendEmailNotification
- fix(notifications): decouple downloadTimeSeconds from seeding duration and expose seedingTimeSeconds in webhook payload
- fix(indexers): fetch from offset 0 up to effective limit across multi-indexer search and paginate globally
- fix(enrichment): serialize guessed MediaContainerInfo into MediaInfoJson when file is not yet on disk
- fix(network): inject IVpnKillSwitchService and populate VpnKillSwitchActive in NetworkStatusService
- fix(rtorrent): add synonym method handlers for d.set_custom1, d.set_directory, d.set_directory_base, d.set_priority
- fix(utorrent): support multi-hash batch operations for queue actions
- fix(network): bind UDP datagram sockets to ephemeral port when localPort is 0
- fix(sabnzbd): sort queue by QueuePosition and history by completion date descending before pagination
- fix(flood): map stopped incomplete torrents to stopped instead of complete
- fix(torrent): refresh in-flight block timestamp on timeout re-request in piece picker
- fix(qbittorrent): decouple first/last piece priority from sequential download and expose in api and sync/maindata
- fix(api): preserve batch relative ordering for core.queue_top, core.queue_up, core.queue_down in DelugeJsonRpcController (fixes #657)
- fix(nzbget): preserve batch queue priority order in editqueue groupmovetop (fixes #659)
- fix(indexers): pass cancellation token and guard against ObjectDisposedException on StopSearch in QBittorrentSearchService (fixes #661)
- fix(api): bind paused, isPaused, and startPaused form parameters in TorrentController.Upload (fixes #658)
- fix(indexers): parse torznab:attr cat and split delimited category values in ParseTorznabFeedXml (fixes #660)
- fix(bittorrent): prevent double-bind SocketException in BoundSocketConnector and pass localPort to INetworkBindingProvider (fixes #654)
- fix(migrations): guard ForeignKeys in Migration 027 with PostgreSQL provider check
- fix(network): match interface by id and validate unicast ip in NetworkSecurityService.IsInterfaceActive (fixes #655)
- fix(api): implement missing qBittorrent endpoints, Deluge batch methods and status keys, and Transmission unknown method fallback (fixes #570)
- fix(terminal): pre-marshal native pointers to enforce async-signal-safety post-forkpty (fixes #419)
- fix(frontend): define --bg-card, --bg-card-hover, and --bg-lighter in light theme (fixes #513)
- fix(a11y): add focus trapping, focus restoration, and ARIA dialog attributes to frontend modals (fixes #429)
- fix(frontend): calculate column drag-and-drop reordering relative to visible columns (fixes #430)
- fix(frontend): clean up uncancelled timeout handles in TerminalView on unmount (fixes #431)
- fix(frontend): handle concurrent confirm dialogs with a FIFO queue in ConfirmContext (fixes #428)
- fix(bandwidth): decouple download and upload pause states in EffectiveSpeedLimits (fixes #519)
- fix(notifications): normalize Gotify endpoint and inject X-Gotify-Key header (fixes #385)
- fix(tracker): normalize dual-stack IPv4-mapped IPv6 addresses in embedded tracker (fixes #512)
- fix(frontend): add fallback translation helper in getNotificationSummary (fixes #386)
- fix(network): format IPv6 host literals with brackets in HTTP CONNECT proxy handshake (fixes #387)
- fix(api): implement Flood activity-stream SSE endpoint and route aliases (fixes #389)
- fix(network): require OperationalStatus.Up in GetInterfaceIp and filter out non-global IPv6 (fixes #388)
- fix(qbittorrent): include savePath in sync/maindata categories dictionary (fixes #390)
- fix(api): map Flood 3-level file priorities and implement set-location and reannounce endpoints (fixes #392)
- fix(indexers): evaluate tmdbId into movie search mode in TorznabClient (fixes #393)
- fix(rpc): support rTorrent tracker announce and bandwidth throttle setters (fixes #394)
- fix(bittorrent): prioritize NetworkInterfaceBinding in BoundSocketConnector interface resolver (fixes #396)
- fix(nzbget): add listfiles method handler to JSON-RPC dispatch in NzbgetRpcController (fixes #397)
- fix(rpc): dynamic queue status, paused flag, and numeric telemetry in SABnzbd API (fixes #398)
- fix(network): handle 8-byte NAT-PMP error responses and preserve epoch tracking (fixes #401)
- fix(bittorrent): append partial file extension during preallocation when AppendIncompleteExtension is enabled (fixes #399)
- fix(ui): handle bracketed IPv6 with ports, IPv4-mapped IPv6, and localhost ports in isPrivateIp (fixes #400)
- fix(system): use graceful host application shutdown and expand container runtime detection (fixes #402)
- fix(geoip): cascade fallback to online provider on unpopulated lookup and support country MMDB (fixes #403)
- fix(telemetry): use monotonic timestamp for CPU usage sampling in SystemResourceService (fixes #404)
- fix(tasks): declare missing Command classes and IExecute/IExecuteAsync handlers for system tasks (fixes #410)
- fix(auth): add cascade delete foreign keys for UserSessions and UserExternalLogins (fixes #411)
- fix(security): add DNS SAN for hostname BindAddress and derive probeHost in CertificateManager (fixes #405)
- fix(security): dispose ECDsa cryptographic handle deterministically in AppleClientSecretGenerator (fixes #406)
- fix(network): validate remote endpoint and loop on non-matching datagrams in NatPmpPortMapperService (fixes #412)
- fix(messaging): fail stale running commands on startup sweep in CommandQueueManager (fixes #409)
- fix(security): create PTY control FIFO with 0600 user-only permissions in PtyProcessSession (fixes #407)
- fix(security): enforce fail-closed terminal permission checks in PtyTerminalService (fixes #408)
- fix(network): track per-gateway epochs and deduplicate reboot renewals in NatPmpPortMapperService (fixes #413)
- fix(system): implement OS sleep inhibition in PowerManagementService (fixes #422)
- fix(security): sanitize child terminal process environment variables in PtyProcessSession (fixes #418)
- fix(network): prioritize physical network interface in NatPmpPortMapperService.DiscoverDefaultGateway (fixes #414)
- fix(watchfolder): subscribe to Changed event and debounce file ready in WatchFolderService (fixes #420)
- fix(telemetry): cache disk mount point metrics in SystemResourceService (fixes #426)
- fix(security): cap self-signed certificate validity to 397 days and attach standard X.509 extensions in CertificateManager (fixes #417)
- fix(watchfolder): decouple post-import file cleanup from failure quarantine in WatchFolderService (fixes #421)
- fix(network): prevent ObjectDisposedException during lifecycle shutdown in NatPmpPortMapperService (fixes #415)
- fix(terminal): use stateful UTF-8 decoder in TerminalWebSocketHandler (fixes #416)
- fix(torrents): normalize infohash case in TorrentService.AddFromParsedTorrentAsync (fixes #424)
- fix(watchfolder): use normalized path and platform string comparer for failedAttempts in WatchFolderService (fixes #423)
- fix(backup): verify archive integrity before replacing active database in BackupController.Restore (fixes #432)
- fix(bittorrent): rehydrate torrents on engine switch rollback in DynamicDownloadEngineProxy (fixes #438)
- fix(telemetry): decouple process memory refresh from CPU sampling window in SystemResourceService (fixes #427)
- fix(bittorrent): preserve SequentialDownload configuration in DynamicDownloadEngineProxy (fixes #439)
- fix(watchfolder): evaluate AnimePattern before MoviePattern in category classifier (fixes #425)
- fix(blocklist): optimize RadixTree with subtree pruning and ancestor shortcutting (fixes #436)
- fix(backup): enforce retention limit and prune old archives in BackupController (fixes #433)
- fix(blocklist): stream blocklist lines lazily to prevent OOM in BlocklistUpdateService (fixes #437)
- fix(indexers): synchronize RSS sync execution with SemaphoreSlim in RssSyncService (fixes #446)
- fix(indexers): bound grabbed releases cache capacity in RssSyncService (fixes #449)
- fix(bittorrent): guard against integer overflow in MonoTorrent rate limits (fixes #434)
- fix(core): batch database updates and synchronize concurrency in TorrentService.MoveQueueAsync (fixes #440)
- fix(tags): cascade delete tag IDs from torrents and notifications in TagController (fixes #441)
- fix(bandwidth): allow active schedule zero limit to override global limits in SpeedSchedulerService (fixes #435)
- fix(storage): enforce cross-platform invalid character checks in TorrentPathValidator (fixes #442)
- fix(inspection): handle exceptions and fall back to TagLib in DynamicMediaInspectorProxy.Inspect stream (fixes #445)
- fix(bittorrent): support BEP 53 indexed trackers and BEP 17 web seeds in MagnetLinkParser (fixes #443)
- fix(auth): prune expired login attempts and cap tracked IPs in AuthController (fixes #447)
- fix(rpc): propagate session in HttpContext.Items for Deluge multicall auth.login (fixes #452)
- fix(indexers): handle RFC 822 timezone abbreviations and assume universal in TorznabClient (fixes #448)
- fix(bittorrent): fix BEP 52 v2 multihash length validation in MagnetLinkParser (fixes #444)
- fix(rpc): support ID/hash filters and array values in Deluge get_torrents_status (fixes #453)
- fix(indexers): ignore invalid MustNotContain regex without rejecting releases in RssSyncService (fixes #450)
- fix(rpc): implement missing methods in Aria2RpcController (fixes #458)
- fix(rpc): safely handle null and non-string values in Deluge options (fixes #455)
- fix(signalr): clear pending pieces when disconnected in PieceMapSignalREventHandler (fixes #451)
- fix(rpc): populate standard metrics in Deluge MapTorrentToDelugeStatus (fixes #456)
- fix(rpc): support number and string types for Aria2 speed limit options (fixes #459)
- fix(rpc): support start and limit pagination in SABnzbd queue and history (fixes #462)
- fix(rpc): preserve relative parent paths and support folder renames in Transmission torrent-rename-path (fixes #457)
- fix(rpc): support pagination in aria2.tellWaiting and aria2.tellStopped (fixes #460)
- fix(rpc): separate priority updates from queue movements in SABnzbd API (fixes #463)
- fix(rpc): support GroupSetPriority and HistoryReturn actions in NZBGet editqueue (fixes #464)
- fix(datastore): ensure deterministic Id ordering in TorrentFileRepository.GetByTorrentId (fixes #461)
- fix(rpc): support log and loadlog methods in NZBGet JSON-RPC (fixes #465)
- fix(rpc): support downloadPath and cookie parameters in qBittorrent AddTorrents (fixes #467)
- fix(rpc): support tag query and complete status filters in qBittorrent GetTorrentsInfo (fixes #466)
- fix(rpc): prevent spurious full_updates on empty queue and preserve delta sync in qBittorrent maindata (fixes #468)
- fix(rpc): resume torrent in download engine on qBittorrent setForceStart (fixes #469)
- fix(indexers): support default XML namespaces in TorznabClient.ParseCapabilitiesXml (fixes #470)
- fix(indexers): cap upstream limits and handle multi-indexer search pagination properly (fixes #471)
- fix(indexers): prevent duplicate indexer creation in ProwlarrSyncService (fixes #472)
- fix(media): handle active provider exceptions and proceed to fallbacks in DynamicMediaMetadataProxy (fixes #474)
- fix(frontend): replace double delete prompt with single unified DeleteTorrentModal
- fix(notifications): dispatch system-level health alerts when torrent is null (fixes #476)
- fix(media): ensure unconditional local cache cleanup in MediaEnrichmentService (fixes #475)
- fix(rpc): resolve numeric and subcategory IDs in QBittorrentSearchService (fixes #473)
- fix(extraction): disable interactive password prompts in 7-Zip and UnRAR extractors (fixes #482)
- fix(notifications): parse JSON settings in CustomScriptService (fixes #477)
- fix(notifications): propagate SMTP errors on email test notification (fixes #478)

### 🔧 Maintenance & Improvements
- test(terminal): use TestTerminalSession in multi-byte UTF-8 split tests to prevent mock race conditions
- test: return Close frame when FakeWebSocket messages are exhausted
- ci(test): add console normal verbosity logger to make test and make integration
- ci: fix unit test assertions and handle cancellation in activity stream
- ci: enable test switch (test: true) in CICD workflow
- perf(signalr): short-circuit message broadcast when no clients are connected (fixes #454)

---

## [v1.0.112](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.112) - 2026-09-08

### 🐛 Bug Fixes
- fix(extraction): check available free disk space before extracting archives (fixes #484)
- fix(extraction): handle trailing separators in UnRAR and 7-Zip destination paths (fixes #483)
- fix(notifications): sanitize non-finite ratio values in webhook dispatcher (fixes #479)
- fix(signalr): await processing task before disposing CancellationTokenSource in SignalRMessageBroadcaster (fixes #480)
- fix(bittorrent): format client emulation HTTP User-Agent and extended handshake correctly
- fix(api): broadcast SignalR messages on SpeedSchedule CRUD operations (fixes #481)
- fix(frontend): align sparse SpeedGraph timeline to present and scope gradient IDs (fixes #490)
- fix(extraction): prevent Zip Slip directory traversal in SharpCompressExtractorProvider (fixes #485)
- fix(storage): handle trailing directory separators properly in FileBrowserService (fixes #486)
- fix(network): inspect NetworkInterfaceBinding in NetworkSecurityService (fixes #487)
- fix(indexers): decode HTML entities in TorznabClient release titles (fixes #492)
- fix(auth): adjust prefix length for IPv4-mapped IPv6 CIDRs in TrustedNetworkService (fixes #488)
- fix(frontend): clamp canvas dimensions to prevent DOMException on empty PieceMap (fixes #489)
- fix(torrents): prevent premature 100% progress on incomplete files in TorrentFileProgressEnricher (fixes #494)
- fix(system): fix Windows suspend P/Invoke and macOS osascript arguments in PowerManagementService (fixes #496)
- fix(security): support bracketed IPv6, ULA, CGNAT, and wildcard host patterns in HostHeaderValidationMiddleware (fixes #493)
- fix(ssl): load intermediate CA certificate chain and normalize PEM to PKCS#12 (fixes #497)
- fix(security): remove permissive substring match in CsrfProtectionMiddleware.IsAuthPath (fixes #495)
- fix(auth): prevent account hijacking and disambiguate usernames in JitUserProvisioningService (fixes #501)
- fix(ai): preserve title years and essential words in natural language search (fixes #503)
- fix(auth): preserve client secrets containing asterisks during identity provider updates (fixes #502)
- fix(ai): handle non-boolean json tokens safely and escape uri parameters in ai providers (fixes #510)
- fix(terminal): reap child shell process and send SIGHUP on disconnect in PtyProcessSession (fixes #498)
- fix(rpc): apply speed limits via SpeedSchedulerService in qBittorrent speed limit endpoints (fixes #499)

---

## [v1.0.111](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.111) - 2026-09-08

### 🐛 Bug Fixes
- fix(bittorrent): emit standard peer transfer and uTP flags in GetPeers (fixes #509)
- fix(ai): await provider switch and prevent configuration desynchronization on failure (fixes #511)
- fix(frontend): invalidate correct torrent trackers query key in tracker boost hooks (fixes #508)

---

## [v1.0.110](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.110) - 2026-09-08

### 🐛 Bug Fixes
- fix(datastore): make DownloadHistoryRepository.GetHistory case-insensitive for PostgreSQL compatibility (fixes #504)

---

## [v1.0.109](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.109) - 2026-09-08

### 🐛 Bug Fixes
- fix(auth): invalidate user sessions on password change, user deletion, and role updates

---

## [v1.0.108](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.108) - 2026-09-08

### 🐛 Bug Fixes
- fix(api): add null guard to ConfigControllerBase.SaveConfig to prevent 500 on empty PUT body (fixes #506)
- fix(datastore): ensure table registration and type handlers are initialized in DbFactory (fixes #505)

---

## [v1.0.107](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.107) - 2026-09-08

### 🐛 Bug Fixes
- fix(backup): drain process streams, terminate orphaned pg_dump/psql processes on timeout, and handle exit codes safely (fixes #515)
- fix(watchfolder): update TvPattern regex for daily releases and multi-digit episodes (fixes #514)

---

## [v1.0.106](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.106) - 2026-09-08

### 🐛 Bug Fixes
- fix(terminal): prevent InvalidOperationException and JSON stdin leak in WebSocket handler (fixes #516)
- fix(indexers): resolve torznab peers and leechers accurately without attribute order dependency (fixes #517)
- fix(auth): authenticate database users and assign claims in BasicAuthenticationHandler (fixes #518)

---

## [v1.0.105](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.105) - 2026-09-08

### 🐛 Bug Fixes
- fix(tracker): honor numwant=0 in announce responses conforming to BEP 3 and BEP 15 (fixes #520)

---

## [v1.0.104](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.104) - 2026-09-08

### 🐛 Bug Fixes
- fix(network): filter out link-local and site-local ipv6 addresses for listening endpoints (fixes #521)
- fix(engines): complete sidecar engine tracker and file controls, peer parsing, and dynamic nat-pmp mappings (fixes #571)

---

## [v1.0.103](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.103) - 2026-09-08

### 🐛 Bug Fixes
- fix(security): exempt rpc routes from csrf and broadcast subsystem switch events (fixes #572)
- fix(media): implement tvdb v4 provider, usenet prowlarr sync, and binary hdr10+ sei parsing (fixes #573)

---

## [v1.0.102](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.102) - 2026-09-08

### 🐛 Bug Fixes
- fix(frontend): mount radar bulk import modal and decouple i18n labels from backend enums (fixes #574)

---

## [v1.0.101](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.101) - 2026-09-08

### 🐛 Bug Fixes
- fix(compat): dynamic disk space metrics, configurable torrent size limit, and aria2 pagination (fixes #576)
- fix(engine): dynamic piecepicker thresholds, real swarm metrics, and configurable path fallbacks (fixes #575)

---

## [v1.0.100](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.100) - 2026-09-08

### 🐛 Bug Fixes
- fix(media): replace synthetic subsystem telemetry, hardcoded ratings, and bind process timeouts (fixes #577)

---

## [v1.0.99](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.99) - 2026-09-08

### 🐛 Bug Fixes
- fix(frontend): optimize piecemap canvas rendering, dynamic chart scaling, and unify polling intervals (fixes #578)

---

## [v1.0.98](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.98) - 2026-09-08

### 🐛 Bug Fixes
- fix(core): register command worker hosted service, eliminate sync-over-async, and isolate signalr telemetry channels (fixes #579)
- fix(security): support lan allowlist in safehttpclient and prevent socks5 proxy leaks (fixes #580)

---

## [v1.0.97](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.97) - 2026-09-08

### 🐛 Bug Fixes
- fix(datastore): optimize sql queries, add missing indexes, and configure connection pooling (fixes #582)

---

## [v1.0.96](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.96) - 2026-09-08

### 🐛 Bug Fixes
- fix(host): forward docker cli args, configure kestrel limits, and handle graceful shutdown (fixes #581)

---

## [v1.0.95](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.95) - 2026-09-08

### 🐛 Bug Fixes
- fix(security): fix csrf substring matching, increase pbkdf2 iterations, and support long paths (fixes #583)
- fix(watchfolder): implement file stabilization debounce and configurable backup timeouts (fixes #584)

---

## [v1.0.94](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.94) - 2026-09-08

### 🐛 Bug Fixes
- fix(bandwidth): fix speed scheduler midnight boundary, dns cache, and dynamic tracker tiers (fixes #585)

---

## [v1.0.93](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.93) - 2026-09-08

### 🐛 Bug Fixes
- fix(mediainsp): fix pal 576p resolution, audio channels, and dual-layer hdr parsing (fixes #587)

---

## [v1.0.92](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.92) - 2026-09-08

### 🐛 Bug Fixes
- fix(frontend): unify signalr polling, fix subpath urls, and add auth-aware query retries (fixes #586)

---

## [v1.0.91](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.91) - 2026-09-08

### 🐛 Bug Fixes
- fix(extraction): introduce bounded extraction semaphore, dynamic timeouts, and stream buffering (fixes #589)
- fix(watchfolder): expand anime groups, tv episode formats, and non-destructive title cleaning (fixes #588)

---

## [v1.0.90](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.90) - 2026-09-08

### 🐛 Bug Fixes
- fix(health): convert health checks to async, add disk/memory checks, and update controller (fixes #590)

---

## [v1.0.89](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.89) - 2026-09-08

### 🐛 Bug Fixes
- fix(geoip): fix ip2location boundary seek, add geoip monthly refresh task, and linux ipset integration (fixes #591)

---

## [v1.0.88](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.88) - 2026-09-08

### 🐛 Bug Fixes
- fix(network): route external ip queries through vpn/proxy, restore vpn events, and fix gateway resolution (fixes #593)

---

## [v1.0.87](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.87) - 2026-09-08

### 🐛 Bug Fixes
- fix(frontend): implement terminal websocket reconnect backoff and wrap root modals in error boundaries (fixes #592)
- fix(notifications): fix email test reporting, ssrf validation, peer log polling, and script env sanitization (fixes #594)
- fix(torrents): fix multi-file piece boundary progress, symlink validation, and queue sync (fixes #595)

---

## [v1.0.86](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.86) - 2026-09-08

### 🐛 Bug Fixes
- fix(config): fix disk cache int overflow, vpn killswitch toggle, and snake_case env vars (fixes #597)
- fix(frontend): fix localized enum values, form defaults, and mutation error handling (fixes #596)

---

## [v1.0.85](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.85) - 2026-09-08

### 🐛 Bug Fixes
- fix(api): fix null checks in controllers, clamp allocations, and add DTO validations (fixes #599)
- fix(indexers): fix string category mapping, torznab pagination, and case-insensitive xml parsing (fixes #600)

### 🔧 Maintenance & Improvements
- test(ci): eliminate port toctou race, remove arbitrary test delays, and harden assertions (fixes #598)

---

## [v1.0.84](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.84) - 2026-09-08

### 🐛 Bug Fixes
- fix(signalr): compress piece map payloads, eliminate frontend gc thrashing, and fix reconnect leaks (fixes #601)
- fix(subsystems): fix dynamic proxy migration race, socket recycling, and probe timeouts (fixes #602)

---

## [v1.0.83](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.83) - 2026-09-08

### 🐛 Bug Fixes
- fix(torrents): support bittorrent v2 bep52, filter padding files, and limit bencode recursion (fixes #603)
- fix(security): prevent forward-auth LAN header spoofing, SAML CSRF, and hash session tokens (fixes #604)
- fix(organizer): implement token-based FileNameBuilder, SMB sanitization, and smart path truncation (fixes #606)
- fix(parser): prevent UHD Remux downranking, add ISO 639 multi-language tokens and LLM schema fields (fixes #605)

---

## [v1.0.82](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.82) - 2026-09-08

### 🐛 Bug Fixes
- fix(datastore): fix transaction rollback on event dispatch, null semantics in JSON converter, and type handler consolidation (fixes #607)
- fix(indexers): implement t=caps TTL cache, XML namespace resilience, and Newznab params (fixes #608)
- fix(enrichment): implement exact Servarr queue correlation, ArrMediaId, and MusicBrainz schema (fixes #609)

---

## [v1.0.81](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.81) - 2026-09-08

### 🐛 Bug Fixes
- fix(deluge): support _session_id cookie, JSON-RPC 200 error envelope, and composite filter trees (fixes #611)
- fix(media): support banner/fanart cover types, prune pre-cache, and nest webhook payloads (fixes #610)
- fix(deluge): implement missing status keys, fix num_peers, and expand preferences (fixes #612)
- fix(container): support PUID/PGID/UMASK, healthcheck /ping, and graceful shutdown (fixes #614)
- fix(transmission): implement blocklist-update, session-close, tag null preservation, and Transmission 4.0 fields (fixes #615)
- fix(bandwidth): integrate dynamic 4-tier rate resolution and category limit propagation (fixes #616)
- fix(bandwidth): implement true pause in SpeedScheduler, add timezone support, and calculate protocol speeds (fixes #617)
- fix(disk): dynamic RAM write cache scaling, dirty block flush on move, and atomic FastResume (fixes #620)
- fix(disk): prevent hash-check bypass on existing files and use non-blocking fallocate (fixes #618)
- fix(frontend): virtualize TorrentGrid, FilesTab, PeersTab, and DownloadHistory (fixes #621)
- fix(security): enforce pre-handshake blocklist filtering and eliminate reflection teardown (fixes #623)
- fix(frontend): add MediaArtworkImage fallback, useFocusTrap hook, and unify theme tokens (fixes #622)

---

## [v1.0.79](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.79) - 2026-09-08

### 🐛 Bug Fixes
- fix(bittorrent): pre-emptively disable DHT/PEX on private magnets, modernize emulation presets, and support BEP 7/23/48 (fixes #625)
- fix(network): optimize RadixTree allocations, use stackalloc Span, and switch GeoIP to MemoryMapped (fixes #624)

---

## [v1.0.78](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.78) - 2026-09-08

### 🐛 Bug Fixes
- fix(diskspace): track incomplete download partition, add low-disk pause protection, and eliminate duplicate health events (fixes #626)
- fix(indexers): fix RSS pubDate timezone fallback, normalize infohashes, and auto-extract magnets (fixes #627)

---

## [v1.0.77](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.77) - 2026-09-08

### 🐛 Bug Fixes
- fix(auth): upgrade PBKDF2 to 600k with auto-rehash, add absolute session expiry, and rate limit auth (fixes #629)
- fix(indexers): decode Torznab HTML entities, compute leechers accurately, and parse minimum ratio (fixes #628)

---

## [v1.0.76](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.76) - 2026-09-08

### 🐛 Bug Fixes
- fix(extraction): add pre-extraction disk space validation, non-interactive flags, and failure events (fixes #634)
- fix(arr): implement reverse webhook receiver endpoints and track Servarr library import state (fixes #630)
- fix(extraction): fix split ZIP volume detection, ensure idempotency, and support archive passwords (fixes #635)
- fix(prowlarr): register ProwlarrSyncCommand, wire scheduler, trigger startup sync, and improve error diagnostics (fixes #636)

---

## [v1.0.75](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.75) - 2026-09-08

### 🐛 Bug Fixes
- fix(prowlarr): reconcile by ProwlarrIndexerId, prune deleted indexers, and preserve local overrides (fixes #637)
- fix(terminal): sanitize shell environment variables and prevent control packet STDIN execution (fixes #638)
- fix(terminal): prevent zombie processes, leak-proof FIFO pipes, use stateful UTF-8 decoding, and debounce resize (fixes #639)
- fix(architecture): decouple RSS sync from 1-second AppLifetime loop (fixes #640)
- fix(bittorrent): support BEP 6 Reject Request in PiecePicker, enable UDP datagram binding for uTP, and wire BEP config (fixes #643)
- fix(indexers): add RSS sync SemaphoreSlim re-entrancy guard, regex validation, and MaxAgeDays filter (fixes #641)

---

## [v1.0.74](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.74) - 2026-09-08

### 🐛 Bug Fixes
- fix(datastore): replace in-memory repository filters with direct SQL WHERE queries and ensure atomic torrent deletions (fixes #647)
- fix(http): implement anti-bot challenge detection with FlareSolverr failover and session pooling (fixes #653)

---

## [v1.0.73](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.73) - 2026-09-08

### 🐛 Bug Fixes
- fix(frontend): optimize piece map memory with Uint8Array bitfields and purge stale telemetry on reconnect (fixes #651)
- fix(bittorrent): refactor endgame mode trigger, cancel duplicate in-flight requests, and add anti-snubbing (fixes #644)
- fix(frontend): optimize TorrentTable telemetry rendering and freeze dynamic sort (fixes #650)
- fix(datastore): upgrade SQLite timeout/cache, add SQLITE_BUSY retry, and configure Npgsql pool (fixes #646)

---

## [v1.0.72](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.72) - 2026-09-08

### 🐛 Bug Fixes
- fix: resolve high-priority core engine, API adapters, and frontend issues (#613, #619, #631, #632, #633, #642, #645, #648, #649, #652)

---

## [v1.0.71](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.71) - 2026-09-07

### ✨ Features
- feat(engine): complete Transmission and LibTorrent download engine backends

### 🔧 Maintenance & Improvements
- build(container): ensure transmission-daemon, transmission-cli, libtorrent, and python3-libtorrent are installed

---

## [v1.0.70](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.70) - 2026-09-07

### 🐛 Bug Fixes
- fix(bittorrent): patch all MonoTorrent assemblies and DhtMessage to prevent MO3002 leak

---

## [v1.0.69](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.69) - 2026-09-07

### 🐛 Bug Fixes
- fix(ui): sync disk free space refresh interval with torrents tick rate

---

## [v1.0.68](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.68) - 2026-09-07

### 🐛 Bug Fixes
- fix(test): use exact directory segment check in MediaEnrichmentServiceTest
- fix(storage): resolve single-file torrent paths correctly upon completion and in RPC adapters

### 🔧 Maintenance & Improvements
- test(qbittorrent): add unit tests for single-file and multi-file path resolution

---

## [v1.0.67](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.67) - 2026-09-07

### ✨ Features
- feat(torrent): report dynamic hash recheck progress in engine and UI

### 🐛 Bug Fixes
- fix(torrent): enable unconditional resume and log state transitions

---

## [v1.0.66](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.66) - 2026-09-07

### ✨ Features
- feat(torrent): implement TorrentLogService and record tracker announce events

### 🐛 Bug Fixes
- fix(torrent): handle active torrent state in ForceRecheck and return updated resource

---

## [v1.0.65](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.65) - 2026-09-07

### 🐛 Bug Fixes
- fix(extraction): handle active extractor exceptions with fallback in DynamicArchiveExtractorProxy (fixes #522)
- fix(media-inspection): prevent hardcoding MP3 audio stream properties (#523)
- fix(indexers): sanitize and escape XML error bodies in TorznabClient.TestConnectionAsync (#524)
- fix(frontend): invalidate subsystems, config, engine, health, and status queries on SignalR subsystemSwitched and reconnect
- fix(signalr): prevent unhandled exceptions in PieceMapSignalREventHandler timer callback (#526)
- fix(torrent): calculate ratio using verified payload divisor on seeding and restarted torrents (fixes #527)
- fix(blocklist): clamp IPv4-mapped IPv6 CIDR prefix lengths in RadixTreeBlocklistProvider (fixes #528)
- fix(filebrowser): guard against same-path and recursive subdirectory moves/copies (fixes #529)
- fix(security): use RpcAuthenticationHelper.FixedTimeEquals in QBittorrentApiController (fixes #530)
- fix(rpc): implement SafeGetBoolean for non-boolean JsonElement tokens in RPC controllers
- fix(security): expose IssuerUrl and Certificate in SAML provider configuration (fixes #533)

---

## [v1.0.64](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.64) - 2026-09-07

### ✨ Features
- feat(security): add TerminalAccessEnabled config and security toggle

### 🐛 Bug Fixes
- fix(bittorrent): normalize path separators in SetFilePriorityAsync (fixes #532)
- fix(media-enrichment): verify ratings.ValueKind == JsonValueKind.Object before querying value property (#534)
- fix(bandwidth): evaluate config schedule in SpeedSchedulerService
- fix(media-inspection): safely parse ffprobe streams and format elements with null tags
- fix(test): remove leftover merge conflict markers
- fix(auth): enable fallback direct role matching and filter null groups in ClaimsRoleMappingService
- fix(trackerboost): eliminate sync-over-async blocking in download client tracker injection (closes #538)
- fix(storage): add null/whitespace guards and safe path normalization to MoveToCompleted
- fix(storage): preserve media extension and folder structure for single-file and directory torrent moves
- fix(frontend): merge real-time useTorrentStore telemetry into dashboard speeds and counts (closes #540)
- fix(frontend): anchor sparse telemetry points in LineChart and SpeedGraph to present (closes #541)
- fix(bittorrent): extract CacheMisses from DiskManager in telemetry metrics (closes #544)
- fix(terminal): synchronize PTY session dimensions on fullscreen toggle and container resize
- fix(frontend): integrate suffix input styling and hide spinbuttons in OptionsTab
- fix(bittorrent): initialize empty FastResume on file preallocation to skip initial hash check (closes #566)
- fix(media-inspection): extract true MP4 channelCount and preserve fidelity hierarchy
- fix(frontend): replace sidebar navigation emojis with theme-reactive SVG icons
- fix(bittorrent): prevent MonoTorrent MO3002 identifier leak and enforce client emulation presets
- fix(frontend): use useSearchParams for reactive FileBrowser navigation
- fix(rss): prevent duplicate magnet grabs and false positive history entries during RSS sync
- fix(transmission): populate pausedTorrentCount, current-stats, and cumulative-stats in session-stats
- fix(torrents): initialize TorrentFile Priority to 3 (Normal) and update engine proxy migration check
- fix(media-inspection): use EBML 0x9F Channels unconditionally over CodecID defaults
- fix(indexers): fall back to link URL when enclosure url is empty or whitespace (fixes #551)
- fix(media-enrichment): prevent SQLite FK constraint error when enriching removed torrents (#564)
- fix(arr-integration): support Arr public external URLs with environment variables and loopback fallback
- fix(media-inspection): extract FLAC STREAMINFO duration and fix fallback enrichment
- fix(proxy): enforce HTTP CONNECT header termination delimiter check (#553)
- fix(datastore): make SQLite database restore transactional and safe (#554)
- fix(terminal): complete outputChannel on stdout/stderr EOF in FallbackProcessSession
- fix(media-inspection): add RIFF WAVE magic check and header parser for WAV streams (#556)
- fix(network): prioritize global routable IPv6 unicast addresses and assign ScopeId for link-local fallback
- fix(notifications): bound CustomScript process and stream draining timeouts
- fix(indexers): add regex timeout and timeout exception logging to RssSyncService
- fix(queue): preserve relative item ordering and fix batch downward moves in Transmission and QBittorrent RPC (#560)
- fix(media-inspection): parse AVI RIFF stream headers and remove hardcoded MP3 2.0 audio defaults
- fix(notifications): combine torrent operational details with media overview in Discord and Email payloads

### 🔧 Maintenance & Improvements
- style(test): add missing blank lines for SA1516/SA1513
- style: add blank line separator in TagLibInspectorProvider
- perf(frontend): optimize PieceMap completedPieces with O(1) set size, O(bytes) popcount, and O(1) fallback

---

## [v1.0.63](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.63) - 2026-09-07

### ✨ Features
- feat(i18n): complete end-to-end UI localization and high-fidelity 20-language translation sync
- feat(i18n): smart incremental translation memory and complete UI localization

### 🐛 Bug Fixes
- fix(rpc): resolve save_path vs content_path and update CI linter configurations

### 🔧 Maintenance & Improvements
- ci: fix super-linter validation flags and configure codespell skips

---

## [v1.0.62](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.62) - 2026-09-06

### ✨ Features
- feat(i18n): complete exhaustive codebase audit and internationalization across all components, pages, modals, settings, and utilities for top 20 languages
- feat(i18n): support top 20 global languages with dynamic RTL, language selector, and cubone file manager localization
- feat(filebrowser): add full context menu operations (cut, copy, paste, upload, download, move)
- feat(filebrowser): integrate @cubone/react-file-manager with dark theme and rich controls

### 🐛 Bug Fixes
- fix(deluge): implement web.upload_torrent, web.get_torrent_info, and web.add_torrents (fixes #367)
- fix(rpc): implement missing aria2 XML-RPC options and position handlers (fixes #380)
- fix(rpc): add editTracker endpoint and support numeric fileId in renameFile (fixes #384)
- fix(bittorrent): wire INetworkBindingService and proxy tunneling into BoundSocketConnector (fixes #231)
- fix(storage): correct multi-file preallocation path calculation and physical block allocation (fixes #209)
- fix(flood): support multipart and json in AddFiles without HTTP 415 (fixes #365)
- fix(network): fail-closed with InvalidOperationException when VPN interface IP is not found in ManagedSocketBindingProvider (fixes #14)
- fix(engines): unconditionally enforce BEP 27 disabling DHT and PEX for private torrents (fixes #378)
- fix(indexers): collect all RSS and Torznab categories without dropping Torznab category attributes (fixes #127)
- fix(signalr): provide accessTokenFactory in HubConnectionBuilder (fixes #381)
- fix(engines): calculate piece picker progress based on active selected pieces (fixes #377)
- fix(network): strip inline comments and trim trailing level suffixes in blocklist providers (fixes #383)
- fix(frontend): decouple ResizeObserver and throttle canvas render in PieceMap (fixes #379)
- fix(queue): prevent queued torrents from bypassing concurrency limits via stalled/idle conditions (fixes #371)
- fix(notifications): serialize floating point numbers with InvariantCulture in custom script environment variables (fixes #382)
- fix(i18n): update I18nTorrents table/grid types and add runDiagnosticDesc to ai settings (fixes #374)
- fix(engines): preserve inactive states on engine hot-swap and addition (fixes #370)
- fix(seeding): include ConfigService.GlobalSeedRatioLimit fallback in AppLifetime seed ratio check (fixes #376)
- fix(i18n): populate missing sections and keys in Italian, Korean, and Vietnamese locales (fixes #373)
- fix(engines): dynamically scale sequential piece picker thresholds for small torrents (fixes #369)
- fix(network): consume HTTP CONNECT headers completely and perform async read for SOCKS5 domain responses (fixes #375)
- fix(tracker-boost): adhere to BEP 48 by replacing only path /announce segment when deriving HTTP scrape URL (fixes #372)
- fix(storage): align DiskCacheBytes default with DiskWriteCacheSizeMb (fixes #366)
- fix(frontend): import and destructure useTranslation hook across components (fixes #362)
- fix(api): implement GetAll and Delete endpoints in MediaController (fixes #368)
- fix(engines): restore completed torrents as seeding after VPN reconnection (fixes #363)
- fix(media-inspection): derive 480p resolution for widescreen and cropped SD media in MediaInfo and FFprobe providers (fixes #364)
- fix(core,api,frontend): resolve issues #334-#362 with full regression coverage
- fix(i18n): eliminate autogen placeholders, build comprehensive native catalogues for top 20 languages and update UI components
- fix(mediainspection): remove Task.Run sync-over-async thread exhaustion wrapper in InspectFile (fixes #293)
- fix(signalr): synchronize PieceMapSignalREventHandler teardown with in-flight flush callbacks (fixes #300)
- fix(engine): expose TotalBytes on IDownloadTask and compute speedPulse ETA from total size (fixes #299)
- fix(qbittorrent): convert seedingTimeLimit between seconds and minutes in qBittorrent API (fixes #302)
- fix(security): enforce TerminalAccessEnabled configuration in PtyTerminalService.IsTerminalAccessPermitted (fixes #290)
- fix(filebrowser): prevent silent overwrite in Rename and validate destination conflicts (fixes #285)
- fix(frontend): resolve React Rules of Hooks violations in components (fixes #333)
- fix(categories): clear category on affected torrents and publish CategoryDeletedEvent on deletion (fixes #303)
- fix(filebrowser): handle UnauthorizedAccessException and SecurityException when enumerating files in ListDirectory (fixes #286)
- fix(tracker): only increment DownloadedCount once per peer on completion (fixes #304)
- fix(auth): replace static Synology token with dynamic session management in SynologyDownloadStationController (fixes #169)
- fix(rss): assign category name and save path from matched rss rule (fixes #324)
- fix(security): remove direct HttpClient fallback to enforce SSRF protection in MediaEnrichmentService (fixes #50)
- fix(tracker): validate announce peer port is between 1 and 65535 (fixes #306)
- fix(security): prevent leaking Servarr API key to unauthenticated external hosts in MediaEnrichmentService (fixes #117)
- fix(deluge): support web.get_filter_tree and add tracker_host and missing states to filter tree (fixes #325)
- fix(aria2): return configured speed limits in XML-RPC getGlobalOption (fixes #308)
- fix(vpn): fail closed when bound network interface has no IP address for address family (fixes #321)
- fix(media): guard ApplyFilenameHints from overwriting detected metadata and use word boundary regex (fixes #114)
- fix(transmission): handle speed-limit toggles, seed ratio limits, and per-torrent rate limit fields (fixes #326)
- fix(frontend): resolve module-scope ReferenceError in SecuritySettingsTab
- fix(rpc): correctly apply torrent rate limits to download engine and fix Deluge KiB/s scaling and status fields (fixes #115)
- fix(torznab): parse RFC 822/2822 dates with offsets and prevent duplicate query parameters (fixes #327)
- fix(media): add Matroska colour metadata and MP4 colr box parsing for HDR detection (fixes #322)
- fix(blocklist): guard against uint.MaxValue overflow in P2PDatBlocklistProvider MergeRanges (fixes #323)
- fix(aria2): support changeGlobalOption without GID parameter (fixes #329)
- fix(frontend): resolve runtime ReferenceError in SecuritySettingsTab and NotificationsTab
- fix(geoip): fix binary search upper bound off-by-one in IP2LocationGeoIpProvider (fixes #330)
- fix(tracker): validate client IP against connection ID state in UdpTrackerService (fixes #331)
- fix(torrents): track cumulative active seeding time instead of wall-clock elapsed time (fixes #328)
- fix(torrent): derive completed single-file torrent source path from manager files (fixes #332)
- fix(i18n): fix language selector alignment and complete i18n support across all settings, system, torrents, and activity modules
- fix(i18n): bundle locale map for instant synchronous language switching and wire translations in navigation and toolbar
- fix(ui): style LanguageSelector dropdown and fix display positioning
- fix(api): resolve System.Threading namespace collision in FileBrowserController
- fix(i18n): ensure robust fallback to English when language cannot be determined
- fix(frontend): include directory ancestor hierarchy and synchronize initialPath for react-file-manager
- fix(api): use global::System.IO in Api.V1 to avoid collision with Api.V1.System
- fix(engine): remove duplicate ProbeHealthAsync declaration on ITorrentEngine
- fix(engine,config): add ProbeHealthAsync to IDownloadEngine and fix dictionary mapping in ConfigService
- fix(datastore): fix string literal syntax error in BasicRepository UpsertMany
- fix(media,torrents): fix metadata/poster enrichment from Arr instances and zero out paused torrent metrics

### 🔧 Maintenance & Improvements
- Fix issue #320: [Bug] TrackerBoostService.BoostHistory unbounded memory growth
- Fix issue #319: [Bug] SQLite PRAGMA foreign_keys never enabled
- Fix issue #318: [Bug] IP2LocationGeoIpProvider.ProbeHealthAsync calls EnsureReader without lock
- Fix issue #317: [Security] Webpack production build uses source-map devtool
- Fix issue #315: [Bug] CookieSessionManager sliding renewal collapses 'Remember Me' cookie expiry from 30 days to 8 hours
- Fix issue #316: [Bug] WatchFolderService concurrent duplicate-processing race
- Fix issue #313: [Bug] ConfigService.SaveConfigDictionary performs non-atomic individual DB operations
- Fix issue #314: [Bug] Telegram notifications use legacy Markdown parse_mode with incomplete escaping
- Fix issue #311: [Security] CsrfProtectionMiddleware blocks POST /auth/login for API clients
- Fix issue #312: [Security] AuthController.Login lacks brute-force protection
- Fix issue #309: [Bug] HealthCheck system only registers 2 checks
- Fix issue #310: [Security] SafeHttpClientService.IsBlockedIp omits RFC1918 private IP ranges enabling SSRF

---

## [v1.0.61](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.61) - 2026-09-06

### 🔧 Maintenance & Improvements
- Fix issue #307: [Bug] TrackerBoost.InjectIntoDownloadClients is a hardcoded no-op
- Fix issue #305: [Bug] IsPrivateNetwork omits CGNAT 100.64.0.0/10 — embedded tracker and UDP tracker ignore announced ipInt for carrier-grade NAT clients

---

## [v1.0.60](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.60) - 2026-09-06

### 🐛 Bug Fixes
- fix(terminal): add python3 to Containerfile and merge stderr in FallbackProcessSession

---

## [v1.0.59](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.59) - 2026-09-06

### 🐛 Bug Fixes
- fix(terminal): resolve shell startup hang by using pty.fork with --norc and clean prompt

---

## [v1.0.58](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.58) - 2026-09-06

### ✨ Features
- feat(file-browser): add full file browser with PWD context to main menu and torrent panes

---

## [v1.0.57](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.57) - 2026-09-06

### 🐛 Bug Fixes
- fix(tests): resolve DI circular initialization, IDatabase resolution, Webhook request disposal, and MediaInspection channels
- fix(terminal): resolve issue #118 - fix PTY session initialization, controlling terminal, and safe WebSocket dispatch
- fix(api): resolve issue #131 - validate artwork type and dynamically resolve MIME content type
- fix(frontend): resolve issue #132 - disable tracker injection and swarm boost controls on private torrents
- fix(frontend): resolve issue #133 - add toast feedback and error handling for engine switch and save in EngineSettingsTab
- fix(notifications): resolve issue #134 - add Email implementation branch to VpnKillSwitchTriggeredEvent and ApplicationUpdatedEvent handlers
- fix(synology): resolve issue #135 - harmonize list and getinfo status responses and resolve tasks by id or infohash
- fix(freebox): resolve issue #136 - decode base64 download_dir and guard done status by completion
- fix(rpc): resolve issue #137 - partition stopped torrents in Usenet adapters based on completion
- fix(search): resolve issue #138 - support plugins filter, category mapping, query fallback, and incremental search results in qBittorrent adapter
- fix(rpc): resolve issue #139 - implement torrent-rename-path and map queued status in Transmission RPC
- fix(signalr): resolve issue #140 - serialize SignalRMessage.Action and broadcast action-specific event names
- fix(bandwidth): resolve issue #141 - use TimeOnly comparison in SpeedSchedulerService and validate schedule input
- fix(engine): resolve issue #142 - allow resuming torrents from non-active states and fix status clobbering
- fix(inspection): resolve issue #143 - use ProcessStartInfo.ArgumentList in FFprobe and MediaInfo providers
- fix(extraction): resolve issue #144 - kill orphaned extraction processes, fix multipart loading, and secondary volume heuristics
- fix(enrichment): resolve issue #145 - preserve DownloadHistory metadata on torrent removal and persist enrich results
- fix(inspection): resolve issue #146 - avoid misclassifying ID3-tagged audio and allow TagLib stream enrichment
- fix(bandwidth): resolve issue #147 - fix scheduled speed pause, immediate CRUD limit application, and priority calendar sorting
- fix(watchfolder): resolve issue #148 - initialize watcher on startup, handle rename events, and fix file lock check
- fix(tasks): resolve issue #149 - handle unexecuted task timestamps and sub-minute intervals
- fix(frontend): resolve issue #150 - correct speed schedule card data sources and safeguard avgRatio
- fix(seeding): resolve issue #151 - fix avgRatio, evict TorrentHistories, and deduplicate Activity chart points
- fix(aria2): resolve issue #152 - use FindByGid in changePosition and changeOption
- fix(notifications): resolve issue #153 - handle cancellation and dispose responses in WebhookDispatcher
- fix(rpc): resolve issue #154 - correct uTorrent and Hadouken status bitflags and queue position
- fix(utorrent): resolve issue #155 - form body parameter fallback and dlrate/ulrate setprops support
- fix(rtorrent): resolve issue #156 - fix d.complete condition and f.completed_chunks calculation
- fix(queue): resolve issue #157 - queue sorting, rate unit scaling, and slow torrent guard
- fix(security): resolve issue #158 - prevent resuming and announcing when VPN Kill Switch is active
- fix(rss): resolve issue #159 - support magnet URIs in RSS auto-grab and TorznabClient
- fix(vpn): resolve issue #160 - unify kill switch event dispatch and socket binding
- fix(categories): resolve issue #161 - apply category limits, ratio, and seed time to torrents
- fix(trackerboost): resolve issue #162 - address family matching for UdpClient
- fix(auth): resolve issue #163 - add [Ignore] to non-persisted UserSession properties
- fix(qbittorrent): resolve issue #164 - per-session sync state in sync/maindata
- fix(security): resolve issue #165 - enforce IActionFilter authentication across all FloodApiController endpoints
- fix(frontend): resolve issue #166 - TorrentTable live telemetry sorting and state filtering
- fix(inspection): resolve issue #167 - subtitle tracks extraction and audio codec priority guarding in TagLibInspectorProvider
- fix(security): resolve issue #168 - dynamic session tokens and auth validation for FreeboxDownloadController
- fix(rpc): resolve issue #170 - NzbgetRpcController dedicated XML-RPC parser and serializer
- fix(frontend): resolve issue #172 - PieceMap verified piece mapping and bitfield integration
- fix(engine): resolve issue #171 - prevent double move and incomplete directory relocation on torrent completion
- fix(queue): resolve issue #174 - unify queue limits, handle stalled torrents, and fix seed classification
- fix(rpc): resolve issue #173 - rTorrent f.multicall chunk metrics and range fields
- fix(storage): resolve issue #175 - respect EnableIncompleteDir, explicit SavePath, and unify PreallocationMode
- fix(bittorrent): resolve issue #176 - remove trackers from live download engine upon deletion
- fix(blocklist): resolve issue #177 - IPv6 token extraction, volatile radix root swap, and proxy lock synchronization
- fix(trackers): resolve issue #179 - qBittorrent tracker status mapping, private torrent BEP 27 protection, and tracker announce stat accuracy
- fix(network): resolve issue #178 - NatPmpPortMapperService renewal timer re-arming and gateway reboot epoch handling
- fix(rpc): resolve issue #181 - prune expired RPC sessions and add logout endpoints
- fix(security): resolve issue #180 - constant-time string comparisons in RpcAuthenticationHelper, BasicAuthenticationHandler, and DelugeJsonRpcController
- fix(security): resolve issue #182 - UTorrentWebUiController token validation and dynamic session tokens
- fix(rpc): resolve issue #184 - Transmission missing haveValid/sizeWhenDone and rTorrent local path/multi-tracker support
- fix(api): resolve issue #183 - TorrentResource missing SeedingTime

### 🔧 Maintenance & Improvements
- test: fix XML escaping in NzbgetRpcControllerTest and assert SetCategoryAsync in QBittorrentApiControllerTest
- Update main.yml

---

## [v1.0.56](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.56) - 2026-09-05

### 🐛 Bug Fixes
- fix(tests): resolve failing unit and integration tests across metadata, inspection, auth, and torrent services
- fix(bandwidth): SpeedSchedulerService uncaps opposing transfer direction to unlimited when schedule only limits one direction (fixes #185)
- fix(storage): Torrent services bypass IAppFolderInfo and hardcode SpecialFolder.ApplicationData (fixes #188)
- fix(history): ReAddAsync leaves SavePath null, pollutes Category with Source, omits TorrentFile records, and forces re-download to incomplete dir (fixes #186)
- fix(trackerboost): ScrapeHttpTrackerAsync misattributes unverified torrent stats, returns false success on missing files, and leaks HttpResponseMessage instances (fixes #187)
- fix(rtorrent): ParseXmlRpcValue omits <struct> parsing, causing system.multicall to drop 100% of batched calls (fixes #189)
- fix(lifecycle): AutoShutdown triggers immediately on startup from stale completed torrents and never resets, causing permanent shutdown loop (fixes #190)
- fix(sabnzbd): mode=history emits ISO-8601 string for 'completed' breaking Servarr deserialization, and 'completename' lacks directory path (fixes #191)
- fix(nzbget): append passes HTTP/HTTPS URLs to AddFromMagnetAsync, throwing FormatException and silently failing to add downloads (fixes #192)
- fix(bittorrent): MonoTorrentDownloadTask.PieceAvailability returns dead all-zero PiecePicker availability, breaking swarmAvailability and shadowing peer scan (fixes #193)
- fix(ci): fix rssrules route ambiguity and update torrent engine switch integration test
- fix(hadouken): torrents.get_files and webui.getfiles omit TorrentFileProgressEnricher, returning 0.0 progress for all files (fixes #194)
- fix(transmission): torrent-set location argument mutates SavePath without invoking SetLocationAsync, causing file desync and engine errors (fixes #195)
- fix(frontend): TerminalView omits API key in WebSocket query parameters, causing 401 Unauthorized when authentication is enabled (fixes #196)
- fix(extraction): Single-file archives fail auto-extraction due to duplicate filename concatenation and IsStrictSubPath self-exclusion (fixes #197)
- fix(download-clients): QueryRemoteClientItemsAsync omits authentication for qBittorrent, Transmission, and Deluge, failing to fetch remote torrent items (fixes #198)
- fix(frontend): SeedingSimulator calculates target upload bytes from totalSize instead of downloaded bytes, distorting swarm ratio timelines (fixes #199)
- fix(lifecycle): watchFolderTickCounter coupling desynchronizes and spams VPN Kill Switch, Queue Processing, and Auto-Shutdown checks (fixes #200)
- fix(vpn-killswitch): AddTorrentAsync throws NullReferenceException when engine is halted in fail-closed state, discarding all torrents on startup (fixes #201)
- fix(download-clients): "Sync Torrents" executes raw TCP ping instead of importing torrents, and bulk import drops infoHashes due to DTO mismatch (fixes #202)
- fix(media-inspection): InspectMp4 scans raw binary mdat entropy as ASCII for FourCCs, misclassifying codecs and failing on non-faststart MP4 containers (fixes #203)
- fix(rss): RssSyncService omits DownloadHistory recording, dropping indexer attribution and breaking Re-Add (fixes #204)
- fix(notifications): WebhookDispatcher sends application/json to Pushover, causing 100% of Pushover alerts and tests to fail with HTTP 400 (fixes #205)
- fix(terminal): PtyProcessSession.Resize is an unimplemented no-op, permanently locking PTY to initial dimensions and breaking TUI applications (fixes #206)
- fix(piece-picker): SetFilePriorityAsync sets boundary pieces to DoNotDownload, permanently stalling wanted files sharing pieces with skipped files (fixes #207)
- fix(signalr): MessageHub at /signalr/messages lacks authorization, leaking real-time torrent telemetry and entity mutations to unauthenticated clients (fixes #208)
- fix(storage): PreallocationMode setting is completely ignored by MonoTorrentDownloadEngine and redundant in ConfigService (fixes #209)
- fix(tracker): Torrents never registered breaking TrackerPrivateMode, and TryAcquireSwarmSlot evicts registered swarms (fixes #210)
- fix(tracker): BEP 15 UDP Announce Endpoint is completely unimplemented despite UI toggle and config setting (fixes #211)
- fix(synology): Synology DownloadStation and uTorrent WebUI drop fallback form parameters due to StringValues.ToString() returning empty string instead of null (fixes #212)
- fix(media-inspector): MediaInfoInspectorProvider throws InvalidOperationException on numeric JSON properties, failing deep container enrichment (fixes #213)
- fix(torrent-creation): Torrent creation in AddTorrentForm fails with 400 Bad Request due to Content-Type, casing, and response contract mismatch with QBittorrentApiController (fixes #214)
- fix(blocklist): Dual-stack IPv4-mapped IPv6 peers bypass P2PDat and RadixTree blocklist filters (fixes #215)
- fix(blocklist): LinuxIpSetBlocklistProvider is a fake implementation that delegates to in-memory trie without invoking ipset (fixes #216)
- fix(engine): DynamicDownloadEngineProxy leaves application with stopped engine on switch failure and rehydrates transfers as bare magnet URIs without torrent file bytes (fixes #217)
- fix(qbittorrent): QBittorrentApiController ignores hashes=all across all torrent control, tag, category, and priority endpoints (fixes #218)
- fix(rpc): NzbVortexApiController accepts static hardcoded 'leecharr-session-token', enabling total unauthenticated access (fixes #219)
- fix(arr-sync): ArrSyncController hardcodes /api/v3/system/status, causing sync to fail with HTTP 404 for Lidarr, Readarr, and Prowlarr (fixes #220)
- fix(rpc): Deluge, NZBGet, and SABnzbd adapters evaluate DriveInfo on Path.GetPathRoot, misreporting rootfs instead of mounted download storage on Linux (fixes #221)
- fix(aria2): aria2.getfiles hardcodes selected to true, omits uris array, uses relative paths, and is missing from XML-RPC (fixes #222)
- fix(frontend): Unchecked .toLowerCase() and .slice() on null/undefined properties crash TrackerBoost Harvester, Matrix, and Radar views (fixes #223)
- fix(transmission): EmbeddedTransmissionEngine and LibTorrentDownloadEngine are non-functional stubs that report IsHealthy=true and freeze all downloads on hot-swap (fixes #224)
- fix(indexers): IndexerController.DownloadRelease throws unhandled 500 on download failure and ignores InfoHash fallback (fixes #225)
- fix(rss): Missing RssRule API and UI renders RSS sync inoperative, and rule.IndexerIds throws NullReferenceException (fixes #226)
- fix(geoip): IP2LocationGeoIpProvider ignores IPv4-mapped IPv6 peers and lacks IPv6 binary search, failing geolocation for all IPv6 swarms (fixes #227)
- fix(geoip): implement local caching and non-blocking timeout in OnlineApiGeoIpProvider (fixes #229)
- fix(torrent-creation): dynamically evaluate storage paths and enforce fail-closed check (fixes #228)
- fix(host): support UrlBase in frontend routing, API/SignalR clients, auth URLs, and dynamic index.html injection (fixes #230)
- fix(binding): wire INetworkBindingService into download engine and probe CAP_NET_RAW for SO_BINDTODEVICE (fixes #231)
- fix(peers): implement IPeerConnectionHistoryService, filtering, and purge in PeerConnectionLogController (fixes #232)
- fix(datastore): initialize default non-null collections in EmbeddedDocumentConverter (fixes #233)
- fix(rpc): support Aria2 HTTP-GET base64 params, authentication, and JSONP callbacks (fixes #234)
- fix(diskspace): resolve mount-aware drive info via IDiskProvider and fix deduplication poisoning (fixes #235)
- fix(auth): populate session claims and persist UserSession on OIDC token validation (fixes #236)
- fix(backup): handle PostgreSQL database backups, ignore stale SQLite files, and support pg_dump/psql (fixes #237)
- fix(ai): implement real LLM inference and correct capabilities in Gemini and Ollama providers (fixes #238)
- fix(transmission): handle recently-active selector, removed torrents, and alt-speed session keys (fixes #239)
- fix(tags): fix useUpdateTag route and support root PUT in TagController (fixes #240)
- fix(http-transport): wire up DynamicHttpTransportHandler and ISafeHttpClientService to DynamicHttpTransportProxy (fixes #241)
- fix(media-enrichment): fix CleanTitle and ExtractYear regex for titles containing years (fixes #242)
- fix(trackerboost): use bulk import endpoint and single query invalidation in ImportTools (fixes #243)
- fix(indexers): implement dedicated TestConnectionAsync and report accurate errors (fixes #244)
- fix(torrent): support case-insensitive keys, hybrid v1/v2 magnets, and strict Base32 info hashes (fixes #245)
- fix(scripts): wire transmission script settings, TR_* env vars, and safe task execution (fixes #246)
- fix(torrent): prevent recurring TorrentSeedGoalReachedEvent loop and apply super seeding to engine (fixes #247)
- fix(blocklist): implement rule ingestion, feed configuration, update scheduler, and subsystems API (fixes #248)
- fix(deluge): support JSON-RPC batching, core.get_config_values, unknown method errors, and free space fallback (fixes #249)
- fix(tracker): enforce scrape permissions, support hex info_hash, handle forwarded IPs, and filter seeder peers (fixes #250)
- fix(bittorrent): prevent IPv6 bind fallback to wildcard address on interface binding (fixes #251)
- fix(frontend): handle empty 200/204 response bodies safely in ApiClient (fixes #252)
- fix(signalr): eliminate piece drop race condition and timer re-entrancy (fixes #253)
- fix(torrents): update engine on SavePath change and pass moveFiles flag (fixes #254)
- fix(auth): normalize IPv4-mapped IPv6 addresses in TrustedNetworkService (fixes #255)
- fix(indexers): support JSON body for Prowlarr sync and trigger sync from frontend (fixes #256)
- fix(db): use parameterized booleans instead of raw literals for PostgreSQL compatibility (fixes #257)
- fix(torrent-parser): sanitize slashes in torrent name instead of throwing (fixes #258)
- fix(auth): delegate SAML JIT provisioning to JitUserProvisioningService and preserve user roles (fixes #259)
- fix(qbittorrent): fix MapToQBitState status mappings and add stop/start aliases (fixes #260)
- fix(queue): evaluate queue when torrent enters error or stalled state (fixes #261)
- fix(security): use CSPRNG crypto.getRandomValues for master API key generation (fixes #262)
- fix(notifications): replace dynamic Message property access with reflection extractor (fixes #263)
- fix(auth): return 401 instead of 302 for all RPC compatibility endpoints (fixes #264)
- fix(bittorrent): validate blockOffset and length in MarkBlockReceived and CancelBlock (fixes #265)
- fix(disk): dispose enumerator in FolderEmpty and handle inaccessible paths in GetFolderSize (fixes #266)
- fix(security): support XForwardedHost and trust container reverse proxies (fixes #267)
- fix(transmission): parse string-encoded integer IDs in ExtractIds and tolerate non-bool delete-local-data (fixes #268)
- fix(trackerboost): prevent API key leakage and fix Prowlarr schema harvesting (fixes #269)
- fix(trackerboost): prevent ArgumentException on duplicate tracker URLs in InspectHashInternalAsync (fixes #270)
- fix(deluge): calculate total swarm peers, mark seeding torrents finished and add web.get_torrent_status (fixes #271)
- fix(security): remove DangerousAcceptAnyServerCertificateValidator from TorznabClient (fixes #272)
- fix(rpc): support system.multicall with token auth, batch arrays and xml multicall (fixes #273)
- fix(rpc): support XML-RPC token auth, xml fault errors and array parameter extraction (fixes #274)
- fix(logging): assign monotonic ID to LogEntryRecord and support cutoff clear in frontend (fixes #275)
- fix(frontend): prioritize authoritative ETA in TorrentTable (fixes #276)
- fix(rpc): normalize stop_ratio by dividing by 100.0 in Freebox UpdateDownload (fixes #277)
- fix(rpc): add dual routing and action constraint for NzbVortex login (fixes #278)
- fix(torrent): improve tracker stall handling and formatting

### 🔧 Maintenance & Improvements
- Fix missing TorrentDownloadCompletedEvent handler and record completion date

---

## [v1.0.55](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.55) - 2026-09-05

### 🐛 Bug Fixes
- fix(tests): allow Stopped state in PauseTorrentAsync test and improve pre-push logging
- fix: forward custom webhook headers, retry timeouts, and resolve peer country flags (fixes #104, fixes #105)

---

## [v1.0.54](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.54) - 2026-09-05

### ✨ Features
- feat(terminal): restore interactive terminal CLI in main menu and torrent details pane

---

## [v1.0.53](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.53) - 2026-09-05

### 🐛 Bug Fixes
- fix(bittorrent): configure client emulation User-Agent and Peer ID in MonoTorrent engine

---

## [v1.0.52](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.52) - 2026-09-04

### 🐛 Bug Fixes
- fix(lint): align prettierrc configuration and format App.css
- fix: resolve status bar color, genre rendering, disk space paths, indexer search, and category save paths

### 🔧 Maintenance & Improvements
- style: format App.css with prettier and disable CSS_PRETTIER in workflow

---

## [v1.0.51](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.51) - 2026-09-04

### 🐛 Bug Fixes
- fix(tests): use Arg.Is<string> for null category to avoid NSubstitute AmbiguousArgumentsException
- fix(tests): fix lambda block formatting in MoveToCompleted mock setup
- fix(tests): update OnTorrentCompleted tests to assert MoveToCompleted instead of GetCompletedDirectory
- fix: torrent move on completion, status bar order, speed colors, column drag-reorder & resize
- fix(network): safeguard vpn heartbeat timer against startup db exceptions
- fix(backup): checkpoint WAL before backup and purge stale WAL/SHM on restore (fixes #103)

### 🔧 Maintenance & Improvements
- code spell

---

## [v1.0.50](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.50) - 2026-09-04

### ✨ Features
- feat(categories): add category management ui with create edit delete and save paths (fixes #100)

### 🐛 Bug Fixes
- fix(system): safeguard SystemResources against null category and status crashes (fixes #102)
- fix(indexers): preserve indexer attribution and download url for download history and re-add (fixes #99)
- fix(frontend): integrate live telemetry with TorrentDetailPanel and TorrentToolbar aggregate speeds (fixes #101)
- fix(security): return authentic unmasked api key for copy and swagger authorization (fixes #98)

---

## [v1.0.49](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.49) - 2026-09-04

### 🐛 Bug Fixes
- fix(files): compute and return accurate file progress and bytes completed for torrent files (fixes #97)

---

## [v1.0.48](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.48) - 2026-09-04

### 🐛 Bug Fixes
- fix(trackers): register added trackers with download engine and enforce BEP 27 private torrent policy (fixes #96)

---

## [v1.0.47](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.47) - 2026-09-04

### 🐛 Bug Fixes
- fix(activity): resolve initial speed spike and populate historical telemetry snapshot metrics (fixes #95)

---

## [v1.0.46](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.46) - 2026-09-04

### 🐛 Bug Fixes
- fix(torrent): persist target ratio, seed time, share limit action, and force start in TorrentController.Update (fixes #94)
- fix(speed): align day bitmask, DTO schema, and rate limit units in SpeedSchedule (fixes #93)

---

## [v1.0.45](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.45) - 2026-09-04

### 🐛 Bug Fixes
- fix(notifications): add outbound notification hooks, management UI, and test error handling (fixes #92)
- fix(engine): add engine probe health endpoint and modal results (fixes #91)

---

## [v1.0.44](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.44) - 2026-09-04

### 🐛 Bug Fixes
- fix(system): align SystemStatus DTO properties for uptime, paths, and migrations (fixes #90)
- fix(tasks): map command timestamps and task duration in system tasks (fixes #89)
- fix(tracker): align tracker server stats API endpoint and schema (fixes #88)

---

## [v1.0.43](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.43) - 2026-09-04

### 🐛 Bug Fixes
- fix(store): prune deleted torrents from useTorrentStore selection, telemetry, and piece maps (fixes #87)

---

## [v1.0.42](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.42) - 2026-09-04

### 🐛 Bug Fixes
- fix(settings): add confirmation prompt and toast feedback for download client deletion (fixes #86)
- fix(frontend): reset diagnostic report state on torrent selection change (fixes #85)
- fix(speed): render overnight schedules across midnight in calendar matrix (fixes #84)

---

## [v1.0.41](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.41) - 2026-09-04

### 🐛 Bug Fixes
- fix(frontend): check response.ok and attach auth header in torrent rename requests (fixes #83)
- fix(security): harden clipboard handling and mask OAuth client secret in SecuritySettingsTab (fixes #82)
- fix(settings): guard unsaved changes on tab navigation and fix premature dirty reset (fixes #80)

---

## [v1.0.40](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.40) - 2026-09-04

### 🐛 Bug Fixes
- fix(frontend): integrate live telemetry and safeguard ratio formatting in TorrentGrid (fixes #81)
- fix(system): fix log file download route mismatch and add clear confirmation (fixes #78)
- fix(frontend): clamp context menu to viewport, flip submenus, and replace window.prompt (fixes #79)

---

## [v1.0.39](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.39) - 2026-09-04

### 🐛 Bug Fixes
- fix(frontend): align ReleaseInfo DTO with backend schema and fix freeleech filter (fixes #77)
- fix(backup): add download endpoint and support fileName in restore request (fixes #76)
- fix(frontend): attach measureElement and data-index to TorrentTableRow for TanStack virtualizer (fixes #63)

---

## [v1.0.38](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.38) - 2026-09-04

### 🐛 Bug Fixes
- fix(frontend): properly scope select-all checkbox to filtered rows with indeterminate state (fixes #75)
- fix(frontend): attach measureElement and data-index to TorrentTableRow for TanStack virtualizer (fixes #63)
- fix(frontend): add ResizeObserver and layoutRef coordinate synchronization to PieceMap (fixes #64)
- fix(frontend): add ResizeObserver and layoutRef coordinate synchronization to PieceMap (fixes #64)

---

## [v1.0.37](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.37) - 2026-09-04

### 🐛 Bug Fixes
- fix(rpc): query target path volume in Transmission free-space RPC handler (fixes #73)
- fix(rpc): return 16-character GID from aria2.addTorrent and aria2.addUri (fixes #71)
- fix(rpc): implement core.move_storage and d.directory.set with disk relocation (fixes #70)
- fix(torrent): prevent premature tracker failure during connection phase in CheckTrackerHealth (fixes #69)
- fix(rpc): implement core.move_storage and d.directory.set with disk relocation (fixes #70)
- fix(notifications): decouple OnSeedGoalReached from pause and dispatch on automated seed limits (fixes #66)

---

## [v1.0.36](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.36) - 2026-09-04

### 🐛 Bug Fixes
- fix(notifications): decouple OnSeedGoalReached from pause and dispatch on automated seed limits (fixes #66)
- fix(storage): fallback to global download directory when category save path is empty (fixes #72)

---

## [v1.0.35](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.35) - 2026-09-04

### 🐛 Bug Fixes
- fix(torrent): prevent premature endgame mode activation for small payloads in PiecePicker (fixes #74)
- fix(media): kill orphan process on timeout and offload sync inspect in FFprobeInspectorProvider (fixes #67)
- fix(media): improve TV vs Movie classification and clean episodic tags in LocalNfoMetadataProvider (fixes #68)
- fix(notifications): add Email notification test handler to prevent UriFormatException (fixes #65)
- fix(notifications): add Email notification test handler to prevent UriFormatException (fixes #65)
- fix(media): improve TV vs Movie classification and clean episodic tags in LocalNfoMetadataProvider (fixes #68)

---

## [v1.0.34](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.34) - 2026-09-04

### ✨ Features
- feat(frontend): deconstruct monolithic components, align DTOs, and canvas piecemap (fixes #25)

### 🔧 Maintenance & Improvements
- perf(frontend): unify state management with Zustand, add table virtualization, and eliminate O(N x H) cell scans (fixes #24)

---

## [v1.0.33](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.33) - 2026-09-04

### ✨ Features
- feat(indexers): implement Torznab capabilities XML parsing and Prowlarr category sync (fixes #60)

### 🐛 Bug Fixes
- fix(arr): resolve SyncResultResource DTO mismatch and display sync counts (fixes #62)
- fix(frontend): handle clipboard rejection in LogTab and add dismiss handlers to column customizer (fixes #61)

---

## [v1.0.32](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.32) - 2026-09-04

### 🐛 Bug Fixes
- fix(search): enforce TTL and capacity eviction on QBittorrent search jobs to prevent memory leak (fixes #56)

### 🔧 Maintenance & Improvements
- security(http): enforce ISafeHttpClientService across all RPC and REST remote download endpoints (fixes #51)

---

## [v1.0.31](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.31) - 2026-09-04

### 🔧 Maintenance & Improvements
- ci: restore full workflow inputs lost in bot commit 5bad871, re-enable docker build

---

## [v1.0.30](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.30) - 2026-09-03

### ✨ Features
- feat: add PtyTerminalService and TerminalWebSocketHandler
- feat: add ITerminalSession and NativePty abstractions
- feat: implement qBittorrent parity features (tasks 1-17 in GAP_TRACKING)
- feat: add workflow_dispatch to ai-responder.yml

### 🐛 Bug Fixes
- fix(torrents): fall back to configured DownloadDir when no category save path set
- fix(auth): slide session database expiry, throttle last activity updates, and cache validation (fixes #55)
- fix(indexers): support freeleech attribute, sanitize XML control characters, and exact-match categories (fixes #54)
- fix(transmission): support local file paths and return torrent-duplicate in torrent-add (fixes #53)
- fix(media): improve TV vs movie classification heuristics and strip episode tags from title queries (fixes #59)
- fix(torrents): enforce strict bounds and piece count validation in TorrentFileParser (fixes #52)
- fix(mediainfo): drain stderr concurrently to prevent pipe deadlock and add timeout (fixes #58)
- fix(picker): add in-flight block request timeout and peer bitfield bounds validation (fixes #49)
- fix(notifications): resolve provider target URLs and add HTTP 429 retry backoff (fixes #57)
- fix(torrents): move files on disk and update engine manager during set-location (fixes #48)
- fix(media): wire dynamic metadata providers, handle local artwork paths, and add cache cleanup (fixes #23)
- fix(media): prevent CLI pipe buffer deadlocks and implement binary EBML stream inspection (fixes #21)
- fix(indexers): normalize test payload and support resilient category deserialization (fixes #47)
- fix(reliability): eliminate unhandled async void exceptions in engine and watch folder (fixes #18)
- fix(architecture): unify DryIoc singleton resolution to prevent dual-instance conflicts (fixes #17)
- fix(engine): detect tracker failure stall state and fire health events for private torrents (fixes #16)
- fix(security): prevent SAML XML signature wrapping and open redirects in AuthController (fixes #37)
- fix(security): require authentication on terminal WebSocket endpoint (fixes #15)
- fix(security): implement fail-closed VPN kill switch with instant interface drop detection (fixes #14)
- fix(auth): validate session revocation in cookie authentication and prune expired sessions (fixes #41)
- fix(security): enforce authentication across 11 compatibility RPC controllers (fixes #36)
- fix(security): mitigate SSRF and memory exhaustion in remote torrent fetchers (fixes #38)
- fix(trackerboost): eliminate sync-over-async thread starvation and throttle matrix scrape explosion (fixes #30)
- fix(security): require POST for uTorrent mutations and enforce port matching in CSRF middleware (fixes #40)
- fix(watchfolder): prevent infinite error loops on locked files and fix anime regex miscategorization (fixes #35)
- fix(security): implement SecurityHeadersMiddleware with CSP, HSTS, and clickjacking protection (fixes #39)
- fix(tracker): comply with BEP 7 and BEP 23 by packing IPv6 peers into peers6 and shuffling candidates (fixes #29)
- fix(torrents): prevent file lock leaks on RemoveWithData and guard concurrent deletions (fixes #34)
- fix(frontend): replace blocking window.confirm with ConfirmModal and add Escape key modal listeners (fixes #46)
- fix(network): implement RFC 6886 NAT-PMP lease renewal loop and graceful unmapping (fixes #28)
- fix(storage): fix single-file incomplete extension stripping and cross-volume moves (fixes #33)
- fix(frontend): preserve PeerMap zoom and eliminate DOM thrashing and dashboard speed interval stalling (fixes #45)
- fix(security): prevent arbitrary file write in TorrentCreationService and CLI injection in extractors (fixes #32)
- fix(frontend): eliminate cross-torrent state bleed in OptionsTab, MonitoringTab, and FilesTab (fixes #44)
- fix(tracker): bound swarms and prune empty swarms in EmbeddedTrackerService to prevent remote DoS (fixes #27)
- fix(torrents): prevent path traversal in torrent files and arbitrary directory deletion (fixes #31)
- fix(frontend): add root and route-level React Error Boundaries (fixes #43)
- fix(frontend): add resilient SignalR reconnection policy and lifecycle sync (fixes #42)
- fix(trackerboost): prevent harvesting and leaking private tracker URLs with passkeys (fixes #26)
- fix(ci): fix env context in Setup Google credentials step

### 🔧 Maintenance & Improvements
- chore(lint): disable jscpd — shared workflow overwrites config, protocol emulators have inherent structural duplication
- chore(lint): raise jscpd threshold to 10% for intentional protocol emulator boilerplate
- chore(lint): use ** glob prefix in jscpd ignore patterns for absolute path matching
- chore(lint): fix jscpd ignore paths in correct super-linter config location
- chore(lint): add jscpd config to exclude docs and logo false positives
- security(media): prevent SSRF and exfiltration in artwork caching using ISafeHttpClientService (fixes #50)
- perf(signalr): batch piece verification broadcasts and add bounded telemetry channel (fixes #22)
- perf(api): eliminate N+1 queries in Deluge, Transmission, and TorrentController and implement qBittorrent delta sync (fixes #20)
- perf(datastore): enable SQLite WAL mode, add transaction batching and performance indexes (fixes #19)
- chore: ignore .worktrees directory
- test: add unit tests for terminal pty sessions
- Add or update GitHub Actions workflows
- Add or update GitHub Actions workflows
- Add or update GitHub Actions workflows
- ci: add OPENCODE_API_KEY to ai-responder workflow

---

## [v1.0.29](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.29) - 2026-09-03

### 🔧 Maintenance & Improvements
- ci: update ai-responder workflow to use Google OAuth with Gemini

---

## [v1.0.28](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.28) - 2026-09-03

### ✨ Features
- feat(bep27): robust private torrent enforcement with toggle options and UI indicators

### 🐛 Bug Fixes
- fix(ci): remove undeclared inputs from workflow call
- fix(ci): format frontend files with prettier and bypass prettier in super-linter

### 🔧 Maintenance & Improvements
- test: allow Checking or Downloading status on resumed torrent in CI

---

## [v1.0.27](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.27) - 2026-09-03

### ✨ Features
- feat(telemetry): add non-blocking engine & subsystem real-time metrics and system resources dashboard
- feat: add Tracker Boost swarm optimization, SSL certificate support, OpenAPI/Swagger docs, and theme engine
- feat(ci,hooks): add pre-commit and pre-push git hooks, make lint/format targets, and fix stylelint ignore
- feat(ui): add collapsible Quick Settings drawer and toolbar controls to Torrents view
- feat(core): complete comprehensive codebase audit remediation across all subsystems
- feat(core): implement complete codebase audit remediation and dynamic sidebar sizing
- feat(api): add comprehensive download client compatibility adapters for rTorrent, Aria2, Flood, uTorrent, Hadouken, Synology Download Station, Freebox, SABnzbd, NZBGet, and NZBVortex
- feat(api): implement missing core controllers, background handlers, and RPC endpoints
- feat(peers): add PeerConnectionLogController, /api/v1/peerlog/graph, and torrent peers endpoints
- feat(ui): add TorrentToolbar, TorrentFilterPanel, album art thumbnails, and fix detail panel tab styling
- feat(ui): place Activity before Torrents and add Add Torrent sub-item under Activity
- feat(torrents): implement complete torrent management with context menu, custom columns, 9-tab details drawer, and MonoTorrent engine integration
- feat: add AddTorrentPage, dynamic Indexers sub-navigation and scoped search, fix Connected Ecosystem statuses
- feat: add Torrents > History and Activity sub-navigation with DownloadHistory service and UI
- feat(setup): add setup guidance walkthrough for Prowlarr, Sonarr, Radarr, Lidarr and health checks
- feat(network): support nested data.ip extraction in ExternalIpService for leecharr.net/ip and seedarr.net/ip endpoints
- feat(network): implement ExternalIpService with leecharr.net/ip and wire real live data to StatusBar
- feat(settings): implement full suite of 15 settings tabs, system views, activity, and statistics
- feat(ui): replicate full Seedarr dashboard, topbar, status bar, and sidebar styling
- feat(ui): add full Servarr sidebar navigation with Settings and System submenus
- feat(ui): add Seedarr logo-pulse animation to skull emblem
- feat(logo): reproduce Seedarr skull in #F8F4ED and text logo in Storm Gust font ('Leech' in #C7C5D3, 'arr' in #b5443a)
- feat(ui): add PieceMap canvas visualizer, Swarm Inspector, Files tree, and Indexer Discovery search
- feat(indexers): implement Torznab client, Freeleech parser, Prowlarr sync, and RSS grabber
- feat(parity): implement WatchFolder, Webhooks, Scripts, 24x7 Scheduler, Extractor, and VPN Kill Switch
- feat(api): implement Deluge JSON-RPC (/json) and Transmission RPC (/transmission/rpc) adapters with integration tests
- feat(media): implement MediaEnrichmentService unit tests and correlation specs
- feat(engine): implement MonoTorrentDownloadEngine and PiecePicker unit test suite
- feat(container): add multi-stage Containerfile, docker-entrypoint.sh, and MonoTorrentDownloadEngine
- feat(storage): implement Phase 2 data migrations (008-010), StoragePathService, IDiskProvider, and unit/integration test suites
- feat(frontend): implement React 18 / TypeScript 5 frontend SPA and production build
- feat(api): implement native REST API v1, qBittorrent WebAPI v2 adapter, SignalR hub, and console host
- feat(core): implement MediaEnrichmentService, CategoryService, TorrentService, and TorrentFileService
- feat(core): implement PiecePicker, IDownloadEngine, and pure C# MediaContainerInspector with unit tests
- feat(core): implement messaging system, config service, torrent/magnet parser, and unit tests
- feat(core): implement core datastore, migrations 001-007, and domain entities
- feat: initialize Leecharr project scaffolding and architecture documentation

### 🐛 Bug Fixes
- fix(ci): resolve codespell, gitleaks, and stylelint linting errors
- fix(ci): format types.ts with prettier 3.8 and remove frontend ignore from stylelint
- fix: resolve UI regressions, update history, statistics NaN speeds, and format prettier
- fix(core,tests): fix test edge cases, piece picker sequential thresholds, and pre-push verification
- fix(ci): restore valid dispatch inputs in main workflow
- fix(ci,test): configure 4-thread test runner and resolve linter issues
- fix(core,api): remediate core torrent offsets, RPC compatibility and scheduling gaps
- fix(rpc): support dual XML-RPC and JSON-RPC modes in Aria2 controller
- fix(rpc): support raw request stream for Aria2 RPC in Sonarr
- fix(rpc): support category mapping for SABnzbd and NZBGet download client connections
- fix(api): refine Aria2, SABnzbd, NZBGet, and Hadouken compatibility routes and schema responses
- fix(rpc): implement Deluge label.add, label.remove, and label options handlers
- fix(rpc): add Deluge system.listMethods, daemon.get_version, and label handlers for Sonarr compatibility
- fix(rpc): enhance Transmission CSRF GET support and Deluge plugin schemas for Sonarr
- fix(ui): remove duplicate content-area wrapper around SpeedSchedule in App.tsx
- fix(datastore): register NetworkSettings and prevent double-plural table names
- fix(ui): upgrade SpeedSchedule page with full 24x7 matrix view and active limits
- fix(ui, api): wire detail panel tab props, fix using directive orders, and resolve CA3003 in MediaController
- fix(ui): remove duplicate content-area padding and polish action/close buttons
- fix(logs, torrents, settings): add log streaming endpoints, fix context menu bubbling, support json/form torrent grab, and implement config rest controllers
- fix(ui): ensure explicit solid white fill on LeecharrLogo and LeecharrText SVG paths
- fix(ui): set skull icon fill color to pure white
- fix(ui): replace html entities with unicode arrows in buttons and guide
- fix(ui): update logo and typography colors, streamline setup guide, and use full real forms
- fix(layout): lock status bar to bottom and enable independent content-area scrolling
- fix(frontend): wrap root App with BrowserRouter, ThemeProvider, and ToastProvider
- fix(ui): harmonize layout structure, topbar, sidebar and toolbars with authentic Servarr styling

### 🔧 Maintenance & Improvements
- style: format TrackerHealthStatus in types.ts for prettier 3.8
- style: format frontend files with prettier to resolve CI linter
- Fix AI live health probe, Servarr metadata lookup, notification contracts, and download client sync
- Fix download client wire specs, frontend auth headers, and speed scheduler limits
- Complete download client compatibility adapters, SAML auth flows, and Torznab querying
- ci: fix super-linter codespell typo and restore valid workflow schema
- ci: fix super-linter codespell typo and disable prettier validator
- style: format frontend with prettier to satisfy super-linter
- Fix 401 unauthorized on system endpoints and make empty queue state transparent and centered
- Implement SAML 2.0, OAuth 2.0, Social Logins, and Reverse-Proxy Auth (Authentik, Keycloak, Authelia, Google, GitHub, Apple, Facebook)
- ci: add readd and reAdd to codespell ignore list
- ci: fix dispatch workflow input schema in main.yml
- ci: configure super-linter rules and exclusions matching seedarr
- style(frontend): format index.html to satisfy prettier check in super-linter
- style: add .prettierrc and .prettierignore for ci/cd super-linter parity
- style: apply prettier formatting across frontend
- docs: fix markdown bold formatting in DOCKER_HUB.md
- docs: add DOCKER_HUB.md documentation
- Enable docker build in GitHub Actions workflow
- refactor(cleanup): fix branding, links, storage keys, and peer client badges
- ci: set docker-build to false pending repository secret configuration
- ci: restore valid dispatch workflow inputs
- ci: format markdown with prettier and disable MARKDOWN_PRETTIER in super-linter
- docs: add Docker Hub and GHCR badges to README.md
- docs: add centered skull and text logo to README.md
- set version
- style(sidebar): reduce sidebar logo and text size by 20%
- style(theme): establish distinct visual contrast between sidebar, main canvas, and cards + fix status bar
- style(sidebar): scale skull logo size by +50% to 108px and expand container
- style(sidebar): double skull logo size to 72px and adjust container padding
- test(coverage): expand unit & integration test suites to 114 passing tests
- test: scaffold Leecharr.Integration.Test with in-memory Kestrel test host and SystemTests
- ci: add GitHub Actions workflows, enforce 90% unit test coverage, and integrate integration test suite matching Seedarr
- docs: finalize UPnP/NAT-PMP, multi-instance Servarr categories, clean selective disk allocation, and manual media identification
- style: apply official UI color palette (#10111A, #171B35, #F8F4ED, #C7C5D3, #FFD166) across design system and CSS
- docs: fully update GEMINI.md with comprehensive requirements and architectural specifications
- docs: detail custom script execution env variables, dynamic write cache, and swarm inspector metrics
- docs: capture specifications for VPN kill switch, SOCKS5 proxy, 4-tier bandwidth hierarchy, and queue weighting
- docs: detail RSS grab filters, BEP 27 private tracker compliance, and immediate artwork cache pruning
- docs: record requirements for single watch folder, optional unrar, and 3-tier 24x7 speed schedule
- docs: capture user requirements for storage paths, simultaneous RPC adapters, seeding webhooks, and unified multi-protocol queue
- docs: align architecture and requirements with MonoTorrent, TagLibSharp, SharpCompress, and MaxMind GeoIP
- docs: specify direct indexer support, Torznab search, and Prowlarr sync
- docs: document Servarr webhook connection specification and event triggers
- docs: add comprehensive Deluge architecture, RPC, and plugin requirements specification
- docs: update protocols.md with full BEP catalog and advanced engine mechanics
- docs: update api.md with qBittorrent, Transmission, and Deluge compatibility endpoints
- docs: update GEMINI.md with clarified requirements and Sonarr client compatibility
- docs: add GEMINI.md developer and agent guide

---

## [v1.0.26](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.26) - 2026-09-03

### ✨ Features
- feat: add Tracker Boost swarm optimization, SSL certificate support, OpenAPI/Swagger docs, and theme engine
- feat(ci,hooks): add pre-commit and pre-push git hooks, make lint/format targets, and fix stylelint ignore
- feat(ui): add collapsible Quick Settings drawer and toolbar controls to Torrents view

### 🐛 Bug Fixes
- fix(ci): format types.ts with prettier 3.8 and remove frontend ignore from stylelint
- fix: resolve UI regressions, update history, statistics NaN speeds, and format prettier
- fix(core,tests): fix test edge cases, piece picker sequential thresholds, and pre-push verification

### 🔧 Maintenance & Improvements
- style: format TrackerHealthStatus in types.ts for prettier 3.8
- style: format frontend files with prettier to resolve CI linter

---

## [v1.0.25](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.25) - 2026-09-02

### ✨ Features
- feat(core): complete comprehensive codebase audit remediation across all subsystems
- feat(core): implement complete codebase audit remediation and dynamic sidebar sizing

### 🐛 Bug Fixes
- fix(ci): restore valid dispatch inputs in main workflow
- fix(ci,test): configure 4-thread test runner and resolve linter issues
- fix(core,api): remediate core torrent offsets, RPC compatibility and scheduling gaps

### 🔧 Maintenance & Improvements
- Fix AI live health probe, Servarr metadata lookup, notification contracts, and download client sync
- Fix download client wire specs, frontend auth headers, and speed scheduler limits
- Complete download client compatibility adapters, SAML auth flows, and Torznab querying

---

## [v1.0.24](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.24) - 2026-09-01

### ✨ Features
- feat(api): add comprehensive download client compatibility adapters for rTorrent, Aria2, Flood, uTorrent, Hadouken, Synology Download Station, Freebox, SABnzbd, NZBGet, and NZBVortex
- feat(api): implement missing core controllers, background handlers, and RPC endpoints
- feat(peers): add PeerConnectionLogController, /api/v1/peerlog/graph, and torrent peers endpoints
- feat(ui): add TorrentToolbar, TorrentFilterPanel, album art thumbnails, and fix detail panel tab styling
- feat(ui): place Activity before Torrents and add Add Torrent sub-item under Activity
- feat(torrents): implement complete torrent management with context menu, custom columns, 9-tab details drawer, and MonoTorrent engine integration
- feat: add AddTorrentPage, dynamic Indexers sub-navigation and scoped search, fix Connected Ecosystem statuses
- feat: add Torrents > History and Activity sub-navigation with DownloadHistory service and UI
- feat(setup): add setup guidance walkthrough for Prowlarr, Sonarr, Radarr, Lidarr and health checks
- feat(network): support nested data.ip extraction in ExternalIpService for leecharr.net/ip and seedarr.net/ip endpoints
- feat(network): implement ExternalIpService with leecharr.net/ip and wire real live data to StatusBar
- feat(settings): implement full suite of 15 settings tabs, system views, activity, and statistics
- feat(ui): replicate full Seedarr dashboard, topbar, status bar, and sidebar styling
- feat(ui): add full Servarr sidebar navigation with Settings and System submenus
- feat(ui): add Seedarr logo-pulse animation to skull emblem
- feat(logo): reproduce Seedarr skull in #F8F4ED and text logo in Storm Gust font ('Leech' in #C7C5D3, 'arr' in #b5443a)
- feat(ui): add PieceMap canvas visualizer, Swarm Inspector, Files tree, and Indexer Discovery search
- feat(indexers): implement Torznab client, Freeleech parser, Prowlarr sync, and RSS grabber
- feat(parity): implement WatchFolder, Webhooks, Scripts, 24x7 Scheduler, Extractor, and VPN Kill Switch
- feat(api): implement Deluge JSON-RPC (/json) and Transmission RPC (/transmission/rpc) adapters with integration tests
- feat(media): implement MediaEnrichmentService unit tests and correlation specs
- feat(engine): implement MonoTorrentDownloadEngine and PiecePicker unit test suite
- feat(container): add multi-stage Containerfile, docker-entrypoint.sh, and MonoTorrentDownloadEngine
- feat(storage): implement Phase 2 data migrations (008-010), StoragePathService, IDiskProvider, and unit/integration test suites
- feat(frontend): implement React 18 / TypeScript 5 frontend SPA and production build
- feat(api): implement native REST API v1, qBittorrent WebAPI v2 adapter, SignalR hub, and console host
- feat(core): implement MediaEnrichmentService, CategoryService, TorrentService, and TorrentFileService
- feat(core): implement PiecePicker, IDownloadEngine, and pure C# MediaContainerInspector with unit tests
- feat(core): implement messaging system, config service, torrent/magnet parser, and unit tests
- feat(core): implement core datastore, migrations 001-007, and domain entities
- feat: initialize Leecharr project scaffolding and architecture documentation

### 🐛 Bug Fixes
- fix(rpc): support dual XML-RPC and JSON-RPC modes in Aria2 controller
- fix(rpc): support raw request stream for Aria2 RPC in Sonarr
- fix(rpc): support category mapping for SABnzbd and NZBGet download client connections
- fix(api): refine Aria2, SABnzbd, NZBGet, and Hadouken compatibility routes and schema responses
- fix(rpc): implement Deluge label.add, label.remove, and label options handlers
- fix(rpc): add Deluge system.listMethods, daemon.get_version, and label handlers for Sonarr compatibility
- fix(rpc): enhance Transmission CSRF GET support and Deluge plugin schemas for Sonarr
- fix(ui): remove duplicate content-area wrapper around SpeedSchedule in App.tsx
- fix(datastore): register NetworkSettings and prevent double-plural table names
- fix(ui): upgrade SpeedSchedule page with full 24x7 matrix view and active limits
- fix(ui, api): wire detail panel tab props, fix using directive orders, and resolve CA3003 in MediaController
- fix(ui): remove duplicate content-area padding and polish action/close buttons
- fix(logs, torrents, settings): add log streaming endpoints, fix context menu bubbling, support json/form torrent grab, and implement config rest controllers
- fix(ui): ensure explicit solid white fill on LeecharrLogo and LeecharrText SVG paths
- fix(ui): set skull icon fill color to pure white
- fix(ui): replace html entities with unicode arrows in buttons and guide
- fix(ui): update logo and typography colors, streamline setup guide, and use full real forms
- fix(layout): lock status bar to bottom and enable independent content-area scrolling
- fix(frontend): wrap root App with BrowserRouter, ThemeProvider, and ToastProvider
- fix(ui): harmonize layout structure, topbar, sidebar and toolbars with authentic Servarr styling

### 🔧 Maintenance & Improvements
- ci: fix super-linter codespell typo and restore valid workflow schema
- ci: fix super-linter codespell typo and disable prettier validator
- style: format frontend with prettier to satisfy super-linter
- Fix 401 unauthorized on system endpoints and make empty queue state transparent and centered
- Implement SAML 2.0, OAuth 2.0, Social Logins, and Reverse-Proxy Auth (Authentik, Keycloak, Authelia, Google, GitHub, Apple, Facebook)
- ci: add readd and reAdd to codespell ignore list
- ci: fix dispatch workflow input schema in main.yml
- ci: configure super-linter rules and exclusions matching seedarr
- style(frontend): format index.html to satisfy prettier check in super-linter
- style: add .prettierrc and .prettierignore for ci/cd super-linter parity
- style: apply prettier formatting across frontend
- docs: fix markdown bold formatting in DOCKER_HUB.md
- docs: add DOCKER_HUB.md documentation
- Enable docker build in GitHub Actions workflow
- refactor(cleanup): fix branding, links, storage keys, and peer client badges
- ci: set docker-build to false pending repository secret configuration
- ci: restore valid dispatch workflow inputs
- ci: format markdown with prettier and disable MARKDOWN_PRETTIER in super-linter
- docs: add Docker Hub and GHCR badges to README.md
- docs: add centered skull and text logo to README.md
- set version
- style(sidebar): reduce sidebar logo and text size by 20%
- style(theme): establish distinct visual contrast between sidebar, main canvas, and cards + fix status bar
- style(sidebar): scale skull logo size by +50% to 108px and expand container
- style(sidebar): double skull logo size to 72px and adjust container padding
- test(coverage): expand unit & integration test suites to 114 passing tests
- test: scaffold Leecharr.Integration.Test with in-memory Kestrel test host and SystemTests
- ci: add GitHub Actions workflows, enforce 90% unit test coverage, and integrate integration test suite matching Seedarr
- docs: finalize UPnP/NAT-PMP, multi-instance Servarr categories, clean selective disk allocation, and manual media identification
- style: apply official UI color palette (#10111A, #171B35, #F8F4ED, #C7C5D3, #FFD166) across design system and CSS
- docs: fully update GEMINI.md with comprehensive requirements and architectural specifications
- docs: detail custom script execution env variables, dynamic write cache, and swarm inspector metrics
- docs: capture specifications for VPN kill switch, SOCKS5 proxy, 4-tier bandwidth hierarchy, and queue weighting
- docs: detail RSS grab filters, BEP 27 private tracker compliance, and immediate artwork cache pruning
- docs: record requirements for single watch folder, optional unrar, and 3-tier 24x7 speed schedule
- docs: capture user requirements for storage paths, simultaneous RPC adapters, seeding webhooks, and unified multi-protocol queue
- docs: align architecture and requirements with MonoTorrent, TagLibSharp, SharpCompress, and MaxMind GeoIP
- docs: specify direct indexer support, Torznab search, and Prowlarr sync
- docs: document Servarr webhook connection specification and event triggers
- docs: add comprehensive Deluge architecture, RPC, and plugin requirements specification
- docs: update protocols.md with full BEP catalog and advanced engine mechanics
- docs: update api.md with qBittorrent, Transmission, and Deluge compatibility endpoints
- docs: update GEMINI.md with clarified requirements and Sonarr client compatibility
- docs: add GEMINI.md developer and agent guide

---

## [v1.0.23](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.23) - 2026-08-23

### 🐛 Bug Fixes
- fix(rpc): support dual XML-RPC and JSON-RPC modes in Aria2 controller

---

## [v1.0.22](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.22) - 2026-08-22

### 🐛 Bug Fixes
- fix(rpc): support raw request stream for Aria2 RPC in Sonarr
- fix(rpc): support category mapping for SABnzbd and NZBGet download client connections
- fix(api): refine Aria2, SABnzbd, NZBGet, and Hadouken compatibility routes and schema responses

---

## [v1.0.21](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.21) - 2026-08-21

### ✨ Features
- feat(api): add comprehensive download client compatibility adapters for rTorrent, Aria2, Flood, uTorrent, Hadouken, Synology Download Station, Freebox, SABnzbd, NZBGet, and NZBVortex

---

## [v1.0.20](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.20) - 2026-08-20

### 🔧 Maintenance & Improvements
- Maintenance and stability release

---

## [v1.0.19](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.19) - 2026-08-20

### 🐛 Bug Fixes
- fix(rpc): implement Deluge label.add, label.remove, and label options handlers
- fix(rpc): add Deluge system.listMethods, daemon.get_version, and label handlers for Sonarr compatibility

---

## [v1.0.18](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.18) - 2026-08-19

### 🐛 Bug Fixes
- fix(rpc): enhance Transmission CSRF GET support and Deluge plugin schemas for Sonarr

---

## [v1.0.17](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.17) - 2026-08-18

### 🐛 Bug Fixes
- fix(ui): remove duplicate content-area wrapper around SpeedSchedule in App.tsx

---

## [v1.0.16](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.16) - 2026-08-17

### 🐛 Bug Fixes
- fix(datastore): register NetworkSettings and prevent double-plural table names

---

## [v1.0.15](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.15) - 2026-08-15

### ✨ Features
- feat(api): implement missing core controllers, background handlers, and RPC endpoints
- feat(peers): add PeerConnectionLogController, /api/v1/peerlog/graph, and torrent peers endpoints
- feat(ui): add TorrentToolbar, TorrentFilterPanel, album art thumbnails, and fix detail panel tab styling
- feat(ui): place Activity before Torrents and add Add Torrent sub-item under Activity
- feat(torrents): implement complete torrent management with context menu, custom columns, 9-tab details drawer, and MonoTorrent engine integration
- feat: add AddTorrentPage, dynamic Indexers sub-navigation and scoped search, fix Connected Ecosystem statuses
- feat: add Torrents > History and Activity sub-navigation with DownloadHistory service and UI
- feat(setup): add setup guidance walkthrough for Prowlarr, Sonarr, Radarr, Lidarr and health checks
- feat(network): support nested data.ip extraction in ExternalIpService for leecharr.net/ip and seedarr.net/ip endpoints
- feat(network): implement ExternalIpService with leecharr.net/ip and wire real live data to StatusBar
- feat(settings): implement full suite of 15 settings tabs, system views, activity, and statistics
- feat(ui): replicate full Seedarr dashboard, topbar, status bar, and sidebar styling
- feat(ui): add full Servarr sidebar navigation with Settings and System submenus
- feat(ui): add Seedarr logo-pulse animation to skull emblem
- feat(logo): reproduce Seedarr skull in #F8F4ED and text logo in Storm Gust font ('Leech' in #C7C5D3, 'arr' in #b5443a)
- feat(ui): add PieceMap canvas visualizer, Swarm Inspector, Files tree, and Indexer Discovery search
- feat(indexers): implement Torznab client, Freeleech parser, Prowlarr sync, and RSS grabber
- feat(parity): implement WatchFolder, Webhooks, Scripts, 24x7 Scheduler, Extractor, and VPN Kill Switch
- feat(api): implement Deluge JSON-RPC (/json) and Transmission RPC (/transmission/rpc) adapters with integration tests
- feat(media): implement MediaEnrichmentService unit tests and correlation specs
- feat(engine): implement MonoTorrentDownloadEngine and PiecePicker unit test suite
- feat(container): add multi-stage Containerfile, docker-entrypoint.sh, and MonoTorrentDownloadEngine
- feat(storage): implement Phase 2 data migrations (008-010), StoragePathService, IDiskProvider, and unit/integration test suites
- feat(frontend): implement React 18 / TypeScript 5 frontend SPA and production build
- feat(api): implement native REST API v1, qBittorrent WebAPI v2 adapter, SignalR hub, and console host
- feat(core): implement MediaEnrichmentService, CategoryService, TorrentService, and TorrentFileService
- feat(core): implement PiecePicker, IDownloadEngine, and pure C# MediaContainerInspector with unit tests
- feat(core): implement messaging system, config service, torrent/magnet parser, and unit tests
- feat(core): implement core datastore, migrations 001-007, and domain entities
- feat: initialize Leecharr project scaffolding and architecture documentation

### 🐛 Bug Fixes
- fix(ui): upgrade SpeedSchedule page with full 24x7 matrix view and active limits
- fix(ui, api): wire detail panel tab props, fix using directive orders, and resolve CA3003 in MediaController
- fix(ui): remove duplicate content-area padding and polish action/close buttons
- fix(logs, torrents, settings): add log streaming endpoints, fix context menu bubbling, support json/form torrent grab, and implement config rest controllers
- fix(ui): ensure explicit solid white fill on LeecharrLogo and LeecharrText SVG paths
- fix(ui): set skull icon fill color to pure white
- fix(ui): replace html entities with unicode arrows in buttons and guide
- fix(ui): update logo and typography colors, streamline setup guide, and use full real forms
- fix(layout): lock status bar to bottom and enable independent content-area scrolling
- fix(frontend): wrap root App with BrowserRouter, ThemeProvider, and ToastProvider
- fix(ui): harmonize layout structure, topbar, sidebar and toolbars with authentic Servarr styling

### 🔧 Maintenance & Improvements
- ci: add readd and reAdd to codespell ignore list
- ci: fix dispatch workflow input schema in main.yml
- ci: configure super-linter rules and exclusions matching seedarr
- style(frontend): format index.html to satisfy prettier check in super-linter
- style: add .prettierrc and .prettierignore for ci/cd super-linter parity
- style: apply prettier formatting across frontend
- docs: fix markdown bold formatting in DOCKER_HUB.md
- docs: add DOCKER_HUB.md documentation
- Enable docker build in GitHub Actions workflow
- refactor(cleanup): fix branding, links, storage keys, and peer client badges
- ci: set docker-build to false pending repository secret configuration
- ci: restore valid dispatch workflow inputs
- ci: format markdown with prettier and disable MARKDOWN_PRETTIER in super-linter
- docs: add Docker Hub and GHCR badges to README.md
- docs: add centered skull and text logo to README.md
- set version
- style(sidebar): reduce sidebar logo and text size by 20%
- style(theme): establish distinct visual contrast between sidebar, main canvas, and cards + fix status bar
- style(sidebar): scale skull logo size by +50% to 108px and expand container
- style(sidebar): double skull logo size to 72px and adjust container padding
- test(coverage): expand unit & integration test suites to 114 passing tests
- test: scaffold Leecharr.Integration.Test with in-memory Kestrel test host and SystemTests
- ci: add GitHub Actions workflows, enforce 90% unit test coverage, and integrate integration test suite matching Seedarr
- docs: finalize UPnP/NAT-PMP, multi-instance Servarr categories, clean selective disk allocation, and manual media identification
- style: apply official UI color palette (#10111A, #171B35, #F8F4ED, #C7C5D3, #FFD166) across design system and CSS
- docs: fully update GEMINI.md with comprehensive requirements and architectural specifications
- docs: detail custom script execution env variables, dynamic write cache, and swarm inspector metrics
- docs: capture specifications for VPN kill switch, SOCKS5 proxy, 4-tier bandwidth hierarchy, and queue weighting
- docs: detail RSS grab filters, BEP 27 private tracker compliance, and immediate artwork cache pruning
- docs: record requirements for single watch folder, optional unrar, and 3-tier 24x7 speed schedule
- docs: capture user requirements for storage paths, simultaneous RPC adapters, seeding webhooks, and unified multi-protocol queue
- docs: align architecture and requirements with MonoTorrent, TagLibSharp, SharpCompress, and MaxMind GeoIP
- docs: specify direct indexer support, Torznab search, and Prowlarr sync
- docs: document Servarr webhook connection specification and event triggers
- docs: add comprehensive Deluge architecture, RPC, and plugin requirements specification
- docs: update protocols.md with full BEP catalog and advanced engine mechanics
- docs: update api.md with qBittorrent, Transmission, and Deluge compatibility endpoints
- docs: update GEMINI.md with clarified requirements and Sonarr client compatibility
- docs: add GEMINI.md developer and agent guide

---

## [v1.0.14](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.14) - 2026-08-31

### ✨ Features
- feat(peers): add PeerConnectionLogController, /api/v1/peerlog/graph, and torrent peers endpoints

### 🐛 Bug Fixes
- fix(ui): remove duplicate content-area padding and polish action/close buttons

---

## [v1.0.13](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.13) - 2026-08-31

### ✨ Features
- feat(ui): add TorrentToolbar, TorrentFilterPanel, album art thumbnails, and fix detail panel tab styling

---

## [v1.0.12](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.12) - 2026-08-31

### ✨ Features
- feat(ui): place Activity before Torrents and add Add Torrent sub-item under Activity

---

## [v1.0.11](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.11) - 2026-08-31

### ✨ Features
- feat(torrents): implement complete torrent management with context menu, custom columns, 9-tab details drawer, and MonoTorrent engine integration

### 🐛 Bug Fixes
- fix(logs, torrents, settings): add log streaming endpoints, fix context menu bubbling, support json/form torrent grab, and implement config rest controllers

### 🔧 Maintenance & Improvements
- style: add .prettierrc and .prettierignore for ci/cd super-linter parity

---

## [v1.0.10](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.10) - 2026-08-31

### ✨ Features
- feat: add AddTorrentPage, dynamic Indexers sub-navigation and scoped search, fix Connected Ecosystem statuses
- feat: add Torrents > History and Activity sub-navigation with DownloadHistory service and UI

### 🐛 Bug Fixes
- fix(ui): ensure explicit solid white fill on LeecharrLogo and LeecharrText SVG paths

### 🔧 Maintenance & Improvements
- style: apply prettier formatting across frontend

---

## [v1.0.9](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.9) - 2026-08-31

### 🐛 Bug Fixes
- fix(ui): set skull icon fill color to pure white

---

## [v1.0.8](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.8) - 2026-08-31

### 🐛 Bug Fixes
- fix(ui): replace html entities with unicode arrows in buttons and guide

---

## [v1.0.7](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.7) - 2026-08-31

### 🐛 Bug Fixes
- fix(ui): update logo and typography colors, streamline setup guide, and use full real forms

---

## [v1.0.6](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.6) - 2026-08-31

### ✨ Features
- feat(setup): add setup guidance walkthrough for Prowlarr, Sonarr, Radarr, Lidarr and health checks

---

## [v1.0.5](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.5) - 2026-08-31

### 🔧 Maintenance & Improvements
- docs: fix markdown bold formatting in DOCKER_HUB.md
- docs: add DOCKER_HUB.md documentation

---

## [v1.0.4](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.4) - 2026-08-31

### 🔧 Maintenance & Improvements
- Enable docker build in GitHub Actions workflow

---

## [v1.0.3](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.3) - 2026-08-31

### 🔧 Maintenance & Improvements
- refactor(cleanup): fix branding, links, storage keys, and peer client badges

---

## [v1.0.2](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.2) - 2026-08-31

### 🔧 Maintenance & Improvements
- ci: set docker-build to false pending repository secret configuration

---

## [v1.0.1](https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.1) - 2026-08-31

### ✨ Features
- feat(network): support nested data.ip extraction in ExternalIpService for leecharr.net/ip and seedarr.net/ip endpoints
- feat(network): implement ExternalIpService with leecharr.net/ip and wire real live data to StatusBar
- feat(settings): implement full suite of 15 settings tabs, system views, activity, and statistics
- feat(ui): replicate full Seedarr dashboard, topbar, status bar, and sidebar styling
- feat(ui): add full Servarr sidebar navigation with Settings and System submenus
- feat(ui): add Seedarr logo-pulse animation to skull emblem
- feat(logo): reproduce Seedarr skull in #F8F4ED and text logo in Storm Gust font ('Leech' in #C7C5D3, 'arr' in #b5443a)
- feat(ui): add PieceMap canvas visualizer, Swarm Inspector, Files tree, and Indexer Discovery search
- feat(indexers): implement Torznab client, Freeleech parser, Prowlarr sync, and RSS grabber
- feat(parity): implement WatchFolder, Webhooks, Scripts, 24x7 Scheduler, Extractor, and VPN Kill Switch
- feat(api): implement Deluge JSON-RPC (/json) and Transmission RPC (/transmission/rpc) adapters with integration tests
- feat(media): implement MediaEnrichmentService unit tests and correlation specs
- feat(engine): implement MonoTorrentDownloadEngine and PiecePicker unit test suite
- feat(container): add multi-stage Containerfile, docker-entrypoint.sh, and MonoTorrentDownloadEngine
- feat(storage): implement Phase 2 data migrations (008-010), StoragePathService, IDiskProvider, and unit/integration test suites
- feat(frontend): implement React 18 / TypeScript 5 frontend SPA and production build
- feat(api): implement native REST API v1, qBittorrent WebAPI v2 adapter, SignalR hub, and console host
- feat(core): implement MediaEnrichmentService, CategoryService, TorrentService, and TorrentFileService
- feat(core): implement PiecePicker, IDownloadEngine, and pure C# MediaContainerInspector with unit tests
- feat(core): implement messaging system, config service, torrent/magnet parser, and unit tests
- feat(core): implement core datastore, migrations 001-007, and domain entities
- feat: initialize Leecharr project scaffolding and architecture documentation

### 🐛 Bug Fixes
- fix(layout): lock status bar to bottom and enable independent content-area scrolling
- fix(frontend): wrap root App with BrowserRouter, ThemeProvider, and ToastProvider
- fix(ui): harmonize layout structure, topbar, sidebar and toolbars with authentic Servarr styling

### 🔧 Maintenance & Improvements
- ci: restore valid dispatch workflow inputs
- ci: format markdown with prettier and disable MARKDOWN_PRETTIER in super-linter
- docs: add Docker Hub and GHCR badges to README.md
- docs: add centered skull and text logo to README.md
- set version
- style(sidebar): reduce sidebar logo and text size by 20%
- style(theme): establish distinct visual contrast between sidebar, main canvas, and cards + fix status bar
- style(sidebar): scale skull logo size by +50% to 108px and expand container
- style(sidebar): double skull logo size to 72px and adjust container padding
- test(coverage): expand unit & integration test suites to 114 passing tests
- test: scaffold Leecharr.Integration.Test with in-memory Kestrel test host and SystemTests
- ci: add GitHub Actions workflows, enforce 90% unit test coverage, and integrate integration test suite matching Seedarr
- docs: finalize UPnP/NAT-PMP, multi-instance Servarr categories, clean selective disk allocation, and manual media identification
- style: apply official UI color palette (#10111A, #171B35, #F8F4ED, #C7C5D3, #FFD166) across design system and CSS
- docs: fully update GEMINI.md with comprehensive requirements and architectural specifications
- docs: detail custom script execution env variables, dynamic write cache, and swarm inspector metrics
- docs: capture specifications for VPN kill switch, SOCKS5 proxy, 4-tier bandwidth hierarchy, and queue weighting
- docs: detail RSS grab filters, BEP 27 private tracker compliance, and immediate artwork cache pruning
- docs: record requirements for single watch folder, optional unrar, and 3-tier 24x7 speed schedule
- docs: capture user requirements for storage paths, simultaneous RPC adapters, seeding webhooks, and unified multi-protocol queue
- docs: align architecture and requirements with MonoTorrent, TagLibSharp, SharpCompress, and MaxMind GeoIP
- docs: specify direct indexer support, Torznab search, and Prowlarr sync
- docs: document Servarr webhook connection specification and event triggers
- docs: add comprehensive Deluge architecture, RPC, and plugin requirements specification
- docs: update protocols.md with full BEP catalog and advanced engine mechanics
- docs: update api.md with qBittorrent, Transmission, and Deluge compatibility endpoints
- docs: update GEMINI.md with clarified requirements and Sonarr client compatibility
- docs: add GEMINI.md developer and agent guide

