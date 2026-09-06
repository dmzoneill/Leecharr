import React from "react";
import { useBitTorrentConfig, useSaveBitTorrentConfig } from "../../api/hooks";
import { PlayIcon, SeedingIcon } from "../icons/UIIcons";
import { useToast } from "../../context/ToastContext";
import { useTranslation } from "../../i18n";

export const QueueConcurrencyCard: React.FC = () => {
  const { t } = useTranslation();
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();
  const { showToast } = useToast();

  const handleUpdate = (
    updates: Partial<import("../../api/types").BitTorrentConfig>,
  ) => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        ...updates,
      },
      {
        onError: (err: any) => {
          showToast(
            t("quickSettings.failedToUpdateQueue", [err.message]),
            "error",
          );
        },
      },
    );
  };

  if (isLoading || !config) {
    return (
      <div className="quick-card loading">
        <div className="quick-card-header">
          <span className="quick-card-title">
            {t("quickSettings.queueConcurrency")}
          </span>
        </div>
        <div className="quick-card-body">{t("quickSettings.loadingQueue")}</div>
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
        <span className="quick-card-title">
          {t("quickSettings.queueConcurrency")}
        </span>
        <span
          className="quick-card-subtitle"
          style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}
        >
          {t("quickSettings.maxActiveTransfers")}
        </span>
      </div>

      <div className="quick-card-body">
        {/* Max Active Downloads */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <PlayIcon size={12} />
            <span>{t("quickSettings.maxActiveDl")}</span>
          </div>
          <div className="quick-stepper">
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeDl(-1)}
              disabled={activeDl <= 0}
              title={t("quickSettings.decreaseMaxDownloads")}
            >
              −
            </button>
            <span className="stepper-value">
              {activeDl === 0 ? "∞" : activeDl}
            </span>
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeDl(1)}
              title={t("quickSettings.increaseMaxDownloads")}
            >
              +
            </button>
          </div>
        </div>

        {/* Max Active Seeds */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <SeedingIcon size={12} />
            <span>{t("quickSettings.maxActiveSeed")}</span>
          </div>
          <div className="quick-stepper">
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeSeed(-1)}
              disabled={activeSeed <= 0}
              title={t("quickSettings.decreaseMaxSeeds")}
            >
              −
            </button>
            <span className="stepper-value">
              {activeSeed === 0 ? "∞" : activeSeed}
            </span>
            <button
              type="button"
              className="stepper-btn"
              onClick={() => changeSeed(1)}
              title={t("quickSettings.increaseMaxSeeds")}
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
              onChange={(e) =>
                handleUpdate({ queueStalledEnabled: e.target.checked })
              }
            />
            <span>{t("quickSettings.ignoreStalled")}</span>
          </label>
        </div>
      </div>
    </div>
  );
};

export default QueueConcurrencyCard;
