import React, { useMemo, useState, useEffect, useRef, useCallback } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { Torrent } from "../api/types";
import { PlayIcon, StopIcon } from "./icons/UIIcons";
import { MediaArtworkImage } from "./common/MediaArtworkImage";
import {
  formatFileSize,
  formatSpeed,
  formatRatio,
  formatSeconds,
} from "../utils/formatters";
import { filterTorrents } from "../utils/filterUtils";
import { useTorrentStore, applyTelemetry } from "../stores/useTorrentStore";
import { useTranslation } from "../i18n";
import TorrentContextMenu from "./TorrentContextMenu";
import {
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useUpdateTorrent,
  useAnnounceTorrent,
  useRecheckTorrent,
  useMoveTorrentQueue,
  useMoveTorrentQueueBatch,
} from "../api/hooks";

export interface TorrentGridCardProps {
  torrent: Torrent;
  isSelected: boolean;
  isChecked?: boolean;
  index?: number;
  onSelect: (torrent: Torrent) => void;
  onClick?: (torrent: Torrent, index: number, e: React.MouseEvent) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (payload: { id: number; deleteFiles?: boolean; ids?: number[] }) => void;
  onContextMenu?: (e: React.MouseEvent, torrent: Torrent) => void;
  onToggleSelect?: (id: number, multi?: boolean) => void;
}

