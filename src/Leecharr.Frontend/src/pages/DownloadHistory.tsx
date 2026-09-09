import React, { useState, useMemo } from "react";
import { useTranslation } from "../i18n";
import {
  useDownloadHistory,
  useArrConnections,
  useIndexers,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { IndexerSearchModal } from "../components/IndexerSearchModal";
import type { DownloadHistoryEntry } from "../api/types";
import {
  HistoryFilterBar,
  HistoryGridView,
  HistoryTableView,
  HistoryExportModal,
  HistoryDetailModal,
  useHistoryActions,
} from "./downloadhistory";

export default function DownloadHistory() {
  const { t } = useTranslation();
  const { showToast } = useToast();

  const [searchTerm, setSearchTerm] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [startDate, setStartDate] = useState<string>("");
  const [endDate, setEndDate] = useState<string>("");
  const [viewMode, setViewMode] = useState<"grid" | "table">("grid");
  const [searchModalQuery, setSearchModalQuery] = useState<string | null>(null);
  const [selectedDetailItem, setSelectedDetailItem] =
    useState<DownloadHistoryEntry | null>(null);
  const [isExportOpen, setIsExportOpen] = useState(false);

  const { data: arrConnections } = useArrConnections();
  const { data: indexers } = useIndexers();

  const {
    data: history,
    isLoading,
    isError,
  } = useDownloadHistory({
    query: searchTerm.trim() || undefined,
    status: statusFilter !== "all" ? statusFilter : undefined,
  });

  const {
    handleReAdd,
    handleDelete,
    handleEnrich,
    handleEnrichAll,
    handleReconcile,
    handleClearAll,
    isReAdding,
    isClearing,
    isEnriching,
    isEnrichingAll,
    isReconciling,
  } = useHistoryActions(selectedDetailItem, setSelectedDetailItem);

  const filteredHistory = useMemo(() => {
    if (!history) return [];
    if (!startDate && !endDate) return history;

    return history.filter((item) => {
      const addedTime = new Date(item.dateAdded).getTime();
      if (startDate) {
        const start = new Date(startDate).getTime();
        if (addedTime < start) return false;
      }
      if (endDate) {
        const end = new Date(endDate).getTime() + 86400000; // End of day
        if (addedTime > end) return false;
      }
      return true;
    });
  }, [history, startDate, endDate]);

  const totalCount = filteredHistory.length;

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
        boxSizing: "border-box",
      }}
    >
      <HistoryFilterBar
        searchTerm={searchTerm}
        onSearchChange={setSearchTerm}
        statusFilter={statusFilter}
        onStatusFilterChange={setStatusFilter}
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        onReconcile={handleReconcile}
        isReconciling={isReconciling}
        onEnrichAll={handleEnrichAll}
        isEnrichingAll={isEnrichingAll}
        onClearAll={handleClearAll}
        isClearing={isClearing}
        onOpenExport={() => setIsExportOpen(true)}
        totalCount={totalCount}
        startDate={startDate}
        endDate={endDate}
        onStartDateChange={setStartDate}
        onEndDateChange={setEndDate}
      />

      {/* Loading & Error States */}
      {isLoading && (
        <div
          className="card"
          style={{ padding: "3rem", textAlign: "center", borderRadius: "8px" }}
        >
          <div className="loading">{t("history.loading")}</div>
        </div>
      )}

      {isError && (
        <div
          className="card"
          style={{
            padding: "2rem",
            textAlign: "center",
            color: "var(--danger, #dc3545)",
            borderRadius: "8px",
          }}
        >
          {t("history.failedToLoad")}
        </div>
      )}

      {!isLoading && !isError && totalCount === 0 && (
        <div
          className="card empty-state"
          style={{
            padding: "3.5rem 1rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            className="empty-state-title"
            style={{
              fontSize: "1.25rem",
              fontWeight: 600,
              marginBottom: "0.5rem",
            }}
          >
            {t("history.noHistoricalDownloads")}
          </div>
          <div
            className="empty-state-text"
            style={{
              color: "var(--text-muted, #888)",
              maxWidth: "500px",
              margin: "0 auto",
            }}
          >
            {searchTerm || statusFilter !== "all" || startDate || endDate
              ? t("history.noMatchingRecords")
              : t("history.noHistoricalDownloadsDesc")}
          </div>
        </div>
      )}

      {/* Grid View */}
      {!isLoading && !isError && totalCount > 0 && viewMode === "grid" && (
        <HistoryGridView
          items={filteredHistory}
          arrConnections={arrConnections}
          onSelectItem={setSelectedDetailItem}
          onSearchItem={setSearchModalQuery}
          onReAddItem={handleReAdd}
          isReAdding={isReAdding}
          onFilterByGenre={setSearchTerm}
        />
      )}

      {/* Table View */}
      {!isLoading && !isError && totalCount > 0 && viewMode === "table" && (
        <HistoryTableView
          items={filteredHistory}
          arrConnections={arrConnections}
          onSelectItem={setSelectedDetailItem}
          onSearchItem={setSearchModalQuery}
          onReAddItem={handleReAdd}
          isReAdding={isReAdding}
          onDeleteItem={handleDelete}
          onFilterByTracker={setSearchTerm}
        />
      )}

      {/* Media Detail Modal */}
      <HistoryDetailModal
        item={selectedDetailItem}
        onClose={() => setSelectedDetailItem(null)}
        arrConnections={arrConnections}
        indexers={indexers}
        onEnrich={handleEnrich}
        isEnriching={isEnriching}
        onSearch={setSearchModalQuery}
        onReAdd={handleReAdd}
        isReAdding={isReAdding}
        onFilterByTracker={setSearchTerm}
      />

      {/* Export Modal */}
      <HistoryExportModal
        isOpen={isExportOpen}
        onClose={() => setIsExportOpen(false)}
        items={filteredHistory}
      />

      {/* Indexer Search Modal */}
      {searchModalQuery && (
        <IndexerSearchModal
          initialQuery={searchModalQuery}
          onClose={() => setSearchModalQuery(null)}
          onTorrentAdded={() => {
            setSearchModalQuery(null);
            showToast(
              t(
                "history.torrentAddedSuccess",
                "Torrent added to download queue",
              ),
              "success",
            );
          }}
        />
      )}
    </div>
  );
}
