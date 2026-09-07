import { useState, useMemo } from "react";
import { useTrackerBoostMatrix, useTorrents } from "../../api/hooks";
import TrackerFavicon from "../../components/TrackerFavicon";
import { useTranslation } from "../../i18n";
import type { TorrentMetaMap } from "./types";

export interface MatrixViewProps {
  torrentMetaMap: TorrentMetaMap;
  onInspectTorrent?: (infoHash: string) => void;
}

export function MatrixView({
  torrentMetaMap,
  onInspectTorrent,
}: MatrixViewProps) {
  const { t } = useTranslation();
  const { data: matrixData, isLoading: matrixLoading } =
    useTrackerBoostMatrix();
  const { data: torrents } = useTorrents();

  const [matrixViewMode, setMatrixViewMode] = useState<
    "by_torrent" | "by_tracker"
  >("by_torrent");
  const [matrixLayoutMode, setMatrixLayoutMode] = useState<"grid" | "table">(
    "grid",
  );
  const [matrixSearch, setMatrixSearch] = useState("");

  const filteredMatrixTorrents = useMemo(() => {
    return (matrixData?.torrents ?? []).filter((t) => {
      if (!matrixSearch.trim()) return true;
      const q = matrixSearch.toLowerCase();
      const meta = torrentMetaMap.get((t.infoHash || "").toLowerCase());
      return (
        (t.torrentName || "").toLowerCase().includes(q) ||
        (meta?.mediaTitle && meta.mediaTitle.toLowerCase().includes(q)) ||
        (t.infoHash || "").toLowerCase().includes(q) ||
        (t.trackers || []).some((tr) =>
          (tr.trackerHost || tr.trackerUrl || "").toLowerCase().includes(q),
        )
      );
    });
  }, [matrixData?.torrents, matrixSearch, torrentMetaMap]);

  const filteredMatrixTrackers = useMemo(() => {
    return (matrixData?.trackers ?? []).filter((tr) => {
      if (!matrixSearch.trim()) return true;
      const q = matrixSearch.toLowerCase();
      return (
        (tr.trackerUrl || "").toLowerCase().includes(q) ||
        (tr.host || "").toLowerCase().includes(q) ||
        (tr.registeredTorrentNames || []).some((n) =>
          (n || "").toLowerCase().includes(q),
        )
      );
    });
  }, [matrixData?.trackers, matrixSearch]);

  return (
    <div
      className="card"
      style={{
        padding: "1.25rem",
        flex: "1 1 auto",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        marginBottom: "0.5rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.25rem",
        }}
      >
        <div>
          <h2 style={{ fontSize: "1.1rem", margin: "0 0 0.25rem 0" }}>
            {t("trackerBoost.matrix.title", "Swarm Cross-Matrix Explorer")}
          </h2>
          <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
            {t(
              "trackerBoost.matrix.subtitle",
              "Bi-directional mapping between library torrents and verified BitTorrent tracker endpoints",
            )}
          </div>
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.75rem",
            flexWrap: "wrap",
          }}
        >
          <input
            type="text"
            className="form-control"
            style={{
              width: "240px",
              padding: "0.35rem 0.75rem",
              fontSize: "0.85rem",
            }}
            placeholder={t(
              "trackerBoost.searchTorrentsOrTrackers",
              "Search torrents or trackers...",
            )}
            value={matrixSearch}
            onChange={(e) => setMatrixSearch(e.target.value)}
          />

          <div className="view-toggle">
            <button
              className={`view-toggle-btn ${matrixViewMode === "by_torrent" ? "active" : ""}`}
              onClick={() => setMatrixViewMode("by_torrent")}
              title={t(
                "trackerBoost.groupByTorrent",
                "Group swarms by torrent download",
              )}
            >
              {t(
                "trackerBoost.matrix.torrentsToTrackers",
                "Torrents → Trackers",
              )}
            </button>
            <button
              className={`view-toggle-btn ${matrixViewMode === "by_tracker" ? "active" : ""}`}
              onClick={() => setMatrixViewMode("by_tracker")}
              title={t(
                "trackerBoost.groupByTracker",
                "Group swarms by tracker endpoint",
              )}
            >
              {t(
                "trackerBoost.matrix.trackersToTorrents",
                "Trackers → Torrents",
              )}
            </button>
          </div>

          <div className="view-toggle">
            <button
              className={`view-toggle-btn ${matrixLayoutMode === "grid" ? "active" : ""}`}
              onClick={() => setMatrixLayoutMode("grid")}
              title={t("torrents.toolbar.gridView", "Poster Card Grid View")}
            >
              {t("trackerBoost.matrix.posters", "🎬 Posters")}
            </button>
            <button
              className={`view-toggle-btn ${matrixLayoutMode === "table" ? "active" : ""}`}
              onClick={() => setMatrixLayoutMode("table")}
              title={t(
                "torrents.toolbar.tableView",
                "Detailed Table / List View",
              )}
            >
              {t("trackerBoost.matrix.table", "📑 Table")}
            </button>
          </div>
        </div>
      </div>

      {matrixLoading ? (
        <div
          style={{
            padding: "3rem",
            textAlign: "center",
            color: "var(--text-muted)",
          }}
        >
          {t(
            "trackerBoost.matrix.buildingMatrix",
            "Building swarm cross-matrix...",
          )}
        </div>
      ) : matrixViewMode === "by_torrent" ? (
        matrixLayoutMode === "grid" ? (
          /* TORRENTS GRID VIEW (POSTER CARDS) */
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(260px, 1fr))",
              alignContent: "start",
              gap: "1.25rem",
              flex: "1 1 auto",
              minHeight: 0,
              overflowY: "auto",
              paddingRight: "0.25rem",
            }}
          >
            {filteredMatrixTorrents.map((torr) => {
              const meta = torrentMetaMap.get(
                (torr.infoHash || "").toLowerCase(),
              );
              const displayTitle = meta?.mediaTitle || torr.torrentName;
              const hasPoster = Boolean(meta?.posterUrl);

              return (
                <div
                  key={torr.torrentId || torr.infoHash}
                  className="card"
                  style={{
                    padding: 0,
                    overflow: "hidden",
                    display: "flex",
                    flexDirection: "column",
                    height: "auto",
                    borderRadius: "8px",
                    border: "1px solid rgba(255, 255, 255, 0.08)",
                    backgroundColor: "var(--bg-secondary)",
                    boxShadow: "0 4px 14px rgba(0, 0, 0, 0.35)",
                    transition: "transform 0.18s ease, box-shadow 0.18s ease",
                  }}
                >
                  {/* Poster artwork container */}
                  <div
                    style={{
                      position: "relative",
                      width: "100%",
                      paddingTop: "140%",
                      backgroundColor: "#141414",
                      overflow: "hidden",
                    }}
                  >
                    {hasPoster ? (
                      <img
                        src={meta?.posterUrl || ""}
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
                          style={{
                            fontSize: "2.5rem",
                            marginBottom: "0.5rem",
                          }}
                        >
                          {meta?.source === "Radarr"
                            ? "🎬"
                            : meta?.source === "Sonarr"
                              ? "📺"
                              : meta?.source === "Lidarr"
                                ? "🎵"
                                : "📦"}
                        </span>
                        <span
                          style={{
                            fontSize: "0.82rem",
                            fontWeight: 600,
                            color: "var(--text-secondary)",
                            wordBreak: "break-word",
                          }}
                        >
                          {displayTitle}
                        </span>
                      </div>
                    )}

                    {/* Dark Gradient Overlay */}
                    <div
                      style={{
                        position: "absolute",
                        bottom: 0,
                        left: 0,
                        right: 0,
                        height: "65%",
                        background:
                          "linear-gradient(to top, rgba(15,15,15,0.95) 0%, rgba(15,15,15,0.6) 50%, transparent 100%)",
                        pointerEvents: "none",
                      }}
                    />

                    {/* Top-Left Arr Source Badge */}
                    {meta?.source && (
                      <span
                        className="badge badge-primary"
                        style={{
                          position: "absolute",
                          top: "8px",
                          left: "8px",
                          fontSize: "0.7rem",
                          padding: "0.2rem 0.5rem",
                          borderRadius: "4px",
                          backdropFilter: "blur(4px)",
                        }}
                      >
                        {meta.source}
                      </span>
                    )}

                    {/* Top-Right Privacy Badge */}
                    <span
                      className={`badge ${torr.isPrivate ? "badge-secondary" : "badge-success"}`}
                      style={{
                        position: "absolute",
                        top: "8px",
                        right: "8px",
                        fontSize: "0.7rem",
                        padding: "0.2rem 0.5rem",
                        borderRadius: "4px",
                        backdropFilter: "blur(4px)",
                      }}
                    >
                      {torr.isPrivate
                        ? t("trackerBoost.privateBadge", "🔒 Private")
                        : t("trackerBoost.publicBadge", "🌐 Public")}
                    </span>

                    {/* Bottom title & year overlay */}
                    <div
                      style={{
                        position: "absolute",
                        bottom: "8px",
                        left: "10px",
                        right: "10px",
                        color: "#fff",
                      }}
                    >
                      <div
                        style={{
                          fontWeight: 700,
                          fontSize: "0.92rem",
                          lineHeight: 1.25,
                          textShadow: "0 2px 4px rgba(0,0,0,0.8)",
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          display: "-webkit-box",
                          WebkitLineClamp: 2,
                          WebkitBoxOrient: "vertical",
                        }}
                        title={displayTitle}
                      >
                        {displayTitle}
                      </div>
                      {meta?.year && (
                        <span
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--accent, #c8a84e)",
                            fontWeight: 600,
                          }}
                        >
                          {meta.year}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Card Info & Trackers Body */}
                  <div
                    style={{
                      padding: "0.85rem",
                      display: "flex",
                      flexDirection: "column",
                      flex: 1,
                      gap: "0.6rem",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        fontSize: "0.78rem",
                      }}
                    >
                      <span
                        style={{
                          fontFamily: "monospace",
                          color: "var(--text-muted)",
                          fontSize: "0.75rem",
                        }}
                        title={torr.infoHash}
                      >
                        {torr.infoHash
                          ? `${torr.infoHash.slice(0, 10)}...`
                          : ""}
                      </span>
                      <div style={{ display: "flex", gap: "0.35rem" }}>
                        <span
                          className="badge badge-primary"
                          style={{ fontSize: "0.7rem" }}
                        >
                          {t(
                            "trackerBoost.matrix.attachedCount",
                            "{count} Attached",
                            { count: torr.attachedTrackersCount },
                          )}
                        </span>
                        {torr.verifiedTrackersCount > 0 && (
                          <span
                            className="badge badge-success"
                            style={{ fontSize: "0.7rem" }}
                          >
                            {t(
                              "trackerBoost.matrix.verifiedCount",
                              "{count} Verified",
                              { count: torr.verifiedTrackersCount },
                            )}
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Trackers list chips */}
                    <div
                      style={{
                        display: "flex",
                        flexWrap: "wrap",
                        gap: "0.35rem",
                        maxHeight: "130px",
                        overflowY: "auto",
                      }}
                    >
                      {torr.trackers.map((tr, idx) => (
                        <span
                          key={tr.trackerId || idx}
                          className={`badge ${tr.isAttached ? "badge-primary" : "badge-success"}`}
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.35rem",
                            padding: "0.25rem 0.45rem",
                            fontSize: "0.72rem",
                            fontFamily: "monospace",
                          }}
                        >
                          <TrackerFavicon
                            urlOrHost={tr.trackerHost || tr.trackerUrl}
                            size={13}
                          />
                          <span>{tr.trackerHost || tr.trackerUrl}</span>
                          {(tr.seeders > 0 || tr.leechers > 0) && (
                            <span style={{ opacity: 0.85 }}>
                              ({tr.seeders}s/{tr.leechers}l)
                            </span>
                          )}
                        </span>
                      ))}
                      {torr.trackers.length === 0 && (
                        <span
                          style={{
                            fontSize: "0.78rem",
                            color: "var(--text-muted)",
                          }}
                        >
                          {t(
                            "trackerBoost.matrix.noPositiveScrapes",
                            "No positive tracker scrapes found yet.",
                          )}
                        </span>
                      )}
                    </div>

                    <div
                      style={{
                        marginTop: "auto",
                        paddingTop: "0.5rem",
                        borderTop: "1px solid var(--border-light)",
                      }}
                    >
                      <button
                        className="btn btn-sm btn-outline"
                        style={{
                          width: "100%",
                          fontSize: "0.78rem",
                          padding: "0.3rem 0",
                        }}
                        onClick={() => {
                          if (onInspectTorrent && torr.infoHash) {
                            onInspectTorrent(torr.infoHash);
                          }
                        }}
                      >
                        {t(
                          "trackerBoost.matrix.inspectSwarm",
                          "⚡ Inspect Swarm",
                        )}
                      </button>
                    </div>
                  </div>
                </div>
              );
            })}
            {filteredMatrixTorrents.length === 0 && (
              <div
                style={{
                  gridColumn: "1 / -1",
                  padding: "3rem",
                  textAlign: "center",
                  color: "var(--text-muted)",
                }}
              >
                {t(
                  "trackerBoost.matrix.noTorrentsMatch",
                  "No torrents match the search query.",
                )}
              </div>
            )}
          </div>
        ) : (
          /* TORRENTS TABLE VIEW */
          <div
            className="torrent-table-wrapper"
            style={{
              borderRadius: "6px",
              border: "1px solid var(--border)",
              flex: "1 1 auto",
              minHeight: 0,
              overflowY: "auto",
              backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
            }}
          >
            <table
              className="torrent-table"
              style={{ width: "100%", fontSize: "0.85rem" }}
            >
              <thead
                style={{
                  position: "sticky",
                  top: 0,
                  zIndex: 2,
                  backgroundColor: "var(--bg-secondary)",
                }}
              >
                <tr>
                  <th className="torrent-table-th" style={{ width: "35%" }}>
                    {t(
                      "trackerBoost.matrix.torrentAndMedia",
                      "Torrent & Media",
                    )}
                  </th>
                  <th className="torrent-table-th" style={{ width: "12%" }}>
                    {t("trackerBoost.matrix.privacyAndHash", "Privacy & Hash")}
                  </th>
                  <th className="torrent-table-th" style={{ width: "43%" }}>
                    {t(
                      "trackerBoost.matrix.scrapedAttachedTrackers",
                      "Scraped & Attached Trackers",
                    )}
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ width: "10%", textAlign: "right" }}
                  >
                    {t("trackerBoost.matrix.actions", "Actions")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredMatrixTorrents.map((torr) => {
                  const meta = torrentMetaMap.get(
                    (torr.infoHash || "").toLowerCase(),
                  );
                  const displayTitle = meta?.mediaTitle || torr.torrentName;

                  return (
                    <tr
                      key={torr.torrentId || torr.infoHash}
                      className="torrent-table-row"
                    >
                      <td>
                        <div
                          style={{
                            display: "flex",
                            alignItems: "center",
                            gap: "0.75rem",
                          }}
                        >
                          {meta?.posterUrl ? (
                            <img
                              src={meta.posterUrl}
                              alt={displayTitle}
                              style={{
                                width: "36px",
                                height: "50px",
                                borderRadius: "4px",
                                objectFit: "cover",
                                flexShrink: 0,
                              }}
                            />
                          ) : (
                            <div
                              style={{
                                width: "36px",
                                height: "50px",
                                borderRadius: "4px",
                                backgroundColor: "var(--bg-secondary)",
                                display: "flex",
                                alignItems: "center",
                                justifyContent: "center",
                                fontSize: "1.2rem",
                                flexShrink: 0,
                              }}
                            >
                              {meta?.source === "Radarr"
                                ? "🎬"
                                : meta?.source === "Sonarr"
                                  ? "📺"
                                  : "📦"}
                            </div>
                          )}
                          <div style={{ minWidth: 0 }}>
                            <div
                              style={{
                                fontWeight: 600,
                                color: "var(--text-primary)",
                                overflow: "hidden",
                                textOverflow: "ellipsis",
                                whiteSpace: "nowrap",
                              }}
                            >
                              {displayTitle}
                            </div>
                            <div
                              style={{
                                fontSize: "0.75rem",
                                color: "var(--text-muted)",
                                display: "flex",
                                alignItems: "center",
                                gap: "0.4rem",
                                marginTop: "0.15rem",
                              }}
                            >
                              {meta?.source && (
                                <span
                                  className="badge badge-primary"
                                  style={{
                                    fontSize: "0.65rem",
                                    padding: "0.1rem 0.35rem",
                                  }}
                                >
                                  {meta.source}
                                </span>
                              )}
                              {meta?.year && <span>({meta.year})</span>}
                              <span
                                style={{
                                  overflow: "hidden",
                                  textOverflow: "ellipsis",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {torr.torrentName}
                              </span>
                            </div>
                          </div>
                        </div>
                      </td>
                      <td>
                        <div>
                          <span
                            className={`badge ${torr.isPrivate ? "badge-secondary" : "badge-success"}`}
                            style={{ fontSize: "0.72rem" }}
                          >
                            {torr.isPrivate
                              ? t("trackerBoost.privateBadge", "🔒 Private")
                              : t("trackerBoost.publicBadge", "🌐 Public")}
                          </span>
                          <div
                            style={{
                              fontFamily: "monospace",
                              fontSize: "0.72rem",
                              color: "var(--text-muted)",
                              marginTop: "0.25rem",
                            }}
                          >
                            {torr.infoHash
                              ? `${torr.infoHash.slice(0, 12)}...`
                              : ""}
                          </div>
                        </div>
                      </td>
                      <td>
                        <div
                          style={{
                            display: "flex",
                            flexWrap: "wrap",
                            gap: "0.35rem",
                          }}
                        >
                          {torr.trackers.map((tr, idx) => (
                            <span
                              key={tr.trackerId || idx}
                              className={`badge ${tr.isAttached ? "badge-primary" : "badge-success"}`}
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.35rem",
                                padding: "0.25rem 0.45rem",
                                fontSize: "0.72rem",
                                fontFamily: "monospace",
                              }}
                            >
                              <TrackerFavicon
                                urlOrHost={tr.trackerHost || tr.trackerUrl}
                                size={13}
                              />
                              <span>{tr.trackerHost || tr.trackerUrl}</span>
                              {(tr.seeders > 0 || tr.leechers > 0) && (
                                <span style={{ opacity: 0.85 }}>
                                  ({tr.seeders}s/{tr.leechers}l)
                                </span>
                              )}
                            </span>
                          ))}
                          {torr.trackers.length === 0 && (
                            <span
                              style={{
                                fontSize: "0.78rem",
                                color: "var(--text-muted)",
                              }}
                            >
                              {t(
                                "trackerBoost.matrix.noPositiveScrapes",
                                "No positive tracker scrapes found yet.",
                              )}
                            </span>
                          )}
                        </div>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <button
                          className="btn btn-sm btn-outline"
                          style={{
                            fontSize: "0.75rem",
                            padding: "0.25rem 0.5rem",
                          }}
                          onClick={() => {
                            if (onInspectTorrent && torr.infoHash) {
                              onInspectTorrent(torr.infoHash);
                            }
                          }}
                        >
                          {t("trackerBoost.matrix.inspect", "⚡ Inspect")}
                        </button>
                      </td>
                    </tr>
                  );
                })}
                {filteredMatrixTorrents.length === 0 && (
                  <tr>
                    <td
                      colSpan={4}
                      style={{
                        padding: "3rem",
                        textAlign: "center",
                        color: "var(--text-muted)",
                      }}
                    >
                      {t(
                        "trackerBoost.matrix.noLibraryTorrentsMatch",
                        "No library torrents match the search query.",
                      )}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )
      ) : matrixLayoutMode === "grid" ? (
        /* TRACKERS GRID VIEW */
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))",
            gap: "1.25rem",
            flex: "1 1 auto",
            minHeight: 0,
            overflowY: "auto",
            paddingRight: "0.25rem",
          }}
        >
          {filteredMatrixTrackers.map((tr) => (
            <div
              key={tr.trackerId || tr.trackerUrl}
              className="card"
              style={{
                padding: "1rem",
                backgroundColor: "var(--bg-secondary)",
                borderRadius: "8px",
                border: "1px solid rgba(255, 255, 255, 0.08)",
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "flex-start",
                  gap: "0.5rem",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                    minWidth: 0,
                  }}
                >
                  <TrackerFavicon urlOrHost={tr.trackerUrl} size={20} />
                  <div style={{ minWidth: 0 }}>
                    <div
                      style={{
                        fontWeight: 600,
                        fontSize: "0.9rem",
                        fontFamily: "monospace",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {tr.host || tr.trackerUrl}
                    </div>
                    <div
                      style={{
                        display: "flex",
                        gap: "0.35rem",
                        marginTop: "0.15rem",
                      }}
                    >
                      <span
                        className="badge badge-secondary"
                        style={{ fontSize: "0.68rem" }}
                      >
                        {tr.protocol}
                      </span>
                      {tr.latencyMs > 0 && (
                        <span
                          style={{
                            fontSize: "0.68rem",
                            color: "var(--text-muted)",
                            fontFamily: "monospace",
                          }}
                        >
                          {tr.latencyMs}ms
                        </span>
                      )}
                    </div>
                  </div>
                </div>
                <span
                  className="badge badge-success"
                  style={{ fontSize: "0.72rem", flexShrink: 0 }}
                >
                  {t("trackerBoost.matrix.torrentsCount", "{count} Torrents", {
                    count: tr.registeredTorrentsCount,
                  })}
                </span>
              </div>

              {/* Matched Torrents Poster / Title Gallery */}
              <div
                style={{
                  display: "flex",
                  flexWrap: "wrap",
                  gap: "0.5rem",
                  marginTop: "0.25rem",
                }}
              >
                {tr.registeredTorrentNames.map((name, idx) => {
                  const matchedTorrent = (torrents ?? []).find(
                    (t) => t.name === name,
                  );
                  const meta = matchedTorrent
                    ? torrentMetaMap.get(
                        (matchedTorrent.infoHash || "").toLowerCase(),
                      )
                    : undefined;
                  return (
                    <div
                      key={idx}
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.4rem",
                        padding: "0.25rem 0.5rem",
                        borderRadius: "4px",
                        backgroundColor: "rgba(255,255,255,0.05)",
                        border: "1px solid var(--border-light)",
                        fontSize: "0.75rem",
                        maxWidth: "100%",
                      }}
                      title={name}
                    >
                      {meta?.posterUrl ? (
                        <img
                          src={meta.posterUrl}
                          alt={name}
                          style={{
                            width: "16px",
                            height: "22px",
                            borderRadius: "2px",
                            objectFit: "cover",
                            flexShrink: 0,
                          }}
                        />
                      ) : (
                        <span>🎬</span>
                      )}
                      <span
                        style={{
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {meta?.mediaTitle || name}
                      </span>
                    </div>
                  );
                })}
                {tr.registeredTorrentNames.length === 0 && (
                  <span
                    style={{
                      fontSize: "0.78rem",
                      color: "var(--text-muted)",
                    }}
                  >
                    {t(
                      "trackerBoost.matrix.noTorrentsRegisteredOnEndpoint",
                      "No library torrents currently registered on this tracker endpoint.",
                    )}
                  </span>
                )}
              </div>
            </div>
          ))}
          {filteredMatrixTrackers.length === 0 && (
            <div
              style={{
                gridColumn: "1 / -1",
                padding: "3rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              {t(
                "trackerBoost.matrix.noTrackersMatch",
                "No tracker endpoints match the search query.",
              )}
            </div>
          )}
        </div>
      ) : (
        /* TRACKERS TABLE VIEW */
        <div
          className="torrent-table-wrapper"
          style={{
            borderRadius: "6px",
            border: "1px solid var(--border)",
            flex: "1 1 auto",
            minHeight: 0,
            overflowY: "auto",
            backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
          }}
        >
          <table
            className="torrent-table"
            style={{ width: "100%", fontSize: "0.85rem" }}
          >
            <thead
              style={{
                position: "sticky",
                top: 0,
                zIndex: 2,
                backgroundColor: "var(--bg-secondary)",
              }}
            >
              <tr>
                <th className="torrent-table-th" style={{ width: "35%" }}>
                  {t("trackerBoost.matrix.trackerEndpoint", "Tracker Endpoint")}
                </th>
                <th className="torrent-table-th" style={{ width: "10%" }}>
                  {t("trackerBoost.protocol", "Protocol")}
                </th>
                <th className="torrent-table-th" style={{ width: "10%" }}>
                  {t("trackerBoost.latency", "Latency")}
                </th>
                <th className="torrent-table-th" style={{ width: "45%" }}>
                  {t(
                    "trackerBoost.matrix.matchedLibraryTorrents",
                    "Matched Library Torrents",
                  )}
                </th>
              </tr>
            </thead>
            <tbody>
              {filteredMatrixTrackers.map((tr) => (
                <tr
                  key={tr.trackerId || tr.trackerUrl}
                  className="torrent-table-row"
                >
                  <td>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.5rem",
                      }}
                    >
                      <TrackerFavicon urlOrHost={tr.trackerUrl} size={16} />
                      <span
                        style={{
                          fontFamily: "monospace",
                          fontSize: "0.82rem",
                          wordBreak: "break-all",
                        }}
                      >
                        {tr.trackerUrl}
                      </span>
                    </div>
                  </td>
                  <td>
                    <span
                      className="badge badge-secondary"
                      style={{ fontSize: "0.75rem" }}
                    >
                      {tr.protocol}
                    </span>
                  </td>
                  <td style={{ fontFamily: "monospace" }}>
                    {tr.latencyMs > 0 ? `${tr.latencyMs}ms` : "-"}
                  </td>
                  <td>
                    <div
                      style={{
                        display: "flex",
                        flexWrap: "wrap",
                        gap: "0.4rem",
                      }}
                    >
                      {tr.registeredTorrentNames.map((name, idx) => {
                        const matchedTorrent = (torrents ?? []).find(
                          (t) => t.name === name,
                        );
                        const meta = matchedTorrent
                          ? torrentMetaMap.get(
                              (matchedTorrent.infoHash || "").toLowerCase(),
                            )
                          : undefined;
                        return (
                          <span
                            key={idx}
                            className="badge badge-secondary"
                            style={{
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.35rem",
                              padding: "0.25rem 0.5rem",
                              fontSize: "0.72rem",
                            }}
                            title={name}
                          >
                            {meta?.posterUrl && (
                              <img
                                src={meta.posterUrl}
                                alt={name}
                                style={{
                                  width: "14px",
                                  height: "18px",
                                  borderRadius: "2px",
                                  objectFit: "cover",
                                }}
                              />
                            )}
                            <span>{meta?.mediaTitle || name}</span>
                          </span>
                        );
                      })}
                      {tr.registeredTorrentNames.length === 0 && (
                        <span
                          style={{
                            fontSize: "0.78rem",
                            color: "var(--text-muted)",
                          }}
                        >
                          {t(
                            "trackerBoost.matrix.noTorrentsRegistered",
                            "No library torrents currently registered.",
                          )}
                        </span>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {filteredMatrixTrackers.length === 0 && (
                <tr>
                  <td
                    colSpan={4}
                    style={{
                      padding: "3rem",
                      textAlign: "center",
                      color: "var(--text-muted)",
                    }}
                  >
                    {t(
                      "trackerBoost.matrix.noTrackersMatch",
                      "No tracker endpoints match the search query.",
                    )}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default MatrixView;
