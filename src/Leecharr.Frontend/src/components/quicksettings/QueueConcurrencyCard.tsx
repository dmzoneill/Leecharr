import React from "react";
import { useBitTorrentConfig, useSaveBitTorrentConfig } from "../../api/hooks";
import { PlayIcon, SeedingIcon } from "../icons/UIIcons";

export const QueueConcurrencyCard: React.FC = () => {
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const handleUpdate = (updates: Partial<import("../../api/types").BitTorrentConfig>) => {
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
          <span className="quick-card-title">📋 Queue & Concurrency</span>
        </div>
        <div className="quick-card-body">Loading queue settings...</div>
      </div>
    );
  }

  const activeDl = config.downloadQueueSize ?? 5;
  const activeSeed = config.seedQueueSize ?? 10;
  const ignoreStalled = config.queueStalledEnabled ?? true;

  const changeDl = (delta: number) => {
    const next = Math.max(0, activeDl + delta);
    handleUpdate({ downloadQueueSize: next });
  };

  const changeSeed = (delta: number) => {
    const next = Math.max(0, activeSeed + delta);
    handleUpdate({ seedQueueSize: next });
  };

  return (
    <div className="quick-card">
      <div className="quick-card-header">
        <span className="quick-card-title">📋 Queue Concurrency</span>
        <span className="quick-card-subtitle" style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
          Max active transfers
        </span>
      </div>

      <div className="quick-card-body">
        {/* Max Active Downloads */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <PlayIcon size={12} />
            <span>Max Active DL:</span>
          </div>
          <div className="quick-stepper">
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeDl(-1)}
              disabled={activeDl <= 0}
              title="Decrease max downloads"
            >
              -
            </button>
            <span className="stepper-value">
              {activeDl === 0 ? "∞" : activeDl}
            </span>
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeDl(1)}
              title="Increase max downloads"
            >
              +
            </button>
          </div>
        </div>

        {/* Max Active Seeds */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <SeedingIcon size={12} />
            <span>Max Active Seed:</span>
          </div>
          <div className="quick-stepper">
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeSeed(-1)}
              disabled={activeSeed <= 0}
              title="Decrease max seeds"
            >
              -
            </button>
            <span className="stepper-value">
              {activeSeed === 0 ? "∞" : activeSeed}
            </span>
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeSeed(1)}
              title="Increase max seeds"
            >
              +
            </button>
          </div>
        </div>

        {/* Stalled Torrents Handling */}
        <div className="quick-toggle-row">
          <label className="quick-checkbox-label">
            <input
              type="checkbox"
              checked={ignoreStalled}
              onChange={(e) => handleUpdate({ queueStalledEnabled: e.target.checked })}
            />
            <span>Ignore stalled torrents in queue limit</span>
          </label>
        </div>
      </div>
    </div>
  );
};

export default QueueConcurrencyCard;
