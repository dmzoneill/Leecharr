import React from "react";
import {
  formatBytes,
  formatDate,
  formatRatio,
  formatSeconds,
  formatSpeed,
  extractTrackerDomain,
} from "../../utils/formatters";
import type { Torrent } from "../../api/types";
import { InfoRow } from "./shared";

export function StatusTab({ torrent }: { torrent: Torrent }) {
  const percent = Math.min(100, Math.max(0, (torrent.progress ?? 0) * 100));
  const isComplete = percent >= 100;
  const isSeeding = (torrent.status || "").toLowerCase() === "seeding";
  const isPaused = (torrent.status || "").toLowerCase() === "paused";
  const isError = (torrent.status || "").toLowerCase() === "error";

  const statusColor = isError
    ? "var(--danger, #ef4444)"
    : isComplete || isSeeding
      ? "var(--success, #22c55e)"
      : isPaused
        ? "var(--warning, #f59e0b)"
        : "var(--accent, #ffd166)";

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      {/* Top Telemetry & Speed Highlights */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
          gap: "0.5rem",
          padding: "0.6rem 0.8rem",
          backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
          borderRadius: "6px",
          border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
          alignItems: "center",
        }}
      >
        <div>
          <div
            style={{
              fontSize: "0.7rem",
              color: "var(--text-muted)",
              textTransform: "uppercase",
            }}
          >
            Status
          </div>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              marginTop: "0.15rem",
            }}
          >
            <span
              style={{
                width: "8px",
                height: "8px",
                borderRadius: "50%",
                backgroundColor: statusColor,
              }}
            />
            <span
              style={{
                fontWeight: 700,
                fontSize: "0.9rem",
                color: statusColor,
                textTransform: "capitalize",
              }}
            >
              {torrent.status || "Unknown"}
            </span>
          </div>
        </div>

        <div>
          <div
            style={{
              fontSize: "0.7rem",
              color: "var(--text-muted)",
              textTransform: "uppercase",
            }}
          >
            Download Speed
          </div>
          <div
            style={{
              fontWeight: 700,
              fontSize: "0.9rem",
              color: "#06d6a0",
              marginTop: "0.15rem",
            }}
          >
            ↓ {formatSpeed(torrent.downloadSpeed || 0)}
          </div>
        </div>

        <div>
          <div
            style={{
              fontSize: "0.7rem",
              color: "var(--text-muted)",
              textTransform: "uppercase",
            }}
          >
            Upload Speed
          </div>
          <div
            style={{
              fontWeight: 700,
              fontSize: "0.9rem",
              color: "var(--accent, #ffd166)",
              marginTop: "0.15rem",
            }}
          >
            ↑ {formatSpeed(torrent.uploadSpeed || 0)}
          </div>
        </div>

        <div>
          <div
            style={{
              fontSize: "0.7rem",
              color: "var(--text-muted)",
              textTransform: "uppercase",
            }}
          >
            Estimated Time (ETA)
          </div>
          <div
            style={{
              fontWeight: 600,
              fontSize: "0.9rem",
              color: "var(--text-primary)",
              marginTop: "0.15rem",
            }}
          >
            ⏱ {isComplete ? "Done" : formatSeconds(torrent.eta)}
          </div>
        </div>

        <div>
          <div
            style={{
              fontSize: "0.7rem",
              color: "var(--text-muted)",
              textTransform: "uppercase",
            }}
          >
            Share Ratio
          </div>
          <div
            style={{
              fontWeight: 700,
              fontSize: "0.9rem",
              color:
                (torrent.ratio || 0) >= 1.0 ? "#22c55e" : "var(--text-primary)",
              marginTop: "0.15rem",
            }}
          >
            {formatRatio(torrent.ratio || 0)}
          </div>
        </div>
      </div>

      {/* Progress Bar */}
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          gap: "0.25rem",
          padding: "0.5rem 0.8rem",
          backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
          borderRadius: "6px",
          border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            fontSize: "0.75rem",
          }}
        >
          <span>
            <strong>{percent.toFixed(1)}%</strong> (
            {formatBytes(torrent.downloaded || 0)} of{" "}
            {formatBytes(torrent.totalSize || 0)})
          </span>
          <span style={{ color: "var(--text-muted)" }}>
            {formatBytes(
              Math.max(0, (torrent.totalSize || 0) - (torrent.downloaded || 0)),
            )}{" "}
            remaining
          </span>
        </div>
        <div
          style={{
            height: "8px",
            backgroundColor: "rgba(255, 255, 255, 0.08)",
            borderRadius: "4px",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              width: `${percent}%`,
              height: "100%",
              background: isComplete
                ? "linear-gradient(90deg, #27ae60 0%, #2ecc71 100%)"
                : "linear-gradient(90deg, #c8a84e 0%, #ffd166 100%)",
              transition: "width 0.3s ease",
            }}
          />
        </div>
      </div>

      {/* 3 Detail Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
          gap: "0.75rem",
        }}
      >
        {/* Card 1: Transfer Telemetry */}
        <div
          style={{
            backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            padding: "0.6rem 0.8rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.35rem",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              textTransform: "uppercase",
              letterSpacing: "0.05em",
              borderBottom:
                "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              paddingBottom: "0.25rem",
              marginBottom: "0.2rem",
            }}
          >
            Transfer Telemetry
          </div>
          <InfoRow
            label="Downloaded"
            value={formatBytes(torrent.downloaded || 0)}
            mono
          />
          <InfoRow
            label="Uploaded"
            value={formatBytes(torrent.uploaded || 0)}
            mono
          />
          <InfoRow
            label="Total Size"
            value={formatBytes(torrent.totalSize || 0)}
            mono
          />
          <InfoRow
            label="Download Limit"
            value={
              torrent.downloadLimit > 0
                ? `${torrent.downloadLimit} KB/s`
                : "Unlimited"
            }
          />
          <InfoRow
            label="Upload Limit"
            value={
              torrent.uploadLimit > 0
                ? `${torrent.uploadLimit} KB/s`
                : "Unlimited"
            }
          />
        </div>

        {/* Card 2: Swarm & Networking */}
        <div
          style={{
            backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            padding: "0.6rem 0.8rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.35rem",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              textTransform: "uppercase",
              letterSpacing: "0.05em",
              borderBottom:
                "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              paddingBottom: "0.25rem",
              marginBottom: "0.2rem",
            }}
          >
            Swarm & Network
          </div>
          <InfoRow
            label="Connected Seeders"
            value={String(torrent.seeders || 0)}
          />
          <InfoRow
            label="Connected Leechers"
            value={String(torrent.leechers || 0)}
          />
          <InfoRow
            label="Tracker Domain"
            value={extractTrackerDomain(torrent.trackerUrl)}
          />
          <InfoRow
            label="Announce Interval"
            value={
              torrent.announceInterval
                ? `${torrent.announceInterval}s (${Math.round(torrent.announceInterval / 60)}m)`
                : "1800s (30m)"
            }
          />
          <InfoRow
            label="Next Update"
            value={
              torrent.nextUpdate != null
                ? torrent.nextUpdate > 60
                  ? `${Math.floor(torrent.nextUpdate / 60)}m ${torrent.nextUpdate % 60}s`
                  : `${torrent.nextUpdate}s`
                : "1800s"
            }
          />
          <InfoRow
            label="Priority"
            value={
              torrent.priority === 2
                ? "High"
                : torrent.priority === 1
                  ? "Normal"
                  : "Low"
            }
          />
          <InfoRow
            label="Flags"
            value={[
              torrent.isPrivate ? "🔒 Private (BEP 27)" : "🌐 Public",
              torrent.sequentialDownload ? "Sequential" : null,
              torrent.forceStart ? "Forced" : null,
              torrent.initialSeeding ? "Super-Seed" : null,
            ]
              .filter(Boolean)
              .join(" • ")}
          />
        </div>

        {/* Card 3: Seeding & Lifecycle */}
        <div
          style={{
            backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            padding: "0.6rem 0.8rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.35rem",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              textTransform: "uppercase",
              letterSpacing: "0.05em",
              borderBottom:
                "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              paddingBottom: "0.25rem",
              marginBottom: "0.2rem",
            }}
          >
            Seeding & Lifecycle
          </div>
          <InfoRow
            label="Seeding Time"
            value={formatSeconds(torrent.seedingTime || 0)}
          />
          <InfoRow
            label="Target Ratio"
            value={
              torrent.targetRatio && torrent.targetRatio > 0
                ? `${torrent.targetRatio.toFixed(2)}x`
                : "Global Default"
            }
          />
          <InfoRow label="Added" value={formatDate(torrent.dateAdded)} />
          <InfoRow
            label="Completed"
            value={
              torrent.dateCompleted ? formatDate(torrent.dateCompleted) : "-"
            }
          />
          <InfoRow label="Last Active" value={formatDate(torrent.lastActive)} />
          <InfoRow
            label="Category / Label"
            value={`${torrent.category || "Default"} / ${torrent.label || "-"}`}
          />
        </div>
      </div>
    </div>
  );
}
