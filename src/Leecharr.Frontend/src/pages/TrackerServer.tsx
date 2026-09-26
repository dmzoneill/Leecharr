import { useState, useMemo } from "react";
import { Link } from "react-router";
import {
  useTrackerServerStats,
  useTrackerServerTorrents,
  useTrackerServerConfig,
  useSaveTrackerServerConfig,
  useNetworkStatus,
  useArrConnections,
} from "../api/hooks";
import { formatBytes, formatDate, formatUptime } from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";
import { useToast } from "../context/ToastContext";
import type { TrackerServerConfig } from "../api/types";

type SortField =
  | "peers"
  | "seeders"
  | "leechers"
  | "activity"
  | "name"
  | "uploaded"
  | "downloaded"
  | "completed";

type SortDirection = "asc" | "desc";

function SortArrow({ direction }: { direction: SortDirection }) {
  return (
    <svg
      width="10"
      height="10"
      viewBox="0 0 10 10"
      fill="currentColor"
      style={{ marginLeft: "4px", opacity: 0.8 }}
    >
      {direction === "asc" ? (
        <polygon points="5,2 9,8 1,8" />
      ) : (
        <polygon points="5,8 1,2 9,2" />
      )}
    </svg>
  );
}

function TrackerServer() {
  const [viewMode, setViewMode] = useState<"grid" | "table">("grid");
  const [filterScope, setFilterScope] = useState<
    "all" | "internal" | "external"
  >("all");
  const [searchTerm, setSearchTerm] = useState("");
  const [sortBy, setSortBy] = useState<SortField>("peers");
  const [sortDirection, setSortDirection] = useState<SortDirection>("desc");

  const { data: stats, isLoading: statsLoading } = useTrackerServerStats();
  const { data: torrents, isLoading: torrentsLoading } =
    useTrackerServerTorrents();
  const { data: config } = useTrackerServerConfig();
  const { data: network } = useNetworkStatus();
  const { data: arrConnections } = useArrConnections();
  const saveConfig = useSaveTrackerServerConfig();
  const { showToast } = useToast();

  const host = network?.externalIp || window.location.hostname || "localhost";
  const httpAnnounceUrl = config?.trackerHttpPort
    ? `http://${host}:${config.trackerHttpPort}/announce`
    : null;
  const udpAnnounceUrl = config?.trackerUdpPort
    ? `udp://${host}:${config.trackerUdpPort}/announce`
    : null;

  function handleToggleEnabled() {
    if (!config) return;
    const updated: TrackerServerConfig = {
      ...config,
      trackerServerEnabled: !config.trackerServerEnabled,
    };
    saveConfig.mutate(updated, {
      onSuccess: (cfg) => {
        showToast(
          `Tracker server ${cfg.trackerServerEnabled ? "enabled" : "disabled"} successfully`,
          "success",
        );
      },
      onError: (err: unknown) => {
        const message = err instanceof Error ? err.message : "Failed to update tracker daemon status";
        showToast(`Failed to update tracker daemon: ${message}`, "error");
      },
    });
  }

  const handleSort = (field: SortField) => {
    if (sortBy === field) {
      setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortBy(field);
      setSortDirection(field === "name" ? "asc" : "desc");
    }
  };

  const copyToClipboard = async (text: string, label: string) => {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
      } else {
        const textArea = document.createElement("textarea");
        textArea.value = text;
        textArea.style.position = "fixed";
        textArea.style.opacity = "0";
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        document.execCommand("copy");
        document.body.removeChild(textArea);
      }
      showToast(`Copied ${label} announce URL to clipboard`, "success");
    } catch {
      showToast(`Failed to copy ${label} announce URL`, "error");
    }
  };

  const sortedTorrents = useMemo(() => {
    const filtered = (torrents ?? []).filter((t) => {
      if (filterScope === "internal" && !t.isInternal) return false;
      if (filterScope === "external" && t.isInternal) return false;
      if (searchTerm.trim()) {
        const q = searchTerm.toLowerCase();
        const matchName = t.name?.toLowerCase().includes(q);
        const matchTitle = t.mediaTitle?.toLowerCase().includes(q);
        const matchHash = t.infoHash?.toLowerCase().includes(q);
        const matchGenre = t.genres?.some((g) => g.toLowerCase().includes(q));
        if (!matchName && !matchTitle && !matchHash && !matchGenre)
          return false;
      }
      return true;
    });

    return filtered.sort((a, b) => {
      let cmp = 0;
      switch (sortBy) {
        case "peers":
          cmp = (a.peerCount ?? 0) - (b.peerCount ?? 0);
          break;
        case "seeders":
          cmp = (a.seeders ?? 0) - (b.seeders ?? 0);
          break;
        case "leechers":
          cmp = (a.leechers ?? 0) - (b.leechers ?? 0);
          break;
        case "name": {
          const titleA = (a.mediaTitle || a.name || "").toLowerCase();
          const titleB = (b.mediaTitle || b.name || "").toLowerCase();
          cmp = titleA.localeCompare(titleB);
          break;
        }
        case "activity": {
          const timeA = a.lastActivity ? new Date(a.lastActivity).getTime() : 0;
          const timeB = b.lastActivity ? new Date(b.lastActivity).getTime() : 0;
          cmp = timeA - timeB;
          break;
        }
        case "uploaded":
          cmp = (a.uploaded ?? 0) - (b.uploaded ?? 0);
          break;
        case "downloaded":
          cmp = (a.downloaded ?? 0) - (b.downloaded ?? 0);
          break;
        case "completed":
          cmp = (a.completed ?? 0) - (b.completed ?? 0);
          break;
        default:
          cmp = 0;
      }

      if (cmp !== 0) {
        return sortDirection === "asc" ? cmp : -cmp;
      }

      // Deterministic tie-breaker: infoHash
      return (a.infoHash || "").localeCompare(b.infoHash || "");
    });
  }, [torrents, filterScope, searchTerm, sortBy, sortDirection]);

  return (
    <div
      className="content-area tracker-server-page"
      style={{
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        padding: "1.5rem",
        boxSizing: "border-box",
      }}
    >
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
          flexShrink: 0,
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>📡</span> Tracker Server (
            {statsLoading ? "-" : (stats?.totalTorrents ?? 0)})
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Inbuilt private and public BitTorrent tracker server endpoints,
            swarms, and peer stats
          </p>
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {/* View Mode Toggle */}
          <div className="view-toggle">
            <button
              className={`view-toggle-btn ${viewMode === "grid" ? "active" : ""}`}
              onClick={() => setViewMode("grid")}
              title="Poster Card Grid View"
            >
              🎬 Posters
            </button>
            <button
              className={`view-toggle-btn ${viewMode === "table" ? "active" : ""}`}
              onClick={() => setViewMode("table")}
              title="Detailed Table View"
            >
              📋 Table
            </button>
          </div>

          {config && (
            <button
              className={`btn ${config.trackerServerEnabled ? "btn-danger" : "btn-success"}`}
              onClick={handleToggleEnabled}
              disabled={saveConfig.isPending}
              style={{ fontSize: "0.82rem" }}
            >
              {config.trackerServerEnabled
                ? "Disable Tracker"
                : "Enable Tracker"}
            </button>
          )}
        </div>
      </div>

      {/* Top Stat Cards */}
      <div
        className="tracker-stats-grid"
        style={{ marginBottom: "1rem", flexShrink: 0 }}
      >
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Torrents</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : (stats?.totalTorrents ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Internal (Leecharr)</div>
          <div className="tracker-stat-value">
            {statsLoading
              ? "-"
              : (stats?.internalTorrents ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Peers</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : (stats?.totalPeers ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Total Announces</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : (stats?.totalAnnounces ?? 0).toLocaleString()}
          </div>
        </div>
        <div className="card tracker-stat-card">
          <div className="tracker-stat-label">Uptime</div>
          <div className="tracker-stat-value">
            {statsLoading ? "-" : formatUptime(stats?.uptime ?? 0)}
          </div>
        </div>
      </div>

      {/* Announce Endpoints Quick-Copy Banner */}
      {config?.trackerServerEnabled && (
        <div
          className="card"
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
            gap: "1rem",
            marginBottom: "1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            flexShrink: 0,
          }}
        >
          {config.trackerHttpEnabled && httpAnnounceUrl && (
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                gap: "0.5rem",
                padding: "0.75rem 1rem",
                backgroundColor: "var(--bg-primary)",
                borderRadius: "6px",
                border: "1px solid var(--border-light)",
              }}
            >
              <div>
                <div
                  style={{
                    fontSize: "0.72rem",
                    color: "var(--text-muted)",
                    fontWeight: 600,
                  }}
                >
                  HTTP ANNOUNCE URL
                </div>
                <code style={{ fontSize: "0.85rem", color: "var(--accent)" }}>
                  {httpAnnounceUrl}
                </code>
              </div>
              <button
                className="btn btn-outline"
                style={{ fontSize: "0.75rem", padding: "0.25rem 0.5rem" }}
                onClick={() => copyToClipboard(httpAnnounceUrl, "HTTP URL")}
                title="Copy HTTP Announce URL"
              >
                Copy
              </button>
            </div>
          )}

          {config.trackerUdpEnabled && udpAnnounceUrl && (
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                gap: "0.5rem",
                padding: "0.75rem 1rem",
                backgroundColor: "var(--bg-primary)",
                borderRadius: "6px",
                border: "1px solid var(--border-light)",
              }}
            >
              <div>
                <div
                  style={{
                    fontSize: "0.72rem",
                    color: "var(--text-muted)",
                    fontWeight: 600,
                  }}
                >
                  UDP ANNOUNCE URL
                </div>
                <code style={{ fontSize: "0.85rem", color: "var(--accent)" }}>
                  {udpAnnounceUrl}
                </code>
              </div>
              <button
                className="btn btn-outline"
                style={{ fontSize: "0.75rem", padding: "0.25rem 0.5rem" }}
                onClick={() => copyToClipboard(udpAnnounceUrl, "UDP URL")}
                title="Copy UDP Announce URL"
              >
                Copy
              </button>
            </div>
          )}
        </div>
      )}

      {/* Filter and Search Bar */}
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
          {(
            [
              { id: "all", label: "All" },
              { id: "internal", label: "Internal (Leecharr)" },
              { id: "external", label: "External Peers" },
            ] as const
          ).map((scope) => (
            <button
              key={scope.id}
              className={`btn ${filterScope === scope.id ? "btn-primary" : "btn-outline"}`}
              style={{
                fontSize: "0.82rem",
                padding: "0.35rem 0.85rem",
                borderRadius: "6px",
                fontWeight: 500,
              }}
              onClick={() => setFilterScope(scope.id)}
            >
              {scope.label}
            </button>
          ))}
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            alignItems: "center",
            flexWrap: "wrap",
            flex: "1",
            justifyContent: "flex-end",
          }}
        >
          {/* Swarm Sort Controls */}
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.35rem" }}
          >
            <span
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                fontWeight: 500,
              }}
            >
              Sort:
            </span>
            <select
              className="form-control"
              value={sortBy}
              onChange={(e) => {
                const newSort = e.target.value as SortField;
                setSortBy(newSort);
                setSortDirection(newSort === "name" ? "asc" : "desc");
              }}
              style={{
                fontSize: "0.82rem",
                padding: "0.35rem 0.6rem",
                borderRadius: "6px",
                border: "1px solid var(--border-light)",
                backgroundColor: "var(--bg-primary)",
                color: "inherit",
              }}
            >
              <option value="peers">Peers</option>
              <option value="seeders">Seeders</option>
              <option value="leechers">Leechers</option>
              <option value="activity">Last Activity</option>
              <option value="name">Name</option>
              <option value="uploaded">Uploaded</option>
              <option value="downloaded">Downloaded</option>
              <option value="completed">Completed</option>
            </select>
            <button
              className="btn btn-outline"
              onClick={() =>
                setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"))
              }
              style={{
                fontSize: "0.82rem",
                padding: "0.35rem 0.6rem",
                borderRadius: "6px",
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
              }}
              title={`Sort ${sortDirection === "asc" ? "Descending" : "Ascending"}`}
            >
              <span>{sortDirection === "asc" ? "▲ Asc" : "▼ Desc"}</span>
            </button>
          </div>

          <div
            style={{
              display: "flex",
              gap: "0.5rem",
              alignItems: "center",
              minWidth: "200px",
              maxWidth: "350px",
              flex: "1",
            }}
          >
            <input
              type="text"
              className="form-control"
              placeholder="Search tracked torrents, titles, hash..."
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
                title="Clear search filter"
              >
                ✕
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Tracked Torrents - Grid or Table */}
      {torrentsLoading ? (
        <div className="card" style={{ padding: "3rem", textAlign: "center" }}>
          <p className="loading">Loading tracked swarms & rich metadata...</p>
        </div>
      ) : sortedTorrents.length === 0 ? (
        <div
          className="card empty-state"
          style={{ padding: "3.5rem 1rem", textAlign: "center" }}
        >
          <div
            className="empty-state-title"
            style={{
              fontSize: "1.25rem",
              fontWeight: 600,
              marginBottom: "0.5rem",
            }}
          >
            No Tracked Torrents
          </div>
          <div
            className="empty-state-text"
            style={{
              color: "var(--text-muted)",
              maxWidth: "500px",
              margin: "0 auto",
            }}
          >
            {searchTerm || filterScope !== "all"
              ? "No tracked torrents match the current search or scope filter."
              : "Torrents announced to this built-in tracker will appear here with live peer statistics and rich media posters."}
          </div>
        </div>
      ) : viewMode === "grid" ? (
        /* POSTER GRID VIEW */
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
            gridAutoRows: "max-content",
            alignContent: "start",
            gap: "1.25rem",
            padding: "1.25rem",
            overflowY: "auto",
            overflowX: "hidden",
            flex: "1 1 0%",
            minHeight: 0,
            height: "100%",
            width: "100%",
          }}
        >
          {sortedTorrents.map((t) => {
            const displayTitle = t.mediaTitle || t.name;
            const hasPoster = Boolean(t.posterUrl);
            const arrLink = getMediaDeepLink(
              {
                source: t.source,
                metadata: { title: t.mediaTitle, mediaId: 0 },
                title: t.name,
              },
              arrConnections,
            );

            return (
              <div
                key={t.infoHash}
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
                  border: "1px solid var(--border-light)",
                  backgroundColor: "var(--bg-secondary)",
                  boxShadow:
                    "0 4px 14px rgba(0, 0, 0, 0.35), 0 1px 3px rgba(0, 0, 0, 0.2)",
                  transition:
                    "transform 0.18s ease, box-shadow 0.18s ease, border-color 0.18s ease",
                  cursor: "pointer",
                }}
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
                      src={t.posterUrl || ""}
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
                        style={{ fontSize: "2.5rem", marginBottom: "0.5rem" }}
                      >
                        {t.source === "Radarr"
                          ? "🎬"
                          : t.source === "Sonarr"
                            ? "📺"
                            : t.source === "Lidarr"
                              ? "🎵"
                              : "📡"}
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

                  {/* Top Left Source Badge */}
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
                        border: "1px solid var(--border)",
                        cursor: arrLink ? "pointer" : "default",
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.25rem",
                        borderRadius: "4px",
                      }}
                      title={
                        arrLink
                          ? `${arrLink.label} (${arrLink.url})`
                          : t.source || "Tracker"
                      }
                    >
                      {t.isInternal ? "🌱 Internal" : "🌐 External"}{" "}
                      {arrLink ? "↗" : ""}
                    </span>
                  </div>

                  {/* Top Right Peers & Rating Badges */}
                  <div
                    style={{
                      position: "absolute",
                      top: "8px",
                      right: "8px",
                      zIndex: 2,
                      display: "flex",
                      flexDirection: "column",
                      gap: "4px",
                      alignItems: "flex-end",
                    }}
                  >
                    <span
                      className="badge badge-success"
                      style={{
                        fontSize: "0.72rem",
                        padding: "0.2rem 0.5rem",
                        boxShadow: "0 2px 6px rgba(0,0,0,0.5)",
                        borderRadius: "4px",
                      }}
                    >
                      👥 {t.peerCount} peers
                    </span>
                    {t.rating && (
                      <span
                        className="badge"
                        style={{
                          backgroundColor: "rgba(0, 0, 0, 0.8)",
                          backdropFilter: "blur(4px)",
                          color: "var(--accent)",
                          fontSize: "0.68rem",
                          padding: "0.15rem 0.45rem",
                          border: "1px solid rgba(200, 168, 78, 0.3)",
                          borderRadius: "4px",
                        }}
                      >
                        ⭐ {t.rating}
                      </span>
                    )}
                  </div>

                  {/* Bottom Swarm Stats Bar over poster */}
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
                      justifyContent: "space-around",
                      fontSize: "0.7rem",
                      borderTop: "1px solid var(--border-light)",
                    }}
                  >
                    <span style={{ color: "#4caf50" }}>
                      ▲ {t.seeders} seeds
                    </span>
                    <span style={{ color: "#ff9800" }}>
                      ▼ {t.leechers} leech
                    </span>
                    <span style={{ color: "#90caf9" }}>
                      ✓ {t.completed} done
                    </span>
                  </div>
                </div>

                {/* Content Details */}
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
                    title={t.name}
                  >
                    {displayTitle} {t.year ? `(${t.year})` : ""}
                  </div>

                  {/* Genres */}
                  {t.genres && t.genres.length > 0 && (
                    <div
                      style={{
                        display: "flex",
                        gap: "0.3rem",
                        flexWrap: "wrap",
                      }}
                    >
                      {t.genres.slice(0, 2).map((g) => (
                        <span
                          key={g}
                          style={{
                            fontSize: "0.65rem",
                            padding: "0.1rem 0.35rem",
                            backgroundColor: "rgba(255,255,255,0.06)",
                            color: "var(--text-muted)",
                            borderRadius: "3px",
                          }}
                        >
                          {g}
                        </span>
                      ))}
                    </div>
                  )}

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
                      <span style={{ color: "var(--text-dim)" }}>Up: </span>
                      <span
                        style={{
                          color: "var(--text-secondary)",
                          fontWeight: 500,
                        }}
                      >
                        {formatBytes(t.uploaded)}
                      </span>
                    </div>
                    <div>
                      <span style={{ color: "var(--text-dim)" }}>Down: </span>
                      <span
                        style={{
                          color: "var(--text-secondary)",
                          fontWeight: 500,
                        }}
                      >
                        {formatBytes(t.downloaded)}
                      </span>
                    </div>
                    <div>
                      <span style={{ color: "var(--text-dim)" }}>
                        Activity:{" "}
                      </span>
                      <span
                        style={{
                          color: "var(--text-secondary)",
                          fontWeight: 500,
                        }}
                      >
                        {t.lastActivity
                          ? formatDate(t.lastActivity).split(" ")[0]
                          : "Idle"}
                      </span>
                    </div>
                    <div>
                      <span style={{ color: "var(--text-dim)" }}>Hash: </span>
                      <code
                        style={{ fontSize: "0.68rem", color: "var(--accent)" }}
                      >
                        {t.infoHash.substring(0, 6)}...
                      </code>
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        /* TABLE VIEW */
        <div className="card" style={{ padding: 0, overflow: "hidden" }}>
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Cover</th>
                  <th className="torrent-table-th">Source</th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("name")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Name"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Name{" "}
                      {sortBy === "name" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("seeders")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Seeders"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Seeders{" "}
                      {sortBy === "seeders" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("leechers")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Leechers"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Leechers{" "}
                      {sortBy === "leechers" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("uploaded")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Uploaded"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Uploaded{" "}
                      {sortBy === "uploaded" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("downloaded")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Downloaded"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Downloaded{" "}
                      {sortBy === "downloaded" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("completed")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Completed"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Completed{" "}
                      {sortBy === "completed" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("peers")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Total Peers"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Peers{" "}
                      {sortBy === "peers" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th
                    className="torrent-table-th"
                    onClick={() => handleSort("activity")}
                    style={{ cursor: "pointer", userSelect: "none" }}
                    title="Click to sort by Last Activity"
                  >
                    <span
                      style={{ display: "inline-flex", alignItems: "center" }}
                    >
                      Last Activity{" "}
                      {sortBy === "activity" && (
                        <SortArrow direction={sortDirection} />
                      )}
                    </span>
                  </th>
                  <th className="torrent-table-th">Info Hash</th>
                </tr>
              </thead>
              <tbody>
                {sortedTorrents.map((t) => {
                  const displayTitle = t.mediaTitle || t.name;
                  return (
                    <tr key={t.infoHash} className="torrent-table-row">
                      <td style={{ width: "42px", padding: "0.35rem 0.5rem" }}>
                        {t.posterUrl ? (
                          <img
                            src={t.posterUrl}
                            alt=""
                            style={{
                              width: "32px",
                              height: "46px",
                              objectFit: "cover",
                              borderRadius: "4px",
                              border: "1px solid var(--border-light)",
                            }}
                            loading="lazy"
                          />
                        ) : (
                          <div
                            style={{
                              width: "32px",
                              height: "46px",
                              backgroundColor: "var(--bg-primary)",
                              borderRadius: "4px",
                              display: "flex",
                              alignItems: "center",
                              justifyContent: "center",
                              fontSize: "1rem",
                            }}
                          >
                            📦
                          </div>
                        )}
                      </td>
                      <td>
                        <span
                          className={`badge ${t.isInternal ? "badge-seeding" : "badge-warning"}`}
                          style={{ borderRadius: "4px" }}
                        >
                          {t.isInternal ? "Internal" : "External"}
                        </span>
                      </td>
                      <td>
                        <div style={{ fontWeight: 500 }}>
                          {t.isInternal ? (
                            <Link
                              to="/torrents"
                              style={{
                                color: "inherit",
                                textDecoration: "none",
                              }}
                              title="Jump to active torrent in library"
                            >
                              {displayTitle} ↗
                            </Link>
                          ) : (
                            displayTitle
                          )}
                        </div>
                        {t.mediaTitle && t.mediaTitle !== t.name && (
                          <div
                            style={{
                              fontSize: "0.72rem",
                              color: "var(--text-dim)",
                            }}
                          >
                            {t.name}
                          </div>
                        )}
                      </td>
                      <td style={{ color: "#4caf50", fontWeight: 600 }}>
                        {t.seeders}
                      </td>
                      <td style={{ color: "#ff9800", fontWeight: 600 }}>
                        {t.leechers}
                      </td>
                      <td>{formatBytes(t.uploaded)}</td>
                      <td>{formatBytes(t.downloaded)}</td>
                      <td>{t.completed}</td>
                      <td>
                        <span
                          className="badge badge-secondary"
                          style={{ borderRadius: "4px" }}
                        >
                          {t.peerCount}
                        </span>
                      </td>
                      <td>
                        {t.lastActivity ? formatDate(t.lastActivity) : "Never"}
                      </td>
                      <td>
                        <code className="info-hash">
                          {t.infoHash.substring(0, 10)}...
                        </code>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

export default TrackerServer;
