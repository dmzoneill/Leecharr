/**
 * Checks if an IP address is a private, loopback, or LAN address.
 */
export function isPrivateIp(ip?: string | null): boolean {
  if (!ip) return false;
  const clean = ip
    .trim()
    .replace(/^\[|\]$/g, "")
    .toLowerCase();
  if (
    clean === "localhost" ||
    clean === "::1" ||
    clean === "0.0.0.0" ||
    clean.startsWith("127.") ||
    clean.startsWith("10.") ||
    clean.startsWith("192.168.") ||
    clean.startsWith("169.254.") ||
    clean.startsWith("fc00:") ||
    clean.startsWith("fd00:") ||
    clean.startsWith("fe80:")
  ) {
    return true;
  }
  // Check 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
  return /^172\.(1[6-9]|2\d|3[0-1])\./.test(clean);
}

/**
 * Returns a flag emoji from an ISO 3166-1 alpha-2 country code
 */
export function getCountryFlag(countryCode?: string | null): string {
  if (!countryCode) return "🌐";
  const trimmed = countryCode.trim().toUpperCase();
  if (trimmed.length !== 2) return "🌐";
  const first = trimmed.charCodeAt(0);
  const second = trimmed.charCodeAt(1);
  if (first < 65 || first > 90 || second < 65 || second > 90) return "🌐";
  try {
    return String.fromCodePoint(first - 65 + 0x1f1e6, second - 65 + 0x1f1e6);
  } catch {
    return "🌐";
  }
}

export interface CountryFlagProps {
  ip?: string | null;
  countryCode?: string | null;
  countryName?: string | null;
  className?: string;
}

export function CountryFlag({
  ip,
  countryCode,
  countryName,
  className,
}: CountryFlagProps) {
  // If IP is loopback or local LAN
  if (isPrivateIp(ip)) {
    return (
      <span
        className={className}
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: "0.25rem",
          fontSize: "0.75rem",
          color: "var(--text-muted)",
        }}
        title="Local / LAN Peer"
      >
        🏠 <span style={{ fontSize: "0.7rem" }}>LAN</span>
      </span>
    );
  }

  const cleanCode = countryCode?.trim().toUpperCase();
  const isValidCode = !!cleanCode && /^[A-Z]{2}$/.test(cleanCode);
  const flag = isValidCode ? getCountryFlag(cleanCode) : "🌐";
  const title =
    countryName?.trim() ||
    (isValidCode ? cleanCode : "") ||
    ip?.trim() ||
    "Peer";

  return (
    <span
      className={className}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "0.25rem",
        fontSize: "0.85rem",
      }}
      title={title}
    >
      <span>{flag}</span>
      {isValidCode && (
        <span
          style={{
            fontSize: "0.68rem",
            fontFamily: "monospace",
            color: "var(--text-muted)",
          }}
        >
          {cleanCode}
        </span>
      )}
    </span>
  );
}

export default CountryFlag;
