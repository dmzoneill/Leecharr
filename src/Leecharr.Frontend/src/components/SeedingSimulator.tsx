import { useState, useMemo } from "react";
import { formatBytes, formatSpeed } from "../utils/formatters";

interface SeedingSimulatorProps {
  currentUploaded: number;
  totalSize: number;
  currentRatio: number;
  currentUploadSpeed: number; // bytes/sec
  seedingTimeSeconds?: number;
  minSeedingHours?: number;
  className?: string;
}

export function SeedingSimulator({
  currentUploaded,
  totalSize,
  currentRatio,
  currentUploadSpeed,
  seedingTimeSeconds = 0,
  minSeedingHours = 72,
  className,
}: SeedingSimulatorProps) {
  const [targetRatio, setTargetRatio] = useState<number>(2.0);

  // Target calculations
  const targetUploadBytes = totalSize * targetRatio;
  const remainingUploadBytes = Math.max(0, targetUploadBytes - currentUploaded);

  const etaSeconds = useMemo(() => {
    if (remainingUploadBytes <= 0) return 0;
    if (currentUploadSpeed <= 0) return null;
    return remainingUploadBytes / currentUploadSpeed;
  }, [remainingUploadBytes, currentUploadSpeed]);

  const formatDuration = (sec: number | null): string => {
    if (sec === null) return "Unknown (speed is 0 B/s)";
    if (sec <= 0) return "Achieved! 🎉";
    const days = Math.floor(sec / 86400);
    const hours = Math.floor((sec % 86400) / 3600);
    const minutes = Math.floor((sec % 3600) / 60);

    if (days > 0) return `${days}d ${hours}h ${minutes}m`;
    if (hours > 0) return `${hours}h ${minutes}m`;
    return `${minutes}m ${Math.floor(sec % 60)}s`;
  };

  // HnR Safety check
  const requiredSeedingSeconds = minSeedingHours * 3600;
  const hnrProgress = Math.min(
    1.0,
    requiredSeedingSeconds > 0
      ? seedingTimeSeconds / requiredSeedingSeconds
      : 1.0,
  );
  const hnrRemainingSeconds = Math.max(
    0,
    requiredSeedingSeconds - seedingTimeSeconds,
  );
  const isHnrCleared = hnrRemainingSeconds <= 0 || currentRatio >= 1.0;

  return (
    <div
      className={className}
      style={{
        padding: "1rem",
        backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
        borderRadius: "8px",
        border: "1px solid var(--border-light)",
        display: "flex",
        flexDirection: "column",
        gap: "0.85rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.5rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <span style={{ fontSize: "1.1rem" }}>🎯</span>
          <h4 style={{ margin: 0, fontSize: "0.95rem" }}>
            Seeding Milestone & Ratio Calculator
          </h4>
        </div>
        <span
          className="badge badge-primary"
          style={{ fontSize: "0.75rem", fontFamily: "monospace" }}
        >
          Speed: {formatSpeed(currentUploadSpeed)}
        </span>
      </div>

      {/* Preset target buttons */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "0.5rem",
          flexWrap: "wrap",
        }}
      >
        <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
          Target Ratio:
        </span>
        {[1.0, 1.5, 2.0, 3.0, 5.0].map((r) => (
          <button
            key={r}
            type="button"
            className={`btn btn-sm ${targetRatio === r ? "btn-primary" : "btn-action"}`}
            style={{
              padding: "0.2rem 0.55rem",
              fontSize: "0.75rem",
              fontWeight: targetRatio === r ? 700 : 500,
            }}
            onClick={() => setTargetRatio(r)}
          >
            {r.toFixed(1)}x
          </button>
        ))}
        <div
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: "0.3rem",
            marginLeft: "auto",
          }}
        >
          <input
            type="number"
            step="0.1"
            min="0.1"
            max="100"
            value={targetRatio}
            onChange={(e) => setTargetRatio(parseFloat(e.target.value) || 1.0)}
            className="form-control"
            style={{
              width: "70px",
              padding: "0.2rem 0.4rem",
              fontSize: "0.78rem",
            }}
          />
          <span style={{ fontSize: "0.8rem" }}>ratio</span>
        </div>
      </div>

      {/* Milestone projection cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
          gap: "0.6rem",
        }}
      >
        <div className="stat-card" style={{ padding: "0.6rem" }}>
          <div
            className="stat-value"
            style={{ fontSize: "1.1rem", color: "var(--accent)" }}
          >
            {formatBytes(targetUploadBytes)}
          </div>
          <div className="stat-label" style={{ fontSize: "0.72rem" }}>
            Target Upload
          </div>
        </div>

        <div className="stat-card" style={{ padding: "0.6rem" }}>
          <div className="stat-value" style={{ fontSize: "1.1rem" }}>
            {formatBytes(remainingUploadBytes)}
          </div>
          <div className="stat-label" style={{ fontSize: "0.72rem" }}>
            Upload Needed
          </div>
        </div>

        <div className="stat-card" style={{ padding: "0.6rem" }}>
          <div
            className="stat-value"
            style={{
              fontSize: "1.1rem",
              color: etaSeconds === 0 ? "var(--success)" : "inherit",
            }}
          >
            {formatDuration(etaSeconds)}
          </div>
          <div className="stat-label" style={{ fontSize: "0.72rem" }}>
            Estimated Time
          </div>
        </div>
      </div>

      {/* Hit & Run (HnR) Tracker Safety Shield */}
      <div
        style={{
          padding: "0.65rem 0.85rem",
          borderRadius: "6px",
          backgroundColor: isHnrCleared
            ? "rgba(39, 174, 96, 0.1)"
            : "rgba(230, 126, 34, 0.12)",
          border: isHnrCleared
            ? "1px solid rgba(39, 174, 96, 0.3)"
            : "1px solid rgba(230, 126, 34, 0.35)",
          display: "flex",
          flexDirection: "column",
          gap: "0.35rem",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            fontSize: "0.8rem",
          }}
        >
          <span
            style={{
              fontWeight: 600,
              display: "inline-flex",
              alignItems: "center",
              gap: "0.35rem",
            }}
          >
            {isHnrCleared ? "🛡️ HnR Cleared" : "⚠️ Hit & Run Risk Guard"}
          </span>
          <span
            className={`badge ${isHnrCleared ? "badge-success" : "badge-warning"}`}
            style={{ fontSize: "0.68rem" }}
          >
            {isHnrCleared
              ? "Safe to stop"
              : `${(hnrProgress * 100).toFixed(0)}% Seeding Time`}
          </span>
        </div>

        {!isHnrCleared && (
          <div style={{ fontSize: "0.74rem", color: "var(--text-muted)" }}>
            Requires {minSeedingHours}h seeding time or 1.0x ratio. Seed for{" "}
            <strong>{formatDuration(hnrRemainingSeconds)}</strong> more to avoid
            private tracker warning.
          </div>
        )}
      </div>
    </div>
  );
}

export default SeedingSimulator;
