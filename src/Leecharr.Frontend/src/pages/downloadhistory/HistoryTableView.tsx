import React, { useRef, useState, useMemo } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useTranslation } from "../../i18n";
import {
  formatBytes,
  formatRatio,
  formatDate,
} from "../../utils/formatters";
import { getMediaDeepLink } from "../../utils/arrLinks";
import { MediaArtworkImage } from "../../components/common/MediaArtworkImage";
import type { DownloadHistoryEntry, ArrConnection } from "../../api/types";
import { formatDuration, type HistorySortColumn, type SortDirection } from "./types";

export interface HistoryTableViewProps {
  items: DownloadHistoryEntry[];
  arrConnections?: ArrConnection[] | null;
  onSelectItem: (item: DownloadHistoryEntry) => void;
  onSearchItem: (title: string) => void;
  onReAddItem: (id: number, title: string) => void;
  isReAdding: boolean;
  onDeleteItem: (id: number, title: string) => void;
  onFilterByTracker: (tracker: string) => void;
}

export const HistoryTableView: React.FC<HistoryTableViewProps> = ({
  items,
  arrConnections,
  onSelectItem,
  onSearchItem,
  onReAddItem,
  isReAdding,
  onDeleteItem,
  onFilterByTracker,
}) => {
  const { t } = useTranslation();
  const tableContainerRef = useRef<HTMLDivElement>(null);
  const [sortColumn, setSortColumn] = useState<HistorySortColumn | null>(null);
  const [sortDirection, setSortDirection] = useState<SortDirection>("desc");

  const handleSort = (column: HistorySortColumn) => {
    if (sortColumn === column) {
      setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortColumn(column);
      setSortDirection("desc");
    }
  };

  const sortedItems = useMemo(() => {
    if (!sortColumn) return items;
    return [...items].sort((a, b) => {
      let valA: number | string = 0;
      let valB: number | string = 0;

      switch (sortColumn) {
        case "title":
          valA = (a.metadata?.title || a.title || "").toLowerCase();
          valB = (b.metadata?.title || b.title || "").toLowerCase();
          break;
        case "totalSize":
          valA = a.totalSize || 0;
          valB = b.totalSize || 0;
          break;
        case "uploaded":
          valA = a.uploaded || 0;
          valB = b.uploaded || 0;
          break;
        case "ratio":
          valA = a.ratio || 0;
          valB = b.ratio || 0;
          break;
        case "seedingTime":
          valA = a.seedingTime || 0;
          valB = b.seedingTime || 0;
          break;
        case "dateAdded":
          valA = new Date(a.dateAdded).getTime() || 0;
          valB = new Date(b.dateAdded).getTime() || 0;
          break;
        case "status":
          valA = (a.status || "").toLowerCase();
          valB = (b.status || "").toLowerCase();
          break;
      }

      if (valA < valB) return sortDirection === "asc" ? -1 : 1;
      if (valA > valB) return sortDirection === "asc" ? 1 : -1;
      return 0;
    });
  }, [items, sortColumn, sortDirection]);

  const tableVirtualizer = useVirtualizer({
    count: sortedItems.length,
    getScrollElement: () => tableContainerRef.current,
    estimateSize: () => 74,
    overscan: 10,
  });

  const tableVirtualRows = tableVirtualizer.getVirtualItems();
  const tableTotalHeight = tableVirtualizer.getTotalSize();
  const tablePaddingTop =
    tableVirtualRows.length > 0 ? tableVirtualRows[0].start : 0;
  const tablePaddingBottom =
    tableVirtualRows.length > 0
      ? tableTotalHeight - tableVirtualRows[tableVirtualRows.length - 1].end
      : 0;

  const renderSortIndicator = (column: HistorySortColumn) => {
    if (sortColumn !== column) return null;
    return <span style={{ marginLeft: "4px" }}>{sortDirection === "asc" ? "▲" : "▼"}</span>;
  };

  return (
    <div
      className="card"
      style={{
        padding: 0,
        overflow: "hidden",
        flex: "1 1 auto",
        minHeight: 0,
        display: "flex",
        flexDirection: "column",
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
      }}
    >
      <div
        ref={tableContainerRef}
        style={{
          flex: "1 1 auto",
          minHeight: 0,
          overflowY: "auto",
          overflowX: "auto",
        }}
      >
        <table
          className="table"
          style={{ width: "100%", borderCollapse: "collapse" }}
        >
          <thead
            style={{
              position: "sticky",
              top: 0,
              zIndex: 2,
              backgroundColor: "var(--bg-secondary)",
            }}
          >
            <tr
              style={{
                borderBottom: "1px solid var(--border-color, #333)",
                textAlign: "left",
              }}
            >
              <th
                style={{ padding: "0.75rem 1rem", cursor: "pointer" }}
                onClick={() => handleSort("title")}
              >
                {t("history.releaseMedia")} {renderSortIndicator("title")}
              </th>
              <th
                style={{ padding: "0.75rem 1rem", width: "100px", cursor: "pointer" }}
                onClick={() => handleSort("totalSize")}
              >
                {t("history.size")} {renderSortIndicator("totalSize")}
              </th>
              <th
                style={{ padding: "0.75rem 1rem", width: "120px", cursor: "pointer" }}
                onClick={() => handleSort("uploaded")}
              >
                {t("history.uploaded")} {renderSortIndicator("uploaded")}
              </th>
              <th
                style={{ padding: "0.75rem 1rem", width: "90px", cursor: "pointer" }}
                onClick={() => handleSort("ratio")}
              >
                {t("history.ratio")} {renderSortIndicator("ratio")}
              </th>
              <th
                style={{ padding: "0.75rem 1rem", width: "100px", cursor: "pointer" }}
                onClick={() => handleSort("seedingTime")}
              >
                {t("history.seedTime")} {renderSortIndicator("seedingTime")}
              </th>
              <th
                style={{ padding: "0.75rem 1rem", width: "130px", cursor: "pointer" }}
                onClick={() => handleSort("dateAdded")}
              >
                {t("history.dateAdded")} {renderSortIndicator("dateAdded")}
              </th>
              <th
                style={{ padding: "0.75rem 1rem", width: "100px", cursor: "pointer" }}
                onClick={() => handleSort("status")}
              >
                {t("history.status")} {renderSortIndicator("status")}
              </th>
              <th
                style={{
                  padding: "0.75rem 1rem",
                  minWidth: "290px",
                  textAlign: "right",
                  whiteSpace: "nowrap",
                }}
              >
                {t("history.actions")}
              </th>
            </tr>
          </thead>
          <tbody>
            {tablePaddingTop > 0 && (
              <tr>
                <td
                  colSpan={8}
                  style={{
                    height: `${tablePaddingTop}px`,
                    padding: 0,
                    border: 0,
                  }}
                />
              </tr>
            )}
            {tableVirtualRows.map((virtualRow) => {
              const item = sortedItems[virtualRow.index];
              const meta = item.metadata;
              const displayTitle = meta?.title || item.title;
              const arrLink = getMediaDeepLink(item, arrConnections);

              return (
                <tr
                  key={item.id}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    transition: "background-color 0.15s ease",
                  }}
                >
                  <td style={{ padding: "0.75rem 1rem" }}>
                    <div
                      style={{
                        display: "flex",
                        gap: "0.75rem",
                        alignItems: "center",
                      }}
                    >
                      <MediaArtworkImage
                        src={
                          meta?.posterUrl ||
                          (item.torrentId
                            ? `/api/v1/media/artwork/${item.torrentId}/poster`
                            : "")
                        }
                        alt={displayTitle}
                        width={38}
                        height={54}
                        borderRadius="4px"
                        fallbackIcon={
                          item.source === "Radarr"
                            ? "🎬"
                            : item.source === "Sonarr"
                              ? "📺"
                              : item.source === "Lidarr"
                                ? "🎵"
                                : "📦"
                        }
                        onClick={() => onSelectItem(item)}
                      />

                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div
                          style={{
                            fontWeight: 600,
                            wordBreak: "break-word",
                            cursor: "pointer",
                          }}
                          onClick={() => onSelectItem(item)}
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
                        <div
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--text-muted, #777)",
                            fontFamily: "monospace",
                            marginTop: "0.2rem",
                            display: "flex",
                            gap: "0.5rem",
                            alignItems: "center",
                            flexWrap: "wrap",
                          }}
                        >
                          <span>{item.infoHash}</span>
                          {item.source &&
                            (arrLink ? (
                              <a
                                href={arrLink.url}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="badge badge-secondary"
                                style={{
                                  fontSize: "0.7rem",
                                  padding: "0.1rem 0.4rem",
                                  textDecoration: "none",
                                  color: "inherit",
                                }}
                                title={arrLink.label}
                                onClick={(e) => e.stopPropagation()}
                              >
                                {item.source} ↗
                              </a>
                            ) : (
                              <span
                                className="badge badge-secondary"
                                style={{
                                  fontSize: "0.7rem",
                                  padding: "0.1rem 0.4rem",
                                }}
                              >
                                {item.source}
                              </span>
                            ))}
                          {item.primaryTracker && (
                            <span
                              style={{
                                color: "var(--text-dim, #999)",
                                cursor: "pointer",
                              }}
                              onClick={() =>
                                onFilterByTracker(item.primaryTracker || "")
                              }
                              title={t("history.filterByTracker")}
                            >
                              • {item.primaryTracker}
                            </span>
                          )}
                        </div>
                      </div>
                    </div>
                  </td>

                  <td
                    style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}
                  >
                    {formatBytes(item.totalSize)}
                  </td>

                  <td
                    style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}
                  >
                    {formatBytes(item.uploaded)}
                  </td>

                  <td style={{ padding: "0.75rem 1rem" }}>
                    <span
                      className={`badge ${
                        item.ratio >= 1.0
                          ? "badge-success"
                          : "badge-secondary"
                      }`}
                      style={{ fontSize: "0.8rem" }}
                    >
                      {formatRatio(item.ratio)}
                    </span>
                  </td>

                  <td
                    style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}
                  >
                    {formatDuration(item.seedingTime)}
                  </td>

                  <td
                    style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}
                  >
                    <div>{formatDate(item.dateAdded)}</div>
                    {item.dateRemoved && (
                      <div
                        style={{
                          fontSize: "0.75rem",
                          color: "var(--text-muted, #777)",
                        }}
                      >
                        {t("history.removed")}{" "}
                        {formatDate(item.dateRemoved)}
                      </div>
                    )}
                  </td>

                  <td style={{ padding: "0.75rem 1rem" }}>
                    <span
                      className={`badge ${
                        item.status === "Active"
                          ? "badge-success"
                          : item.status === "Completed"
                            ? "badge-primary"
                            : "badge-stopped"
                      }`}
                    >
                      {t(
                        "torrentStatus." +
                          (item.status || "active").toLowerCase(),
                        item.status,
                      )}
                    </span>
                  </td>

                  <td
                    style={{
                      padding: "0.75rem 1rem",
                      textAlign: "right",
                      whiteSpace: "nowrap",
                    }}
                  >
                    <div
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.45rem",
                        whiteSpace: "nowrap",
                      }}
                    >
                      <button
                        className="btn btn-outline"
                        style={{
                          fontSize: "0.75rem",
                          padding: "0.3rem 0.65rem",
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.35rem",
                          whiteSpace: "nowrap",
                        }}
                        onClick={() => onSelectItem(item)}
                        title={t("history.viewSynopsis")}
                      >
                        <span>ℹ️</span>
                        <span>{t("history.details")}</span>
                      </button>
                      <button
                        className="btn btn-outline"
                        style={{
                          fontSize: "0.75rem",
                          padding: "0.3rem 0.65rem",
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.35rem",
                          whiteSpace: "nowrap",
                        }}
                        onClick={() => onSearchItem(item.title)}
                        title={t("history.searchReleaseAgain")}
                      >
                        <span>🔍</span>
                        <span>{t("history.search")}</span>
                      </button>
                      <button
                        className="btn btn-primary"
                        style={{
                          fontSize: "0.75rem",
                          padding: "0.3rem 0.65rem",
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.35rem",
                          whiteSpace: "nowrap",
                        }}
                        onClick={() => onReAddItem(item.id, item.title)}
                        disabled={
                          isReAdding ||
                          item.status === "Active"
                        }
                        title={
                          item.status === "Active"
                            ? t(
                                "history.alreadyInLibrary",
                                "Already in library",
                              )
                            : t("history.reAddTitle")
                        }
                      >
                        <span>🔄</span>
                        <span>{t("history.reAdd")}</span>
                      </button>
                      <button
                        className="btn btn-outline"
                        style={{
                          fontSize: "0.75rem",
                          padding: "0.3rem 0.55rem",
                          color: "var(--danger, #dc3545)",
                          display: "inline-flex",
                          alignItems: "center",
                          justifyContent: "center",
                          whiteSpace: "nowrap",
                        }}
                        onClick={() => onDeleteItem(item.id, item.title)}
                        title={t("history.deleteHistoricalRecord")}
                      >
                        ✕
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
            {tablePaddingBottom > 0 && (
              <tr>
                <td
                  colSpan={8}
                  style={{
                    height: `${tablePaddingBottom}px`,
                    padding: 0,
                    border: 0,
                  }}
                />
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};
