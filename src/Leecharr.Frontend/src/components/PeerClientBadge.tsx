import { useTranslation } from "../i18n";

interface PeerClientBadgeProps {
  client: string;
  flags?: string;
  className?: string;
}

interface ClientMeta {
  name: string;
  version?: string;
  badgeClass: string;
  icon: string;
}

const AZUREUS_CLIENTS: Record<
  string,
  { name: string; badgeClass: string; icon: string }
> = {
  qB: { name: "qBittorrent", badgeClass: "badge-primary", icon: "🔵" },
  TR: { name: "Transmission", badgeClass: "badge-danger", icon: "🔴" },
  DE: { name: "Deluge", badgeClass: "badge-success", icon: "🟢" },
  rt: { name: "rTorrent", badgeClass: "badge-warning", icon: "🟣" },
  LT: {
    name: "libtorrent (Rasterbar)",
    badgeClass: "badge-secondary",
    icon: "⚙️",
  },
  lt: { name: "libTorrent", badgeClass: "badge-secondary", icon: "⚙️" },
  UT: { name: "µTorrent", badgeClass: "badge-success", icon: "µ" },
  UM: { name: "µTorrent Mac", badgeClass: "badge-success", icon: "µ" },
  UE: { name: "µTorrent Embedded", badgeClass: "badge-success", icon: "µ" },
  AZ: { name: "Azureus / Vuze", badgeClass: "badge-primary", icon: "🐸" },
  BG: { name: "BiglyBT", badgeClass: "badge-primary", icon: "🐸" },
  SD: { name: "Seedarr", badgeClass: "badge-primary", icon: "🌱" },
  LC: { name: "Leecharr", badgeClass: "badge-primary", icon: "💀" },
  KT: { name: "KTorrent", badgeClass: "badge-info", icon: "🔷" },
  BC: { name: "BitComet", badgeClass: "badge-warning", icon: "☄️" },
  BT: { name: "BitTorrent", badgeClass: "badge-primary", icon: "🌊" },
  WD: { name: "WebTorrent", badgeClass: "badge-info", icon: "🌐" },
  PI: { name: "PicoTorrent", badgeClass: "badge-secondary", icon: "📦" },
  FD: { name: "Free Download Manager", badgeClass: "badge-info", icon: "📥" },
  AR: { name: "Arctic", badgeClass: "badge-secondary", icon: "❄️" },
  FL: { name: "Folx", badgeClass: "badge-info", icon: "🦊" },
};

function decodeAzureusVersion(vStr: string): string {
  if (vStr.length === 4) {
    const chars = vStr.split("");
    if (/^\d{4}$/.test(vStr)) {
      return `${chars[0]}.${chars[1]}.${chars[2]}.${chars[3]}`;
    }
    return chars.join(".");
  }
  return vStr;
}

export function parsePeerClient(clientStr: string): ClientMeta {
  if (!clientStr || clientStr === "Unknown" || clientStr === "-") {
    return { name: "Unknown", badgeClass: "badge-secondary", icon: "👤" };
  }

  const normalized = clientStr.trim();

  // Check Azureus style: -XXvvvv-
  const azMatch = normalized.match(/^-([A-Za-z0-9~]{2})([0-9A-Za-z]{4})-/);
  if (azMatch) {
    const code = azMatch[1];
    const verRaw = azMatch[2];
    const clientInfo = AZUREUS_CLIENTS[code] || {
      name: `Client (${code})`,
      badgeClass: "badge-secondary",
      icon: "👤",
    };
    const version = decodeAzureusVersion(verRaw);
    return {
      name: clientInfo.name,
      version,
      badgeClass: clientInfo.badgeClass,
      icon: clientInfo.icon,
    };
  }

  const lower = normalized.toLowerCase();

  if (lower.includes("qbittorrent") || lower.startsWith("qb/")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "qBittorrent",
      version,
      badgeClass: "badge-primary",
      icon: "🔵",
    };
  }
  if (lower.includes("transmission") || lower.startsWith("tr/")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "Transmission",
      version,
      badgeClass: "badge-danger",
      icon: "🔴",
    };
  }
  if (lower.includes("deluge") || lower.startsWith("de/")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "Deluge",
      version,
      badgeClass: "badge-success",
      icon: "🟢",
    };
  }
  if (lower.includes("rtorrent") || lower.startsWith("rt/")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "rTorrent",
      version,
      badgeClass: "badge-warning",
      icon: "🟣",
    };
  }
  if (lower.includes("libtorrent") || lower.startsWith("lt/")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "libtorrent",
      version,
      badgeClass: "badge-secondary",
      icon: "⚙️",
    };
  }
  if (lower.includes("utorrent") || lower.startsWith("ut/")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "µTorrent",
      version,
      badgeClass: "badge-success",
      icon: "µ",
    };
  }
  if (lower.includes("biglybt") || lower.includes("azureus")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "BiglyBT",
      version,
      badgeClass: "badge-primary",
      icon: "🐸",
    };
  }
  if (lower.includes("leecharr")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "Leecharr",
      version,
      badgeClass: "badge-primary",
      icon: "💀",
    };
  }
  if (lower.includes("seedarr")) {
    const version = normalized.split(/[/ ]/)[1] || "";
    return {
      name: "Seedarr Seeder",
      version,
      badgeClass: "badge-primary",
      icon: "🌱",
    };
  }

  return {
    name: normalized.length > 18 ? `${normalized.slice(0, 16)}...` : normalized,
    badgeClass: "badge-secondary",
    icon: "🌐",
  };
}

export function PeerClientBadge({
  client,
  flags,
  className,
}: PeerClientBadgeProps) {
  const { t } = useTranslation();
  const meta = parsePeerClient(client);

  const isEncrypted =
    flags &&
    (flags.includes("E") || flags.includes("e") || flags.includes("x"));
  const isUtp = flags && (flags.includes("P") || flags.includes("p"));

  return (
    <div
      className={className}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "0.35rem",
        flexWrap: "nowrap",
      }}
    >
      <span
        className={`badge ${meta.badgeClass}`}
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: "0.25rem",
          fontSize: "0.72rem",
          padding: "0.2rem 0.45rem",
        }}
        title={client}
      >
        <span>{meta.icon}</span>
        <span style={{ fontWeight: 600 }}>{meta.name}</span>
        {meta.version && (
          <span style={{ opacity: 0.8, fontSize: "0.68rem" }}>
            {meta.version}
          </span>
        )}
      </span>

      {isEncrypted && (
        <span
          className="badge badge-secondary"
          style={{
            fontSize: "0.65rem",
            padding: "0.15rem 0.35rem",
            opacity: 0.85,
          }}
          title={t("torrentDetail.encryptedConn", "Encrypted connection")}
        >
          🔒
        </span>
      )}

      {isUtp && (
        <span
          className="badge badge-secondary"
          style={{
            fontSize: "0.65rem",
            padding: "0.15rem 0.35rem",
            opacity: 0.85,
          }}
          title={t(
            "torrentDetail.utpProtocol",
            "Micro Transport Protocol (uTP)",
          )}
        >
          uTP
        </span>
      )}
    </div>
  );
}

export default PeerClientBadge;
