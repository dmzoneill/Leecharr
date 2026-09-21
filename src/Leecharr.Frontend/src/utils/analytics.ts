export const GA_MEASUREMENT_ID = "G-86X994W6SL";
export const STORAGE_KEY_INSTANCE_UUID = "leecharr_instance_uuid";
export const STORAGE_KEY_TELEMETRY_ENABLED = "leecharr_telemetry_enabled";

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: (...args: unknown[]) => void;
    [key: string]: unknown;
  }
}

/**
 * Checks whether anonymous usage telemetry is enabled by user preference.
 * Defaults to true unless explicitly disabled in settings.
 */
export function isTelemetryEnabled(): boolean {
  if (typeof window === "undefined") return false;
  try {
    return localStorage.getItem(STORAGE_KEY_TELEMETRY_ENABLED) !== "false";
  } catch {
    return true;
  }
}

/**
 * Enables or disables telemetry tracking, updating localStorage and Google's opt-out flag.
 */
export function setTelemetryEnabled(enabled: boolean): void {
  if (typeof window === "undefined") return;
  try {
    localStorage.setItem(STORAGE_KEY_TELEMETRY_ENABLED, String(enabled));
  } catch {
    /* ignore localStorage errors */
  }

  // Set standard GA4 opt-out window property
  (window as Record<string, unknown>)[`ga-disable-${GA_MEASUREMENT_ID}`] = !enabled;
}

// On script evaluation, disable GA if user previously opted out
if (typeof window !== "undefined" && !isTelemetryEnabled()) {
  (window as Record<string, unknown>)[`ga-disable-${GA_MEASUREMENT_ID}`] = true;
}

function getStoredInstanceUuid(): string | undefined {
  if (typeof window === "undefined") return undefined;
  try {
    return localStorage.getItem(STORAGE_KEY_INSTANCE_UUID) || undefined;
  } catch {
    return undefined;
  }
}

let currentInstanceUuid: string | undefined = getStoredInstanceUuid();

if (
  currentInstanceUuid &&
  isTelemetryEnabled() &&
  typeof window !== "undefined" &&
  typeof window.gtag === "function"
) {
  window.gtag("set", "user_properties", {
    instance_id: currentInstanceUuid,
  });
  window.gtag("config", GA_MEASUREMENT_ID, {
    user_id: currentInstanceUuid,
    send_page_view: false,
  });
}

/**
 * Sets the installation UUID for Google Analytics 4 tracking.
 * Configures both user_id and instance_id user property, and caches it in localStorage.
 */
export function setAnalyticsInstanceUuid(instanceUuid?: string): void {
  if (!instanceUuid) return;
  currentInstanceUuid = instanceUuid;
  try {
    localStorage.setItem(STORAGE_KEY_INSTANCE_UUID, instanceUuid);
  } catch {
    /* ignore localStorage errors */
  }

  if (!isTelemetryEnabled()) return;

  if (typeof window !== "undefined" && typeof window.gtag === "function") {
    window.gtag("set", "user_properties", {
      instance_id: instanceUuid,
    });
    window.gtag("config", GA_MEASUREMENT_ID, {
      user_id: instanceUuid,
      send_page_view: false,
    });
  }
}

/**
 * Sanitizes page path to remove sensitive query parameters (e.g. auth tokens, apikeys, secrets, search queries)
 */
function sanitizePath(rawPath: string): string {
  try {
    const [pathname, search] = rawPath.split("?");
    if (!search) return pathname;
    const params = new URLSearchParams(search);
    const safeParams = new URLSearchParams();
    // Allowlist non-sensitive UI routing parameters
    for (const key of ["tab", "view", "sortKey", "sortDir", "group", "mode"]) {
      const val = params.get(key);
      if (val) {
        safeParams.set(key, val);
      }
    }
    const safeQuery = safeParams.toString();
    return safeQuery ? `${pathname}?${safeQuery}` : pathname;
  } catch {
    return rawPath.split("?")[0];
  }
}

/**
 * Sends a sanitized page view event to Google Analytics 4.
 */
export function trackPageView(pagePath: string, pageTitle?: string): void {
  if (!isTelemetryEnabled()) return;

  if (typeof window !== "undefined" && typeof window.gtag === "function") {
    window.gtag("event", "page_view", {
      page_path: sanitizePath(pagePath),
      page_title:
        pageTitle || (typeof document !== "undefined" ? document.title : ""),
      send_to: GA_MEASUREMENT_ID,
      ...(currentInstanceUuid
        ? { user_id: currentInstanceUuid, instance_id: currentInstanceUuid }
        : {}),
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
  if (!isTelemetryEnabled()) return;

  if (typeof window !== "undefined" && typeof window.gtag === "function") {
    window.gtag("event", eventName, {
      ...eventParams,
      send_to: GA_MEASUREMENT_ID,
      ...(currentInstanceUuid
        ? { user_id: currentInstanceUuid, instance_id: currentInstanceUuid }
        : {}),
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
 * Helper to track indexer search queries without exposing raw search titles.
 */
export function trackIndexerSearch(
  query: string,
  resultCount?: number,
  category?: string,
): void {
  trackEvent("indexer_search", {
    has_query: Boolean(query && query.trim()),
    term_length: query ? query.length : 0,
    category: category || "all",
    result_count: resultCount,
  });
}

/**
 * Helper to track grabbing a release from indexer search results without exposing raw release filenames.
 */
export function trackReleaseGrab(_title: string, indexerName?: string): void {
  trackEvent("release_grab", {
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

/**
 * Helper to track TrackerBoost optimization actions.
 */
export function trackTrackerBoostAction(
  action: "harvest" | "boost_all" | "scan_all" | "bulk_import",
  count?: number,
): void {
  trackEvent("tracker_boost_action", {
    tb_action: action,
    item_count: count,
  });
}

/**
 * Helper to track engine switches (e.g. Libtorrent, Hadouken, MonoTorrent).
 */
export function trackEngineSwitch(engine: string): void {
  trackEvent("engine_switch", {
    selected_engine: engine,
  });
}

/**
 * Helper to track bulk operations on torrents.
 */
export function trackBulkAction(action: string, count: number): void {
  trackEvent("bulk_action", {
    bulk_action_type: action,
    item_count: count,
  });
}

/**
 * Helper to report caught frontend exceptions to Google Analytics.
 */
export function trackException(errorDescription: string, fatal: boolean = false): void {
  trackEvent("exception", {
    description: (errorDescription || "Unknown error").slice(0, 150),
    fatal,
  });
}

/**
 * Helper to track anonymous settings configuration changes.
 * Helps determine which settings, features, and defaults are most utilized.
 */
export function trackSettingSave(
  category: string,
  options?: Record<string, string | number | boolean | undefined | null>,
): void {
  trackEvent("setting_save", {
    setting_category: category,
    ...options,
  });
}

/**
 * Helper to anonymously report active configuration adoption profile on startup.
 */
export function trackConfigAdoption(
  profile: Record<string, string | number | boolean | undefined | null>,
): void {
  trackEvent("config_adoption", profile);
}

/**
 * Helper to track download client connections and configurations.
 */
export function trackDownloadClientAction(
  clientType: string,
  action: "test" | "add" | "delete",
  success?: boolean,
): void {
  trackEvent("download_client_action", {
    client_type: clientType,
    client_action: action,
    is_success: success ?? true,
  });
}

/**
 * Helper to track quick-settings drawer toggles.
 */
export function trackQuickSettingChange(
  settingName: string,
  value: string | number | boolean,
): void {
  trackEvent("quick_setting_change", {
    setting_name: settingName,
    setting_value: String(value),
  });
}
