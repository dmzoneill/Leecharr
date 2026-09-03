export interface Torrent {
  id: number;
  name: string;
  infoHash: string;
  totalSize: number;
  pieceCount: number;
  pieceLength: number;
  comment: string | null;
  createdBy: string | null;
  creationDate: string | null;
  isPrivate: boolean;
  status: string;
  uploaded: number;
  downloaded: number;
  ratio: number;
  progress: number;
  seeders: number;
  leechers: number;
  trackerUrl: string | null;
  sourcePath: string | null;
  dateAdded: string;
  lastActive: string | null;
  priority: number;
  uploadLimit: number;
  downloadLimit: number;
  initialSeeding: boolean;
  forceStart: boolean;
  label: string | null;
  sequentialDownload: boolean;
  announceInterval: number;
  nextUpdate: number;
  sessionUploaded: number;
  sessionDownloaded: number;
  smallTorrentLimit: number;
  threshold: number;
  uploadSpeed: number;
  downloadSpeed: number;
  active: boolean;
  availability: number;
  eta: number;
  sortOrder: number;
  forceCompleted: boolean;
  seedingTime: number;
  queuePosition?: number;
  category?: string | null;
  resolution?: string | null;
  hdrFormat?: string | null;
  videoCodec?: string | null;
  audioCodec?: string | null;
  audioChannels?: string | null;
  artworkUrl?: string | null;
  posterUrl?: string | null;
  fanartUrl?: string | null;
  bannerUrl?: string | null;
  mediaTitle?: string | null;
  year?: number | null;
  overview?: string | null;
  rating?: number | null;
  genres?: string[];
  trackers?: string[];
  source?: string | null;
  targetRatio?: number;
  dateCompleted?: string | null;
}

export interface TrackerMetric {
  id: number;
  trackerUrl: string;
  host: string;
  domain: string;
  protocol: string;
  port: number;
  status: string;
  firstSeen: string;
  lastAnnounce: string | null;
  lastScrape: string | null;
  lastSuccess: string | null;
  lastErrorTime: string | null;
  lastErrorMessage: string | null;
  totalAnnounces: number;
  successfulAnnounces: number;
  failedAnnounces: number;
  announceSuccessRate: number;
  totalScrapes: number;
  successfulScrapes: number;
  failedScrapes: number;
  totalUploaded: number;
  totalDownloaded: number;
  ratio: number;
  totalLeft: number;
  sessionUploaded: number;
  sessionDownloaded: number;
  totalTorrentsTracked: number;
  lastSeeders: number;
  lastLeechers: number;
  lastPeers: number;
  totalPeersDiscovered: number;
  avgResponseTimeMs: number;
  lastResponseTimeMs: number;
  minResponseTimeMs: number;
  maxResponseTimeMs: number;
  consecutiveFailures: number;
}

export interface TrackerMetricSnapshot {
  id: number;
  trackerMetricId: number;
  trackerUrl: string;
  timestamp: string;
  responseTimeMs: number;
  uploaded: number;
  downloaded: number;
  seeders: number;
  leechers: number;
  peersDiscovered: number;
  isSuccess: boolean;
  operation: string;
}

export interface HourlyTrafficPoint {
  timeLabel: string;
  timestamp: string;
  uploaded: number;
  downloaded: number;
  announces: number;
  peersDiscovered: number;
  avgLatencyMs: number;
}

export interface TrackerMetricItemSummary {
  id: number;
  trackerUrl: string;
  domain: string;
  protocol: string;
  status: string;
  totalUploaded: number;
  totalDownloaded: number;
  totalPeersDiscovered: number;
  avgResponseTimeMs: number;
  successRate: number;
}

export interface TrackerMetricsSummary {
  totalTrackers: number;
  healthyTrackers: number;
  degradedTrackers: number;
  offlineTrackers: number;
  totalUploaded: number;
  totalDownloaded: number;
  globalRatio: number;
  totalAnnounces: number;
  successfulAnnounces: number;
  failedAnnounces: number;
  announceSuccessRate: number;
  totalScrapes: number;
  successfulScrapes: number;
  totalPeersDiscovered: number;
  avgResponseTimeMs: number;
  protocolDistribution: Record<string, number>;
  healthDistribution: Record<string, number>;
  topUploadTrackers: TrackerMetricItemSummary[];
  topPeerTrackers: TrackerMetricItemSummary[];
  hourlyHistory: HourlyTrafficPoint[];
}

export interface Category {
  id: number;
  name: string;
  savePath?: string;
  defaultUploadLimit?: number;
  defaultDownloadLimit?: number;
  targetRatio?: number;
  targetSeedTimeMinutes?: number;
  autoStop?: boolean;
  isDefault?: boolean;
}

