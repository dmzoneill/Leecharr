import { useTranslation } from "../i18n";
import { useState } from "react";
import type { Backup } from "../api/types";
import {
  useBackups,
  useCreateBackup,
  useDeleteBackup,
  useRestoreBackup,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { useEscapeKey } from "../hooks/useEscapeKey";
import { formatBytes, formatDate } from "../utils/formatters";

function BackupIcon() {
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
      <polyline points="17 8 12 3 7 8" />
      <line x1="12" y1="3" x2="12" y2="15" />
    </svg>
  );
}

function RestoreIcon() {
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
      <polyline points="1 4 1 10 7 10" />
      <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
    </svg>
  );
}

function DownloadIcon() {
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

function SystemBackup() {
  const { t } = useTranslation();
  const { data: backups, isLoading, isError } = useBackups();
  const createBackup = useCreateBackup();
  const deleteBackup = useDeleteBackup();
  const restoreBackup = useRestoreBackup();
  const { showToast } = useToast();

  const [confirmDelete, setConfirmDelete] = useState<number | null>(null);
  const [confirmRestore, setConfirmRestore] = useState<Backup | null>(null);

  useEscapeKey(() => setConfirmDelete(null), confirmDelete !== null);
  useEscapeKey(() => setConfirmRestore(null), confirmRestore !== null);

  const handleCreateBackup = () => {
    createBackup.mutate(undefined, {
      onSuccess: () => showToast("Backup created successfully", "success"),
      onError: () => showToast("Failed to create backup", "error"),
    });
  };

  const handleDeleteBackup = (id: number) => {
    deleteBackup.mutate(id, {
      onSuccess: () => {
        showToast("Backup deleted", "success");
        setConfirmDelete(null);
      },
      onError: () => showToast("Failed to delete backup", "error"),
    });
  };

  const handleRestoreBackup = async (backup: Backup) => {
    try {
      await restoreBackup.mutateAsync({
        backupId: backup.id,
        fileName: backup.name,
      });
      showToast("Backup restored. Restart required.", "info");
      setConfirmRestore(null);
    } catch {
      showToast("Failed to restore backup", "error");
    }
  };

  return (
    <div className="content-area">
      {/* Page Header */}
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
              {t("system.backupTitle")}
            </h1>
            <span className="badge badge-primary">{t("system.snapshots")}</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("system.backupsSubtitle")}
          </div>
        </div>

        <button
          className="btn btn-primary btn-small"
          onClick={handleCreateBackup}
          disabled={createBackup.isPending}
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: "0.4rem",
          }}
        >
          <BackupIcon />
          <span>
            {createBackup.isPending
              ? "Creating Backup..."
              : t("system.createBackup")}
          </span>
        </button>
      </div>

      {isLoading && <p className="loading">{t("system.loadingBackups")}</p>}
      {!isLoading && isError && (
        <p className="error">{t("system.failedToLoadBackups")}</p>
      )}

      {/* Backups List Card */}
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
        <div
          style={{
            padding: "1.1rem 1.25rem 0.85rem",
            borderBottom: "1px solid rgba(255, 255, 255, 0.06)",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              margin: 0,
            }}
          >
            {t("system.storedBackups")}
          </h2>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("system.storedBackupsSubtitle")}
          </div>
        </div>

        {backups && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">
                    {t("system.archiveName")}
                  </th>
                  <th className="torrent-table-th">{t("system.fileSize")}</th>
                  <th className="torrent-table-th">
                    {t("system.creationDate")}
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    {t("common.actions")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {backups.length === 0 && (
                  <tr>
                    <td colSpan={4} className="torrent-table-empty">
                      {t("system.noBackupsFound")}
                    </td>
                  </tr>
                )}
                {backups.map((backup) => (
                  <tr key={backup.id} className="torrent-table-row">
                    <td>
                      <a
                        href={`${typeof window !== "undefined" && (window as any).Leecharr?.urlBase ? (window as any).Leecharr.urlBase.replace(/\/+$/, "") : ""}/api/v1/backup/${backup.id}/download`}
                        className="torrent-link"
                        download
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.4rem",
                          fontWeight: 500,
                        }}
                      >
                        <DownloadIcon /> {backup.name}
                      </a>
                    </td>
                    <td>{formatBytes(backup.size)}</td>
                    <td>{formatDate(backup.time)}</td>
                    <td style={{ textAlign: "right" }}>
                      <div
                        className="torrent-actions"
                        style={{ display: "inline-flex", gap: "0.4rem" }}
                      >
                        <button
                          className="btn btn-small btn-outline"
                          onClick={() => setConfirmRestore(backup)}
                          title={t("system.restoreSnapshot")}
                          disabled={restoreBackup.isPending}
                        >
                          <RestoreIcon />
                        </button>
                        <button
                          className="btn btn-small btn-danger"
                          onClick={() => setConfirmDelete(backup.id)}
                          title={t("system.deleteSnapshot")}
                          disabled={deleteBackup.isPending}
                        >
                          <TrashIcon />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Delete Confirmation Modal */}
      {confirmDelete !== null && (
        <div className="modal-overlay" onClick={() => setConfirmDelete(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 450,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid rgba(255, 255, 255, 0.12)",
            }}
          >
            <h3
              className="modal-title"
              style={{ fontSize: "1.15rem", marginBottom: "0.75rem" }}
            >
              {t("system.deleteBackupSnapshot")}
            </h3>
            <p
              style={{
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                marginBottom: "1.25rem",
                lineHeight: 1.5,
              }}
            >
              {t("system.confirmDeleteBackup", {
                name: backups?.find((b) => b.id === confirmDelete)?.name || "",
              })}
            </p>
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                className="btn btn-outline btn-small"
                onClick={() => setConfirmDelete(null)}
              >
                {t("common.cancel")}
              </button>
              <button
                className="btn btn-danger btn-small"
                onClick={() => handleDeleteBackup(confirmDelete)}
                disabled={deleteBackup.isPending}
              >
                {deleteBackup.isPending ? "Deleting..." : t("common.delete")}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Restore Confirmation Modal */}
      {confirmRestore !== null && (
        <div className="modal-overlay" onClick={() => setConfirmRestore(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 500,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid rgba(255, 255, 255, 0.12)",
            }}
          >
            <h3
              className="modal-title"
              style={{ fontSize: "1.15rem", marginBottom: "0.75rem" }}
            >
              {t("system.restoreBackupSnapshot")}
            </h3>
            <div
              style={{
                padding: "0.75rem 1rem",
                borderRadius: "6px",
                backgroundColor: "rgba(220, 53, 69, 0.15)",
                border: "1px solid rgba(220, 53, 69, 0.35)",
                color: "var(--danger, #dc3545)",
                fontSize: "0.85rem",
                marginBottom: "1rem",
                lineHeight: 1.4,
              }}
            >
              {t("system.warningRestore")}
            </div>
            <p
              style={{
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                marginBottom: "1.25rem",
                lineHeight: 1.5,
              }}
            >
              {t("system.confirmRestore", { name: confirmRestore.name })}
            </p>
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                className="btn btn-outline btn-small"
                onClick={() => setConfirmRestore(null)}
              >
                {t("common.cancel")}
              </button>
              <button
                className="btn btn-danger btn-small"
                onClick={() => handleRestoreBackup(confirmRestore)}
                disabled={restoreBackup.isPending}
              >
                {restoreBackup.isPending ? "Restoring..." : "Confirm & Restore"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemBackup;
