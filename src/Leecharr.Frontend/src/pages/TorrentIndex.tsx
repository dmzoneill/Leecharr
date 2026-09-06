import React, { useState, useMemo, useEffect } from "react";
import { Torrent, Category } from "../api/types";
import { TorrentGrid } from "../components/TorrentGrid";
import { TorrentTable } from "../components/TorrentTable";
import { TorrentDetailPanel } from "../components/TorrentDetailPanel";
import { TorrentToolbar } from "./torrentindex/TorrentToolbar";
import { TorrentFilterPanel } from "./torrentindex/TorrentFilterPanel";
import { QuickSettingsDrawer } from "../components/quicksettings/QuickSettingsDrawer";
import { ViewMode } from "./torrentindex/types";
import { extractTrackerDomain } from "../utils/formatters";
import { useConfirm } from "../context/ConfirmContext";
import { useToast } from "../context/ToastContext";
import { useTorrentStore } from "../stores/useTorrentStore";
import { api } from "../api/client";
import { useQueryClient } from "@tanstack/react-query";

interface TorrentIndexProps {
  torrents: Torrent[];
  categories?: Category[];
  selectedCategory?: string;
  onSelectCategory?: (cat: string) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (payload: { id: number; deleteFiles?: boolean }) => void;
  onOpenAddModal: () => void;
  onOpenSearchModal: () => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
  onRefresh?: () => void;
}