export interface TorrentFileInfo {
  id: number;
  torrentId: number;
  path: string;
  size: number;
  pieceOffset: number;
  pieceCount: number;
  priority?: number;
  bytesCompleted?: number;
  progress?: number;
}

export type TorrentFile = TorrentFileInfo;

export interface SeedingStats {
  activeTorrents: number;
  totalUploaded: number;
  totalDownloaded: number;
  averageRatio: number;
  overallRatio?: number;
  uploadSpeed?: number;
  downloadSpeed?: number;
}

export interface SpeedSnapshot {
  timestamp: string;
  uploadSpeed: number;
  downloadSpeed: number;
  activeTorrents: number;
  totalPeers: number;
  averageRatio: number;
  totalUploaded: number;
  totalDownloaded: number;
}

export interface TorrentSpeedSnapshot {
  timestamp: string;
  uploadSpeed: number;
  downloadSpeed: number;
}

export interface SystemStatus {
  appName: string;
  version: string;
  instanceUuid?: string;
  buildTime: string;
  isDebug: boolean;
  isProduction: boolean;
  startTime: string;
  osName: string;
  osVersion: string;
  runtimeVersion: string;
  runtimeName: string;
  isDocker: boolean;
  branch: string;
  startupPath: string;
  AppDataPath?: string;
  appDataPath: string;
  databaseVersion: string;
  databaseMigration: string;
  uptimeSeconds: number;
}

export interface DiskSpaceInfo {
  path: string;
  label: string;
  freeSpace: number;
  totalSpace: number;
}

export interface HealthCheckResult {
  type: "Ok" | "Notice" | "Warning" | "Error";
  source: string;
  message: string | null;
}

export interface NetworkStatus {
  localIp: string;
  externalIp: string;
  networkInterface?: string;
  upnpAvailable: boolean;
  proxyEnabled: boolean;
  portMappings: PortMapping[];
}

export interface PortMapping {
  internalPort: number;
  externalPort: number;
  protocol: string;
  description: string;
  isActive: boolean;
}

export interface Peer {
  id: number;
  ip: string;
  port: number;
  client: string;
  uploadSpeed: number;
  downloadSpeed: number;
  uploaded: number;
  downloaded: number;
  progress: number;
  flags: string;
}

export interface TrackerServerStats {
  totalTorrents: number;
  internalTorrents: number;
  totalPeers: number;
  totalAnnounces: number;
  totalScrapes: number;
  uptime: number;
}

export interface GeneralConfig {
  id: number;
  instanceUuid?: string;
  autoStart: boolean;
  themeStyle: string;
  colorScheme: string;
  watchFolderEnabled: boolean;
  watchFolderPath: string;
  watchFolderScanIntervalSeconds: number;
  watchFolderAutoStartTorrents: boolean;
  watchFolderDeleteAddedTorrents: boolean;
  port: number;
  bindAddress: string;
  urlBase: string;
  authenticationEnabled: boolean;
  apiKey: string;
  enableSsl: boolean;
  sslPort: number;
  sslCertPath: string;
  sslKeyPath: string;
  sslCertPassword?: string;
  redirectHttpToHttps: boolean;
}

export interface SslTestRequest {
  enableSsl?: boolean;
  sslPort: number;
  sslCertPath?: string;
  sslKeyPath?: string;
  sslCertPassword?: string;
  bindAddress?: string;
}

export interface SslCertificateValidationResult {
  isValid: boolean;
  subject: string;
  issuer: string;
  validFrom: string;
  validTo: string;
  thumbprint: string;
  hasPrivateKey: boolean;
  subjectAlternativeNames: string[];
  handshakeSucceeded: boolean;
  message: string;
}

export interface SeedingConfig {
  id: number;
  maxUploadSpeedKbps: number;
  maxDownloadSpeedKbps: number;
  alternativeSpeedEnabled: boolean;
  altUploadSpeedKbps: number;
  altDownloadSpeedKbps: number;
  globalSeedRatioLimit: number;
  uploadDistributionAlgorithm: string;
  uploadDistributionSpreadPercentage: number;
  uploadRedistributionMode: string;
  uploadCustomIntervalMinutes: number;
  uploadStoppedMinPercentage: number;
  uploadStoppedMaxPercentage: number;
  downloadDistributionAlgorithm: string;
  downloadDistributionSpreadPercentage: number;
  downloadRedistributionMode: string;
  downloadCustomIntervalMinutes: number;
  downloadStoppedMinPercentage: number;
  downloadStoppedMaxPercentage: number;
  speedVariationMin: number;
  speedVariationMax: number;
}

