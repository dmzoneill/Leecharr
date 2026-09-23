export interface TorrentFileValidationResult {
  valid: boolean;
  error?: string;
}

export interface ParsedMagnetUri {
  valid: boolean;
  infoHash?: string;
  name?: string;
  trackers: string[];
  isV2?: boolean;
}

/**
 * Pre-flight validation for torrent files.
 * - Extension must be .torrent (case-insensitive)
 * - Size must be > 0 bytes
 * - Size must be <= 10 MB (10 * 1024 * 1024 bytes)
 */
export function validateTorrentFile(file: {
  name: string;
  size: number;
}): TorrentFileValidationResult {
  if (!file || !file.name || !file.name.toLowerCase().endsWith(".torrent")) {
    return { valid: false, error: "File must have a .torrent extension" };
  }
  if (file.size <= 0) {
    return { valid: false, error: "Torrent file cannot be empty (0 bytes)" };
  }
  if (file.size > 10 * 1024 * 1024) {
    return {
      valid: false,
      error: "Torrent file exceeds maximum allowed size of 10 MB",
    };
  }
  return { valid: true };
}

/**
 * Parses a BitTorrent Magnet URI according to BEP 9 and BEP 52.
 * Supports v1 (urn:btih: 40 hex or 32 base32) and v2 (urn:btmh: 64 hex).
 */
export function parseMagnetUri(uri: string): ParsedMagnetUri {
  if (!uri || typeof uri !== "string") {
    return { valid: false, trackers: [] };
  }
  const trimmed = uri.trim();
  if (!trimmed.toLowerCase().startsWith("magnet:?")) {
    return { valid: false, trackers: [] };
  }

  try {
    const rawParams = trimmed.substring(8);
    const params = new URLSearchParams(rawParams);
    const xtList = params.getAll("xt");

    let infoHash: string | undefined;
    let isV2 = false;

    // Check for BEP 52 v2 multihash (urn:btmh: optionally with 1220 sha256 multihash header)
    for (const xt of xtList) {
      const v2Match = xt.match(/^urn:btmh:(?:1220)?([0-9a-fA-F]{64})/i);
      if (v2Match) {
        infoHash = v2Match[1].toLowerCase();
        isV2 = true;
        break;
      }
    }

    // If no v2 found, check for v1 btih (40 hex or 32 base32 characters)
    if (!infoHash) {
      for (const xt of xtList) {
        const cleaned = xt
          .replace(/^urn:btih:/i, "")
          .trim()
          .replace(/=+$/, "");
        if (
          /^[0-9a-fA-F]{40}$/i.test(cleaned) ||
          /^[2-7a-zA-Z]{32}$/i.test(cleaned)
        ) {
          infoHash = cleaned;
          isV2 = false;
          break;
        }
      }
    }

    let name: string | undefined;
    const dn = params.get("dn");
    if (dn) {
      try {
        name = decodeURIComponent(dn.replace(/\+/g, " "));
      } catch {
        name = dn;
      }
    }

    const trackers: string[] = [];
    for (const tr of params.getAll("tr")) {
      if (tr && tr.trim()) {
        try {
          trackers.push(decodeURIComponent(tr.trim()));
        } catch {
          trackers.push(tr.trim());
        }
      }
    }

    return {
      valid: Boolean(infoHash),
      infoHash,
      name,
      trackers,
      isV2,
    };
  } catch {
    return { valid: false, trackers: [] };
  }
}
