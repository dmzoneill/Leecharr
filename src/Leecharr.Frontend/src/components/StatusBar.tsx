import React, { useMemo } from "react";
import {
  useSeedingStats,
  useNetworkStatus,
  useTorrents,
  useSystemStatus,
  useHealthChecks,
} from "../api/hooks";
import {
  formatBytes,
  formatSpeed,
  formatRatio,
  formatUptime,
} from "../utils/formatters";
import { useTranslation } from "../i18n";
import {
  useTorrentStore,
  useAggregatedTorrentMetrics,
} from "../stores/useTorrentStore";
import {
  SeedingIcon,
  UploadIcon,
  DownloadIcon,
  UsersIcon,
  WifiIcon,
  ActivityIcon,
  InfoIcon,
  ErrorIcon,
} from "./icons/UIIcons";

export interface StatusBarProps {
  connected?: boolean;
  isReconnecting?: boolean;
}

export function StatusBar({ connected, isReconnecting }: StatusBarProps = {}) {
  const { t } = useTranslation();

  const { data: stats } = useSeedingStats();
  const { data: network } = useNetworkStatus();
  const { data: torrents } = useTorrents();
  const { data: systemStatus } = useSystemStatus();
  const { data: healthChecks } = useHealthChecks();

  const {
    totalDlSpeed,
    totalUlSpeed,
    activeCount,
    totalSeeders,
    totalLeechers,
    totalUploaded,
    totalDownloaded,
    averageRatio,
  } = useAggregatedTorrentMetrics(torrents, stats);

  const totalPeers = totalSeeders + totalLeechers;

  const hasIssues =
    healthChecks &&
    healthChecks.some((c) => c.type === "Warning" || c.type === "Error");
  const issuesCount = hasIssues
    ? healthChecks.filter((c) => c.type === "Warning" || c.type === "Error")
        .length
    : 0;

  return (
    <footer className="status-bar">
      <div className="status-bar-content">
        <span className="status-bar-item">
          <InfoIcon size={14} />{" "}
          {systemStatus?.version
            ? `v${systemStatus.version}`
            : t("statusBar.loading")}
        </span>
        <span className="status-bar-item">
          <ActivityIcon size={14} /> {t("statusBar.uptime")}{" "}
          {systemStatus
            ? formatUptime(
                systemStatus.uptimeSeconds ??
                  (systemStatus.startTime
                    ? Math.floor(
                        (Date.now() -
                          new Date(systemStatus.startTime).getTime()) /
                          1000,
                      )
                    : 0),
              )
            : "..."}
        </span>
        <span
          className="status-bar-item"
          style={{ color: hasIssues ? "var(--danger)" : "var(--success)" }}
        >
          {hasIssues ? <ErrorIcon size={14} /> : <InfoIcon size={14} />}
          {t("statusBar.health")}{" "}
          {hasIssues
            ? issuesCount === 1
              ? t("statusBar.healthIssues", { count: issuesCount })
              : t("statusBar.healthIssuesPlural", { count: issuesCount })
            : t("statusBar.healthOk")}
        </span>
        {(connected !== undefined || isReconnecting !== undefined) && (
          <span
            className="status-bar-item"
            style={{
              color: isReconnecting
                ? "var(--accent, #ffd166)"
                : connected
                  ? "var(--success)"
                  : "var(--danger)",
            }}
          >
            <WifiIcon size={14} />{" "}
            {isReconnecting
              ? t("statusBar.reconnecting")
              : connected
                ? t("statusBar.connected")
                : t("statusBar.disconnected")}
          </span>
        )}

        <div className="status-bar-separator" style={{ flexGrow: 1 }} />

        <span className="status-bar-item">
          <SeedingIcon size={14} /> {t("statusBar.active")} {activeCount}
        </span>
        <span className="status-bar-item status-bar-download">
          <DownloadIcon size={14} /> {formatSpeed(totalDlSpeed)}
        </span>
        <span className="status-bar-item status-bar-upload">
          <UploadIcon size={14} /> {formatSpeed(totalUlSpeed)}
        </span>
        <span className="status-bar-item">
          <UsersIcon size={14} /> {t("statusBar.peers")} {totalSeeders} /{" "}
          {totalPeers}
        </span>
        <span className="status-bar-item">
          <UploadIcon size={14} /> {t("statusBar.totalUp")}{" "}
          {formatBytes(totalUploaded)}
        </span>
        <span className="status-bar-item">
          <DownloadIcon size={14} /> {t("statusBar.totalDown")}{" "}
          {formatBytes(totalDownloaded)}
        </span>
        <span className="status-bar-item">
          {t("statusBar.ratio")} {formatRatio(averageRatio)}
        </span>
        <span className="status-bar-item">
          <WifiIcon size={14} /> {t("statusBar.ip")}{" "}
          {network?.externalIp || "..."}
        </span>
      </div>
    </footer>
  );
}

export default StatusBar;
