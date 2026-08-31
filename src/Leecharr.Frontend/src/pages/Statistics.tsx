import { useState, useMemo } from "react";
import { Link } from "react-router";
import { useTorrents, useSeedingStats } from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatSeconds,
} from "../utils/formatters";
import {
  calculateAchievements,
  calculateTrackerBuffers,
} from "../utils/milestones";
import SpeedGraph from "../components/SpeedGraph";

import SeedingSimulator from "../components/SeedingSimulator";

const STATUS_COLORS: Record<string, string> = {
  Seeding: "var(--color-success, #27ae60)",
  Stopped: "var(--color-danger, #e74c3c)",
  Queued: "var(--color-warning, #f39c12)",
  Error: "#c0392b",
};

function Statistics() {
  const {
    data: torrents,
    isLoading: torrentsLoading,
    isError: torrentsError,
  } = useTorrents();
  const { data: stats } = useSeedingStats();

  const [activeTab, setActiveTab] = useState<
    "overview" | "achievements" | "buffers" | "simulator"
  >("overview");

  const achievements = useMemo(
    () => calculateAchievements(torrents, stats),
    [torrents, stats],
  );

  const trackerBuffers = useMemo(
    () => calculateTrackerBuffers(torrents),
    [torrents],
  );

  const statusCounts: Record<string, number> = {};
  (torrents ?? []).forEach((t) => {
    statusCounts[t.status] = (statusCounts[t.status] || 0) + 1;
  });
  const total = torrents?.length ?? 0;
  const entries = Object.entries(statusCounts).filter(([, v]) => v > 0);

  const topTorrents = [...(torrents ?? [])]
    .sort((a, b) => b.uploaded - a.uploaded)
    .slice(0, 10);

  return (
    <div className="content-area">
      {/* Header */}
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
              Statistics & Achievements
            </h1>
            <span className="badge badge-primary">
              Level {achievements.overallLevel}: {achievements.rankTitle}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Live transfer speeds, seeding milestones, swarm preservation, and
            tracker metrics
          </div>
        </div>

        {/* Tab switcher */}
        <div className="view-toggle" style={{ margin: 0 }}>
          <button
            className={`view-toggle-btn ${activeTab === "overview" ? "active" : ""}`}
            onClick={() => setActiveTab("overview")}
          >
            📊 Swarm Overview
          </button>
          <button
            className={`view-toggle-btn ${activeTab === "achievements" ? "active" : ""}`}
            onClick={() => setActiveTab("achievements")}
          >
            🏆 Achievements ({achievements.unlockedCount}/
            {achievements.totalCount})
          </button>
          <button
            className={`view-toggle-btn ${activeTab === "buffers" ? "active" : ""}`}
            onClick={() => setActiveTab("buffers")}
          >
            🛡️ Tracker Buffers & BP
          </button>
          <button
            className={`view-toggle-btn ${activeTab === "simulator" ? "active" : ""}`}
            onClick={() => setActiveTab("simulator")}
          >
            🎯 Seeding Simulator
          </button>
        </div>
      </div>

      {/* OVERVIEW TAB */}
      {activeTab === "overview" && (
        <>
          <SpeedGraph />

          {/* Quick Gamification Highlight Banner */}
          <div
            className="card"
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "1rem",
              marginBottom: "1.25rem",
              background:
                "linear-gradient(90deg, rgba(200, 168, 78, 0.15) 0%, rgba(22, 22, 22, 0.9) 100%)",
              border: "1px solid rgba(200, 168, 78, 0.3)",
              borderRadius: "8px",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
              padding: "1rem 1.25rem",
            }}
          >
            <div>
              <div
                style={{
                  fontWeight: 700,
                  fontSize: "1.1rem",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                }}
              >
                <span>🎖️ {achievements.rankTitle}</span>
                <span
                  className="badge badge-secondary"
                  style={{ fontSize: "0.75rem" }}
                >
                  Level {achievements.overallLevel}
                </span>
              </div>
              <div
                style={{
                  fontSize: "0.85rem",
                  color: "var(--text-muted)",
                  marginTop: "0.2rem",
                }}
              >
                {achievements.unlockedCount} of {achievements.totalCount}{" "}
                Seeding Milestones Unlocked •{" "}
                {achievements.totalSwarmGuardians.length} Rare Swarms Protected
              </div>
            </div>

            <button
              className="btn btn-outline btn-small"
              onClick={() => setActiveTab("achievements")}
            >
              View Hall of Fame 🏆
            </button>
          </div>

          {total > 0 && (
            <div
              className="card"
              style={{
                marginBottom: "1.25rem",
                borderRadius: "8px",
                boxShadow:
                  "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
                border: "1px solid rgba(255, 255, 255, 0.08)",
                padding: "1.25rem",
              }}
            >
              <h3 style={{ margin: "0 0 0.75rem", fontSize: "1.05rem" }}>
                Status Breakdown
              </h3>
              <div
                style={{
                  display: "flex",
                  height: 32,
                  borderRadius: 6,
                  overflow: "hidden",
                  marginBottom: 12,
                  boxShadow: "inset 0 1px 3px rgba(0, 0, 0, 0.4)",
                }}
              >
                {entries.map(([status, count]) => {
                  const pct = (count / total) * 100;
                  return (
                    <div
                      key={status}
                      style={{
                        width: `${pct}%`,
                        backgroundColor: STATUS_COLORS[status] || "#666",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "center",
                        color: "#fff",
                        fontSize: 12,
                        fontWeight: 600,
                      }}
                      title={`${status}: ${count}`}
                    >
                      {pct > 10 ? `${status} (${count})` : ""}
                    </div>
                  );
                })}
              </div>
              <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
                {entries.map(([status, count]) => (
                  <div
                    key={status}
                    style={{ display: "flex", alignItems: "center", gap: 6 }}
                  >
                    <div
                      style={{
                        width: 12,
                        height: 12,
                        borderRadius: 3,
                        backgroundColor: STATUS_COLORS[status] || "#666",
                      }}
                    />
                    <span
                      style={{
                        fontSize: "0.85rem",
                        color: "var(--text-secondary)",
                      }}
                    >
                      {status}:{" "}
                      <strong style={{ color: "var(--text-primary)" }}>
                        {count}
                      </strong>
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
              gap: "1.25rem",
              marginBottom: "1.25rem",
            }}
          >
            <div
              className="card"
              style={{
                borderRadius: "8px",
                boxShadow:
                  "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
                border: "1px solid rgba(255, 255, 255, 0.08)",
                padding: "1.25rem",
              }}
            >
              <h3 style={{ margin: "0 0 0.75rem", fontSize: "1.05rem" }}>
                Top Torrents by Upload
              </h3>
              {torrentsLoading ? (
                <p className="loading">Loading...</p>
              ) : torrentsError ? (
                <p className="error">Failed to load data.</p>
              ) : (
                <div className="torrent-table-wrapper">
                  <table className="torrent-table">
                    <thead>
                      <tr>
                        <th className="torrent-table-th">Name</th>
                        <th className="torrent-table-th">Uploaded</th>
                        <th className="torrent-table-th">Ratio</th>
                        <th className="torrent-table-th">Speed</th>
                      </tr>
                    </thead>
                    <tbody>
                      {topTorrents.length === 0 ? (
                        <tr>
                          <td colSpan={4} className="torrent-table-empty">
                            No torrents
                          </td>
                        </tr>
                      ) : (
                        topTorrents.map((t) => (
                          <tr key={t.id} className="torrent-table-row">
                            <td>
                              <Link
                                to="/torrents"
                                style={{
                                  color: "inherit",
                                  textDecoration: "none",
                                  fontWeight: 500,
                                }}
                              >
                                {t.name}
                              </Link>
                            </td>
                            <td style={{ fontWeight: 600 }}>
                              {formatBytes(t.uploaded)}
                            </td>
                            <td>
                              <span
                                className={`badge ${t.ratio >= 2.0 ? "badge-primary" : t.ratio >= 1.0 ? "badge-secondary" : "badge-outline"}`}
                                style={{ fontSize: "0.75rem" }}
                              >
                                {formatRatio(t.ratio)}
                              </span>
                            </td>
                            <td
                              style={{
                                color: "var(--accent, #c8a84e)",
                                fontWeight: 600,
                              }}
                            >
                              {formatSpeed(t.uploadSpeed)}
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {/* Swarm Guardian Highlights */}
            <div
              className="card"
              style={{
                borderRadius: "8px",
                boxShadow:
                  "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
                border: "1px solid rgba(255, 255, 255, 0.08)",
                padding: "1.25rem",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: "0.5rem",
                }}
              >
                <h3 style={{ margin: 0, fontSize: "1.05rem" }}>
                  🛡️ Swarm Guardians
                </h3>
                <span
                  className="badge badge-warning"
                  style={{ fontSize: "0.75rem" }}
                >
                  {achievements.totalSwarmGuardians.length} Rare
                </span>
              </div>
              <p
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted)",
                  margin: "0 0 0.75rem 0",
                }}
              >
                Releases with 2 or fewer seeders in the world where you are
                keeping the archive alive.
              </p>

              {achievements.totalSwarmGuardians.length === 0 ? (
                <p className="loading" style={{ margin: 0, padding: "1rem 0" }}>
                  No dying swarms detected. All active torrents have healthy
                  peer counts!
                </p>
              ) : (
                <div className="torrent-table-wrapper">
                  <table className="torrent-table">
                    <thead>
                      <tr>
                        <th className="torrent-table-th">Protected Torrent</th>
                        <th className="torrent-table-th">Seeders</th>
                        <th className="torrent-table-th">Seed Time</th>
                      </tr>
                    </thead>
                    <tbody>
                      {achievements.totalSwarmGuardians.slice(0, 5).map((t) => (
                        <tr key={t.id} className="torrent-table-row">
                          <td>
                            <Link
                              to="/torrents"
                              style={{
                                color: "inherit",
                                textDecoration: "none",
                                fontWeight: 500,
                              }}
                            >
                              {t.name}
                            </Link>
                          </td>
                          <td>
                            <span
                              className="badge badge-danger"
                              style={{ fontSize: "0.75rem" }}
                            >
                              ⚠️ {t.seeders} Seeder{t.seeders !== 1 ? "s" : ""}
                            </span>
                          </td>
                          <td style={{ color: "var(--text-secondary)" }}>
                            {formatSeconds(t.seedingTime)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>
        </>
      )}

      {/* ACHIEVEMENTS & HALL OF FAME TAB */}
      {activeTab === "achievements" && (
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          {/* Level & Rank Header */}
          <div
            className="card"
            style={{
              padding: "1.5rem",
              background:
                "linear-gradient(135deg, rgba(200, 168, 78, 0.2) 0%, rgba(20, 20, 20, 0.95) 100%)",
              border: "1px solid var(--accent)",
              borderRadius: "8px",
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
                gap: "1rem",
              }}
            >
              <div>
                <div
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--accent)",
                    textTransform: "uppercase",
                    fontWeight: 700,
                    letterSpacing: "0.08em",
                  }}
                >
                  Seeding Mastery Tier
                </div>
                <h2 style={{ margin: "0.25rem 0", fontSize: "1.7rem" }}>
                  Level {achievements.overallLevel} • {achievements.rankTitle}
                </h2>
                <div
                  style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}
                >
                  {achievements.unlockedCount} of {achievements.totalCount}{" "}
                  Achievements Complete (
                  {(
                    (achievements.unlockedCount / achievements.totalCount) *
                    100
                  ).toFixed(0)}
                  %)
                </div>
              </div>

              {/* Progress Bar */}
              <div style={{ minWidth: "220px", flex: "0 1 300px" }}>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontSize: "0.8rem",
                    marginBottom: "0.35rem",
                  }}
                >
                  <span>Tier Progress</span>
                  <span style={{ fontWeight: 600, color: "var(--accent)" }}>
                    {achievements.unlockedCount}/{achievements.totalCount}
                  </span>
                </div>
                <div
                  style={{
                    width: "100%",
                    height: "10px",
                    backgroundColor: "rgba(0,0,0,0.5)",
                    borderRadius: "5px",
                    overflow: "hidden",
                  }}
                >
                  <div
                    style={{
                      width: `${(achievements.unlockedCount / achievements.totalCount) * 100}%`,
                      height: "100%",
                      backgroundColor: "var(--accent)",
                      transition: "width 0.3s ease",
                    }}
                  />
                </div>
              </div>
            </div>
          </div>

          {/* Badges Grid */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            {achievements.badges.map((badge) => (
              <div
                key={badge.id}
                className="card"
                style={{
                  padding: "1.25rem",
                  borderRadius: "8px",
                  border: badge.isUnlocked
                    ? "1px solid var(--accent)"
                    : "1px solid rgba(255, 255, 255, 0.08)",
                  backgroundColor: badge.isUnlocked
                    ? "var(--bg-secondary)"
                    : "rgba(25, 25, 25, 0.5)",
                  opacity: badge.isUnlocked ? 1 : 0.8,
                  display: "flex",
                  flexDirection: "column",
                  justifyContent: "space-between",
                  boxShadow:
                    "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
                }}
              >
                <div>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      marginBottom: "0.6rem",
                    }}
                  >
                    <span style={{ fontSize: "1.8rem" }}>{badge.icon}</span>
                    <span
                      className={`badge ${
                        badge.isUnlocked ? "badge-primary" : "badge-secondary"
                      }`}
                      style={{ fontSize: "0.75rem" }}
                    >
                      {badge.isUnlocked ? "✓ Unlocked" : "In Progress"}
                    </span>
                  </div>

                  <h3 style={{ margin: "0 0 0.35rem 0", fontSize: "1.05rem" }}>
                    {badge.name}
                  </h3>
                  <p
                    style={{
                      fontSize: "0.82rem",
                      color: "var(--text-muted)",
                      margin: "0 0 1rem 0",
                      lineHeight: "1.4",
                    }}
                  >
                    {badge.description}
                  </p>
                </div>

                <div>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      fontSize: "0.75rem",
                      marginBottom: "0.3rem",
                      color: "var(--text-secondary)",
                    }}
                  >
                    <span>Current: {badge.currentValueText}</span>
                    <span>Goal: {badge.targetValueText}</span>
                  </div>
                  <div
                    style={{
                      width: "100%",
                      height: "6px",
                      backgroundColor: "rgba(0,0,0,0.4)",
                      borderRadius: "3px",
                      overflow: "hidden",
                    }}
                  >
                    <div
                      style={{
                        width: `${badge.progress}%`,
                        height: "100%",
                        backgroundColor: badge.isUnlocked
                          ? "var(--accent)"
                          : "var(--text-muted)",
                      }}
                    />
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* TRACKER BUFFERS & BONUS POINTS TAB */}
      {activeTab === "buffers" && (
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          <div
            className="card"
            style={{
              padding: "1.25rem",
              borderRadius: "8px",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
              border: "1px solid rgba(255, 255, 255, 0.08)",
            }}
          >
            <h3 style={{ margin: "0 0 0.5rem 0", fontSize: "1.05rem" }}>
              Private Tracker Buffer & Bonus Point Estimator
            </h3>
            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                margin: 0,
              }}
            >
              Calculates your safe download buffer across trackers before
              dropping below 1.0 ratio, plus estimated hourly Bonus Points (BP)
              generated by your active swarms.
            </p>
          </div>

          <div
            className="card"
            style={{
              padding: 0,
              overflow: "hidden",
              borderRadius: "8px",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
              border: "1px solid rgba(255, 255, 255, 0.08)",
            }}
          >
            <div className="torrent-table-wrapper">
              <table className="torrent-table">
                <thead>
                  <tr>
                    <th className="torrent-table-th">Tracker Domain</th>
                    <th className="torrent-table-th">Active Torrents</th>
                    <th className="torrent-table-th">Total Uploaded</th>
                    <th className="torrent-table-th">Total Downloaded</th>
                    <th className="torrent-table-th">Ratio</th>
                    <th className="torrent-table-th">Safe Buffer (1.0x)</th>
                    <th
                      className="torrent-table-th"
                      style={{ textAlign: "right" }}
                    >
                      Est. Bonus Points
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {trackerBuffers.map((tb) => (
                    <tr key={tb.tracker} className="torrent-table-row">
                      <td>
                        <Link
                          to={`/torrents?tracker=${encodeURIComponent(tb.tracker)}`}
                          style={{
                            color: "inherit",
                            textDecoration: "none",
                            fontWeight: 600,
                          }}
                        >
                          {tb.tracker} ↗
                        </Link>
                      </td>
                      <td>{tb.torrentCount}</td>
                      <td style={{ fontWeight: 600 }}>
                        {formatBytes(tb.totalUploaded)}
                      </td>
                      <td>{formatBytes(tb.totalDownloaded)}</td>
                      <td>
                        <span
                          className={`badge ${tb.ratio >= 2.0 ? "badge-primary" : tb.ratio >= 1.0 ? "badge-secondary" : "badge-outline"}`}
                          style={{ fontSize: "0.75rem" }}
                        >
                          {formatRatio(tb.ratio)}
                        </span>
                      </td>
                      <td>
                        <span
                          style={{
                            fontWeight: 600,
                            color:
                              tb.bufferBytes > 0
                                ? "var(--accent, #c8a84e)"
                                : "inherit",
                          }}
                        >
                          +{formatBytes(tb.bufferBytes)}
                        </span>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <span
                          className="badge badge-secondary"
                          style={{ fontSize: "0.75rem" }}
                        >
                          ⚡ ~{tb.estimatedPointsPerHour} pts/hr
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* SIMULATOR TAB */}
      {activeTab === "simulator" && (
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>
              ⚡ Global Seeding & Ratio Milestone Simulator
            </h3>
            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                margin: "0 0 1rem 0",
              }}
            >
              Calculate estimated upload timelines, target ratios, and private
              tracker Hit & Run safety thresholds based on your live seeding
              transfer rates.
            </p>

            <SeedingSimulator
              currentUploaded={stats?.totalUploaded ?? 0}
              totalSize={
                stats?.totalDownloaded && stats.totalDownloaded > 0
                  ? stats.totalDownloaded
                  : (torrents ?? []).reduce((acc, t) => acc + t.totalSize, 0)
              }
              currentRatio={stats?.overallRatio ?? 0}
              currentUploadSpeed={stats?.uploadSpeed ?? 0}
            />
          </div>

          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.75rem 0" }}>
              Simulate Individual Swarms
            </h3>
            <div
              style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
            >
              {(torrents ?? []).slice(0, 5).map((t) => (
                <div
                  key={t.id}
                  style={{
                    padding: "1rem",
                    borderRadius: "8px",
                    backgroundColor: "var(--bg-secondary)",
                    border: "1px solid var(--border-light)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      marginBottom: "0.75rem",
                      flexWrap: "wrap",
                      gap: "0.5rem",
                    }}
                  >
                    <span style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                      {t.name}
                    </span>
                    <span className="badge badge-secondary">
                      {t.status} • {formatBytes(t.totalSize)}
                    </span>
                  </div>
                  <SeedingSimulator
                    currentUploaded={t.uploaded}
                    totalSize={t.totalSize}
                    currentRatio={t.ratio}
                    currentUploadSpeed={t.uploadSpeed}
                    seedingTimeSeconds={t.seedingTime}
                  />
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default Statistics;
