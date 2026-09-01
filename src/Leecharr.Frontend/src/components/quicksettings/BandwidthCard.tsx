import React from "react";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import { DownloadIcon, UploadIcon } from "../icons/UIIcons";
import { useToast } from "../../context/ToastContext";

const DL_PRESETS = [
  { label: "∞", value: 0, title: "Unlimited" },
  { label: "5M", value: 5000, title: "5 MB/s" },
  { label: "10M", value: 10000, title: "10 MB/s" },
  { label: "25M", value: 25000, title: "25 MB/s" },
  { label: "50M", value: 50000, title: "50 MB/s" },
];

const UL_PRESETS = [
  { label: "∞", value: 0, title: "Unlimited" },
  { label: "1M", value: 1000, title: "1 MB/s" },
  { label: "2.5M", value: 2500, title: "2.5 MB/s" },
  { label: "5M", value: 5000, title: "5 MB/s" },
  { label: "10M", value: 10000, title: "10 MB/s" },
];

export const BandwidthCard: React.FC = () => {
  const { data: config, isLoading } = useSeedingConfig();
  const saveMutation = useSaveSeedingConfig();
  const { showToast } = useToast();

  const handleUpdate = (
    updates: Partial<import("../../api/types").SeedingConfig>,
  ) => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        ...updates,
      },
      {
        onError: (err: any) => {
          showToast(`Failed to update speed limit: ${err.message}`, "error");
        },
      },
    );
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
                ? "∞"
                : `${currentDl >= 1000 ? (currentDl / 1000).toFixed(currentDl % 1000 === 0 ? 0 : 1) + " MB/s" : currentDl + " KB/s"}`}
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
                  title={p.title}
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
                ? "∞"
                : `${currentUl >= 1000 ? (currentUl / 1000).toFixed(currentUl % 1000 === 0 ? 0 : 1) + " MB/s" : currentUl + " KB/s"}`}
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
                  title={p.title}
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
