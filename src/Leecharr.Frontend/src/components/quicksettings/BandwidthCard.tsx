import React from "react";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import { DownloadIcon, UploadIcon } from "../icons/UIIcons";

const DL_PRESETS = [
  { label: "∞", value: 0 },
  { label: "5 MB/s", value: 5000 },
  { label: "10 MB/s", value: 10000 },
  { label: "25 MB/s", value: 25000 },
  { label: "50 MB/s", value: 50000 },
];

const UL_PRESETS = [
  { label: "∞", value: 0 },
  { label: "1 MB/s", value: 1000 },
  { label: "2.5 MB/s", value: 2500 },
  { label: "5 MB/s", value: 5000 },
  { label: "10 MB/s", value: 10000 },
];

export const BandwidthCard: React.FC = () => {
  const { data: config, isLoading } = useSeedingConfig();
  const saveMutation = useSaveSeedingConfig();

  const handleUpdate = (
    updates: Partial<import("../../api/types").SeedingConfig>,
  ) => {
    if (!config) return;
    saveMutation.mutate({
      ...config,
      ...updates,
    });
  };

  if (isLoading || !config) {
    return (
      <div className="quick-card loading">
        <div className="quick-card-header">
          <span className="quick-card-title">⚡ Bandwidth Limits</span>
        </div>
        <div className="quick-card-body">Loading bandwidth settings...</div>
      </div>
    );
  }

  const isAltActive = config.alternativeSpeedEnabled ?? false;
  const currentDl = config.maxDownloadSpeedKbps ?? 0;
  const currentUl = config.maxUploadSpeedKbps ?? 0;

  return (
    <div className="quick-card">
      <div className="quick-card-header">
        <span className="quick-card-title">⚡ Bandwidth Limits</span>
        <button
          type="button"
          className={`quick-pill-btn ${isAltActive ? "active-turtle" : ""}`}
          onClick={() =>
            handleUpdate({ alternativeSpeedEnabled: !isAltActive })
          }
          title="Toggle temporary Alternative Speed Limits (Turtle Mode)"
        >
          🐢 Turtle Mode: {isAltActive ? "ON" : "OFF"}
        </button>
      </div>

      <div className="quick-card-body">
        {/* Download Speed */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <DownloadIcon size={13} />
            <span>Max DL:</span>
            <span className="quick-control-current">
              {currentDl === 0
                ? "∞ Unlimited"
                : `${currentDl >= 1000 ? (currentDl / 1000).toFixed(1) + " MB/s" : currentDl + " KB/s"}`}
            </span>
          </div>
          <div className="quick-presets-group">
            {DL_PRESETS.map((p) => {
              const active = currentDl === p.value;
              return (
                <button
                  key={p.label}
                  type="button"
                  className={`preset-chip ${active ? "active" : ""}`}
                  onClick={() =>
                    handleUpdate({ maxDownloadSpeedKbps: p.value })
                  }
                >
                  {p.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Upload Speed */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <UploadIcon size={13} />
            <span>Max UL:</span>
            <span className="quick-control-current">
              {currentUl === 0
                ? "∞ Unlimited"
                : `${currentUl >= 1000 ? (currentUl / 1000).toFixed(1) + " MB/s" : currentUl + " KB/s"}`}
            </span>
          </div>
          <div className="quick-presets-group">
            {UL_PRESETS.map((p) => {
              const active = currentUl === p.value;
              return (
                <button
                  key={p.label}
                  type="button"
                  className={`preset-chip ${active ? "active" : ""}`}
                  onClick={() => handleUpdate({ maxUploadSpeedKbps: p.value })}
                >
                  {p.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Alternative Limits Sub-row if active */}
        {isAltActive && (
          <div className="quick-alt-speed-banner">
            <span
              style={{
                fontSize: "0.75rem",
                color: "var(--accent, #ffd166)",
                fontWeight: 600,
              }}
            >
              🐢 Alternative limits active:
            </span>
            <span
              style={{ fontSize: "0.75rem", color: "var(--text-secondary)" }}
            >
              DL: {config.altDownloadSpeedKbps ?? 2000} KB/s | UL:{" "}
              {config.altUploadSpeedKbps ?? 500} KB/s
            </span>
          </div>
        )}
      </div>
    </div>
  );
};

export default BandwidthCard;
