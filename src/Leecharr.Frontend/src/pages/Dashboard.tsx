import React, { useState, useEffect, useRef } from "react";
import { Torrent } from "../api/types";
import {
  useArrConnections,
  useIndexers,
  useSchedulerConfig,
  useActiveSpeedLimits,
  useDiskSpace,
  useSeedingStats,
} from "../api/hooks";
import { extractTrackerDomain } from "../utils/formatters";
import { calculateAchievements } from "../utils/milestones";
import HealthAlerts from "../components/HealthAlerts";

interface DashboardProps {
  torrents: Torrent[];
  onNavigateTorrents: () => void;
  onNavigateSettings?: (section: string) => void;
}

export const Dashboard: React.FC<DashboardProps> = ({
  torrents,
  onNavigateTorrents,
  onNavigateSettings,
}) => {
  const [speedHistory, setSpeedHistory] = useState<{ dl: number; ul: number; time: number }[]>([]);

  const { data: arrConnections } = useArrConnections();
  const { data: indexers } = useIndexers();
  const { data: schedulerConfig } = useSchedulerConfig();
  const { data: activeLimits } = useActiveSpeedLimits();
  const { data: diskSpace } = useDiskSpace();
  const { data: seedingStats } = useSeedingStats();

  const achievements = calculateAchievements(torrents, seedingStats);
  const totalSize = torrents.reduce((acc, t) => acc + (t.totalSize || 0), 0);
  const totalLibrarySize =
    (diskSpace || []).reduce((acc, d) => acc + (d.totalSpace - d.freeSpace), 0) || totalSize;

  const totalDlSpeed = torrents.reduce((acc, t) => acc + (t.downloadSpeed || 0), 0);
  const totalUlSpeed = torrents.reduce((acc, t) => acc + (t.uploadSpeed || 0), 0);
  const avgRatio =
    torrents.length > 0
      ? torrents.reduce((acc, t) => acc + (t.ratio ?? 0), 0) / torrents.length
      : 0;

  const speedsRef = useRef({ dl: totalDlSpeed, ul: totalUlSpeed });
  speedsRef.current = { dl: totalDlSpeed, ul: totalUlSpeed };

  // Track live speed history for graph
  useEffect(() => {
    const interval = setInterval(() => {
      setSpeedHistory((prev) => {
        const next = [
          ...prev,
          { dl: speedsRef.current.dl, ul: speedsRef.current.ul, time: Date.now() },
        ];
        return next.slice(-40); // Keep last 40 samples (approx 60s)
      });
    }, 1500);
    return () => clearInterval(interval);
  }, []);

  const formatSize = (bytes: number) => {
    if (!bytes) return "0 B";
    const tb = bytes / (1024 * 1024 * 1024 * 1024);
    if (tb >= 1) return `${tb.toFixed(1)} TB`;
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(1)} GB`;
    return `${(bytes / (1024 * 1024)).toFixed(0)} MB`;
  };

  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec) return "0 B/s";
    const mb = bytesPerSec / (1024 * 1024);
    if (mb >= 1) return `${mb.toFixed(1)} MB/s`;
    return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  };

  // Generate SVG path for speed chart
  const maxSpeed = Math.max(1024 * 1024, ...speedHistory.map((h) => Math.max(h.dl, h.ul)));
  const chartWidth = 900;
  const chartHeight = 120;

  const getSvgPoints = (key: "dl" | "ul") => {
    if (speedHistory.length < 2) return "";
    return speedHistory
      .map((h, i) => {
        const x = (i / (speedHistory.length - 1)) * chartWidth;
        const y = chartHeight - (h[key] / maxSpeed) * (chartHeight - 20) - 10;
        return `${i === 0 ? "M" : "L"} ${x} ${y}`;
      })
      .join(" ");
  };

  // Check connection status for each Arr service
  const getArrStatus = (serviceName: string) => {
    const list = arrConnections || [];
    const conn = list.find((c) => {
      const nameMatch = c.name?.toLowerCase().includes(serviceName.toLowerCase());
      const typeMatch =
        c.arrType?.toLowerCase().includes(serviceName.toLowerCase()) ||
        c.implementation?.toLowerCase().includes(serviceName.toLowerCase());
      return nameMatch || typeMatch;
    });

    if (!conn) {
      // Check Prowlarr in indexers list as well
      if (serviceName.toLowerCase() === "prowlarr") {
        const prowlarrIndexer = (indexers || []).find(
          (i) =>
            i.name?.toLowerCase().includes("prowlarr") ||
            i.indexerType?.toLowerCase().includes("prowlarr")
        );
        if (prowlarrIndexer) {
          return prowlarrIndexer.enable
            ? { label: "Connected", color: "var(--success, #22c55e)" }
            : { label: "Disabled", color: "var(--warning, #eab308)" };
        }
      }
      return { label: "Not Configured", color: "var(--text-muted, #7e8092)" };
    }

    if (!conn.enable) {
      return { label: "Disabled", color: "var(--warning, #eab308)" };
    }

    return { label: "Connected", color: "var(--success, #22c55e)" };
  };

  const ecosystemItems = [
    { name: "Sonarr", icon: "📺", status: getArrStatus("Sonarr") },
    { name: "Radarr", icon: "🎬", status: getArrStatus("Radarr") },
    { name: "Lidarr", icon: "🎵", status: getArrStatus("Lidarr") },
    { name: "Prowlarr", icon: "🔍", status: getArrStatus("Prowlarr") },
    {
      name: "qBittorrent API",
      icon: "🔌",
      status: {
        label: "Port 7889 (/api/v2)",
        color: "var(--success, #22c55e)",
      },
    },
    {
      name: "Deluge RPC",
      icon: "⚡",
      status: { label: "Port 7889 (/json)", color: "var(--success, #22c55e)" },
    },
    {
      name: "Transmission RPC",
      icon: "🧲",
      status: {
        label: "Port 7889 (/transmission/rpc)",
        color: "var(--success, #22c55e)",
      },
    },
  ];

  // Dynamic active indexers and trackers
  const activeIndexers = (indexers || []).filter((i) => i.enable);
  const activeTrackerDomains = Array.from(
    new Set(
      torrents.map((t) => extractTrackerDomain(t.trackerUrl)).filter((d) => d && d !== "Unknown")
    )
  ).slice(0, 3);

  return (
    <div
      className="dashboard-page"
      style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
    >
      {/* Setup & System Health Guidance Alerts */}
      <HealthAlerts />

      {/* Hero Achievement / Status Banner */}
      <div
        className="card"
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "1rem 1.5rem",
          backgroundColor: "rgba(255, 209, 102, 0.05)",
          border: "1px solid rgba(255, 209, 102, 0.2)",
          borderRadius: "8px",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
          <div style={{ fontSize: "2rem" }}>🏆</div>
          <div>
            <div
              style={{
                fontWeight: 700,
                color: "var(--accent, #ffd166)",
                fontSize: "1rem",
              }}
            >
              Level {achievements.overallLevel}: {achievements.rankTitle}{" "}
              <span
                style={{
                  color: "var(--text-muted, #7e8092)",
                  fontSize: "0.8rem",
                  fontWeight: 500,
                }}
              >
                {achievements.unlockedCount}/{achievements.totalCount} BADGES
              </span>
            </div>
            <div
              style={{
                color: "var(--text-secondary, #c7c5d3)",
                fontSize: "0.85rem",
                marginTop: "2px",
              }}
            >
              🛡 {achievements.totalSwarmGuardians.length} Swarm Guardian
              {achievements.totalSwarmGuardians.length === 1 ? "" : "s"} protected &bull;
              Non-blocking async disk cache running
            </div>
          </div>
        </div>
        <button
          className="btn btn-small"
          onClick={onNavigateTorrents}
          style={{
            backgroundColor: "rgba(255, 209, 102, 0.1)",
            color: "var(--accent, #ffd166)",
            border: "1px solid var(--accent, #ffd166)",
            fontWeight: 600,
          }}
        >
          Queue & Torrents →
        </button>
      </div>

      {/* 4 Stat Metric Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(4, 1fr)",
          gap: "1.25rem",
        }}
      >
        <div
          className="card"
          style={{
            padding: "1.25rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            style={{
              fontSize: "2rem",
              fontWeight: 700,
              color: "var(--text-primary, #f8f4ed)",
            }}
          >
            {torrents.length}
          </div>
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted, #7e8092)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
              marginTop: "4px",
            }}
          >
            Active Torrents
          </div>
        </div>

        <div
          className="card"
          style={{
            padding: "1.25rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            style={{
              fontSize: "2rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
            }}
          >
            {formatSize(totalSize)}
          </div>
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted, #7e8092)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
              marginTop: "4px",
            }}
          >
            Total Downloaded
          </div>
        </div>

        <div
          className="card"
          style={{
            padding: "1.25rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            style={{
              fontSize: "2rem",
              fontWeight: 700,
              color: "var(--text-primary, #f8f4ed)",
            }}
          >
            {avgRatio.toFixed(2)}
          </div>
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted, #7e8092)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
              marginTop: "4px",
            }}
          >
            Average Ratio
          </div>
        </div>

        <div
          className="card"
          style={{
            padding: "1.25rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            style={{
              fontSize: "2rem",
              fontWeight: 700,
              color: "var(--success, #22c55e)",
            }}
          >
            {formatSize(totalLibrarySize)}
          </div>
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted, #7e8092)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
              marginTop: "4px",
            }}
          >
            Total Library Size
          </div>
        </div>
      </div>

      {/* Connected Servarr Ecosystem */}
      <div className="card" style={{ padding: "1rem 1.25rem", borderRadius: "8px" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "0.75rem",
          }}
        >
          <span
            style={{
              fontSize: "0.9rem",
              fontWeight: 600,
              color: "var(--text-primary, #f8f4ed)",
            }}
          >
            Connected Ecosystem
          </span>
          <span
            style={{
              fontSize: "0.8rem",
              color: "var(--accent, #ffd166)",
              cursor: "pointer",
            }}
            onClick={() => onNavigateSettings && onNavigateSettings("connections")}
          >
            Manage Connections ⚙
          </span>
        </div>
        <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap" }}>
          {ecosystemItems.map((item, idx) => (
            <div
              key={idx}
              style={{
                flex: 1,
                minWidth: "130px",
                padding: "0.6rem 0.8rem",
                backgroundColor: "var(--bg-primary, #10111a)",
                border: "1px solid var(--border-light, #1c203b)",
                borderRadius: "6px",
                display: "flex",
                alignItems: "center",
                gap: "8px",
              }}
            >
              <span style={{ fontSize: "1.2rem" }}>{item.icon}</span>
              <div>
                <div
                  style={{
                    fontSize: "0.8rem",
                    fontWeight: 600,
                    color: "var(--text-primary, #f8f4ed)",
                  }}
                >
                  {item.name}
                </div>
                <div
                  style={{
                    fontSize: "0.7rem",
                    color: item.status.color,
                    display: "flex",
                    alignItems: "center",
                    gap: "4px",
                  }}
                >
                  <span
                    style={{
                      width: "6px",
                      height: "6px",
                      borderRadius: "50%",
                      backgroundColor: item.status.color,
                    }}
                  />
                  {item.status.label}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Middle 3-Column Info Row */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr 1fr",
          gap: "1.25rem",
        }}
      >
        {/* Status Distribution */}
        <div
          className="card"
          style={{
            padding: "1.25rem",
            borderRadius: "8px",
            display: "flex",
            alignItems: "center",
            gap: "1.5rem",
          }}
        >
          <div
            style={{
              width: "80px",
              height: "80px",
              borderRadius: "50%",
              border: "6px solid var(--accent, #ffd166)",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              fontSize: "1.5rem",
              fontWeight: 700,
              color: "var(--text-primary, #f8f4ed)",
              flexShrink: 0,
            }}
          >
            {torrents.length}
          </div>
          <div>
            <div
              style={{
                fontSize: "0.85rem",
                color: "var(--accent, #ffd166)",
                fontWeight: 600,
                marginBottom: "4px",
              }}
            >
              &bull; Downloading:{" "}
              {torrents.filter((t) => (t.status || "").toLowerCase() === "downloading").length}
            </div>
            <div
              style={{
                fontSize: "0.85rem",
                color: "var(--success, #22c55e)",
                fontWeight: 600,
                marginBottom: "4px",
              }}
            >
              &bull; Seeding:{" "}
              {torrents.filter((t) => (t.status || "").toLowerCase() === "seeding").length}
            </div>
            <div
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted, #7e8092)",
                fontWeight: 600,
              }}
            >
              &bull; Paused:{" "}
              {torrents.filter((t) => (t.status || "").toLowerCase() === "paused").length}
            </div>
          </div>
        </div>

        {/* Speed Schedule Limits */}
        <div className="card" style={{ padding: "1.25rem", borderRadius: "8px" }}>
          <div
            style={{
              fontSize: "0.85rem",
              fontWeight: 600,
              color: "var(--text-primary, #f8f4ed)",
              marginBottom: "0.75rem",
            }}
          >
            Speed Schedule
          </div>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              fontSize: "0.8rem",
              padding: "4px 0",
              borderBottom: "1px solid rgba(255,255,255,0.05)",
            }}
          >
            <span style={{ color: "var(--text-muted, #7e8092)" }}>Active Mode:</span>
            <span
              className="badge"
              style={{
                backgroundColor: "var(--bg-primary, #10111a)",
                color: "var(--text-secondary, #c7c5d3)",
              }}
            >
              {activeLimits?.isThrottled
                ? "THROTTLED"
                : schedulerConfig?.schedulerEnabled
                ? "SCHEDULED"
                : "NORMAL (24x7)"}
            </span>
          </div>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              fontSize: "0.8rem",
              padding: "4px 0",
              borderBottom: "1px solid rgba(255,255,255,0.05)",
            }}
          >
            <span style={{ color: "var(--text-muted, #7e8092)" }}>Upload Limit:</span>
            <span style={{ color: "var(--text-primary, #f8f4ed)", fontWeight: 600 }}>
              {activeLimits?.maxUploadSpeedKbps
                ? `${activeLimits.maxUploadSpeedKbps} KB/s`
                : "Unlimited"}
            </span>
          </div>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              fontSize: "0.8rem",
              padding: "4px 0",
            }}
          >
            <span style={{ color: "var(--text-muted, #7e8092)" }}>Download Limit:</span>
            <span style={{ color: "var(--text-primary, #f8f4ed)", fontWeight: 600 }}>
              {activeLimits?.maxDownloadSpeedKbps
                ? `${activeLimits.maxDownloadSpeedKbps} KB/s`
                : "Unlimited"}
            </span>
          </div>
        </div>

        {/* Active Indexers & Trackers */}
        <div className="card" style={{ padding: "1.25rem", borderRadius: "8px" }}>
          <div
            style={{
              fontSize: "0.85rem",
              fontWeight: 600,
              color: "var(--text-primary, #f8f4ed)",
              marginBottom: "0.75rem",
            }}
          >
            Active Indexers & Trackers
          </div>
          {activeIndexers.length > 0 ? (
            activeIndexers.slice(0, 2).map((idx) => (
              <div
                key={idx.id}
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  fontSize: "0.8rem",
                  padding: "4px 0",
                  borderBottom: "1px solid rgba(255,255,255,0.05)",
                }}
              >
                <span style={{ color: "var(--text-secondary, #c7c5d3)" }}>
                  {idx.name} ({idx.indexerType}) ↗
                </span>
                <span style={{ color: "var(--success, #22c55e)", fontWeight: 600 }}>Active</span>
              </div>
            ))
          ) : (
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                fontSize: "0.8rem",
                padding: "4px 0",
                borderBottom: "1px solid rgba(255,255,255,0.05)",
              }}
            >
              <span style={{ color: "var(--text-muted, #7e8092)" }}>No Indexers Configured</span>
              <span
                style={{
                  color: "var(--accent, #ffd166)",
                  cursor: "pointer",
                  fontWeight: 600,
                }}
                onClick={() => onNavigateSettings && onNavigateSettings("indexers")}
              >
                + Add
              </span>
            </div>
          )}

          {activeTrackerDomains.length > 0 ? (
            activeTrackerDomains.map((domain, i) => (
              <div
                key={i}
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  fontSize: "0.8rem",
                  padding: "4px 0",
                  borderBottom:
                    i < activeTrackerDomains.length - 1
                      ? "1px solid rgba(255,255,255,0.05)"
                      : "none",
                }}
              >
                <span style={{ color: "var(--text-secondary, #c7c5d3)" }}>{domain} ↗</span>
                <span style={{ color: "var(--accent, #ffd166)", fontWeight: 600 }}>Connected</span>
              </div>
            ))
          ) : (
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                fontSize: "0.8rem",
                padding: "4px 0",
              }}
            >
              <span style={{ color: "var(--text-secondary, #c7c5d3)" }}>
                BitTorrent DHT Swarm ↗
              </span>
              <span style={{ color: "var(--accent, #ffd166)", fontWeight: 600 }}>Swarm Ready</span>
            </div>
          )}
        </div>
      </div>

      {/* Transfer Speed Live Graph */}
      <div className="card" style={{ padding: "1.25rem", borderRadius: "8px" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "0.75rem",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
            <span
              style={{
                fontSize: "0.9rem",
                fontWeight: 600,
                color: "var(--text-primary, #f8f4ed)",
              }}
            >
              Transfer Speed
            </span>
            <span
              className="badge"
              style={{
                backgroundColor: "rgba(16, 185, 129, 0.15)",
                color: "var(--success, #22c55e)",
                fontSize: "0.65rem",
              }}
            >
              &bull; Live (1s)
            </span>
          </div>
          <div style={{ display: "flex", gap: "16px", fontSize: "0.8rem" }}>
            <span style={{ color: "var(--accent, #ffd166)", fontWeight: 600 }}>
              &bull; Download: {formatSpeed(totalDlSpeed)}
            </span>
            <span style={{ color: "var(--success, #22c55e)", fontWeight: 600 }}>
              &bull; Upload: {formatSpeed(totalUlSpeed)}
            </span>
          </div>
        </div>

        <div
          style={{
            height: "120px",
            backgroundColor: "rgba(0,0,0,0.3)",
            borderRadius: "6px",
            overflow: "hidden",
            border: "1px solid var(--border-light, #1c203b)",
          }}
        >
          <svg
            width="100%"
            height="100%"
            viewBox={`0 0 ${chartWidth} ${chartHeight}`}
            preserveAspectRatio="none"
          >
            {/* Grid lines */}
            <line
              x1="0"
              y1="30"
              x2={chartWidth}
              y2="30"
              stroke="rgba(255,255,255,0.05)"
              strokeDasharray="4 4"
            />
            <line
              x1="0"
              y1="60"
              x2={chartWidth}
              y2="60"
              stroke="rgba(255,255,255,0.05)"
              strokeDasharray="4 4"
            />
            <line
              x1="0"
              y1="90"
              x2={chartWidth}
              y2="90"
              stroke="rgba(255,255,255,0.05)"
              strokeDasharray="4 4"
            />

            {/* Download Speed Line (Amber) */}
            <path
              d={getSvgPoints("dl")}
              fill="none"
              stroke="#ffd166"
              strokeWidth="2.5"
              strokeLinecap="round"
            />

            {/* Upload Speed Line (Green) */}
            <path
              d={getSvgPoints("ul")}
              fill="none"
              stroke="#10b981"
              strokeWidth="2.5"
              strokeLinecap="round"
            />
          </svg>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
