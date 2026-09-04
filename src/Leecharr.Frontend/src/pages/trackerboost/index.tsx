import { useState, useMemo } from "react";
import {
  useTorrents,
  useDownloadHistory,
  useTrackerBoostStatus,
  useTrackerBoostTrackers,
  useTrackerBoostLogs,
} from "../../api/hooks";
import { HarvesterPanel } from "./HarvesterPanel";
import { MatrixView } from "./MatrixView";
import { RadarView } from "./RadarView";
import { LogViewer } from "./LogViewer";
import { ImportTools } from "./ImportTools";
import type { UnifiedDownloadItem } from "./types";

export * from "./types";
export * from "./HarvesterPanel";
export * from "./MatrixView";
export * from "./RadarView";
export * from "./LogViewer";
export * from "./ImportTools";

export function TrackerBoost() {
  const { data: torrents, isLoading: torrentsLoading } = useTorrents();
  const { data: history } = useDownloadHistory();
  const { data: status } = useTrackerBoostStatus();
  const { data: trackers } = useTrackerBoostTrackers();
  const { data: boostLogs } = useTrackerBoostLogs(250);

  const [activeTab, setActiveTab] = useState<"booster" | "matrix" | "radar" | "logs" | "settings">(
    "booster"
  );
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [showBulkImportModal, setShowBulkImportModal] = useState(false);

  // Build unified items list
  const unifiedItems = useMemo<UnifiedDownloadItem[]>(() => {
    const list: UnifiedDownloadItem[] = [];
    const seenHashes = new Set<string>();

    (torrents ?? []).forEach((t) => {
      const hash = (t.infoHash || "").toLowerCase();
      if (hash) seenHashes.add(hash);
      list.push({
        key: `leecharr-${t.id}`,
        id: t.id,
        infoHash: t.infoHash || "",
        name: t.name,
        totalSize: t.totalSize,
        ratio: t.ratio,
        seeders: t.seeders,
        isPrivate: t.isPrivate,
        sourceType: "leecharr",
        clientName: "Leecharr Engine",
      });
    });

    (history ?? []).forEach((h) => {
      const hash = (h.infoHash || "").toLowerCase();
      if (hash && !seenHashes.has(hash)) {
        seenHashes.add(hash);
        list.push({
          key: `history-${h.id}`,
          id: h.torrentId || 0,
          infoHash: h.infoHash,
          name: h.title,
          totalSize: h.totalSize,
          ratio: 0,
          seeders: 0,
          isPrivate: false,
          sourceType: "real_client",
          clientName: h.source || "Download Client",
        });
      }
    });

    return list;
  }, [torrents, history]);

  const torrentMetaMap = useMemo(() => {
    const map = new Map<
      string,
      {
        posterUrl?: string | null;
        mediaTitle?: string | null;
        source?: string | null;
        year?: number | null;
        totalSize?: number;
      }
    >();

    (torrents ?? []).forEach((t) => {
      if (t.infoHash) {
        map.set(t.infoHash.toLowerCase(), {
          posterUrl: t.posterUrl,
          mediaTitle: t.mediaTitle,
          source: t.source,
          year: t.mediaYear ?? t.year,
          totalSize: t.totalSize,
        });
      }
    });

    (history ?? []).forEach((h) => {
      if (h.infoHash && !map.has(h.infoHash.toLowerCase())) {
        map.set(h.infoHash.toLowerCase(), {
          posterUrl: h.metadata?.posterUrl,
          mediaTitle: h.metadata?.title || h.title,
          source: h.source,
          year: h.metadata?.year,
          totalSize: h.totalSize,
        });
      }
    });

    return map;
  }, [torrents, history]);

  const handleInspectTorrent = (infoHash: string) => {
    const targetKey = unifiedItems.find(
      (u) => u.infoHash.toLowerCase() === infoHash.toLowerCase()
    )?.key;
    if (targetKey) setSelectedKey(targetKey);
    setActiveTab("booster");
  };

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      {/* Top Header Row */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              flexWrap: "wrap",
            }}
          >
            <h1
              className="page-heading"
              style={{
                margin: 0,
                padding: 0,
                background: "transparent",
                border: "none",
              }}
            >
              Tracker Boost
            </h1>
            <span className="badge badge-primary">⚡ Smart Booster</span>
            <span className="badge badge-secondary">BEP 15 & 48 Scraper</span>
          </div>
          <div
            style={{
              fontSize: "0.85rem",
              color: "var(--text-muted)",
              marginTop: "0.3rem",
            }}
          >
            Scrapes live tracker swarms by info_hash to discover and inject verified seeders/peers
            into Leecharr and download clients
          </div>
        </div>
      </div>

      {/* Global Metric Cards */}
      <div className="stats-grid" style={{ marginBottom: "1rem", flexShrink: 0 }}>
        <div className="stat-card">
          <div className="stat-value">{status?.totalTrackersMonitored ?? 0}</div>
          <div className="stat-label">Trackers Monitored</div>
        </div>
        <div className="stat-card">
          <div className="stat-value" style={{ color: "var(--success)" }}>
            {status?.aliveTrackersCount ?? 0}
          </div>
          <div className="stat-label">Alive & Responsive</div>
        </div>
        <div className="stat-card">
          <div className="stat-value" style={{ color: "var(--accent)" }}>
            {status?.activeTorrentTrackersCount ?? 0}
          </div>
          <div className="stat-label">Harvested from Swarms</div>
        </div>
        <div className="stat-card">
          <div className="stat-value" style={{ color: "#38bdf8" }}>
            {status?.torrentsBoostedCount ?? 0}
          </div>
          <div className="stat-label">Swarms Boosted</div>
        </div>
      </div>

      {/* Tab Navigation Bar placed right above content */}
      <div
        style={{
          display: "flex",
          gap: "0.5rem",
          alignItems: "center",
          marginBottom: "1rem",
          paddingBottom: "0.75rem",
          borderBottom: "1px solid var(--border-light)",
          flexWrap: "wrap",
          flexShrink: 0,
        }}
      >
        <button
          className={`btn ${activeTab === "booster" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("booster")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "booster" ? 600 : 500,
          }}
        >
          ⚡ Swarm Optimizer
        </button>
        <button
          className={`btn ${activeTab === "matrix" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("matrix")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "matrix" ? 600 : 500,
          }}
        >
          📊 Cross-Matrix
        </button>
        <button
          className={`btn ${activeTab === "radar" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("radar")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "radar" ? 600 : 500,
          }}
        >
          📡 Tracker Radar ({trackers?.length || 0})
        </button>
        <button
          className={`btn ${activeTab === "logs" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("logs")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "logs" ? 600 : 500,
          }}
        >
          📜 Activity Logs {boostLogs && boostLogs.length > 0 ? `(${boostLogs.length})` : ""}
        </button>
        <button
          className={`btn ${activeTab === "settings" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("settings")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "settings" ? 600 : 500,
          }}
        >
          ⚙️ Sources & Automation
        </button>
      </div>

      {/* Render Active View */}
      {activeTab === "booster" && (
        <HarvesterPanel
          unifiedItems={unifiedItems}
          torrentsLoading={torrentsLoading}
          selectedKey={selectedKey}
          onSelectKey={setSelectedKey}
        />
      )}

      {activeTab === "matrix" && (
        <MatrixView torrentMetaMap={torrentMetaMap} onInspectTorrent={handleInspectTorrent} />
      )}

      {activeTab === "radar" && <RadarView onOpenBulkImport={() => setShowBulkImportModal(true)} />}

      {activeTab === "logs" && <LogViewer />}

      {activeTab === "settings" && (
        <ImportTools
          showModal={showBulkImportModal}
          onCloseModal={() => setShowBulkImportModal(false)}
        />
      )}
    </div>
  );
}

export default TrackerBoost;
