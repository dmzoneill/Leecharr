import { useTranslation } from "../i18n";
import { useState } from "react";
import {
  useDownloadHistory,
  useReAddHistoryTorrent,
  useDeleteHistoryTorrent,
  useClearDownloadHistory,
  useEnrichHistoryTorrent,
  useEnrichAllHistory,
  useReconcileDownloadHistory,
  useArrConnections,
  useIndexers,
} from "../api/hooks";
import {
  formatBytes,
  formatRatio,
  formatDate,
  normalizeGenres,
} from "../utils/formatters";
import {
  getMediaDeepLink,
  getImdbUrl,
  getTmdbUrl,
  getTvdbUrl,
  getActorSearchUrl,
  getProwlarrUrl,
} from "../utils/arrLinks";
import { useToast } from "../context/ToastContext";
import { useConfirm } from "../context/ConfirmContext";
import { useEscapeKey } from "../hooks/useEscapeKey";
import { IndexerSearchModal } from "../components/IndexerSearchModal";
import type { DownloadHistoryEntry } from "../api/types";

function formatDuration(seconds: number): string {
  if (!seconds || seconds <= 0) return "0s";
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);

  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

export default function DownloadHistory() {
  const { t } = useTranslation();

  const [searchTerm, setSearchTerm] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [viewMode, setViewMode] = useState<"grid" | "table">("grid");
  const [searchModalQuery, setSearchModalQuery] = useState<string | null>(null);
  const [selectedDetailItem, setSelectedDetailItem] =
    useState<DownloadHistoryEntry | null>(null);
  const { showToast } = useToast();
  const confirm = useConfirm();

  useEscapeKey(() => setSelectedDetailItem(null), Boolean(selectedDetailItem));

  const { data: arrConnections } = useArrConnections();
  const { data: indexers } = useIndexers();

  const {
    data: history,
    isLoading,
    isError,
  } = useDownloadHistory({
    query: searchTerm.trim() || undefined,
    status: statusFilter !== "all" ? statusFilter : undefined,
  });

  const reAddMutation = useReAddHistoryTorrent();
  const deleteMutation = useDeleteHistoryTorrent();
  const clearMutation = useClearDownloadHistory();
  const enrichMutation = useEnrichHistoryTorrent();
  const enrichAllMutation = useEnrichAllHistory();
  const reconcileMutation = useReconcileDownloadHistory();

  const handleReAdd = (id: number, title: string) => {
    reAddMutation.mutate(id, {
      onSuccess: () => {
        showToast(
          t(
            "history.reAddedToast",
            'Re-added "{title}" to active seeding library',
            { title },
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t("history.failedToReAdd", 'Failed to re-add "{title}": {error}', {
            title,
            error: err.message || "Unknown error",
          }),
          "error",
        );
      },
    });
  };

  const handleDelete = async (id: number, title: string) => {
    const ok = await confirm({
      title: t("history.deleteHistoryRecord", "Delete History Record"),
      message: t(
        "history.deleteHistoryRecordConfirm",
        'Delete history record for "{title}"?',
        { title },
      ),
      danger: true,
      confirmText: t("common.delete", "Delete"),
    });
    if (!ok) return;

    deleteMutation.mutate(id, {
      onSuccess: () => {
        if (selectedDetailItem?.id === id) {
          setSelectedDetailItem(null);
        }
        showToast(
          t("history.recordRemovedToast", "Historical record removed"),
          "info",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToDeleteRecord",
            "Failed to delete record: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleEnrich = (item: DownloadHistoryEntry) => {
    enrichMutation.mutate(item.id, {
      onSuccess: (updated) => {
        showToast(
          t(
            "history.enrichedMetadataToast",
            'Enriched metadata for "{title}"',
            { title: item.title },
          ),
          "success",
        );
        if (selectedDetailItem?.id === item.id) {
          setSelectedDetailItem(updated);
        }
      },
      onError: (err) => {
        showToast(
          t(
            "history.couldNotEnrichMetadata",
            "Could not enrich metadata: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleEnrichAll = () => {
    enrichAllMutation.mutate(undefined, {
      onSuccess: () => {
        showToast(
          t(
            "history.startedMetadataEnrichment",
            "Started metadata enrichment from connected Arr instances",
          ),
          "info",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToStartEnrichment",
            "Failed to start enrichment: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleReconcile = () => {
    reconcileMutation.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "history.reconciledLibraryToast",
            "Reconciled library and enriched metadata ({count} processed)",
            { count: res.processedCount },
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToReconcileLibrary",
            "Failed to reconcile library: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleClearAll = async () => {
    const ok = await confirm({
      title: t("history.clearDownloadHistory", "Clear Download History"),
      message: t(
        "history.clearDownloadHistoryConfirm",
        "Are you sure you want to clear all download history? This action cannot be undone.",
      ),
      danger: true,
      confirmText: t("common.clearAll", "Clear All"),
    });
    if (!ok) return;

    clearMutation.mutate(undefined, {
      onSuccess: () => {
        setSelectedDetailItem(null);
        showToast(
          t(
            "history.historyClearedSuccess",
            "Download history cleared successfully",
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToClearHistory",
            "Failed to clear history: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const totalCount = history?.length || 0;

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
        boxSizing: "border-box",
      }}
    >
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexWrap: "wrap",
          gap: "0.75rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <h1 className="page-heading" style={{ margin: 0 }}>
            {t("history.title")} ({totalCount})
          </h1>
        </div>

        <div
          className="page-header-actions"
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {/* View mode toggle */}
          <div className="view-toggle">
            <button
              className={`view-toggle-btn ${viewMode === "grid" ? "active" : ""}`}
              onClick={() => setViewMode("grid")}
              title={t("history.postersView")}
            >
              {t("history.postersView")}
            </button>
            <button
              className={`view-toggle-btn ${viewMode === "table" ? "active" : ""}`}
              onClick={() => setViewMode("table")}
              title={t("history.tableView")}
            >
              {t("history.tableView")}
            </button>
          </div>

          <button
            className="btn btn-success"
            onClick={handleReconcile}
            disabled={reconcileMutation.isPending}
            title={t("history.scanActive")}
          >
            {reconcileMutation.isPending
              ? t("common.loading")
              : "🔄 " + t("history.syncArrMetadata")}
          </button>

          <button
            className="btn btn-outline"
            onClick={handleEnrichAll}
            disabled={enrichAllMutation.isPending || totalCount === 0}
            title={t("history.syncMetadata")}
          >
            {t("history.syncArrMetadata")}
          </button>

          <button
            className="btn btn-outline"
            onClick={handleClearAll}
            disabled={clearMutation.isPending || totalCount === 0}
            title={t("history.clearAll")}
          >
            {t("history.clearHistory")}
          </button>
        </div>
      </div>

      {/* Filter and search toolbar */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.25rem",
          padding: "0.75rem 1rem",
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          flexShrink: 0,
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.4rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {(["all", "Active", "Completed", "Removed"] as const).map((st) => (
            <button
              key={st}
              className={`btn ${statusFilter === st ? "btn-primary" : "btn-outline"}`}
              style={{
                fontSize: "0.82rem",
                padding: "0.35rem 0.85rem",
                borderRadius: "6px",
                fontWeight: 500,
              }}
              onClick={() => setStatusFilter(st)}
            >
              {st === "all"
                ? t("common.all", "All")
                : t("torrentStatus." + st.toLowerCase(), st)}
            </button>
          ))}
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            minWidth: "260px",
            flex: "1",
            maxWidth: "450px",
          }}
        >
          <input
            type="text"
            className="form-control"
            placeholder={t("history.filterPlaceholder")}
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            style={{
              width: "100%",
              padding: "0.4rem 0.75rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary)",
              color: "inherit",
              fontSize: "0.85rem",
            }}
          />
          {searchTerm && (
            <button
              className="btn btn-outline"
              onClick={() => setSearchTerm("")}
              style={{
                fontSize: "0.75rem",
                padding: "0.35rem 0.5rem",
                borderRadius: "6px",
              }}
              title={t("history.clearSearchFilter")}
            >
              ✕
            </button>
          )}
        </div>
      </div>

      {/* Loading & Error States */}
      {isLoading && (
        <div
          className="card"
          style={{ padding: "3rem", textAlign: "center", borderRadius: "8px" }}
        >
          <div className="loading">{t("history.loading")}</div>
        </div>
      )}

      {isError && (
        <div
          className="card"
          style={{
            padding: "2rem",
            textAlign: "center",
            color: "var(--danger, #dc3545)",
            borderRadius: "8px",
          }}
        >
          {t("history.failedToLoad")}
        </div>
      )}

      {!isLoading && !isError && totalCount === 0 && (
        <div
          className="card empty-state"
          style={{
            padding: "3.5rem 1rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            className="empty-state-title"
            style={{
              fontSize: "1.25rem",
              fontWeight: 600,
              marginBottom: "0.5rem",
            }}
          >
            {t("history.noHistoricalDownloads")}
          </div>
          <div
            className="empty-state-text"
            style={{
              color: "var(--text-muted, #888)",
              maxWidth: "500px",
              margin: "0 auto",
            }}
          >
            {searchTerm || statusFilter !== "all"
              ? t("history.noMatchingRecords")
              : t("history.noHistoricalDownloadsDesc")}
          </div>
        </div>
      )}

      {/* POSTER GRID VIEW (Sonarr / Radarr Style) */}
      {!isLoading && !isError && totalCount > 0 && viewMode === "grid" && (
        <div
          style={{
            flex: "1 1 0%",
            minHeight: 0,
            height: "100%",
            width: "100%",
            overflowY: "auto",
            overflowX: "hidden",
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
            gridAutoRows: "max-content",
            alignContent: "start",
            gap: "1.25rem",
            paddingRight: "0.25rem",
            paddingBottom: "1rem",
          }}
        >
          {history?.map((item) => {
            const meta = item.metadata;
            const displayTitle = meta?.title || item.title;
            const posterSrc =
              meta?.posterUrl ||
              (item.torrentId
                ? `/api/v1/media/artwork/${item.torrentId}/poster`
                : "");
            const hasPoster = Boolean(posterSrc);
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
                onClick={() => setSelectedDetailItem(item)}
              >
                {/* Poster Artwork Box */}
                <div
                  style={{
                    position: "relative",
                    width: "100%",
                    aspectRatio: "2 / 3",
                    backgroundColor: "#141414",
                    overflow: "hidden",
                    flexShrink: 0,
                  }}
                >
                  {hasPoster ? (
                    <img
                      src={posterSrc}
                      alt={displayTitle}
                      style={{
                        position: "absolute",
                        top: 0,
                        left: 0,
                        width: "100%",
                        height: "100%",
                        objectFit: "cover",
                      }}
                      loading="lazy"
                      onError={(e) => {
                        (e.target as HTMLElement).style.display = "none";
                      }}
                    />
                  ) : (
                    <div
                      style={{
                        position: "absolute",
                        top: 0,
                        left: 0,
                        width: "100%",
                        height: "100%",
                        display: "flex",
                        flexDirection: "column",
                        alignItems: "center",
                        justifyContent: "center",
                        padding: "1rem",
                        textAlign: "center",
                        background:
                          "linear-gradient(180deg, #2a2620 0%, #151412 100%)",
                      }}
                    >
                      <span
                        style={{ fontSize: "2.5rem", marginBottom: "0.5rem" }}
                      >
                        {item.source === "Radarr"
                          ? "🎬"
                          : item.source === "Sonarr"
                            ? "📺"
                            : item.source === "Lidarr"
                              ? "🎵"
                              : "📦"}
                      </span>
                      <div
                        style={{
                          fontSize: "0.82rem",
                          fontWeight: 600,
                          wordBreak: "break-word",
                          color: "var(--text-secondary)",
                          lineHeight: "1.25",
                        }}
                      >
                        {displayTitle}
                      </div>
                    </div>
                  )}

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
                              setSearchTerm(g);
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
                      onClick={() => setSearchModalQuery(item.title)}
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
                      onClick={() => handleReAdd(item.id, item.title)}
                      disabled={
                        reAddMutation.isPending || item.status === "Active"
                      }
                      title={
                        item.status === "Active"
                          ? t("history.alreadyInLibrary", "Already in library")
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
                      onClick={() => setSelectedDetailItem(item)}
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
      )}

      {/* DETAILED TABLE VIEW */}
      {!isLoading && !isError && totalCount > 0 && viewMode === "table" && (
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
                  <th style={{ padding: "0.75rem 1rem" }}>
                    {t("history.releaseMedia")}
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "100px" }}>
                    {t("history.size")}
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "120px" }}>
                    {t("history.uploaded")}
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "90px" }}>
                    {t("history.ratio")}
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "100px" }}>
                    {t("history.seedTime")}
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                    {t("history.dateAdded")}
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "100px" }}>
                    {t("history.status")}
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
                {history?.map((item) => {
                  const meta = item.metadata;
                  const displayTitle = meta?.title || item.title;
                  const arrLink = getMediaDeepLink(item, arrConnections);

                  return (
                    <tr
                      key={item.id}
                      style={{
                        borderBottom: "1px solid var(--border-color, #222)",
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
                          {meta?.posterUrl || item.torrentId ? (
                            <img
                              src={
                                meta?.posterUrl ||
                                `/api/v1/media/artwork/${item.torrentId}/poster`
                              }
                              alt=""
                              style={{
                                width: "38px",
                                height: "54px",
                                objectFit: "cover",
                                borderRadius: "4px",
                                flexShrink: 0,
                                cursor: "pointer",
                              }}
                              onClick={() => setSelectedDetailItem(item)}
                              loading="lazy"
                              onError={(e) => {
                                (e.target as HTMLElement).style.display =
                                  "none";
                              }}
                            />
                          ) : (
                            <div
                              style={{
                                width: "38px",
                                height: "54px",
                                backgroundColor: "#222",
                                borderRadius: "4px",
                                display: "flex",
                                alignItems: "center",
                                justifyContent: "center",
                                fontSize: "1.2rem",
                                flexShrink: 0,
                                cursor: "pointer",
                              }}
                              onClick={() => setSelectedDetailItem(item)}
                            >
                              🎬
                            </div>
                          )}

                          <div style={{ flex: 1, minWidth: 0 }}>
                            <div
                              style={{
                                fontWeight: 600,
                                wordBreak: "break-word",
                                cursor: "pointer",
                              }}
                              onClick={() => setSelectedDetailItem(item)}
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
                                    setSearchTerm(item.primaryTracker || "")
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
                            onClick={() => setSelectedDetailItem(item)}
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
                            onClick={() => setSearchModalQuery(item.title)}
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
                            onClick={() => handleReAdd(item.id, item.title)}
                            disabled={
                              reAddMutation.isPending ||
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
                            onClick={() => handleDelete(item.id, item.title)}
                            title={t("history.deleteHistoricalRecord")}
                          >
                            ✕
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* RICH MEDIA DETAILS MODAL WITH DEEP-LINK INTEGRATIONS */}
      {selectedDetailItem && (
        <div
          className="modal-overlay"
          onClick={() => setSelectedDetailItem(null)}
        >
          <div
            className="modal-content"
            style={{
              maxWidth: "860px",
              width: "95%",
              padding: 0,
              overflow: "hidden",
              borderRadius: "10px",
              backgroundColor: "var(--bg-card, #171b35)",
            }}
            onClick={(e) => e.stopPropagation()}
          >
            {/* Fanart Backdrop Header */}
            <div
              style={{
                position: "relative",
                height: "230px",
                backgroundImage:
                  selectedDetailItem.metadata?.backdropUrl ||
                  selectedDetailItem.metadata?.fanartUrl
                    ? `url(${selectedDetailItem.metadata.backdropUrl || selectedDetailItem.metadata.fanartUrl})`
                    : undefined,
                backgroundSize: "cover",
                backgroundPosition: "center",
                backgroundColor: "#111",
                display: "flex",
                alignItems: "flex-end",
                padding: "1.5rem",
              }}
            >
              <div
                style={{
                  position: "absolute",
                  inset: 0,
                  background:
                    "linear-gradient(180deg, rgba(0,0,0,0.35) 0%, rgba(23,27,53,0.96) 100%)",
                }}
              />

              <button
                type="button"
                className="btn btn-outline"
                style={{
                  position: "absolute",
                  top: "1rem",
                  right: "1rem",
                  zIndex: 10,
                  backgroundColor: "rgba(0,0,0,0.6)",
                  border: "none",
                  color: "#fff",
                  padding: "0.25rem 0.6rem",
                  fontSize: "1rem",
                }}
                onClick={() => setSelectedDetailItem(null)}
              >
                ✕
              </button>

              <div
                style={{
                  position: "relative",
                  zIndex: 2,
                  display: "flex",
                  gap: "1.5rem",
                  alignItems: "flex-end",
                  width: "100%",
                }}
              >
                {(selectedDetailItem.metadata?.posterUrl ||
                  selectedDetailItem.torrentId) && (
                  <img
                    src={
                      selectedDetailItem.metadata?.posterUrl ||
                      `/api/v1/media/artwork/${selectedDetailItem.torrentId}/poster`
                    }
                    alt=""
                    style={{
                      width: "110px",
                      height: "160px",
                      objectFit: "cover",
                      borderRadius: "6px",
                      boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
                      border: "1px solid rgba(255,255,255,0.15)",
                      marginBottom: "-1.5rem",
                    }}
                    onError={(e) => {
                      (e.target as HTMLElement).style.display = "none";
                    }}
                  />
                )}

                <div style={{ flex: 1, minWidth: 0 }}>
                  <h2
                    style={{
                      margin: "0 0 0.35rem 0",
                      fontSize: "1.55rem",
                      fontWeight: 700,
                      wordBreak: "break-word",
                    }}
                  >
                    {selectedDetailItem.metadata?.title ||
                      selectedDetailItem.title}
                    {selectedDetailItem.metadata?.year && (
                      <span
                        style={{
                          color: "var(--text-muted, #aaa)",
                          fontWeight: 400,
                          fontSize: "1.1rem",
                          marginLeft: "0.5rem",
                        }}
                      >
                        ({selectedDetailItem.metadata.year})
                      </span>
                    )}
                  </h2>

                  {/* Arr & External Database Links Bar */}
                  <div
                    style={{
                      display: "flex",
                      gap: "0.5rem",
                      alignItems: "center",
                      flexWrap: "wrap",
                    }}
                  >
                    {(() => {
                      const arrLink = getMediaDeepLink(
                        selectedDetailItem,
                        arrConnections,
                      );
                      if (arrLink) {
                        return (
                          <a
                            href={arrLink.url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="btn btn-primary"
                            style={{
                              fontSize: "0.8rem",
                              padding: "0.25rem 0.65rem",
                              textDecoration: "none",
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.3rem",
                            }}
                            title={`Open in ${arrLink.appName} (${arrLink.url})`}
                          >
                            🔗 {arrLink.label} ↗
                          </a>
                        );
                      }
                      if (selectedDetailItem.source) {
                        return (
                          <span className="badge badge-primary">
                            {selectedDetailItem.source}
                          </span>
                        );
                      }
                      return null;
                    })()}

                    {/* IMDb link */}
                    <a
                      href={getImdbUrl(
                        selectedDetailItem.metadata?.imdbId,
                        selectedDetailItem.metadata?.title ||
                          selectedDetailItem.title,
                      )}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="badge"
                      style={{
                        backgroundColor: "#f5c518",
                        color: "#000",
                        fontWeight: 700,
                        textDecoration: "none",
                        fontSize: "0.75rem",
                        padding: "0.25rem 0.5rem",
                      }}
                      title={t("history.viewOnImdb")}
                    >
                      {t("history.imdb")}
                    </a>

                    {/* TMDb link */}
                    {selectedDetailItem.metadata?.tmdbId && (
                      <a
                        href={
                          getTmdbUrl(
                            selectedDetailItem.metadata.tmdbId,
                            selectedDetailItem.metadata.mediaType,
                          ) || "#"
                        }
                        target="_blank"
                        rel="noopener noreferrer"
                        className="badge"
                        style={{
                          backgroundColor: "#01b4e4",
                          color: "#fff",
                          fontWeight: 700,
                          textDecoration: "none",
                          fontSize: "0.75rem",
                          padding: "0.25rem 0.5rem",
                        }}
                        title={t("history.viewOnTmdb")}
                      >
                        {t("history.tmdb")}
                      </a>
                    )}

                    {/* TheTVDB link */}
                    {selectedDetailItem.metadata?.tvdbId && (
                      <a
                        href={
                          getTvdbUrl(selectedDetailItem.metadata.tvdbId) || "#"
                        }
                        target="_blank"
                        rel="noopener noreferrer"
                        className="badge"
                        style={{
                          backgroundColor: "#228b22",
                          color: "#fff",
                          fontWeight: 700,
                          textDecoration: "none",
                          fontSize: "0.75rem",
                          padding: "0.25rem 0.5rem",
                        }}
                        title={t("history.viewOnThetvdb")}
                      >
                        {t("history.thetvdb")}
                      </a>
                    )}

                    {/* Prowlarr Deep Link if configured */}
                    {getProwlarrUrl(
                      indexers,
                      selectedDetailItem.metadata?.title ||
                        selectedDetailItem.title,
                    ) && (
                      <a
                        href={
                          getProwlarrUrl(
                            indexers,
                            selectedDetailItem.metadata?.title ||
                              selectedDetailItem.title,
                          ) || "#"
                        }
                        target="_blank"
                        rel="noopener noreferrer"
                        className="badge"
                        style={{
                          backgroundColor: "#f38020",
                          color: "#fff",
                          fontWeight: 700,
                          textDecoration: "none",
                          fontSize: "0.75rem",
                          padding: "0.25rem 0.5rem",
                        }}
                        title={t("history.searchTitleOnProwlarr")}
                      >
                        {t("history.prowlarr")}
                      </a>
                    )}
                  </div>
                </div>
              </div>
            </div>

            {/* Modal Body Content */}
            <div
              style={{
                padding: "2rem 1.5rem 1.5rem 1.5rem",
                display: "flex",
                flexDirection: "column",
                gap: "1.25rem",
              }}
            >
              {/* Studio & Rating Bar */}
              <div
                style={{
                  display: "flex",
                  gap: "1rem",
                  alignItems: "center",
                  flexWrap: "wrap",
                  fontSize: "0.85rem",
                  color: "var(--text-muted, #aaa)",
                }}
              >
                {selectedDetailItem.metadata?.studioOrNetwork && (
                  <div>
                    🏢{" "}
                    <strong style={{ color: "var(--text-primary)" }}>
                      {selectedDetailItem.metadata.studioOrNetwork}
                    </strong>
                  </div>
                )}
                {selectedDetailItem.metadata?.rating && (
                  <div>
                    ⭐{" "}
                    <strong style={{ color: "var(--text-primary)" }}>
                      {selectedDetailItem.metadata.rating.toFixed(1)} / 10
                    </strong>
                  </div>
                )}
                {(() => {
                  const genresList = normalizeGenres(
                    selectedDetailItem.metadata?.genres,
                  );
                  return genresList.length > 0 ? (
                    <div
                      style={{
                        display: "flex",
                        gap: "0.35rem",
                        flexWrap: "wrap",
                      }}
                    >
                      {genresList.map((g, i) => (
                        <span
                          key={i}
                          className="badge badge-secondary"
                          style={{
                            fontSize: "0.7rem",
                            padding: "0.15rem 0.45rem",
                            backgroundColor: "rgba(255,255,255,0.08)",
                            color: "var(--text-primary)",
                            borderRadius: "4px",
                          }}
                        >
                          {g}
                        </span>
                      ))}
                    </div>
                  ) : null;
                })()}
              </div>

              {/* Synopsis / Overview */}
              {selectedDetailItem.metadata?.overview && (
                <div>
                  <h4
                    style={{
                      margin: "0 0 0.4rem 0",
                      fontSize: "0.9rem",
                      color: "var(--text-muted, #aaa)",
                      textTransform: "uppercase",
                      letterSpacing: "0.5px",
                    }}
                  >
                    {t("history.overview")}
                  </h4>
                  <p
                    style={{
                      margin: 0,
                      lineHeight: "1.55",
                      fontSize: "0.92rem",
                      color: "var(--text-secondary)",
                    }}
                  >
                    {selectedDetailItem.metadata.overview}
                  </p>
                </div>
              )}

              {/* Cast & Actors with Headshots */}
              {selectedDetailItem.metadata?.actors &&
                selectedDetailItem.metadata.actors.length > 0 && (
                  <div>
                    <h4
                      style={{
                        margin: "0 0 0.6rem 0",
                        fontSize: "0.9rem",
                        color: "var(--text-muted, #aaa)",
                        textTransform: "uppercase",
                        letterSpacing: "0.5px",
                      }}
                    >
                      {t("history.castCharacters")}
                    </h4>
                    <div
                      style={{
                        display: "flex",
                        gap: "0.85rem",
                        overflowX: "auto",
                        paddingBottom: "0.6rem",
                      }}
                    >
                      {selectedDetailItem.metadata.actors.map((act, i) => (
                        <a
                          key={i}
                          href={getActorSearchUrl(act.name)}
                          target="_blank"
                          rel="noopener noreferrer"
                          style={{
                            display: "flex",
                            flexDirection: "column",
                            alignItems: "center",
                            width: "78px",
                            flexShrink: 0,
                            textDecoration: "none",
                            color: "inherit",
                          }}
                          title={`Search ${act.name} on TMDb`}
                        >
                          {act.imageUrl ? (
                            <img
                              src={act.imageUrl}
                              alt={act.name}
                              style={{
                                width: "60px",
                                height: "60px",
                                borderRadius: "50%",
                                objectFit: "cover",
                                border: "1px solid rgba(255,255,255,0.15)",
                                marginBottom: "0.35rem",
                              }}
                              loading="lazy"
                            />
                          ) : (
                            <div
                              style={{
                                width: "60px",
                                height: "60px",
                                borderRadius: "50%",
                                backgroundColor: "rgba(255,255,255,0.08)",
                                display: "flex",
                                alignItems: "center",
                                justifyContent: "center",
                                fontSize: "1.2rem",
                                marginBottom: "0.35rem",
                              }}
                            >
                              👤
                            </div>
                          )}
                          <div
                            style={{
                              fontSize: "0.72rem",
                              fontWeight: 600,
                              textAlign: "center",
                              whiteSpace: "nowrap",
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              width: "100%",
                            }}
                          >
                            {act.name}
                          </div>
                          {act.character && (
                            <div
                              style={{
                                fontSize: "0.65rem",
                                color: "var(--text-muted, #888)",
                                textAlign: "center",
                                whiteSpace: "nowrap",
                                overflow: "hidden",
                                textOverflow: "ellipsis",
                                width: "100%",
                              }}
                            >
                              {act.character}
                            </div>
                          )}
                        </a>
                      ))}
                    </div>
                  </div>
                )}

              {/* Technical Download Telemetry Grid */}
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(170px, 1fr))",
                  gap: "0.85rem",
                  padding: "1rem",
                  backgroundColor: "rgba(0,0,0,0.25)",
                  borderRadius: "8px",
                  border: "1px solid var(--border-light)",
                }}
              >
                <div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #888)",
                    }}
                  >
                    {t("history.infoHash")}
                  </div>
                  <code
                    style={{
                      fontSize: "0.75rem",
                      wordBreak: "break-all",
                      display: "block",
                    }}
                  >
                    {selectedDetailItem.infoHash}
                  </code>
                </div>

                <div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #888)",
                    }}
                  >
                    {t("history.ratio")}
                  </div>
                  <div
                    style={{
                      fontSize: "1.1rem",
                      fontWeight: 700,
                      color:
                        selectedDetailItem.ratio >= 1.0
                          ? "var(--success)"
                          : "inherit",
                    }}
                  >
                    {formatRatio(selectedDetailItem.ratio)}
                  </div>
                </div>

                <div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #888)",
                    }}
                  >
                    {t("history.uploaded")}
                  </div>
                  <div style={{ fontSize: "1.1rem", fontWeight: 700 }}>
                    {formatBytes(selectedDetailItem.uploaded)}
                  </div>
                </div>

                <div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #888)",
                    }}
                  >
                    {t("history.size")}
                  </div>
                  <div style={{ fontSize: "1.1rem", fontWeight: 700 }}>
                    {formatBytes(selectedDetailItem.totalSize)}
                  </div>
                </div>

                <div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #888)",
                    }}
                  >
                    {t("history.seedTime")}
                  </div>
                  <div style={{ fontSize: "1.1rem", fontWeight: 700 }}>
                    {formatDuration(selectedDetailItem.seedingTime)}
                  </div>
                </div>

                <div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #888)",
                    }}
                  >
                    {t("history.filterByTracker")}
                  </div>
                  <div
                    style={{
                      fontSize: "0.85rem",
                      wordBreak: "break-all",
                      cursor: selectedDetailItem.primaryTracker
                        ? "pointer"
                        : "default",
                    }}
                    onClick={() => {
                      if (selectedDetailItem.primaryTracker) {
                        setSearchTerm(selectedDetailItem.primaryTracker);
                        setSelectedDetailItem(null);
                      }
                    }}
                    title={
                      selectedDetailItem.primaryTracker
                        ? t(
                            "history.filterByTracker",
                            "Click to filter by tracker",
                          )
                        : undefined
                    }
                  >
                    {selectedDetailItem.primaryTracker ||
                      t("common.none", "None")}
                  </div>
                </div>

                <div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #888)",
                    }}
                  >
                    {t("history.dateAdded")}
                  </div>
                  <div style={{ fontSize: "0.85rem" }}>
                    {formatDate(selectedDetailItem.dateAdded)}
                  </div>
                </div>
              </div>

              {/* Modal Actions */}
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: "0.5rem",
                }}
              >
                <button
                  className="btn btn-outline"
                  onClick={() => handleEnrich(selectedDetailItem)}
                  disabled={enrichMutation.isPending}
                  title={t("history.syncMetadata")}
                  style={{ fontSize: "0.85rem" }}
                >
                  {t("history.syncArrMetadata")}
                </button>

                <div style={{ display: "flex", gap: "0.5rem" }}>
                  <button
                    className="btn btn-outline"
                    onClick={() => {
                      setSearchModalQuery(selectedDetailItem.title);
                      setSelectedDetailItem(null);
                    }}
                  >
                    {t("history.search")}
                  </button>
                  <button
                    className="btn btn-primary"
                    onClick={() =>
                      handleReAdd(
                        selectedDetailItem.id,
                        selectedDetailItem.title,
                      )
                    }
                    disabled={
                      reAddMutation.isPending ||
                      selectedDetailItem.status === "Active"
                    }
                  >
                    {t("history.reAdd")}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Indexer Search Modal */}
      {searchModalQuery && (
        <IndexerSearchModal
          initialQuery={searchModalQuery}
          onClose={() => setSearchModalQuery(null)}
          onTorrentAdded={() => {
            setSearchModalQuery(null);
            showToast(
              t(
                "history.torrentAddedSuccess",
                "Torrent added to download queue",
              ),
              "success",
            );
          }}
        />
      )}
    </div>
  );
}
