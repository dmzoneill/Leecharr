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

export function parsePeerClient(clientStr: string): ClientMeta {
  if (!clientStr || clientStr === "Unknown" || clientStr === "-") {
    return { name: "Unknown", badgeClass: "badge-secondary", icon: "👤" };
  }

  const normalized = clientStr.trim();
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
  const meta = parsePeerClient(client);

  const isEncrypted =
    flags &&
    (flags.includes("E") || flags.includes("e") || flags.includes("x"));
  const isUtp = flags && (flags.includes("U") || flags.includes("u"));

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
          title="Encrypted connection"
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
          title="Micro Transport Protocol (uTP)"
        >
          uTP
        </span>
      )}
    </div>
  );
}

export default PeerClientBadge;
