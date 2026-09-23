import type { Torrent } from "../api/types";
import { extractTrackerDomain } from "./formatters";

export interface TorrentFilterCriteria {
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  categoryFilter?: string;
  privacyFilter?: string;
  tagFilter?: string;
  selectedTagIds?: number[] | Set<number>;
  tagMatchMode?: "AND" | "OR";
  untaggedOnly?: boolean;
}

/**
 * Filters a list of torrents based on search query, state, tracker, privacy, category, and tag filters.
 */
export function filterTorrents(
  torrents: Torrent[] | undefined,
  criteria: TorrentFilterCriteria,
): Torrent[] {
  const {
    filter,
    stateFilter,
    trackerFilter,
    categoryFilter,
    privacyFilter,
    tagFilter,
    selectedTagIds,
    tagMatchMode,
    untaggedOnly,
  } = criteria;

  const hasSelectedTagIds =
    selectedTagIds != null &&
    (Array.isArray(selectedTagIds)
      ? selectedTagIds.length > 0
      : selectedTagIds.size > 0);

  return (torrents ?? []).filter((t) => {
    if (filter) {
      const q = filter.toLowerCase();
      const matchName = (t.name || "").toLowerCase().includes(q);
      const matchMedia = ((t as any).mediaTitle || "")
        .toLowerCase()
        .includes(q);
      if (!matchName && !matchMedia) return false;
    }

    if (stateFilter && stateFilter !== "All") {
      const st = (t.status || "").toLowerCase();
      const target = stateFilter.toLowerCase();
      if (target === "stopped" || target === "paused") {
        if (st !== "paused" && st !== "stopped" && st !== "idle") return false;
      } else if (st !== target) {
        return false;
      }
    }

    if (trackerFilter && trackerFilter !== "All") {
      const urls =
        t.trackers && t.trackers.length > 0
          ? t.trackers
          : t.trackerUrl
            ? [t.trackerUrl]
            : [];
      const hasTracker = urls.some(
        (u) => extractTrackerDomain(u) === trackerFilter,
      );
      if (!hasTracker) return false;
    }

    if (privacyFilter && privacyFilter !== "All") {
      if (privacyFilter === "Private" && !t.isPrivate) return false;
      if (privacyFilter === "Public" && t.isPrivate) return false;
    }

    if (categoryFilter && categoryFilter !== "All") {
      const cat = t.category?.trim() || "Uncategorized";
      if (cat !== categoryFilter) return false;
    }

    if (hasSelectedTagIds) {
      const tagIdsArray = Array.isArray(selectedTagIds)
        ? selectedTagIds
        : Array.from(selectedTagIds);
      const tTags = t.tagIds ?? [];
      const mode = tagMatchMode ?? "OR";
      if (mode === "AND") {
        const matches = tagIdsArray.every((id) => tTags.includes(id));
        if (!matches) return false;
      } else {
        const matches = tagIdsArray.some((id) => tTags.includes(id));
        if (!matches) return false;
      }
    } else if (untaggedOnly) {
      const isUntagged =
        (t.tagIds == null || t.tagIds.length === 0) &&
        (!t.label || t.label.trim() === "" || t.label.trim() === "Untagged");
      if (!isUntagged) return false;
    } else if (tagFilter && tagFilter !== "All") {
      if (tagFilter === "Untagged") {
        const isUntagged =
          (t.tagIds == null || t.tagIds.length === 0) &&
          (!t.label || t.label.trim() === "" || t.label.trim() === "Untagged");
        if (!isUntagged) return false;
      } else {
        const tag = t.label?.trim() || "Untagged";
        if (tag !== tagFilter) return false;
      }
    }

    return true;
  });
}
