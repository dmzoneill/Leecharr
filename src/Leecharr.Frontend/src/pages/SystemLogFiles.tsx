import { useTranslation } from "../i18n";
import { useState } from "react";
import { useLogFiles, useClearLogFiles, useSystemStatus } from "../api/hooks";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { useToast } from "../context/ToastContext";
import { ConfirmModal } from "../components/ConfirmModal";

function DownloadIcon() {
  const { t } = useTranslation();

  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="7 10 12 15 17 10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  );
}

function RefreshIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="23 4 23 10 17 10" />
      <polyline points="1 20 1 14 7 14" />
      <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
    </svg>
  );
}

function TrashIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}

function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  const value = bytes / Math.pow(1024, i);
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

function formatRelativeTime(iso: string): string {
  const date = new Date(iso);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  if (diffDay > 0) return `${diffDay} day${diffDay > 1 ? "s" : ""} ago`;
  if (diffHour > 0) return `${diffHour} hour${diffHour > 1 ? "s" : ""} ago`;
  if (diffMin > 0) return `${diffMin} minute${diffMin > 1 ? "s" : ""} ago`;
  return "just now";
}

export interface SystemLogFilesProps {
  embedded?: boolean;
}

export function SystemLogFiles({ embedded = false }: SystemLogFilesProps) {
  const { t } = useTranslation();
  const { data: logFiles, isLoading, error } = useLogFiles();
  const { data: status } = useSystemStatus();
  const clearLogFiles = useClearLogFiles();
  const queryClient = useQueryClient();
  const toast = useToast();

  const [showConfirmClear, setShowConfirmClear] = useState(false);

  const appData = status?.appDataPath || status?.appDataFolder;
  const logPath = appData ? `${appData}/logs` : "{appData}/logs";

  const handleRefresh = () => {
    queryClient.invalidateQueries({ queryKey: ["logfiles"] });
  };

  const handleClear = () => {
    setShowConfirmClear(true);
  };

  const handleConfirmClear = () => {
    setShowConfirmClear(false);
    clearLogFiles.mutate(undefined, {
      onSuccess: () => {
        toast?.showToast(
          t(
            "system.logFilesClearedSuccess",
            "Disk log files successfully cleared.",
          ),
          "success",
        );
      },
      onError: (err) => {
        toast?.showToast(
          t(
            "system.logFilesClearFailed",
            "Failed to clear log files: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const content = (
    <>
      {!embedded && (
        <div
          className="page-header"
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1.25rem",
          }}
        >
          <div className="page-header-group">
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
            >
              <h1 className="page-heading" style={{ margin: 0 }}>
                {t("system.systemLogFiles")}
              </h1>
              <span className="badge badge-primary">
                {t("system.diskFiles")}
              </span>
            </div>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {t("system.logFilesSubtitle")}
            </div>
          </div>

          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <button
              className="btn btn-outline btn-small"
              onClick={handleRefresh}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
              }}
            >
              <RefreshIcon />
              <span>{t("common.refresh")}</span>
            </button>
            <button
              className="btn btn-danger btn-small"
              onClick={handleClear}
              disabled={clearLogFiles.isPending}
              title={t("system.clearAllFiles")}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
              }}
            >
              <TrashIcon />
              <span>
                {clearLogFiles.isPending
                  ? t("system.clearing", "Clearing...")
                  : t("system.clearAllFiles", "Clear All Files")}
              </span>
            </button>
          </div>
        </div>
      )}

      {/* Info Alert Box */}
      <div
        className="card"
        style={{
          display: "flex",
          alignItems: "flex-start",
          gap: "0.75rem",
          padding: "0.85rem 1.15rem",
          marginBottom: "1.25rem",
          borderRadius: "8px",
          backgroundColor: "rgba(200, 168, 78, 0.1)",
          border: "1px solid rgba(200, 168, 78, 0.3)",
          fontSize: "0.85rem",
          color: "var(--text-secondary)",
          lineHeight: 1.5,
        }}
      >
        <span
          style={{
            color: "var(--accent, #c8a84e)",
            fontSize: "1.1rem",
            lineHeight: 1,
          }}
        >
          ℹ️
        </span>
        <div>
          {t("system.logFilesStoredAt")}{" "}
          <code
            style={{
              fontFamily: "monospace",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
            }}
          >
            {logPath}
          </code>
          {t("system.adjustVerbosity")}{" "}
          <Link
            to="/settings/advanced"
            style={{
              color: "var(--accent, #c8a84e)",
              textDecoration: "underline",
            }}
          >
            {t("system.settingsAdvanced")}
          </Link>
          .
        </div>
      </div>

      {isLoading && <p className="loading">{t("system.loadingLogFiles")}</p>}

      {error && (
        <div className="card" style={{ marginBottom: "1rem" }}>
          <p className="error">{t("system.failedToLoadLogFiles")}</p>
        </div>
      )}

      {/* Log Files Table Card */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          border: "1px solid rgba(255, 255, 255, 0.08)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        {logFiles && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">
                    {t("system.logFilename")}
                  </th>
                  <th className="torrent-table-th">
                    {t("system.lastModified")}
                  </th>
                  <th className="torrent-table-th">{t("system.fileSize")}</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    {t("common.download")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {logFiles.length === 0 && (
                  <tr>
                    <td colSpan={4} className="torrent-table-empty">
                      {t("system.noLogFilesOnDisk")}
                    </td>
                  </tr>
                )}
                {logFiles.map((file) => (
                  <tr key={file.filename} className="torrent-table-row">
                    <td>
                      <code
                        style={{
                          fontSize: "0.85rem",
                          color: "var(--text-primary)",
                          fontWeight: 600,
                        }}
                      >
                        {file.filename}
                      </code>
                    </td>
                    <td>{formatRelativeTime(file.lastWriteTime)}</td>
                    <td>{formatFileSize(file.size)}</td>
                    <td style={{ textAlign: "right" }}>
                      <a
                        href={`${typeof window !== "undefined" && (window as any).Leecharr?.urlBase ? (window as any).Leecharr.urlBase.replace(/\/+$/, "") : ""}/api/v1/logfile/${file.filename}`}
                        className="btn btn-outline btn-small"
                        download
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.4rem",
                          textDecoration: "none",
                        }}
                      >
                        <DownloadIcon />
                        <span>{t("common.download")}</span>
                      </a>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <ConfirmModal
        isOpen={showConfirmClear}
        title={t("system.clearAllFiles")}
        message={t(
          "system.clearAllFilesConfirm",
          "Are you sure you want to delete all disk log files? This action cannot be undone.",
        )}
        confirmText={t("system.clearAllFiles", "Clear All Files")}
        cancelText={t("common.cancel", "Cancel")}
        danger={true}
        onConfirm={handleConfirmClear}
        onCancel={() => setShowConfirmClear(false)}
      />
    </>
  );

  if (embedded) {
    return content;
  }

  return <div className="content-area">{content}</div>;
}

export default SystemLogFiles;
