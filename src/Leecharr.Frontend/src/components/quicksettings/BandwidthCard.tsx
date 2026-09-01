import React, { useState, useEffect } from "react";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import { DownloadIcon, UploadIcon } from "../icons/UIIcons";
import { useToast } from "../../context/ToastContext";

const DL_STEPS: number[] = [
  0, 50, 100, 150, 200, 250, 300, 350, 400, 450, 500, 600, 700, 800, 900, 1000,
  1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 6000, 7000, 8000, 9000, 10000,
  12000, 14000, 16000, 18000, 20000, 25000, 30000, 40000, 50000, 60000, 75000,
  100000,
];

const UL_STEPS: number[] = [
  0, 50, 100, 150, 200, 250, 300, 350, 400, 450, 500, 600, 700, 800, 900, 1000,
  1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 6000, 7000, 8000, 9000, 10000,
  12000, 15000, 20000, 25000, 30000, 40000, 50000,
];

function valueToStepIndex(val: number, steps: number[]): number {
  if (val <= 0) return 0;
  let closestIdx = 0;
  let minDiff = Infinity;
  for (let i = 0; i < steps.length; i++) {
    const diff = Math.abs(steps[i] - val);
    if (diff < minDiff) {
      minDiff = diff;
      closestIdx = i;
    }
  }
  return closestIdx;
}

function formatSpeedLimit(kbps: number): string {
  if (kbps <= 0) return "∞";
  if (kbps < 1000) return `${kbps} KB/s`;
  const mb = kbps / 1000;
  return `${Number(mb.toFixed(mb >= 10 || mb % 1 === 0 ? 0 : 1))} MB/s`;
}

export const BandwidthCard: React.FC = () => {
  const { data: config, isLoading } = useSeedingConfig();
  const saveMutation = useSaveSeedingConfig();
  const { showToast } = useToast();

  const serverDl = config?.maxDownloadSpeedKbps ?? 0;
  const serverUl = config?.maxUploadSpeedKbps ?? 0;

  const [localDl, setLocalDl] = useState<number>(serverDl);
  const [localUl, setLocalUl] = useState<number>(serverUl);
  const [isDraggingDl, setIsDraggingDl] = useState(false);
  const [isDraggingUl, setIsDraggingUl] = useState(false);

  useEffect(() => {
    if (!isDraggingDl) {
      setLocalDl(serverDl);
    }
  }, [serverDl, isDraggingDl]);

  useEffect(() => {
    if (!isDraggingUl) {
      setLocalUl(serverUl);
    }
  }, [serverUl, isDraggingUl]);

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

  const dlIndex = valueToStepIndex(localDl, DL_STEPS);
  const dlPercent = (dlIndex / (DL_STEPS.length - 1)) * 100;

  const ulIndex = valueToStepIndex(localUl, UL_STEPS);
  const ulPercent = (ulIndex / (UL_STEPS.length - 1)) * 100;

  const handleDlChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setIsDraggingDl(true);
    const idx = Number(e.target.value);
    setLocalDl(DL_STEPS[idx] ?? 0);
  };

  const commitDlChange = () => {
    setIsDraggingDl(false);
    handleUpdate({ maxDownloadSpeedKbps: localDl });
  };

  const setDlDirect = (val: number) => {
    setLocalDl(val);
    handleUpdate({ maxDownloadSpeedKbps: val });
  };

  const handleUlChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setIsDraggingUl(true);
    const idx = Number(e.target.value);
    setLocalUl(UL_STEPS[idx] ?? 0);
  };

  const commitUlChange = () => {
    setIsDraggingUl(false);
    handleUpdate({ maxUploadSpeedKbps: localUl });
  };

  const setUlDirect = (val: number) => {
    setLocalUl(val);
    handleUpdate({ maxUploadSpeedKbps: val });
  };

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
        {/* Download Speed Slider */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <DownloadIcon size={13} />
            <span>Max DL:</span>
            <span className="quick-control-current">
              {formatSpeedLimit(localDl)}
            </span>
          </div>
          <div className="quick-slider-container">
            <button
              type="button"
              className={`quick-slider-bound ${localDl === 0 ? "active" : ""}`}
              onClick={() => setDlDirect(0)}
              title="Set Download to Unlimited (∞)"
            >
              ∞
            </button>
            <input
              type="range"
              min={0}
              max={DL_STEPS.length - 1}
              value={dlIndex}
              onChange={handleDlChange}
              onPointerUp={commitDlChange}
              onKeyUp={commitDlChange}
              className="quick-range-slider"
              style={{
                background: `linear-gradient(to right, var(--accent, #ffd166) 0%, var(--accent, #ffd166) ${dlPercent}%, rgba(255, 255, 255, 0.12) ${dlPercent}%, rgba(255, 255, 255, 0.12) 100%)`,
              }}
              title={`Max Download: ${formatSpeedLimit(localDl)}`}
              aria-label="Max Download Speed Limit"
            />
            <button
              type="button"
              className={`quick-slider-bound ${localDl === DL_STEPS[DL_STEPS.length - 1] ? "active" : ""}`}
              onClick={() => setDlDirect(DL_STEPS[DL_STEPS.length - 1])}
              title={`Set Download to Max (${formatSpeedLimit(DL_STEPS[DL_STEPS.length - 1])})`}
            >
              100M
            </button>
          </div>
        </div>

        {/* Upload Speed Slider */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <UploadIcon size={13} />
            <span>Max UL:</span>
            <span className="quick-control-current">
              {formatSpeedLimit(localUl)}
            </span>
          </div>
          <div className="quick-slider-container">
            <button
              type="button"
              className={`quick-slider-bound ${localUl === 0 ? "active" : ""}`}
              onClick={() => setUlDirect(0)}
              title="Set Upload to Unlimited (∞)"
            >
              ∞
            </button>
            <input
              type="range"
              min={0}
              max={UL_STEPS.length - 1}
              value={ulIndex}
              onChange={handleUlChange}
              onPointerUp={commitUlChange}
              onKeyUp={commitUlChange}
              className="quick-range-slider"
              style={{
                background: `linear-gradient(to right, var(--accent, #ffd166) 0%, var(--accent, #ffd166) ${ulPercent}%, rgba(255, 255, 255, 0.12) ${ulPercent}%, rgba(255, 255, 255, 0.12) 100%)`,
              }}
              title={`Max Upload: ${formatSpeedLimit(localUl)}`}
              aria-label="Max Upload Speed Limit"
            />
            <button
              type="button"
              className={`quick-slider-bound ${localUl === UL_STEPS[UL_STEPS.length - 1] ? "active" : ""}`}
              onClick={() => setUlDirect(UL_STEPS[UL_STEPS.length - 1])}
              title={`Set Upload to Max (${formatSpeedLimit(UL_STEPS[UL_STEPS.length - 1])})`}
            >
              50M
            </button>
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
