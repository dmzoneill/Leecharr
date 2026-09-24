import { useState, useMemo } from "react";
import {
  useTrackerMetrics,
  useTrackerMetricsSummary,
  useTrackerMetricHistory,
  useResetTrackerMetric,
  useDeleteTrackerMetric,
} from "../api/hooks";
import { formatBytes, formatDate } from "../utils/formatters";
import TrackerFavicon from "../components/TrackerFavicon";
import { useToast } from "../context/ToastContext";
import type {
  TrackerMetric,
  TrackerMetricSnapshot,
  HourlyTrafficPoint,
} from "../api/types";

type ProtocolFilter = "ALL" | "UDP" | "HTTP" | "HTTPS";
type StatusFilter = "ALL" | "Working" | "Degraded" | "Offline";
type SortField =
  | "upload"
  | "download"
  | "ratio"
  | "announces"
  | "successRate"
  | "latency"
  | "peers"
  | "domain"
  | "lastAnnounce";

export function TrackerMetrics() {
  const [isLive, setIsLive] = useState(true);
  const [protocolFilter, setProtocolFilter] = useState<ProtocolFilter>("ALL");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("ALL");
  const [searchTerm, setSearchTerm] = useState("");
  const [sortField, setSortField] = useState<SortField>("upload");
  const [sortAsc, setSortAsc] = useState(false);
  const [selectedMetric, setSelectedMetric] = useState<TrackerMetric | null>(
    null,
  );

  const {
    data: metrics = [],
    isLoading: isLoadingMetrics,
    refetch: refetchMetrics,
  } = useTrackerMetrics(isLive ? 4000 : false);
  const {
    data: summary,
    refetch: refetchSummary,
  } = useTrackerMetricsSummary(isLive ? 4000 : false);

  const resetMetric = useResetTrackerMetric();
  const deleteMetric = useDeleteTrackerMetric();
  const { showToast } = useToast();

  const handleRefresh = () => {
    refetchMetrics();
    refetchSummary();
    showToast("Tracker metrics refreshed", "info");
  };

  const handleReset = (metric: TrackerMetric) => {
    if (
      confirm(
        `Reset all metrics history for ${metric.domain || metric.trackerUrl}?`,
      )
    ) {
      resetMetric.mutate(metric.id, {
        onSuccess: () =>
          showToast(
            `Reset stats for ${metric.domain || metric.host}`,
            "success",
          ),
        onError: (err) => showToast(`Failed to reset: ${err.message}`, "error"),
      });
    }
  };

  const handleDelete = (metric: TrackerMetric) => {
    if (
      confirm(`Delete tracking data for ${metric.domain || metric.trackerUrl}?`)
    ) {
      deleteMetric.mutate(metric.id, {
        onSuccess: () => {
          showToast(`Deleted ${metric.domain || metric.host}`, "success");
          if (selectedMetric?.id === metric.id) setSelectedMetric(null);
        },
        onError: (err) =>
          showToast(`Failed to delete: ${err.message}`, "error"),
      });
    }
  };

  // Filter and Sort
  const filteredMetrics = useMemo(() => {
    return metrics
      .filter((m) => {
        if (
          protocolFilter !== "ALL" &&
          (m.protocol || "").toUpperCase() !== protocolFilter
        ) {
          return false;
        }
        if (
          statusFilter !== "ALL" &&
          (m.status || "").toLowerCase() !== statusFilter.toLowerCase()
        ) {
          return false;
        }
        if (searchTerm) {
          const q = searchTerm.toLowerCase();
          const matchUrl = (m.trackerUrl || "").toLowerCase().includes(q);
          const matchDomain = (m.domain || "").toLowerCase().includes(q);
          const matchHost = (m.host || "").toLowerCase().includes(q);
          if (!matchUrl && !matchDomain && !matchHost) return false;
        }
        return true;
      })
      .sort((a, b) => {
        let cmp = 0;
        switch (sortField) {
          case "upload":
            cmp = a.totalUploaded - b.totalUploaded;
            break;
          case "download":
            cmp = a.totalDownloaded - b.totalDownloaded;
            break;
          case "ratio":
            cmp = a.ratio - b.ratio;
            break;
          case "announces":
            cmp = a.totalAnnounces - b.totalAnnounces;
            break;
          case "successRate":
            cmp = a.announceSuccessRate - b.announceSuccessRate;
            break;
          case "latency":
            cmp = a.avgResponseTimeMs - b.avgResponseTimeMs;
            break;
          case "peers":
            cmp = a.totalPeersDiscovered - b.totalPeersDiscovered;
            break;
          case "domain":
            cmp = (a.domain || "").localeCompare(b.domain || "");
            break;
          case "lastAnnounce":
            cmp =
              new Date(a.lastAnnounce || 0).getTime() -
              new Date(b.lastAnnounce || 0).getTime();
            break;
          default:
            cmp = a.totalUploaded - b.totalUploaded;
        }
        return sortAsc ? cmp : -cmp;
      });
  }, [metrics, protocolFilter, statusFilter, searchTerm, sortField, sortAsc]);

  const summaryMaxUpload = useMemo(() => {
    return Math.max(
      ...(summary?.topUploadTrackers ?? []).map((t) => t.totalUploaded || 0),
      1,
    );
  }, [summary?.topUploadTrackers]);

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "1.25rem",
        padding: "1.5rem",
        paddingBottom: "3rem",
      }}
    >
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.5rem",
        }}
      >
        <div>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              flexWrap: "wrap",
            }}
          >
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
              <span>📡</span> Tracker Metrics
            </h1>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
                fontSize: "0.75rem",
                fontWeight: 600,
                padding: "0.2rem 0.55rem",
                borderRadius: "12px",
                backgroundColor: isLive
                  ? "rgba(34, 197, 94, 0.15)"
                  : "rgba(148, 163, 184, 0.15)",
                color: isLive ? "#4ade80" : "#94a3b8",
                border: `1px solid ${isLive ? "rgba(34, 197, 94, 0.3)" : "rgba(148, 163, 184, 0.3)"}`,
              }}
            >
              <span
                style={{
                  width: "6px",
                  height: "6px",
                  borderRadius: "50%",
                  backgroundColor: isLive ? "#22c55e" : "#94a3b8",
                  boxShadow: isLive ? "0 0 6px #22c55e" : "none",
                }}
              />
              {isLive ? "Live Telemetry" : "Paused"}
            </span>
          </div>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Telemetry, traffic statistics, scrape responses, and latency metrics
            for all interacted trackers
          </p>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <button
            className={`btn btn-sm ${isLive ? "btn-outline" : "btn-primary"}`}
            onClick={() => setIsLive(!isLive)}
            title={isLive ? "Pause auto-updates" : "Resume live telemetry"}
          >
            {isLive ? "⏸ Pause" : "▶ Resume"}
          </button>
          <button
            className="btn btn-outline btn-sm"
            onClick={handleRefresh}
            title="Refresh metrics"
          >
            🔄 Refresh
          </button>
        </div>
      </div>

      {/* KPI Cards Grid */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
          gap: "0.85rem",
        }}
      >
        {/* Total Uploaded */}
        <div
          className="card"
          style={{
            padding: "1.1rem",
            borderRadius: "8px",
            position: "relative",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
              fontWeight: 600,
              textTransform: "uppercase",
            }}
          >
            Total Upload Reported
          </div>
          <div
            style={{
              fontSize: "1.65rem",
              fontWeight: 700,
              color: "#4ade80",
              marginTop: "0.3rem",
            }}
          >
            {formatBytes(summary?.totalUploaded ?? 0)}
          </div>
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-muted)",
              marginTop: "0.25rem",
            }}
          >
            Across {summary?.totalTrackers ?? 0} swarms
          </div>
        </div>

        {/* Total Downloaded */}
        <div
          className="card"
          style={{ padding: "1.1rem", borderRadius: "8px" }}
        >
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
              fontWeight: 600,
              textTransform: "uppercase",
            }}
          >
            Total Download Reported
          </div>
          <div
            style={{
              fontSize: "1.65rem",
              fontWeight: 700,
              color: "#60a5fa",
              marginTop: "0.3rem",
            }}
          >
            {formatBytes(summary?.totalDownloaded ?? 0)}
          </div>
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-muted)",
              marginTop: "0.25rem",
            }}
          >
            Global Ratio:{" "}
            <strong style={{ color: "#fbbf24" }}>
              {summary?.globalRatio ?? 0}x
            </strong>
          </div>
        </div>

        {/* Success Rate */}
        <div
          className="card"
          style={{ padding: "1.1rem", borderRadius: "8px" }}
        >
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
              fontWeight: 600,
              textTransform: "uppercase",
            }}
          >
            Announce Success Rate
          </div>
          <div
            style={{
              fontSize: "1.65rem",
              fontWeight: 700,
              color: "#22c55e",
              marginTop: "0.3rem",
            }}
          >
            {summary?.announceSuccessRate ?? 100}%
          </div>
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-muted)",
              marginTop: "0.25rem",
            }}
          >
            {summary?.successfulAnnounces?.toLocaleString() ?? 0} ok /{" "}
            {summary?.failedAnnounces ?? 0} fail
          </div>
        </div>

        {/* Avg Response Time */}
        <div
          className="card"
          style={{ padding: "1.1rem", borderRadius: "8px" }}
        >
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
              fontWeight: 600,
              textTransform: "uppercase",
            }}
          >
            Avg Response Latency
          </div>
          <div
            style={{
              fontSize: "1.65rem",
              fontWeight: 700,
              color: "#e2e8f0",
              marginTop: "0.3rem",
            }}
          >
            {Math.round(summary?.avgResponseTimeMs ?? 0)}{" "}
            <span style={{ fontSize: "1rem", fontWeight: 400 }}>ms</span>
          </div>
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-muted)",
              marginTop: "0.25rem",
            }}
          >
            Round-trip UDP/HTTP
          </div>
        </div>

        {/* Trackers Health Breakdown */}
        <div
          className="card"
          style={{ padding: "1.1rem", borderRadius: "8px" }}
        >
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
              fontWeight: 600,
              textTransform: "uppercase",
            }}
          >
            Active Tracker Swarms
          </div>
          <div
            style={{
              fontSize: "1.65rem",
              fontWeight: 700,
              marginTop: "0.3rem",
            }}
          >
            {summary?.totalTrackers ?? 0}
          </div>
          <div
            style={{
              display: "flex",
              gap: "0.5rem",
              marginTop: "0.25rem",
              fontSize: "0.76rem",
            }}
          >
            <span style={{ color: "#4ade80" }}>
              ● {summary?.healthyTrackers ?? 0} ok
            </span>
            <span style={{ color: "#fbbf24" }}>
              ● {summary?.degradedTrackers ?? 0} slow
            </span>
            <span style={{ color: "#f87171" }}>
              ● {summary?.offlineTrackers ?? 0} down
            </span>
          </div>
        </div>

        {/* Peers Discovered */}
        <div
          className="card"
          style={{ padding: "1.1rem", borderRadius: "8px" }}
        >
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
              fontWeight: 600,
              textTransform: "uppercase",
            }}
          >
            Total Peers Discovered
          </div>
          <div
            style={{
              fontSize: "1.65rem",
              fontWeight: 700,
              color: "#c084fc",
              marginTop: "0.3rem",
            }}
          >
            {(summary?.totalPeersDiscovered ?? 0).toLocaleString()}
          </div>
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-muted)",
              marginTop: "0.25rem",
            }}
          >
            Candidates harvested
          </div>
        </div>
      </div>

      {/* Visual Charts & Diagrams Section */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(360px, 1fr))",
          gap: "1rem",
        }}
      >
        {/* Chart 1: 24h Hourly Activity Timeline */}
        <div
          className="card"
          style={{
            padding: "1.25rem",
            borderRadius: "8px",
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <h3
              style={{
                margin: 0,
                fontSize: "0.98rem",
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
              }}
            >
              <span>📈</span> 24-Hour Announce & Traffic Activity
            </h3>
            <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
              Last 24 Hours
            </span>
          </div>

          <div
            style={{
              height: "180px",
              width: "100%",
              position: "relative",
              marginTop: "0.5rem",
            }}
          >
            <HourlyActivitySvgChart data={summary?.hourlyHistory ?? []} />
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "center",
              gap: "1.25rem",
              fontSize: "0.75rem",
              color: "var(--text-secondary)",
            }}
          >
            <span
              style={{ display: "flex", alignItems: "center", gap: "0.35rem" }}
              title="Upload volume across 24 hours (traffic scale)"
            >
              <span
                style={{
                  width: "10px",
                  height: "10px",
                  backgroundColor: "#4ade80",
                  borderRadius: "2px",
                }}
              />
              Upload Volume
            </span>
            <span
              style={{ display: "flex", alignItems: "center", gap: "0.35rem" }}
              title="Download volume across 24 hours (traffic scale)"
            >
              <span
                style={{
                  width: "10px",
                  height: "10px",
                  backgroundColor: "#60a5fa",
                  borderRadius: "2px",
                }}
              />
              Download Volume
            </span>
            <span
              style={{ display: "flex", alignItems: "center", gap: "0.35rem" }}
              title="Announce count scaled independently on secondary scale"
            >
              <span
                style={{
                  width: "10px",
                  height: "10px",
                  backgroundColor: "#f59e0b",
                  borderRadius: "2px",
                }}
              />
              Announce Count (Secondary Scale)
            </span>
          </div>
        </div>

        {/* Chart 2: Top Upload Trackers & Protocols Breakdown */}
        <div
          className="card"
          style={{
            padding: "1.25rem",
            borderRadius: "8px",
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
          }}
        >
          <h3
            style={{
              margin: 0,
              fontSize: "0.98rem",
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <span>🏆</span> Top Swarms by Upload Traffic
          </h3>

          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "0.6rem",
              flex: 1,
              justifyContent: "center",
            }}
          >
            {(summary?.topUploadTrackers ?? []).length === 0 ? (
              <div
                style={{
                  textAlign: "center",
                  color: "var(--text-muted)",
                  fontSize: "0.85rem",
                  padding: "2rem",
                }}
              >
                No tracker traffic recorded yet
              </div>
            ) : (
              (summary?.topUploadTrackers ?? []).map((t, idx) => {
                const pct =
                  summaryMaxUpload > 0
                    ? (t.totalUploaded / summaryMaxUpload) * 100
                    : 0;
                return (
                  <div
                    key={t.id || idx}
                    style={{
                      display: "flex",
                      flexDirection: "column",
                      gap: "0.2rem",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        fontSize: "0.8rem",
                      }}
                    >
                      <span
                        style={{
                          fontWeight: 600,
                          display: "flex",
                          alignItems: "center",
                          gap: "0.35rem",
                        }}
                      >
                        <TrackerFavicon urlOrHost={t.trackerUrl} size={14} />
                        {t.domain || t.trackerUrl}
                        <span
                          style={{
                            fontSize: "0.65rem",
                            padding: "0.05rem 0.35rem",
                            borderRadius: "3px",
                            backgroundColor: "rgba(255,255,255,0.08)",
                            color: "var(--text-muted)",
                          }}
                        >
                          {(t.protocol || "http").toUpperCase()}
                        </span>
                      </span>
                      <span style={{ fontWeight: 600, color: "#4ade80" }}>
                        {formatBytes(t.totalUploaded)}
                      </span>
                    </div>
                    {/* Progress visual bar */}
                    <div
                      style={{
                        height: "6px",
                        width: "100%",
                        backgroundColor: "rgba(255,255,255,0.06)",
                        borderRadius: "3px",
                        overflow: "hidden",
                      }}
                    >
                      <div
                        style={{
                          height: "100%",
                          width: `${Math.max(4, Math.min(100, pct))}%`,
                          backgroundColor:
                            idx === 0
                              ? "#22c55e"
                              : idx === 1
                                ? "#3b82f6"
                                : idx === 2
                                  ? "#a855f7"
                                  : "#eab308",
                          borderRadius: "3px",
                        }}
                      />
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>
      </div>

      {/* Main Trackers Table Section */}
      <div
        className="card"
        style={{
          padding: "1.25rem",
          borderRadius: "8px",
          display: "flex",
          flexDirection: "column",
          gap: "0.85rem",
        }}
      >
        {/* Controls, Filters & Search Bar */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              flex: "1 1 240px",
              minWidth: "200px",
            }}
          >
            <input
              type="text"
              className="form-control"
              placeholder="Search tracker domains, URLs, hosts..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              style={{
                width: "100%",
                padding: "0.35rem 0.65rem",
                fontSize: "0.82rem",
                backgroundColor: "var(--bg-primary)",
                borderRadius: "6px",
              }}
            />
          </div>

          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              flexWrap: "wrap",
            }}
          >
            {/* Protocol Filter */}
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.25rem" }}
            >
              <span style={{ fontSize: "0.75rem", color: "var(--text-dim)" }}>
                Protocol:
              </span>
              {(["ALL", "UDP", "HTTP", "HTTPS"] as ProtocolFilter[]).map(
                (p) => (
                  <button
                    key={p}
                    className={`btn btn-xs ${protocolFilter === p ? "btn-primary" : "btn-outline"}`}
                    style={{ fontSize: "0.72rem", padding: "0.18rem 0.45rem" }}
                    onClick={() => setProtocolFilter(p)}
                  >
                    {p}
                  </button>
                ),
              )}
            </div>

            {/* Status Filter */}
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.25rem" }}
            >
              <span style={{ fontSize: "0.75rem", color: "var(--text-dim)" }}>
                Status:
              </span>
              {(
                ["ALL", "Working", "Degraded", "Offline"] as StatusFilter[]
              ).map((s) => (
                <button
                  key={s}
                  className={`btn btn-xs ${statusFilter === s ? "btn-primary" : "btn-outline"}`}
                  style={{ fontSize: "0.72rem", padding: "0.18rem 0.45rem" }}
                  onClick={() => setStatusFilter(s)}
                >
                  {s}
                </button>
              ))}
            </div>
          </div>
        </div>

        {/* Results summary count */}
        <div style={{ fontSize: "0.78rem", color: "var(--text-muted)" }}>
          Showing {filteredMetrics.length} of {metrics.length} tracked tracker
          servers
        </div>

        {/* Interactive Data Table */}
        <div
          className="torrent-table-wrapper"
          style={{
            overflowX: "auto",
            borderRadius: "6px",
            border: "1px solid var(--border-light)",
          }}
        >
          <table
            className="torrent-table"
            style={{
              fontSize: "0.8rem",
              width: "100%",
              borderCollapse: "collapse",
            }}
          >
            <thead>
              <tr
                style={{
                  backgroundColor: "#161b22",
                  borderBottom: "1px solid var(--border-light)",
                }}
              >
                <th
                  className="torrent-table-th"
                  style={{ cursor: "pointer", padding: "0.5rem 0.65rem" }}
                  onClick={() => {
                    if (sortField === "domain") setSortAsc(!sortAsc);
                    else {
                      setSortField("domain");
                      setSortAsc(true);
                    }
                  }}
                >
                  Tracker Domain / Host{" "}
                  {sortField === "domain" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{ width: "90px", padding: "0.5rem 0.65rem" }}
                >
                  Status
                </th>
                <th
                  className="torrent-table-th"
                  style={{
                    cursor: "pointer",
                    padding: "0.5rem 0.65rem",
                    textAlign: "right",
                  }}
                  onClick={() => {
                    if (sortField === "upload") setSortAsc(!sortAsc);
                    else {
                      setSortField("upload");
                      setSortAsc(false);
                    }
                  }}
                >
                  Uploaded {sortField === "upload" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{
                    cursor: "pointer",
                    padding: "0.5rem 0.65rem",
                    textAlign: "right",
                  }}
                  onClick={() => {
                    if (sortField === "download") setSortAsc(!sortAsc);
                    else {
                      setSortField("download");
                      setSortAsc(false);
                    }
                  }}
                >
                  Downloaded {sortField === "download" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{
                    cursor: "pointer",
                    padding: "0.5rem 0.65rem",
                    textAlign: "center",
                  }}
                  onClick={() => {
                    if (sortField === "successRate") setSortAsc(!sortAsc);
                    else {
                      setSortField("successRate");
                      setSortAsc(false);
                    }
                  }}
                >
                  Success Rate{" "}
                  {sortField === "successRate" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{
                    cursor: "pointer",
                    padding: "0.5rem 0.65rem",
                    textAlign: "center",
                  }}
                  onClick={() => {
                    if (sortField === "announces") setSortAsc(!sortAsc);
                    else {
                      setSortField("announces");
                      setSortAsc(false);
                    }
                  }}
                >
                  Announces {sortField === "announces" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{
                    cursor: "pointer",
                    padding: "0.5rem 0.65rem",
                    textAlign: "right",
                  }}
                  onClick={() => {
                    if (sortField === "latency") setSortAsc(!sortAsc);
                    else {
                      setSortField("latency");
                      setSortAsc(true);
                    }
                  }}
                >
                  Latency {sortField === "latency" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{
                    cursor: "pointer",
                    padding: "0.5rem 0.65rem",
                    textAlign: "right",
                  }}
                  onClick={() => {
                    if (sortField === "peers") setSortAsc(!sortAsc);
                    else {
                      setSortField("peers");
                      setSortAsc(false);
                    }
                  }}
                >
                  Peers {sortField === "peers" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{ cursor: "pointer", padding: "0.5rem 0.65rem" }}
                  onClick={() => {
                    if (sortField === "lastAnnounce") setSortAsc(!sortAsc);
                    else {
                      setSortField("lastAnnounce");
                      setSortAsc(false);
                    }
                  }}
                >
                  Last Active{" "}
                  {sortField === "lastAnnounce" && (sortAsc ? "▲" : "▼")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{
                    textAlign: "right",
                    width: "100px",
                    padding: "0.5rem 0.65rem",
                  }}
                >
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {isLoadingMetrics && metrics.length === 0 ? (
                <tr className="torrent-table-row">
                  <td
                    colSpan={10}
                    style={{
                      textAlign: "center",
                      padding: "2.5rem",
                      color: "var(--text-muted)",
                    }}
                  >
                    Loading tracker metrics telemetry...
                  </td>
                </tr>
              ) : filteredMetrics.length === 0 ? (
                <tr className="torrent-table-row">
                  <td
                    colSpan={10}
                    style={{
                      textAlign: "center",
                      padding: "2.5rem",
                      color: "var(--text-muted)",
                    }}
                  >
                    {metrics.length === 0
                      ? "No tracker announces or scrapes recorded yet"
                      : "No trackers match current filter criteria"}
                  </td>
                </tr>
              ) : (
                filteredMetrics.map((m) => {
                  const statusClass =
                    m.status === "Working"
                      ? "badge-seeding"
                      : m.status === "Degraded"
                        ? "badge-warning"
                        : "badge-error";
                  const proto = (m.protocol || "http").toUpperCase();
                  return (
                    <tr
                      key={m.id}
                      className="torrent-table-row"
                      style={{
                        cursor: "pointer",
                        borderBottom: "1px solid var(--border-light)",
                      }}
                      onClick={() => setSelectedMetric(m)}
                    >
                      {/* Domain / URL */}
                      <td style={{ padding: "0.45rem 0.65rem" }}>
                        <div
                          style={{
                            display: "flex",
                            alignItems: "center",
                            gap: "0.5rem",
                          }}
                        >
                          <TrackerFavicon urlOrHost={m.trackerUrl} size={16} />
                          <div
                            style={{ display: "flex", flexDirection: "column" }}
                          >
                            <span
                              style={{
                                fontWeight: 600,
                                color: "var(--text-primary)",
                              }}
                            >
                              {m.domain || m.host || "Unknown"}
                            </span>
                            <span
                              style={{
                                fontSize: "0.72rem",
                                color: "var(--text-muted)",
                                fontFamily: "monospace",
                                maxWidth: "260px",
                                overflow: "hidden",
                                textOverflow: "ellipsis",
                                whiteSpace: "nowrap",
                              }}
                              title={m.trackerUrl}
                            >
                              {m.trackerUrl}
                            </span>
                          </div>
                        </div>
                      </td>

                      {/* Status */}
                      <td style={{ padding: "0.45rem 0.65rem" }}>
                        <div
                          style={{
                            display: "flex",
                            flexDirection: "column",
                            gap: "0.2rem",
                          }}
                        >
                          <span
                            className={`badge ${statusClass}`}
                            style={{ fontSize: "0.72rem" }}
                          >
                            {m.status}
                          </span>
                          <span
                            style={{
                              fontSize: "0.68rem",
                              color: "var(--text-dim)",
                              backgroundColor: "rgba(255,255,255,0.06)",
                              padding: "0.05rem 0.3rem",
                              borderRadius: "3px",
                              width: "fit-content",
                            }}
                          >
                            {proto}
                          </span>
                        </div>
                      </td>

                      {/* Uploaded */}
                      <td
                        style={{
                          textAlign: "right",
                          padding: "0.45rem 0.65rem",
                          fontWeight: 600,
                          color: "#4ade80",
                        }}
                      >
                        {formatBytes(m.totalUploaded)}
                      </td>

                      {/* Downloaded */}
                      <td
                        style={{
                          textAlign: "right",
                          padding: "0.45rem 0.65rem",
                          color: "#60a5fa",
                        }}
                      >
                        {formatBytes(m.totalDownloaded)}
                      </td>

                      {/* Success Rate */}
                      <td
                        style={{
                          textAlign: "center",
                          padding: "0.45rem 0.65rem",
                        }}
                      >
                        <div
                          style={{
                            display: "inline-flex",
                            flexDirection: "column",
                            alignItems: "center",
                            gap: "0.15rem",
                          }}
                        >
                          <span
                            style={{
                              fontWeight: 600,
                              color:
                                m.announceSuccessRate >= 90
                                  ? "#22c55e"
                                  : m.announceSuccessRate >= 70
                                    ? "#eab308"
                                    : "#ef4444",
                            }}
                          >
                            {m.announceSuccessRate}%
                          </span>
                          <div
                            style={{
                              width: "42px",
                              height: "4px",
                              backgroundColor: "rgba(255,255,255,0.1)",
                              borderRadius: "2px",
                              overflow: "hidden",
                            }}
                          >
                            <div
                              style={{
                                height: "100%",
                                width: `${Math.min(100, Math.max(0, m.announceSuccessRate))}%`,
                                backgroundColor:
                                  m.announceSuccessRate >= 90
                                    ? "#22c55e"
                                    : m.announceSuccessRate >= 70
                                      ? "#eab308"
                                      : "#ef4444",
                              }}
                            />
                          </div>
                        </div>
                      </td>

                      {/* Announces */}
                      <td
                        style={{
                          textAlign: "center",
                          padding: "0.45rem 0.65rem",
                        }}
                      >
                        <span style={{ color: "#e2e8f0" }}>
                          {m.successfulAnnounces}
                        </span>
                        <span
                          style={{
                            color: "var(--text-dim)",
                            fontSize: "0.75rem",
                          }}
                        >
                          {" "}
                          / {m.totalAnnounces}
                        </span>
                      </td>

                      {/* Latency */}
                      <td
                        style={{
                          textAlign: "right",
                          padding: "0.45rem 0.65rem",
                        }}
                      >
                        <span
                          style={{
                            fontWeight: 600,
                            color:
                              m.avgResponseTimeMs < 100
                                ? "#22c55e"
                                : m.avgResponseTimeMs < 350
                                  ? "#fbbf24"
                                  : "#f87171",
                          }}
                        >
                          {Math.round(m.avgResponseTimeMs)} ms
                        </span>
                      </td>

                      {/* Peers Discovered */}
                      <td
                        style={{
                          textAlign: "right",
                          padding: "0.45rem 0.65rem",
                          color: "#c084fc",
                          fontWeight: 500,
                        }}
                      >
                        {m.totalPeersDiscovered.toLocaleString()}
                      </td>

                      {/* Last Active */}
                      <td
                        style={{
                          padding: "0.45rem 0.65rem",
                          color: "var(--text-muted)",
                          fontSize: "0.75rem",
                        }}
                      >
                        {formatDate(m.lastAnnounce || m.lastScrape)}
                      </td>

                      {/* Actions */}
                      <td
                        style={{
                          textAlign: "right",
                          padding: "0.45rem 0.65rem",
                        }}
                        onClick={(e) => e.stopPropagation()}
                      >
                        <div style={{ display: "inline-flex", gap: "0.3rem" }}>
                          <button
                            className="btn btn-outline btn-xs"
                            onClick={() => setSelectedMetric(m)}
                            title="Inspect tracker history"
                            style={{
                              fontSize: "0.72rem",
                              padding: "0.15rem 0.4rem",
                            }}
                          >
                            📊
                          </button>
                          <button
                            className="btn btn-outline btn-xs"
                            onClick={() => handleReset(m)}
                            title="Reset statistics"
                            style={{
                              fontSize: "0.72rem",
                              padding: "0.15rem 0.4rem",
                            }}
                          >
                            🔄
                          </button>
                          <button
                            className="btn btn-danger btn-xs"
                            onClick={() => handleDelete(m)}
                            title="Delete tracker metric"
                            style={{
                              fontSize: "0.72rem",
                              padding: "0.15rem 0.4rem",
                            }}
                          >
                            ✕
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Tracker Details / History Modal */}
      {selectedMetric && (
        <TrackerMetricDetailModal
          metric={selectedMetric}
          onClose={() => setSelectedMetric(null)}
          onReset={() => handleReset(selectedMetric)}
        />
      )}
    </div>
  );
}

export interface ChartPointsResult {
  maxTraffic: number;
  maxAnnounce: number;
  pointsUpload: string;
  pointsDownload: string;
  pointsAnnounce: string;
}

export function calculateHourlyActivityPoints(
  data: HourlyTrafficPoint[],
  width = 600,
  height = 160,
  padding = 20,
): ChartPointsResult {
  const safeData = data ?? [];
  const maxTraffic = Math.max(
    ...safeData.map((d) => {
      const up = Number.isFinite(d?.uploaded) ? Math.max(0, d.uploaded) : 0;
      const down = Number.isFinite(d?.downloaded)
        ? Math.max(0, d.downloaded)
        : 0;
      return Math.max(up, down);
    }),
    1,
  );
  const maxAnnounce = Math.max(
    ...safeData.map((d) => {
      return Number.isFinite(d?.announces) ? Math.max(0, d.announces) : 0;
    }),
    1,
  );

  const safeMaxTraffic =
    Number.isFinite(maxTraffic) && maxTraffic > 0 ? maxTraffic : 1;
  const safeMaxAnnounce =
    Number.isFinite(maxAnnounce) && maxAnnounce > 0 ? maxAnnounce : 1;

  const count = safeData.length;
  const divisor = count > 1 ? count - 1 : 1;
  const innerWidth = width - 2 * padding;
  const innerHeight = height - 2 * padding;
  const fallbackY = height - padding;

  const pointsUpload = safeData
    .map((d, i) => {
      const rawX = padding + (i / divisor) * innerWidth;
      const x = Number.isFinite(rawX) ? rawX : padding;
      const val = Number.isFinite(d?.uploaded) ? Math.max(0, d.uploaded) : 0;
      const rawY = height - padding - (val / safeMaxTraffic) * innerHeight;
      const y = Number.isFinite(rawY) ? rawY : fallbackY;
      return `${x},${y}`;
    })
    .join(" ");

  const pointsDownload = safeData
    .map((d, i) => {
      const rawX = padding + (i / divisor) * innerWidth;
      const x = Number.isFinite(rawX) ? rawX : padding;
      const val = Number.isFinite(d?.downloaded)
        ? Math.max(0, d.downloaded)
        : 0;
      const rawY = height - padding - (val / safeMaxTraffic) * innerHeight;
      const y = Number.isFinite(rawY) ? rawY : fallbackY;
      return `${x},${y}`;
    })
    .join(" ");

  const pointsAnnounce = safeData
    .map((d, i) => {
      const rawX = padding + (i / divisor) * innerWidth;
      const x = Number.isFinite(rawX) ? rawX : padding;
      const val = Number.isFinite(d?.announces) ? Math.max(0, d.announces) : 0;
      const rawY = height - padding - (val / safeMaxAnnounce) * innerHeight;
      const y = Number.isFinite(rawY) ? rawY : fallbackY;
      return `${x},${y}`;
    })
    .join(" ");

  return {
    maxTraffic: safeMaxTraffic,
    maxAnnounce: safeMaxAnnounce,
    pointsUpload,
    pointsDownload,
    pointsAnnounce,
  };
}

// 24h Hourly Svg Line Chart component
export function HourlyActivitySvgChart({
  data,
}: {
  data: HourlyTrafficPoint[];
}) {
  if (!data || data.length === 0) {
    return (
      <div
        style={{
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          color: "var(--text-muted)",
          fontSize: "0.85rem",
        }}
      >
        Collecting 24-hour activity telemetry...
      </div>
    );
  }

  const width = 600;
  const height = 160;
  const padding = 20;

  const {
    maxTraffic,
    maxAnnounce,
    pointsUpload,
    pointsDownload,
    pointsAnnounce,
  } = calculateHourlyActivityPoints(data, width, height, padding);

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      style={{ width: "100%", height: "100%", overflow: "visible" }}
    >
      {/* Background Grid Lines */}
      <line
        x1={padding}
        y1={padding}
        x2={width - padding}
        y2={padding}
        stroke="rgba(255,255,255,0.06)"
        strokeDasharray="3 3"
      />
      <line
        x1={padding}
        y1={height / 2}
        x2={width - padding}
        y2={height / 2}
        stroke="rgba(255,255,255,0.06)"
        strokeDasharray="3 3"
      />
      <line
        x1={padding}
        y1={height - padding}
        x2={width - padding}
        y2={height - padding}
        stroke="rgba(255,255,255,0.12)"
      />

      {/* Scale indicators / axis labels */}
      <text
        x={padding}
        y={padding - 6}
        fill="#4ade80"
        fontSize="9"
        fontWeight="500"
        textAnchor="start"
      >
        Traffic Max: {formatBytes(maxTraffic)}
      </text>
      <text
        x={width - padding}
        y={padding - 6}
        fill="#f59e0b"
        fontSize="9"
        fontWeight="500"
        textAnchor="end"
      >
        Announces Max: {maxAnnounce}
      </text>

      {/* Upload Line */}
      <polyline
        fill="none"
        stroke="#4ade80"
        strokeWidth="2.5"
        points={pointsUpload}
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <title>{`Upload Volume (Peak: ${formatBytes(maxTraffic)})`}</title>
      </polyline>

      {/* Download Line */}
      <polyline
        stroke="#60a5fa"
        points={pointsDownload}
        fill="none"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <title>{`Download Volume (Peak: ${formatBytes(maxTraffic)})`}</title>
      </polyline>

      {/* Announce Line */}
      <polyline
        fill="none"
        stroke="#f59e0b"
        strokeWidth="2"
        strokeDasharray="4 3"
        points={pointsAnnounce}
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <title>{`Announce Count (Peak: ${maxAnnounce} announces - scaled independently)`}</title>
      </polyline>

      {/* Data Points */}
      {data.map((d, i) => {
        const rawX =
          padding + (i / (data.length - 1 || 1)) * (width - 2 * padding);
        const x = Number.isFinite(rawX) ? rawX : padding;

        const upVal = Number.isFinite(d?.uploaded)
          ? Math.max(0, d.uploaded)
          : 0;
        const downVal = Number.isFinite(d?.downloaded)
          ? Math.max(0, d.downloaded)
          : 0;
        const annVal = Number.isFinite(d?.announces)
          ? Math.max(0, d.announces)
          : 0;

        const rawYUp =
          height - padding - (upVal / maxTraffic) * (height - 2 * padding);
        const yUp = Number.isFinite(rawYUp) ? rawYUp : height - padding;

        const rawYDown =
          height - padding - (downVal / maxTraffic) * (height - 2 * padding);
        const yDown = Number.isFinite(rawYDown) ? rawYDown : height - padding;

        return (
          <g key={i}>
            <circle cx={x} cy={yUp} r="3" fill="#22c55e">
              <title>{`${d?.timeLabel || ""}: ${formatBytes(upVal)} uploaded, ${formatBytes(downVal)} downloaded, ${annVal} announces`}</title>
            </circle>
            <circle cx={x} cy={yDown} r="2.5" fill="#60a5fa">
              <title>{`${d?.timeLabel || ""}: ${formatBytes(downVal)} downloaded`}</title>
            </circle>
          </g>
        );
      })}
    </svg>
  );
}

// Modal Detail View for a Tracker Metric
function TrackerMetricDetailModal({
  metric,
  onClose,
  onReset,
}: {
  metric: TrackerMetric;
  onClose: () => void;
  onReset: () => void;
}) {
  const { data: history = [], isLoading } = useTrackerMetricHistory(
    metric.id,
    24,
  );

  return (
    <div
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.75)",
        zIndex: 9999,
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        padding: "1rem",
      }}
      onClick={onClose}
    >
      <div
        className="card"
        style={{
          width: "100%",
          maxWidth: "760px",
          maxHeight: "90vh",
          overflowY: "auto",
          borderRadius: "8px",
          padding: "1.5rem",
          display: "flex",
          flexDirection: "column",
          gap: "1rem",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Modal Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "flex-start",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}>
            <TrackerFavicon urlOrHost={metric.trackerUrl} size={22} />
            <div>
              <h2 style={{ margin: 0, fontSize: "1.2rem" }}>
                {metric.domain || metric.host}
              </h2>
              <div
                style={{
                  fontFamily: "monospace",
                  fontSize: "0.78rem",
                  color: "var(--text-muted)",
                  wordBreak: "break-all",
                }}
              >
                {metric.trackerUrl}
              </div>
            </div>
          </div>
          <button className="btn btn-outline btn-sm" onClick={onClose}>
            ✕
          </button>
        </div>

        {/* Quick Stats Matrix */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
            gap: "0.6rem",
            padding: "0.85rem",
            backgroundColor: "var(--bg-primary)",
            borderRadius: "6px",
          }}
        >
          <div>
            <div style={{ fontSize: "0.72rem", color: "var(--text-muted)" }}>
              Status
            </div>
            <div
              style={{
                fontWeight: 600,
                color: metric.status === "Working" ? "#4ade80" : "#f87171",
              }}
            >
              {metric.status} ({metric.protocol.toUpperCase()}:{metric.port})
            </div>
          </div>
          <div>
            <div style={{ fontSize: "0.72rem", color: "var(--text-muted)" }}>
              Total Upload
            </div>
            <div style={{ fontWeight: 600, color: "#4ade80" }}>
              {formatBytes(metric.totalUploaded)}
            </div>
          </div>
          <div>
            <div style={{ fontSize: "0.72rem", color: "var(--text-muted)" }}>
              Total Download
            </div>
            <div style={{ fontWeight: 600, color: "#60a5fa" }}>
              {formatBytes(metric.totalDownloaded)}
            </div>
          </div>
          <div>
            <div style={{ fontSize: "0.72rem", color: "var(--text-muted)" }}>
              Announce Success
            </div>
            <div style={{ fontWeight: 600, color: "#22c55e" }}>
              {metric.announceSuccessRate}% ({metric.successfulAnnounces}/
              {metric.totalAnnounces})
            </div>
          </div>
          <div>
            <div style={{ fontSize: "0.72rem", color: "var(--text-muted)" }}>
              Latency (Avg / Last)
            </div>
            <div style={{ fontWeight: 600, color: "#fbbf24" }}>
              {Math.round(metric.avgResponseTimeMs)}ms /{" "}
              {metric.lastResponseTimeMs}ms
            </div>
          </div>
          <div>
            <div style={{ fontSize: "0.72rem", color: "var(--text-muted)" }}>
              Peers Discovered
            </div>
            <div style={{ fontWeight: 600, color: "#c084fc" }}>
              {metric.totalPeersDiscovered.toLocaleString()}
            </div>
          </div>
        </div>

        {/* Error message if present */}
        {metric.lastErrorMessage && (
          <div
            style={{
              padding: "0.6rem 0.85rem",
              borderRadius: "6px",
              backgroundColor: "rgba(239, 68, 68, 0.12)",
              border: "1px solid rgba(239, 68, 68, 0.3)",
              fontSize: "0.8rem",
              color: "#fca5a5",
            }}
          >
            <strong>Last Error:</strong> {metric.lastErrorMessage} (
            {formatDate(metric.lastErrorTime)})
          </div>
        )}

        {/* History Snapshots List */}
        <div>
          <h4 style={{ margin: "0.5rem 0 0.5rem 0", fontSize: "0.9rem" }}>
            Recent 24h Interaction Snapshots ({history.length})
          </h4>
          <div
            style={{
              maxHeight: "240px",
              overflowY: "auto",
              backgroundColor: "#0d1117",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
            }}
          >
            <table
              className="torrent-table"
              style={{ fontSize: "0.75rem", width: "100%" }}
            >
              <thead>
                <tr style={{ backgroundColor: "#161b22" }}>
                  <th className="torrent-table-th">Time</th>
                  <th className="torrent-table-th">Operation</th>
                  <th className="torrent-table-th">Outcome</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Latency
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Peers
                  </th>
                </tr>
              </thead>
              <tbody style={{ fontFamily: "monospace" }}>
                {isLoading ? (
                  <tr>
                    <td
                      colSpan={5}
                      style={{
                        textAlign: "center",
                        padding: "1.5rem",
                        color: "var(--text-muted)",
                      }}
                    >
                      Loading interaction snapshots...
                    </td>
                  </tr>
                ) : history.length === 0 ? (
                  <tr>
                    <td
                      colSpan={5}
                      style={{
                        textAlign: "center",
                        padding: "1.5rem",
                        color: "var(--text-muted)",
                      }}
                    >
                      No snapshots recorded yet in the last 24 hours.
                    </td>
                  </tr>
                ) : (
                  history.map((h: TrackerMetricSnapshot) => (
                    <tr
                      key={h.id}
                      className="torrent-table-row"
                      style={{
                        borderBottom: "1px solid var(--border-light)",
                      }}
                    >
                      <td style={{ color: "#8b949e" }}>
                        {formatDate(h.timestamp)}
                      </td>
                      <td>{h.operation}</td>
                      <td>
                        <span
                          style={{
                            color: h.isSuccess ? "#4ade80" : "#f87171",
                            fontWeight: 600,
                          }}
                        >
                          {h.isSuccess ? "✓ SUCCESS" : "✗ FAILED"}
                        </span>
                      </td>
                      <td style={{ textAlign: "right", color: "#fbbf24" }}>
                        {h.responseTimeMs} ms
                      </td>
                      <td style={{ textAlign: "right", color: "#c084fc" }}>
                        {h.peersDiscovered}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Modal Actions */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginTop: "0.5rem",
          }}
        >
          <button className="btn btn-danger btn-sm" onClick={onReset}>
            Reset Stats
          </button>
          <button className="btn btn-primary btn-sm" onClick={onClose}>
            Done
          </button>
        </div>
      </div>
    </div>
  );
}

export default TrackerMetrics;
