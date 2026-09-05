import React, { useState, useCallback, useMemo, useRef } from "react";
import { useVirtualizer, type VirtualItem } from "@tanstack/react-virtual";
import { useTorrentStore, applyTelemetry } from "../stores/useTorrentStore";
import {
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useUpdateTorrent,
  useAnnounceTorrent,
  useRecheckTorrent,
  useMoveTorrentQueue,
  useDownloadHistory,
  useArrConnections,
} from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatDate,
  formatSeconds,
  extractTrackerDomain,
} from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";
import { getTorrentBadges } from "../utils/milestones";
import { SkeletonTableRow } from "./Skeleton";
import TorrentContextMenu from "./TorrentContextMenu";
import TrackerFavicon from "./TrackerFavicon";
import { PlayIcon, StopIcon } from "./icons/UIIcons";
import useEscapeKey from "../hooks/useEscapeKey";
import type { Torrent, DownloadHistoryEntry } from "../api/types";

export type ColumnKey =
  | "#"
  | "name"
  | "status"
  | "totalSize"
  | "uploaded"
  | "downloaded"
  | "ratio"
  | "progress"
  | "seeders"
  | "leechers"
  | "trackerUrl"
  | "dateAdded"
  | "lastActive"
  | "pieceCount"
  | "pieceLength"
  | "comment"
  | "createdBy"
  | "creationDate"
  | "isPrivate"
  | "infoHash"
  | "priority"
  | "uploadLimit"
  | "downloadLimit"
  | "initialSeeding"
  | "forceStart"
  | "category"
  | "label"
  | "sequentialDownload"
  | "uploadSpeed"
  | "downloadSpeed"
  | "active"
  | "eta";

export interface ColumnDef {
  key: ColumnKey;
  label: string;
  sortable: boolean;
}

export const ALL_COLUMNS: ColumnDef[] = [
  { key: "#", label: "#", sortable: true },
  { key: "name", label: "Name", sortable: true },
  { key: "category", label: "Category", sortable: true },
  { key: "status", label: "Status", sortable: true },
  { key: "progress", label: "Progress", sortable: true },
  { key: "totalSize", label: "Size", sortable: true },
  { key: "downloaded", label: "Downloaded", sortable: true },
  { key: "uploaded", label: "Uploaded", sortable: true },
  { key: "downloadSpeed", label: "Down Speed", sortable: true },
  { key: "uploadSpeed", label: "Up Speed", sortable: true },
  { key: "ratio", label: "Ratio", sortable: true },
  { key: "seeders", label: "Seeds", sortable: true },
  { key: "leechers", label: "Peers", sortable: true },
  { key: "eta", label: "ETA", sortable: true },
  { key: "trackerUrl", label: "Tracker", sortable: true },
  { key: "priority", label: "Priority", sortable: true },
  { key: "label", label: "Label", sortable: true },
  { key: "uploadLimit", label: "Upload Limit", sortable: true },
  { key: "downloadLimit", label: "Download Limit", sortable: true },
  { key: "initialSeeding", label: "Initial Seeding", sortable: true },
  { key: "sequentialDownload", label: "Sequential", sortable: true },
  { key: "dateAdded", label: "Added", sortable: true },
  { key: "lastActive", label: "Last Active", sortable: true },
  { key: "pieceCount", label: "Pieces", sortable: true },
  { key: "pieceLength", label: "Piece Length", sortable: true },
  { key: "isPrivate", label: "Private Swarm", sortable: true },
  { key: "infoHash", label: "Info Hash", sortable: true },
  { key: "comment", label: "Comment", sortable: true },
  { key: "createdBy", label: "Created By", sortable: true },
];

const PREF_VISIBLE_COLS_STORAGE = "leecharr_cols_v2";

const DEFAULT_VISIBLE: Set<string> = new Set([
  "#",
  "name",
  "category",
  "totalSize",
  "progress",
  "status",
  "downloadSpeed",
  "uploadSpeed",
  "seeders",
  "leechers",
  "ratio",
]);

function loadVisibleColumns(): Set<string> {
  try {
    const stored = localStorage.getItem(PREF_VISIBLE_COLS_STORAGE);
    if (stored) {
      const parsed = JSON.parse(stored) as string[];
      if (Array.isArray(parsed) && parsed.length > 0) return new Set(parsed);
    }
  } catch (err) {
    console.warn("Failed to parse localStorage:", err);
  }
  return new Set(DEFAULT_VISIBLE);
}

function saveVisibleColumns(cols: Set<string>) {
  localStorage.setItem(PREF_VISIBLE_COLS_STORAGE, JSON.stringify([...cols]));
}

const PREF_COL_ORDER_STORAGE = "leecharr_col_order_v1";
const PREF_COL_WIDTHS_STORAGE = "leecharr_col_widths_v1";

