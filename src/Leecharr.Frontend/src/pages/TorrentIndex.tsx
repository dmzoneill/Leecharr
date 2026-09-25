import React, { useState, useMemo, useEffect, useCallback } from "react";
import { Torrent, Category } from "../api/types";
import { TorrentGrid } from "../components/TorrentGrid";
import { TorrentTable } from "../components/TorrentTable";
import { TorrentDetailPanel } from "../components/TorrentDetailPanel";
import { TorrentToolbar } from "./torrentindex/TorrentToolbar";
import { TorrentFilterPanel } from "./torrentindex/TorrentFilterPanel";
import { QuickSettingsDrawer } from "../components/quicksettings/QuickSettingsDrawer";
import { DeleteTorrentModal } from "../components/DeleteTorrentModal";
import { BulkTagModal } from "../components/BulkTagModal";
import { ImportPackageModal } from "../components/ImportPackageModal";
import { ViewMode } from "./torrentindex/types";
import { extractTrackerDomain } from "../utils/formatters";
import { useTorrentStore } from "../stores/useTorrentStore";
import {
  useMoveTorrentQueue,
  useTags,
  useBulkTorrentAction,
} from "../api/hooks";
import { useColumnPreferences } from "./torrentindex/columnPreferences";
import { useToast } from "../context/ToastContext";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "../i18n";
import { getErrorMessage } from "../utils/errorUtils";
import {
  trackViewModeChange,
  trackBulkAction,
  trackQueueMove,
} from "../utils/analytics";

interface TorrentIndexProps {
  torrents: Torrent[];
  categories?: Category[];
  selectedCategory?: string;
  onSelectCategory?: (cat: string) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (payload: {
    id: number;
    deleteFiles?: boolean;
    ids?: number[];
  }) => void;
  onOpenAddModal: () => void;
  onOpenSearchModal: () => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
}

