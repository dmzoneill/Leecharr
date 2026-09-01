import { useState, useMemo } from "react";
import {
  useTorrents,
  useDownloadHistory,
  useDownloadClients,
  useTrackerBoostStatus,
  useTrackerBoostTrackers,
  useTrackerBoostSettings,
  useUpdateTrackerBoostSettings,
  useTrackerBoostMatrix,
  useInspectTorrentTrackers,
  useInspectHashTrackers,
  useScanTrackerBoostTrackers,
  useHarvestDownloadTrackers,
  useHarvestProwlarrTrackers,
  useHarvestFeedTrackers,
  useBoostTorrent,
  useBoostHash,
  useInjectTrackerToTorrent,
  useBoostAllTorrents,
  useAddTrackerBoostTracker,
  useDeleteTrackerBoostTracker,
  useTrackerBoostLogs,
  useClearTrackerBoostLogs,
} from "../api/hooks";
import { formatBytes, formatRatio } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import type { TrackerBoostSettings } from "../api/types";
import TrackerFavicon from "../components/TrackerFavicon";

interface UnifiedDownloadItem {
  key: string;
  id?: number;
  infoHash: string;
  name: string;
  totalSize: number;
  ratio: number;
  seeders: number;
  isPrivate: boolean;
  sourceType: "real_client" | "leecharr";
  clientName: string;
}

