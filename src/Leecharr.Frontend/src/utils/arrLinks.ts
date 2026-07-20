import type {
  ArrConnection,
  IndexerDefinition,
  DownloadHistoryEntry,
  MediaMetadata,
} from "../api/types";

export function getArrInstanceUrl(
  source: string | null | undefined,
  connections: ArrConnection[] | undefined,
): string | null {
  if (!source || !connections) return null;
  const cleanedSource = source.toLowerCase();

  const match = connections.find(
    (c) =>
      c.enable &&
      (c.arrType?.toLowerCase() === cleanedSource ||
        c.name?.toLowerCase() === cleanedSource ||
        cleanedSource.includes(c.arrType?.toLowerCase() ?? "") ||
        cleanedSource.includes(c.name?.toLowerCase() ?? "")),
  );

  return match?.url ? match.url.replace(/\/+$/, "") : null;
}

export function getMediaDeepLink(
  item:
    | DownloadHistoryEntry
    | {
        source?: string | null;
        metadata?: MediaMetadata | null;
        title?: string;
      },
  connections: ArrConnection[] | undefined,
): { url: string; label: string; appName: string } | null {
  const instanceUrl = getArrInstanceUrl(item.source, connections);
  if (!instanceUrl) return null;

  const meta = item.metadata;
  const mediaId = meta?.mediaId;
  const mediaType = (meta?.mediaType || item.source || "").toLowerCase();

  if (mediaType.includes("sonarr") || mediaType === "series") {
    return {
      url: mediaId
        ? `${instanceUrl}/series/${mediaId}`
        : `${instanceUrl}/activity/history`,
      label: "Open in Sonarr",
      appName: "Sonarr",
    };
  }

  if (mediaType.includes("radarr") || mediaType === "movie") {
    return {
      url: mediaId
        ? `${instanceUrl}/movie/${mediaId}`
        : `${instanceUrl}/activity/history`,
      label: "Open in Radarr",
      appName: "Radarr",
    };
  }

  if (
    mediaType.includes("lidarr") ||
    mediaType === "album" ||
    mediaType === "artist"
  ) {
    return {
      url: mediaId
        ? `${instanceUrl}/album/${mediaId}`
        : `${instanceUrl}/activity/history`,
      label: "Open in Lidarr",
      appName: "Lidarr",
    };
  }

  return {
    url: instanceUrl,
    label: `Open in ${item.source}`,
    appName: item.source || "Arr",
  };
}

export function getProwlarrUrl(
  indexers: IndexerDefinition[] | undefined,
  query?: string,
): string | null {
  if (!indexers) return null;
  const prowlarr = indexers.find(
    (i) =>
      i.enable &&
      (i.indexerType?.toLowerCase() === "prowlarr" ||
        i.name?.toLowerCase().includes("prowlarr")),
  );
  if (!prowlarr?.url) return null;

  const base = prowlarr.url.replace(/\/+$/, "");
  return query ? `${base}/search?query=${encodeURIComponent(query)}` : base;
}

export function getImdbUrl(
  imdbId?: string | null,
  fallbackTitle?: string | null,
): string {
  if (imdbId) {
    const cleanId = imdbId.startsWith("tt") ? imdbId : `tt${imdbId}`;
    return `https://www.imdb.com/title/${cleanId}/`;
  }
  return `https://www.imdb.com/find?q=${encodeURIComponent(fallbackTitle || "")}&s=tt`;
}

export function getTmdbUrl(
  tmdbId?: number | null,
  mediaType?: string | null,
): string | null {
  if (!tmdbId) return null;
  const type = mediaType === "movie" ? "movie" : "tv";
  return `https://www.themoviedb.org/${type}/${tmdbId}`;
}

export function getTvdbUrl(tvdbId?: number | null): string | null {
  if (!tvdbId) return null;
  return `https://thetvdb.com/dereferrer/series/${tvdbId}`;
}

export function getActorSearchUrl(actorName: string): string {
  return `https://www.themoviedb.org/search/person?query=${encodeURIComponent(actorName)}`;
}
