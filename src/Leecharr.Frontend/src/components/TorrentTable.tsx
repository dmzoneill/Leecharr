import React, { useState, useCallback } from "react";
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
import { PlayIcon, StopIcon } from "./icons/UIIcons";
import type { Torrent } from "../api/types";

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
  selectedId?: number | null;
  selectedTorrentId?: number | null;
  onSelect?: (torrent: Torrent) => void;
  onSelectTorrent?: (id: number | null) => void;
  onPause?: (id: number) => void;
  onResume?: (id: number) => void;
  onDelete?: (id: number) => void;
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number) => void;
  onSelectAll?: (ids: number[]) => void;
  onSearchIndexers?: (query: string) => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
}

export const TorrentTable: React.FC<TorrentTableProps> = ({
  torrents: propTorrents,
  filter,
  stateFilter,
  trackerFilter,
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

  const [sortKey, setSortKey] = useState<ColumnKey>("name");
  const [sortAsc, setSortAsc] = useState(true);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [visibleColumns, setVisibleColumns] =
    useState<Set<string>>(loadVisibleColumns);
  const [showColumnModal, setShowColumnModal] = useState(false);

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

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

  const filteredTorrents = sourceTorrents.filter((t) => {
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
      const trackerDomain = extractTrackerDomain(t.trackerUrl || "");
      if (trackerDomain !== trackerFilter) return false;
    }
    return true;
  });

  const sortedTorrents = [...filteredTorrents].sort((a, b) => {
    let valA: any = (a as any)[sortKey];
    let valB: any = (b as any)[sortKey];

    if (sortKey === "#") {
      valA = a.queuePosition ?? a.id;
      valB = b.queuePosition ?? b.id;
    } else if (sortKey === "category") {
      valA = a.category ?? a.label ?? "";
      valB = b.category ?? b.label ?? "";
    } else if (sortKey === "eta") {
      valA =
        a.eta ??
        (a.downloadSpeed > 0
          ? (a.totalSize * (1 - a.progress)) / a.downloadSpeed
          : 9999999);
      valB =
        b.eta ??
        (b.downloadSpeed > 0
          ? (b.totalSize * (1 - b.progress)) / b.downloadSpeed
          : 9999999);
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

  const columns = ALL_COLUMNS.filter((col) => visibleColumns.has(col.key));
  const allSelected =
    filteredTorrents.length > 0 && selectedIds.size === filteredTorrents.length;

  if (filteredTorrents.length === 0) {
    return (
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
    );
  }

  const renderCell = (t: Torrent, key: ColumnKey, idx: number) => {
    switch (key) {
      case "#":
        return (
          <span
            style={{ color: "var(--text-muted, #7e8092)", fontSize: "0.75rem" }}
          >
            {t.queuePosition ?? idx + 1}
          </span>
        );

      case "name": {
        const historyMatch = history?.find(
          (h) =>
            (t.infoHash &&
              h.infoHash?.toLowerCase() === t.infoHash.toLowerCase()) ||
            h.title?.toLowerCase() === t.name?.toLowerCase(),
        );
        const meta = historyMatch?.metadata;
        const arrLink = historyMatch
          ? getMediaDeepLink(historyMatch, arrConnections)
          : null;
        const badges = getTorrentBadges(t);
        const posterSrc =
          t.posterUrl || t.artworkUrl || t.bannerUrl || meta?.posterUrl;

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
              <div
                style={{ display: "flex", alignItems: "center", gap: "6px" }}
              >
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
                  {meta?.title || t.mediaTitle || t.name}{" "}
                  {meta?.year ? `(${meta.year})` : ""}
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
                      style={{
                        fontSize: "0.65rem",
                        padding: "0.05rem 0.3rem",
                        backgroundColor: "rgba(255, 255, 255, 0.08)",
                        color: "var(--text-secondary, #c7c5d3)",
                      }}
                    >
                      {b}
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
                    pct >= 100
                      ? "var(--success, #22c55e)"
                      : "var(--accent, #ffd166)",
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
        return (
          <span>{formatBytes(t.downloaded ?? t.totalSize * t.progress)}</span>
        );

      case "uploaded":
        return <span>{formatBytes(t.uploaded ?? 0)}</span>;

      case "downloadSpeed":
        return (
          <span
            style={{
              color:
                t.downloadSpeed > 0
                  ? "var(--accent, #ffd166)"
                  : "var(--text-muted, #7e8092)",
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
              color:
                t.uploadSpeed > 0
                  ? "var(--success, #22c55e)"
                  : "var(--text-muted, #7e8092)",
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
        const remaining = t.totalSize * (1 - (t.progress || 0));
        const etaSec =
          t.downloadSpeed > 0 ? Math.floor(remaining / t.downloadSpeed) : 0;
        return (
          <span>
            {t.progress >= 1.0
              ? "Done"
              : etaSec > 0
                ? formatSeconds(etaSec)
                : "∞"}
          </span>
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

      default:
        return <span>{String((t as any)[key] ?? "-")}</span>;
    }
  };

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
            gridTemplateColumns: "repeat(2, 1fr)",
            gap: "0.4rem 1.5rem",
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
      )}

      {/* Main Table */}
      <div style={{ flex: "1 1 auto", minHeight: 0, overflow: "auto" }}>
        <table
          className="torrent-table"
          style={{ width: "100%", borderCollapse: "collapse" }}
        >
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
                <th
                  className="torrent-table-th"
                  style={{ width: 36, textAlign: "center" }}
                >
                  <input
                    type="checkbox"
                    checked={allSelected}
                    onChange={() =>
                      onSelectAll(allSelected ? [] : torrents.map((t) => t.id))
                    }
                  />
                </th>
              )}

              {columns.map((c) => (
                <th
                  key={c.key}
                  className="torrent-table-th"
                  onClick={() => c.sortable && handleSort(c.key)}
                  style={{
                    cursor: c.sortable ? "pointer" : "default",
                    userSelect: "none",
                    whiteSpace: "nowrap",
                    padding: "0.6rem 0.75rem",
                    fontSize: "0.78rem",
                    color:
                      sortKey === c.key
                        ? "var(--accent, #ffd166)"
                        : "var(--text-secondary, #c7c5d3)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "4px",
                    }}
                  >
                    <span>{c.label}</span>
                    {sortKey === c.key && <span>{sortAsc ? "▲" : "▼"}</span>}
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {sortedTorrents.map((t, idx) => {
              const isSelected = t.id === selectedId;
              const isChecked = selectedIds.has(t.id);

              return (
                <tr
                  key={t.id}
                  className={`torrent-table-row ${isSelected ? "torrent-table-row-selected" : ""}`}
                  onClick={() => onSelect(t)}
                  onContextMenu={(e) => handleContextMenu(e, t)}
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
                      <input
                        type="checkbox"
                        checked={isChecked}
                        onChange={() => onToggleSelect(t.id)}
                      />
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
                      {renderCell(t, c.key, idx)}
                    </td>
                  ))}
                </tr>
              );
            })}
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
          onDelete={(payload) =>
            onDelete ? onDelete(payload.id) : deleteTorrent.mutate(payload)
          }
          onMoveQueue={(payload) => moveTorrentQueue.mutate(payload)}
          onSearchIndexers={onSearchIndexers}
          onNavigateTab={onNavigateTab}
        />
      )}
    </div>
  );
};

export default TorrentTable;
