import React, { useMemo, useState, useEffect, useRef } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { Torrent } from "../api/types";
import { PlayIcon, StopIcon } from "./icons/UIIcons";
import { MediaArtworkImage } from "./common/MediaArtworkImage";
import {
  extractTrackerDomain,
  formatFileSize,
  formatSpeed,
  formatRatio,
  formatSeconds,
} from "../utils/formatters";
import { useTorrentStore, applyTelemetry } from "../stores/useTorrentStore";
import { useTranslation } from "../i18n";

export interface TorrentGridCardProps {
  torrent: Torrent;
  isSelected: boolean;
  onSelect: (torrent: Torrent) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (payload: { id: number; deleteFiles?: boolean }) => void;
}

export const TorrentGridCard: React.FC<TorrentGridCardProps> = React.memo(
  ({
    torrent: tTorrent,
    isSelected,
    onSelect,
    onPause,
    onResume,
    onDelete,
  }) => {
    const { t } = useTranslation();
    const telemetry = useTorrentStore((state) => state.telemetry[tTorrent.id]);
    const mergedTorrent = useMemo(
      () => applyTelemetry(tTorrent, telemetry),
      [tTorrent, telemetry],
    );

    const statusLower = (mergedTorrent.status || "").toLowerCase();
    const isDownloading = statusLower === "downloading";
    const isSeeding = statusLower === "seeding";
    const isPaused = statusLower === "paused";
    const isChecking = statusLower === "checking";

    return (
      <div
        className={`card torrent-grid-card ${isSelected ? "torrent-grid-card-selected" : ""}`}
        onClick={() => onSelect(mergedTorrent)}
        style={{
          display: "flex",
          flexDirection: "column",
          cursor: "pointer",
          overflow: "hidden",
          borderRadius: "6px",
          border: isSelected
            ? "1px solid var(--accent)"
            : "1px solid var(--border)",
          backgroundColor: "var(--bg-secondary)",
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
              left: "8px",
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
                : isChecking
                  ? "var(--info, #38bdf8)"
                  : isDownloading
                    ? "var(--accent)"
                    : "var(--text-muted)",
            }}
          >
            {isChecking
              ? `${t("torrentStatus.checking", "Checking")} (${((mergedTorrent.progress ?? 0) * 100).toFixed(1)}%)`
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
            onClick={(e) => e.stopPropagation()}
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

export interface TorrentGridProps {
  torrents: Torrent[];
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  privacyFilter?: string;
  selectedId: number | null;
  onSelect: (torrent: Torrent) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (payload: { id: number; deleteFiles?: boolean }) => void;
}

export const TorrentGrid: React.FC<TorrentGridProps> = ({
  torrents,
  filter,
  stateFilter,
  trackerFilter,
  privacyFilter,
  selectedId,
  onSelect,
  onPause,
  onResume,
  onDelete,
}) => {
  const { t } = useTranslation();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);

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
    return torrents.filter((tTorrent) => {
      if (filter) {
        const q = filter.toLowerCase();
        const matchName = (tTorrent.name || "").toLowerCase().includes(q);
        const matchMedia = (tTorrent.mediaTitle || "")
          .toLowerCase()
          .includes(q);
        if (!matchName && !matchMedia) return false;
      }
      if (stateFilter && stateFilter !== "All") {
        const st = (tTorrent.status || "").toLowerCase();
        const target = stateFilter.toLowerCase();
        if (target === "stopped" || target === "paused") {
          if (st !== "paused" && st !== "stopped" && st !== "idle")
            return false;
        } else if (st !== target) {
          return false;
        }
      }
      if (trackerFilter && trackerFilter !== "All") {
        const matchesTracker =
          (tTorrent.trackers &&
            tTorrent.trackers.some(
              (u) => extractTrackerDomain(u) === trackerFilter,
            )) ||
          extractTrackerDomain(tTorrent.trackerUrl || "") === trackerFilter;
        if (!matchesTracker) return false;
      }
      if (privacyFilter && privacyFilter !== "All") {
        if (privacyFilter === "Private" && !tTorrent.isPrivate) return false;
        if (privacyFilter === "Public" && tTorrent.isPrivate) return false;
      }
      return true;
    });
  }, [torrents, filter, stateFilter, trackerFilter, privacyFilter]);

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
              {rowTorrents.map((tTorrent) => (
                <TorrentGridCard
                  key={tTorrent.id}
                  torrent={tTorrent}
                  isSelected={tTorrent.id === selectedId}
                  onSelect={onSelect}
                  onPause={onPause}
                  onResume={onResume}
                  onDelete={onDelete}
                />
              ))}
            </div>
          );
        })}
      </div>
    </div>
  );
};

export default TorrentGrid;