export interface NetworkConfig {
  id: number;
  listeningPort: number;
  upnpEnabled: boolean;
  bindInterface?: string;
  enableVpnKillSwitch?: boolean;
  maxGlobalConnections: number;
  maxPerTorrentConnections: number;
  maxUploadSlots: number;
  maxConnectionsPerIp?: number;
  maximumHalfOpenConnections?: number;
  anonymousMode?: boolean;
  forceProxy?: boolean;
  peerDscp?: number;
  proxyType: string;
  proxyHost: string;
  proxyPort: number;
  proxyAuthEnabled: boolean;
  proxyUsername: string;
  proxyPassword: string;
}

export interface BitTorrentConfig {
  id: number;
  activeTorrentEngine?: string;

  // BitTorrent Core
  enableDht: boolean;
  enablePex: boolean;
  enableLpd: boolean;
  enableBep27PrivateTorrents?: boolean;
  encryptionMode: string;
  bitTorrentUserAgent: string;
  peerIdPrefix: string;
  announceIntervalSeconds: number;
  minAnnounceIntervalSeconds: number;
  scrapeIntervalSeconds: number;

  // Storage & Incomplete Staging & Preallocation
  downloadDir?: string;
  incompleteDownloadDir?: string;
  enableIncompleteDir?: boolean;
  preallocationMode?: string;
  renamePartialFiles?: boolean;
  umask?: string;

  // Queue & Concurrency Management
  downloadQueueSize?: number;
  seedQueueSize?: number;
  queueStalledEnabled?: boolean;
  queueStalledMinutes?: number;
  idleSeedingLimitMinutes?: number;

  // Network & Sockets Extended
  networkInterfaceBinding?: string;
  maxConnectionsPerIp?: number;
  maximumHalfOpenConnections?: number;
  anonymousMode?: boolean;
  forceProxy?: boolean;
  peerDscp?: number;
  peerPortRandomOnStart?: boolean;
  peerPortRandomLow?: number;
  peerPortRandomHigh?: number;

  // MonoTorrent Specific
  diskCacheBytes?: number;
  diskCachePolicy?: string;
  fastResumeMode?: string;
  autoSaveFastResumeIntervalSeconds?: number;
  autoSaveLoadMagnetMetadata?: boolean;
  autoSaveLoadDhtCache?: boolean;
  piecePickerStrategy?: string;
  endGamePickerEnabled?: boolean;
  staleRequestTimeoutSeconds?: number;
  webSeedDelaySeconds?: number;
  maximumDiskReadRateKbps?: number;
  maximumDiskWriteRateKbps?: number;

  // libtorrent Specific
  hashingThreads?: number;
  aioThreads?: number;
  diskIoWriteMode?: string;
  diskIoReadMode?: string;
  filePoolSize?: number;
  chokingAlgorithm?: string;
  seedChokingAlgorithm?: string;
  mixedModeAlgorithm?: string;
  alertMask?: string;

  // Transmission Specific
  scriptTorrentDoneFilename?: string;
  scriptTorrentAddedFilename?: string;
  scriptTorrentDoneSeedingFilename?: string;
  prefetchEnabled?: boolean;
  scrapePausedTorrentsEnabled?: boolean;
  rpcWhitelistEnabled?: boolean;
  rpcWhitelist?: string;

  // Swarm & Scripts
  onDownloadCompleteScript?: string;
  onSeedGoalReachedScript?: string;
  defaultTrackers?: string;
  dhtBootstrapNodes?: string;
}

export interface PeerProtocolConfig {
  id: number;
  handshakeTimeoutSeconds: number;
  messageReadTimeoutSeconds: number;
  keepAliveIntervalSeconds: number;
  peerContactIntervalSeconds: number;
  udpTrackerTimeoutSeconds: number;
  httpTrackerTimeoutSeconds: number;
  peerRequestCount: number;
  seederUploadActivityProbability: number;
  peerIdleChance: number;
  peerDropoutProbability: number;
  connectionRotationPercentage: number;
}

export interface ProtocolsConfig {
  id: number;
  extensionUtMetadata: boolean;
  extensionUtPex: boolean;
  extensionLtDontHave: boolean;
  extensionFastExtension: boolean;
  enableBep27PrivateTorrents?: boolean;
  utpEnabled: boolean;
  tcpFallback: boolean;
  transportConnectionTimeoutSeconds: number;
  pexInterval: number;
  pexMaxPeersPerMessage: number;
  multiTrackerEnabled: boolean;
  multiTrackerFailoverEnabled: boolean;
  announceToAllTiers: boolean;
  announceToAllInTier: boolean;
  failoverMaxConsecutiveFailures: number;
  failoverBackoffBaseSeconds: number;
  failoverMaxBackoffSeconds: number;
  dhtRoutingTableSize: number;
  dhtAnnouncementInterval: number;
  dhtBootstrapTimeout: number;
  dhtQueryTimeout: number;
  dhtMaxNodes: number;
  dhtBucketSize: number;
  dhtConcurrentQueries: number;
  dhtAutoBootstrap: boolean;
  dhtRateLimitEnabled: boolean;
  dhtMaxQueriesPerSecond: number;
}

