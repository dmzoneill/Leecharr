import React from "react";
import { useTranslation } from "../../i18n";

export interface HistoryFilterBarProps {
  searchTerm: string;
  onSearchChange: (value: string) => void;
  statusFilter: string;
  onStatusFilterChange: (status: string) => void;
  viewMode: "grid" | "table";
  onViewModeChange: (mode: "grid" | "table") => void;
  onReconcile: () => void;
  isReconciling: boolean;
  onEnrichAll: () => void;
  isEnrichingAll: boolean;
  onClearAll: () => void;
  isClearing: boolean;
  onOpenExport?: () => void;
  totalCount: number;
  startDate?: string;
  endDate?: string;
  onStartDateChange?: (date: string) => void;
  onEndDateChange?: (date: string) => void;
}

export const HistoryFilterBar: React.FC<HistoryFilterBarProps> = ({
  searchTerm,
  onSearchChange,
  statusFilter,
  onStatusFilterChange,
  viewMode,
  onViewModeChange,
  onReconcile,
  isReconciling,
  onEnrichAll,
  isEnrichingAll,
  onClearAll,
  isClearing,
  onOpenExport,
  totalCount,
  startDate,
  endDate,
  onStartDateChange,
  onEndDateChange,
}) => {
  const { t } = useTranslation();

  return (
    <>
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexWrap: "wrap",
          gap: "0.75rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <h1 className="page-heading" style={{ margin: 0 }}>
            {t("history.title")} ({totalCount})
          </h1>
        </div>

        <div
          className="page-header-actions"
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {/* View mode toggle */}
          <div className="view-toggle">
            <button
              className={`view-toggle-btn ${viewMode === "grid" ? "active" : ""}`}
              onClick={() => onViewModeChange("grid")}
              title={t("history.postersView")}
            >
              {t("history.postersView")}
            </button>
            <button
              className={`view-toggle-btn ${viewMode === "table" ? "active" : ""}`}
              onClick={() => onViewModeChange("table")}
              title={t("history.tableView")}
            >
              {t("history.tableView")}
            </button>
          </div>

          {onOpenExport && (
            <button
              className="btn btn-outline"
              onClick={onOpenExport}
              disabled={totalCount === 0}
              title={t("history.export", "Export History")}
            >
              📊 {t("history.export", "Export")}
            </button>
          )}

          <button
            className="btn btn-success"
            onClick={onReconcile}
            disabled={isReconciling}
            title={t("history.scanActive")}
          >
            {isReconciling
              ? t("common.loading")
              : "🔄 " + t("history.syncArrMetadata")}
          </button>

          <button
            className="btn btn-outline"
            onClick={onEnrichAll}
            disabled={isEnrichingAll || totalCount === 0}
            title={t("history.syncMetadata")}
          >
            {t("history.syncArrMetadata")}
          </button>

          <button
            className="btn btn-outline"
            onClick={onClearAll}
            disabled={isClearing || totalCount === 0}
            title={t("history.clearAll")}
          >
            {t("history.clearHistory")}
          </button>
        </div>
      </div>

      {/* Filter and search toolbar */}
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
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          flexShrink: 0,
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.4rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {(["all", "Active", "Completed", "Removed"] as const).map((st) => (
            <button
              key={st}
              className={`btn ${statusFilter === st ? "btn-primary" : "btn-outline"}`}
              style={{
                fontSize: "0.82rem",
                padding: "0.35rem 0.85rem",
                borderRadius: "6px",
                fontWeight: 500,
              }}
              onClick={() => onStatusFilterChange(st)}
            >
              {st === "all"
                ? t("common.all", "All")
                : t("torrentStatus." + st.toLowerCase(), st)}
            </button>
          ))}
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            minWidth: "260px",
            flex: "1",
            maxWidth: "520px",
            flexWrap: "wrap",
          }}
        >
          {onStartDateChange && onEndDateChange && (
            <div
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
                fontSize: "0.8rem",
              }}
            >
              <input
                type="date"
                className="form-control"
                value={startDate || ""}
                onChange={(e) => onStartDateChange(e.target.value)}
                style={{
                  padding: "0.35rem 0.5rem",
                  fontSize: "0.78rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light)",
                  backgroundColor: "var(--bg-primary)",
                  color: "inherit",
                }}
                title={t("history.filterStartDate", "From Date")}
              />
              <span>-</span>
              <input
                type="date"
                className="form-control"
                value={endDate || ""}
                onChange={(e) => onEndDateChange(e.target.value)}
                style={{
                  padding: "0.35rem 0.5rem",
                  fontSize: "0.78rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light)",
                  backgroundColor: "var(--bg-primary)",
                  color: "inherit",
                }}
                title={t("history.filterEndDate", "To Date")}
              />
            </div>
          )}

          <div
            style={{
              position: "relative",
              display: "flex",
              flex: 1,
              alignItems: "center",
            }}
          >
            <input
              type="text"
              className="form-control"
              placeholder={t("history.filterPlaceholder")}
              value={searchTerm}
              onChange={(e) => onSearchChange(e.target.value)}
              style={{
                width: "100%",
                padding: "0.4rem 0.75rem",
                borderRadius: "6px",
                border: "1px solid var(--border-light)",
                backgroundColor: "var(--bg-primary)",
                color: "inherit",
                fontSize: "0.85rem",
              }}
            />
            {searchTerm && (
              <button
                className="btn btn-outline"
                onClick={() => onSearchChange("")}
                style={{
                  position: "absolute",
                  right: "4px",
                  fontSize: "0.75rem",
                  padding: "0.2rem 0.4rem",
                  borderRadius: "4px",
                  lineHeight: 1,
                }}
                title={t("history.clearSearchFilter")}
              >
                ✕
              </button>
            )}
          </div>
        </div>
      </div>
    </>
  );
};
