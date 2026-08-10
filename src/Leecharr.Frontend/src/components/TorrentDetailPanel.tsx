import React, { useState } from "react";
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
import PieceMap from "./PieceMap";
import type { Torrent } from "../api/types";

export type DetailTab =
  | "status"
  | "details"
  | "files"
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
  { key: "peers", label: "Peers" },
  { key: "trackers", label: "Trackers" },
  { key: "options", label: "Options" },
  { key: "piecemap", label: "Piece Map" },
  { key: "monitoring", label: "Monitoring" },
  { key: "log", label: "Engine Log" },
];

export const TorrentDetailPanel: React.FC<TorrentDetailPanelProps> = ({
  torrent: initialTorrent,
  torrentId: initialTorrentId,
  onClose,
}) => {
  const targetId = initialTorrent?.id ?? initialTorrentId ?? 0;
  const { data: fetchedTorrent, isLoading, isError } = useTorrent(targetId);
  const currentTorrent = fetchedTorrent || initialTorrent;

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
          Loading torrent specifications...
        </div>
      </div>
    );
  }

  if (isError && !currentTorrent) {
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-empty">
          Failed to load torrent specifications.
        </div>
      </div>
    );
  }

  if (!currentTorrent) {
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-empty">Torrent not found</div>
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
        </div>

        <div className="detail-panel-actions">
          {isPaused ? (
            <button
              type="button"
              className="btn btn-small btn-success"
              onClick={() => startSeeding.mutate(currentTorrent.id)}
            >
              ▶ Resume
            </button>
          ) : (
            <button
              type="button"
              className="btn btn-small btn-warning"
              onClick={() => stopSeeding.mutate(currentTorrent.id)}
            >
              ⏸ Pause
            </button>
          )}

          <button
            type="button"
            className="btn btn-small btn-outline"
            onClick={() => recheckTorrent.mutate(currentTorrent.id)}
            title="Force recheck torrent piece integrity"
          >
            🛡 Recheck
          </button>

          <button
            type="button"
            className="btn btn-small btn-outline"
            onClick={() => announceTorrent.mutate(currentTorrent.id)}
            title="Force announce to all trackers"
          >
            ⚡ Announce
          </button>

          <button
            type="button"
            className="btn-icon"
            onClick={onClose}
            title="Close details panel"
            style={{
              fontSize: "1.2rem",
              color: "var(--text-muted, #7e8092)",
              padding: "0 0.4rem",
            }}
          >
            &times;
          </button>
        </div>
      </div>

      {/* 9 Tab Navigation Bar */}
      <div className="detail-panel-tabs">
        {DETAIL_TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            className={`tab-btn${tab === t.key ? " tab-btn-active" : ""}`}
            onClick={() => setTab(t.key)}
          >
            {TAB_ICONS[t.key]}
            <span>{t.label}</span>
          </button>
        ))}
      </div>

      {/* Tab Content Body */}
      <div className="detail-panel-body">
        {tab === "status" && <StatusTab torrent={currentTorrent} />}
        {tab === "details" && <DetailsTab torrent={currentTorrent} />}
        {tab === "files" && <FilesTab torrent={currentTorrent} />}
        {tab === "peers" && <PeersTab torrent={currentTorrent} />}
        {tab === "trackers" && <TrackersTab torrent={currentTorrent} />}
        {tab === "options" && <OptionsTab torrent={currentTorrent} />}
        {tab === "piecemap" && <PieceMap torrentId={currentTorrent.id} />}
        {tab === "monitoring" && <MonitoringTab torrent={currentTorrent} />}
        {tab === "log" && <LogTab torrent={currentTorrent} />}
      </div>
    </div>
  );
};

export default TorrentDetailPanel;