export interface SimulationConfig {
  id: number;
  clientBehaviorEngineEnabled: boolean;
  primaryClient: string;
  behaviorVariation: number;
  clientProfileSwitching: boolean;
  switchClientProbability: number;
  trafficPatternProfile: string;
  realisticVariations: boolean;
  timeBasedPatterns: boolean;
  swarmIntelligenceEnabled: boolean;
  swarmAdaptationRate: number;
  swarmPeerAnalysisDepth: number;
}

export interface TrackerServerConfig {
  id: number;
  trackerServerEnabled: boolean;
  trackerHttpEnabled: boolean;
  trackerHttpPort: number;
  trackerUdpEnabled: boolean;
  trackerUdpPort: number;
  trackerBindAddress: string;
  trackerAnnounceInterval: number;
  trackerMaxPeersPerAnnounce: number;
  trackerEnableScrape: boolean;
  trackerPrivateMode: boolean;
  trackerLogAnnounces: boolean;
  trackerRateLimitPerMinute: number;
}

export interface SchedulerConfig {
  id: number;
  schedulerEnabled: boolean;
  enabled?: boolean;
  uploadLimitKBs?: number;
  downloadLimitKBs?: number;
  schedulerStartHour: number;
  schedulerStartMinute: number;
  schedulerEndHour: number;
  schedulerEndMinute: number;
  schedulerMonday: boolean;
  schedulerTuesday: boolean;
  schedulerWednesday: boolean;
  schedulerThursday: boolean;
  schedulerFriday: boolean;
  schedulerSaturday: boolean;
  schedulerSunday: boolean;
}

export interface AdvancedConfig {
  id: number;
  logToFile: boolean;
  fileLogLevel: string;
  debugMode: boolean;
  uiRefreshRateSec: number;
}

export interface NotificationSettings {
  enabled: boolean;
  position: string;
  autoDismissSeconds: number;
  showInfo: boolean;
  showSuccess: boolean;
  showWarning: boolean;
  showError: boolean;
}

export interface ArrTestResult {
  success: boolean;
  message?: string;
}

export interface DownloadClientTestResult {
  success: boolean;
  message?: string;
}

export interface ArrConnection {
  id: number;
  name: string;
  arrType: string;
  url: string;
  apiKey: string;
  enable?: boolean;
  syncEnabled: boolean;
  enableAutomaticAdd: boolean;
  webhookEnabled: boolean;
  webhookHost: string;
  implementation: string;
  configContract: string;
}

export interface DownloadClientDefinition {
  id: number;
  name: string;
  clientType: string;
  host: string;
  port: number;
  useSsl: boolean;
  username: string;
  password: string;
  category: string;
  implementation: string;
  configContract: string;
  enable: boolean;
}

export interface DownloadClientRemoteItem {
  downloadId: string;
  title: string;
  infoHash: string;
  totalSize: number;
  remainingSize: number;
  progress: number;
  status: string;
  outputPath: string;
  category: string;
  isInLibrary: boolean;
  libraryTorrentId?: number | null;
}

export interface IndexerDefinition {
  id: number;
  name: string;
  indexerType: string;
  url: string;
  apiKey: string;
  apiPath: string;
  enableRss: boolean;
  enableSearch: boolean;
  categories: string;
  downloadClientId: number;
  implementation: string;
  configContract: string;
  enable: boolean;
}

export interface IndexerTestResult {
  success: boolean;
  message?: string;
  responseTimeMs?: number;
}

export interface TrackerEntry {
  id: number;
  torrentId: number;
  url: string;
  tier: number;
  status: string;
  enabled: boolean;
  seeders: number;
  leechers: number;
  downloaded: number;
  totalAnnounces: number;
  successfulAnnounces: number;
  consecutiveFailures: number;
  lastResponseTime: number;
  averageResponseTime: number;
  announceInterval: number;
  minAnnounceInterval: number;
  lastAnnounce: string | null;
  lastScrape: string | null;
  nextAnnounce: string | null;
  errorMessage: string | null;
  warningMessage: string | null;
}

export interface TrackerServerTorrent {
  infoHash: string;
  name: string;
  peerCount: number;
  seeders: number;
  leechers: number;
  completed: number;
  uploaded: number;
  downloaded: number;
  isInternal: boolean;
  lastActivity: string | null;
  posterUrl?: string | null;
  fanartUrl?: string | null;
  mediaTitle?: string | null;
  year?: number | null;
  rating?: number | null;
  genres?: string[];
  source?: string | null;
  totalSize?: number;
  ratio?: number;
}

