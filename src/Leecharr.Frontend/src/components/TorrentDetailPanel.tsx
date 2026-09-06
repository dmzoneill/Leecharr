import React, { useState, useMemo } from "react";
import { useTorrentStore, applyTelemetry } from "../stores/useTorrentStore";
import {
  useTorrent,
  useStartSeeding,
  useStopSeeding,
  useRecheckTorrent,
  useAnnounceTorrent,
} from "../api/hooks";
import {
  InfoIcon,
  ClipboardIcon,
  FileIcon,
  UsersIcon,
  GlobeIcon,
  SlidersIcon,
  ActivityIcon,
} from "./icons/UIIcons";
import { PeerMapIcon } from "./icons/AppIcons";
import { usePanelHeight } from "./torrentdetailpanel/shared";
import { StatusTab } from "./torrentdetailpanel/StatusTab";
import { DetailsTab } from "./torrentdetailpanel/DetailsTab";
import { FilesTab } from "./torrentdetailpanel/FilesTab";
import { PeersTab } from "./torrentdetailpanel/PeersTab";
import { TrackersTab } from "./torrentdetailpanel/TrackersTab";
import { OptionsTab } from "./torrentdetailpanel/OptionsTab";
import { MonitoringTab } from "./torrentdetailpanel/MonitoringTab";
import { LogTab } from "./torrentdetailpanel/LogTab";
import { CliTab } from "./torrentdetailpanel/CliTab";
import PieceMap from "./PieceMap";
import type { Torrent } from "../api/types";
import { useTranslation } from "../i18n";

export type DetailTab =
  | "status"
  | "details"
  | "files"
  | "cli"
  | "peers"
  | "trackers"
  | "options"
  | "piecemap"
  | "monitoring"
  | "log";

export interface TorrentDetailPanelProps {
  torrent?: Torrent | null;
  torrentId?: number | null;
  onClose: () => void;
}

const TAB_ICONS: Record<DetailTab, React.ReactNode> = {
  status: <InfoIcon size={13} />,
  details: <ClipboardIcon size={13} />,
  files: <FileIcon size={13} />,
  cli: (
    <span
      style={{ fontSize: "0.75rem", fontFamily: "monospace", fontWeight: 700 }}
    >
      &gt;_
    </span>
  ),
  peers: <UsersIcon size={13} />,
  trackers: <GlobeIcon size={13} />,
  options: <SlidersIcon size={13} />,
  piecemap: <PeerMapIcon size={13} />,
  monitoring: <ActivityIcon size={13} />,
  log: <span style={{ fontSize: "0.8rem", fontWeight: 700 }}>#</span>,
};

const DETAIL_TABS: { key: DetailTab; label: string }[] = [
  { key: "status", label: "Status" },
  { key: "details", label: "Details" },
  { key: "files", label: "Files" },
  { key: "cli", label: "CLI" },
  { key: "peers", label: "Peers" },
  { key: "trackers", label: "Trackers" },
  { key: "options", label: "Options" },
  { key: "piecemap", label: "Piece Map" },
  { key: "monitoring", label: "Monitoring" },
  { key: "log", label: "Engine Log" },
];

const getTabLabel = (key: DetailTab, t: (k: string) => string): string => {
  switch (key) {
    case "status":
      return t("torrents.tabs.status");
    case "details":
      return t("torrents.tabs.overview");
    case "files":
      return t("torrents.tabs.files");
    case "cli":
      return t("torrents.tabs.cli");
    case "peers":
      return t("torrents.tabs.peers");
    case "trackers":
      return t("torrents.tabs.trackers");
    case "options":
      return t("torrents.tabs.options");
    case "piecemap":
      return t("torrents.tabs.pieces");
    case "monitoring":
      return t("torrents.tabs.monitoring");
    case "log":
      return t("torrents.tabs.log");
    default:
      return key;
  }
};

class TabErrorBoundary extends React.Component<
  { children: React.ReactNode; tabKey: string },
  { hasError: boolean; error: Error | null }
> {
  constructor(props: { children: React.ReactNode; tabKey: string }) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error) {
    return { hasError: true, error };
  }

  componentDidUpdate(prevProps: { tabKey: string }) {
    if (prevProps.tabKey !== this.props.tabKey && this.state.hasError) {
      this.setState({ hasError: false, error: null });
    }
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ padding: "1.5rem", color: "var(--danger, #ef4444)" }}>
          <div style={{ fontWeight: 600, marginBottom: "0.5rem" }}>
            Failed to render tab contents
          </div>
          <p style={{ fontSize: "0.82rem", color: "var(--text-muted)" }}>
            {this.state.error?.message || "An unexpected error occurred."}
          </p>
          <button
            type="button"
            className="btn btn-small"
            onClick={() => this.setState({ hasError: false, error: null })}
          >
            Retry Tab
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}