function loadColumnOrder(): ColumnKey[] {
  try {
    const stored = localStorage.getItem(PREF_COL_ORDER_STORAGE);
    if (stored) {
      const parsed = JSON.parse(stored) as ColumnKey[];
      if (Array.isArray(parsed) && parsed.length > 0) return parsed;
    }
  } catch {
    /* ignore */
  }
  return ALL_COLUMNS.map((c) => c.key);
}

function saveColumnOrder(order: ColumnKey[]) {
  localStorage.setItem(PREF_COL_ORDER_STORAGE, JSON.stringify(order));
}

function loadColumnWidths(): Record<string, number> {
  try {
    const stored = localStorage.getItem(PREF_COL_WIDTHS_STORAGE);
    if (stored) return JSON.parse(stored) as Record<string, number>;
  } catch {
    /* ignore */
  }
  return {};
}

function saveColumnWidths(widths: Record<string, number>) {
  localStorage.setItem(PREF_COL_WIDTHS_STORAGE, JSON.stringify(widths));
}

interface ContextMenuState {
  x: number;
  y: number;
  torrent: Torrent | null;
}

export interface TorrentTableProps {
  torrents?: Torrent[];
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  privacyFilter?: string;
  selectedId?: number | null;
  selectedTorrentId?: number | null;
  onSelect?: (torrent: Torrent) => void;
  onSelectTorrent?: (id: number | null) => void;
  onPause?: (id: number) => void;
  onResume?: (id: number) => void;
  onDelete?: (payload: { id: number; deleteFiles?: boolean }) => void;
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number) => void;
  onSelectAll?: (ids: number[]) => void;
  onSearchIndexers?: (query: string) => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
}

interface TorrentTableRowProps {
  torrent: Torrent;
  index: number;
  virtualRow?: VirtualItem;
  columns: ColumnDef[];
  isSelected: boolean;
  isChecked: boolean;
  onSelect?: (torrent: Torrent) => void;
  onToggleSelect?: (id: number) => void;
  onContextMenu: (e: React.MouseEvent, torrent: Torrent | null) => void;
  renderCell: (t: Torrent, key: ColumnKey, idx: number) => React.ReactNode;
  measureElement?: (node: HTMLElement | null) => void;
}

const TorrentTableRow = React.memo<TorrentTableRowProps>(
  ({
    torrent: t,
    index: idx,
    virtualRow,
    columns,
    isSelected,
    isChecked,
    onSelect,
    onToggleSelect,
    onContextMenu,
    renderCell,
    measureElement,
  }) => {
    const telemetry = useTorrentStore((state) => state.telemetry[t.id]);
    const mergedTorrent = useMemo(() => applyTelemetry(t, telemetry), [t, telemetry]);
    const rowIndex = virtualRow?.index ?? idx;

    return (
      <tr
        ref={measureElement}
        data-index={virtualRow?.index ?? idx}
        className={`torrent-table-row ${isSelected ? "torrent-table-row-selected" : ""}`}
        onClick={() => onSelect?.(mergedTorrent)}
        onContextMenu={(e) => onContextMenu(e, mergedTorrent)}
        style={{
          cursor: "pointer",
          backgroundColor: isSelected
            ? "var(--bg-card-hover, #23284b)"
            : isChecked
              ? "rgba(255, 209, 102, 0.05)"
              : "transparent",
          borderBottom: "1px solid rgba(255, 255, 255, 0.04)",
          fontSize: "0.82rem",
        }}
      >
        {onToggleSelect && (
          <td
            style={{ textAlign: "center", padding: "0.5rem" }}
            onClick={(e) => e.stopPropagation()}
          >
            <input type="checkbox" checked={isChecked} onChange={() => onToggleSelect(t.id)} />
          </td>
        )}

        {columns.map((c) => (
          <td
            key={c.key}
            style={{
              padding: "0.55rem 0.75rem",
              whiteSpace: "nowrap",
            }}
          >
            {renderCell(mergedTorrent, c.key, rowIndex)}
          </td>
        ))}
      </tr>
    );
  }
);
TorrentTableRow.displayName = "TorrentTableRow";

