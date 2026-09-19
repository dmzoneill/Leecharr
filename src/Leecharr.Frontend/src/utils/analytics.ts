export const GA_MEASUREMENT_ID = "G-86X994W6SL";

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: (...args: unknown[]) => void;
  }
}

/**
 * Sends a page view event to Google Analytics 4.
 */
export function trackPageView(pagePath: string, pageTitle?: string): void {
  if (typeof window !== "undefined" && typeof window.gtag === "function") {
    window.gtag("event", "page_view", {
      page_path: pagePath,
      page_title:
        pageTitle || (typeof document !== "undefined" ? document.title : ""),
      send_to: GA_MEASUREMENT_ID,
    });
  }
}

/**
 * Sends a custom event to Google Analytics 4.
 */
export function trackEvent(
  eventName: string,
  eventParams?: Record<string, string | number | boolean | undefined | null>,
): void {
  if (typeof window !== "undefined" && typeof window.gtag === "function") {
    window.gtag("event", eventName, {
      ...eventParams,
      send_to: GA_MEASUREMENT_ID,
    });
  }
}

/**
 * Helper to track adding a torrent.
 */
export function trackTorrentAdd(
  mode: "file" | "magnet" | "search" | "create",
  count: number = 1,
  category?: string,
): void {
  trackEvent("torrent_add", {
    add_mode: mode,
    torrent_count: count,
    category: category || "default",
  });
}

/**
 * Helper to track lifecycle actions on a torrent (pause, resume, delete).
 */
export function trackTorrentAction(
  action: "pause" | "resume" | "delete",
  torrentId: number,
  deleteFiles?: boolean,
): void {
  trackEvent(`torrent_${action}`, {
    torrent_id: torrentId,
    delete_files: deleteFiles ?? false,
  });
}

/**
 * Helper to track indexer search queries.
 */
export function trackIndexerSearch(query: string, resultCount?: number): void {
  trackEvent("indexer_search", {
    search_term: query,
    result_count: resultCount,
  });
}

/**
 * Helper to track grabbing a release from indexer search results.
 */
export function trackReleaseGrab(title: string, indexerName?: string): void {
  trackEvent("release_grab", {
    release_title: title,
    indexer: indexerName || "unknown",
  });
}

/**
 * Helper to track theme toggle.
 */
export function trackThemeChange(theme: "light" | "dark"): void {
  trackEvent("theme_change", {
    theme,
  });
}

/**
 * Helper to track UI language change.
 */
export function trackLanguageChange(language: string): void {
  trackEvent("language_change", {
    language,
  });
}

/**
 * Helper to track modal openings.
 */
export function trackModalOpen(modalName: string): void {
  trackEvent("modal_open", {
    modal_name: modalName,
  });
}
