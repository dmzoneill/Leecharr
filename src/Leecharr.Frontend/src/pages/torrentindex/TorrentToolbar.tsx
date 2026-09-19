import React, { useMemo } from "react";
import { formatSpeed } from "../../utils/formatters";
import {
  PlusIcon,
  PlayIcon,
  StopIcon,
  TableIcon,
  GridIcon,
  SlidersIcon,
  FilterIcon,
} from "../../components/icons/UIIcons";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import { DiskStorageBadge } from "../../components/quicksettings/DiskStorageBadge";
import { ViewMode } from "./types";
import { useTranslation } from "../../i18n";
import { useTorrentStore } from "../../stores/useTorrentStore";
import type { Torrent } from "../../api/types";

export interface ToolbarSpeedSummaryProps {
  torrents?: Torrent[];
  totalUploadSpeed?: number;
  totalDownloadSpeed?: number;
}

export const ToolbarSpeedSummary: React.FC<ToolbarSpeedSummaryProps> =
  React.memo(
    ({ torrents, totalUploadSpeed: propUl, totalDownloadSpeed: propDl }) => {
      const telemetry = useTorrentStore((state) => state.telemetry);
      const { totalUploadSpeed, totalDownloadSpeed } = useMemo(() => {
        if (propUl !== undefined && propDl !== undefined) {
          return { totalUploadSpeed: propUl, totalDownloadSpeed: propDl };
        }
        let ul = 0;
        let dl = 0;
        if (torrents) {
          for (const t of torrents) {
            const tel = telemetry[t.id];
            ul += tel?.uploadSpeed ?? t.uploadSpeed ?? 0;
            dl += tel?.downloadSpeed ?? t.downloadSpeed ?? 0;
          }
        }
        return { totalUploadSpeed: ul, totalDownloadSpeed: dl };
      }, [torrents, telemetry, propUl, propDl]);

      return (
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
      );
    },
  );
ToolbarSpeedSummary.displayName = "ToolbarSpeedSummary";

interface TorrentToolbarProps {
  count: number;
  torrents?: Torrent[];
  totalUploadSpeed?: number;
  totalDownloadSpeed?: number;
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
  onBulkMoveQueue?: (position: "top" | "up" | "down" | "bottom") => void;
  showQuickSettings?: boolean;
  onToggleQuickSettings?: () => void;
  isFilterCollapsed?: boolean;
  onToggleFilter?: () => void;
}

export function TorrentToolbar({
  count,
  torrents,
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
  onBulkMoveQueue,
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
                ? t("torrents.toolbar.showFilters")
                : t("torrents.toolbar.hideFilters")
            }
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "5px",
              fontSize: "0.8rem",
              padding: "0.3rem 0.6rem",
            }}
          >
            <FilterIcon size={12} />
            <span>{isFilterCollapsed ? "▶" : "◀"}</span>
            <span>{t("common.filter")}</span>
          </button>
        )}
        <h1 className="page-heading">
          {t("nav.torrents")} ({count})
        </h1>
        {selectedCount > 0 ? (
          <div className="bulk-actions">
            <span className="bulk-actions-count">
              {t("filebrowser.selectedCount", { count: selectedCount })}
            </span>
            <button
              type="button"
              className="btn btn-success"
              onClick={onBulkStart}
              disabled={bulkPending}
            >
              <PlayIcon size={13} /> {t("torrents.actions.resume")}
            </button>
            <button
              type="button"
              className="btn btn-outline"
              onClick={onBulkStop}
              disabled={bulkPending}
            >
              <StopIcon size={13} /> {t("torrents.actions.pause")}
            </button>
            <button
              type="button"
              className="btn btn-danger"
              onClick={onBulkDelete}
              disabled={bulkPending}
            >
              {t("common.delete")}
            </button>
            {onBulkMoveQueue && (
              <div
                className="btn-group"
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "2px",
                  marginLeft: "4px",
                  borderLeft:
                    "1px solid var(--border, rgba(255, 255, 255, 0.15))",
                  paddingLeft: "6px",
                }}
              >
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("top")}
                  disabled={bulkPending}
                  title={t("torrents.contextMenu.top", {
                    defaultValue: "Move to Top",
                  })}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ⤒ {t("torrents.contextMenu.top", { defaultValue: "Top" })}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("up")}
                  disabled={bulkPending}
                  title={t("torrents.contextMenu.up", {
                    defaultValue: "Move Up",
                  })}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ▲ {t("torrents.contextMenu.up", { defaultValue: "Up" })}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("down")}
                  disabled={bulkPending}
                  title={t("torrents.contextMenu.down", {
                    defaultValue: "Move Down",
                  })}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ▼ {t("torrents.contextMenu.down", { defaultValue: "Down" })}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("bottom")}
                  disabled={bulkPending}
                  title={t("torrents.contextMenu.bottom", {
                    defaultValue: "Move to Bottom",
                  })}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ⤓{" "}
                  {t("torrents.contextMenu.bottom", { defaultValue: "Bottom" })}
                </button>
              </div>
            )}
            <button
              type="button"
              className="btn btn-outline"
              onClick={onBulkClear}
              disabled={bulkPending}
            >
              {t("common.reset")}
            </button>
          </div>
        ) : (
          <>
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
                title={t("torrents.toolbar.toggleQuickSettings")}
              >
                <SlidersIcon size={13} /> {t("settings.general")}
              </button>
            )}
          </>
        )}
      </div>
      <div className="page-header-actions">
        <button
          type="button"
          className={`quick-pill-btn ${isAltActive ? "active-turtle" : ""}`}
          onClick={toggleTurtleMode}
          title={
            isAltActive
              ? t("torrents.toolbar.turtleTitleActive")
              : t("torrents.toolbar.turtleTitleInactive")
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
          {isAltActive
            ? t("torrents.toolbar.turtleOn")
            : t("torrents.toolbar.turtleOff")}
        </button>
        <DiskStorageBadge compact />
        <button
          type="button"
          className="btn btn-success"
          onClick={onStartAll}
          title={t("torrents.toolbar.resumeAll")}
        >
          <PlayIcon size={13} />{" "}
          {t("torrents.toolbar.resumeAll") || t("torrents.actions.resume")}
        </button>
        <button
          type="button"
          className="btn btn-danger"
          onClick={onStopAll}
          title={t("torrents.toolbar.pauseAll")}
        >
          <StopIcon size={13} />{" "}
          {t("torrents.toolbar.pauseAll") || t("torrents.actions.pause")}
        </button>
        <input
          type="text"
          className="search-input"
          placeholder={t("torrents.toolbar.filterPlaceholder")}
          value={filter}
          onChange={(e) => onFilterChange(e.target.value)}
        />
        <div className="view-toggle">
          <button
            type="button"
            className={`view-toggle-btn${viewMode === "table" ? " active" : ""}`}
            onClick={() => onViewModeChange("table")}
            title={t("torrents.toolbar.tableView")}
          >
            <TableIcon size={13} /> {t("torrents.toolbar.table")}
          </button>
          <button
            type="button"
            className={`view-toggle-btn${viewMode === "grid" ? " active" : ""}`}
            onClick={() => onViewModeChange("grid")}
            title={t("torrents.toolbar.gridView")}
          >
            <GridIcon size={13} /> {t("torrents.toolbar.grid")}
          </button>
        </div>
      </div>
    </div>
  );
}

export default TorrentToolbar;