export interface UpdateChanges {
  new: string[];
  fixed: string[];
}

export interface UpdateEntry {
  version: string;
  releaseDate: string;
  installed: boolean;
  latest: boolean;
  changes: UpdateChanges;
}

export interface LogFile {
  filename: string;
  lastWriteTime: string;
  size: number;
}

export interface Backup {
  id: number;
  name: string;
  size: number;
  time: string;
}

export interface PeerGraphNode {
  id: string;
  label: string;
  type: "center" | "torrent" | "peer";
  infoHash?: string;
  isEncrypted?: boolean;
}

export interface PeerGraphLink {
  source: string;
  target: string;
  type: string;
}

export interface PeerGraphData {
  nodes: PeerGraphNode[];
  links: PeerGraphLink[];
}

export interface SpeedScheduleEntry {
  id: number;
  name: string;
  days: number;
  startTime: string;
  endTime: string;
  maxUploadSpeed: number;
  maxDownloadSpeed: number;
  isEnabled: boolean;
  priority: number;
}

export interface SpeedLimits {
  maxUploadSpeed: number;
  maxDownloadSpeed: number;
  isScheduleActive: boolean;
  activeScheduleName: string;
}

export interface Tag {
  id: number;
  label: string;
}

export interface NetworkDiagnostics {
  localIp: string;
  externalIp: string;
  localAddresses: string[];
  upnpAvailable: boolean;
  proxyEnabled: boolean;
  portMappings: PortMapping[];
  listeningPort: number;
  activeConnections: number;
  uploadSlots: number;
  dhtEnabled: boolean;
  dhtNodeCount: number;
  encryptionMode: string;
  encryptedConnections: number;
  plaintextConnections: number;
  encryptionPercentage: number;
}

export interface PeerConnectionLogEntry {
  id: number;
  remoteIp: string;
  remotePort: number;
  infoHash: string;
  torrentName: string;
  peerId: string;
  isEncrypted: boolean;
  eventType: string;
  timestamp: string;
}

export interface TorrentEventLogEntry {
  id: number;
  torrentId: number;
  timeStamp?: string;
  timestamp?: string;
  level: string;
  source?: string;
  message: string;
}

export interface SyncResult {
  added: number;
  skipped: number;
  failed: number;
  syncedCount?: number;
}

export interface MediaActor {
  name: string;
  character?: string | null;
  imageUrl?: string | null;
}

export interface MediaMetadata {
  mediaType?: string | null;
  mediaId?: number | null;
  title?: string | null;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  fanartUrl?: string | null;
  bannerUrl?: string | null;
  genres?: string[];
  actors?: MediaActor[];
  studioOrNetwork?: string | null;
  rating?: number | null;
  imdbId?: string | null;
  tmdbId?: number | null;
  tvdbId?: number | null;
}

export interface DownloadHistoryEntry {
  id: number;
  torrentId: number | null;
  title: string;
  infoHash: string;
  totalSize: number;
  dateAdded: string;
  dateCompleted: string | null;
  dateRemoved: string | null;
  uploaded: number;
  downloaded: number;
  ratio: number;
  seedingTime: number;
  primaryTracker: string | null;
  indexerName: string | null;
  source: string | null;
  magnetUrl: string | null;
  downloadUrl: string | null;
  status: string;
  removalReason: string | null;
  dataJson: string | null;
  metadata?: MediaMetadata | null;
}

export interface ReleaseInfo {
  guid?: string;
  title: string;
  indexerId?: number;
  indexer?: string;
  size: number;
  seeders?: number | null;
  leechers?: number | null;
  publishDate?: string | null;
  downloadUrl?: string | null;
  magnetUrl?: string | null;
  infoHash?: string | null;
  categories?: string[];
  protocol?: string;
  isFreeleech?: boolean;
  downloadVolumeFactor?: number;
}

export interface DownloadReleaseRequest {
  title?: string;
  downloadUrl?: string;
  magnetUrl?: string;
  infoHash?: string;
  indexerId?: number;
  indexerName?: string;
  category?: string;
}

export type TrackerProtocol = "Udp" | "Http" | "Https" | number;
export type TrackerHealthStatus = "Untested" | "Alive" | "Slow" | "Offline" | number;
export type TrackerSourceType =
  "PublicList" | "Prowlarr" | "ReleaseMagnet" | "Manual" | "ActiveTorrent" | number;

export interface TrackerBoostTracker {
  id: number;
  url: string;
  host: string;
  port: number;
  protocol: TrackerProtocol;
  status: TrackerHealthStatus;
  source: TrackerSourceType;
  sourceName: string;
  latencyMs: number;
  lastScraped: string | null;
  lastSuccess: string | null;
  successfulScrapes: number;
  failedScrapes: number;
  totalSwarmsFound: number;
  totalVerifiedTorrents?: number;
  enabled: boolean;
}

