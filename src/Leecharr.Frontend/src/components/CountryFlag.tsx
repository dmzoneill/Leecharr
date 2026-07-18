/**
 * Returns a flag emoji from an ISO 3166-1 alpha-2 country code
 */
export function getCountryFlag(countryCode?: string): string {
  if (!countryCode || countryCode.length !== 2) return "🌐";
  const code = countryCode.toUpperCase();
  const first = code.charCodeAt(0) - 65 + 0x1f1e6;
  const second = code.charCodeAt(1) - 65 + 0x1f1e6;
  try {
    return String.fromCodePoint(first, second);
  } catch {
    return "🌐";
  }
}

interface CountryFlagProps {
  ip?: string;
  countryCode?: string;
  countryName?: string;
  className?: string;
}

export function CountryFlag({
  ip,
  countryCode,
  countryName,
  className,
}: CountryFlagProps) {
  // If IP is loopback or local LAN
  if (
    ip &&
    (ip.startsWith("127.") ||
      ip.startsWith("192.168.") ||
      ip.startsWith("10.") ||
      ip.startsWith("172.16.") ||
      ip === "::1" ||
      ip === "localhost")
  ) {
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

  const flag = getCountryFlag(countryCode);

  return (
    <span
      className={className}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "0.25rem",
        fontSize: "0.85rem",
      }}
      title={countryName || countryCode || ip || "Peer"}
    >
      <span>{flag}</span>
      {countryCode && (
        <span
          style={{
            fontSize: "0.68rem",
            fontFamily: "monospace",
            color: "var(--text-muted)",
          }}
        >
          {countryCode.toUpperCase()}
        </span>
      )}
    </span>
  );
}

export default CountryFlag;