export const TorrentGridCard: React.FC<TorrentGridCardProps> = React.memo(
  ({
    torrent: tTorrent,
    isSelected,
    isChecked = false,
    index,
    onSelect,
    onClick,
    onPause,
    onResume,
    onDelete,
    onContextMenu,
    onToggleSelect,
  }) => {
    const { t } = useTranslation();
    const telemetry = useTorrentStore((state) => state.telemetry[tTorrent.id]);
    const mergedTorrent = useMemo(
      () => applyTelemetry(tTorrent, telemetry),
      [tTorrent, telemetry],
    );

    const handleClick = (e: React.MouseEvent) => {
      if (onClick && index !== undefined) {
        onClick(mergedTorrent, index, e);
      } else {
        onSelect(mergedTorrent);
      }
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        if (onClick && index !== undefined) {
          onClick(mergedTorrent, index, e as unknown as React.MouseEvent);
        } else {
          onSelect(mergedTorrent);
        }
      }
    };

    const handleContextMenu = (e: React.MouseEvent) => {
      if (onContextMenu) {
        onContextMenu(e, mergedTorrent);
      }
    };

    const statusLower = (mergedTorrent.status || "").toLowerCase();
    const isActive =
      statusLower === "downloading" ||
      statusLower === "seeding" ||
      statusLower === "checking" ||
      statusLower === "active";
    const isDownloading = statusLower === "downloading";
    const isSeeding = statusLower === "seeding";
    const isPaused = !isActive;
    const isChecking = statusLower === "checking";
    const isQueuedRecheck =
      statusLower === "queuedforchecking" ||
      statusLower === "checking_queued" ||
      statusLower === "queued_check";

    return (
      <div
        className={`card torrent-grid-card ${isSelected ? "torrent-grid-card-selected" : ""} ${isChecked ? "torrent-grid-card-checked" : ""}`}
        role="button"
        tabIndex={0}
        onClick={handleClick}
        onKeyDown={handleKeyDown}
        onContextMenu={handleContextMenu}
        style={{
          display: "flex",
          flexDirection: "column",
          cursor: "pointer",
          overflow: "hidden",
          borderRadius: "6px",
          border: isSelected
            ? "1px solid var(--accent)"
            : isChecked
              ? "1px solid var(--accent-light, #ffd166)"
              : "1px solid var(--border)",
          backgroundColor: isSelected
            ? "var(--accent-bg-light, rgba(255, 209, 102, 0.1))"
            : isChecked
              ? "rgba(255, 209, 102, 0.05)"
              : "var(--bg-secondary)",
          transition: "all 0.15s ease-in-out",
          height: "380px",
          boxSizing: "border-box",
        }}
      >
        {/* Poster Header */}
        <div
          style={{
            height: "240px",
            backgroundColor: "rgba(0,0,0,0.4)",
            position: "relative",
            overflow: "hidden",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
          }}
        >
          {onToggleSelect && (
            <div
              className="torrent-grid-checkbox-container"
              role="button"
              tabIndex={0}
              onClick={(e) => e.stopPropagation()}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.stopPropagation();
                }
              }}
              onContextMenu={(e) => e.stopPropagation()}
              style={{
                position: "absolute",
                top: "8px",
                left: "8px",
                zIndex: 3,
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                backgroundColor: "rgba(16, 17, 26, 0.75)",
                borderRadius: "4px",
                padding: "3px 5px",
                backdropFilter: "blur(4px)",
              }}
            >
              <input
                type="checkbox"
                className="torrent-row-checkbox"
                checked={isChecked}
                onChange={(e) => {
                  e.stopPropagation();
                  onToggleSelect(mergedTorrent.id);
                }}
                aria-label={`Select ${mergedTorrent.name}`}
                style={{ cursor: "pointer", margin: 0 }}
              />
            </div>
          )}

          <MediaArtworkImage
            src={mergedTorrent.posterUrl}
            alt={mergedTorrent.name}
            height={240}
            width="100%"
            fallbackIcon="🎬"
            fallbackText={mergedTorrent.mediaTitle || mergedTorrent.name}
            style={{ width: "100%", height: "100%" }}
          />

          {/* Media Badges */}
          <div
            style={{
              position: "absolute",
              top: "8px",
              left: onToggleSelect ? "38px" : "8px",
              display: "flex",
              gap: "4px",
              flexWrap: "wrap",
              zIndex: 2,
            }}
          >
            {mergedTorrent.isPrivate && (
              <span
                className="badge"
                title={t("torrents.table.privateTooltip")}
                style={{
                  backgroundColor: "var(--danger)",
                  color: "#fff",
                  fontSize: "0.65rem",
                  fontWeight: 700,
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "3px",
                  boxShadow: "0 1px 3px rgba(0,0,0,0.5)",
                }}
              >
                <i className="fas fa-lock" style={{ fontSize: "0.58rem" }} />{" "}
                {t("torrents.filters.privateBep27")}
              </span>
            )}
            {mergedTorrent.resolution && (
              <span
                className="badge"
                style={{
                  backgroundColor: "#8b5cf6",
                  color: "#fff",
                  fontSize: "0.65rem",
                }}
              >
                {mergedTorrent.resolution}
              </span>
            )}
            {mergedTorrent.hdrFormat && (
              <span
                className="badge"
                style={{
                  backgroundColor: "#f43f5e",
                  color: "#fff",
                  fontSize: "0.65rem",
                }}
              >
                {mergedTorrent.hdrFormat}
              </span>
            )}
            {mergedTorrent.audioCodec && (
              <span
                className="badge"
                style={{
                  backgroundColor: "#3b82f6",
                  color: "#fff",
                  fontSize: "0.65rem",
                }}
              >
                {mergedTorrent.audioCodec}
              </span>
            )}
          </div>

          {/* Status Pill */}
          <div
            style={{
              position: "absolute",
              top: "8px",
              right: "8px",
              padding: "2px 8px",
              borderRadius: "4px",
              fontSize: "0.7rem",
              fontWeight: 700,
              textTransform: "capitalize",
              backgroundColor: "rgba(16, 17, 26, 0.85)",
              border: "1px solid var(--border)",
              color: isSeeding
                ? "var(--success)"
                : isQueuedRecheck
                  ? "#f59e0b"
                  : isChecking
                    ? "var(--info, #38bdf8)"
                    : isDownloading
                      ? "var(--accent)"
                      : "var(--text-muted)",
            }}
          >
            {isChecking
              ? `${t("torrentStatus.checking", "Checking")} (${((mergedTorrent.progress ?? 0) * 100).toFixed(1)}%)`
              : isQueuedRecheck
                ? t("torrentStatus.queuedforchecking", "Queued for Recheck")
                : t(
                    "torrentStatus." +
                      (mergedTorrent.status || "idle").toLowerCase(),
                    mergedTorrent.status || "Idle",
                  )}
          </div>
        </div>

        {/* Card Body */}
        <div
          style={{
            padding: "0.75rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.5rem",
            flex: 1,
          }}
        >
          <div
            style={{
              fontSize: "0.9rem",
              fontWeight: 600,
              color: "var(--text-primary)",
              whiteSpace: "nowrap",
              overflow: "hidden",
              textOverflow: "ellipsis",
            }}
            title={mergedTorrent.name}
          >
            {mergedTorrent.isPrivate && (
              <i
                className="fas fa-lock"
                title={t("torrents.table.privateTooltip")}
                style={{
                  color: "var(--danger)",
                  marginRight: "6px",
                  fontSize: "0.75rem",
                }}
              />
            )}
            {mergedTorrent.mediaTitle || mergedTorrent.name}
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              fontSize: "0.75rem",
              color: "var(--text-secondary)",
            }}
          >
            <span>{formatFileSize(mergedTorrent.totalSize)}</span>
            <span
              style={{
                fontWeight: 600,
                color: isChecking ? "var(--info, #38bdf8)" : undefined,
              }}
            >
              {((mergedTorrent.progress ?? 0) * 100).toFixed(1)}%
            </span>
          </div>

          {/* Progress Bar */}
          <div
            style={{
              height: "4px",
              backgroundColor: "rgba(255,255,255,0.08)",
              borderRadius: "2px",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                height: "100%",
                width: `${Math.min(100, Math.max(0, (mergedTorrent.progress ?? 0) * 100))}%`,
                backgroundColor: isSeeding
                  ? "var(--success)"
                  : isChecking
                    ? "var(--info, #38bdf8)"
                    : "var(--accent)",
                transition: "width 0.3s",
              }}
            />
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              fontSize: "0.75rem",
              marginTop: "2px",
            }}
          >
            {isDownloading && (
              <span
                style={{ color: "var(--accent)", fontWeight: 600 }}
                title={
                  mergedTorrent.eta && mergedTorrent.eta > 0
                    ? `${t("torrents.detail.eta")}: ${formatSeconds(mergedTorrent.eta)}`
                    : undefined
                }
              >
                ↓ {formatSpeed(mergedTorrent.downloadSpeed)}
                {mergedTorrent.eta && mergedTorrent.eta > 0 && (
                  <span
                    style={{
                      fontWeight: 400,
                      opacity: 0.85,
                      marginLeft: "4px",
                    }}
                  >
                    ({formatSeconds(mergedTorrent.eta)})
                  </span>
                )}
              </span>
            )}
            {isSeeding && (
              <span style={{ color: "var(--success)", fontWeight: 600 }}>
                ↑ {formatSpeed(mergedTorrent.uploadSpeed)}
              </span>
            )}
            <span
              style={{
                color: "var(--text-dim)",
                marginLeft: !isDownloading && !isSeeding ? "auto" : undefined,
              }}
            >
              {t("torrents.grid.ratio", {
                ratio: formatRatio(mergedTorrent.ratio ?? 0),
              })}
            </span>
          </div>

          {/* Card Footer Actions */}
          <div
            style={{
              display: "flex",
              gap: "6px",
              marginTop: "auto",
              paddingTop: "6px",
            }}
            role="button"
            tabIndex={0}
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.stopPropagation();
              }
            }}
          >
            {isPaused ? (
              <button
                className="btn btn-small btn-success"
                style={{ flex: 1 }}
                onClick={() => onResume(mergedTorrent.id)}
              >
                <PlayIcon size={11} /> {t("torrents.grid.resume")}
              </button>
            ) : (
              <button
                className="btn btn-small"
                style={{ flex: 1 }}
                onClick={() => onPause(mergedTorrent.id)}
              >
                <StopIcon size={11} /> {t("torrents.grid.pause")}
              </button>
            )}
            <button
              className="btn btn-small btn-danger"
              onClick={() => {
                onDelete({ id: mergedTorrent.id, deleteFiles: false });
              }}
            >
              {t("torrents.grid.delete")}
            </button>
          </div>
        </div>
      </div>
    );
  },
);
TorrentGridCard.displayName = "TorrentGridCard";

