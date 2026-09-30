import React, { useState, useMemo } from "react";
import { useTranslation } from "../../i18n";
import { useToast } from "../../context/ToastContext";
import { useFocusTrap } from "../../hooks/useFocusTrap";
import { formatBytes, formatRatio, formatDate } from "../../utils/formatters";
import type { DownloadHistoryEntry } from "../../api/types";
import { formatDuration } from "./types";

export interface HistoryExportModalProps {
  isOpen: boolean;
  onClose: () => void;
  items: DownloadHistoryEntry[];
}

export function generateExportData(
  items: DownloadHistoryEntry[],
  format: "json" | "csv" | "txt",
  includeMetadata = true,
): string {
  if (items.length === 0) return "";

  if (format === "json") {
    const records = items.map((item) => {
      const base: Record<string, unknown> = {
        id: item.id,
        title: item.title,
        infoHash: item.infoHash,
        totalSize: item.totalSize,
        formattedSize: formatBytes(item.totalSize),
        uploaded: item.uploaded,
        formattedUploaded: formatBytes(item.uploaded),
        downloaded: item.downloaded,
        ratio: item.ratio,
        formattedRatio: formatRatio(item.ratio),
        seedingTimeSeconds: item.seedingTime,
        formattedSeedingTime: formatDuration(item.seedingTime),
        primaryTracker: item.primaryTracker,
        indexerName: item.indexerName,
        source: item.source,
        magnetUrl: item.magnetUrl,
        isPrivate: item.isPrivate ?? false,
        status: item.status,
        dateAdded: item.dateAdded,
        dateCompleted: item.dateCompleted,
        dateRemoved: item.dateRemoved,
      };

      if (includeMetadata && item.metadata) {
        base.metadata = item.metadata;
      }

      return base;
    });

    return JSON.stringify(records, null, 2);
  } else if (format === "csv") {
    // CSV Format
    const headers = [
      "ID",
      "Title",
      "InfoHash",
      "TotalSize",
      "FormattedSize",
      "Uploaded",
      "Downloaded",
      "Ratio",
      "SeedingTime",
      "Source",
      "PrimaryTracker",
      "Indexer",
      "IsPrivate",
      "MagnetUrl",
      "Status",
      "DateAdded",
      "DateCompleted",
      "DateRemoved",
    ];

    const rows = items.map((item) => {
      const displayTitle = (item.metadata?.title || item.title || "").replace(
        /"/g,
        '""',
      );
      const tracker = (item.primaryTracker || "").replace(/"/g, '""');
      const src = (item.source || "").replace(/"/g, '""');
      const indexer = (item.indexerName || "").replace(/"/g, '""');
      const magnet = (item.magnetUrl || "").replace(/"/g, '""');

      return [
        item.id,
        `"${displayTitle}"`,
        `"${item.infoHash}"`,
        item.totalSize,
        `"${formatBytes(item.totalSize)}"`,
        item.uploaded,
        item.downloaded,
        formatRatio(item.ratio),
        `"${formatDuration(item.seedingTime)}"`,
        `"${src}"`,
        `"${tracker}"`,
        `"${indexer}"`,
        item.isPrivate ? "true" : "false",
        `"${magnet}"`,
        `"${item.status}"`,
        `"${formatDate(item.dateAdded)}"`,
        item.dateCompleted ? `"${formatDate(item.dateCompleted)}"` : '""',
        item.dateRemoved ? `"${formatDate(item.dateRemoved)}"` : '""',
      ].join(",");
    });

    return [headers.join(","), ...rows].join("\n");
  } else {
    // TXT Format
    return items
      .map((item) => {
        const displayTitle = item.metadata?.title || item.title || "";
        const lines = [`Title: ${displayTitle}`, `InfoHash: ${item.infoHash}`];
        if (item.magnetUrl) {
          lines.push(`Magnet: ${item.magnetUrl}`);
        }
        return lines.join("\n");
      })
      .join("\n\n");
  }
}

export const HistoryExportModal: React.FC<HistoryExportModalProps> = ({
  isOpen,
  onClose,
  items,
}) => {
  const { t } = useTranslation();
  const { showToast } = useToast();
  const [exportFormat, setExportFormat] = useState<"json" | "csv" | "txt">(
    "json",
  );
  const [includeMetadata, setIncludeMetadata] = useState(true);

  const trapRef = useFocusTrap<HTMLDivElement>({ isOpen, onClose });

  const exportedData = useMemo(() => {
    if (!isOpen || items.length === 0) return "";
    return generateExportData(items, exportFormat, includeMetadata);
  }, [isOpen, items, exportFormat, includeMetadata]);

  if (!isOpen) return null;

  const handleDownload = () => {
    const mimeType =
      exportFormat === "json"
        ? "application/json"
        : exportFormat === "csv"
          ? "text/csv;charset=utf-8;"
          : "text/plain;charset=utf-8;";
    const blob = new Blob([exportedData], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    const dateStr = new Date().toISOString().split("T")[0];
    link.href = url;
    link.setAttribute(
      "download",
      `leecharr-download-history-${dateStr}.${exportFormat}`,
    );
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);

    showToast(
      t("history.exportDownloaded", "Export downloaded successfully"),
      "success",
    );
  };

  const handleCopyToClipboard = async () => {
    try {
      await navigator.clipboard.writeText(exportedData);
      showToast(
        t("history.copiedToClipboard", "Copied export data to clipboard"),
        "success",
      );
    } catch {
      showToast(
        t("history.copyFailed", "Failed to copy to clipboard"),
        "error",
      );
    }
  };

  const previewSnippet = exportedData.slice(0, 1200);

  return (
    <div
      className="modal-overlay"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-labelledby="history-export-modal-title"
    >
      <div
        ref={trapRef}
        className="modal-content"
        role="button"
        tabIndex={0}
        style={{
          maxWidth: "650px",
          width: "90%",
          padding: "1.5rem",
          borderRadius: "8px",
          backgroundColor: "var(--bg-card)",
          boxShadow: "0 8px 30px rgba(0, 0, 0, 0.4)",
        }}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.stopPropagation();
          }
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1.25rem",
          }}
        >
          <h2
            id="history-export-modal-title"
            style={{ margin: 0, fontSize: "1.25rem", fontWeight: 600 }}
          >
            📊 {t("history.exportHistoryTitle", "Export Download History")}
          </h2>
          <button
            type="button"
            className="btn btn-outline"
            style={{ padding: "0.25rem 0.5rem", fontSize: "0.85rem" }}
            onClick={onClose}
          >
            ✕
          </button>
        </div>

        {/* Export options */}
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "1rem",
            marginBottom: "1.25rem",
          }}
        >
          <div style={{ display: "flex", gap: "1rem", alignItems: "center" }}>
            <span style={{ fontSize: "0.9rem", fontWeight: 500 }}>
              {t("history.exportFormat", "Format:")}
            </span>
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                cursor: "pointer",
              }}
            >
              <input
                type="radio"
                name="exportFormat"
                value="json"
                checked={exportFormat === "json"}
                onChange={() => setExportFormat("json")}
              />
              JSON
            </label>
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                cursor: "pointer",
              }}
            >
              <input
                type="radio"
                name="exportFormat"
                value="csv"
                checked={exportFormat === "csv"}
                onChange={() => setExportFormat("csv")}
              />
              CSV
            </label>
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                cursor: "pointer",
              }}
            >
              <input
                type="radio"
                name="exportFormat"
                value="txt"
                checked={exportFormat === "txt"}
                onChange={() => setExportFormat("txt")}
              />
              TXT
            </label>
          </div>

          {exportFormat === "json" && (
            <label
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.5rem",
                fontSize: "0.85rem",
                cursor: "pointer",
              }}
            >
              <input
                type="checkbox"
                checked={includeMetadata}
                onChange={(e) => setIncludeMetadata(e.target.checked)}
              />
              {t(
                "history.includeEnrichedMetadata",
                "Include enriched media metadata",
              )}
            </label>
          )}

          <div>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginBottom: "0.4rem",
              }}
            >
              {t("history.exportPreview", "Preview ({count} records):", {
                count: items.length,
              })}
            </div>
            <pre
              style={{
                backgroundColor: "var(--bg-primary)",
                border: "1px solid var(--border-light)",
                borderRadius: "6px",
                padding: "0.75rem",
                fontSize: "0.75rem",
                maxHeight: "180px",
                overflowY: "auto",
                fontFamily: "monospace",
                color: "var(--text-secondary)",
                margin: 0,
              }}
            >
              {previewSnippet}
              {exportedData.length > 1200 ? "\n..." : ""}
            </pre>
          </div>
        </div>

        {/* Modal actions */}
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.6rem",
          }}
        >
          <button className="btn btn-outline" onClick={onClose}>
            {t("common.cancel", "Cancel")}
          </button>
          <button className="btn btn-outline" onClick={handleCopyToClipboard}>
            📋 {t("history.copyToClipboard", "Copy")}
          </button>
          <button className="btn btn-primary" onClick={handleDownload}>
            💾{" "}
            {t("history.downloadFile", "Download .{ext}", {
              ext: exportFormat,
            })}
          </button>
        </div>
      </div>
    </div>
  );
};
