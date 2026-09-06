import React from "react";
import { BandwidthCard } from "./BandwidthCard";
import { QueueConcurrencyCard } from "./QueueConcurrencyCard";
import { NetworkSwarmCard } from "./NetworkSwarmCard";
import { SeedingAutomationCard } from "./SeedingAutomationCard";
import { SlidersIcon } from "../icons/UIIcons";
import { useTranslation } from "../../i18n";

interface QuickSettingsDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  onNavigateSettings?: (tab: string) => void;
}

export const QuickSettingsDrawer: React.FC<QuickSettingsDrawerProps> = ({
  isOpen,
  onClose,
  onNavigateSettings,
}) => {
  const { t } = useTranslation();

  if (!isOpen) return null;

  return (
    <div className="quick-settings-drawer">
      <div className="quick-settings-drawer-header">
        <div className="quick-settings-title-group">
          <SlidersIcon size={14} className="quick-settings-icon" />
          <span className="quick-settings-heading">
            {t("quickSettings.title")}
          </span>
          <span className="quick-settings-hint">
            {t("quickSettings.subtitle")} (Hotkey: <kbd>Q</kbd>)
          </span>
        </div>

        <div className="quick-settings-actions">
          {onNavigateSettings && (
            <button
              type="button"
              className="btn btn-small btn-outline quick-settings-header-btn"
              onClick={() => onNavigateSettings("speed")}
              title={t("quickSettings.openFullSettingsTitle")}
            >
              {t("quickSettings.viewAllSettings")}
            </button>
          )}
          <button
            type="button"
            className="quick-settings-close-btn"
            onClick={onClose}
            title={t("quickSettings.closeTitle")}
          >
            ✕
          </button>
        </div>
      </div>

      <div className="quick-settings-grid">
        <BandwidthCard />
        <QueueConcurrencyCard />
        <NetworkSwarmCard />
        <SeedingAutomationCard onNavigateSettings={onNavigateSettings} />
      </div>
    </div>
  );
};

export default QuickSettingsDrawer;