export const TorrentDetailPanel: React.FC<TorrentDetailPanelProps> = ({
  torrent: initialTorrent,
  torrentId: initialTorrentId,
  onClose,
}) => {
  const { t } = useTranslation();
  const targetId = initialTorrent?.id ?? initialTorrentId ?? 0;
  const { data: fetchedTorrent, isLoading, isError } = useTorrent(targetId);
  const telemetry = useTorrentStore((state) =>
    targetId ? state.telemetry[targetId] : undefined,
  );
  const currentTorrent = useMemo(() => {
    const base = fetchedTorrent || initialTorrent;
    return base ? applyTelemetry(base, telemetry) : null;
  }, [fetchedTorrent, initialTorrent, telemetry]);

  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const recheckTorrent = useRecheckTorrent();
  const announceTorrent = useAnnounceTorrent();

  const [tab, setTab] = useState<DetailTab>("status");
  const { height, panelRef, onMouseDown } = usePanelHeight();

  if (isLoading && !currentTorrent) {
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-loading">
          {t("torrents.detail.loading")}
        </div>
      </div>
    );
  }

  if (isError && !currentTorrent) {
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-empty">
          {t("torrents.detail.failedToLoad")}
        </div>
      </div>
    );
  }

  if (!currentTorrent) {
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-empty">
          {t("torrents.detail.notFound")}
        </div>
      </div>
    );
  }

  const isPaused = currentTorrent.status?.toLowerCase() === "paused";

  return (
    <div className="detail-panel" ref={panelRef} style={{ height }}>
      {/* Resizable handle */}
      <div className="detail-panel-resize-handle" onMouseDown={onMouseDown} />

      {/* Top Header */}
      <div className="detail-panel-header">
        <div className="detail-panel-title">
          <span style={{ fontWeight: 700, color: "var(--accent, #ffd166)" }}>
            {currentTorrent.mediaTitle || currentTorrent.name}
          </span>
          {currentTorrent.mediaTitle &&
            currentTorrent.mediaTitle !== currentTorrent.name && (
              <span
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted, #7e8092)",
                  marginLeft: "0.5rem",
                }}
              >
                ({currentTorrent.name})
              </span>
            )}
          {currentTorrent.isPrivate ? (
            <span
              className="badge"
              style={{
                backgroundColor: "rgba(239, 68, 68, 0.2)",
                color: "#f87171",
                border: "1px solid rgba(239, 68, 68, 0.4)",
                fontSize: "0.7rem",
                marginLeft: "0.6rem",
                display: "inline-flex",
                alignItems: "center",
                gap: "4px",
              }}
              title={t("torrents.detail.privateSwarmTooltip")}
            >
              <i className="fas fa-lock" style={{ fontSize: "0.62rem" }} />{" "}
              {t("torrents.detail.privateSwarmBadge")}
            </span>
          ) : (
            <span
              className="badge"
              style={{
                backgroundColor: "rgba(59, 130, 246, 0.15)",
                color: "#60a5fa",
                fontSize: "0.7rem",
                marginLeft: "0.6rem",
                display: "inline-flex",
                alignItems: "center",
                gap: "4px",
              }}
              title={t("torrents.detail.publicSwarmTooltip")}
            >
              <i className="fas fa-globe" style={{ fontSize: "0.62rem" }} />{" "}
              {t("torrents.detail.publicSwarmBadge")}
            </span>
          )}
        </div>

        <div className="detail-panel-actions">
          {isPaused ? (
            <button
              type="button"
              className="btn btn-small btn-success"
              onClick={() => startSeeding.mutate(currentTorrent.id)}
            >
              {t("torrents.actions.start")}
            </button>
          ) : (
            <button
              type="button"
              className="btn btn-small btn-danger"
              onClick={() => stopSeeding.mutate(currentTorrent.id)}
            >
              {t("torrents.actions.stop")}
            </button>
          )}

          <button
            type="button"
            className="btn btn-small"
            onClick={() => recheckTorrent.mutate(currentTorrent.id)}
            title={t("torrents.actions.recheck")}
          >
            {t("torrents.actions.recheck")}
          </button>

          <button
            type="button"
            className="btn btn-small"
            onClick={() => announceTorrent.mutate(currentTorrent.id)}
            title={t("torrents.actions.announce")}
          >
            {t("torrents.actions.announce")}
          </button>

          <button
            type="button"
            className="btn btn-small"
            onClick={onClose}
            title={t("torrents.actions.close")}
          >
            X
          </button>
        </div>
      </div>

      {/* 9 Tab Navigation Bar */}
      <div className="detail-panel-tabs">
        {DETAIL_TABS.map((tTab) => (
          <button
            key={tTab.key}
            type="button"
            className={`tab-btn${tab === tTab.key ? " tab-btn-active" : ""}`}
            onClick={() => setTab(tTab.key)}
          >
            {TAB_ICONS[tTab.key]}
            <span>{getTabLabel(tTab.key, t)}</span>
          </button>
        ))}
      </div>

      {/* Tab Content Body */}
      <div className="detail-panel-body">
        <TabErrorBoundary tabKey={tab}>
          {tab === "status" && <StatusTab torrent={currentTorrent} />}
          {tab === "details" && <DetailsTab torrent={currentTorrent} />}
          {tab === "files" && (
            <FilesTab torrent={currentTorrent} torrentId={currentTorrent.id} />
          )}
          {tab === "cli" && <CliTab torrent={currentTorrent} />}
          {tab === "peers" && (
            <PeersTab torrent={currentTorrent} torrentId={currentTorrent.id} />
          )}
          {tab === "trackers" && (
            <TrackersTab
              torrent={currentTorrent}
              torrentId={currentTorrent.id}
            />
          )}
          {tab === "piecemap" && (
            <PieceMap
              torrentId={currentTorrent.id}
              pieceCount={currentTorrent.pieceCount}
              pieceLength={currentTorrent.pieceLength}
              progress={currentTorrent.progress}
              isSeeding={
                (currentTorrent.status || "").toLowerCase() === "seeding"
              }
              bitfield={currentTorrent.bitfield}
            />
          )}
          {tab === "monitoring" && (
            <MonitoringTab
              torrent={currentTorrent}
              torrentId={currentTorrent.id}
            />
          )}
          {tab === "options" && <OptionsTab torrent={currentTorrent} />}
          {tab === "log" && (
            <LogTab torrent={currentTorrent} torrentId={currentTorrent.id} />
          )}
        </TabErrorBoundary>
      </div>
    </div>
  );
};

export default TorrentDetailPanel;
