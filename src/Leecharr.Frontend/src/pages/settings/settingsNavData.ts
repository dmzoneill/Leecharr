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
    title: "General & Security",
    shortLabel: "General",
    description:
      "Application hosting, interface theming, authentication, and watch folder directory monitoring",
    icon: "⚙️",
    pages: [
      {
        id: "host",
        groupId: "general-security",
        title: "Host & Web Server",
        shortLabel: "Host & Server",
        description:
          "Configure HTTP listening port, network bind address, and reverse proxy URL sub-path",
        icon: "🖥️",
        badge: "Core",
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
        title: "Web UI & Appearance",
        shortLabel: "Web UI & Themes",
        description:
          "Dark/light surfaces, typography contrast, brand accent palettes, and display formatting",
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
        title: "Security & Authentication",
        shortLabel: "Security & API",
        description:
          "Forms/Basic user authentication and Leecharr REST API key (X-Api-Key) management",
        icon: "🔒",
        badge: "Auth",
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
        title: "Automated Watch Folder",
        shortLabel: "Watch Folder",
        description:
          "Monitor directories for incoming .torrent payload files with automated queue import",
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
    title: "Storage & Queues",
    shortLabel: "Storage",
    description:
      "Download staging directories, sparse preallocation, queue concurrency, and automation hooks",
    icon: "💾",
    pages: [
      {
        id: "storage",
        groupId: "storage-queues",
        title: "Download Staging & File Storage",
        shortLabel: "Storage & Staging",
        description:
          "Incomplete staging paths, sparse disk preallocation mode, POSIX umask, and partial file extensions",
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
        title: "Queue & Concurrency Management",
        shortLabel: "Queue & Limits",
        description:
          "Active download/seed queue limits, stalled transfer timeouts, and share ratio limits",
        icon: "📊",
        badge: "Limits",
        keywords: [
          "queue size",
          "concurrency",
          "active downloads",
          "active seeds",
          "stalled",
          "ratio limit",
          "seed time",
        ],
      },
      {
        id: "custom-scripts",
        groupId: "storage-queues",
        title: "Custom Script Execution",
        shortLabel: "Script Hooks",
        description:
          "Trigger custom shell/python scripts on download complete and seed goal reached events",
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
    title: "BitTorrent Engine",
    shortLabel: "Engine",
    description:
      "Runtime engine hot-swapping, BEP protocol extensions, DHT discovery, client emulation, and tracker server",
    icon: "⚡",
    badge: "Hot-Swap",
    pages: [
      {
        id: "engine",
        groupId: "bittorrent-engine",
        title: "Engine Core & Tuning",
        shortLabel: "Engine Core",
        description:
          "Select and switch between MonoTorrent, libtorrent (Rasterbar), and Transmission daemon engines",
        icon: "🔄",
        badge: "Dynamic",
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
        title: "Protocols & BEP Extensions",
        shortLabel: "Protocols & BEPs",
        description:
          "ut_metadata (BEP 9), PEX (BEP 11), lt_donthave (BEP 54), Fast Extension, uTP LEDBAT, and encryption",
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
        title: "DHT & Peer Discovery",
        shortLabel: "DHT Discovery",
        description:
          "BEP 5 Distributed Hash Table parameters, bootstrap routing nodes, rate limiting, and table sizes",
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
        title: "Client Emulation & Identity",
        shortLabel: "Client Emulation",
        description:
          "Emulate qBittorrent, Deluge, Transmission, or uTorrent signatures and organic diurnal traffic curves",
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
        title: "Inbuilt Tracker Server Daemon",
        shortLabel: "Tracker Server",
        description:
          "Embedded high-performance HTTP (9696) and UDP (6969) BitTorrent tracker server daemon",
        icon: "🛰️",
        badge: "Daemon",
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
    title: "Network & Bandwidth",
    shortLabel: "Network",
    description:
      "Global speed caps, alternative profiles, 24x7 hourly scheduling matrix, VPN kill-switch, and proxy routing",
    icon: "🌐",
    pages: [
      {
        id: "speed",
        groupId: "network-bandwidth",
        title: "Speed Limits & Bandwidth Profiles",
        shortLabel: "Speed Limits",
        description:
          "Global rate limits, alternative speed profiles, and mathematical swarm distribution curves (Pareto/PowerLaw)",
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
        title: "Speed Schedule (24x7 Matrix)",
        shortLabel: "24x7 Scheduler",
        description:
          "Weekly scheduling matrix to engage alternative throttled rate profiles during peak hours",
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
        title: "Network & VPN Kill Switch",
        shortLabel: "Network & VPN",
        description:
          "Interface binding (tun0/wg0), VPN kill-switch protection, listening port, UPnP, and connection limits",
        icon: "🛡️",
        badge: "KillSwitch",
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
        title: "Proxy & Privacy Routing",
        shortLabel: "Proxy & Privacy",
        description:
          "Route tracker queries and peer transfers via SOCKS5/HTTP proxies with strict privacy enforcement",
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
    title: "Integrations & Servarr",
    shortLabel: "Integrations",
    description:
      "Prowlarr/Torznab indexers, Sonarr/Radarr/Lidarr media managers, external download clients, and UI notifications",
    icon: "🔌",
    pages: [
      {
        id: "indexers",
        groupId: "integrations",
        title: "Torznab & Newznab Indexers",
        shortLabel: "Indexers & Torznab",
        description:
          "Connect Prowlarr, Jackett, and standalone Torznab providers for automated multi-indexer searching",
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
        title: "Servarr (*arr) Media Connections",
        shortLabel: "Servarr (*arr)",
        description:
          "Direct integration with Sonarr, Radarr, Lidarr, and Readarr for download grab & import notifications",
        icon: "📺",
        badge: "Media",
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
        title: "External Client Compatibility Adapters",
        shortLabel: "Client Adapters",
        description:
          "Connect external qBittorrent, Transmission, and Deluge clients to import active torrent state",
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
        title: "Webhooks & Notifications",
        shortLabel: "Webhooks & Alerts",
        description:
          "Configure outbound alerts for Discord, Telegram, Gotify, Generic Webhooks, and lifecycle events",
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
    title: "Advanced & AI Intelligence",
    shortLabel: "Advanced & AI",
    description:
      "Pluggable engine subsystems, local/cloud AI Copilot models, rolling disk logging, and database maintenance",
    icon: "🧠",
    badge: "AI",
    pages: [
      {
        id: "subsystems",
        groupId: "advanced-ai",
        title: "Pluggable Subsystems Matrix",
        shortLabel: "Subsystems",
        description:
          "Zero-downtime hot-swappable providers for BitTorrent engines, archive extractors, media inspectors, and GeoIP",
        icon: "🧩",
        badge: "Modular",
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
        title: "AI Copilot & Natural Intelligence",
        shortLabel: "AI Copilot",
        description:
          "Local ONNX, Ollama LLM sidecars, and Google Gemini API integration for swarm diagnostics and search",
        icon: "✨",
        badge: "Intelligence",
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
        title: "Logging, Diagnostics & Maintenance",
        shortLabel: "Logging & Tools",
        description:
          "Disk log levels (Trace..Error), Debug tracing mode, SQLite database VACUUM, and artwork cache purges",
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
  categories: { groupId: "storage-queues", pageId: "queue" },
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
