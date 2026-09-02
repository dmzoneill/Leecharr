import React from "react";
import {
  useSeedingConfig,
  useSaveSeedingConfig,
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
} from "../../api/hooks";
import { DiskStorageBadge } from "./DiskStorageBadge";

const RATIO_PRESETS = [
  { label: "1.0", value: 1.0 },
  { label: "1.5", value: 1.5 },
  { label: "2.0", value: 2.0 },
  { label: "∞", value: 0 },
];

interface SeedingAutomationCardProps {
  onNavigateSettings?: (tab: string) => void;
}

export const SeedingAutomationCard: React.FC<SeedingAutomationCardProps> = ({
  onNavigateSettings,
}) => {
  const { data: seedConfig, isLoading: seedLoading } = useSeedingConfig();
  const saveSeedMutation = useSaveSeedingConfig();

  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();

  const handleSeedUpdate = (updates: Partial<import("../../api/types").SeedingConfig>) => {
    if (!seedConfig) return;
    saveSeedMutation.mutate({
      ...seedConfig,
      ...updates,
    });
  };

  const handleBtUpdate = (updates: Partial<import("../../api/types").BitTorrentConfig>) => {
    if (!btConfig) return;
    saveBtMutation.mutate({
      ...btConfig,
      ...updates,
    });
  };

  if (seedLoading || btLoading || !seedConfig || !btConfig) {
    return (
      <div className="quick-card loading">
        <div className="quick-card-header">
          <span className="quick-card-title">🌾 Seeding & Storage</span>
        </div>
        <div className="quick-card-body">Loading seeding parameters...</div>
      </div>
    );
  }

  const currentRatio = seedConfig.globalSeedRatioLimit ?? 0;
  const currentStrategy = btConfig.piecePickerStrategy || "RarestFirst";
  const isSequential = currentStrategy.toLowerCase().includes("sequential");

  return (
    <div className="quick-card">
      <div className="quick-card-header">
        <span className="quick-card-title">🌾 Seeding & Storage</span>
        <DiskStorageBadge compact />
      </div>

      <div className="quick-card-body">
        {/* Seed Ratio Limit */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <span>Ratio Goal:</span>
            <span className="quick-control-current">
              {currentRatio === 0 ? "∞ No Limit" : `${currentRatio.toFixed(2)}x`}
            </span>
          </div>
          <div className="quick-presets-group">
            {RATIO_PRESETS.map((p) => {
              const active = currentRatio === p.value;
              return (
                <button
                  key={p.label}
                  type="button"
                  className={`preset-chip ${active ? "active" : ""}`}
                  onClick={() => handleSeedUpdate({ globalSeedRatioLimit: p.value })}
                >
                  {p.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Piece Picker / Sequential Mode Toggle */}
        <div className="quick-toggle-row" style={{ marginTop: "0.25rem" }}>
          <label className="quick-checkbox-label" title="Prioritize first and last pieces for instant media inspection">
            <input
              type="checkbox"
              checked={isSequential}
              onChange={(e) =>
                handleBtUpdate({
                  piecePickerStrategy: e.target.checked ? "Sequential" : "RarestFirst",
                })
              }
            />
            <span>Sequential piece picking by default</span>
          </label>
        </div>

        {/* Deep Link to Full Settings */}
        {onNavigateSettings && (
          <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "0.4rem" }}>
            <button
              type="button"
              className="quick-settings-link-btn"
              onClick={() => onNavigateSettings("speed")}
            >
              ⚙️ Full Settings &rarr;
            </button>
          </div>
        )}
      </div>
    </div>
  );
};

export default SeedingAutomationCard;
