import { useTranslation } from "../../i18n";
import {
  useReAddHistoryTorrent,
  useDeleteHistoryTorrent,
  useClearDownloadHistory,
  useEnrichHistoryTorrent,
  useEnrichAllHistory,
  useReconcileDownloadHistory,
} from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import { useConfirm } from "../../context/ConfirmContext";
import type { DownloadHistoryEntry } from "../../api/types";

export function useHistoryActions(
  selectedDetailItem: DownloadHistoryEntry | null,
  setSelectedDetailItem: (item: DownloadHistoryEntry | null) => void,
) {
  const { t } = useTranslation();
  const { showToast } = useToast();
  const confirm = useConfirm();

  const reAddMutation = useReAddHistoryTorrent();
  const deleteMutation = useDeleteHistoryTorrent();
  const clearMutation = useClearDownloadHistory();
  const enrichMutation = useEnrichHistoryTorrent();
  const enrichAllMutation = useEnrichAllHistory();
  const reconcileMutation = useReconcileDownloadHistory();

  const handleReAdd = (id: number, title: string) => {
    reAddMutation.mutate(id, {
      onSuccess: () => {
        showToast(
          t(
            "history.reAddedToast",
            'Re-added "{title}" to active seeding library',
            { title },
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t("history.failedToReAdd", 'Failed to re-add "{title}": {error}', {
            title,
            error: err.message || "Unknown error",
          }),
          "error",
        );
      },
    });
  };

  const handleDelete = async (id: number, title: string) => {
    const ok = await confirm({
      title: t("history.deleteHistoryRecord", "Delete History Record"),
      message: t(
        "history.deleteHistoryRecordConfirm",
        'Delete history record for "{title}"?',
        { title },
      ),
      danger: true,
      confirmText: t("common.delete", "Delete"),
    });
    if (!ok) return;

    deleteMutation.mutate(id, {
      onSuccess: () => {
        if (selectedDetailItem?.id === id) {
          setSelectedDetailItem(null);
        }
        showToast(
          t("history.recordRemovedToast", "Historical record removed"),
          "info",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToDeleteRecord",
            "Failed to delete record: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleEnrich = (item: DownloadHistoryEntry) => {
    enrichMutation.mutate(item.id, {
      onSuccess: (updated) => {
        showToast(
          t(
            "history.enrichedMetadataToast",
            'Enriched metadata for "{title}"',
            { title: item.title },
          ),
          "success",
        );
        if (selectedDetailItem?.id === item.id) {
          setSelectedDetailItem(updated);
        }
      },
      onError: (err) => {
        showToast(
          t(
            "history.couldNotEnrichMetadata",
            "Could not enrich metadata: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleEnrichAll = () => {
    enrichAllMutation.mutate(undefined, {
      onSuccess: () => {
        showToast(
          t(
            "history.startedMetadataEnrichment",
            "Started metadata enrichment from connected Arr instances",
          ),
          "info",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToStartEnrichment",
            "Failed to start enrichment: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleReconcile = () => {
    reconcileMutation.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "history.reconciledLibraryToast",
            "Reconciled library and enriched metadata ({count} processed)",
            { count: res.processedCount },
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToReconcileLibrary",
            "Failed to reconcile library: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleClearAll = async () => {
    const ok = await confirm({
      title: t("history.clearDownloadHistory", "Clear Download History"),
      message: t(
        "history.clearDownloadHistoryConfirm",
        "Are you sure you want to clear all download history? This action cannot be undone.",
      ),
      danger: true,
      confirmText: t("common.clearAll", "Clear All"),
    });
    if (!ok) return;

    clearMutation.mutate(undefined, {
      onSuccess: () => {
        setSelectedDetailItem(null);
        showToast(
          t(
            "history.historyClearedSuccess",
            "Download history cleared successfully",
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "history.failedToClearHistory",
            "Failed to clear history: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  return {
    handleReAdd,
    handleDelete,
    handleEnrich,
    handleEnrichAll,
    handleReconcile,
    handleClearAll,
    isReAdding: reAddMutation.isPending,
    isDeleting: deleteMutation.isPending,
    isClearing: clearMutation.isPending,
    isEnriching: enrichMutation.isPending,
    isEnrichingAll: enrichAllMutation.isPending,
    isReconciling: reconcileMutation.isPending,
  };
}