export type DownloadPlusPlusTracker = TrackerBoostTracker;

export interface SwarmBoostResult {
  torrentId: number;
  torrentName: string;
  infoHash: string;
  isPrivate: boolean;
  boosted: boolean;
  addedTrackersCount: number;
  addedTrackers: string[];
  totalSeedersFound: number;
  totalLeechersFound: number;
  verifiedCandidateTrackersCount?: number;
  skippedTrackersCount?: number;
  message: string;
}

export interface TrackerBoostStatusSummary {
  totalTrackersMonitored: number;
  aliveTrackersCount: number;
  slowTrackersCount: number;
  offlineTrackersCount: number;
  untestedTrackersCount: number;
  prowlarrTrackersCount: number;
  publicListTrackersCount: number;
  activeTorrentTrackersCount: number;
  torrentsBoostedCount: number;
  extraTrackersInjectedCount: number;
  totalVerifiedMatchesCount: number;
  autoBoostEnabled: boolean;
  autoHarvestEnabled: boolean;
  lastScanTime: string | null;
  lastHarvestTime: string | null;
  lastProwlarrHarvestTime: string | null;
  lastAutoBoostTime: string | null;
}

export type DownloadPlusPlusStatusSummary = TrackerBoostStatusSummary;

export interface TorrentTrackerDetection {
  trackerId: number;
  trackerUrl: string;
  trackerHost: string;
  protocol: TrackerProtocol;
  source: TrackerSourceType;
  sourceName: string;
  isAttached: boolean;
  isDetected: boolean;
  isVerified: boolean;
  seeders: number;
  leechers: number;
  downloaded?: number;
  latencyMs: number;
  healthStatus: TrackerHealthStatus;
  detectionStatus: string;
}

export interface TorrentTrackerInspectionResult {
  torrentId: number;
  torrentName: string;
  infoHash: string;
  isPrivate: boolean;
  isBoosted?: boolean;
  boostedAt?: string | null;
  injectedTrackersCount?: number;
  totalTrackersChecked: number;
  attachedTrackersCount: number;
  detectedTrackersCount: number;
  verifiedTrackersCount: number;
  detections: TorrentTrackerDetection[];
}

export interface TrackerBoostSettings {
  autoBoostEnabled: boolean;
  autoHarvestEnabled: boolean;
  intervalMinutes: number;
  maxTrackersPerTorrent: number;
  onlyVerified: boolean;
}

export interface TorrentMatrixItem {
  torrentId: number;
  torrentName: string;
  infoHash: string;
  isPrivate: boolean;
  isBoosted: boolean;
  attachedTrackersCount: number;
  verifiedTrackersCount: number;
  trackers: TorrentTrackerDetection[];
}

export interface TrackerMatrixItem {
  trackerId: number;
  trackerUrl: string;
  host: string;
  protocol: TrackerProtocol;
  status: TrackerHealthStatus;
  latencyMs: number;
  registeredTorrentsCount: number;
  registeredTorrentNames: string[];
}

export interface TrackerCrossMatrixResult {
  torrents: TorrentMatrixItem[];
  trackers: TrackerMatrixItem[];
}

export interface TrackerBoostLogEntry {
  id: number;
  timestamp: string;
  level: "Info" | "Success" | "Warn" | "Error" | "Debug" | string;
  category:
    "General" | "Scrape" | "Health" | "Discovery" | "Inject" | "Announce" | "Cycle" | string;
  trackerUrl: string;
  infoHash: string;
  message: string;
}

export interface MediaActor {
  name: string;
  character?: string | null;
  imageUrl?: string | null;
}

export interface MediaMetadata {
  mediaType?: string | null;
  mediaId?: number | null;
  title?: string | null;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  fanartUrl?: string | null;
  backdropUrl?: string | null;
  bannerUrl?: string | null;
  genres?: string[];
  actors?: MediaActor[];
  studioOrNetwork?: string | null;
  rating?: number | null;
  imdbId?: string | null;
  tmdbId?: number | null;
  tvdbId?: number | null;
  mediaInfoJson?: string | null;
}

export interface TorrentEngineCapabilities {
  supportsUtp: boolean;
  supportsDht: boolean;
  supportsPex: boolean;
  supportsLpd: boolean;
  supportsV2Torrents: boolean;
  supportsSequentialDownload: boolean;
  supportsFastResume: boolean;
  supportsCustomPiecePickers: boolean;
  supportsDynamicRateLimits: boolean;
  supportsSparseAllocation: boolean;
  supportsMemoryMappedIo: boolean;
  supportsEncryptionToggle: boolean;
}

