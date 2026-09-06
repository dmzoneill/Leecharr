import React from "react";
import {
  useSeedingConfig,
  useSaveSeedingConfig,
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
} from "../../api/hooks";
import { DiskStorageBadge } from "./DiskStorageBadge";
import { useToast } from "../../context/ToastContext";
import { useTranslation } from "../../i18n";

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
  const { t } = useTranslation();
  const { data: seedConfig, isLoading: seedLoading } = useSeedingConfig();
  const saveSeedMutation = useSaveSeedingConfig();

  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();
  const { showToast } = useToast();

  const handleSeedUpdate = (
    updates: Partial<import("../../api/types").SeedingConfig>,
  ) => {
    if (!seedConfig) return;
    saveSeedMutation.mutate(
      {
        ...seedConfig,
        ...updates,
      },
      {
        onError: (err: any) => {
          showToast(
            t("quickSettings.failedToUpdateSeeding", [err.message]),
            "error",
          );
        },
      },
    );
  };

  const handleBtUpdate = (
    updates: Partial<import("../../api/types").BitTorrentConfig>,
  ) => {
    if (!btConfig) return;
    saveBtMutation.mutate(
      {
        ...btConfig,
        ...updates,
      },
      {
        onError: (err: any) => {
          showToast(
            t("quickSettings.failedToUpdatePiecePicker", [err.message]),
            "error",
          );
        },
      },
    );
  };

  if (seedLoading || btLoading || !seedConfig || !btConfig) {
    return (
      <div className="quick-card loading">
        <div className="quick-card-header">
          <span className="quick-card-title">
            {t("quickSettings.seedingStorage")}
          </span>
        </div>
        <div className="quick-card-body">
          {t("quickSettings.loadingSeeding")}
        </div>
      </div>
    );
  }

  const currentRatio = seedConfig.globalSeedRatioLimit ?? 0;
  const currentStrategy = btConfig.piecePickerStrategy || "RarestFirst";
  const isSequential = currentStrategy.toLowerCase().includes("sequential");

  return (
    <div className="quick-card">
      <div className="quick-card-header">
        <span className="quick-card-title">
          {t("quickSettings.seedingStorage")}
        </span>
        <DiskStorageBadge compact />
      </div>

      <div className="quick-card-body">
        {/* Seed Ratio Limit */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <span>{t("quickSettings.ratioGoal")}</span>
            <span className="quick-control-current">
              {currentRatio === 0 ? "∞" : `${currentRatio.toFixed(1)}x`}
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
                  onClick={() =>
                    handleSeedUpdate({ globalSeedRatioLimit: p.value })
                  }
                  title={
                    p.value === 0
                      ? t("quickSettings.unlimitedRatioTitle")
                      : t("quickSettings.targetRatioTitle", [p.label])
                  }
                >
                  {p.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Piece Picker / Sequential Mode Toggle */}
        <div className="quick-toggle-row">
          <label
            className="quick-checkbox-label"
            title={t("quickSettings.sequentialPiecePickingTitle")}
          >
            <input
              type="checkbox"
              checked={isSequential}
              onChange={(e) =>
                handleBtUpdate({
                  piecePickerStrategy: e.target.checked
                    ? "Sequential"
                    : "RarestFirst",
                })
              }
            />
            <span>{t("quickSettings.sequentialPiecePicking")}</span>
          </label>
        </div>
      </div>
    </div>
  );
};

export default SeedingAutomationCard;
