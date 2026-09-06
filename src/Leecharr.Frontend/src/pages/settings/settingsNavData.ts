export type SettingsGroupId =
  | "general-security"
  | "storage-queues"
  | "bittorrent-engine"
  | "network-bandwidth"
  | "integrations"
  | "advanced-ai";

export interface SettingsPageDefinition {
  id: string;
  groupId: SettingsGroupId;
  title: string;
  shortLabel: string;
  description: string;
  icon: string;
  badge?: string;
  keywords: string[];
}

export interface SettingsGroupDefinition {
  id: SettingsGroupId;
  title: string;
  shortLabel: string;
  description: string;
  icon: string;
  badge?: string;
  pages: SettingsPageDefinition[];
}

export const SETTINGS_GROUPS: SettingsGroupDefinition[] = [
  {
    id: "general-security",
    title: "settingsTabs.nav.groups.generalSecurity.title",
    shortLabel: "settingsTabs.nav.groups.generalSecurity.shortLabel",
    description: "settingsTabs.nav.groups.generalSecurity.description",
    icon: "⚙️",
    pages: [
      {
        id: "host",
        groupId: "general-security",
        title: "settingsTabs.nav.groups.generalSecurity.pages.host.title",
        shortLabel:
          "settingsTabs.nav.groups.generalSecurity.pages.host.shortLabel",
        description:
          "settingsTabs.nav.groups.generalSecurity.pages.host.description",
        icon: "🖥️",
        badge: "settingsTabs.nav.groups.generalSecurity.pages.host.badge",
        keywords: [
          "port",
          "bind address",
          "url base",
          "reverse proxy",
          "autostart",
          "http",
          "launch",
          "ssl",
        ],
      },
      {
        id: "webui",
        groupId: "general-security",
        title: "settingsTabs.nav.groups.generalSecurity.pages.webui.title",
        shortLabel:
          "settingsTabs.nav.groups.generalSecurity.pages.webui.shortLabel",
        description:
          "settingsTabs.nav.groups.generalSecurity.pages.webui.description",
        icon: "🎨",
        keywords: [
          "theme",
          "dark mode",
          "accent color",
          "palette",
          "gold",
          "sapphire",
          "emerald",
          "styling",
          "toasts",
        ],
      },
      {
        id: "security",
        groupId: "general-security",
        title: "settingsTabs.nav.groups.generalSecurity.pages.security.title",
        shortLabel:
          "settingsTabs.nav.groups.generalSecurity.pages.security.shortLabel",
        description:
          "settingsTabs.nav.groups.generalSecurity.pages.security.description",
        icon: "🔒",
        badge: "settingsTabs.nav.groups.generalSecurity.pages.security.badge",
        keywords: [
          "api key",
          "password",
          "authentication",
          "login",
          "forms",
          "basic auth",
          "security",
          "token",
        ],
      },
      {
        id: "watch-folder",
        groupId: "general-security",
        title:
          "settingsTabs.nav.groups.generalSecurity.pages.watchFolder.title",
        shortLabel:
          "settingsTabs.nav.groups.generalSecurity.pages.watchFolder.shortLabel",
        description:
          "settingsTabs.nav.groups.generalSecurity.pages.watchFolder.description",
        icon: "📁",
        keywords: [
          "watch folder",
          "scan interval",
          "auto start",
          "delete torrent",
          "drop folder",
          "directory monitor",
        ],
      },
    ],
  },
  {
    id: "storage-queues",
    title: "settingsTabs.nav.groups.storageQueues.title",
    shortLabel: "settingsTabs.nav.groups.storageQueues.shortLabel",
    description: "settingsTabs.nav.groups.storageQueues.description",
    icon: "💾",
    pages: [
      {
        id: "storage",
        groupId: "storage-queues",
        title: "settingsTabs.nav.groups.storageQueues.pages.storage.title",
        shortLabel:
          "settingsTabs.nav.groups.storageQueues.pages.storage.shortLabel",
        description:
          "settingsTabs.nav.groups.storageQueues.pages.storage.description",
        icon: "💽",
        keywords: [
          "download dir",
          "downloads folder",
          "default download directory",
          "completed downloads",
          "save path",
          "incomplete dir",
          "staging",
          "preallocation",
          "sparse",
          "full",
          "umask",
          "permissions",
          ".part",
        ],
      },
      {
        id: "queue",
        groupId: "storage-queues",
        title: "settingsTabs.nav.groups.storageQueues.pages.queue.title",
        shortLabel:
          "settingsTabs.nav.groups.storageQueues.pages.queue.shortLabel",
        description:
          "settingsTabs.nav.groups.storageQueues.pages.queue.description",
        icon: "📊",
        badge: "settingsTabs.nav.groups.storageQueues.pages.queue.badge",
        keywords: [
          "queue size",
          "concurrency",
          "active downloads",
          "active seeds",
          "stalled",
          "ratio limit",
          "seed time",
          "category",
          "categories",
          "save path",
          "label",
        ],
      },
      {
        id: "categories",
        groupId: "storage-queues",
        title: "settingsTabs.nav.groups.storageQueues.pages.categories.title",
        shortLabel:
          "settingsTabs.nav.groups.storageQueues.pages.categories.shortLabel",
        description:
          "settingsTabs.nav.groups.storageQueues.pages.categories.description",
        icon: "🏷️",
        badge: "settingsTabs.nav.groups.storageQueues.pages.categories.badge",
        keywords: [
          "category",
          "categories",
          "save path",
          "label",
          "labels",
          "download path",
          "custom path",
          "path routing",
          "rate limits",
          "ratio limit",
        ],
      },
      {
        id: "custom-scripts",
        groupId: "storage-queues",
        title:
          "settingsTabs.nav.groups.storageQueues.pages.customScripts.title",
        shortLabel:
          "settingsTabs.nav.groups.storageQueues.pages.customScripts.shortLabel",
        description:
          "settingsTabs.nav.groups.storageQueues.pages.customScripts.description",
        icon: "📜",
        keywords: [
          "scripts",
          "hooks",
          "on complete",
          "on seed goal",
          "automation",
          "bash",
          "python",
          "webhook",
        ],
      },
    ],
  },
  {
    id: "bittorrent-engine",
    title: "settingsTabs.nav.groups.bittorrentEngine.title",
    shortLabel: "settingsTabs.nav.groups.bittorrentEngine.shortLabel",
    description: "settingsTabs.nav.groups.bittorrentEngine.description",
    icon: "⚡",
    badge: "settingsTabs.nav.groups.bittorrentEngine.badge",
    pages: [
      {
        id: "engine",
        groupId: "bittorrent-engine",
        title: "settingsTabs.nav.groups.bittorrentEngine.pages.engine.title",
        shortLabel:
          "settingsTabs.nav.groups.bittorrentEngine.pages.engine.shortLabel",
        description:
          "settingsTabs.nav.groups.bittorrentEngine.pages.engine.description",
        icon: "🔄",
        badge: "settingsTabs.nav.groups.bittorrentEngine.pages.engine.badge",
        keywords: [
          "engine",
          "monotorrent",
          "libtorrent",
          "rasterbar",
          "transmission",
          "hot-swap",
          "disk cache",
          "fastresume",
        ],
      },
      {
        id: "protocols",
        groupId: "bittorrent-engine",
        title: "settingsTabs.nav.groups.bittorrentEngine.pages.protocols.title",
        shortLabel:
          "settingsTabs.nav.groups.bittorrentEngine.pages.protocols.shortLabel",
        description:
          "settingsTabs.nav.groups.bittorrentEngine.pages.protocols.description",
        icon: "📡",
        keywords: [
          "bep",
          "ut_metadata",
          "ut_pex",
          "lt_donthave",
          "fast extension",
          "utp",
          "ledbat",
          "tcp fallback",
          "encryption",
        ],
      },
      {
        id: "dht",
        groupId: "bittorrent-engine",
        title: "settingsTabs.nav.groups.bittorrentEngine.pages.dht.title",
        shortLabel:
          "settingsTabs.nav.groups.bittorrentEngine.pages.dht.shortLabel",
        description:
          "settingsTabs.nav.groups.bittorrentEngine.pages.dht.description",
        icon: "🌐",
        keywords: [
          "dht",
          "bootstrap nodes",
          "routing table",
          "trackerless",
          "bep 5",
          "queries per second",
          "pex",
          "lpd",
        ],
      },
      {
        id: "client-emulation",
        groupId: "bittorrent-engine",
        title:
          "settingsTabs.nav.groups.bittorrentEngine.pages.clientEmulation.title",
        shortLabel:
          "settingsTabs.nav.groups.bittorrentEngine.pages.clientEmulation.shortLabel",
        description:
          "settingsTabs.nav.groups.bittorrentEngine.pages.clientEmulation.description",
        icon: "🎭",
        keywords: [
          "client emulation",
          "simulation",
          "qbittorrent",
          "deluge",
          "transmission",
          "utorrent",
          "traffic pattern",
          "diurnal",
          "peer id",
        ],
      },
      {
        id: "tracker-server",
        groupId: "bittorrent-engine",
        title:
          "settingsTabs.nav.groups.bittorrentEngine.pages.trackerServer.title",
        shortLabel:
          "settingsTabs.nav.groups.bittorrentEngine.pages.trackerServer.shortLabel",
        description:
          "settingsTabs.nav.groups.bittorrentEngine.pages.trackerServer.description",
        icon: "🛰️",
        badge:
          "settingsTabs.nav.groups.bittorrentEngine.pages.trackerServer.badge",
        keywords: [
          "tracker server",
          "embedded tracker",
          "http tracker",
          "udp tracker",
          "scrape",
          "private mode",
          "whitelist",
        ],
      },
    ],
  },
  {
    id: "network-bandwidth",
    title: "settingsTabs.nav.groups.networkBandwidth.title",
    shortLabel: "settingsTabs.nav.groups.networkBandwidth.shortLabel",
    description: "settingsTabs.nav.groups.networkBandwidth.description",
    icon: "🌐",
    pages: [
      {
        id: "speed",
        groupId: "network-bandwidth",
        title: "settingsTabs.nav.groups.networkBandwidth.pages.speed.title",
        shortLabel:
          "settingsTabs.nav.groups.networkBandwidth.pages.speed.shortLabel",
        description:
          "settingsTabs.nav.groups.networkBandwidth.pages.speed.description",
        icon: "🚀",
        keywords: [
          "speed limit",
          "upload cap",
          "download cap",
          "alternative speed",
          "distribution curve",
          "pareto",
          "powerlaw",
        ],
      },
      {
        id: "schedule",
        groupId: "network-bandwidth",
        title: "settingsTabs.nav.groups.networkBandwidth.pages.schedule.title",
        shortLabel:
          "settingsTabs.nav.groups.networkBandwidth.pages.schedule.shortLabel",
        description:
          "settingsTabs.nav.groups.networkBandwidth.pages.schedule.description",
        icon: "🕒",
        keywords: [
          "scheduler",
          "weekly schedule",
          "hourly",
          "peak hours",
          "speed throttling",
          "time based",
        ],
      },
      {
        id: "network",
        groupId: "network-bandwidth",
        title: "settingsTabs.nav.groups.networkBandwidth.pages.network.title",
        shortLabel:
          "settingsTabs.nav.groups.networkBandwidth.pages.network.shortLabel",
        description:
          "settingsTabs.nav.groups.networkBandwidth.pages.network.description",
        icon: "🛡️",
        badge: "settingsTabs.nav.groups.networkBandwidth.pages.network.badge",
        keywords: [
          "network interface",
          "bind",
          "tun0",
          "wg0",
          "vpn kill switch",
          "listening port",
          "upnp",
          "max connections",
        ],
      },
      {
        id: "proxy",
        groupId: "network-bandwidth",
        title: "settingsTabs.nav.groups.networkBandwidth.pages.proxy.title",
        shortLabel:
          "settingsTabs.nav.groups.networkBandwidth.pages.proxy.shortLabel",
        description:
          "settingsTabs.nav.groups.networkBandwidth.pages.proxy.description",
        icon: "🔀",
        keywords: [
          "proxy",
          "socks5",
          "socks4",
          "http proxy",
          "anonymous mode",
          "force proxy",
          "privacy",
        ],
      },
    ],
  },
  {
    id: "integrations",
    title: "settingsTabs.nav.groups.integrations.title",
    shortLabel: "settingsTabs.nav.groups.integrations.shortLabel",
    description: "settingsTabs.nav.groups.integrations.description",
    icon: "🔌",
    pages: [
      {
        id: "indexers",
        groupId: "integrations",
        title: "settingsTabs.nav.groups.integrations.pages.indexers.title",
        shortLabel:
          "settingsTabs.nav.groups.integrations.pages.indexers.shortLabel",
        description:
          "settingsTabs.nav.groups.integrations.pages.indexers.description",
        icon: "🔍",
        keywords: [
          "indexers",
          "prowlarr",
          "jackett",
          "torznab",
          "newznab",
          "search",
          "rss sync",
        ],
      },
      {
        id: "connections",
        groupId: "integrations",
        title: "settingsTabs.nav.groups.integrations.pages.connections.title",
        shortLabel:
          "settingsTabs.nav.groups.integrations.pages.connections.shortLabel",
        description:
          "settingsTabs.nav.groups.integrations.pages.connections.description",
        icon: "📺",
        badge: "settingsTabs.nav.groups.integrations.pages.connections.badge",
        keywords: [
          "servarr",
          "sonarr",
          "radarr",
          "lidarr",
          "readarr",
          "webhook",
          "media manager",
          "sync",
        ],
      },
      {
        id: "download-clients",
        groupId: "integrations",
        title:
          "settingsTabs.nav.groups.integrations.pages.downloadClients.title",
        shortLabel:
          "settingsTabs.nav.groups.integrations.pages.downloadClients.shortLabel",
        description:
          "settingsTabs.nav.groups.integrations.pages.downloadClients.description",
        icon: "📥",
        keywords: [
          "download clients",
          "qbittorrent api",
          "transmission rpc",
          "deluge rpc",
          "import state",
          "dual client",
        ],
      },
      {
        id: "notifications",
        groupId: "integrations",
        title: "settingsTabs.nav.groups.integrations.pages.notifications.title",
        shortLabel:
          "settingsTabs.nav.groups.integrations.pages.notifications.shortLabel",
        description:
          "settingsTabs.nav.groups.integrations.pages.notifications.description",
        icon: "🔔",
        keywords: [
          "notifications",
          "webhooks",
          "discord",
          "telegram",
          "gotify",
          "alerts",
          "events",
          "polly",
        ],
      },
    ],
  },
  {
    id: "advanced-ai",
    title: "settingsTabs.nav.groups.advancedAi.title",
    shortLabel: "settingsTabs.nav.groups.advancedAi.shortLabel",
    description: "settingsTabs.nav.groups.advancedAi.description",
    icon: "🧠",
    badge: "settingsTabs.nav.groups.advancedAi.badge",
    pages: [
      {
        id: "subsystems",
        groupId: "advanced-ai",
        title: "settingsTabs.nav.groups.advancedAi.pages.subsystems.title",
        shortLabel:
          "settingsTabs.nav.groups.advancedAi.pages.subsystems.shortLabel",
        description:
          "settingsTabs.nav.groups.advancedAi.pages.subsystems.description",
        icon: "🧩",
        badge: "settingsTabs.nav.groups.advancedAi.pages.subsystems.badge",
        keywords: [
          "subsystems",
          "pluggable",
          "providers",
          "hot-swap",
          "archive extractor",
          "media inspector",
          "geoip",
          "blocklist",
        ],
      },
      {
        id: "ai",
        groupId: "advanced-ai",
        title: "settingsTabs.nav.groups.advancedAi.pages.ai.title",
        shortLabel: "settingsTabs.nav.groups.advancedAi.pages.ai.shortLabel",
        description: "settingsTabs.nav.groups.advancedAi.pages.ai.description",
        icon: "✨",
        badge: "settingsTabs.nav.groups.advancedAi.pages.ai.badge",
        keywords: [
          "ai",
          "copilot",
          "ollama",
          "gemini",
          "onnx",
          "natural language",
          "diagnostics",
          "floating button",
        ],
      },
      {
        id: "logging",
        groupId: "advanced-ai",
        title: "settingsTabs.nav.groups.advancedAi.pages.logging.title",
        shortLabel:
          "settingsTabs.nav.groups.advancedAi.pages.logging.shortLabel",
        description:
          "settingsTabs.nav.groups.advancedAi.pages.logging.description",
        icon: "🛠️",
        keywords: [
          "logging",
          "log level",
          "trace",
          "debug mode",
          "vacuum",
          "sqlite",
          "cache purge",
          "maintenance",
        ],
      },
    ],
  },
];

