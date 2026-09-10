import React from "react";
import { useTranslation } from "../../i18n";
import { useEscapeKey } from "../../hooks/useEscapeKey";
import {
  formatBytes,
  formatRatio,
  formatDate,
  normalizeGenres,
} from "../../utils/formatters";
import {
  getMediaDeepLink,
  getImdbUrl,
  getTmdbUrl,
  getTvdbUrl,
  getActorSearchUrl,
  getProwlarrUrl,
} from "../../utils/arrLinks";
import { MediaArtworkImage } from "../../components/common/MediaArtworkImage";
import type {
  DownloadHistoryEntry,
  ArrConnection,
  IndexerDefinition,
} from "../../api/types";
import { formatDuration } from "./types";

export interface HistoryDetailModalProps {
  item: DownloadHistoryEntry | null;
  onClose: () => void;
  arrConnections?: ArrConnection[] | null;
  indexers?: IndexerDefinition[] | null;
  onEnrich: (item: DownloadHistoryEntry) => void;
  isEnriching: boolean;
  onSearch: (title: string) => void;
  onReAdd: (id: number, title: string) => void;
  isReAdding: boolean;
  onFilterByTracker: (tracker: string) => void;
}

export const HistoryDetailModal: React.FC<HistoryDetailModalProps> = ({
  item,
  onClose,
  arrConnections,
  indexers,
  onEnrich,
  isEnriching,
  onSearch,
  onReAdd,
  isReAdding,
  onFilterByTracker,
}) => {
  const { t } = useTranslation();

  useEscapeKey(onClose, Boolean(item));

  if (!item) return null;

  const meta = item.metadata;
  const displayTitle = meta?.title || item.title;
  const arrLink = getMediaDeepLink(item, arrConnections);
  const prowlarrLink = getProwlarrUrl(indexers, displayTitle);
  const genresList = normalizeGenres(meta?.genres);

  return (
    <div
      className="modal-overlay"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-labelledby="history-detail-modal-title"
    >
      <div
        className="modal-content"
        style={{
          maxWidth: "860px",
          width: "95%",
          padding: 0,
          overflow: "hidden",
          borderRadius: "10px",
          backgroundColor: "var(--bg-card)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Fanart Backdrop Header */}
        <div
          style={{
            position: "relative",
            height: "230px",
            backgroundImage:
              meta?.backdropUrl || meta?.fanartUrl
                ? `url(${meta.backdropUrl || meta.fanartUrl})`
                : undefined,
            backgroundSize: "cover",
            backgroundPosition: "center",
            backgroundColor: "var(--bg-primary)",
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
            onClick={onClose}
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
            <MediaArtworkImage
              src={
                meta?.posterUrl ||
                (item.torrentId
                  ? `/api/v1/media/artwork/${item.torrentId}/poster`
                  : "")
              }
              alt={displayTitle}
              width={110}
              height={160}
              borderRadius="6px"
              fallbackIcon={
                item.source === "Radarr"
                  ? "🎬"
                  : item.source === "Sonarr"
                    ? "📺"
                    : item.source === "Lidarr"
                      ? "🎵"
                      : "📦"
              }
              style={{
                flexShrink: 0,
                boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
                border: "1px solid var(--border)",
                marginBottom: "-1.5rem",
              }}
            />

            <div style={{ flex: 1, minWidth: 0 }}>
              <h2
                id="history-detail-modal-title"
                style={{
                  margin: "0 0 0.35rem 0",
                  fontSize: "1.55rem",
                  fontWeight: 700,
                  wordBreak: "break-word",
                }}
              >
                {displayTitle}
                {meta?.year && (
                  <span
                    style={{
                      color: "var(--text-muted)",
                      fontWeight: 400,
                      fontSize: "1.1rem",
                      marginLeft: "0.5rem",
                    }}
                  >
                    ({meta.year})
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
                {arrLink ? (
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
                ) : item.source ? (
                  <span className="badge badge-primary">{item.source}</span>
                ) : null}

                {/* IMDb link */}
                <a
                  href={getImdbUrl(meta?.imdbId, displayTitle)}
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
                {meta?.tmdbId && (
                  <a
                    href={getTmdbUrl(meta.tmdbId, meta.mediaType) || "#"}
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
                {meta?.tvdbId && (
                  <a
                    href={getTvdbUrl(meta.tvdbId) || "#"}
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
                {prowlarrLink && (
                  <a
                    href={prowlarrLink}
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
            {meta?.studioOrNetwork && (
              <div>
                🏢{" "}
                <strong style={{ color: "var(--text-primary)" }}>
                  {meta.studioOrNetwork}
                </strong>
              </div>
            )}
            {meta?.rating && (
              <div>
                ⭐{" "}
                <strong style={{ color: "var(--text-primary)" }}>
                  {meta.rating.toFixed(1)} / 10
                </strong>
              </div>
            )}
            {genresList.length > 0 && (
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
            )}
          </div>

          {/* Synopsis / Overview */}
          {meta?.overview && (
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
                {meta.overview}
              </p>
            </div>
          )}

          {/* Cast & Actors with Headshots */}
          {meta?.actors && meta.actors.length > 0 && (
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
                {meta.actors.map((act, i) => (
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
                {item.infoHash}
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
                  color: item.ratio >= 1.0 ? "var(--success)" : "inherit",
                }}
              >
                {formatRatio(item.ratio)}
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
                {formatBytes(item.uploaded)}
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
                {formatBytes(item.totalSize)}
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
                {formatDuration(item.seedingTime)}
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
                  cursor: item.primaryTracker ? "pointer" : "default",
                }}
                onClick={() => {
                  if (item.primaryTracker) {
                    onFilterByTracker(item.primaryTracker);
                    onClose();
                  }
                }}
                title={
                  item.primaryTracker
                    ? t("history.filterByTracker", "Click to filter by tracker")
                    : undefined
                }
              >
                {item.primaryTracker || t("common.none", "None")}
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
                {formatDate(item.dateAdded)}
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
              onClick={() => onEnrich(item)}
              disabled={isEnriching}
              title={t("history.syncMetadata")}
              style={{ fontSize: "0.85rem" }}
            >
              {t("history.syncArrMetadata")}
            </button>

            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button
                className="btn btn-outline"
                onClick={() => {
                  onSearch(item.title);
                  onClose();
                }}
              >
                {t("history.search")}
              </button>
              <button
                className="btn btn-primary"
                onClick={() => onReAdd(item.id, item.title)}
                disabled={isReAdding || item.status === "Active"}
              >
                {t("history.reAdd")}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