export function computeRangeSelection(
  torrentIds: number[],
  anchorIndex: number,
  targetIndex: number,
): number[] {
  const start = Math.min(anchorIndex, targetIndex);
  const end = Math.max(anchorIndex, targetIndex);
  return torrentIds.slice(start, end + 1);
}

export function computeEffectiveContextMenuSelection(
  selectedIds: Set<number>,
  clickedTorrentId: number | null,
): Set<number> {
  if (clickedTorrentId === null) {
    return selectedIds;
  }
  if (!selectedIds.has(clickedTorrentId)) {
    return new Set([clickedTorrentId]);
  }
  return selectedIds;
}

interface ContextMenuState {
  x: number;
  y: number;
  torrent: Torrent | null;
  selectedTorrents?: Torrent[];
}

export interface TorrentGridProps {
  torrents: Torrent[];
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  privacyFilter?: string;
  selectedTagIds?: Set<number>;
  tagMatchMode?: "AND" | "OR";
  selectedTag?: string;
  selectedId: number | null;
  onSelect: (torrent: Torrent) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (payload: { id: number; deleteFiles?: boolean; ids?: number[] }) => void;
  selectedIds?: Set<number>;
  onToggleSelect?: (id: number, multi?: boolean) => void;
  onSelectAll?: (ids: number[]) => void;
  onContextMenu?: (e: React.MouseEvent, torrent: Torrent) => void;
  onSearchIndexers?: (name: string) => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
}

