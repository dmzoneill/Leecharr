import React from "react";
import { formatSpeed } from "../../utils/formatters";
import {
  PlusIcon,
  PlayIcon,
  StopIcon,
  TableIcon,
  GridIcon,
  SlidersIcon,
} from "../../components/icons/UIIcons";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import { DiskStorageBadge } from "../../components/quicksettings/DiskStorageBadge";
import { ViewMode } from "./types";
import { useTranslation } from "../../i18n";

interface TorrentToolbarProps {
  count: number;
  totalUploadSpeed: number;
  totalDownloadSpeed: number;
  filter: string;
  onFilterChange: (value: string) => void;
  viewMode: ViewMode;
  onViewModeChange: (mode: ViewMode) => void;
  onAddTorrent: () => void;
  onSearchIndexers?: () => void;
  onStartAll: () => void;
  onStopAll: () => void;
  selectedCount: number;
  bulkPending?: boolean;
  onBulkStart: () => void;
  onBulkStop: () => void;
  onBulkDelete: () => void;
  onBulkClear: () => void;
  showQuickSettings?: boolean;
  onToggleQuickSettings?: () => void;
  isFilterCollapsed?: boolean;
  onToggleFilter?: () => void;
}

export function TorrentToolbar({
  count,
  totalUploadSpeed,
  totalDownloadSpeed,
  filter,
  onFilterChange,
  viewMode,
  onViewModeChange,
  onAddTorrent,
  onSearchIndexers,
  onStartAll,
  onStopAll,
  selectedCount,
  bulkPending = false,
  onBulkStart,
  onBulkStop,
  onBulkDelete,
  onBulkClear,
  showQuickSettings = false,
  onToggleQuickSettings,
  isFilterCollapsed = false,
  onToggleFilter,
}: TorrentToolbarProps) {
  const { t } = useTranslation();
  const { data: seedConfig } = useSeedingConfig();
  const saveSeedMutation = useSaveSeedingConfig();

  const isAltActive = seedConfig?.alternativeSpeedEnabled ?? false;

  const toggleTurtleMode = () => {
    if (!seedConfig) return;
    saveSeedMutation.mutate({
      ...seedConfig,
      alternativeSpeedEnabled: !isAltActive,
    });
  };
  return (
    <div className="page-header" style={{ marginBottom: 0 }}>
      <div className="page-header-group">
        {onToggleFilter && (
          <button
            type="button"
            className={`btn btn-small ${isFilterCollapsed ? "btn-outline" : "btn-secondary"}`}
            onClick={onToggleFilter}
            title={
              isFilterCollapsed
                ? "Show Filters Sidebar (State / Tracker)"
                : "Hide Filters Sidebar"
            }
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "4px",
              fontSize: "0.8rem",
              padding: "0.3rem 0.6rem",
            }}
          >
            <span>{isFilterCollapsed ? "▶" : "◀"}</span>
            <span>{t("common.filter")}</span>
          </button>
        )}
        <h1 className="page-heading">
          {t("nav.torrents")} ({count})
        </h1>
        <button
          type="button"
          className="btn btn-success"
          onClick={onAddTorrent}
        >
          <PlusIcon size={13} /> {t("modals.addTorrent")}
        </button>
        {onSearchIndexers && (
          <button
            type="button"
            className="btn btn-outline"
            onClick={onSearchIndexers}
            style={{ fontSize: "0.82rem" }}
          >
            🔍 {t("modals.indexerSearch")}
          </button>
        )}
        {onToggleQuickSettings && (
          <button
            type="button"
            className={`btn ${showQuickSettings ? "btn-primary" : "btn-outline"}`}
            onClick={onToggleQuickSettings}
            style={{
              fontSize: "0.82rem",
              display: "inline-flex",
              alignItems: "center",
              gap: "5px",
            }}
            title="Toggle Quick Settings Drawer (Q)"
          >
            <SlidersIcon size={13} /> {t("settings.general")}
          </button>
        )}
        {selectedCount > 0 && (
          <div className="bulk-actions">
            <span className="bulk-actions-count">
              {t("filebrowser.selectedCount", { count: selectedCount })}
            </span>
            <button
              type="button"
              className="btn btn-small btn-success"
              onClick={onBulkStart}
              disabled={bulkPending}
            >
              <PlayIcon size={12} /> {t("torrents.actions.resume")}
            </button>
            <button
              type="button"
              className="btn btn-small"
              onClick={onBulkStop}
              disabled={bulkPending}
            >
              <StopIcon size={12} /> {t("torrents.actions.pause")}
            </button>
            <button
              type="button"
              className="btn btn-small btn-danger"
              onClick={onBulkDelete}
              disabled={bulkPending}
            >
              {t("common.delete")}
            </button>
            <button
              type="button"
              className="btn btn-small"
              onClick={onBulkClear}
              disabled={bulkPending}
            >
              {t("common.reset")}
            </button>
          </div>
        )}
      </div>
      <div className="page-header-actions">
        <button
          type="button"
          className={`quick-pill-btn ${isAltActive ? "active-turtle" : ""}`}
          onClick={toggleTurtleMode}
          title={
            isAltActive
              ? "Turtle Mode ON (Alternative speed limits active)"
              : "Toggle Turtle Mode (Alternative speed limits)"
          }
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: "4px",
            padding: "4px 8px",
            borderRadius: "6px",
            fontSize: "0.8rem",
            fontWeight: 600,
          }}
        >
          🐢 {isAltActive ? "Turtle: ON" : "Turtle: OFF"}
        </button>
        <DiskStorageBadge compact />
        <button
          type="button"
          className="btn btn-success"
          onClick={onStartAll}
          title="Resume all torrents"
        >
          <PlayIcon size={13} /> {t("torrents.actions.resume")}
        </button>
        <button
          type="button"
          className="btn btn-danger"
          onClick={onStopAll}
          title="Pause all torrents"
        >
          <StopIcon size={13} /> {t("torrents.actions.pause")}
        </button>
        <div
          className="speed-controls"
          style={{ display: "flex", alignItems: "center", gap: "4px" }}
        >
          <span style={{ fontSize: "0.85em", opacity: 0.8 }}>
            UL: {formatSpeed(totalUploadSpeed)}
          </span>
          <span style={{ fontSize: "0.85em", opacity: 0.8, marginLeft: "8px" }}>
            DL: {formatSpeed(totalDownloadSpeed)}
          </span>
        </div>
        <input
          type="text"
          className="search-input"
          placeholder="Filter torrents..."
          value={filter}
          onChange={(e) => onFilterChange(e.target.value)}
        />
        <div className="view-toggle">
          <button
            type="button"
            className={`view-toggle-btn${viewMode === "table" ? " active" : ""}`}
            onClick={() => onViewModeChange("table")}
            title="Table view"
          >
            <TableIcon size={13} /> Table
          </button>
          <button
            type="button"
            className={`view-toggle-btn${viewMode === "grid" ? " active" : ""}`}
            onClick={() => onViewModeChange("grid")}
            title="Grid view"
          >
            <GridIcon size={13} /> Grid
          </button>
        </div>
      </div>
    </div>
  );
}

export default TorrentToolbar;
