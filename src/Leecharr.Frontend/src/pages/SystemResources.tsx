import { useState, useMemo } from "react";
import { Link } from "react-router";
import { useSystemResources } from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatUptime,
  formatSeconds,
} from "../utils/formatters";
import type { TorrentResourceMetrics } from "../api/types";
import { useTranslation } from "../i18n";

export function SystemResources() {
  const { t } = useTranslation();

  const [refreshInterval, setRefreshInterval] = useState<number | false>(2000);
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [selectedTorrent, setSelectedTorrent] =
    useState<TorrentResourceMetrics | null>(null);

  const {
    data: snapshot,
    isLoading,
    isFetching,
    refetch,
  } = useSystemResources(refreshInterval);

  const host = snapshot?.host;
  const engine = snapshot?.torrentEngine;
  const subsystems = snapshot?.subsystems ?? [];
  const perTorrent = snapshot?.perTorrent ?? [];

  const filteredTorrents = useMemo(() => {
    return perTorrent.filter((torrent) => {
      const matchesSearch =
        !searchQuery ||
        (torrent.name || "")
          .toLowerCase()
          .includes(searchQuery.toLowerCase()) ||
        (torrent.infoHash || "")
          .toLowerCase()
          .includes(searchQuery.toLowerCase()) ||
        (torrent.category || "")
          .toLowerCase()
          .includes(searchQuery.toLowerCase());

      const matchesStatus =
        statusFilter === "all" ||
        (torrent.status || "").toLowerCase() === statusFilter.toLowerCase();

      return matchesSearch && matchesStatus;
    });
  }, [perTorrent, searchQuery, statusFilter]);

  // Status color mapper
  const getStatusBadge = (status?: string | null) => {
    const s = (status || "").toLowerCase();
    if (s === "downloading") return "badge-downloading";
    if (s === "seeding") return "badge-seeding";
    if (s === "paused") return "badge-paused";
    if (s === "checking") return "badge-warning";
    if (s === "error") return "badge-error";
    return "badge-muted";
  };

  return (
    <div className="content-area">
      {/* Page Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.25rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {t("system.realTimeTelemetry")}
            </h1>
            <span className="badge badge-primary">{t("system.realTime")}</span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.35rem",
                fontSize: "0.75rem",
                color: refreshInterval ? "#2ecc71" : "var(--text-muted)",
                backgroundColor: refreshInterval
                  ? "rgba(46, 204, 113, 0.12)"
                  : "rgba(255, 255, 255, 0.05)",
                padding: "0.2rem 0.5rem",
                borderRadius: "12px",
                border: refreshInterval
                  ? "1px solid rgba(46, 204, 113, 0.3)"
                  : "1px solid rgba(255, 255, 255, 0.1)",
              }}
            >
              <span
                style={{
                  width: "7px",
                  height: "7px",
                  borderRadius: "50%",
                  backgroundColor: refreshInterval ? "#2ecc71" : "#888",
                  boxShadow: refreshInterval ? "0 0 8px #2ecc71" : "none",
                  animation: refreshInterval ? "pulse 1.8s infinite" : "none",
                }}
              />
              {refreshInterval ? "Live Stream Active" : "Paused"}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("system.realTimeHardwareUtilization")}
          </div>
        </div>

        {/* Header Right Actions */}
        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <div
            className="btn-group"
            style={{
              display: "inline-flex",
              backgroundColor: "rgba(0, 0, 0, 0.25)",
              borderRadius: "4px",
              padding: "2px",
              border: "1px solid rgba(255, 255, 255, 0.08)",
            }}
          >
            {[
              { label: "1s", val: 1000 },
              { label: "2s", val: 2000 },
              { label: "5s", val: 5000 },
              { label: "Pause", val: false },
            ].map((btn) => (
              <button
                key={String(btn.val)}
                className={`btn btn-small ${refreshInterval === btn.val ? "btn-primary" : "btn-link"}`}
                style={{
                  padding: "0.2rem 0.55rem",
                  fontSize: "0.75rem",
                  minWidth: "36px",
                }}
                onClick={() => setRefreshInterval(btn.val as number | false)}
              >
                {btn.label}
              </button>
            ))}
          </div>

          <button
            className="btn btn-small"
            onClick={() => refetch()}
            disabled={isFetching}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.35rem",
            }}
          >
            <span>🔄</span> {t("common.refresh")}
          </button>
        </div>
      </div>

      {isLoading && <p className="loading">{t("common.loading")}</p>}

      {/* Hero Metric Cards (4 Columns) */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
          gap: "1rem",
          marginBottom: "1.25rem",
        }}
      >
        {/* Card 1: CPU & Threads */}
        <div
          className="card"
          style={{
            padding: "1.1rem",
            borderRadius: "8px",
            border: "1px solid rgba(255, 255, 255, 0.08)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            background: "var(--bg-secondary, #171b35)",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "0.82rem",
                fontWeight: 600,
                color: "var(--text-secondary, #c7c5d3)",
                textTransform: "uppercase",
                letterSpacing: "0.5px",
              }}
            >
              {t("system.processCpuThreads")}
            </span>
            <span
              style={{
                fontSize: "0.75rem",
                padding: "0.15rem 0.4rem",
                borderRadius: "4px",
                backgroundColor: "rgba(255, 209, 102, 0.15)",
                color: "var(--accent, #ffd166)",
              }}
            >
              {host?.cpuCores ?? 1} {t("system.cores")}
            </span>
          </div>
          <div
            style={{
              display: "flex",
              alignItems: "baseline",
              gap: "0.5rem",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "1.9rem",
                fontWeight: 700,
                color:
                  (host?.cpuProcessPercent ?? 0) > 75
                    ? "#e74c3c"
                    : (host?.cpuProcessPercent ?? 0) > 40
                      ? "#f39c12"
                      : "#2ecc71",
              }}
            >
              {(host?.cpuProcessPercent ?? 0).toFixed(1)}%
            </span>
            <span style={{ fontSize: "0.78rem", color: "var(--text-muted)" }}>
              {t("system.processLoad")}
            </span>
          </div>
          <div
            style={{
              width: "100%",
              height: "6px",
              backgroundColor: "rgba(255, 255, 255, 0.08)",
              borderRadius: "3px",
              overflow: "hidden",
              marginBottom: "0.75rem",
            }}
          >
            <div
              style={{
                width: `${Math.min(100, Math.max(0, host?.cpuProcessPercent ?? 0))}%`,
                height: "100%",
                backgroundColor:
                  (host?.cpuProcessPercent ?? 0) > 75
                    ? "#e74c3c"
                    : (host?.cpuProcessPercent ?? 0) > 40
                      ? "#f39c12"
                      : "#2ecc71",
                transition: "width 0.4s ease",
              }}
            />
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: "0.4rem",
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
            }}
          >
            <div>
              {t("system.threads")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {host?.threadCount ?? 0}
              </strong>
            </div>
            <div>
              {t("system.handles")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {host?.handleCount ?? 0}
              </strong>
            </div>
            <div>
              {t("system.threadpoolActive")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {host?.threadPoolWorkerThreads ?? 0}
              </strong>
            </div>
            <div>
              {t("system.uptime")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {formatUptime(host?.uptimeSeconds)}
              </strong>
            </div>
          </div>
        </div>

        {/* Card 2: Memory & Managed GC */}
        <div
          className="card"
          style={{
            padding: "1.1rem",
            borderRadius: "8px",
            border: "1px solid rgba(255, 255, 255, 0.08)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            background: "var(--bg-secondary, #171b35)",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "0.82rem",
                fontWeight: 600,
                color: "var(--text-secondary, #c7c5d3)",
                textTransform: "uppercase",
                letterSpacing: "0.5px",
              }}
            >
              {t("system.ramManagedGc")}
            </span>
            <span
              style={{
                fontSize: "0.75rem",
                padding: "0.15rem 0.4rem",
                borderRadius: "4px",
                backgroundColor: "rgba(52, 152, 219, 0.15)",
                color: "#3498db",
              }}
            >
              {t("system.residentSet")}
            </span>
          </div>
          <div
            style={{
              display: "flex",
              alignItems: "baseline",
              gap: "0.5rem",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "1.9rem",
                fontWeight: 700,
                color: "var(--accent, #ffd166)",
              }}
            >
              {formatBytes(host?.workingSetBytes ?? 0)}
            </span>
            <span style={{ fontSize: "0.78rem", color: "var(--text-muted)" }}>
              {t("system.workingSet")}
            </span>
          </div>
          <div
            style={{
              width: "100%",
              height: "6px",
              backgroundColor: "rgba(255, 255, 255, 0.08)",
              borderRadius: "3px",
              overflow: "hidden",
              marginBottom: "0.75rem",
            }}
          >
            <div
              style={{
                width: `${Math.min(100, Math.max(5, ((host?.managedHeapBytes ?? 0) / Math.max(1, host?.workingSetBytes ?? 1)) * 100))}%`,
                height: "100%",
                backgroundColor: "#3498db",
                transition: "width 0.4s ease",
              }}
            />
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: "0.4rem",
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
            }}
          >
            <div>
              {t("system.managedHeap")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {formatBytes(host?.managedHeapBytes ?? 0)}
              </strong>
            </div>
            <div>
              {t("system.privateBytes")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {formatBytes(host?.privateMemoryBytes ?? 0)}
              </strong>
            </div>
            <div>
              {t("system.gcGen01")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {host?.gcGen0Collections ?? 0} / {host?.gcGen1Collections ?? 0}
              </strong>
            </div>
            <div>
              {t("system.gcGen2")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {host?.gcGen2Collections ?? 0}
              </strong>
            </div>
          </div>
        </div>

        {/* Card 3: Disk I/O & Dynamic Write Cache */}
        <div
          className="card"
          style={{
            padding: "1.1rem",
            borderRadius: "8px",
            border: "1px solid rgba(255, 255, 255, 0.08)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            background: "var(--bg-secondary, #171b35)",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "0.82rem",
                fontWeight: 600,
                color: "var(--text-secondary, #c7c5d3)",
                textTransform: "uppercase",
                letterSpacing: "0.5px",
              }}
            >
              {t("system.diskIoCache")}
            </span>
            <span
              style={{
                fontSize: "0.75rem",
                padding: "0.15rem 0.4rem",
                borderRadius: "4px",
                backgroundColor: "rgba(46, 204, 113, 0.15)",
                color: "#2ecc71",
              }}
            >
              {engine?.diskCacheHitRatio ?? 100}
              {t("system.hitRatio")}
            </span>
          </div>
          <div
            style={{
              display: "flex",
              alignItems: "baseline",
              gap: "0.5rem",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "1.9rem",
                fontWeight: 700,
                color: "#2ecc71",
              }}
            >
              {formatSpeed(engine?.diskWriteRate ?? 0)}
            </span>
            <span style={{ fontSize: "0.78rem", color: "var(--text-muted)" }}>
              {t("system.writeThroughput")}
            </span>
          </div>
          <div
            style={{
              width: "100%",
              height: "6px",
              backgroundColor: "rgba(255, 255, 255, 0.08)",
              borderRadius: "3px",
              overflow: "hidden",
              marginBottom: "0.75rem",
            }}
          >
            <div
              style={{
                width: `${Math.min(100, Math.max(3, ((engine?.diskCacheBytesAllocated ?? 0) / Math.max(1, engine?.diskCacheCapacityBytes ?? 1)) * 100))}%`,
                height: "100%",
                backgroundColor: "#2ecc71",
                transition: "width 0.4s ease",
              }}
            />
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: "0.4rem",
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
            }}
          >
            <div>
              {t("system.cacheAlloc")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {formatBytes(engine?.diskCacheBytesAllocated ?? 0)} /{" "}
                {formatBytes(engine?.diskCacheCapacityBytes ?? 0)}
              </strong>
            </div>
            <div>
              {t("system.pendingQueue")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {engine?.diskPendingWrites ?? 0} {t("system.blocks")}
              </strong>
            </div>
            <div>
              {t("system.diskRead")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {formatSpeed(engine?.diskReadRate ?? 0)}
              </strong>
            </div>
            <div>
              {t("system.totalWritten")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {formatBytes(engine?.diskTotalBytesWritten ?? 0)}
              </strong>
            </div>
          </div>
        </div>

        {/* Card 4: Swarm Network & Efficiency */}
        <div
          className="card"
          style={{
            padding: "1.1rem",
            borderRadius: "8px",
            border: "1px solid rgba(255, 255, 255, 0.08)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            background: "var(--bg-secondary, #171b35)",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "0.82rem",
                fontWeight: 600,
                color: "var(--text-secondary, #c7c5d3)",
                textTransform: "uppercase",
                letterSpacing: "0.5px",
              }}
            >
              {t("system.swarmNetworkSockets")}
            </span>
            <span
              style={{
                fontSize: "0.75rem",
                padding: "0.15rem 0.4rem",
                borderRadius: "4px",
                backgroundColor: "rgba(155, 89, 182, 0.15)",
                color: "#9b59b6",
              }}
            >
              {engine?.dhtNodeCount ?? 0} {t("system.dhtNodes")}
            </span>
          </div>
          <div
            style={{
              display: "flex",
              alignItems: "baseline",
              gap: "0.5rem",
              marginBottom: "0.6rem",
            }}
          >
            <span
              style={{
                fontSize: "1.9rem",
                fontWeight: 700,
                color: "#9b59b6",
              }}
            >
              {engine?.openConnections ?? 0}
            </span>
            <span style={{ fontSize: "0.78rem", color: "var(--text-muted)" }}>
              {t("system.openSockets")}
              {engine?.maxConnections ?? 300} {t("system.max")}
            </span>
          </div>
          <div
            style={{
              width: "100%",
              height: "6px",
              backgroundColor: "rgba(255, 255, 255, 0.08)",
              borderRadius: "3px",
              overflow: "hidden",
              marginBottom: "0.75rem",
            }}
          >
            <div
              style={{
                width: `${Math.min(100, Math.max(2, ((engine?.openConnections ?? 0) / Math.max(1, engine?.maxConnections ?? 300)) * 100))}%`,
                height: "100%",
                backgroundColor: "#9b59b6",
                transition: "width 0.4s ease",
              }}
            />
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: "0.4rem",
              fontSize: "0.78rem",
              color: "var(--text-secondary)",
            }}
          >
            <div>
              {t("system.tcpUtp")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {engine?.tcpConnectionsCount ?? 0} /{" "}
                {engine?.utpConnectionsCount ?? 0}
              </strong>
            </div>
            <div>
              {t("system.encryptedRc4")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {engine?.encryptedConnectionsCount ?? 0}
              </strong>
            </div>
            <div>
              {t("system.seedsLeechers")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {engine?.connectedSeeds ?? 0} / {engine?.connectedLeechers ?? 0}
              </strong>
            </div>
            <div>
              {t("system.overheadRatio")}{" "}
              <strong style={{ color: "var(--text-primary)" }}>
                {engine?.protocolOverheadPercentage ?? 0}%
              </strong>
            </div>
          </div>
        </div>
      </div>

      {/* Subsystems Telemetry Grid */}
      <div
        className="card"
        style={{
          padding: "1.25rem",
          borderRadius: "8px",
          border: "1px solid rgba(255, 255, 255, 0.08)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          marginBottom: "1.25rem",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
          }}
        >
          <div>
            <h2
              style={{
                fontSize: "1.05rem",
                fontWeight: 600,
                color: "var(--accent, #ffd166)",
                margin: 0,
              }}
            >
              {t("system.subsystemsOperationalStatus")}
            </h2>
            <div
              style={{
                fontSize: "0.78rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {t("system.activeModularSubsystems")}
            </div>
          </div>
          <Link
            to="/settings/host"
            className="btn btn-small"
            style={{ fontSize: "0.75rem" }}
          >
            {t("system.configureSubsystems")}
          </Link>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "0.85rem",
          }}
        >
          {subsystems.map((sub) => (
            <div
              key={sub.subsystemId}
              style={{
                backgroundColor: "rgba(0, 0, 0, 0.22)",
                borderRadius: "6px",
                border: "1px solid rgba(255, 255, 255, 0.06)",
                padding: "0.85rem",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "flex-start",
                  marginBottom: "0.4rem",
                }}
              >
                <div>
                  <div
                    style={{
                      fontWeight: 600,
                      fontSize: "0.88rem",
                      color: "var(--text-primary)",
                    }}
                  >
                    {sub.subsystemName}
                  </div>
                  <div
                    style={{
                      fontSize: "0.74rem",
                      color: "var(--text-muted)",
                    }}
                  >
                    {t("system.provider")}{" "}
                    <span style={{ color: "var(--accent)" }}>
                      {sub.activeProvider}
                    </span>
                  </div>
                </div>
                <span
                  style={{
                    fontSize: "0.72rem",
                    padding: "0.15rem 0.45rem",
                    borderRadius: "4px",
                    backgroundColor:
                      sub.status === "Healthy"
                        ? "rgba(46, 204, 113, 0.15)"
                        : "rgba(243, 156, 18, 0.15)",
                    color: sub.status === "Healthy" ? "#2ecc71" : "#f39c12",
                  }}
                >
                  ● {sub.status}
                </span>
              </div>

              {/* Subsystem specific metric tags */}
              <div
                style={{
                  display: "flex",
                  flexWrap: "wrap",
                  gap: "0.35rem",
                  marginTop: "0.5rem",
                }}
              >
                {Object.entries(sub.metrics ?? {}).map(([k, v]) => (
                  <span
                    key={k}
                    style={{
                      fontSize: "0.7rem",
                      padding: "0.1rem 0.4rem",
                      borderRadius: "3px",
                      backgroundColor: "rgba(255, 255, 255, 0.05)",
                      color: "var(--text-secondary)",
                      border: "1px solid rgba(255, 255, 255, 0.04)",
                    }}
                  >
                    {k}:{" "}
                    <strong style={{ color: "var(--text-primary)" }}>
                      {typeof v === "boolean"
                        ? v
                          ? "Yes"
                          : "No"
                        : typeof v === "number" &&
                            k.toLowerCase().includes("speed")
                          ? formatSpeed(v)
                          : String(v)}
                    </strong>
                  </span>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Per-Torrent Resource Inspector */}
      <div
        className="card"
        style={{
          padding: "1.25rem",
          borderRadius: "8px",
          border: "1px solid rgba(255, 255, 255, 0.08)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: "0.75rem",
            marginBottom: "1rem",
          }}
        >
          <div>
            <h2
              style={{
                fontSize: "1.05rem",
                fontWeight: 600,
                color: "var(--accent, #ffd166)",
                margin: 0,
              }}
            >
              {t("system.perTorrentBreakdown")}
            </h2>
            <div
              style={{
                fontSize: "0.78rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {t("system.sessionTelemetryPerSwarm")}
            </div>
          </div>

          {/* Filter Bar */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              flexWrap: "wrap",
            }}
          >
            <input
              type="text"
              placeholder={t("system.filterTorrents")}
              className="form-control"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              style={{
                padding: "0.3rem 0.6rem",
                fontSize: "0.8rem",
                width: "180px",
              }}
            />

            <div
              className="btn-group"
              style={{
                display: "inline-flex",
                backgroundColor: "rgba(0, 0, 0, 0.25)",
                borderRadius: "4px",
                padding: "2px",
                border: "1px solid rgba(255, 255, 255, 0.08)",
              }}
            >
              {["all", "downloading", "seeding", "paused"].map((st) => (
                <button
                  key={st}
                  className={`btn btn-small ${statusFilter === st ? "btn-primary" : "btn-link"}`}
                  style={{
                    padding: "0.2rem 0.55rem",
                    fontSize: "0.75rem",
                    textTransform: "capitalize",
                  }}
                  onClick={() => setStatusFilter(st)}
                >
                  {st}
                </button>
              ))}
            </div>

            <span
              className="badge badge-primary"
              style={{ fontSize: "0.75rem" }}
            >
              {filteredTorrents.length} {t("system.swarms")}
            </span>
          </div>
        </div>

        {/* Table */}
        {filteredTorrents.length === 0 ? (
          <div
            style={{
              padding: "2.5rem 1rem",
              textAlign: "center",
              color: "var(--text-muted)",
            }}
          >
            {t("system.noActiveTorrentsMatch")}
          </div>
        ) : (
          <div className="table-responsive">
            <table className="table" style={{ width: "100%", margin: 0 }}>
              <thead>
                <tr>
                  <th style={{ minWidth: "180px" }}>
                    {t("system.torrentCategory")}
                  </th>
                  <th>{t("common.status")}</th>
                  <th>{t("system.payloadSpeed")}</th>
                  <th>{t("system.protocolOverhead")}</th>
                  <th>{t("system.bufferMemory")}</th>
                  <th>{t("system.peersSeedsLeech")}</th>
                  <th>{t("system.transportCrypto")}</th>
                  <th>{t("system.piecesVerification")}</th>
                  <th>{t("system.availability")}</th>
                  <th style={{ textAlign: "right" }}>{t("system.inspect")}</th>
                </tr>
              </thead>
              <tbody>
                {filteredTorrents.map((torrent) => (
                  <tr key={torrent.torrentId}>
                    <td>
                      <div
                        style={{
                          fontWeight: 600,
                          fontSize: "0.85rem",
                          color: "var(--text-primary)",
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                          maxWidth: "240px",
                        }}
                        title={torrent.name}
                      >
                        {torrent.name}
                      </div>
                      <div
                        style={{
                          fontSize: "0.72rem",
                          color: "var(--text-muted)",
                          display: "flex",
                          gap: "0.4rem",
                          alignItems: "center",
                        }}
                      >
                        {torrent.category ? (
                          <span className="badge badge-small">
                            {torrent.category}
                          </span>
                        ) : null}
                        <span>{formatBytes(torrent.totalBytes)}</span>
                        <span>•</span>
                        <span>
                          {((torrent.progress ?? 0) * 100).toFixed(1)}%
                        </span>
                      </div>
                    </td>
                    <td>
                      <span
                        className={`badge ${getStatusBadge(torrent.status)}`}
                        style={{ fontSize: "0.72rem" }}
                      >
                        {torrent.status}
                      </span>
                    </td>
                    <td>
                      <div
                        style={{
                          fontSize: "0.82rem",
                          fontWeight: 600,
                          color:
                            torrent.payloadDownloadSpeed > 0
                              ? "var(--color-download, #3498db)"
                              : "var(--text-secondary)",
                        }}
                      >
                        ↓ {formatSpeed(torrent.payloadDownloadSpeed)}
                      </div>
                      <div
                        style={{
                          fontSize: "0.72rem",
                          color:
                            torrent.payloadUploadSpeed > 0
                              ? "var(--color-upload, #2ecc71)"
                              : "var(--text-muted)",
                        }}
                      >
                        ↑ {formatSpeed(torrent.payloadUploadSpeed)}
                      </div>
                    </td>
                    <td>
                      <div
                        style={{
                          fontSize: "0.78rem",
                          color: "var(--text-secondary)",
                        }}
                      >
                        ↓ {formatBytes(torrent.protocolDownloaded)}
                      </div>
                      <div
                        style={{
                          fontSize: "0.7rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {torrent.efficiencyRatio}
                        {t("system.efficiency")}
                      </div>
                    </td>
                    <td>
                      <div
                        style={{
                          fontSize: "0.82rem",
                          fontWeight: 600,
                          color: "var(--text-primary)",
                        }}
                      >
                        {formatBytes(torrent.estimatedMemoryBufferBytes)}
                      </div>
                      <div
                        style={{
                          fontSize: "0.7rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {torrent.piecesInFlight} {t("system.inFlight")}
                      </div>
                    </td>
                    <td>
                      <div
                        style={{
                          fontSize: "0.82rem",
                          color: "var(--text-primary)",
                        }}
                      >
                        <strong>{torrent.connectedPeers}</strong>{" "}
                        {t("system.connected")}
                      </div>
                      <div
                        style={{
                          fontSize: "0.72rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {torrent.connectedSeeds} {t("system.seeds")}
                        {torrent.connectedLeechers} {t("system.leeches")}
                      </div>
                    </td>
                    <td>
                      <div
                        style={{
                          fontSize: "0.76rem",
                          color: "var(--text-secondary)",
                        }}
                      >
                        {t("system.tcp")}
                        {torrent.tcpPeers} {t("system.utp")}
                        {torrent.utpPeers}
                      </div>
                      <div
                        style={{
                          fontSize: "0.72rem",
                          color:
                            torrent.encryptedPeers > 0
                              ? "#9b59b6"
                              : "var(--text-muted)",
                        }}
                      >
                        🔒 {torrent.encryptedPeers} {t("system.encrypted")}
                      </div>
                    </td>
                    <td>
                      <div
                        style={{
                          fontSize: "0.78rem",
                          color: "var(--text-primary)",
                        }}
                      >
                        {torrent.completedPieces} / {torrent.totalPieces}{" "}
                        {t("system.pcs")}
                      </div>
                      <div
                        style={{
                          fontSize: "0.7rem",
                          color:
                            torrent.hashFails > 0
                              ? "#e74c3c"
                              : "var(--text-muted)",
                        }}
                      >
                        {torrent.hashFails > 0
                          ? `⚠️ ${torrent.hashFails} hash rejects (${formatBytes(torrent.wastedBytes)})`
                          : "Zero corrupt blocks"}
                      </div>
                    </td>
                    <td>
                      <span
                        style={{
                          fontSize: "0.8rem",
                          fontWeight: 600,
                          color:
                            torrent.swarmAvailability >= 1.0
                              ? "#2ecc71"
                              : torrent.swarmAvailability > 0
                                ? "#f39c12"
                                : "var(--text-muted)",
                        }}
                      >
                        {torrent.swarmAvailability > 0
                          ? `${torrent.swarmAvailability.toFixed(1)}x`
                          : "-"}
                      </span>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <button
                        className="btn btn-small btn-link"
                        style={{
                          fontSize: "0.75rem",
                          padding: "0.2rem 0.5rem",
                        }}
                        onClick={() => setSelectedTorrent(torrent)}
                      >
                        {t("system.inspect")}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Drill-down Modal for Individual Torrent */}
      {selectedTorrent && (
        <div
          className="modal-backdrop"
          onClick={() => setSelectedTorrent(null)}
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0, 0, 0, 0.75)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1100,
          }}
        >
          <div
            className="modal-content"
            onClick={(e) => e.stopPropagation()}
            style={{
              backgroundColor: "var(--bg-secondary, #171b35)",
              borderRadius: "8px",
              border: "1px solid rgba(255, 255, 255, 0.12)",
              maxWidth: "600px",
              width: "90%",
              padding: "1.5rem",
              boxShadow: "0 10px 30px rgba(0, 0, 0, 0.6)",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "flex-start",
                marginBottom: "1rem",
              }}
            >
              <div>
                <h3
                  style={{
                    margin: 0,
                    fontSize: "1.1rem",
                    color: "var(--accent, #ffd166)",
                  }}
                >
                  {selectedTorrent.name}
                </h3>
                <div
                  style={{
                    fontSize: "0.75rem",
                    color: "var(--text-muted)",
                    fontFamily: "monospace",
                    marginTop: "0.2rem",
                  }}
                >
                  {selectedTorrent.infoHash}
                </div>
              </div>
              <button
                className="btn btn-small btn-link"
                onClick={() => setSelectedTorrent(null)}
                style={{ fontSize: "1.1rem", padding: "0 0.4rem" }}
              >
                ✕
              </button>
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr",
                gap: "0.85rem",
                fontSize: "0.82rem",
                marginBottom: "1.25rem",
              }}
            >
              <div
                style={{
                  backgroundColor: "rgba(0, 0, 0, 0.2)",
                  padding: "0.75rem",
                  borderRadius: "6px",
                }}
              >
                <div
                  style={{ color: "var(--text-muted)", fontSize: "0.75rem" }}
                >
                  {t("system.downloadPayload")}
                </div>
                <div style={{ fontSize: "1.1rem", fontWeight: 700 }}>
                  {formatBytes(selectedTorrent.downloadedPayload)}
                </div>
                <div
                  style={{
                    color: "var(--text-secondary)",
                    fontSize: "0.72rem",
                  }}
                >
                  {t("torrents.speed")}:{" "}
                  {formatSpeed(selectedTorrent.payloadDownloadSpeed)}
                </div>
              </div>

              <div
                style={{
                  backgroundColor: "rgba(0, 0, 0, 0.2)",
                  padding: "0.75rem",
                  borderRadius: "6px",
                }}
              >
                <div
                  style={{ color: "var(--text-muted)", fontSize: "0.75rem" }}
                >
                  {t("system.protocolOverhead")}
                </div>
                <div style={{ fontSize: "1.1rem", fontWeight: 700 }}>
                  {formatBytes(selectedTorrent.protocolDownloaded)}
                </div>
                <div
                  style={{
                    color: "var(--text-secondary)",
                    fontSize: "0.72rem",
                  }}
                >
                  {t("system.efficiency")}: {selectedTorrent.efficiencyRatio}%
                </div>
              </div>

              <div
                style={{
                  backgroundColor: "rgba(0, 0, 0, 0.2)",
                  padding: "0.75rem",
                  borderRadius: "6px",
                }}
              >
                <div
                  style={{ color: "var(--text-muted)", fontSize: "0.75rem" }}
                >
                  {t("system.memoryBufferPieceCache")}
                </div>
                <div style={{ fontSize: "1.1rem", fontWeight: 700 }}>
                  {formatBytes(selectedTorrent.estimatedMemoryBufferBytes)}
                </div>
                <div
                  style={{
                    color: "var(--text-secondary)",
                    fontSize: "0.72rem",
                  }}
                >
                  {selectedTorrent.piecesInFlight} {t("system.piecesInFlight")}{" "}
                  ({formatBytes(selectedTorrent.pieceLength)} /{" "}
                  {t("system.piece")})
                </div>
              </div>

              <div
                style={{
                  backgroundColor: "rgba(0, 0, 0, 0.2)",
                  padding: "0.75rem",
                  borderRadius: "6px",
                }}
              >
                <div
                  style={{ color: "var(--text-muted)", fontSize: "0.75rem" }}
                >
                  {t("system.swarmCryptoNetwork")}
                </div>
                <div style={{ fontSize: "1.1rem", fontWeight: 700 }}>
                  {selectedTorrent.connectedPeers} {t("common.peers")}
                </div>
                <div
                  style={{
                    color: "var(--text-secondary)",
                    fontSize: "0.72rem",
                  }}
                >
                  {selectedTorrent.encryptedPeers} {t("system.encrypted")}{" "}
                  {selectedTorrent.utpPeers} {t("system.utp")}{" "}
                  {selectedTorrent.tcpPeers} {t("system.tcp")}
                </div>
              </div>
            </div>

            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                paddingTop: "0.75rem",
                borderTop: "1px solid rgba(255, 255, 255, 0.08)",
              }}
            >
              <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                {t("common.eta")}: {formatSeconds(selectedTorrent.etaSeconds)} •{" "}
                {t("common.ratio")}: {selectedTorrent.ratio.toFixed(2)}
              </div>
              <button
                className="btn btn-primary btn-small"
                onClick={() => setSelectedTorrent(null)}
              >
                {t("common.close")}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemResources;
