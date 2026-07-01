export interface Torrent {
  id: number;
  name: string;
  infoHash: string;
  totalSize: number;
  pieceCount: number;
  pieceLength: number;
  comment?: string;
  createdBy?: string;
  creationDate?: string;
  isPrivate: boolean;
  status: 'queued' | 'checking' | 'downloading' | 'seeding' | 'paused' | 'stopped' | 'error';
  downloaded: number;
  uploaded: number;
  ratio: number;
  progress: number;
  downloadSpeed: number;
  uploadSpeed: number;
  eta: number;
  seeders: number;
  leechers: number;
  savePath: string;
  category: string;
  priority: number;
  downloadLimit: number;
  uploadLimit: number;
  sequentialDownload: boolean;
  targetRatio: number;
  targetSeedTimeMinutes: number;
  dateAdded: string;
  dateCompleted?: string;
  lastActive?: string;
  tagIds: number[];

  // Enriched media metadata
  mediaTitle?: string;
  mediaYear?: number;
  mediaOverview?: string;
  posterUrl?: string;
  backdropUrl?: string;
  resolution?: string;
  videoCodec?: string;
  audioCodec?: string;
  audioChannels?: string;
  hdrFormat?: string;
  mediaRating?: number;
}

export interface TorrentFile {
  id: number;
  torrentId: number;
  path: string;
  size: number;
  pieceOffset: number;
  pieceCount: number;
  priority: number;
  progress: number;
}

export interface Category {
  id: number;
  name: string;
  savePath: string;
  defaultUploadLimit: number;
  defaultDownloadLimit: number;
  targetRatio: number;
  targetSeedTimeMinutes: number;
  autoStop: boolean;
  isDefault: boolean;
}

export interface Peer {
  ip: string;
  port: number;
  client: string;
  progress: number;
  downloadSpeed: number;
  uploadSpeed: number;
  countryCode: string;
  countryName: string;
  protocol: 'TCP' | 'uTP';
  isEncrypted: boolean;
  flags: string;
}

export interface SpeedSchedule {
  id: number;
  name: string;
  days: number;
  startTime: string;
  endTime: string;
  maxDownloadSpeed: number;
  maxUploadSpeed: number;
  isEnabled: boolean;
  priority: number;
}

export interface IndexerSearchResult {
  title: string;
  guid: string;
  downloadUrl: string;
  magnetUrl: string;
  infoHash: string;
  size: number;
  seeders: number;
  leechers: number;
  downloadVolumeFactor: number;
  isFreeleech: boolean;
  category: string;
  indexerName: string;
  indexerId: number;
}

export interface NetworkSettings {
  bindInterface: string;
  enableVpnKillSwitch: boolean;
  enableUpnp: boolean;
  listenPort: number;
  enableProxy: boolean;
  proxyType: string;
  proxyHost: string;
  proxyPort: number;
  anonymousMode: boolean;
}

export interface SystemStatus {
  appName: string;
  version: string;
  osName: string;
  osVersion: string;
  runtimeVersion: string;
  isDocker: boolean;
  isLinux: boolean;
  isWindows: boolean;
  isOsx: boolean;
  appDataFolder: string;
  startTime: string;
}
