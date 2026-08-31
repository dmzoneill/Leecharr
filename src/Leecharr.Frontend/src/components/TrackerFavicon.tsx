import React, { useState } from "react";

/**
 * Extracts the primary / apex domain from a tracker URL or host.
 * e.g. "udp://tracker.opentrackr.org:1337/announce" -> "opentrackr.org"
 */
export function getTrackerApexDomain(urlOrHost: string): string {
  if (!urlOrHost) return "";
  let host = urlOrHost.trim().toLowerCase();

  // Remove protocol
  if (host.includes("://")) {
    host = host.split("://")[1];
  }
  // Remove path
  if (host.includes("/")) {
    host = host.split("/")[0];
  }
  // Remove port
  if (host.includes(":")) {
    host = host.split(":")[0];
  }

  // Handle pure IPv4 addresses
  if (/^(\d{1,3}\.){3}\d{1,3}$/.test(host)) {
    return host;
  }

  // Extract root apex domain
  const parts = host.split(".");
  if (parts.length > 2) {
    const twoPartTlds = [
      "co.uk",
      "com.au",
      "eu.org",
      "org.uk",
      "net.au",
      "co.nz",
      "co.jp",
      "net.ru",
      "org.ru",
    ];
    const lastTwo = parts.slice(-2).join(".");
    if (twoPartTlds.includes(lastTwo) && parts.length > 3) {
      return parts.slice(-3).join(".");
    }
    return parts.slice(-2).join(".");
  }

  return host;
}

export interface TrackerFaviconProps {
  urlOrHost: string;
  size?: number;
  className?: string;
  style?: React.CSSProperties;
}

export function TrackerFavicon({
  urlOrHost,
  size = 16,
  className,
  style,
}: TrackerFaviconProps) {
  const [error, setError] = useState(false);
  const domain = getTrackerApexDomain(urlOrHost);

  if (!domain || error) {
    return (
      <span
        style={{
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          width: size,
          height: size,
          fontSize: `${size * 0.75}px`,
          borderRadius: "3px",
          backgroundColor: "rgba(255, 255, 255, 0.08)",
          flexShrink: 0,
          ...style,
        }}
        className={className}
        title={domain || urlOrHost}
      >
        📡
      </span>
    );
  }

  const faviconUrl = `https://t1.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://${domain}&size=32`;

  return (
    <img
      src={faviconUrl}
      alt={domain}
      width={size}
      height={size}
      className={className}
      style={{
        width: size,
        height: size,
        borderRadius: "3px",
        objectFit: "contain",
        display: "inline-block",
        verticalAlign: "middle",
        flexShrink: 0,
        backgroundColor: "rgba(255, 255, 255, 0.04)",
        ...style,
      }}
      onError={() => setError(true)}
      loading="lazy"
    />
  );
}

export default TrackerFavicon;