export const TorrentIndex: React.FC<TorrentIndexProps> = ({
  torrents,
  onPause,
  onResume,
  onDelete,
  onOpenAddModal,
  onOpenSearchModal,
  onNavigateTab,
  onRefresh,
}) => {
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const [viewMode, setViewMode] = useState<ViewMode>("table");
  const [selectedState, setSelectedState] = useState<string>("All");
  const [selectedTracker, setSelectedTracker] = useState<string>("All");
  const [selectedPrivacy, setSelectedPrivacy] = useState<string>("All");
  const [filter, setFilter] = useState<string>("");

  const selectedTorrentId = useTorrentStore((state) => state.selectedTorrentId);
  const setSelectedTorrentId = useTorrentStore(
    (state) => state.setSelectedTorrentId,
  );
  const selectedIds = useTorrentStore((state) => state.selectedIds);
  const toggleSelectedId = useTorrentStore((state) => state.toggleSelectedId);
  const selectAllIds = useTorrentStore((state) => state.selectAllIds);
  const clearSelection = useTorrentStore((state) => state.clearSelection);
  const removeTorrent = useTorrentStore((state) => state.removeTorrent);
  const telemetry = useTorrentStore((state) => state.telemetry);

  const [bulkPending, setBulkPending] = useState<boolean>(false);
  const confirm = useConfirm();
  const [showQuickSettings, setShowQuickSettings] = useState<boolean>(() => {
    return localStorage.getItem("leecharr_quick_settings_open") === "true";
  });

  const handleToggleQuickSettings = () => {
    setShowQuickSettings((prev) => {
      const next = !prev;
      localStorage.setItem("leecharr_quick_settings_open", String(next));
      return next;
    });
  };

  const [isFilterCollapsed, setIsFilterCollapsed] = useState<boolean>(() => {
    return localStorage.getItem("leecharr_filter_collapsed") === "true";
  });

  const toggleFilter = () => {
    setIsFilterCollapsed((prev) => {
      const next = !prev;
      localStorage.setItem("leecharr_filter_collapsed", String(next));
      return next;
    });
  };

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName?.toLowerCase();
      if (tag === "input" || tag === "textarea" || tag === "select") return;

      if (e.key === "q" || e.key === "Q") {
        e.preventDefault();
        handleToggleQuickSettings();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  const stateCounts = useMemo(() => {
    const counts: Record<string, number> = {
      All: torrents.length,
      Downloading: 0,
      Seeding: 0,
      Paused: 0,
      Queued: 0,
      Error: 0,
    };
    for (const t of torrents) {
      const st = (t.status || "").toLowerCase();
      if (st === "downloading") counts.Downloading++;
      else if (st === "seeding" || st === "completed") counts.Seeding++;
      else if (st === "paused" || st === "stopped" || st === "idle")
        counts.Paused++;
      else if (st === "queued") counts.Queued++;
      else if (st === "error") counts.Error++;
    }
    return counts;
  }, [torrents]);

  const privacyCounts = useMemo(() => {
    let priv = 0;
    let pub = 0;
    for (const t of torrents) {
      if (t.isPrivate) priv++;
      else pub++;
    }
    return {
      All: torrents.length,
      Private: priv,
      Public: pub,
    };
  }, [torrents]);

  const trackerGroups = useMemo(() => {
    const groups: Record<string, number> = {};
    for (const t of torrents) {
      const domains = new Set<string>();
      if (t.trackers && t.trackers.length > 0) {
        for (const u of t.trackers) {
          const d = extractTrackerDomain(u);
          if (d && d !== "Unknown") domains.add(d);
        }
      }
      if (t.trackerUrl) {
        const d = extractTrackerDomain(t.trackerUrl);
        if (d && d !== "Unknown") domains.add(d);
      }
      if (domains.size === 0) {
        domains.add("Unknown");
      }
      for (const d of domains) {
        groups[d] = (groups[d] || 0) + 1;
      }
    }
    return Object.entries(groups).sort((a, b) => a[0].localeCompare(b[0]));
  }, [torrents]);

  const { totalUploadSpeed, totalDownloadSpeed } = useMemo(() => {
    let ul = 0;
    let dl = 0;
    for (const t of torrents) {
      const tel = telemetry[t.id];
      ul += tel?.uploadSpeed ?? t.uploadSpeed ?? 0;
      dl += tel?.downloadSpeed ?? t.downloadSpeed ?? 0;
    }
    return { totalUploadSpeed: ul, totalDownloadSpeed: dl };
  }, [torrents, telemetry]);

  const handleStartAll = async () => {
    try {
      await api.startAllSeeding();
      showToast("All torrents resumed", "success");
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      onRefresh?.();
    } catch (err: any) {
      showToast(err?.message || "Failed to start all torrents", "error");
    }
  };

  const handleStopAll = async () => {
    try {
      await api.stopAllSeeding();
      showToast("All torrents stopped", "info");
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      onRefresh?.();
    } catch (err: any) {
      showToast(err?.message || "Failed to stop all torrents", "error");
    }
  };

  const handleToggleSelect = (id: number) => {
    toggleSelectedId(id);
  };

  const handleSelectAll = (ids: number[]) => {
    selectAllIds(ids);
  };

  const handleDelete = (payload: { id: number; deleteFiles?: boolean }) => {
    removeTorrent(payload.id);
    onDelete(payload);
  };

  const handleBulkStart = async () => {
    const activeIds = new Set(torrents.map((t) => t.id));
    const validSelectedIds = Array.from(selectedIds).filter((id) =>
      activeIds.has(id),
    );
    for (const id of selectedIds) {
      if (!activeIds.has(id)) {
        removeTorrent(id);
      }
    }
    if (validSelectedIds.length === 0) {
      clearSelection();
      return;
    }
    setBulkPending(true);
    try {
      const results = await Promise.allSettled(
        validSelectedIds.map((id) => api.resumeTorrent(id)),
      );
      const succeeded = results.filter((r) => r.status === "fulfilled").length;
      const failed = results.filter((r) => r.status === "rejected").length;
      if (failed === 0) {
        showToast(
          `Resumed ${succeeded} torrent${succeeded === 1 ? "" : "s"}`,
          "success",
        );
      } else if (succeeded === 0) {
        showToast(
          `Failed to resume ${failed} torrent${failed === 1 ? "" : "s"}`,
          "error",
        );
      } else {
        showToast(
          `Resumed ${succeeded} torrent${succeeded === 1 ? "" : "s"}, ${failed} failed`,
          "warning",
        );
      }
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      onRefresh?.();
      clearSelection();
    } catch (err: any) {
      showToast(err?.message || "Failed to resume selected torrents", "error");
    } finally {
      setBulkPending(false);
    }
  };

  const handleBulkStop = async () => {
    const activeIds = new Set(torrents.map((t) => t.id));
    const validSelectedIds = Array.from(selectedIds).filter((id) =>
      activeIds.has(id),
    );
    for (const id of selectedIds) {
      if (!activeIds.has(id)) {
        removeTorrent(id);
      }
    }
    if (validSelectedIds.length === 0) {
      clearSelection();
      return;
    }
    setBulkPending(true);
    try {
      const results = await Promise.allSettled(
        validSelectedIds.map((id) => api.pauseTorrent(id)),
      );
      const succeeded = results.filter((r) => r.status === "fulfilled").length;
      const failed = results.filter((r) => r.status === "rejected").length;
      if (failed === 0) {
        showToast(
          `Stopped ${succeeded} torrent${succeeded === 1 ? "" : "s"}`,
          "info",
        );
      } else if (succeeded === 0) {
        showToast(
          `Failed to stop ${failed} torrent${failed === 1 ? "" : "s"}`,
          "error",
        );
      } else {
        showToast(
          `Stopped ${succeeded} torrent${succeeded === 1 ? "" : "s"}, ${failed} failed`,
          "warning",
        );
      }
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      onRefresh?.();
      clearSelection();
    } catch (err: any) {
      showToast(err?.message || "Failed to stop selected torrents", "error");
    } finally {
      setBulkPending(false);
    }
  };

  const handleBulkDelete = async () => {
    const activeIds = new Set(torrents.map((t) => t.id));
    const validSelectedIds = Array.from(selectedIds).filter((id) =>
      activeIds.has(id),
    );
    for (const id of selectedIds) {
      if (!activeIds.has(id)) {
        removeTorrent(id);
      }
    }
    if (validSelectedIds.length === 0) {
      clearSelection();
      return;
    }

    const ok = await confirm({
      title: "Delete Selected Torrents",
      message: `Delete ${validSelectedIds.length} selected torrent(s)?`,
      danger: true,
      confirmText: "Delete",
    });
    if (!ok) return;

    setBulkPending(true);
    try {
      const results = await Promise.allSettled(
        validSelectedIds.map((id) => {
          removeTorrent(id);
          return api.deleteTorrent(id, false);
        }),
      );
      const succeeded = results.filter((r) => r.status === "fulfilled").length;
      const failed = results.filter((r) => r.status === "rejected").length;
      if (failed === 0) {
        showToast(
          `Deleted ${succeeded} torrent${succeeded === 1 ? "" : "s"}`,
          "info",
        );
      } else if (succeeded === 0) {
        showToast(
          `Failed to delete ${failed} torrent${failed === 1 ? "" : "s"}`,
          "error",
        );
      } else {
        showToast(
          `Deleted ${succeeded} torrent${succeeded === 1 ? "" : "s"}, ${failed} failed`,
          "warning",
        );
      }
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      onRefresh?.();
      clearSelection();
    } catch (err: any) {
      showToast(err?.message || "Failed to delete selected torrents", "error");
    } finally {
      setBulkPending(false);
    }
  };

  const currentSelectedTorrent = useMemo(() => {
    if (!selectedTorrentId) return null;
    return torrents.find((t) => t.id === selectedTorrentId) || null;
  }, [torrents, selectedTorrentId]);

  return (
    <div className="torrent-index-page">
      <TorrentToolbar
        count={torrents.length}
        totalUploadSpeed={totalUploadSpeed}
        totalDownloadSpeed={totalDownloadSpeed}
        filter={filter}
        onFilterChange={setFilter}
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        onAddTorrent={onOpenAddModal}
        onSearchIndexers={onOpenSearchModal}
        onStartAll={handleStartAll}
        onStopAll={handleStopAll}
        selectedCount={selectedIds.size}
        bulkPending={bulkPending}
        onBulkStart={handleBulkStart}
        onBulkStop={handleBulkStop}
        onBulkDelete={handleBulkDelete}
        onBulkClear={clearSelection}
        showQuickSettings={showQuickSettings}
        onToggleQuickSettings={handleToggleQuickSettings}
        isFilterCollapsed={isFilterCollapsed}
        onToggleFilter={toggleFilter}
      />
      <QuickSettingsDrawer
        isOpen={showQuickSettings}
        onClose={() => {
          setShowQuickSettings(false);
          localStorage.setItem("leecharr_quick_settings_open", "false");
        }}
        onNavigateSettings={(tab) =>
          onNavigateTab && onNavigateTab("settings", tab)
        }
      />
      <div className="torrent-content-layout">
        {!isFilterCollapsed && (
          <TorrentFilterPanel
            selectedState={selectedState}
            onSelectState={setSelectedState}
            selectedTracker={selectedTracker}
            onSelectTracker={setSelectedTracker}
            selectedPrivacy={selectedPrivacy}
            onSelectPrivacy={setSelectedPrivacy}
            privacyCounts={privacyCounts}
            stateCounts={stateCounts}
            trackerGroups={trackerGroups}
            count={torrents.length}
            onCollapse={toggleFilter}
          />
        )}
        <div className="filter-content">
          <div className="torrent-split-pane">
            <div className="torrent-split-top">
              {viewMode === "table" ? (
                <TorrentTable
                  torrents={torrents}
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  privacyFilter={selectedPrivacy}
                  selectedId={currentSelectedTorrent?.id ?? null}
                  onSelect={(t) => setSelectedTorrentId(t ? t.id : null)}
                  onPause={onPause}
                  onResume={onResume}
                  onDelete={handleDelete}
                  selectedIds={selectedIds}
                  onToggleSelect={handleToggleSelect}
                  onSelectAll={handleSelectAll}
                  onSearchIndexers={onOpenSearchModal}
                  onNavigateTab={onNavigateTab}
                />
              ) : (
                <TorrentGrid
                  torrents={torrents}
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  privacyFilter={selectedPrivacy}
                  selectedId={currentSelectedTorrent?.id ?? null}
                  onSelect={(t) => setSelectedTorrentId(t ? t.id : null)}
                  onPause={onPause}
                  onResume={onResume}
                  onDelete={handleDelete}
                />
              )}
            </div>
            {currentSelectedTorrent && (
              <TorrentDetailPanel
                torrent={currentSelectedTorrent}
                torrentId={currentSelectedTorrent.id}
                onClose={() => setSelectedTorrentId(null)}
              />
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default TorrentIndex;
