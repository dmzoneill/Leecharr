import type { SubtitleTrack } from "../api/types";

export const PLAYABLE_EXTENSIONS = [
  ".mp4",
  ".mkv",
  ".webm",
  ".avi",
  ".mov",
  ".m4v",
  ".ts",
  ".m2ts",
  ".mp3",
  ".flac",
  ".aac",
  ".ogg",
  ".oga",
  ".opus",
  ".wav",
  ".m4a",
] as const;

export const AUDIO_EXTENSIONS = [
  ".mp3",
  ".flac",
  ".aac",
  ".ogg",
  ".oga",
  ".opus",
  ".wav",
  ".m4a",
] as const;

export function isPlayableFile(filename?: string | null): boolean {
  if (!filename) return false;
  const lower = filename.toLowerCase().trim();
  return PLAYABLE_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

export function isAudioFile(filename?: string | null): boolean {
  if (!filename) return false;
  const lower = filename.toLowerCase().trim();
  return AUDIO_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

export interface MediaBadges {
  resolution?: string;
  videoCodec?: string;
  audioCodec?: string;
  container?: string;
  badges: string[];
}

export function parseMediaBadges(
  filename?: string | null,
  torrentName?: string | null,
): MediaBadges {
  const combined = `${torrentName ?? ""} ${filename ?? ""}`.trim();
  const badges: string[] = [];

  // Resolution detection
  let resolution: string | undefined;
  if (/\b(2160p|4k|uhd)\b/i.test(combined)) {
    resolution = "4K / 2160p";
    badges.push("4K");
  } else if (/\b(1080p|1080i|fhd)\b/i.test(combined)) {
    resolution = "1080p";
    badges.push("1080p");
  } else if (/\b(720p|hd)\b/i.test(combined)) {
    resolution = "720p";
    badges.push("720p");
  } else if (/\b(576p|480p|sd)\b/i.test(combined)) {
    resolution = "SD";
    badges.push("SD");
  }

  // Video Codec detection
  let videoCodec: string | undefined;
  if (/\b(hevc|h\.?265|x265)\b/i.test(combined)) {
    videoCodec = "HEVC / H.265";
    badges.push("HEVC");
  } else if (/\b(avc|h\.?264|x264)\b/i.test(combined)) {
    videoCodec = "AVC / H.264";
    badges.push("H.264");
  } else if (/\b(av1)\b/i.test(combined)) {
    videoCodec = "AV1";
    badges.push("AV1");
  } else if (/\b(vp9)\b/i.test(combined)) {
    videoCodec = "VP9";
    badges.push("VP9");
  } else if (/\b(xvid|divx)\b/i.test(combined)) {
    videoCodec = "XviD";
    badges.push("XviD");
  }

  // Audio Codec detection
  let audioCodec: string | undefined;
  if (/\b(truehd|atmos)\b/i.test(combined)) {
    audioCodec = "Dolby TrueHD / Atmos";
    badges.push("TrueHD");
  } else if (/\b(dts-hd|dts-x|dts)\b/i.test(combined)) {
    audioCodec = "DTS / DTS-HD";
    badges.push("DTS");
  } else if (/\b(eac3|ddp|dd\+|dolby\s*digital\s*plus)\b/i.test(combined)) {
    audioCodec = "E-AC3 / DDP";
    badges.push("E-AC3");
  } else if (/\b(ac3|dd5\.1|dd2\.0|dolby\s*digital)\b/i.test(combined)) {
    audioCodec = "AC3 / Dolby Digital";
    badges.push("AC3");
  } else if (/\b(flac)\b/i.test(combined)) {
    audioCodec = "FLAC";
    badges.push("FLAC");
  } else if (/\b(aac)\b/i.test(combined)) {
    audioCodec = "AAC";
    badges.push("AAC");
  } else if (/\b(opus)\b/i.test(combined)) {
    audioCodec = "Opus";
    badges.push("Opus");
  } else if (/\b(mp3)\b/i.test(combined)) {
    audioCodec = "MP3";
    badges.push("MP3");
  }

  // Container extension
  let container: string | undefined;
  if (filename) {
    const extMatch = filename.match(/\.([a-z0-9]+)$/i);
    if (extMatch) {
      container = extMatch[1].toUpperCase();
      if (!badges.includes(container)) {
        badges.push(container);
      }
    }
  }

  return {
    resolution,
    videoCodec,
    audioCodec,
    container,
    badges,
  };
}

export function buildStreamUrl(torrentId: number, fileId: number): string {
  return `/api/v1/torrent/${torrentId}/files/${fileId}/stream`;
}

export function buildDownloadUrl(torrentId: number, fileId: number): string {
  return `/api/v1/torrent/${torrentId}/files/${fileId}/download`;
}

export function getAbsoluteUrl(url: string, origin?: string): string {
  if (/^https?:\/\//i.test(url)) {
    return url;
  }
  const base =
    origin ??
    (typeof window !== "undefined" && window.location?.origin
      ? window.location.origin
      : "http://localhost:5000");
  const cleanedBase = base.endsWith("/") ? base.slice(0, -1) : base;
  const cleanedPath = url.startsWith("/") ? url : `/${url}`;
  return `${cleanedBase}${cleanedPath}`;
}

export function buildExternalPlayerUrl(
  player: "vlc" | "mpv",
  streamUrl: string,
  origin?: string,
): string {
  const absoluteStreamUrl = getAbsoluteUrl(streamUrl, origin);
  if (player === "vlc") {
    return `vlc://${absoluteStreamUrl}`;
  }
  return `web+mpv://${absoluteStreamUrl}`;
}

export function cleanUpMediaElement(
  mediaElement: HTMLMediaElement | null | undefined,
): void {
  if (!mediaElement) return;
  try {
    mediaElement.pause();
    mediaElement.removeAttribute("src");
    // Also remove any child <source> elements if present
    while (mediaElement.firstChild) {
      mediaElement.removeChild(mediaElement.firstChild);
    }
    mediaElement.load();
  } catch {
    // Ignore any DOM/media abort exceptions
  }
}

export function setSubtitleTrackActive(
  textTracks: TextTrackList | null | undefined,
  activeTrackId: number | "off" | null,
  tracks?: SubtitleTrack[],
): void {
  if (!textTracks || textTracks.length === 0) return;

  const isOff = activeTrackId === "off" || activeTrackId === null;

  for (let i = 0; i < textTracks.length; i++) {
    const track = textTracks[i];
    if (isOff) {
      track.mode = "disabled";
      continue;
    }

    // Match by track label or language or index
    let matched = false;
    if (tracks && typeof activeTrackId === "number") {
      const targetSub = tracks.find((s) => s.trackId === activeTrackId);
      if (targetSub) {
        const expectedLabel = targetSub.title || targetSub.language;
        if (
          track.label === expectedLabel ||
          track.language === targetSub.twoLetterCode ||
          track.language === targetSub.language
        ) {
          matched = true;
        }
      }
    }

    // Fallback match by index if label match did not hit
    if (!matched && typeof activeTrackId === "number" && activeTrackId === i) {
      matched = true;
    }

    track.mode = matched ? "showing" : "disabled";
  }
}

export function getCodecErrorMessage(
  errorCode?: number,
  fileName?: string,
): string {
  const lower = (fileName ?? "").toLowerCase();
  const isHevc =
    lower.includes("hevc") || lower.includes("x265") || lower.includes("h.265");
  const isAc3 =
    lower.includes("ac3") || lower.includes("dts") || lower.includes("eac3");
  const isMkv = lower.endsWith(".mkv");

  const hints: string[] = [];
  if (isHevc) hints.push("HEVC / H.265 video");
  if (isAc3) hints.push("AC3 / DTS surround audio");
  if (isMkv) hints.push("Matroska (.mkv) container");

  const details = hints.length > 0 ? ` (${hints.join(", ")})` : "";

  if (errorCode === 4) {
    return `Your browser cannot decode this media codec${details}. Most browsers (especially on Linux) lack native system support for proprietary HEVC or AC3/DTS audio codecs.`;
  }
  if (errorCode === 3) {
    return `A media decoding error occurred while playing this stream${details}. The browser media pipeline was unable to render the video or audio packets.`;
  }
  return `Playback error encountered${details}. The browser cannot play this media stream.`;
}
