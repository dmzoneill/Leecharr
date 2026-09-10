import type {
  DownloadHistoryEntry,
  ArrConnection,
  IndexerDefinition,
} from "../../api/types";

export type HistorySortColumn =
  | "title"
  | "totalSize"
  | "uploaded"
  | "ratio"
  | "seedingTime"
  | "dateAdded"
  | "status";

export type SortDirection = "asc" | "desc";

export function formatDuration(seconds: number): string {
  if (!seconds || seconds <= 0) return "0s";
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);

  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}