export const TorrentTable: React.FC<TorrentTableProps> = ({
  torrents: propTorrents,
  filter,
  stateFilter,
  trackerFilter,
  privacyFilter,
  selectedId,
  selectedTorrentId,
  onSelect,
  onSelectTorrent,
  onPause,
  onResume,
  onDelete,
  selectedIds = new Set(),
  onToggleSelect,
  onSelectAll,
  onSearchIndexers,
  onNavigateTab,
}) => {
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const updateTorrent = useUpdateTorrent();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const moveTorrentQueue = useMoveTorrentQueue();

  const telemetry = useTorrentStore((state) => state.telemetry);

  const [sortKey, setSortKey] = useState<ColumnKey>("name");
  const [sortAsc, setSortAsc] = useState(true);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [visibleColumns, setVisibleColumns] = useState<Set<string>>(loadVisibleColumns);
  const [showColumnModal, setShowColumnModal] = useState(false);
  const [columnOrder, setColumnOrder] = useState<ColumnKey[]>(loadColumnOrder);
  const [columnWidths, setColumnWidths] = useState<Record<string, number>>(loadColumnWidths);

  // Drag-to-reorder state
  const dragColRef = useRef<ColumnKey | null>(null);
  const dragOverColRef = useRef<ColumnKey | null>(null);
  const [dragOverKey, setDragOverKey] = useState<ColumnKey | null>(null);

  // Column resize state
  const resizeStateRef = useRef<{
    key: string;
    startX: number;
    startWidth: number;
  } | null>(null);

  useEscapeKey(() => setShowColumnModal(false), showColumnModal);

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const { historyByHash, historyByTitle } = useMemo(() => {
    const byHash = new Map<string, DownloadHistoryEntry>();
    const byTitle = new Map<string, DownloadHistoryEntry>();
    if (history) {
      for (const h of history) {
        if (h.infoHash) {
          byHash.set(h.infoHash.toLowerCase(), h);
        }
        if (h.title) {
          byTitle.set(h.title.toLowerCase(), h);
        }
      }
    }
    return { historyByHash: byHash, historyByTitle: byTitle };
  }, [history]);

  const closeContextMenu = useCallback(() => setContextMenu(null), []);

  const toggleColumn = (key: string) => {
    setVisibleColumns((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        if (next.size > 1) next.delete(key);
      } else {
        next.add(key);
      }
      saveVisibleColumns(next);
      return next;
    });
  };

  // --- Column drag-to-reorder ---
  const handleColDragStart = useCallback((key: ColumnKey) => {
    dragColRef.current = key;
  }, []);

  const handleColDragOver = useCallback((e: React.DragEvent, key: ColumnKey) => {
    e.preventDefault();
    dragOverColRef.current = key;
    setDragOverKey(key);
  }, []);

  const handleColDrop = useCallback((e: React.DragEvent, targetKey: ColumnKey) => {
    e.preventDefault();
    const fromKey = dragColRef.current;
    if (!fromKey || fromKey === targetKey) {
      dragColRef.current = null;
      dragOverColRef.current = null;
      setDragOverKey(null);
      return;
    }
    setColumnOrder((prev) => {
      // Ensure all keys are present (merge any missing from ALL_COLUMNS)
      const allKeys = ALL_COLUMNS.map((c) => c.key);
      const base = [...new Set([...prev, ...allKeys])];
      const fromIdx = base.indexOf(fromKey);
      const toIdx = base.indexOf(targetKey);
      if (fromIdx === -1 || toIdx === -1) return prev;
      const next = [...base];
      next.splice(fromIdx, 1);
      next.splice(toIdx, 0, fromKey);
      saveColumnOrder(next);
      return next;
    });
    dragColRef.current = null;
    dragOverColRef.current = null;
    setDragOverKey(null);
  }, []);

  const handleColDragEnd = useCallback(() => {
    dragColRef.current = null;
    dragOverColRef.current = null;
    setDragOverKey(null);
  }, []);

  // --- Column resize ---
  const handleResizeMouseDown = useCallback(
    (e: React.MouseEvent, key: string, thEl: HTMLElement) => {
      e.preventDefault();
      e.stopPropagation();
      const startWidth = thEl.getBoundingClientRect().width;
      resizeStateRef.current = { key, startX: e.clientX, startWidth };

      const onMouseMove = (ev: MouseEvent) => {
        if (!resizeStateRef.current) return;
        const delta = ev.clientX - resizeStateRef.current.startX;
        const newWidth = Math.max(48, resizeStateRef.current.startWidth + delta);
        setColumnWidths((prev) => ({ ...prev, [resizeStateRef.current!.key]: newWidth }));
      };

      const onMouseUp = () => {
        if (resizeStateRef.current) {
          // Persist final widths
          setColumnWidths((prev) => {
            saveColumnWidths(prev);
            return prev;
          });
          resizeStateRef.current = null;
        }
        document.removeEventListener("mousemove", onMouseMove);
        document.removeEventListener("mouseup", onMouseUp);
      };

      document.addEventListener("mousemove", onMouseMove);
      document.addEventListener("mouseup", onMouseUp);
    },
    []
  );

  const handleContextMenu = (e: React.MouseEvent, torrent: Torrent | null) => {
    e.preventDefault();
    e.stopPropagation();
    setContextMenu({ x: e.clientX, y: e.clientY, torrent });
  };

  const handleSort = (key: ColumnKey) => {
    if (sortKey === key) {
      setSortAsc(!sortAsc);
    } else {
      setSortKey(key);
      setSortAsc(true);
    }
  };

  const sourceTorrents = propTorrents || [];

  const mergedTorrents = useMemo(() => {
    return sourceTorrents.map((t) => applyTelemetry(t, telemetry[t.id]));
  }, [sourceTorrents, telemetry]);

  const filteredTorrents = useMemo(() => {
    return mergedTorrents.filter((t) => {
      if (filter) {
        const q = filter.toLowerCase();
        const matchName = (t.name || "").toLowerCase().includes(q);
        const matchMedia = (t.mediaTitle || "").toLowerCase().includes(q);
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
        const matchesTracker =
          (t.trackers && t.trackers.some((u) => extractTrackerDomain(u) === trackerFilter)) ||
          extractTrackerDomain(t.trackerUrl || "") === trackerFilter;
        if (!matchesTracker) return false;
      }
      if (privacyFilter && privacyFilter !== "All") {
        if (privacyFilter === "Private" && !t.isPrivate) return false;
        if (privacyFilter === "Public" && t.isPrivate) return false;
      }
      return true;
    });
  }, [mergedTorrents, filter, stateFilter, trackerFilter, privacyFilter]);

  const sortedTorrents = useMemo(() => {
    return [...filteredTorrents].sort((a, b) => {
      let valA: any = (a as any)[sortKey];
      let valB: any = (b as any)[sortKey];

      if (sortKey === "#") {
        valA = a.queuePosition && a.queuePosition > 0 ? a.queuePosition : (a.id ?? 0);
        valB = b.queuePosition && b.queuePosition > 0 ? b.queuePosition : (b.id ?? 0);
      } else if (sortKey === "category") {
        valA = a.category ?? a.label ?? "";
        valB = b.category ?? b.label ?? "";
      } else if (sortKey === "eta") {
        valA =
          a.eta ??
          (a.downloadSpeed > 0 ? (a.totalSize * (1 - a.progress)) / a.downloadSpeed : 9999999);
        valB =
          b.eta ??
          (b.downloadSpeed > 0 ? (b.totalSize * (1 - b.progress)) / b.downloadSpeed : 9999999);
      }

      if (valA === valB) return 0;
      if (valA === undefined || valA === null) return 1;
      if (valB === undefined || valB === null) return -1;

      if (typeof valA === "string") {
        return sortAsc
          ? valA.localeCompare(valB, undefined, {
              numeric: true,
              sensitivity: "base",
            })
          : valB.localeCompare(valA, undefined, {
              numeric: true,
              sensitivity: "base",
            });
      }

      return sortAsc ? valA - valB : valB - valA;
    });
  }, [filteredTorrents, sortKey, sortAsc]);

  // Build ordered, visible column list using the user's saved column order.
  const colDefMap = useMemo(() => new Map(ALL_COLUMNS.map((c) => [c.key, c])), []);
  const columns = useMemo(() => {
    const ordered = columnOrder
      .map((k) => colDefMap.get(k))
      .filter((c): c is ColumnDef => c !== undefined && visibleColumns.has(c.key));
    // Append any visible columns not yet in the saved order (e.g. newly added)
    const inOrder = new Set(ordered.map((c) => c.key));
    for (const c of ALL_COLUMNS) {
      if (visibleColumns.has(c.key) && !inOrder.has(c.key)) ordered.push(c);
    }
    return ordered;
  }, [columnOrder, visibleColumns, colDefMap]);

  const allSelected =
    filteredTorrents.length > 0 && filteredTorrents.every((t) => selectedIds.has(t.id));
  const someSelected =
    filteredTorrents.length > 0 &&
    filteredTorrents.some((t) => selectedIds.has(t.id)) &&
    !allSelected;

  // NOTE: Do NOT add an early return here. All hooks (useCallback, useRef, useVirtualizer)
  // must be called unconditionally before any conditional return (Rules of Hooks).
  // The empty-state JSX is stored in a variable and returned after all hooks have run.
  const emptyState =
    filteredTorrents.length === 0 ? (
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          flex: 1,
          minHeight: "50vh",
          height: "100%",
          textAlign: "center",
          padding: "2rem",
          background: "transparent",
          border: "none",
          boxShadow: "none",
        }}
      >
        <div style={{ fontSize: "3.5rem", marginBottom: "1rem", opacity: 0.85 }}>📁</div>
        <h3
          style={{
            color: "var(--text-primary, #f8f4ed)",
            fontSize: "1.25rem",
            fontWeight: 600,
            marginBottom: "0.5rem",
          }}
        >
          No torrent in the queue
        </h3>
        <p
          style={{
            color: "var(--text-secondary, #c7c5d3)",
            fontSize: "0.9rem",
            maxWidth: "400px",
            margin: 0,
          }}
        >
          Add a torrent file, magnet URI, or search indexers to begin downloading.
        </p>
      </div>
    ) : null;

  const renderCell = useCallback(
    (t: Torrent, key: ColumnKey, idx: number) => {
      switch (key) {
        case "#":
          return (
            <span
              style={{
                color: "var(--text-muted, #7e8092)",
                fontSize: "0.75rem",
              }}
            >
              {t.queuePosition && t.queuePosition > 0 ? t.queuePosition : idx + 1}
            </span>
          );

        case "name": {
          const historyMatch =
            (t.infoHash ? historyByHash.get(t.infoHash.toLowerCase()) : undefined) ||
            (t.name ? historyByTitle.get(t.name.toLowerCase()) : undefined);
          const meta = historyMatch?.metadata;
          const arrLink = historyMatch ? getMediaDeepLink(historyMatch, arrConnections) : null;
          const badges = getTorrentBadges(t);
          const posterSrc = t.posterUrl || t.artworkUrl || t.bannerUrl || meta?.posterUrl;

          return (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.6rem",
                minWidth: 200,
                maxWidth: 460,
              }}
            >
              {posterSrc ? (
                <img
                  src={posterSrc}
                  alt=""
                  style={{
                    width: "22px",
                    height: "32px",
                    objectFit: "cover",
                    borderRadius: "3px",
                    flexShrink: 0,
                    boxShadow: "0 1px 3px rgba(0,0,0,0.3)",
                  }}
                  onError={(e) => {
                    (e.currentTarget as HTMLElement).style.display = "none";
                  }}
                />
              ) : (
                <div
                  style={{
                    width: "22px",
                    height: "32px",
                    borderRadius: "3px",
                    backgroundColor: "rgba(255, 255, 255, 0.06)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontSize: "0.75rem",
                    flexShrink: 0,
                    color: "var(--text-muted)",
                  }}
                >
                  🎬
                </div>
              )}

              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "2px",
                  minWidth: 0,
                  overflow: "hidden",
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                  {t.isPrivate && (
                    <span
                      className="badge"
                      title="Private Torrent (BEP 27: Strict Swarm Isolation, DHT/PEX Disabled)"
                      style={{
                        backgroundColor: "rgba(239, 68, 68, 0.2)",
                        color: "#f87171",
                        border: "1px solid rgba(239, 68, 68, 0.4)",
                        fontSize: "0.65rem",
                        padding: "0.05rem 0.35rem",
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "3px",
                        flexShrink: 0,
                      }}
                    >
                      <i className="fas fa-lock" style={{ fontSize: "0.6rem" }} /> Private
                    </span>
                  )}
                  <span
                    style={{
                      fontWeight: 600,
                      color: "var(--text-primary, #f8f4ed)",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                    title={t.name}
                  >
                    {meta?.title || t.mediaTitle || t.name} {meta?.year ? `(${meta.year})` : ""}
                  </span>
                  {arrLink && (
                    <a
                      href={arrLink.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      onClick={(e) => e.stopPropagation()}
                      style={{
                        fontSize: "0.68rem",
                        padding: "0.1rem 0.35rem",
                        borderRadius: "3px",
                        backgroundColor: "rgba(255, 209, 102, 0.15)",
                        color: "var(--accent, #ffd166)",
                        textDecoration: "none",
                        fontWeight: 600,
                        flexShrink: 0,
                      }}
                    >
                      {arrLink.label} ↗
                    </a>
                  )}
                </div>

                {t.mediaTitle && t.mediaTitle !== t.name && (
                  <span
                    style={{
                      fontSize: "0.72rem",
                      color: "var(--text-muted, #7e8092)",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                    title={t.name}
                  >
                    {t.name}
                  </span>
                )}

                {badges.length > 0 && (
                  <div
                    style={{
                      display: "flex",
                      gap: "4px",
                      flexWrap: "wrap",
                      marginTop: "2px",
                    }}
                  >
                    {badges.slice(0, 3).map((b, i) => (
                      <span
                        key={i}
                        className="badge"
                        title={b.title}
                        style={{
                          fontSize: "0.65rem",
                          padding: "0.05rem 0.3rem",
                          backgroundColor: `${b.color}22`,
                          color: b.color,
                          border: `1px solid ${b.color}44`,
                        }}
                      >
                        {b.icon} {b.label}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </div>
          );
        }

        case "category": {
          const cat = t.category || t.label || "NONE";
          return (
            <span
              className="badge"
              style={{
                fontSize: "0.72rem",
                padding: "0.15rem 0.45rem",
                backgroundColor: "rgba(255, 255, 255, 0.06)",
                color: "var(--text-secondary, #c7c5d3)",
                fontWeight: 600,
                textTransform: "uppercase",
              }}
            >
              {cat}
            </span>
          );
        }

        case "status": {
          const st = (t.status || "idle").toLowerCase();
          let color = "var(--text-muted, #7e8092)";
          let bg = "rgba(126, 128, 146, 0.15)";

          if (st === "downloading") {
            color = "var(--accent, #ffd166)";
            bg = "rgba(255, 209, 102, 0.15)";
          } else if (st === "seeding" || st === "completed") {
            color = "var(--success, #22c55e)";
            bg = "rgba(34, 197, 94, 0.15)";
          } else if (st === "checking" || st === "queued") {
            color = "var(--info, #38bdf8)";
            bg = "rgba(56, 189, 248, 0.15)";
          }

          return (
            <span
              className="badge"
              style={{
                backgroundColor: bg,
                color: color,
                fontWeight: 600,
                fontSize: "0.72rem",
                padding: "0.15rem 0.5rem",
                textTransform: "capitalize",
              }}
            >
              {t.status || "Idle"}
            </span>
          );
        }

        case "progress": {
          const pct = Math.floor((t.progress || 0) * 100);
          return (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "8px",
                width: 120,
              }}
            >
              <div
                style={{
                  flex: 1,
                  height: 6,
                  backgroundColor: "rgba(255, 255, 255, 0.1)",
                  borderRadius: 3,
                  overflow: "hidden",
                }}
              >
                <div
                  style={{
                    width: `${pct}%`,
                    height: "100%",
                    backgroundColor:
                      pct >= 100 ? "var(--success, #22c55e)" : "var(--accent, #ffd166)",
                    transition: "width 0.3s",
                  }}
                />
              </div>
              <span
                style={{
                  fontSize: "0.75rem",
                  fontWeight: 600,
                  width: 34,
                  textAlign: "right",
                }}
              >
                {pct}%
              </span>
            </div>
          );
        }

        case "totalSize":
          return <span>{formatBytes(t.totalSize)}</span>;

        case "downloaded":
          return <span>{formatBytes(t.downloaded ?? t.totalSize * t.progress)}</span>;

        case "uploaded":
          return <span>{formatBytes(t.uploaded ?? 0)}</span>;

        case "downloadSpeed":
          return (
            <span
              style={{
                color:
                  t.downloadSpeed > 0 ? "var(--success, #22c55e)" : "var(--text-muted, #7e8092)",
                fontWeight: t.downloadSpeed > 0 ? 600 : 400,
              }}
            >
              {formatSpeed(t.downloadSpeed)}
            </span>
          );

        case "uploadSpeed":
          return (
            <span
              style={{
                color: t.uploadSpeed > 0 ? "var(--accent, #ffd166)" : "var(--text-muted, #7e8092)",
                fontWeight: t.uploadSpeed > 0 ? 600 : 400,
              }}
            >
              {formatSpeed(t.uploadSpeed)}
            </span>
          );

        case "ratio":
          return (
            <span
              style={{
                fontWeight: 600,
                color:
                  (t.ratio || 0) >= 1.0
                    ? "var(--success, #22c55e)"
                    : "var(--text-primary, #f8f4ed)",
              }}
            >
              {formatRatio(t.ratio || 0)}
            </span>
          );

        case "seeders":
          return (
            <span style={{ color: "var(--success, #22c55e)", fontWeight: 600 }}>
              {t.seeders ?? 0}
            </span>
          );

        case "leechers":
          return <span>{t.leechers ?? 0}</span>;

        case "eta": {
          if (t.progress >= 1.0) {
            return <span>Done</span>;
          }
          const etaSec =
            t.eta && t.eta > 0
              ? t.eta
              : t.downloadSpeed > 0
              ? Math.floor((t.totalSize * (1 - (t.progress || 0))) / t.downloadSpeed)
              : 0;
          return (
            <span>{etaSec > 0 ? formatSeconds(etaSec) : "∞"}</span>
          );
        }

        case "trackerUrl": {
          const domain = extractTrackerDomain(t.trackerUrl);
          return (
            <div
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
              }}
            >
              <TrackerFavicon urlOrHost={domain} size={14} />
              <span>{domain || "-"}</span>
            </div>
          );
        }

        case "priority":
          return (
            <span className="badge" style={{ fontSize: "0.7rem" }}>
              {t.priority === 2 ? "High" : t.priority === 0 ? "Low" : "Normal"}
            </span>
          );

        case "dateAdded":
          return <span>{t.dateAdded ? formatDate(t.dateAdded) : "-"}</span>;

        case "pieceCount":
          return <span>{t.pieceCount ?? "-"}</span>;

        case "pieceLength":
          return <span>{t.pieceLength ? formatBytes(t.pieceLength) : "-"}</span>;

        case "infoHash":
          return (
            <span style={{ fontFamily: "monospace", fontSize: "0.72rem" }}>
              {t.infoHash?.substring(0, 10)}...
            </span>
          );

        case "isPrivate":
          return t.isPrivate ? (
            <span
              className="badge"
              style={{
                backgroundColor: "rgba(239, 68, 68, 0.2)",
                color: "#f87171",
                border: "1px solid rgba(239, 68, 68, 0.4)",
                fontSize: "0.7rem",
                display: "inline-flex",
                alignItems: "center",
                gap: "4px",
              }}
              title="Private Torrent (BEP 27: Strict Swarm Isolation)"
            >
              <i className="fas fa-lock" style={{ fontSize: "0.65rem" }} /> Private
            </span>
          ) : (
            <span
              className="badge"
              style={{
                backgroundColor: "rgba(59, 130, 246, 0.15)",
                color: "#60a5fa",
                fontSize: "0.7rem",
                display: "inline-flex",
                alignItems: "center",
                gap: "4px",
              }}
              title="Public Swarm (DHT/PEX/LPD enabled)"
            >
              <i className="fas fa-globe" style={{ fontSize: "0.65rem" }} /> Public
            </span>
          );

        default:
          return <span>{String((t as any)[key] ?? "-")}</span>;
      }
    },
    [
      historyByHash,
      historyByTitle,
      arrConnections,
      startSeeding,
      stopSeeding,
      deleteTorrent,
      updateTorrent,
      announceTorrent,
      recheckTorrent,
      moveTorrentQueue,
      onPause,
      onResume,
      onSearchIndexers,
      onNavigateTab,
    ]
  );

  const tableContainerRef = useRef<HTMLDivElement>(null);

  const rowVirtualizer = useVirtualizer({
    count: sortedTorrents.length,
    getScrollElement: () => tableContainerRef.current,
    estimateSize: () => 44,
    overscan: 10,
    measureElement: (element) => element?.getBoundingClientRect().height,
  });

  const virtualRows = rowVirtualizer.getVirtualItems();
  const totalHeight = rowVirtualizer.getTotalSize();
  const paddingTop = virtualRows.length > 0 ? virtualRows[0].start : 0;
  const paddingBottom =
    virtualRows.length > 0 ? totalHeight - virtualRows[virtualRows.length - 1].end : 0;
  const totalCols = columns.length + (onToggleSelect ? 1 : 0);

  // All hooks have been called above — safe to return early now.
  if (emptyState) {
    return emptyState;
  }

  return (
    <div
      className="torrent-table-wrapper"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        position: "relative",
      }}
      onContextMenu={(e) => handleContextMenu(e, null)}
    >
      {/* Table Action Controls / Column Customizer Trigger */}
      <div
        style={{
          display: "flex",
          justifyContent: "flex-end",
          alignItems: "center",
          padding: "0.4rem 0.75rem",
          gap: "0.5rem",
          backgroundColor: "var(--bg-secondary, #171b35)",
          borderBottom: "1px solid var(--border-light, #1c203b)",
          fontSize: "0.8rem",
        }}
      >
        <button
          type="button"
          className="btn btn-small btn-outline"
          onClick={() => setShowColumnModal(!showColumnModal)}
          style={{ fontSize: "0.75rem", padding: "0.25rem 0.6rem" }}
        >
          ⚙ Customize Columns ({columns.length}/{ALL_COLUMNS.length})
        </button>
      </div>

      {/* Column Chooser Modal Dropdown */}
      {showColumnModal && (
        <>
          <div
            style={{
              position: "fixed",
              top: 0,
              left: 0,
              right: 0,
              bottom: 0,
              zIndex: 99,
            }}
            onClick={() => setShowColumnModal(false)}
          />
          <div
            className="card"
            style={{
              position: "absolute",
              top: "40px",
              right: "10px",
              zIndex: 100,
              padding: "1rem",
              backgroundColor: "var(--bg-card, #171b35)",
              border: "1px solid var(--border, #23284b)",
              boxShadow: "0 10px 30px rgba(0,0,0,0.5)",
              borderRadius: "8px",
              maxHeight: "350px",
              overflowY: "auto",
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(130px, 1fr))",
              maxWidth: "min(380px, 90vw)",
              minWidth: "240px",
              gap: "0.4rem 1.2rem",
              fontSize: "0.8rem",
            }}
          >
            {ALL_COLUMNS.map((c) => (
              <label
                key={c.key}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "6px",
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={visibleColumns.has(c.key)}
                  onChange={() => toggleColumn(c.key)}
                />
                <span
                  style={{
                    color: visibleColumns.has(c.key)
                      ? "var(--text-primary, #f8f4ed)"
                      : "var(--text-muted, #7e8092)",
                  }}
                >
                  {c.label}
                </span>
              </label>
            ))}
          </div>
        </>
      )}

      {/* Main Table */}
      <div ref={tableContainerRef} style={{ flex: "1 1 auto", minHeight: 0, overflow: "auto" }}>
        <table className="torrent-table" style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr
              style={{
                position: "sticky",
                top: 0,
                backgroundColor: "var(--bg-primary, #10111a)",
                zIndex: 2,
                borderBottom: "1px solid var(--border-light, #1c203b)",
              }}
            >
              {onToggleSelect && onSelectAll && (
                <th className="torrent-table-th" style={{ width: 36, textAlign: "center" }}>
                  <input
                    type="checkbox"
                    checked={allSelected}
                    ref={(el) => {
                      if (el) el.indeterminate = someSelected;
                    }}
                    onChange={() => {
                      if (allSelected) {
                        const filteredIdSet = new Set(filteredTorrents.map((t) => t.id));
                        onSelectAll([...selectedIds].filter((id) => !filteredIdSet.has(id)));
                      } else {
                        const next = new Set(selectedIds);
                        filteredTorrents.forEach((t) => next.add(t.id));
                        onSelectAll(Array.from(next));
                      }
                    }}
                  />
                </th>
              )}

              {columns.map((c) => (
                <th
                  key={c.key}
                  className="torrent-table-th"
                  draggable
                  onDragStart={() => handleColDragStart(c.key)}
                  onDragOver={(e) => handleColDragOver(e, c.key)}
                  onDrop={(e) => handleColDrop(e, c.key)}
                  onDragEnd={handleColDragEnd}
                  onClick={() => c.sortable && handleSort(c.key)}
                  style={{
                    cursor: c.sortable ? "pointer" : "default",
                    userSelect: "none",
                    whiteSpace: "nowrap",
                    padding: "0.6rem 0.5rem 0.6rem 0.75rem",
                    fontSize: "0.78rem",
                    position: "relative",
                    width: columnWidths[c.key] ? `${columnWidths[c.key]}px` : undefined,
                    minWidth: columnWidths[c.key] ? `${columnWidths[c.key]}px` : undefined,
                    maxWidth: columnWidths[c.key] ? `${columnWidths[c.key]}px` : undefined,
                    color:
                      sortKey === c.key
                        ? "var(--accent, #ffd166)"
                        : "var(--text-secondary, #c7c5d3)",
                    backgroundColor:
                      dragOverKey === c.key ? "rgba(255, 209, 102, 0.12)" : undefined,
                    borderLeft:
                      dragOverKey === c.key
                        ? "2px solid var(--accent, #ffd166)"
                        : "2px solid transparent",
                    transition: "background-color 0.1s, border-color 0.1s",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "4px",
                      overflow: "hidden",
                    }}
                  >
                    {/* Drag gripper */}
                    <span
                      title="Drag to reorder"
                      style={{
                        cursor: "grab",
                        opacity: 0.35,
                        fontSize: "0.7rem",
                        flexShrink: 0,
                        lineHeight: 1,
                      }}
                    >
                      ⠿
                    </span>
                    <span style={{ overflow: "hidden", textOverflow: "ellipsis" }}>{c.label}</span>
                    {sortKey === c.key && (
                      <span style={{ flexShrink: 0 }}>{sortAsc ? "▲" : "▼"}</span>
                    )}
                  </div>

                  {/* Resize handle — right edge */}
                  <div
                    onMouseDown={(e) => {
                      const th = e.currentTarget.parentElement as HTMLElement;
                      handleResizeMouseDown(e, c.key, th);
                    }}
                    onClick={(e) => e.stopPropagation()}
                    title="Drag to resize column"
                    style={{
                      position: "absolute",
                      top: 0,
                      right: 0,
                      width: "5px",
                      height: "100%",
                      cursor: "col-resize",
                      zIndex: 1,
                      userSelect: "none",
                    }}
                  />
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {paddingTop > 0 && (
              <tr>
                <td
                  colSpan={totalCols}
                  style={{ height: `${paddingTop}px`, padding: 0, border: 0 }}
                />
              </tr>
            )}
            {virtualRows.map((virtualRow) => {
              const t = sortedTorrents[virtualRow.index];
              return (
                <TorrentTableRow
                  key={t.id}
                  torrent={t}
                  index={virtualRow.index}
                  virtualRow={virtualRow}
                  columns={columns}
                  isSelected={t.id === selectedId}
                  isChecked={selectedIds.has(t.id)}
                  onSelect={onSelect}
                  onToggleSelect={onToggleSelect}
                  onContextMenu={handleContextMenu}
                  renderCell={renderCell}
                  measureElement={rowVirtualizer.measureElement}
                />
              );
            })}
            {paddingBottom > 0 && (
              <tr>
                <td
                  colSpan={totalCols}
                  style={{
                    height: `${paddingBottom}px`,
                    padding: 0,
                    border: 0,
                  }}
                />
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Right-Click Context Menu */}
      {contextMenu && (
        <TorrentContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          torrent={contextMenu.torrent}
          visibleColumns={visibleColumns}
          allColumns={ALL_COLUMNS}
          onClose={closeContextMenu}
          onToggleColumn={toggleColumn}
          onStart={(id) => (onResume ? onResume(id) : startSeeding.mutate(id))}
          onStop={(id) => (onPause ? onPause(id) : stopSeeding.mutate(id))}
          onUpdate={(tor) => updateTorrent.mutate(tor)}
          onAnnounce={(id) => announceTorrent.mutate(id)}
          onRecheck={(id) => recheckTorrent.mutate(id)}
          onDelete={(payload) => (onDelete ? onDelete(payload) : deleteTorrent.mutate(payload))}
          onMoveQueue={(payload) => moveTorrentQueue.mutate(payload)}
          onSearchIndexers={onSearchIndexers}
          onNavigateTab={onNavigateTab}
        />
      )}
    </div>
  );
};

export default TorrentTable;
