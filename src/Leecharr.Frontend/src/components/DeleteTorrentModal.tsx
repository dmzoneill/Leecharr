import React, { useState, useEffect } from "react";
import { useEscapeKey } from "../hooks/useEscapeKey";
import { useTranslation } from "../i18n";
import type { Torrent } from "../api/types";

export interface DeleteTorrentModalProps {
  isOpen: boolean;
  torrent?: Torrent | null;
  count?: number;
  onConfirm: (deleteFiles: boolean) => void;
  onCancel: () => void;
}

export function DeleteTorrentModal({
  isOpen,
  torrent,
  count,
  onConfirm,
  onCancel,
}: DeleteTorrentModalProps) {
  const { t } = useTranslation();
  const [deleteFiles, setDeleteFiles] = useState(false);

  useEscapeKey(onCancel, isOpen);

  useEffect(() => {
    if (isOpen) {
      setDeleteFiles(false);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const isBulk = typeof count === "number" && count > 1;
  const title = isBulk
    ? t("torrents.toolbar.bulkDeleteTitle", { count })
    : t("torrents.contextMenu.removeTorrent");

  const message = isBulk
    ? t("torrents.toolbar.bulkDeleteConfirm", { count })
    : t("torrents.contextMenu.removeTorrentConfirm", {
        name: torrent?.name || "",
      });

  const handleConfirm = () => {
    onConfirm(deleteFiles);
  };

  return (
    <div
      className="modal-overlay"
      onClick={onCancel}
      role="dialog"
      aria-modal="true"
      aria-labelledby="delete-torrent-modal-title"
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(10, 11, 18, 0.85)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 10000,
        padding: "1rem",
        animation: "fadeIn 0.15s ease-out",
      }}
    >
      <div
        className="modal-content"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: "480px",
          backgroundColor: "var(--bg-secondary, #171b35)",
          borderRadius: "12px",
          border: "1px solid rgba(230, 57, 70, 0.45)",
          boxShadow: "0 20px 50px rgba(0, 0, 0, 0.75)",
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
          padding: "1.5rem",
          animation: "scaleUp 0.15s ease-out",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.75rem",
            marginBottom: "0.85rem",
          }}
        >
          <div
            style={{
              width: "36px",
              height: "36px",
              borderRadius: "8px",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              backgroundColor: "rgba(230, 57, 70, 0.15)",
              color: "#e63946",
              fontSize: "1.2rem",
              flexShrink: 0,
            }}
          >
            ⚠️
          </div>
          <h3
            id="delete-torrent-modal-title"
            style={{
              margin: 0,
              fontSize: "1.15rem",
              fontWeight: 600,
              color: "var(--text-primary, #f8f4ed)",
            }}
          >
            {title}
          </h3>
        </div>

        {/* Message */}
        <div
          style={{
            fontSize: "0.95rem",
            color: "var(--text-secondary, #c7c5d3)",
            lineHeight: 1.5,
            marginBottom: "1.25rem",
            wordBreak: "break-word",
          }}
        >
          {message}
        </div>

        {/* Checkbox for deleteFiles */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.65rem",
            marginBottom: "1.5rem",
            padding: "0.75rem",
            backgroundColor: "rgba(0, 0, 0, 0.2)",
            borderRadius: "6px",
            border: "1px solid rgba(255, 255, 255, 0.05)",
          }}
        >
          <input
            type="checkbox"
            id="deleteFilesCheckbox"
            checked={deleteFiles}
            onChange={(e) => setDeleteFiles(e.target.checked)}
            style={{
              cursor: "pointer",
              accentColor: "#e63946",
              width: "16px",
              height: "16px",
            }}
          />
          <label
            htmlFor="deleteFilesCheckbox"
            style={{
              color: "var(--text-primary, #f8f4ed)",
              fontSize: "0.9rem",
              cursor: "pointer",
              userSelect: "none",
            }}
          >
            {t("torrents.contextMenu.removeTorrentAndDeleteFiles") ||
              "Also delete downloaded files from disk"}
          </label>
        </div>

        {/* Actions */}
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.75rem",
          }}
        >
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onCancel}
            style={{ minWidth: "90px" }}
          >
            {t("common.cancel")}
          </button>
          <button
            type="button"
            className="btn btn-danger"
            onClick={handleConfirm}
            style={{ minWidth: "90px" }}
            autoFocus
          >
            {t("common.delete")}
          </button>
        </div>
      </div>
    </div>
  );
}

export default DeleteTorrentModal;