export const TorrentGrid: React.FC<TorrentGridProps> = ({
  torrents,
  filter,
  stateFilter,
  trackerFilter,
  privacyFilter,
  selectedTagIds,
  tagMatchMode = "OR",
  selectedTag,
  selectedId,
  onSelect,
  onPause,
  onResume,
  onDelete,
  selectedIds: propSelectedIds,
  onToggleSelect,
  onSelectAll,
  onContextMenu: onContextMenuProp,
  onSearchIndexers,
  onNavigateTab,
}) => {
  const { t } = useTranslation();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);

  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const updateTorrent = useUpdateTorrent();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const moveTorrentQueue = useMoveTorrentQueue();
  const moveTorrentQueueBatch = useMoveTorrentQueueBatch();

  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const closeContextMenu = useCallback(() => setContextMenu(null), []);

  const storeSelectedIds = useTorrentStore((state) => state.selectedIds);
  const selectedIds = propSelectedIds ?? storeSelectedIds;

  const selectionAnchorIndexRef = useRef<number | null>(null);
  const lastClickedIndexRef = useRef<number | null>(null);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    setContainerWidth(el.clientWidth);
    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setContainerWidth(entry.contentRect.width);
      }
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const filteredTorrents = useMemo(() => {
    return filterTorrents(torrents, {
      filter,
      stateFilter,
      trackerFilter,
      privacyFilter,
      selectedTagIds,
      tagMatchMode,
      tagFilter: selectedTag,
    });
  }, [
    torrents,
    filter,
    stateFilter,
    trackerFilter,
    privacyFilter,
    selectedTagIds,
    tagMatchMode,
    selectedTag,
  ]);

  const handleCardClick = useCallback(
    (torrent: Torrent, index: number, e: React.MouseEvent) => {
      if (e.shiftKey) {
        const anchor =
          selectionAnchorIndexRef.current !== null
            ? selectionAnchorIndexRef.current
            : index;
        selectionAnchorIndexRef.current = anchor;
        lastClickedIndexRef.current = index;
        const torrentIds = filteredTorrents.map((r) => r.id);
        const range = computeRangeSelection(torrentIds, anchor, index);
        if (onSelectAll) {
          onSelectAll(range);
        } else {
          useTorrentStore.getState().setSelectedIds(new Set(range));
        }
        onSelect?.(torrent);
      } else if (e.ctrlKey || e.metaKey) {
        selectionAnchorIndexRef.current = index;
        lastClickedIndexRef.current = index;
        if (onToggleSelect) {
          onToggleSelect(torrent.id, true);
        } else {
          useTorrentStore.getState().toggleSelectedId(torrent.id);
        }
        onSelect?.(torrent);
      } else {
        selectionAnchorIndexRef.current = index;
        lastClickedIndexRef.current = index;
        if (onSelectAll) {
          onSelectAll([torrent.id]);
        } else {
          useTorrentStore.getState().setSelectedIds(new Set([torrent.id]));
        }
        onSelect?.(torrent);
      }
    },
    [filteredTorrents, onSelectAll, onSelect, onToggleSelect],
  );

  const handleContextMenu = useCallback(
    (e: React.MouseEvent, torrent: Torrent | null) => {
      e.preventDefault();
      e.stopPropagation();
      const effectiveSelectedIds = computeEffectiveContextMenuSelection(
        selectedIds,
        torrent?.id ?? null,
      );
      if (torrent && !selectedIds.has(torrent.id)) {
        if (onSelectAll) {
          onSelectAll([torrent.id]);
        } else {
          useTorrentStore.getState().setSelectedIds(effectiveSelectedIds);
        }
        onSelect?.(torrent);
      }
      const currentTorrents = torrents || [];
      const selectedTorrents = currentTorrents.filter((t) =>
        effectiveSelectedIds.has(t.id),
      );
      setContextMenu({
        x: e.clientX,
        y: e.clientY,
        torrent,
        selectedTorrents,
      });
      if (torrent && onContextMenuProp) {
        onContextMenuProp(e, torrent);
      }
    },
    [selectedIds, onSelectAll, onSelect, torrents, onContextMenuProp],
  );

  const gap = 16;
  const minCardWidth = 240;
  const availableWidth = Math.max(0, containerWidth - 32);
  const columnCount = Math.max(
    1,
    Math.floor((availableWidth + gap) / (minCardWidth + gap)),
  );
  const rowCount = Math.ceil(filteredTorrents.length / columnCount);

  const rowVirtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => containerRef.current,
    estimateSize: () => 396,
    overscan: 3,
  });

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
        <div
          style={{ fontSize: "3.5rem", marginBottom: "1rem", opacity: 0.85 }}
        >
          📁
        </div>
        <h3
          style={{
            color: "var(--text-primary, #f8f4ed)",
            fontSize: "1.25rem",
            fontWeight: 600,
            marginBottom: "0.5rem",
          }}
        >
          {torrents.length === 0
            ? t("torrents.empty.noTorrents")
            : t("torrents.empty.noFilterMatches")}
        </h3>
        <p
          style={{
            color: "var(--text-secondary, #c7c5d3)",
            fontSize: "0.9rem",
            maxWidth: "400px",
            margin: 0,
          }}
        >
          {torrents.length === 0
            ? t("torrents.empty.noMagnetDesc")
            : t("torrents.empty.noFilterMatchesDesc")}
        </p>
      </div>
    );
  }

  return (
    <div
      ref={containerRef}
      onScroll={() => {
        if (contextMenu) closeContextMenu();
      }}
      onContextMenu={(e) => {
        if (e.target === containerRef.current) {
          handleContextMenu(e, null);
        }
      }}
      style={{
        flex: "1 1 auto",
        minHeight: 0,
        height: "100%",
        overflowY: "auto",
        padding: "1rem",
        boxSizing: "border-box",
      }}
    >
      <div
        style={{
          height: `${rowVirtualizer.getTotalSize()}px`,
          width: "100%",
          position: "relative",
        }}
      >
        {rowVirtualizer.getVirtualItems().map((virtualRow) => {
          const startIndex = virtualRow.index * columnCount;
          const rowTorrents = filteredTorrents.slice(
            startIndex,
            startIndex + columnCount,
          );
          return (
            <div
              key={virtualRow.key}
              data-index={virtualRow.index}
              ref={rowVirtualizer.measureElement}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                transform: `translateY(${virtualRow.start}px)`,
                display: "grid",
                gridTemplateColumns: `repeat(${columnCount}, minmax(0, 1fr))`,
                gap: "1rem",
                paddingBottom: "1rem",
                boxSizing: "border-box",
              }}
            >
              {rowTorrents.map((tTorrent, colIndex) => {
                const cardIndex = startIndex + colIndex;
                return (
                  <TorrentGridCard
                    key={tTorrent.id}
                    torrent={tTorrent}
                    isSelected={tTorrent.id === selectedId}
                    isChecked={selectedIds.has(tTorrent.id)}
                    index={cardIndex}
                    onSelect={onSelect}
                    onClick={handleCardClick}
                    onPause={onPause}
                    onResume={onResume}
                    onDelete={onDelete}
                    onContextMenu={handleContextMenu}
                    onToggleSelect={onToggleSelect}
                  />
                );
              })}
            </div>
          );
        })}
      </div>

      {/* Right-Click Context Menu */}
      {contextMenu && (
        <TorrentContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          torrent={contextMenu.torrent}
          selectedTorrents={contextMenu.selectedTorrents}
          onClose={closeContextMenu}
          onStart={(id) => (onResume ? onResume(id) : startSeeding.mutate(id))}
          onStop={(id) => (onPause ? onPause(id) : stopSeeding.mutate(id))}
          onUpdate={(tor) => updateTorrent.mutate(tor)}
          onAnnounce={(id) => announceTorrent.mutate(id)}
          onRecheck={(id) => recheckTorrent.mutate(id)}
          onDelete={(payload) =>
            onDelete ? onDelete(payload) : deleteTorrent.mutate(payload)
          }
          onMoveQueue={(payload) => moveTorrentQueue.mutate(payload)}
          onBatchStart={(ids) =>
            ids.forEach((id) =>
              onResume ? onResume(id) : startSeeding.mutate(id),
            )
          }
          onBatchStop={(ids) =>
            ids.forEach((id) =>
              onPause ? onPause(id) : stopSeeding.mutate(id),
            )
          }
          onBatchAnnounce={(ids) =>
            ids.forEach((id) => announceTorrent.mutate(id))
          }
          onBatchRecheck={(ids) =>
            ids.forEach((id) => recheckTorrent.mutate(id))
          }
          onBatchDelete={(payload) => {
            if (onDelete && payload.ids.length > 0) {
              onDelete({
                id: payload.ids[0],
                deleteFiles: payload.deleteFiles,
                ids: payload.ids,
              });
            } else {
              payload.ids.forEach((id) =>
                deleteTorrent.mutate({ id, deleteFiles: payload.deleteFiles }),
              );
            }
          }}
          onBatchUpdate={(batchTorrents) =>
            batchTorrents.forEach((tor) => updateTorrent.mutate(tor))
          }
          onBatchMoveQueue={(payload) =>
            moveTorrentQueueBatch.mutate({
              ids: payload.ids,
              position: payload.position,
            })
          }
          onSearchIndexers={onSearchIndexers}
          onNavigateTab={onNavigateTab}
        />
      )}
    </div>
  );
};

export default TorrentGrid;
