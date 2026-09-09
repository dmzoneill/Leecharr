import React, { useState, useRef, useEffect } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useTranslation } from "../../i18n";
import {
  formatBytes,
  formatRatio,
  formatDate,
  normalizeGenres,
} from "../../utils/formatters";
import { getMediaDeepLink } from "../../utils/arrLinks";
import { MediaArtworkImage } from "../../components/common/MediaArtworkImage";
import type { DownloadHistoryEntry, ArrConnection } from "../../api/types";
import { formatDuration } from "./types";

export interface HistoryGridViewProps {
  items: DownloadHistoryEntry[];
  arrConnections?: ArrConnection[] | null;
  onSelectItem: (item: DownloadHistoryEntry) => void;
  onSearchItem: (title: string) => void;
  onReAddItem: (id: number, title: string) => void;
  isReAdding: boolean;
  onFilterByGenre: (genre: string) => void;
}

export const HistoryGridView: React.FC<HistoryGridViewProps> = ({
  items,
  arrConnections,
  onSelectItem,
  onSearchItem,
  onReAddItem,
  isReAdding,
  onFilterByGenre,
}) => {
  const { t } = useTranslation();
  const gridContainerRef = useRef<HTMLDivElement>(null);
  const [gridContainerWidth, setGridContainerWidth] = useState(0);

  useEffect(() => {
    const el = gridContainerRef.current;
    if (!el) return;
    setGridContainerWidth(el.clientWidth);
    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setGridContainerWidth(entry.contentRect.width);
      }
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const gridGap = 20;
  const minCardWidth = 240;
  const availableGridWidth = Math.max(0, gridContainerWidth - 4);
  const columnCount = Math.max(
    1,
    Math.floor((availableGridWidth + gridGap) / (minCardWidth + gridGap)),
  );
  const gridRowCount = Math.ceil(items.length / columnCount);

  const gridVirtualizer = useVirtualizer({
    count: gridRowCount,
    getScrollElement: () => gridContainerRef.current,
    estimateSize: () => 480,
    overscan: 2,
  });

  return (
    <div
      ref={gridContainerRef}
      style={{
        flex: "1 1 0%",
        minHeight: 0,
        height: "100%",
        width: "100%",
        overflowY: "auto",
        overflowX: "hidden",
        paddingRight: "0.25rem",
        paddingBottom: "1rem",
        position: "relative",
        boxSizing: "border-box",
      }}
    >
      <div
        style={{
          height: `${gridVirtualizer.getTotalSize()}px`,
          width: "100%",
          position: "relative",
        }}
      >
        {gridVirtualizer.getVirtualItems().map((virtualRow) => {
          const startIndex = virtualRow.index * columnCount;
          const rowItems = items.slice(startIndex, startIndex + columnCount);
          return (
            <div
              key={virtualRow.key}
              data-index={virtualRow.index}
              ref={gridVirtualizer.measureElement}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                transform: `translateY(${virtualRow.start}px)`,
                display: "grid",
                gridTemplateColumns: `repeat(${columnCount}, minmax(0, 1fr))`,
                gap: "1.25rem",
                paddingBottom: "1.25rem",
                boxSizing: "border-box",
              }}
            >
              {rowItems.map((item) => {
                const meta = item.metadata;
                const displayTitle = meta?.title || item.title;
                const posterSrc =
                  meta?.posterUrl ||
                  (item.torrentId
                    ? `/api/v1/media/artwork/${item.torrentId}/poster`
                    : "");
                const arrLink = getMediaDeepLink(item, arrConnections);

                return (
                  <div
                    key={item.id}
                    className="card"
                    style={{
                      padding: 0,
                      overflow: "hidden",
                      display: "flex",
                      flexDirection: "column",
                      height: "auto",
                      minHeight: "min-content",
                      flexShrink: 0,
                      borderRadius: "8px",
                      border: "1px solid rgba(255, 255, 255, 0.08)",
                      backgroundColor: "var(--bg-secondary)",
                      boxShadow:
                        "0 4px 14px rgba(0, 0, 0, 0.35), 0 1px 3px rgba(0, 0, 0, 0.2)",
                      transition:
                        "transform 0.18s ease, box-shadow 0.18s ease, border-color 0.18s ease",
                      cursor: "pointer",
                    }}
                    onClick={() => onSelectItem(item)}
                  >
                    {/* Poster Artwork Box */}
                    <div
                      style={{
                        position: "relative",
                        width: "100%",
                        aspectRatio: "2 / 3",
                        backgroundColor: "var(--bg-primary)",
                        overflow: "hidden",
                        flexShrink: 0,
                      }}
                    >
                      <MediaArtworkImage
                        src={posterSrc}
                        alt={displayTitle}
                        fallbackIcon={
                          item.source === "Radarr"
                            ? "🎬"
                            : item.source === "Sonarr"
                              ? "📺"
                              : item.source === "Lidarr"
                                ? "🎵"
                                : "📦"
                        }
                        fallbackText={displayTitle}
                        style={{
                          position: "absolute",
                          top: 0,
                          left: 0,
                          width: "100%",
                          height: "100%",
                        }}
                      />

                      {/* Top-left Source Badge & Direct Deep Link */}
                      {item.source && (
                        <div
                          style={{
                            position: "absolute",
                            top: "8px",
                            left: "8px",
                            zIndex: 2,
                          }}
                          onClick={(e) => {
                            if (arrLink) {
                              e.stopPropagation();
                              window.open(
                                arrLink.url,
                                "_blank",
                                "noopener,noreferrer",
                              );
                            }
                          }}
                        >
                          <span
                            className="badge"
                            style={{
                              backgroundColor: "rgba(0, 0, 0, 0.78)",
                              backdropFilter: "blur(4px)",
                              color: "#fff",
                              fontSize: "0.68rem",
                              padding: "0.2rem 0.5rem",
                              border: "1px solid rgba(255,255,255,0.18)",
                              cursor: arrLink ? "pointer" : "default",
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.25rem",
                              borderRadius: "4px",
                            }}
                            title={
                              arrLink
                                ? `${arrLink.label} (${arrLink.url})`
                                : item.source
                            }
                          >
                            {item.source} {arrLink ? "↗" : ""}
                          </span>
                        </div>
                      )}

                      {/* Top-right Ratio Badge */}
                      <div
                        style={{
                          position: "absolute",
                          top: "8px",
                          right: "8px",
                          zIndex: 2,
                        }}
                      >
                        <span
                          className={`badge ${
                            item.ratio >= 2.0
                              ? "badge-success"
                              : item.ratio >= 1.0
                                ? "badge-primary"
                                : "badge-secondary"
                          }`}
                          style={{
                            fontSize: "0.72rem",
                            padding: "0.2rem 0.5rem",
                            boxShadow: "0 2px 6px rgba(0,0,0,0.5)",
                            borderRadius: "4px",
                          }}
                        >
                          ★ {formatRatio(item.ratio)}
                        </span>
                      </div>

                      {/* Bottom Telemetry Overlay Bar */}
                      <div
                        style={{
                          position: "absolute",
                          bottom: 0,
                          left: 0,
                          right: 0,
                          zIndex: 2,
                          backgroundColor: "rgba(0, 0, 0, 0.82)",
                          backdropFilter: "blur(6px)",
                          padding: "0.3rem 0.5rem",
                          display: "flex",
                          justifyContent: "space-between",
                          alignItems: "center",
                          fontSize: "0.7rem",
                          borderTop: "1px solid rgba(255,255,255,0.1)",
                        }}
                      >
                        <span style={{ color: "#eee" }}>
                          ↑ {formatBytes(item.uploaded)}
                        </span>
                        <span style={{ color: "var(--text-muted, #aaa)" }}>
                          ⏱ {formatDuration(item.seedingTime)}
                        </span>
                      </div>
                    </div>

                    {/* Card Info Body */}
                    <div
                      style={{
                        padding: "0.75rem",
                        display: "flex",
                        flexDirection: "column",
                        flex: "0 0 auto",
                        gap: "0.4rem",
                        backgroundColor: "var(--bg-secondary)",
                      }}
                    >
                      <div
                        style={{
                          fontWeight: 600,
                          fontSize: "0.85rem",
                          color: "var(--text-primary)",
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          display: "-webkit-box",
                          WebkitLineClamp: 2,
                          WebkitBoxOrient: "vertical",
                          lineHeight: "1.3",
                          minHeight: "2.2em",
                        }}
                        title={displayTitle}
                      >
                        {displayTitle}{" "}
                        {meta?.year ? (
                          <span
                            style={{
                              color: "var(--text-muted, #888)",
                              fontWeight: 400,
                            }}
                          >
                            ({meta.year})
                          </span>
                        ) : null}
                      </div>

                      {/* Genres (Clickable to Filter) */}
                      {(() => {
                        const genresList = normalizeGenres(meta?.genres);
                        return genresList.length > 0 ? (
                          <div
                            style={{
                              display: "flex",
                              gap: "0.3rem",
                              flexWrap: "wrap",
                            }}
                          >
                            {genresList.slice(0, 2).map((g, i) => (
                              <span
                                key={i}
                                className="badge badge-secondary"
                                style={{
                                  fontSize: "0.65rem",
                                  padding: "0.1rem 0.35rem",
                                  backgroundColor: "rgba(255,255,255,0.06)",
                                  color: "var(--text-muted)",
                                  borderRadius: "3px",
                                  cursor: "pointer",
                                }}
                                onClick={(e) => {
                                  e.stopPropagation();
                                  onFilterByGenre(g);
                                }}
                                title={t(
                                  "history.filterByGenre",
                                  'Filter downloads by genre "{genre}"',
                                  { genre: g },
                                )}
                              >
                                {g}
                              </span>
                            ))}
                          </div>
                        ) : null;
                      })()}

                      {/* Stats Bar */}
                      <div
                        style={{
                          display: "grid",
                          gridTemplateColumns: "1fr 1fr",
                          gap: "0.25rem 0.5rem",
                          fontSize: "0.72rem",
                          color: "var(--text-muted)",
                          marginTop: "auto",
                          paddingTop: "0.4rem",
                          borderTop: "1px solid var(--border-light)",
                        }}
                      >
                        <div>
                          <span>{t("history.size")}</span>
                          <strong style={{ color: "var(--text-primary)" }}>
                            {formatBytes(item.totalSize)}
                          </strong>
                        </div>
                        <div>
                          <span>{t("history.uploaded")}</span>
                          <strong style={{ color: "var(--text-primary)" }}>
                            {formatBytes(item.uploaded)}
                          </strong>
                        </div>
                        <div>
                          <span>{t("history.ratio")}</span>
                          <strong
                            style={{
                              color:
                                item.ratio >= 1.0
                                  ? "var(--success)"
                                  : "var(--text-primary)",
                            }}
                          >
                            {formatRatio(item.ratio)}
                          </strong>
                        </div>
                        <div>
                          <span>{t("history.added")}</span>
                          <strong style={{ color: "var(--text-primary)" }}>
                            {formatDate(item.dateAdded).split(" ")[0]}
                          </strong>
                        </div>
                      </div>

                      {/* Quick Card Action Buttons */}
                      <div
                        style={{
                          display: "flex",
                          gap: "0.3rem",
                          marginTop: "0.5rem",
                          paddingTop: "0.4rem",
                          borderTop: "1px solid var(--border-light)",
                        }}
                        onClick={(e) => e.stopPropagation()}
                      >
                        <button
                          className="btn btn-outline"
                          style={{
                            flex: 1,
                            fontSize: "0.75rem",
                            padding: "0.25rem 0.4rem",
                            display: "inline-flex",
                            alignItems: "center",
                            justifyContent: "center",
                            gap: "0.35rem",
                          }}
                          onClick={() => onSearchItem(item.title)}
                          title={t("history.searchAgain")}
                        >
                          <span>🔍</span> <span>{t("history.search")}</span>
                        </button>
                        <button
                          className="btn btn-primary"
                          style={{
                            flex: 1,
                            fontSize: "0.75rem",
                            padding: "0.25rem 0.4rem",
                            display: "inline-flex",
                            alignItems: "center",
                            justifyContent: "center",
                            gap: "0.35rem",
                          }}
                          onClick={() => onReAddItem(item.id, item.title)}
                          disabled={isReAdding || item.status === "Active"}
                          title={
                            item.status === "Active"
                              ? t(
                                  "history.alreadyInLibrary",
                                  "Already in library",
                                )
                              : t("history.reAddTitle")
                          }
                        >
                          <span>🔄</span> <span>{t("history.reAdd")}</span>
                        </button>
                        <button
                          className="btn btn-outline"
                          style={{
                            fontSize: "0.75rem",
                            padding: "0.25rem 0.45rem",
                            display: "inline-flex",
                            alignItems: "center",
                            justifyContent: "center",
                          }}
                          onClick={() => onSelectItem(item)}
                          title={t("history.viewFullMedia")}
                        >
                          ℹ️
                        </button>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    </div>
  );
};