export interface TorrentEngine {
  id: number;
  engineId: string;
  displayName: string;
  version: string;
  isActive: boolean;
  isAvailable: boolean;
  status: string;
  description: string;
  capabilities: TorrentEngineCapabilities;
  warnings: string[];
}

export interface ActiveEngineStatus {
  engineId: string;
  displayName: string;
  version: string;
  activeTorrentsCount: number;
  connectedPeersCount: number;
  downloadSpeedBytes: number;
  uploadSpeedBytes: number;
  protocolName: string;
}

export interface SwitchEngineRequest {
  engineId: string;
  preserveTransfers: boolean;
}

export interface SwitchEngineResult {
  success: boolean;
  previousEngine: string;
  activeEngine: string;
  torrentsMigrated: number;
  message?: string;
  error?: string;
}

export interface EngineProbeResult {
  engineId: string;
  isHealthy: boolean;
  statusMessage: string;
  dependencyChecks: string[];
  warnings: string[];
}

export interface SubsystemProvider {
  providerId: string;
  displayName: string;
  version: string;
  description: string;
  isActive: boolean;
  isAvailable: boolean;
  status: string;
  capabilities: Record<string, boolean | string | number>;
}

export interface SubsystemOverview {
  id: string;
  name: string;
  category: string;
  description: string;
  activeProviderId: string;
  providers: SubsystemProvider[];
}

export interface SwitchSubsystemRequest {
  subsystemId: string;
  providerId: string;
}

export interface SwitchSubsystemResult {
  success: boolean;
  subsystemId: string;
  previousProvider: string;
  activeProvider: string;
  message?: string;
  error?: string;
}

export interface SubsystemProbeResult {
  subsystemId: string;
  providerId: string;
  isHealthy: boolean;
  statusMessage: string;
  dependencyChecks?: string[];
  warnings?: string[];
}

export interface AiCapabilities {
  supportsNaturalLanguageSearch: boolean;
  supportsReleaseNameParsing: boolean;
  supportsDiagnosticCopilot: boolean;
  supportsMalwareAnomalyDetection: boolean;
  supportsSwarmOptimization: boolean;
  supportsLocalOfflineInference: boolean;
  supportsCloudLlm: boolean;
}

export interface AiStatus {
  activeProviderId: string;
  displayName: string;
  version: string;
  description: string;
  capabilities: AiCapabilities;
  health: {
    isHealthy: boolean;
    statusMessage: string;
    warnings: string[];
    latencyMs: number;
    modelName: string;
    version: string;
  };
}

export interface AiParsedRelease {
  rawTitle: string;
  cleanTitle: string;
  year?: number | null;
  season?: number | null;
  episode?: number | null;
  episodes: number[];
  resolution?: string;
  quality?: string;
  videoCodec?: string;
  audioCodec?: string;
  audioChannels?: string;
  dynamicRange?: string;
  releaseGroup?: string;
  language?: string;
  edition?: string;
  isProper: boolean;
  isRepack: boolean;
  isRemux: boolean;
  confidenceScore: number;
  additionalTags: Record<string, string>;
}

export interface AiDiagnosticReport {
  torrentId: number;
  torrentName: string;
  overallHealth: string;
  severity: "Low" | "Medium" | "High" | string;
  summary: string;
  issues: string[];
  recommendations: string[];
  suggestedActions: string[];
  swarmAnalysis: string;
  trackerAnalysis: string;
  healthScore: number;
  analyzedAt: string;
}

export interface AiSearchParameters {
  rawQuery: string;
  cleanQuery: string;
  cleanTitle?: string;
  category?: string;
  year?: number | null;
  season?: number | null;
  episode?: number | null;
  resolution?: string;
  quality?: string;
  codec?: string;
  releaseGroup?: string;
  minSeeders: number;
  maxAgeDays?: number | null;
  freeleechOnly: boolean;
  tags: string[];
  confidenceScore: number;
}

export interface AiMalwareRiskAssessment {
  torrentName: string;
  riskScore: number;
  riskLevel: "Low" | "Medium" | "High" | "Critical" | string;
  isSuspicious: boolean;
  analyzedFilesCount: number;
  suspiciousFileNames: string[];
  threatReasons: string[];
  recommendations: string[];
  assessedAt: string;
}

export interface AiChatRequest {
  message: string;
  context?: string;
}

export interface AiChatResponse {
  reply: string;
  provider: string;
  success: boolean;
  error?: string;
}

export interface AiConfig {
  activeAiProvider: string;
  ollamaHost: string;
  ollamaModel: string;
  geminiApiKey: string;
  geminiModel: string;
  onnxModelPath: string;
  enableCopilotButton: boolean;
  enableNaturalSearch: boolean;
  enableSwarmDiagnostics: boolean;
}

export type IdentityProviderType = 0 | 1 | 2 | 3; // 0=Oidc, 1=Saml, 2=Social, 3=ForwardAuth

