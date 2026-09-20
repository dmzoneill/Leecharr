import React, { useState, useMemo, useRef, useEffect } from "react";
import { useTranslation } from "../../i18n";
import useEscapeKey from "../../hooks/useEscapeKey";
import {
  ALL_COLUMNS,
  COLUMN_CATEGORIES,
  COLUMN_I18N_KEYS,
  ColumnDef,
  ColumnCategory,
  PresetName,
} from "./columnPreferences";

export interface ColumnCustomizerModalProps {
  isOpen: boolean;
  onClose: () => void;
  visibleColumns: Set<string>;
  onToggleColumn: (key: string) => void;
  onResetToDefaults?: () => void;
  onResetSort?: () => void;
  onSelectAll?: () => void;
  onDeselectAll?: () => void;
  onApplyPreset?: (preset: PresetName) => void;
  onToggleCategory?: (category: ColumnCategory, enable?: boolean) => void;
  allColumns?: ReadonlyArray<ColumnDef>;
}

export function ColumnCustomizerModal({
  isOpen,
  onClose,
  visibleColumns,
  onToggleColumn,
  onResetToDefaults,
  onResetSort,
  onSelectAll,
  onDeselectAll,
  onApplyPreset,
  onToggleCategory,
  allColumns = ALL_COLUMNS,
}: ColumnCustomizerModalProps) {
  const { t } = useTranslation();
  const [search, setSearch] = useState("");
  const modalRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);

  useEscapeKey(onClose, isOpen);

  useEffect(() => {
    if (isOpen) {
      setSearch("");
      const timer = setTimeout(() => {
        searchInputRef.current?.focus();
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  // Translate column labels for display and searching
  const columnsWithLabels = useMemo(() => {
    return allColumns.map((col) => {
      const i18nKey =
        COLUMN_I18N_KEYS[col.key as keyof typeof COLUMN_I18N_KEYS] ||
        `torrents.table.${col.key}`;
      const localizedLabel = t(i18nKey, col.label);
      return {
        ...col,
        displayLabel: localizedLabel,
      };
    });
  }, [allColumns, t]);

  // Filter columns based on search input
  const filteredColumns = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return columnsWithLabels;
    return columnsWithLabels.filter(
      (col) =>
        col.displayLabel.toLowerCase().includes(query) ||
        col.key.toLowerCase().includes(query) ||
        col.label.toLowerCase().includes(query),
    );
  }, [columnsWithLabels, search]);

  // Group filtered columns by category
  const groupedColumns = useMemo(() => {
    const groups: {
      category: (typeof COLUMN_CATEGORIES)[number];
      columns: typeof filteredColumns;
      totalInCategory: number;
      selectedInCategory: number;
    }[] = [];

    for (const cat of COLUMN_CATEGORIES) {
      const inCat = filteredColumns.filter((col) => col.category === cat.id);
      const allInCat = allColumns.filter((col) => col.category === cat.id);
      const selectedInCat = allInCat.filter((col) =>
        visibleColumns.has(col.key),
      ).length;

      if (inCat.length > 0) {
        groups.push({
          category: cat,
          columns: inCat,
          totalInCategory: allInCat.length,
          selectedInCategory: selectedInCat,
        });
      }
    }
    return groups;
  }, [filteredColumns, allColumns, visibleColumns]);

  if (!isOpen) return null;

  const totalColumns = allColumns.length;
  const visibleCount = allColumns.filter((col) =>
    visibleColumns.has(col.key),
  ).length;

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const handleToggleGroup = (catId: ColumnCategory) => {
    if (onToggleCategory) {
      onToggleCategory(catId);
    } else {
      // Fallback if onToggleCategory not provided
      const catCols = allColumns.filter((col) => col.category === catId);
      const allSelected = catCols.every((col) => visibleColumns.has(col.key));
      for (const col of catCols) {
        if (allSelected && visibleColumns.has(col.key)) {
          onToggleColumn(col.key);
        } else if (!allSelected && !visibleColumns.has(col.key)) {
          onToggleColumn(col.key);
        }
      }
    }
  };

  return (
    <div
      ref={modalRef}
      className="modal-overlay column-customizer-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="column-customizer-title"
    >
      <div
        className="modal column-customizer-modal"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: "680px", width: "95%" }}
      >
        <div className="column-customizer-header">
          <div>
            <h2
              id="column-customizer-title"
              className="modal-title"
              style={{ margin: 0 }}
            >
              {t("torrents.columnCustomizer.title", "Customize Columns")}
            </h2>
            <div className="column-customizer-subtitle">
              {t(
                "torrents.columnCustomizer.count",
                `${visibleCount} of ${totalColumns} columns visible`,
                { visible: visibleCount, total: totalColumns },
              )}
            </div>
          </div>
          <button
            type="button"
            className="btn btn-small btn-outline column-customizer-close-btn"
            onClick={onClose}
            title={t("common.close", "Close")}
            aria-label={t("common.close", "Close")}
          >
            ✕
          </button>
        </div>

        {/* Toolbar: Search and Presets */}
        <div className="column-customizer-controls">
          <div className="column-customizer-search-wrapper">
            <input
              ref={searchInputRef}
              type="text"
              className="search-input column-customizer-search"
              placeholder={t(
                "torrents.columnCustomizer.searchPlaceholder",
                "Filter columns...",
              )}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              aria-label="Filter columns by name"
            />
            {search && (
              <button
                type="button"
                className="column-customizer-search-clear"
                onClick={() => setSearch("")}
                title="Clear filter"
                aria-label="Clear filter"
              >
                ✕
              </button>
            )}
          </div>

          <div className="column-customizer-presets">
            <span className="column-customizer-presets-label">
              {t("torrents.columnCustomizer.presets", "Presets:")}
            </span>
            <button
              type="button"
              className="btn btn-small btn-outline preset-btn"
              onClick={() =>
                onApplyPreset ? onApplyPreset("default") : onResetToDefaults?.()
              }
              title="Reset to default 11 columns"
            >
              {t("torrents.columnCustomizer.presetDefault", "Default")}
            </button>
            <button
              type="button"
              className="btn btn-small btn-outline preset-btn"
              onClick={() => onApplyPreset?.("compact")}
              title="Essential columns only"
            >
              {t("torrents.columnCustomizer.presetCompact", "Compact")}
            </button>
            <button
              type="button"
              className="btn btn-small btn-outline preset-btn"
              onClick={() =>
                onApplyPreset ? onApplyPreset("all") : onSelectAll?.()
              }
              title="Enable all 39 columns"
            >
              {t("torrents.columnCustomizer.presetAll", "All")}
            </button>
          </div>
        </div>

        {/* Column list grouped by category */}
        <div className="column-customizer-body">
          {groupedColumns.length === 0 ? (
            <div className="column-customizer-empty">
              {t(
                "torrents.columnCustomizer.noMatches",
                `No columns match "${search}"`,
                { query: search },
              )}
            </div>
          ) : (
            groupedColumns.map((group) => {
              const categoryLabel = t(
                group.category.i18nKey,
                group.category.label,
              );
              const allInGroupSelected =
                group.selectedInCategory === group.totalInCategory;

              return (
                <div
                  key={group.category.id}
                  className="column-customizer-group"
                  data-category={group.category.id}
                >
                  <div className="column-customizer-group-header">
                    <div className="column-customizer-group-title">
                      <span>{categoryLabel}</span>
                      <span className="column-customizer-group-count">
                        ({group.selectedInCategory}/{group.totalInCategory})
                      </span>
                    </div>
                    <button
                      type="button"
                      className="column-customizer-group-toggle"
                      onClick={() => handleToggleGroup(group.category.id)}
                      title={
                        allInGroupSelected
                          ? `Deselect all in ${categoryLabel}`
                          : `Select all in ${categoryLabel}`
                      }
                    >
                      {allInGroupSelected
                        ? t("common.deselectAll", "Deselect")
                        : t("common.selectAll", "Select all")}
                    </button>
                  </div>

                  <div className="column-customizer-grid">
                    {group.columns.map((col) => {
                      const isChecked = visibleColumns.has(col.key);
                      // If only 1 column is currently visible and this is it, prevent deselecting it
                      const isLastRemaining = isChecked && visibleCount <= 1;

                      return (
                        <label
                          key={col.key}
                          className={`column-customizer-checkbox-label${
                            isChecked ? " is-active" : ""
                          }${isLastRemaining ? " is-disabled" : ""}`}
                          title={
                            isLastRemaining
                              ? t(
                                  "torrents.columnCustomizer.minOneColumn",
                                  "At least one column must remain visible",
                                )
                              : undefined
                          }
                        >
                          <input
                            type="checkbox"
                            checked={isChecked}
                            disabled={isLastRemaining}
                            onChange={() => onToggleColumn(col.key)}
                            aria-label={col.displayLabel}
                          />
                          <span className="column-name">
                            {col.displayLabel}
                          </span>
                        </label>
                      );
                    })}
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* Modal Actions Footer */}
        <div className="column-customizer-footer">
          <div className="column-customizer-footer-left">
            {onResetToDefaults && (
              <button
                type="button"
                className="btn btn-small btn-outline"
                onClick={onResetToDefaults}
                title="Reset visible columns to defaults"
              >
                {t(
                  "torrents.columnCustomizer.resetDefaults",
                  "Reset to Defaults",
                )}
              </button>
            )}
            {onResetSort && (
              <button
                type="button"
                className="btn btn-small btn-outline"
                onClick={onResetSort}
                title="Reset table sort order"
              >
                {t("torrents.columnCustomizer.resetSort", "Reset Sort")}
              </button>
            )}
            {onSelectAll && (
              <button
                type="button"
                className="btn btn-small btn-outline"
                onClick={onSelectAll}
                title="Select all columns"
              >
                {t("common.selectAll", "Select All")}
              </button>
            )}
            {onDeselectAll && (
              <button
                type="button"
                className="btn btn-small btn-outline"
                onClick={onDeselectAll}
                title="Deselect all except Name"
              >
                {t("common.deselectAll", "Deselect All")}
              </button>
            )}
          </div>

          <div className="column-customizer-footer-right">
            <button
              type="button"
              className="btn btn-success column-customizer-done-btn"
              onClick={onClose}
            >
              {t("common.done", "Done")}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export default ColumnCustomizerModal;