// Helper: Resolve legacy URLs and aliases to exact (groupId, pageId)
export const LEGACY_SETTINGS_MAP: Record<
  string,
  { groupId: SettingsGroupId; pageId: string }
> = {
  general: { groupId: "general-security", pageId: "host" },
  host: { groupId: "general-security", pageId: "host" },
  webui: { groupId: "general-security", pageId: "webui" },
  theme: { groupId: "general-security", pageId: "webui" },
  security: { groupId: "general-security", pageId: "security" },
  auth: { groupId: "general-security", pageId: "security" },
  "watch-folder": { groupId: "general-security", pageId: "watch-folder" },
  watch: { groupId: "general-security", pageId: "watch-folder" },

  storage: { groupId: "storage-queues", pageId: "storage" },
  staging: { groupId: "storage-queues", pageId: "storage" },
  seeding: { groupId: "storage-queues", pageId: "queue" },
  queue: { groupId: "storage-queues", pageId: "queue" },
  categories: { groupId: "storage-queues", pageId: "categories" },
  category: { groupId: "storage-queues", pageId: "categories" },
  label: { groupId: "storage-queues", pageId: "categories" },
  labels: { groupId: "storage-queues", pageId: "categories" },
  "custom-scripts": { groupId: "storage-queues", pageId: "custom-scripts" },
  scripts: { groupId: "storage-queues", pageId: "custom-scripts" },

  bittorrent: { groupId: "bittorrent-engine", pageId: "engine" },
  engine: { groupId: "bittorrent-engine", pageId: "engine" },
  egnine: { groupId: "bittorrent-engine", pageId: "engine" },
  protocols: { groupId: "bittorrent-engine", pageId: "protocols" },
  beps: { groupId: "bittorrent-engine", pageId: "protocols" },
  dht: { groupId: "bittorrent-engine", pageId: "dht" },
  discovery: { groupId: "bittorrent-engine", pageId: "dht" },
  "client-emulation": {
    groupId: "bittorrent-engine",
    pageId: "client-emulation",
  },
  "peer-protocol": { groupId: "bittorrent-engine", pageId: "client-emulation" },
  simulation: { groupId: "bittorrent-engine", pageId: "client-emulation" },
  swarm: { groupId: "bittorrent-engine", pageId: "client-emulation" },
  "tracker-server": { groupId: "bittorrent-engine", pageId: "tracker-server" },
  tracker: { groupId: "bittorrent-engine", pageId: "tracker-server" },

  speed: { groupId: "network-bandwidth", pageId: "speed" },
  bandwidth: { groupId: "network-bandwidth", pageId: "speed" },
  limits: { groupId: "network-bandwidth", pageId: "speed" },
  schedule: { groupId: "network-bandwidth", pageId: "schedule" },
  scheduler: { groupId: "network-bandwidth", pageId: "schedule" },
  network: { groupId: "network-bandwidth", pageId: "network" },
  vpn: { groupId: "network-bandwidth", pageId: "network" },
  proxy: { groupId: "network-bandwidth", pageId: "proxy" },
  privacy: { groupId: "network-bandwidth", pageId: "proxy" },

  indexers: { groupId: "integrations", pageId: "indexers" },
  torznab: { groupId: "integrations", pageId: "indexers" },
  prowlarr: { groupId: "integrations", pageId: "indexers" },
  connections: { groupId: "integrations", pageId: "connections" },
  servarr: { groupId: "integrations", pageId: "connections" },
  "download-clients": { groupId: "integrations", pageId: "download-clients" },
  clients: { groupId: "integrations", pageId: "download-clients" },
  notifications: { groupId: "integrations", pageId: "notifications" },
  webhooks: { groupId: "integrations", pageId: "notifications" },

  subsystems: { groupId: "advanced-ai", pageId: "subsystems" },
  ai: { groupId: "advanced-ai", pageId: "ai" },
  copilot: { groupId: "advanced-ai", pageId: "ai" },
  logging: { groupId: "advanced-ai", pageId: "logging" },
  logs: { groupId: "advanced-ai", pageId: "logging" },
  advanced: { groupId: "advanced-ai", pageId: "logging" },
};