export interface AuthProvider {
  id: number;
  providerId: string;
  name: string;
  providerType: IdentityProviderType;
  iconUrl?: string | null;
  buttonText?: string | null;
  loginUrl: string;
}

export interface CurrentUser {
  id?: number;
  identifier?: string;
  username: string;
  email?: string | null;
  displayName?: string | null;
  roles: string[];
  avatarUrl?: string | null;
  isAuthenticated: boolean;
}

export interface IdentityProviderDefinition {
  id: number;
  providerId: string;
  name: string;
  providerType: IdentityProviderType;
  isEnabled: boolean;
  clientId?: string | null;
  clientSecret?: string | null;
  issuerUrl?: string | null;
  metadataUrl?: string | null;
  scopes?: string | null;
  certificate?: string | null;
  roleMappingRules?: string | null;
  iconUrl?: string | null;
  buttonText?: string | null;
}

export interface DiskMountPointMetrics {
  mountPoint: string;
  driveType: string;
  totalSpaceBytes: number;
  freeSpaceBytes: number;
  usedSpaceBytes: number;
  usedPercent: number;
}

export interface HostProcessResourceMetrics {
  cpuProcessPercent: number;
  cpuCores: number;
  workingSetBytes: number;
  privateMemoryBytes: number;
  virtualMemoryBytes: number;
  managedHeapBytes: number;
  gcGen0Collections: number;
  gcGen1Collections: number;
  gcGen2Collections: number;
  threadCount: number;
  threadPoolWorkerThreads: number;
  threadPoolCompletionPortThreads: number;
  handleCount: number;
  uptimeSeconds: number;
  diskDrives: DiskMountPointMetrics[];
  timestamp: string;
}

export interface TorrentEngineMetrics {
  engineId: string;
  displayName: string;
  version: string;
  isRunning: boolean;
  activeTorrents: number;
  downloadingTorrents: number;
  seedingTorrents: number;
  pausedTorrents: number;
  totalDownloadSpeed: number;
  totalUploadSpeed: number;
  totalProtocolDownloadSpeed: number;
  totalProtocolUploadSpeed: number;
  totalDataDownloaded: number;
  totalDataUploaded: number;
  totalProtocolDownloaded: number;
  totalProtocolUploaded: number;
  protocolOverheadPercentage: number;
  openConnections: number;
  halfOpenConnections: number;
  maxConnections: number;
  connectedSeeds: number;
  connectedLeechers: number;
  totalSwarmPeers: number;
  dhtNodeCount: number;
  dhtState: string;
  diskCacheBytesAllocated: number;
  diskCacheCapacityBytes: number;
  diskCacheHitRatio: number;
  diskCacheHits: number;
  diskCacheMisses: number;
  diskPendingWrites: number;
  diskPendingReads: number;
  diskTotalBytesWritten: number;
  diskTotalBytesRead: number;
  diskWriteRate: number;
  diskReadRate: number;
  piecesHashedPerSec: number;
  hashFailsTotal: number;
  encryptedConnectionsCount: number;
  plaintextConnectionsCount: number;
  utpConnectionsCount: number;
  tcpConnectionsCount: number;
  timestamp: string;
}

export interface TorrentResourceMetrics {
  torrentId: number;
  infoHash: string;
  name: string;
  category: string;
  status: string;
  progress: number;
  totalBytes: number;
  payloadDownloadSpeed: number;
  payloadUploadSpeed: number;
  protocolDownloadSpeed: number;
  protocolUploadSpeed: number;
  downloadedPayload: number;
  uploadedPayload: number;
  protocolDownloaded: number;
  protocolUploaded: number;
  efficiencyRatio: number;
  connectedPeers: number;
  connectedSeeds: number;
  connectedLeechers: number;
  totalAvailablePeers: number;
  tcpPeers: number;
  utpPeers: number;
  encryptedPeers: number;
  plaintextPeers: number;
  totalPieces: number;
  completedPieces: number;
  piecesInFlight: number;
  pieceLength: number;
  hashFails: number;
  wastedBytes: number;
  diskPendingWrites: number;
  estimatedMemoryBufferBytes: number;
  swarmAvailability: number;
  ratio: number;
  etaSeconds?: number | null;
}

export interface SubsystemTelemetryReport {
  subsystemId: string;
  subsystemName: string;
  activeProvider: string;
  status: string;
  resourceLoad: string;
  metrics: Record<string, any>;
}

export interface SystemResourceTelemetrySnapshot {
  host: HostProcessResourceMetrics;
  torrentEngine: TorrentEngineMetrics;
  perTorrent: TorrentResourceMetrics[];
  subsystems: SubsystemTelemetryReport[];
  timestamp: string;
}