export const TorrentIndex: React.FC<TorrentIndexProps> = ({
  torrents,
  onPause,
  onResume,
  onDelete,
  onOpenAddModal,
  onOpenSearchModal,
  onNavigateTab,
}) => {
  const [viewMode, setViewMode] = useState<ViewMode>("table");
  const [selectedState, setSelectedState] = useState<string>("All");
  const [selectedTracker, setSelectedTracker] = useState<string>("All");
  const [selectedPrivacy, setSelectedPrivacy] = useState<string>("All");
  const [filter, setFilter] = useState<string>("");
  const [selectedTag, setSelectedTag] = useState<string>("All");
  const [selectedTagIds, setSelectedTagIds] = useState<Set<number>>(new Set());
  const [tagMatchMode, setTagMatchMode] = useState<"AND" | "OR">("OR");

  const toggleTag = useCallback((id: number) => {
    setSelectedTagIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
    setSelectedTag("All");
  }, []);

  const clearTags = useCallback(() => {
    setSelectedTagIds(new Set());
    setSelectedTag("All");
  }, []);

  const {
    visibleColumns,
    toggleColumn,
    resetToDefaults: resetColumns,
    resetSort,
    selectAll: selectAllColumns,
    deselectAll: deselectAllColumns,
    applyPreset: applyColumnPreset,
    toggleCategory: toggleCategoryColumns,
    columnOrder,
    setColumnOrder,
    columnWidths,
    setColumnWidths,
  } = useColumnPreferences();
  const [isColumnCustomizerOpen, setIsColumnCustomizerOpen] = useState(false);

  const selectedTorrentId = useTorrentStore((state) => state.selectedTorrentId);
  const setSelectedTorrentId = useTorrentStore(
    (state) => state.setSelectedTorrentId,
  );
  const selectedIds = useTorrentStore((state) => state.selectedIds);
  const toggleSelectedId = useTorrentStore((state) => state.toggleSelectedId);
  const selectAllIds = useTorrentStore((state) => state.selectAllIds);
  const clearSelection = useTorrentStore((state) => state.clearSelection);
  const removeTorrent = useTorrentStore((state) => state.removeTorrent);

  const { data: tags = [] } = useTags();
  const bulkAction = useBulkTorrentAction();
  const [bulkTagModalState, setBulkTagModalState] = useState<{
    isOpen: boolean;
    mode: "add" | "remove";
  } | null>(null);

  const [bulkPending, setBulkPending] = useState<boolean>(false);
  const [deleteModalState, setDeleteModalState] = useState<{
    isOpen: boolean;
    torrent?: Torrent | null;
    torrentName?: string;
    targetIds?: number[];
    count?: number;
  }>({ isOpen: false });
  const [showImportPackageModal, setShowImportPackageModal] = useState(false);

  const handleBulkAddTags = useCallback(() => {
    if (selectedIds.size === 0) return;
    setBulkTagModalState({ isOpen: true, mode: "add" });
  }, [selectedIds]);

  const handleBulkRemoveTags = useCallback(() => {
    if (selectedIds.size === 0) return;
    setBulkTagModalState({ isOpen: true, mode: "remove" });
  }, [selectedIds]);

  const { showToast } = useToast();
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const handleConfirmBulkTag = useCallback(
    async (tagIds: number[]) => {
      if (!bulkTagModalState || selectedIds.size === 0 || tagIds.length === 0)
        return;
      const mode = bulkTagModalState.mode;
      const ids = Array.from(selectedIds);
      setBulkPending(true);
      try {
        const res = await bulkAction.mutateAsync({
          torrentIds: ids,
          action: mode === "add" ? "addtags" : "removetags",
          tagIds,
        });

        if (res.failedCount === 0) {
          showToast(
            mode === "add"
              ? `Successfully assigned tags to ${res.successCount} torrent(s).`
              : `Successfully removed tags from ${res.successCount} torrent(s).`,
            "success",
          );
        } else {
          showToast(
            `${res.successCount} succeeded, ${res.failedCount} failed`,
            "info",
          );
        }
        setBulkTagModalState(null);
      } catch (err: unknown) {
        const msg =
          err instanceof Error
            ? err.message
            : `Failed to ${mode === "add" ? "assign" : "remove"} tags`;
        showToast(msg, "error");
      } finally {
        setBulkPending(false);
      }
    },
    [bulkTagModalState, selectedIds, bulkAction, showToast],
  );
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
    if (typeof window !== "undefined" && window.innerWidth <= 768) {
      return true;
    }
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

      // Escape key closes detail panel when no modal is open
      if (e.key === "Escape") {
        const modalOpen = !!document.querySelector(
          'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay, .modal-backdrop',
        );
        if (!modalOpen && selectedTorrentId !== null) {
          e.preventDefault();
          setSelectedTorrentId(null);
        }
      }
    };

    const handleCustomClose = () => {
      setShowQuickSettings(false);
      localStorage.setItem("leecharr_quick_settings_open", "false");
    };

    window.addEventListener("keydown", handleKeyDown);
    window.addEventListener("toggle-quick-settings", handleToggleQuickSettings);
    window.addEventListener("close-modals", handleCustomClose);

    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      window.removeEventListener("toggle-quick-settings", handleToggleQuickSettings);
      window.removeEventListener("close-modals", handleCustomClose);
    };
  }, [selectedTorrentId, setSelectedTorrentId]);

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
      if (st === "downloading" || st === "stalled" || st === "stalleddl")
        counts.Downloading++;
      else if (
        st === "seeding" ||
        st === "completed" ||
        st === "stalledup"
      )
        counts.Seeding++;
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

  const { tagGroups, untaggedCount } = useMemo(() => {
    let untagged = 0;
    const tagCountMap: Record<number, number> = {};
    for (const t of torrents) {
      if (!t.tagIds || t.tagIds.length === 0) {
        untagged++;
      } else {
        for (const tid of t.tagIds) {
          tagCountMap[tid] = (tagCountMap[tid] || 0) + 1;
        }
      }
    }
    const groups = (tags || []).map((tag) => ({
      id: tag.id,
      label: tag.label || `Tag ${tag.id}`,
      count: tagCountMap[tag.id] || 0,
      color: tag.color,
    }));
    return { tagGroups: groups, untaggedCount: untagged };
  }, [torrents, tags]);

  const isTorrentActive = (t: Torrent) => {
    const st = (
      useTorrentStore.getState().telemetry[t.id]?.status ??
      t.status ??
      ""
    ).toLowerCase();
    return st === "downloading" || st === "seeding" || st === "checking";
  };

  const handleStartAll = async () => {
    const inactive = torrents.filter((t) => !isTorrentActive(t));
    if (inactive.length > 0) {
      trackBulkAction("start_all", inactive.length);
      await Promise.all(inactive.map((t) => onResume(t.id)));
    }
  };

  const handleStopAll = async () => {
    const active = torrents.filter((t) => isTorrentActive(t));
    if (active.length > 0) {
      trackBulkAction("stop_all", active.length);
      await Promise.all(active.map((t) => onPause(t.id)));
    }
  };

  const handleToggleSelect = (id: number) => {
    toggleSelectedId(id);
  };

  const handleSelectAll = (ids: number[]) => {
    selectAllIds(ids);
  };

  const handleRequestDelete = (payload: {
    id: number;
    deleteFiles?: boolean;
    ids?: number[];
  }) => {
    const ids =
      payload.ids && payload.ids.length > 0 ? payload.ids : [payload.id];
    const activeIds = new Set(torrents.map((t) => t.id));
    const validIds = ids.filter((id) => activeIds.has(id));
    if (validIds.length === 0) return;

    if (validIds.length > 1) {
      selectAllIds(validIds);
      setDeleteModalState({
        isOpen: true,
        count: validIds.length,
        targetIds: validIds,
      });
      return;
    }

    const targetTorrent = torrents.find((t) => t.id === validIds[0]);
    setDeleteModalState({
      isOpen: true,
      torrent:
        targetTorrent ||
        ({ id: validIds[0], name: `Torrent #${validIds[0]}` } as Torrent),
      torrentName: targetTorrent?.name ?? `Torrent #${validIds[0]}`,
      count: 1,
      targetIds: validIds,
    });
  };

  const handleBulkStart = async () => {
    const activeIds = new Set(torrents.map((t) => t.id));
    const validSelectedIds = Array.from(selectedIds).filter((id) =>
      activeIds.has(id),
    );
    const targetIds =
      validSelectedIds.length > 0
        ? validSelectedIds
        : selectedTorrentId != null && activeIds.has(selectedTorrentId)
          ? [selectedTorrentId]
          : [];
    if (targetIds.length === 0) return;
    setBulkPending(true);
    try {
      trackBulkAction("start", targetIds.length);
      await Promise.all(targetIds.map((id) => onResume(id)));
    } finally {
      setBulkPending(false);
    }
  };

  const handleBulkStop = async () => {
    const activeIds = new Set(torrents.map((t) => t.id));
    const validSelectedIds = Array.from(selectedIds).filter((id) =>
      activeIds.has(id),
    );
    const targetIds =
      validSelectedIds.length > 0
        ? validSelectedIds
        : selectedTorrentId != null && activeIds.has(selectedTorrentId)
          ? [selectedTorrentId]
          : [];
    if (targetIds.length === 0) return;
    setBulkPending(true);
    try {
      trackBulkAction("stop", targetIds.length);
      await Promise.all(targetIds.map((id) => onPause(id)));
    } finally {
      setBulkPending(false);
    }
  };

  const handleBulkDelete = () => {
    const activeIds = new Set(torrents.map((t) => t.id));
    const validSelectedIds = Array.from(selectedIds).filter((id) =>
      activeIds.has(id),
    );
    for (const id of selectedIds) {
      if (!activeIds.has(id)) {
        removeTorrent(id);
      }
    }
    const targetIds =
      validSelectedIds.length > 0
        ? validSelectedIds
        : selectedTorrentId != null && activeIds.has(selectedTorrentId)
          ? [selectedTorrentId]
          : [];

    if (targetIds.length === 0) {
      clearSelection();
      return;
    }

    const singleTorrent =
      targetIds.length === 1
        ? torrents.find((t) => t.id === targetIds[0])
        : undefined;

    setDeleteModalState({
      isOpen: true,
      torrent: singleTorrent,
      torrentName: singleTorrent?.name,
      count: targetIds.length,
      targetIds,
    });
  };

  const handleConfirmDelete = async (deleteFiles: boolean) => {
    const targetIds =
      deleteModalState.targetIds && deleteModalState.targetIds.length > 0
        ? deleteModalState.targetIds
        : deleteModalState.torrent
          ? [deleteModalState.torrent.id]
          : Array.from(selectedIds);

    if (targetIds.length === 0) {
      setDeleteModalState({ isOpen: false });
      return;
    }

    setBulkPending(true);
    try {
      if (targetIds.length === 1) {
        onDelete({ id: targetIds[0], deleteFiles });
      } else {
        const res = await bulkAction.mutateAsync({
          torrentIds: targetIds,
          action: "delete",
          deleteFiles,
        });
        const succeeded = res.succeededIds ?? targetIds;
        succeeded.forEach((id) => removeTorrent(id));
        trackBulkAction("delete", succeeded.length);
        if (deleteFiles) {
          showToast(
            t(
              "torrents.bulkDeleteFilesSuccess",
              { count: succeeded.length },
              `${succeeded.length} torrents and files deleted`,
            ),
            "info",
          );
        } else {
          showToast(
            t(
              "torrents.bulkDeleteSuccess",
              { count: succeeded.length },
              `${succeeded.length} torrents removed`,
            ),
            "info",
          );
        }
        clearSelection();
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
        queryClient.invalidateQueries({ queryKey: ["diskspace"] });
      }
    } catch (err: unknown) {
      showToast(
        getErrorMessage(
          err,
          t("torrents.failedToDelete", "Failed to delete torrent"),
        ),
        "error",
      );
    } finally {
      setBulkPending(false);
      setDeleteModalState({ isOpen: false });
    }
  };

  const moveTorrentQueue = useMoveTorrentQueue();

  const handleBulkMoveQueue = async (
    position: "top" | "up" | "down" | "bottom",
  ) => {
    const activeIds = new Set(torrents.map((t) => t.id));
    const validSelectedIds = Array.from(selectedIds).filter((id) =>
      activeIds.has(id),
    );
    if (validSelectedIds.length === 0) return;

    const orderedIds = torrents
      .filter((t) => validSelectedIds.includes(t.id))
      .map((t) => t.id);

    if (position === "down" || position === "bottom") {
      orderedIds.reverse();
    }

    trackQueueMove(position, validSelectedIds.length);
    setBulkPending(true);
    try {
      for (const id of orderedIds) {
        await moveTorrentQueue.mutateAsync({ id, position });
      }
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
        torrents={torrents}
        filter={filter}
        onFilterChange={setFilter}
        viewMode={viewMode}
        onViewModeChange={(m) => {
          setViewMode(m);
          trackViewModeChange(m);
        }}
        onAddTorrent={onOpenAddModal}
        onImportPackage={() => setShowImportPackageModal(true)}
        onSearchIndexers={onOpenSearchModal}
        onStartAll={handleStartAll}
        onStopAll={handleStopAll}
        selectedCount={
          selectedIds.size > 0
            ? selectedIds.size
            : selectedTorrentId != null
              ? 1
              : 0
        }
        bulkPending={bulkPending}
        onBulkStart={handleBulkStart}
        onBulkStop={handleBulkStop}
        onBulkDelete={handleBulkDelete}
        onBulkClear={clearSelection}
        onBulkAddTags={handleBulkAddTags}
        onBulkRemoveTags={handleBulkRemoveTags}
        onBulkMoveQueue={handleBulkMoveQueue}
        showQuickSettings={showQuickSettings}
        onToggleQuickSettings={handleToggleQuickSettings}
        isFilterCollapsed={isFilterCollapsed}
        onToggleFilter={toggleFilter}
        visibleColumns={visibleColumns}
        onToggleColumn={toggleColumn}
        onResetColumns={resetColumns}
        onResetSort={resetSort}
        onSelectAllColumns={selectAllColumns}
        onDeselectAllColumns={deselectAllColumns}
        onApplyColumnPreset={applyColumnPreset}
        onToggleCategoryColumns={toggleCategoryColumns}
        isColumnCustomizerOpen={isColumnCustomizerOpen}
        onToggleColumnCustomizer={() =>
          setIsColumnCustomizerOpen((prev) => !prev)
        }
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
            selectedTag={selectedTag}
            onSelectTag={setSelectedTag}
            selectedTagIds={selectedTagIds}
            onToggleTag={toggleTag}
            tagMatchMode={tagMatchMode}
            onTagMatchModeChange={setTagMatchMode}
            onClearTags={clearTags}
            untaggedCount={untaggedCount}
            tagGroups={tagGroups}
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
                  selectedTagIds={selectedTagIds}
                  tagMatchMode={tagMatchMode}
                  selectedTag={selectedTag}
                  selectedId={currentSelectedTorrent?.id ?? null}
                  onSelect={(t) => setSelectedTorrentId(t ? t.id : null)}
                  onPause={onPause}
                  onResume={onResume}
                  onDelete={handleRequestDelete}
                  selectedIds={selectedIds}
                  onToggleSelect={handleToggleSelect}
                  onSelectAll={handleSelectAll}
                  onSearchIndexers={onOpenSearchModal}
                  onNavigateTab={onNavigateTab}
                  visibleColumns={visibleColumns}
                  onToggleColumn={toggleColumn}
                  columnOrder={columnOrder}
                  onColumnOrderChange={setColumnOrder}
                  columnWidths={columnWidths}
                  onColumnWidthsChange={setColumnWidths}
                />
              ) : (
                <TorrentGrid
                  torrents={torrents}
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  privacyFilter={selectedPrivacy}
                  selectedTagIds={selectedTagIds}
                  tagMatchMode={tagMatchMode}
                  selectedTag={selectedTag}
                  selectedId={currentSelectedTorrent?.id ?? null}
                  onSelect={(t) => setSelectedTorrentId(t ? t.id : null)}
                  onPause={onPause}
                  onResume={onResume}
                  onDelete={handleRequestDelete}
                />
              )}
            </div>
            {currentSelectedTorrent && (
              <TorrentDetailPanel
                torrent={currentSelectedTorrent}
                torrentId={currentSelectedTorrent.id}
                onClose={() => setSelectedTorrentId(null)}
                onResume={onResume}
                onPause={onPause}
                onDelete={(id) => handleRequestDelete({ id })}
              />
            )}
          </div>
        </div>
      </div>
      <DeleteTorrentModal
        isOpen={deleteModalState.isOpen}
        torrent={deleteModalState.torrent}
        torrentName={deleteModalState.torrentName}
        count={deleteModalState.count}
        isPending={bulkPending}
        onConfirm={handleConfirmDelete}
        onCancel={() => setDeleteModalState({ isOpen: false })}
      />
      {bulkTagModalState?.isOpen && (
        <BulkTagModal
          isOpen={bulkTagModalState.isOpen}
          mode={bulkTagModalState.mode}
          selectedCount={selectedIds.size}
          tags={tags}
          isPending={bulkPending}
          onClose={() => {
            if (!bulkPending) setBulkTagModalState(null);
          }}
          onConfirm={handleConfirmBulkTag}
        />
      )}
      {showImportPackageModal && (
        <ImportPackageModal
          isOpen={showImportPackageModal}
          onClose={() => setShowImportPackageModal(false)}
        />
      )}
    </div>
  );
};

export default TorrentIndex;