function TrackerBoost() {
  const { data: torrents, isLoading: torrentsLoading } = useTorrents();
  const { data: history } = useDownloadHistory();
  const { data: downloadClients } = useDownloadClients();
  const { data: status } = useTrackerBoostStatus();
  const { data: trackers, isLoading: trackersLoading } =
    useTrackerBoostTrackers();
  const { data: settings } = useTrackerBoostSettings();
  const updateSettings = useUpdateTrackerBoostSettings();
  const { data: matrixData, isLoading: matrixLoading } =
    useTrackerBoostMatrix();
  const { showToast } = useToast();

  const [activeTab, setActiveTab] = useState<
    "booster" | "matrix" | "radar" | "logs" | "settings"
  >("booster");
  const [downloadFilter, setDownloadFilter] = useState<
    "all" | "public" | "private" | "real" | "leecharr"
  >("all");
  const [downloadSearch, setDownloadSearch] = useState("");
  const [matrixViewMode, setMatrixViewMode] = useState<
    "by_torrent" | "by_tracker"
  >("by_torrent");
  const [matrixLayoutMode, setMatrixLayoutMode] = useState<"grid" | "table">(
    "grid",
  );
  const [matrixSearch, setMatrixSearch] = useState("");
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [trackerSearch, setTrackerSearch] = useState("");
  const [sourceFilter, setSourceFilter] = useState<string>("all");
  const [healthFilter, setHealthFilter] = useState<string>("all");
  const [newTrackerUrl, setNewTrackerUrl] = useState("");
  const [isAddingTracker, setIsAddingTracker] = useState(false);
  const [showBulkImportModal, setShowBulkImportModal] = useState(false);
  const [bulkImportText, setBulkImportText] = useState("");
  const [isBulkImporting, setIsBulkImporting] = useState(false);

  // Activity Logs state
  const [logLevelFilter, setLogLevelFilter] = useState<string>("all");
  const [logCategoryFilter, setLogCategoryFilter] = useState<string>("all");
  const [logSearch, setLogSearch] = useState<string>("");
  const [logAutoRefresh, setLogAutoRefresh] = useState<boolean>(true);

  const {
    data: boostLogs,
    isLoading: logsLoading,
    refetch: refetchLogs,
  } = useTrackerBoostLogs(
    250,
    logCategoryFilter,
    logLevelFilter,
    activeTab === "logs" && logAutoRefresh ? 3000 : false,
  );
  const clearLogs = useClearTrackerBoostLogs();

  const handleClearLogs = () => {
    clearLogs.mutate(undefined, {
      onSuccess: () => {
        showToast("Activity logs cleared", "info");
      },
      onError: (err) => {
        showToast(`Failed to clear logs: ${err.message}`, "error");
      },
    });
  };

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
          year: t.year,
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

  const filteredMatrixTorrents = useMemo(() => {
    return (matrixData?.torrents ?? []).filter((t) => {
      if (!matrixSearch.trim()) return true;
      const q = matrixSearch.toLowerCase();
      const meta = torrentMetaMap.get((t.infoHash || "").toLowerCase());
      return (
        (t.torrentName || "").toLowerCase().includes(q) ||
        (meta?.mediaTitle && meta.mediaTitle.toLowerCase().includes(q)) ||
        (t.infoHash || "").toLowerCase().includes(q) ||
        (t.trackers || []).some((tr) =>
          (tr.trackerHost || tr.trackerUrl || "").toLowerCase().includes(q),
        )
      );
    });
  }, [matrixData?.torrents, matrixSearch, torrentMetaMap]);

  const filteredMatrixTrackers = useMemo(() => {
    return (matrixData?.trackers ?? []).filter((tr) => {
      if (!matrixSearch.trim()) return true;
      const q = matrixSearch.toLowerCase();
      return (
        (tr.trackerUrl || "").toLowerCase().includes(q) ||
        (tr.host || "").toLowerCase().includes(q) ||
        (tr.registeredTorrentNames || []).some((n) =>
          (n || "").toLowerCase().includes(q),
        )
      );
    });
  }, [matrixData?.trackers, matrixSearch]);

  const filteredDownloads = useMemo(() => {
    return unifiedItems.filter((item) => {
      if (downloadFilter === "public" && item.isPrivate) return false;
      if (downloadFilter === "private" && !item.isPrivate) return false;
      if (downloadFilter === "real" && item.sourceType !== "real_client")
        return false;
      if (downloadFilter === "leecharr" && item.sourceType !== "leecharr")
        return false;
      if (downloadSearch.trim()) {
        const q = downloadSearch.toLowerCase();
        if (
          !item.name.toLowerCase().includes(q) &&
          !item.infoHash.toLowerCase().includes(q)
        ) {
          return false;
        }
      }
      return true;
    });
  }, [unifiedItems, downloadFilter, downloadSearch]);

  const activeSelectedKey = selectedKey ?? filteredDownloads[0]?.key ?? "";
  const selectedItem = useMemo(() => {
    return filteredDownloads.find((i) => i.key === activeSelectedKey);
  }, [filteredDownloads, activeSelectedKey]);

  // Inspection hooks with live hash scraping
  const {
    data: torrentInspection,
    isLoading: torrentInspectLoading,
    refetch: refetchTorrentInspect,
  } = useInspectTorrentTrackers(
    selectedItem?.id ?? 0,
    Boolean(selectedItem?.id && selectedItem.id > 0),
  );

  const {
    data: hashInspection,
    isLoading: hashInspectLoading,
    refetch: refetchHashInspect,
  } = useInspectHashTrackers(
    selectedItem?.infoHash ?? "",
    selectedItem?.name ?? "",
    Boolean(!selectedItem?.id && selectedItem?.infoHash),
  );

  const inspection = selectedItem?.id ? torrentInspection : hashInspection;
  const inspectionLoading = selectedItem?.id
    ? torrentInspectLoading
    : hashInspectLoading;

  const scanTrackers = useScanTrackerBoostTrackers();
  const harvestDownloads = useHarvestDownloadTrackers();
  const harvestProwlarr = useHarvestProwlarrTrackers();
  const harvestFeeds = useHarvestFeedTrackers();
  const boostTorrent = useBoostTorrent();
  const boostHash = useBoostHash();
  const injectTracker = useInjectTrackerToTorrent();
  const boostAll = useBoostAllTorrents();
  const addTracker = useAddTrackerBoostTracker();
  const deleteTracker = useDeleteTrackerBoostTracker();

  const handleScanAll = () => {
    scanTrackers.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Probed ${res.testedCount} tracker endpoints`, "success");
      },
      onError: (err) => {
        showToast(`Failed to probe trackers: ${err.message}`, "error");
      },
    });
  };

  const handleHarvestDownloads = () => {
    harvestDownloads.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          `Harvested ${res.harvestedCount} new trackers from active downloads`,
          "success",
        );
      },
      onError: (err) => {
        showToast(`Failed to harvest from downloads: ${err.message}`, "error");
      },
    });
  };

  const handleHarvestProwlarr = () => {
    harvestProwlarr.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          `Harvested ${res.harvestedCount} trackers from Prowlarr`,
          "success",
        );
      },
      onError: (err) => {
        showToast(`Failed to harvest from Prowlarr: ${err.message}`, "error");
      },
    });
  };

  const handleHarvestFeeds = () => {
    harvestFeeds.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          `Harvested ${res.harvestedCount} trackers from public feeds`,
          "success",
        );
      },
      onError: (err) => {
        showToast(`Failed to harvest from feeds: ${err.message}`, "error");
      },
    });
  };

  const handleBoostItem = (item: UnifiedDownloadItem) => {
    if (item.id && item.id > 0) {
      boostTorrent.mutate(item.id, {
        onSuccess: (res) => {
          showToast(res.message, res.boosted ? "success" : "info");
        },
        onError: (err) => {
          showToast(`Failed to boost: ${err.message}`, "error");
        },
      });
    } else if (item.infoHash) {
      boostHash.mutate(
        { infoHash: item.infoHash, name: item.name },
        {
          onSuccess: (res) => {
            showToast(res.message, res.boosted ? "success" : "info");
          },
          onError: (err) => {
            showToast(`Failed to boost: ${err.message}`, "error");
          },
        },
      );
    }
  };

  const handleInjectSingle = (trackerUrl: string) => {
    if (!selectedItem) return;

    injectTracker.mutate(
      {
        torrentId: selectedItem.id,
        infoHash: selectedItem.infoHash,
        trackerUrl,
      },
      {
        onSuccess: (res) => {
          showToast(res.message, "success");
        },
        onError: (err) => {
          showToast(`Failed to inject tracker: ${err.message}`, "error");
        },
      },
    );
  };

  const handleBoostAll = () => {
    boostAll.mutate(undefined, {
      onSuccess: (resList) => {
        const totalAdded = resList.reduce(
          (sum, r) => sum + r.addedTrackersCount,
          0,
        );
        const totalSeeds = resList.reduce(
          (sum, r) => sum + r.totalSeedersFound,
          0,
        );
        showToast(
          `Boosted ${resList.length} swarms: injected ${totalAdded} verified trackers (+${totalSeeds} seeds discovered)`,
          "success",
        );
      },
      onError: (err) => {
        showToast(`Failed to boost downloads: ${err.message}`, "error");
      },
    });
  };

  const handleToggleSetting = (key: keyof TrackerBoostSettings) => {
    if (!settings) return;
    const updated = { ...settings, [key]: !settings[key] };
    updateSettings.mutate(updated, {
      onSuccess: () => {
        showToast("TrackerBoost settings updated", "success");
      },
    });
  };

  const handleAddCustomTracker = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTrackerUrl.trim()) return;
    addTracker.mutate(
      { url: newTrackerUrl.trim() },
      {
        onSuccess: () => {
          showToast("Custom tracker added successfully", "success");
          setNewTrackerUrl("");
          setIsAddingTracker(false);
        },
        onError: (err) => {
          showToast(`Failed to add tracker: ${err.message}`, "error");
        },
      },
    );
  };

  const handleExportTrackers = () => {
    if (!trackers || trackers.length === 0) {
      showToast("No trackers available to export", "info");
      return;
    }
    const uniqueUrls = Array.from(new Set(trackers.map((t) => t.url))).join(
      "\n",
    );
    const blob = new Blob([uniqueUrls], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `leecharr-trackers-${new Date().toISOString().slice(0, 10)}.txt`;
    link.click();
    URL.revokeObjectURL(url);
    showToast(
      `Exported ${trackers.length} tracker endpoints to .txt!`,
      "success",
    );
  };

  const handleCopyAllTrackers = () => {
    if (!trackers || trackers.length === 0) return;
    const uniqueUrls = Array.from(new Set(trackers.map((t) => t.url))).join(
      "\n",
    );
    navigator.clipboard.writeText(uniqueUrls);
    showToast(`Copied ${trackers.length} tracker URLs to clipboard!`, "info");
  };

  const handleBulkImportTrackers = async () => {
    if (!bulkImportText.trim()) return;
    const lines = bulkImportText
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter(
        (l) =>
          l.startsWith("http://") ||
          l.startsWith("https://") ||
          l.startsWith("udp://"),
      );

    if (lines.length === 0) {
      showToast(
        "No valid http://, https://, or udp:// tracker URLs found.",
        "error",
      );
      return;
    }

    setIsBulkImporting(true);
    let imported = 0;
    for (const url of lines) {
      try {
        await addTracker.mutateAsync({ url });
        imported++;
      } catch {
        // Continue on duplicates/errors
      }
    }
    setIsBulkImporting(false);
    setShowBulkImportModal(false);
    setBulkImportText("");
    showToast(
      `Successfully processed ${lines.length} trackers (${imported} added)!`,
      "success",
    );
  };

  const filteredTrackers = useMemo(() => {
    return (trackers ?? []).filter((t) => {
      if (trackerSearch.trim()) {
        const q = trackerSearch.toLowerCase();
        if (
          !t.url.toLowerCase().includes(q) &&
          !t.host.toLowerCase().includes(q) &&
          !t.sourceName.toLowerCase().includes(q)
        ) {
          return false;
        }
      }
      if (sourceFilter !== "all") {
        if (
          sourceFilter === "active" &&
          t.source !== "ActiveTorrent" &&
          t.source !== 4
        )
          return false;
        if (
          sourceFilter === "prowlarr" &&
          t.source !== "Prowlarr" &&
          t.source !== 1
        )
          return false;
        if (
          sourceFilter === "feeds" &&
          t.source !== "PublicList" &&
          t.source !== 0
        )
          return false;
        if (
          sourceFilter === "manual" &&
          t.source !== "Manual" &&
          t.source !== 3
        )
          return false;
      }
      if (healthFilter !== "all") {
        if (healthFilter === "alive" && t.status !== "Alive" && t.status !== 1)
          return false;
        if (healthFilter === "slow" && t.status !== "Slow" && t.status !== 2)
          return false;
        if (
          healthFilter === "offline" &&
          t.status !== "Offline" &&
          t.status !== 3
        )
          return false;
        if (
          healthFilter === "untested" &&
          t.status !== "Untested" &&
          t.status !== 0
        )
          return false;
      }
      return true;
    });
  }, [trackers, trackerSearch, sourceFilter, healthFilter]);

  const filteredLogs = useMemo(() => {
    return (boostLogs ?? []).filter((l) => {
      if (!logSearch.trim()) return true;
      const q = logSearch.toLowerCase();
      return (
        (l.message && l.message.toLowerCase().includes(q)) ||
        (l.trackerUrl && l.trackerUrl.toLowerCase().includes(q)) ||
        (l.infoHash && l.infoHash.toLowerCase().includes(q)) ||
        (l.category && l.category.toLowerCase().includes(q)) ||
        (l.level && l.level.toLowerCase().includes(q))
      );
    });
  }, [boostLogs, logSearch]);

  const enabledClientsCount = useMemo(() => {
    return (downloadClients ?? []).filter((c) => c.enable).length;
  }, [downloadClients]);

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
            Scrapes live tracker swarms by info_hash to discover and inject
            verified seeders/peers into Leecharr and download clients
          </div>
        </div>
      </div>

      {/* Global Metric Cards */}
      <div
        className="stats-grid"
        style={{ marginBottom: "1rem", flexShrink: 0 }}
      >
        <div className="stat-card">
          <div className="stat-value">
            {status?.totalTrackersMonitored ?? 0}
          </div>
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
          📜 Activity Logs{" "}
          {boostLogs && boostLogs.length > 0 ? `(${boostLogs.length})` : ""}
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

      {/* TAB 1: SWARM BOOSTER & TORRENT DETECTOR */}
      {activeTab === "booster" && (
        <div
          style={{
            flex: "1 1 auto",
            display: "flex",
            flexDirection: "column",
            minHeight: 0,
            marginBottom: "0.5rem",
          }}
        >
          {/* Action Toolbar */}
          <div
            className="card"
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "1rem",
              marginBottom: "1.25rem",
              padding: "0.75rem 1rem",
              borderRadius: "8px",
            }}
          >
            <div
              style={{
                display: "flex",
                gap: "0.6rem",
                alignItems: "center",
                flexWrap: "wrap",
              }}
            >
              <button
                className="btn btn-primary"
                onClick={handleBoostAll}
                disabled={boostAll.isPending || filteredDownloads.length === 0}
                title="Scrape candidate trackers and inject only verified positive matches across all active downloads"
                style={{ padding: "0.45rem 1rem", fontWeight: 600 }}
              >
                {boostAll.isPending
                  ? "⚡ Scraping & Boosting..."
                  : "⚡ Boost All Downloads (Verified Only)"}
              </button>

              <button
                className="btn btn-action"
                onClick={handleHarvestDownloads}
                disabled={harvestDownloads.isPending}
                title="Extract and discover tracker URLs from active download swarms in Leecharr and download clients"
              >
                {harvestDownloads.isPending
                  ? "🔄 Harvesting..."
                  : "🔄 Harvest from Live Swarms"}
              </button>

              <button
                className="btn btn-action"
                onClick={handleScanAll}
                disabled={scanTrackers.isPending}
                title="Ping and probe health across all monitored tracker endpoints"
              >
                {scanTrackers.isPending
                  ? "📡 Probing..."
                  : "📡 Probe All Trackers"}
              </button>
            </div>

            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                flexWrap: "wrap",
              }}
            >
              <select
                className="form-control"
                style={{
                  width: "150px",
                  padding: "0.35rem 0.6rem",
                  fontSize: "0.82rem",
                }}
                value={downloadFilter}
                onChange={(e) => setDownloadFilter(e.target.value as any)}
              >
                <option value="all">All Swarms ({unifiedItems.length})</option>
                <option value="public">
                  Public ({unifiedItems.filter((i) => !i.isPrivate).length})
                </option>
                <option value="private">
                  Private ({unifiedItems.filter((i) => i.isPrivate).length})
                </option>
              </select>
              <input
                type="text"
                className="form-control"
                style={{
                  width: "200px",
                  padding: "0.35rem 0.6rem",
                  fontSize: "0.82rem",
                }}
                placeholder="Search downloads..."
                value={downloadSearch}
                onChange={(e) => setDownloadSearch(e.target.value)}
              />
            </div>
          </div>

          {/* Master-Detail Split: Left = Downloads List, Right = Live Tracker Scraper */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "360px 1fr",
              gap: "1.25rem",
              alignItems: "stretch",
              flex: "1 1 auto",
              minHeight: 0,
            }}
          >
            {/* Left: Downloads List */}
            <div
              className="card"
              style={{
                padding: "0.85rem",
                display: "flex",
                flexDirection: "column",
                height: "100%",
                minHeight: 0,
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: "0.75rem",
                  paddingBottom: "0.5rem",
                  borderBottom: "1px solid var(--border-color)",
                  flexShrink: 0,
                }}
              >
                <span style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                  Swarms ({filteredDownloads.length})
                </span>
                <span
                  style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}
                >
                  Select to inspect swarm
                </span>
              </div>

              {torrentsLoading ? (
                <div
                  style={{
                    padding: "2rem",
                    textAlign: "center",
                    color: "var(--text-muted)",
                  }}
                >
                  Loading downloads...
                </div>
              ) : filteredDownloads.length === 0 ? (
                <div
                  style={{
                    padding: "2rem",
                    textAlign: "center",
                    color: "var(--text-muted)",
                  }}
                >
                  No downloads found matching filter.
                </div>
              ) : (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.5rem",
                    flex: "1 1 0",
                    minHeight: 0,
                    overflowY: "auto",
                    paddingRight: "0.25rem",
                  }}
                >
                  {filteredDownloads.map((item) => {
                    const isSelected = item.key === activeSelectedKey;
                    return (
                      <div
                        key={item.key}
                        onClick={() => setSelectedKey(item.key)}
                        style={{
                          padding: "0.75rem",
                          borderRadius: "6px",
                          cursor: "pointer",
                          backgroundColor: isSelected
                            ? "var(--accent-glow, rgba(56, 189, 248, 0.12))"
                            : "var(--bg-secondary, rgba(255,255,255,0.02))",
                          border: isSelected
                            ? "1px solid var(--accent, #38bdf8)"
                            : "1px solid var(--border-color)",
                          transition: "all 0.15s ease",
                        }}
                      >
                        <div
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "flex-start",
                            gap: "0.5rem",
                            marginBottom: "0.35rem",
                          }}
                        >
                          <span
                            style={{
                              fontWeight: 600,
                              fontSize: "0.85rem",
                              wordBreak: "break-word",
                            }}
                          >
                            {item.name}
                          </span>
                          {item.isPrivate ? (
                            <span
                              className="badge badge-secondary"
                              style={{
                                fontSize: "0.7rem",
                                whiteSpace: "nowrap",
                              }}
                              title="Private tracker swarm"
                            >
                              🔒 Private
                            </span>
                          ) : (
                            <span
                              className="badge badge-success"
                              style={{
                                fontSize: "0.7rem",
                                whiteSpace: "nowrap",
                              }}
                              title="Public swarm boost eligible"
                            >
                              🌐 Public
                            </span>
                          )}
                        </div>
                        <div
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "center",
                            fontSize: "0.75rem",
                            color: "var(--text-muted)",
                          }}
                        >
                          <span>
                            {formatBytes(item.totalSize)} • Ratio:{" "}
                            {formatRatio(item.ratio)}
                          </span>
                          <span
                            style={{
                              color:
                                item.seeders > 0 ? "var(--success)" : "inherit",
                            }}
                          >
                            {item.seeders} Seeds
                          </span>
                        </div>
                        <div
                          style={{
                            marginTop: "0.5rem",
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "center",
                          }}
                        >
                          <span
                            className="badge badge-secondary"
                            style={{ fontSize: "0.7rem" }}
                          >
                            {item.clientName}
                          </span>
                          {!item.isPrivate ? (
                            <button
                              className="btn btn-sm btn-primary"
                              style={{
                                padding: "0.2rem 0.5rem",
                                fontSize: "0.75rem",
                              }}
                              onClick={(e) => {
                                e.stopPropagation();
                                handleBoostItem(item);
                              }}
                              title="Scrape and inject verified trackers"
                            >
                              ⚡ Enrich
                            </button>
                          ) : (
                            <span
                              style={{
                                fontSize: "0.75rem",
                                color: "var(--text-dim)",
                              }}
                            >
                              Protected
                            </span>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            {/* Right: Live Scrape Inspector Pane */}
            <div
              className="card"
              style={{
                padding: "1.25rem",
                display: "flex",
                flexDirection: "column",
                height: "100%",
                minHeight: 0,
                overflow: "hidden",
              }}
            >
              {selectedItem ? (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    height: "100%",
                    minHeight: 0,
                    flex: "1 1 auto",
                    overflow: "hidden",
                  }}
                >
                  {/* Selected Item Banner */}
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      flexWrap: "wrap",
                      gap: "1rem",
                      marginBottom: "1rem",
                      paddingBottom: "0.75rem",
                      borderBottom: "1px solid var(--border-color)",
                      flexShrink: 0,
                    }}
                  >
                    <div>
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.5rem",
                          marginBottom: "0.25rem",
                          flexWrap: "wrap",
                        }}
                      >
                        <h2 style={{ fontSize: "1.1rem", margin: 0 }}>
                          {selectedItem.name}
                        </h2>
                        {selectedItem.isPrivate ? (
                          <span
                            className="badge badge-secondary"
                            style={{ fontSize: "0.75rem" }}
                          >
                            🔒 Private Swarm
                          </span>
                        ) : (
                          <span
                            className="badge badge-success"
                            style={{ fontSize: "0.75rem" }}
                          >
                            🌐 Public Swarm
                          </span>
                        )}
                      </div>
                      <div
                        style={{
                          fontSize: "0.8rem",
                          color: "var(--text-muted)",
                          fontFamily: "monospace",
                        }}
                      >
                        InfoHash: {selectedItem.infoHash}
                      </div>
                    </div>
                    <div style={{ display: "flex", gap: "0.5rem" }}>
                      <button
                        className="btn btn-action"
                        style={{ fontSize: "0.85rem" }}
                        onClick={() =>
                          selectedItem.id
                            ? refetchTorrentInspect()
                            : refetchHashInspect()
                        }
                        title="Re-scrape candidate trackers for this info_hash"
                      >
                        🔄 Re-Scrape Swarm
                      </button>
                      {!selectedItem.isPrivate && (
                        <button
                          className="btn btn-primary"
                          style={{ fontSize: "0.85rem", fontWeight: 600 }}
                          onClick={() => handleBoostItem(selectedItem)}
                          title="Inject verified candidate trackers into this torrent"
                        >
                          ⚡ Boost Torrent (Inject Verified)
                        </button>
                      )}
                    </div>
                  </div>

                  {selectedItem.isPrivate && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        marginBottom: "1rem",
                        borderRadius: "6px",
                        backgroundColor: "rgba(230, 126, 34, 0.12)",
                        border: "1px solid rgba(230, 126, 34, 0.35)",
                        color: "var(--text-primary)",
                        fontSize: "0.85rem",
                        display: "flex",
                        alignItems: "center",
                        gap: "0.75rem",
                        flexShrink: 0,
                      }}
                    >
                      <span style={{ fontSize: "1.25rem" }}>🔒</span>
                      <div>
                        <strong>Private Tracker Swarm:</strong> Cross-swarm
                        public tracker injection is protected and disabled to
                        comply with BitTorrent private tracker rules (BEP 27).
                        Attached private trackers and health metrics are
                        displayed below.
                      </div>
                    </div>
                  )}

                  {/* Scrape Results Overview */}
                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns:
                        "repeat(auto-fit, minmax(130px, 1fr))",
                      gap: "0.75rem",
                      marginBottom: "1rem",
                      flexShrink: 0,
                    }}
                  >
                    <div className="stat-card" style={{ padding: "0.75rem" }}>
                      <div
                        className="stat-value"
                        style={{ fontSize: "1.25rem" }}
                      >
                        {inspection?.attachedTrackersCount ?? 0}
                      </div>
                      <div
                        className="stat-label"
                        style={{ fontSize: "0.75rem" }}
                      >
                        Attached Trackers
                      </div>
                    </div>
                    <div className="stat-card" style={{ padding: "0.75rem" }}>
                      <div
                        className="stat-value"
                        style={{ fontSize: "1.25rem", color: "var(--success)" }}
                      >
                        {inspection?.verifiedTrackersCount ?? 0}
                      </div>
                      <div
                        className="stat-label"
                        style={{ fontSize: "0.75rem" }}
                      >
                        Verified Candidates
                      </div>
                    </div>
                    <div className="stat-card" style={{ padding: "0.75rem" }}>
                      <div
                        className="stat-value"
                        style={{ fontSize: "1.25rem" }}
                      >
                        {inspection?.totalTrackersChecked ?? 0}
                      </div>
                      <div
                        className="stat-label"
                        style={{ fontSize: "0.75rem" }}
                      >
                        Total Checked
                      </div>
                    </div>
                  </div>

                  {/* Candidate Trackers Table */}
                  {inspectionLoading ? (
                    <div
                      style={{
                        padding: "3rem",
                        textAlign: "center",
                        color: "var(--text-muted)",
                      }}
                    >
                      Scraping candidate trackers for hash{" "}
                      {selectedItem.infoHash.slice(0, 8)}...
                    </div>
                  ) : (
                    <div
                      className="torrent-table-wrapper"
                      style={{
                        borderRadius: "6px",
                        border: "1px solid var(--border)",
                        flex: "1 1 auto",
                        minHeight: 0,
                        overflowY: "auto",
                        backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
                      }}
                    >
                      <table
                        className="torrent-table"
                        style={{ width: "100%" }}
                      >
                        <thead
                          style={{
                            position: "sticky",
                            top: 0,
                            zIndex: 2,
                            backgroundColor: "var(--bg-secondary)",
                          }}
                        >
                          <tr>
                            <th
                              className="torrent-table-th"
                              style={{ width: "35%" }}
                            >
                              Tracker URL
                            </th>
                            <th
                              className="torrent-table-th"
                              style={{ width: "10%" }}
                            >
                              Protocol
                            </th>
                            <th
                              className="torrent-table-th"
                              style={{ width: "10%" }}
                            >
                              Latency
                            </th>
                            <th
                              className="torrent-table-th"
                              style={{ width: "25%" }}
                            >
                              Status / Detection
                            </th>
                            <th
                              className="torrent-table-th"
                              style={{ width: "15%" }}
                            >
                              Peers
                            </th>
                            <th
                              className="torrent-table-th"
                              style={{ textAlign: "right" }}
                            >
                              Action
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {(inspection?.detections ?? []).map((det) => (
                            <tr
                              key={det.trackerId || det.trackerUrl}
                              className="torrent-table-row"
                              style={{
                                opacity:
                                  det.healthStatus === "Offline" ||
                                  det.healthStatus === 3
                                    ? 0.6
                                    : 1,
                              }}
                            >
                              <td
                                style={{
                                  maxWidth: "280px",
                                  wordBreak: "break-all",
                                  fontFamily: "monospace",
                                  fontSize: "0.8rem",
                                }}
                              >
                                <div
                                  style={{
                                    display: "inline-flex",
                                    alignItems: "center",
                                    gap: "0.45rem",
                                  }}
                                >
                                  <TrackerFavicon
                                    urlOrHost={det.trackerUrl}
                                    size={15}
                                  />
                                  <span>{det.trackerUrl}</span>
                                </div>
                              </td>
                              <td>
                                <span
                                  className="badge badge-secondary"
                                  style={{ fontSize: "0.75rem" }}
                                >
                                  {det.protocol}
                                </span>
                              </td>
                              <td style={{ fontFamily: "monospace" }}>
                                {det.latencyMs > 0 ? `${det.latencyMs}ms` : "-"}
                              </td>
                              <td>
                                {det.isAttached ? (
                                  <span
                                    className="badge badge-primary"
                                    style={{ fontSize: "0.75rem" }}
                                  >
                                    Attached
                                  </span>
                                ) : det.isVerified ? (
                                  <span
                                    className="badge badge-success"
                                    style={{ fontSize: "0.75rem" }}
                                  >
                                    ✓ Verified Match
                                  </span>
                                ) : (
                                  <span
                                    className="badge badge-secondary"
                                    style={{ fontSize: "0.75rem" }}
                                  >
                                    {det.detectionStatus}
                                  </span>
                                )}
                              </td>
                              <td>
                                <span
                                  style={{
                                    color:
                                      det.seeders > 0
                                        ? "var(--success)"
                                        : "inherit",
                                    fontWeight: 600,
                                  }}
                                >
                                  {det.seeders} seeds
                                </span>{" "}
                                /{" "}
                                <span
                                  style={{
                                    color:
                                      det.leechers > 0
                                        ? "var(--accent)"
                                        : "inherit",
                                  }}
                                >
                                  {det.leechers} leeches
                                </span>
                              </td>
                              <td
                                style={{
                                  textAlign: "right",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {det.isAttached ? (
                                  <span
                                    className="badge badge-primary"
                                    style={{
                                      fontSize: "0.72rem",
                                      padding: "0.25rem 0.5rem",
                                    }}
                                  >
                                    ✓ Attached
                                  </span>
                                ) : selectedItem.isPrivate ? (
                                  <span
                                    className="badge badge-secondary"
                                    title="BEP 27: Public tracker injection is disabled for private torrents"
                                    style={{
                                      fontSize: "0.72rem",
                                      padding: "0.25rem 0.5rem",
                                      opacity: 0.8,
                                    }}
                                  >
                                    🔒 Private Guard
                                  </span>
                                ) : det.isVerified ? (
                                  <button
                                    className="btn btn-sm btn-primary"
                                    onClick={() =>
                                      handleInjectSingle(det.trackerUrl)
                                    }
                                    title="Inject this verified tracker into the torrent"
                                  >
                                    ⚡ Inject
                                  </button>
                                ) : det.healthStatus === "Offline" ||
                                  det.healthStatus === 3 ? (
                                  <span
                                    style={{
                                      color: "var(--text-dim)",
                                      fontSize: "0.75rem",
                                    }}
                                  >
                                    Offline
                                  </span>
                                ) : (
                                  <span
                                    style={{
                                      color: "var(--text-dim)",
                                      fontSize: "0.75rem",
                                    }}
                                  >
                                    —
                                  </span>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              ) : (
                <div
                  style={{
                    padding: "4rem",
                    textAlign: "center",
                    color: "var(--text-muted)",
                  }}
                >
                  Select a download from the left list to inspect live tracker
                  scrape results.
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* TAB 2: SWARM CROSS-MATRIX */}
      {activeTab === "matrix" && (
        <div
          className="card"
          style={{
            padding: "1.25rem",
            flex: "1 1 auto",
            display: "flex",
            flexDirection: "column",
            minHeight: 0,
            marginBottom: "0.5rem",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "1rem",
              marginBottom: "1.25rem",
            }}
          >
            <div>
              <h2 style={{ fontSize: "1.1rem", margin: "0 0 0.25rem 0" }}>
                Swarm Cross-Matrix Explorer
              </h2>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                Bi-directional mapping between library torrents and verified
                BitTorrent tracker endpoints
              </div>
            </div>

            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                flexWrap: "wrap",
              }}
            >
              <input
                type="text"
                className="form-control"
                style={{
                  width: "240px",
                  padding: "0.35rem 0.75rem",
                  fontSize: "0.85rem",
                }}
                placeholder="Search torrents or trackers..."
                value={matrixSearch}
                onChange={(e) => setMatrixSearch(e.target.value)}
              />

              <div className="view-toggle">
                <button
                  className={`view-toggle-btn ${matrixViewMode === "by_torrent" ? "active" : ""}`}
                  onClick={() => setMatrixViewMode("by_torrent")}
                  title="Group swarms by torrent download"
                >
                  Torrents → Trackers
                </button>
                <button
                  className={`view-toggle-btn ${matrixViewMode === "by_tracker" ? "active" : ""}`}
                  onClick={() => setMatrixViewMode("by_tracker")}
                  title="Group swarms by tracker endpoint"
                >
                  Trackers → Torrents
                </button>
              </div>

              <div className="view-toggle">
                <button
                  className={`view-toggle-btn ${matrixLayoutMode === "grid" ? "active" : ""}`}
                  onClick={() => setMatrixLayoutMode("grid")}
                  title="Poster Card Grid View"
                >
                  🎬 Posters
                </button>
                <button
                  className={`view-toggle-btn ${matrixLayoutMode === "table" ? "active" : ""}`}
                  onClick={() => setMatrixLayoutMode("table")}
                  title="Detailed Table / List View"
                >
                  📑 Table
                </button>
              </div>
            </div>
          </div>

          {matrixLoading ? (
            <div
              style={{
                padding: "3rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              Building swarm cross-matrix...
            </div>
          ) : matrixViewMode === "by_torrent" ? (
            matrixLayoutMode === "grid" ? (
              /* TORRENTS GRID VIEW (POSTER CARDS) */
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fill, minmax(260px, 1fr))",
                  alignContent: "start",
                  gap: "1.25rem",
                  flex: "1 1 auto",
                  minHeight: 0,
                  overflowY: "auto",
                  paddingRight: "0.25rem",
                }}
              >
                {filteredMatrixTorrents.map((t) => {
                  const meta = torrentMetaMap.get(
                    (t.infoHash || "").toLowerCase(),
                  );
                  const displayTitle = meta?.mediaTitle || t.torrentName;
                  const hasPoster = Boolean(meta?.posterUrl);

                  return (
                    <div
                      key={t.torrentId || t.infoHash}
                      className="card"
                      style={{
                        padding: 0,
                        overflow: "hidden",
                        display: "flex",
                        flexDirection: "column",
                        height: "auto",
                        borderRadius: "8px",
                        border: "1px solid rgba(255, 255, 255, 0.08)",
                        backgroundColor: "var(--bg-secondary)",
                        boxShadow: "0 4px 14px rgba(0, 0, 0, 0.35)",
                        transition:
                          "transform 0.18s ease, box-shadow 0.18s ease",
                      }}
                    >
                      {/* Poster artwork container */}
                      <div
                        style={{
                          position: "relative",
                          width: "100%",
                          paddingTop: "140%", // 2:3 ratio matching TrackerServer
                          backgroundColor: "#141414",
                          overflow: "hidden",
                        }}
                      >
                        {hasPoster ? (
                          <img
                            src={meta?.posterUrl || ""}
                            alt={displayTitle}
                            style={{
                              position: "absolute",
                              top: 0,
                              left: 0,
                              width: "100%",
                              height: "100%",
                              objectFit: "cover",
                            }}
                            loading="lazy"
                          />
                        ) : (
                          <div
                            style={{
                              position: "absolute",
                              top: 0,
                              left: 0,
                              width: "100%",
                              height: "100%",
                              display: "flex",
                              flexDirection: "column",
                              alignItems: "center",
                              justifyContent: "center",
                              padding: "1rem",
                              textAlign: "center",
                              background:
                                "linear-gradient(180deg, #2a2620 0%, #151412 100%)",
                            }}
                          >
                            <span
                              style={{
                                fontSize: "2.5rem",
                                marginBottom: "0.5rem",
                              }}
                            >
                              {meta?.source === "Radarr"
                                ? "🎬"
                                : meta?.source === "Sonarr"
                                  ? "📺"
                                  : meta?.source === "Lidarr"
                                    ? "🎵"
                                    : "📦"}
                            </span>
                            <span
                              style={{
                                fontSize: "0.82rem",
                                fontWeight: 600,
                                color: "var(--text-secondary)",
                                wordBreak: "break-word",
                              }}
                            >
                              {displayTitle}
                            </span>
                          </div>
                        )}

                        {/* Dark Gradient Overlay */}
                        <div
                          style={{
                            position: "absolute",
                            bottom: 0,
                            left: 0,
                            right: 0,
                            height: "65%",
                            background:
                              "linear-gradient(to top, rgba(15,15,15,0.95) 0%, rgba(15,15,15,0.6) 50%, transparent 100%)",
                            pointerEvents: "none",
                          }}
                        />

                        {/* Top-Left Arr Source Badge */}
                        {meta?.source && (
                          <span
                            className="badge badge-primary"
                            style={{
                              position: "absolute",
                              top: "8px",
                              left: "8px",
                              fontSize: "0.7rem",
                              padding: "0.2rem 0.5rem",
                              borderRadius: "4px",
                              backdropFilter: "blur(4px)",
                            }}
                          >
                            {meta.source}
                          </span>
                        )}

                        {/* Top-Right Privacy Badge */}
                        <span
                          className={`badge ${t.isPrivate ? "badge-secondary" : "badge-success"}`}
                          style={{
                            position: "absolute",
                            top: "8px",
                            right: "8px",
                            fontSize: "0.7rem",
                            padding: "0.2rem 0.5rem",
                            borderRadius: "4px",
                            backdropFilter: "blur(4px)",
                          }}
                        >
                          {t.isPrivate ? "🔒 Private" : "🌐 Public"}
                        </span>

                        {/* Bottom title & year overlay */}
                        <div
                          style={{
                            position: "absolute",
                            bottom: "8px",
                            left: "10px",
                            right: "10px",
                            color: "#fff",
                          }}
                        >
                          <div
                            style={{
                              fontWeight: 700,
                              fontSize: "0.92rem",
                              lineHeight: 1.25,
                              textShadow: "0 2px 4px rgba(0,0,0,0.8)",
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              display: "-webkit-box",
                              WebkitLineClamp: 2,
                              WebkitBoxOrient: "vertical",
                            }}
                            title={displayTitle}
                          >
                            {displayTitle}
                          </div>
                          {meta?.year && (
                            <span
                              style={{
                                fontSize: "0.75rem",
                                color: "var(--accent, #c8a84e)",
                                fontWeight: 600,
                              }}
                            >
                              {meta.year}
                            </span>
                          )}
                        </div>
                      </div>

                      {/* Card Info & Trackers Body */}
                      <div
                        style={{
                          padding: "0.85rem",
                          display: "flex",
                          flexDirection: "column",
                          flex: 1,
                          gap: "0.6rem",
                        }}
                      >
                        <div
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "center",
                            fontSize: "0.78rem",
                          }}
                        >
                          <span
                            style={{
                              fontFamily: "monospace",
                              color: "var(--text-muted)",
                              fontSize: "0.75rem",
                            }}
                            title={t.infoHash}
                          >
                            {t.infoHash ? `${t.infoHash.slice(0, 10)}...` : ""}
                          </span>
                          <div style={{ display: "flex", gap: "0.35rem" }}>
                            <span
                              className="badge badge-primary"
                              style={{ fontSize: "0.7rem" }}
                            >
                              {t.attachedTrackersCount} Attached
                            </span>
                            {t.verifiedTrackersCount > 0 && (
                              <span
                                className="badge badge-success"
                                style={{ fontSize: "0.7rem" }}
                              >
                                {t.verifiedTrackersCount} Verified
                              </span>
                            )}
                          </div>
                        </div>

                        {/* Trackers list chips */}
                        <div
                          style={{
                            display: "flex",
                            flexWrap: "wrap",
                            gap: "0.35rem",
                            maxHeight: "130px",
                            overflowY: "auto",
                          }}
                        >
                          {t.trackers.map((tr, idx) => (
                            <span
                              key={tr.trackerId || idx}
                              className={`badge ${tr.isAttached ? "badge-primary" : "badge-success"}`}
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.35rem",
                                padding: "0.25rem 0.45rem",
                                fontSize: "0.72rem",
                                fontFamily: "monospace",
                              }}
                            >
                              <TrackerFavicon
                                urlOrHost={tr.trackerHost || tr.trackerUrl}
                                size={13}
                              />
                              <span>{tr.trackerHost || tr.trackerUrl}</span>
                              {(tr.seeders > 0 || tr.leechers > 0) && (
                                <span style={{ opacity: 0.85 }}>
                                  ({tr.seeders}s/{tr.leechers}l)
                                </span>
                              )}
                            </span>
                          ))}
                          {t.trackers.length === 0 && (
                            <span
                              style={{
                                fontSize: "0.78rem",
                                color: "var(--text-muted)",
                              }}
                            >
                              No positive tracker scrapes found yet.
                            </span>
                          )}
                        </div>

                        <div
                          style={{
                            marginTop: "auto",
                            paddingTop: "0.5rem",
                            borderTop: "1px solid var(--border-light)",
                          }}
                        >
                          <button
                            className="btn btn-sm btn-outline"
                            style={{
                              width: "100%",
                              fontSize: "0.78rem",
                              padding: "0.3rem 0",
                            }}
                            onClick={() => {
                              const targetKey = unifiedItems.find(
                                (u) =>
                                  u.infoHash.toLowerCase() ===
                                  t.infoHash.toLowerCase(),
                              )?.key;
                              if (targetKey) setSelectedKey(targetKey);
                              setActiveTab("booster");
                            }}
                          >
                            ⚡ Inspect Swarm
                          </button>
                        </div>
                      </div>
                    </div>
                  );
                })}
                {filteredMatrixTorrents.length === 0 && (
                  <div
                    style={{
                      gridColumn: "1 / -1",
                      padding: "3rem",
                      textAlign: "center",
                      color: "var(--text-muted)",
                    }}
                  >
                    No library torrents match the search query.
                  </div>
                )}
              </div>
            ) : (
              /* TORRENTS TABLE VIEW */
              <div
                className="torrent-table-wrapper"
                style={{
                  borderRadius: "6px",
                  border: "1px solid var(--border)",
                  flex: "1 1 auto",
                  minHeight: 0,
                  overflowY: "auto",
                  backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
                }}
              >
                <table
                  className="torrent-table"
                  style={{ width: "100%", fontSize: "0.85rem" }}
                >
                  <thead
                    style={{
                      position: "sticky",
                      top: 0,
                      zIndex: 2,
                      backgroundColor: "var(--bg-secondary)",
                    }}
                  >
                    <tr>
                      <th className="torrent-table-th" style={{ width: "35%" }}>
                        Media / Torrent
                      </th>
                      <th className="torrent-table-th" style={{ width: "12%" }}>
                        Privacy & Hash
                      </th>
                      <th className="torrent-table-th" style={{ width: "40%" }}>
                        Attached & Verified Trackers
                      </th>
                      <th
                        className="torrent-table-th"
                        style={{ width: "13%", textAlign: "right" }}
                      >
                        Action
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredMatrixTorrents.map((t) => {
                      const meta = torrentMetaMap.get(
                        (t.infoHash || "").toLowerCase(),
                      );
                      const displayTitle = meta?.mediaTitle || t.torrentName;
                      return (
                        <tr
                          key={t.torrentId || t.infoHash}
                          className="torrent-table-row"
                        >
                          <td>
                            <div
                              style={{
                                display: "flex",
                                alignItems: "center",
                                gap: "0.75rem",
                              }}
                            >
                              {meta?.posterUrl ? (
                                <img
                                  src={meta.posterUrl}
                                  alt={displayTitle}
                                  style={{
                                    width: "36px",
                                    height: "50px",
                                    borderRadius: "4px",
                                    objectFit: "cover",
                                    flexShrink: 0,
                                  }}
                                  loading="lazy"
                                />
                              ) : (
                                <div
                                  style={{
                                    width: "36px",
                                    height: "50px",
                                    borderRadius: "4px",
                                    backgroundColor: "var(--bg-secondary)",
                                    display: "flex",
                                    alignItems: "center",
                                    justifyContent: "center",
                                    fontSize: "1.2rem",
                                    flexShrink: 0,
                                  }}
                                >
                                  {meta?.source === "Radarr"
                                    ? "🎬"
                                    : meta?.source === "Sonarr"
                                      ? "📺"
                                      : "📦"}
                                </div>
                              )}
                              <div style={{ minWidth: 0 }}>
                                <div
                                  style={{
                                    fontWeight: 600,
                                    color: "var(--text-primary)",
                                    overflow: "hidden",
                                    textOverflow: "ellipsis",
                                    whiteSpace: "nowrap",
                                  }}
                                >
                                  {displayTitle}
                                </div>
                                <div
                                  style={{
                                    fontSize: "0.75rem",
                                    color: "var(--text-muted)",
                                    display: "flex",
                                    alignItems: "center",
                                    gap: "0.4rem",
                                    marginTop: "0.15rem",
                                  }}
                                >
                                  {meta?.source && (
                                    <span
                                      className="badge badge-primary"
                                      style={{
                                        fontSize: "0.65rem",
                                        padding: "0.1rem 0.35rem",
                                      }}
                                    >
                                      {meta.source}
                                    </span>
                                  )}
                                  {meta?.year && <span>({meta.year})</span>}
                                  <span
                                    style={{
                                      overflow: "hidden",
                                      textOverflow: "ellipsis",
                                      whiteSpace: "nowrap",
                                    }}
                                  >
                                    {t.torrentName}
                                  </span>
                                </div>
                              </div>
                            </div>
                          </td>
                          <td>
                            <div>
                              <span
                                className={`badge ${t.isPrivate ? "badge-secondary" : "badge-success"}`}
                                style={{ fontSize: "0.72rem" }}
                              >
                                {t.isPrivate ? "🔒 Private" : "🌐 Public"}
                              </span>
                              <div
                                style={{
                                  fontFamily: "monospace",
                                  fontSize: "0.72rem",
                                  color: "var(--text-muted)",
                                  marginTop: "0.25rem",
                                }}
                              >
                                {t.infoHash
                                  ? `${t.infoHash.slice(0, 12)}...`
                                  : ""}
                              </div>
                            </div>
                          </td>
                          <td>
                            <div
                              style={{
                                display: "flex",
                                flexWrap: "wrap",
                                gap: "0.35rem",
                              }}
                            >
                              {t.trackers.map((tr, idx) => (
                                <span
                                  key={tr.trackerId || idx}
                                  className={`badge ${tr.isAttached ? "badge-primary" : "badge-success"}`}
                                  style={{
                                    display: "inline-flex",
                                    alignItems: "center",
                                    gap: "0.35rem",
                                    padding: "0.25rem 0.45rem",
                                    fontSize: "0.72rem",
                                    fontFamily: "monospace",
                                  }}
                                >
                                  <TrackerFavicon
                                    urlOrHost={tr.trackerHost || tr.trackerUrl}
                                    size={13}
                                  />
                                  <span>{tr.trackerHost || tr.trackerUrl}</span>
                                  {(tr.seeders > 0 || tr.leechers > 0) && (
                                    <span style={{ opacity: 0.85 }}>
                                      ({tr.seeders}s/{tr.leechers}l)
                                    </span>
                                  )}
                                </span>
                              ))}
                              {t.trackers.length === 0 && (
                                <span
                                  style={{
                                    fontSize: "0.78rem",
                                    color: "var(--text-muted)",
                                  }}
                                >
                                  No positive tracker scrapes yet.
                                </span>
                              )}
                            </div>
                          </td>
                          <td style={{ textAlign: "right" }}>
                            <button
                              className="btn btn-sm btn-outline"
                              style={{
                                fontSize: "0.75rem",
                                padding: "0.25rem 0.5rem",
                              }}
                              onClick={() => {
                                const targetKey = unifiedItems.find(
                                  (u) =>
                                    u.infoHash.toLowerCase() ===
                                    t.infoHash.toLowerCase(),
                                )?.key;
                                if (targetKey) setSelectedKey(targetKey);
                                setActiveTab("booster");
                              }}
                            >
                              ⚡ Inspect
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                    {filteredMatrixTorrents.length === 0 && (
                      <tr>
                        <td
                          colSpan={4}
                          style={{
                            padding: "3rem",
                            textAlign: "center",
                            color: "var(--text-muted)",
                          }}
                        >
                          No library torrents match the search query.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            )
          ) : /* TRACKERS -> TORRENTS VIEW */
          matrixLayoutMode === "grid" ? (
            /* TRACKERS GRID VIEW */
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))",
                gap: "1.25rem",
                flex: "1 1 auto",
                minHeight: 0,
                overflowY: "auto",
                paddingRight: "0.25rem",
              }}
            >
              {filteredMatrixTrackers.map((tr) => (
                <div
                  key={tr.trackerId || tr.trackerUrl}
                  className="card"
                  style={{
                    padding: "1rem",
                    backgroundColor: "var(--bg-secondary)",
                    borderRadius: "8px",
                    border: "1px solid rgba(255, 255, 255, 0.08)",
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.75rem",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "flex-start",
                      gap: "0.5rem",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.5rem",
                        minWidth: 0,
                      }}
                    >
                      <TrackerFavicon urlOrHost={tr.trackerUrl} size={20} />
                      <div style={{ minWidth: 0 }}>
                        <div
                          style={{
                            fontWeight: 600,
                            fontSize: "0.9rem",
                            fontFamily: "monospace",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {tr.host || tr.trackerUrl}
                        </div>
                        <div
                          style={{
                            display: "flex",
                            gap: "0.35rem",
                            marginTop: "0.15rem",
                          }}
                        >
                          <span
                            className="badge badge-secondary"
                            style={{ fontSize: "0.68rem" }}
                          >
                            {tr.protocol}
                          </span>
                          {tr.latencyMs > 0 && (
                            <span
                              style={{
                                fontSize: "0.68rem",
                                color: "var(--text-muted)",
                                fontFamily: "monospace",
                              }}
                            >
                              {tr.latencyMs}ms
                            </span>
                          )}
                        </div>
                      </div>
                    </div>
                    <span
                      className="badge badge-success"
                      style={{ fontSize: "0.72rem", flexShrink: 0 }}
                    >
                      {tr.registeredTorrentsCount} Torrents
                    </span>
                  </div>

                  {/* Matched Torrents Poster / Title Gallery */}
                  <div
                    style={{
                      display: "flex",
                      flexWrap: "wrap",
                      gap: "0.5rem",
                      marginTop: "0.25rem",
                    }}
                  >
                    {tr.registeredTorrentNames.map((name, idx) => {
                      const matchedTorrent = (torrents ?? []).find(
                        (t) => t.name === name,
                      );
                      const meta = matchedTorrent
                        ? torrentMetaMap.get(
                            (matchedTorrent.infoHash || "").toLowerCase(),
                          )
                        : undefined;
                      return (
                        <div
                          key={idx}
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.4rem",
                            padding: "0.25rem 0.5rem",
                            borderRadius: "4px",
                            backgroundColor: "rgba(255,255,255,0.05)",
                            border: "1px solid var(--border-light)",
                            fontSize: "0.75rem",
                            maxWidth: "100%",
                          }}
                          title={name}
                        >
                          {meta?.posterUrl ? (
                            <img
                              src={meta.posterUrl}
                              alt={name}
                              style={{
                                width: "16px",
                                height: "22px",
                                borderRadius: "2px",
                                objectFit: "cover",
                                flexShrink: 0,
                              }}
                            />
                          ) : (
                            <span>🎬</span>
                          )}
                          <span
                            style={{
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap",
                            }}
                          >
                            {meta?.mediaTitle || name}
                          </span>
                        </div>
                      );
                    })}
                    {tr.registeredTorrentNames.length === 0 && (
                      <span
                        style={{
                          fontSize: "0.78rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        No library torrents currently registered on this tracker
                        endpoint.
                      </span>
                    )}
                  </div>
                </div>
              ))}
              {filteredMatrixTrackers.length === 0 && (
                <div
                  style={{
                    gridColumn: "1 / -1",
                    padding: "3rem",
                    textAlign: "center",
                    color: "var(--text-muted)",
                  }}
                >
                  No tracker endpoints match the search query.
                </div>
              )}
            </div>
          ) : (
            /* TRACKERS TABLE VIEW */
            <div
              className="torrent-table-wrapper"
              style={{
                borderRadius: "6px",
                border: "1px solid var(--border)",
                flex: "1 1 auto",
                minHeight: 0,
                overflowY: "auto",
                backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
              }}
            >
              <table
                className="torrent-table"
                style={{ width: "100%", fontSize: "0.85rem" }}
              >
                <thead
                  style={{
                    position: "sticky",
                    top: 0,
                    zIndex: 2,
                    backgroundColor: "var(--bg-secondary)",
                  }}
                >
                  <tr>
                    <th className="torrent-table-th" style={{ width: "35%" }}>
                      Tracker Endpoint
                    </th>
                    <th className="torrent-table-th" style={{ width: "10%" }}>
                      Protocol
                    </th>
                    <th className="torrent-table-th" style={{ width: "10%" }}>
                      Latency
                    </th>
                    <th className="torrent-table-th" style={{ width: "45%" }}>
                      Matched Library Torrents
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {filteredMatrixTrackers.map((tr) => (
                    <tr
                      key={tr.trackerId || tr.trackerUrl}
                      className="torrent-table-row"
                    >
                      <td>
                        <div
                          style={{
                            display: "flex",
                            alignItems: "center",
                            gap: "0.5rem",
                          }}
                        >
                          <TrackerFavicon urlOrHost={tr.trackerUrl} size={16} />
                          <span
                            style={{
                              fontFamily: "monospace",
                              fontSize: "0.82rem",
                              wordBreak: "break-all",
                            }}
                          >
                            {tr.trackerUrl}
                          </span>
                        </div>
                      </td>
                      <td>
                        <span
                          className="badge badge-secondary"
                          style={{ fontSize: "0.75rem" }}
                        >
                          {tr.protocol}
                        </span>
                      </td>
                      <td style={{ fontFamily: "monospace" }}>
                        {tr.latencyMs > 0 ? `${tr.latencyMs}ms` : "-"}
                      </td>
                      <td>
                        <div
                          style={{
                            display: "flex",
                            flexWrap: "wrap",
                            gap: "0.4rem",
                          }}
                        >
                          {tr.registeredTorrentNames.map((name, idx) => {
                            const matchedTorrent = (torrents ?? []).find(
                              (t) => t.name === name,
                            );
                            const meta = matchedTorrent
                              ? torrentMetaMap.get(
                                  (matchedTorrent.infoHash || "").toLowerCase(),
                                )
                              : undefined;
                            return (
                              <span
                                key={idx}
                                className="badge badge-secondary"
                                style={{
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  padding: "0.25rem 0.5rem",
                                  fontSize: "0.72rem",
                                }}
                                title={name}
                              >
                                {meta?.posterUrl && (
                                  <img
                                    src={meta.posterUrl}
                                    alt={name}
                                    style={{
                                      width: "14px",
                                      height: "18px",
                                      borderRadius: "2px",
                                      objectFit: "cover",
                                    }}
                                  />
                                )}
                                <span>{meta?.mediaTitle || name}</span>
                              </span>
                            );
                          })}
                          {tr.registeredTorrentNames.length === 0 && (
                            <span
                              style={{
                                fontSize: "0.78rem",
                                color: "var(--text-muted)",
                              }}
                            >
                              No library torrents currently registered.
                            </span>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                  {filteredMatrixTrackers.length === 0 && (
                    <tr>
                      <td
                        colSpan={4}
                        style={{
                          padding: "3rem",
                          textAlign: "center",
                          color: "var(--text-muted)",
                        }}
                      >
                        No tracker endpoints match the search query.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* TAB 3: TRACKER RADAR */}
      {activeTab === "radar" && (
        <div
          className="card"
          style={{
            padding: "1.25rem",
            flex: "1 1 auto",
            display: "flex",
            flexDirection: "column",
            minHeight: 0,
            marginBottom: "0.5rem",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "1rem",
              marginBottom: "1rem",
            }}
          >
            <div
              style={{
                display: "flex",
                gap: "0.5rem",
                alignItems: "center",
                flexWrap: "wrap",
              }}
            >
              <input
                type="text"
                className="form-control"
                style={{
                  width: "240px",
                  padding: "0.4rem 0.75rem",
                  fontSize: "0.85rem",
                }}
                placeholder="Search tracker hosts..."
                value={trackerSearch}
                onChange={(e) => setTrackerSearch(e.target.value)}
              />
              <select
                className="form-control"
                style={{
                  width: "160px",
                  padding: "0.4rem 0.75rem",
                  fontSize: "0.85rem",
                }}
                value={sourceFilter}
                onChange={(e) => setSourceFilter(e.target.value)}
              >
                <option value="all">All Sources</option>
                <option value="active">Active Swarm Harvest</option>
                <option value="prowlarr">Prowlarr</option>
                <option value="feeds">Public Feeds</option>
                <option value="manual">Manual Entry</option>
              </select>
            </div>
            <div
              style={{
                display: "flex",
                gap: "0.5rem",
                alignItems: "center",
                flexWrap: "wrap",
              }}
            >
              <button
                className="btn btn-action"
                onClick={handleCopyAllTrackers}
                title="Copy all tracker URLs to clipboard"
              >
                📋 Copy All
              </button>
              <button
                className="btn btn-action"
                onClick={handleExportTrackers}
                title="Download verified and active trackers as a .txt file"
              >
                📤 Export (.txt)
              </button>
              <button
                className="btn btn-action"
                onClick={() => setShowBulkImportModal(true)}
                title="Paste multiple tracker URLs at once"
              >
                📥 Bulk Import
              </button>
              <button
                className="btn btn-primary"
                onClick={() => setIsAddingTracker(true)}
              >
                + Add Single
              </button>
            </div>
          </div>

          {isAddingTracker && (
            <form
              onSubmit={handleAddCustomTracker}
              style={{ display: "flex", gap: "0.5rem", marginBottom: "1rem" }}
            >
              <input
                type="text"
                className="form-control"
                placeholder="udp://tracker.example.com:1337/announce"
                value={newTrackerUrl}
                onChange={(e) => setNewTrackerUrl(e.target.value)}
                style={{ flex: 1 }}
              />
              <button
                type="submit"
                className="btn btn-primary"
                disabled={addTracker.isPending}
              >
                Save
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => setIsAddingTracker(false)}
              >
                Cancel
              </button>
            </form>
          )}

          <div
            className="torrent-table-wrapper"
            style={{
              borderRadius: "6px",
              border: "1px solid var(--border)",
              marginTop: "0.5rem",
              flex: "1 1 auto",
              minHeight: 0,
              overflowY: "auto",
              backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
            }}
          >
            <table className="torrent-table" style={{ width: "100%" }}>
              <thead
                style={{
                  position: "sticky",
                  top: 0,
                  zIndex: 2,
                  backgroundColor: "var(--bg-secondary)",
                }}
              >
                <tr>
                  <th className="torrent-table-th" style={{ width: "38%" }}>
                    Tracker Endpoint
                  </th>
                  <th className="torrent-table-th" style={{ width: "10%" }}>
                    Protocol
                  </th>
                  <th className="torrent-table-th" style={{ width: "16%" }}>
                    Source
                  </th>
                  <th className="torrent-table-th" style={{ width: "12%" }}>
                    Status
                  </th>
                  <th className="torrent-table-th" style={{ width: "10%" }}>
                    Latency
                  </th>
                  <th className="torrent-table-th" style={{ width: "14%" }}>
                    Verified Swarms
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ width: "10%", textAlign: "right" }}
                  >
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredTrackers.map((tr) => (
                  <tr key={tr.id} className="torrent-table-row">
                    <td
                      style={{
                        fontFamily: "monospace",
                        fontSize: "0.82rem",
                        wordBreak: "break-all",
                      }}
                    >
                      <div
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.45rem",
                        }}
                      >
                        <TrackerFavicon urlOrHost={tr.url} size={15} />
                        <span>{tr.url}</span>
                      </div>
                    </td>
                    <td>
                      <span
                        className="badge badge-secondary"
                        style={{ fontSize: "0.75rem" }}
                      >
                        {tr.protocol}
                      </span>
                    </td>
                    <td>
                      <span
                        className="badge badge-outline"
                        style={{ fontSize: "0.75rem" }}
                      >
                        {tr.sourceName}
                      </span>
                    </td>
                    <td>
                      <span
                        className={`badge ${tr.status === "Alive" || tr.status === 1 ? "badge-success" : tr.status === "Offline" || tr.status === 3 ? "badge-danger" : "badge-secondary"}`}
                        style={{ fontSize: "0.75rem" }}
                      >
                        {tr.status}
                      </span>
                    </td>
                    <td style={{ fontFamily: "monospace" }}>
                      {tr.latencyMs > 0 ? `${tr.latencyMs}ms` : "-"}
                    </td>
                    <td>
                      {tr.totalVerifiedTorrents ?? tr.totalSwarmsFound} swarms
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <button
                        className="btn btn-sm btn-danger"
                        style={{
                          padding: "0.25rem 0.6rem",
                          fontSize: "0.75rem",
                        }}
                        onClick={() => deleteTracker.mutate(tr.id)}
                      >
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 4: SOURCES & AUTOMATION SETTINGS */}
      {activeTab === "settings" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          {/* Automation Toggles */}
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>
              ⚡ Automation & Background Optimization
            </h3>
            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                margin: "0 0 1rem 0",
              }}
            >
              TrackerBoost runs as a background service to constantly discover
              new trackers, monitor health, and optimize swarms across Leecharr
              and connected download clients.
            </p>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
              }}
            >
              <label
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.75rem",
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={settings?.autoBoostEnabled ?? true}
                  onChange={() => handleToggleSetting("autoBoostEnabled")}
                  style={{ width: "1.1rem", height: "1.1rem" }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                    Automatic Background Swarm Boosting (Enabled by Default)
                  </div>
                  <div
                    style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}
                  >
                    Periodically queries candidate trackers and automatically
                    injects verified positive matches into active downloads.
                  </div>
                </div>
              </label>

              <label
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.75rem",
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={settings?.autoHarvestEnabled ?? true}
                  onChange={() => handleToggleSetting("autoHarvestEnabled")}
                  style={{ width: "1.1rem", height: "1.1rem" }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                    Automatic Swarm Tracker Harvesting (Enabled by Default)
                  </div>
                  <div
                    style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}
                  >
                    Continuously extracts and catalogues new public tracker
                    endpoints from downloading torrents to grow the tracker
                    database.
                  </div>
                </div>
              </label>

              <label
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.75rem",
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={settings?.onlyVerified ?? true}
                  onChange={() => handleToggleSetting("onlyVerified")}
                  style={{ width: "1.1rem", height: "1.1rem" }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                    Scrape Verification Guard (Strict Mode)
                  </div>
                  <div
                    style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}
                  >
                    Only injects trackers that respond with active seeders or
                    leechers for the specific info_hash, preventing client
                    clutter.
                  </div>
                </div>
              </label>
            </div>
          </div>

          {/* Connected Download Agents */}
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>
              Connected Download Agents
            </h3>
            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                margin: "0 0 1rem 0",
              }}
            >
              TrackerBoost coordinates with your download clients (qBittorrent,
              Transmission, Deluge) to inject verified trackers into active
              physical downloads.
            </p>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
              {(downloadClients ?? [])
                .filter((c) => c.enable)
                .map((client) => (
                  <span
                    key={client.id}
                    className="badge badge-primary"
                    style={{ padding: "0.4rem 0.75rem", fontSize: "0.85rem" }}
                  >
                    ⚡ {client.name} ({client.clientType})
                  </span>
                ))}
              {enabledClientsCount === 0 && (
                <span style={{ fontSize: "0.85rem", color: "var(--warning)" }}>
                  No download agents currently configured. Add qBittorrent or
                  Transmission in Settings ⚙️ to boost real downloads.
                </span>
              )}
            </div>
          </div>

          {/* Discovery Feeds */}
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>
              Manual Discovery Triggers
            </h3>
            <div
              style={{
                display: "flex",
                gap: "0.75rem",
                flexWrap: "wrap",
                marginTop: "0.75rem",
              }}
            >
              <button
                className="btn btn-action"
                onClick={handleHarvestDownloads}
                disabled={harvestDownloads.isPending}
              >
                {harvestDownloads.isPending
                  ? "⏳ Harvesting Swarms..."
                  : "🔄 Harvest Live Swarms"}
              </button>
              <button
                className="btn btn-action"
                onClick={handleHarvestProwlarr}
                disabled={harvestProwlarr.isPending}
              >
                {harvestProwlarr.isPending
                  ? "⏳ Syncing Prowlarr..."
                  : "🔄 Sync Prowlarr Trackers"}
              </button>
              <button
                className="btn btn-action"
                onClick={handleHarvestFeeds}
                disabled={harvestFeeds.isPending}
              >
                {harvestFeeds.isPending
                  ? "⏳ Syncing Feeds..."
                  : "🌐 Sync Curated Feeds"}
              </button>
              <button
                className="btn btn-action"
                onClick={handleScanAll}
                disabled={scanTrackers.isPending}
              >
                {scanTrackers.isPending
                  ? "⏳ Probing Trackers..."
                  : "📡 Probe All Trackers"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* TAB 5: ACTIVITY LOGS */}
      {activeTab === "logs" && (
        <div
          className="card"
          style={{
            padding: "1.25rem",
            flex: "1 1 auto",
            display: "flex",
            flexDirection: "column",
            minHeight: 0,
            marginBottom: "0.5rem",
          }}
        >
          {/* Controls bar */}
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "1rem",
              marginBottom: "1rem",
              paddingBottom: "1rem",
              borderBottom: "1px solid var(--border-color)",
            }}
          >
            <div
              style={{
                display: "flex",
                gap: "0.5rem",
                alignItems: "center",
                flexWrap: "wrap",
              }}
            >
              <select
                className="form-control"
                style={{
                  width: "150px",
                  padding: "0.4rem 0.6rem",
                  fontSize: "0.82rem",
                }}
                value={logCategoryFilter}
                onChange={(e) => setLogCategoryFilter(e.target.value)}
              >
                <option value="all">All Categories</option>
                <option value="Scrape">🔍 Scrapes</option>
                <option value="Health">🩺 Health Probes</option>
                <option value="Discovery">📡 Discovery</option>
                <option value="Inject">⚡ Injections</option>
                <option value="Cycle">⚙️ Daemon Cycles</option>
                <option value="General">General</option>
              </select>

              <select
                className="form-control"
                style={{
                  width: "130px",
                  padding: "0.4rem 0.6rem",
                  fontSize: "0.82rem",
                }}
                value={logLevelFilter}
                onChange={(e) => setLogLevelFilter(e.target.value)}
              >
                <option value="all">All Levels</option>
                <option value="Success">🟢 Success</option>
                <option value="Info">🔵 Info</option>
                <option value="Warn">🟡 Warning</option>
                <option value="Error">🔴 Error</option>
              </select>

              <input
                type="text"
                className="form-control"
                style={{
                  width: "240px",
                  padding: "0.4rem 0.75rem",
                  fontSize: "0.82rem",
                }}
                placeholder="Search logs, hosts, hashes..."
                value={logSearch}
                onChange={(e) => setLogSearch(e.target.value)}
              />
            </div>

            <div
              style={{
                display: "flex",
                gap: "0.5rem",
                alignItems: "center",
                flexWrap: "wrap",
              }}
            >
              <label
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  fontSize: "0.82rem",
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={logAutoRefresh}
                  onChange={(e) => setLogAutoRefresh(e.target.checked)}
                />
                <span>Live Refresh (3s)</span>
              </label>

              <button
                className="btn btn-outline"
                style={{ fontSize: "0.82rem", padding: "0.4rem 0.75rem" }}
                onClick={() => refetchLogs()}
                title="Refresh log entries"
              >
                🔄 Refresh
              </button>

              <button
                className="btn btn-danger"
                style={{ fontSize: "0.82rem", padding: "0.4rem 0.75rem" }}
                onClick={handleClearLogs}
                disabled={clearLogs.isPending || (boostLogs ?? []).length === 0}
                title="Clear current log buffer"
              >
                🗑️ Clear Logs
              </button>
            </div>
          </div>

          {/* Logs Table / Console */}
          {logsLoading ? (
            <div
              style={{
                padding: "3rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              Loading daemon activity logs...
            </div>
          ) : filteredLogs.length === 0 ? (
            <div
              style={{
                padding: "3rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              No log entries found matching current filter.
            </div>
          ) : (
            <div
              className="torrent-table-wrapper"
              style={{
                borderRadius: "6px",
                border: "1px solid var(--border)",
                flex: "1 1 auto",
                minHeight: 0,
                overflowY: "auto",
                backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
              }}
            >
              <table
                className="torrent-table"
                style={{ width: "100%", fontSize: "0.82rem" }}
              >
                <thead
                  style={{
                    position: "sticky",
                    top: 0,
                    zIndex: 2,
                    backgroundColor: "var(--bg-secondary)",
                  }}
                >
                  <tr>
                    <th className="torrent-table-th" style={{ width: "10%" }}>
                      Time
                    </th>
                    <th className="torrent-table-th" style={{ width: "9%" }}>
                      Level
                    </th>
                    <th className="torrent-table-th" style={{ width: "12%" }}>
                      Category
                    </th>
                    <th className="torrent-table-th" style={{ width: "24%" }}>
                      Tracker / InfoHash
                    </th>
                    <th className="torrent-table-th" style={{ width: "45%" }}>
                      Activity Message
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {filteredLogs.map((log) => {
                    const levelClass =
                      log.level === "Success"
                        ? "badge-success"
                        : log.level === "Error"
                          ? "badge-danger"
                          : log.level === "Warn"
                            ? "badge-warning"
                            : "badge-primary";

                    return (
                      <tr key={log.id} className="torrent-table-row">
                        <td
                          style={{
                            fontFamily: "monospace",
                            color: "var(--text-muted)",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {new Date(log.timestamp).toLocaleTimeString()}
                        </td>
                        <td>
                          <span
                            className={`badge ${levelClass}`}
                            style={{ fontSize: "0.72rem" }}
                          >
                            {log.level === "Success"
                              ? "🟢 Success"
                              : log.level === "Error"
                                ? "🔴 Error"
                                : log.level === "Warn"
                                  ? "🟡 Warn"
                                  : "🔵 Info"}
                          </span>
                        </td>
                        <td>
                          <span
                            className="badge badge-secondary"
                            style={{ fontSize: "0.72rem" }}
                          >
                            {log.category}
                          </span>
                        </td>
                        <td
                          style={{
                            fontFamily: "monospace",
                            fontSize: "0.78rem",
                            wordBreak: "break-all",
                          }}
                        >
                          {log.trackerUrl ? (
                            <div
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.35rem",
                              }}
                            >
                              <TrackerFavicon
                                urlOrHost={log.trackerUrl}
                                size={13}
                              />
                              <span style={{ color: "var(--accent)" }}>
                                {log.trackerUrl}
                              </span>
                            </div>
                          ) : log.infoHash ? (
                            <span style={{ color: "var(--text-muted)" }}>
                              {log.infoHash.slice(0, 16)}...
                            </span>
                          ) : (
                            <span style={{ color: "var(--text-dim)" }}>-</span>
                          )}
                        </td>
                        <td style={{ wordBreak: "break-word" }}>
                          {log.message}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* BULK IMPORT MODAL */}
      {showBulkImportModal && (
        <div
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0, 0, 0, 0.75)",
            backdropFilter: "blur(6px)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 9999,
            padding: "1rem",
          }}
          onClick={() => setShowBulkImportModal(false)}
        >
          <div
            className="card"
            style={{
              width: "560px",
              maxWidth: "92vw",
              display: "flex",
              flexDirection: "column",
              borderRadius: "10px",
              padding: "1.25rem",
              gap: "1rem",
            }}
            onClick={(e) => e.stopPropagation()}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <div
                style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
              >
                <span style={{ fontSize: "1.2rem" }}>📥</span>
                <h3 style={{ margin: 0, fontSize: "1.05rem" }}>
                  Bulk Import Trackers
                </h3>
              </div>
              <button
                className="btn btn-sm btn-outline"
                onClick={() => setShowBulkImportModal(false)}
                style={{ padding: "0.2rem 0.5rem" }}
              >
                ✕
              </button>
            </div>

            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                margin: 0,
              }}
            >
              Paste tracker announce URLs (one per line). Supported protocols:{" "}
              <code>udp://</code>, <code>http://</code>, <code>https://</code>.
            </p>

            <textarea
              className="form-control"
              rows={8}
              placeholder="udp://tracker.opentrackr.org:1337/announce&#10;http://tracker.example.com/announce&#10;udp://open.stealth.si:80/announce"
              value={bulkImportText}
              onChange={(e) => setBulkImportText(e.target.value)}
              style={{ fontFamily: "monospace", fontSize: "0.82rem" }}
            />

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-action"
                onClick={() => setShowBulkImportModal(false)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleBulkImportTrackers}
                disabled={isBulkImporting || !bulkImportText.trim()}
              >
                {isBulkImporting ? "Importing Trackers..." : "Import Trackers"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default TrackerBoost;
