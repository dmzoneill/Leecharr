import React, { useState, useEffect, useRef } from "react";
import { useTranslation } from "../i18n";
import { useModalRegistration } from "./ModalProvider";
import { useFocusTrap } from "../hooks/useFocusTrap";
import { usePermissions } from "../hooks/usePermissions";
import type { Torrent } from "../api/types";

export interface DeleteTorrentModalProps {
  isOpen: boolean;
  torrent?: Torrent | null;
  torrentName?: string;
  count?: number;
  isPending?: boolean;
  onConfirm: (deleteFiles: boolean) => Promise<void> | void;
  onClose?: () => void;
  onCancel?: () => void;
}

export function DeleteTorrentModal({
  isOpen,
  torrent,
  torrentName,
  count = 1,
  isPending = false,
  onConfirm,
  onClose,
  onCancel,
}: DeleteTorrentModalProps) {
  const { t } = useTranslation();
  const { canDeleteTorrent } = usePermissions();
  const [deleteFiles, setDeleteFiles] = useState(false);
  const modalRef = useRef<HTMLDivElement>(null);
  const confirmButtonRef = useRef<HTMLButtonElement>(null);

  const handleClose = () => {
    if (isPending) return;
    if (onClose) {
      onClose();
    } else if (onCancel) {
      onCancel();
    }
  };

  useModalRegistration({
    id: "delete-torrent-modal",
    isOpen,
    onClose: handleClose,
    modalRef,
  });

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onClose: handleClose,
  });

  const setContainerRef = (el: HTMLDivElement | null) => {
    (modalRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
    (trapRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
  };

  useEffect(() => {
    if (isOpen) {
      setDeleteFiles(false);
      const timer = setTimeout(() => {
        confirmButtonRef.current?.focus();
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const effectiveTorrentName = torrentName ?? torrent?.name;
  const isMultiple =
    (typeof count === "number" && count > 1) || (!effectiveTorrentName && count !== 1);

  const title = isMultiple
    ? t("torrents.deleteTorrentsTitle", undefined, "Delete Torrents")
    : t("torrents.deleteTorrentTitle", undefined, "Delete Torrent");

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget && !isPending) {
      handleClose();
    }
  };

  const handleConfirm = async () => {
    if (isPending || !canDeleteTorrent) return;
    await onConfirm(deleteFiles);
  };

  return (
    <div
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="delete-torrent-modal-title"
      aria-describedby="delete-torrent-modal-desc"
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
        ref={setContainerRef}
        className="modal-content modal"
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
            justifyContent: "space-between",
            marginBottom: "0.85rem",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
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
              className="modal-title"
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

          <button
            type="button"
            className="btn btn-outline btn-small"
            onClick={handleClose}
            disabled={isPending}
            aria-label={t("common.close", undefined, "Close")}
            title={t("common.close", undefined, "Close")}
            style={{ padding: "0.2rem 0.5rem", fontSize: "0.8rem" }}
          >
            ✕
          </button>
        </div>

        {/* ReadOnly Permission Warning Banner */}
        {!canDeleteTorrent && (
          <div
            role="alert"
            style={{
              padding: "0.75rem 1rem",
              backgroundColor: "rgba(239, 68, 68, 0.1)",
              border: "1px solid rgba(239, 68, 68, 0.3)",
              borderRadius: "6px",
              marginBottom: "1rem",
              color: "#fca5a5",
              fontSize: "0.875rem",
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>🔒</span>
            <span>
              {t(
                "torrents.readOnlyDeleteWarning",
                undefined,
                "You have ReadOnly permissions. Torrents cannot be deleted.",
              )}
            </span>
          </div>
        )}

        {/* Message */}
        <div
          id="delete-torrent-modal-desc"
          style={{
            padding: "0.75rem 1rem",
            backgroundColor: "var(--danger-bg-alert, rgba(239, 68, 68, 0.12))",
            border: "1px solid var(--danger-border-alert, rgba(239, 68, 68, 0.3))",
            borderRadius: "6px",
            marginBottom: "1.25rem",
            fontSize: "0.875rem",
            color: "var(--text-primary, #fff)",
            lineHeight: 1.5,
          }}
        >
          {isMultiple ? (
            <span>
              {t(
                "torrents.deleteMultipleConfirm",
                { count },
                `Are you sure you want to delete ${count} torrents?`,
              )}
            </span>
          ) : effectiveTorrentName ? (
            <span>
              {t(
                "torrents.deleteSingleConfirm",
                undefined,
                "Are you sure you want to delete",
              )}{" "}
              <strong style={{ wordBreak: "break-all" }}>
                "{effectiveTorrentName}"
              </strong>
              ?
            </span>
          ) : (
            <span>
              {t(
                "torrents.deleteSingleConfirmGeneric",
                undefined,
                "Are you sure you want to delete this torrent?",
              )}
            </span>
          )}
        </div>

        {/* Checkbox for deleteFiles */}
        <div style={{ marginBottom: "1.25rem" }}>
          <label
            htmlFor="deleteFilesCheckbox"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.6rem",
              cursor: isPending ? "not-allowed" : "pointer",
              userSelect: "none",
              fontSize: "0.9rem",
              color: "var(--text-primary, #f8f4ed)",
            }}
          >
            <input
              type="checkbox"
              id="deleteFilesCheckbox"
              checked={deleteFiles}
              onChange={(e) => setDeleteFiles(e.target.checked)}
              disabled={isPending}
              style={{
                cursor: isPending ? "not-allowed" : "pointer",
                width: "1.1rem",
                height: "1.1rem",
                accentColor: "var(--danger, #ef4444)",
              }}
            />
            <span>
              {t(
                "torrents.deleteFilesFromDisk",
                undefined,
                "Also delete downloaded files from disk",
              )}
            </span>
          </label>
          {deleteFiles && (
            <p
              style={{
                margin: "0.4rem 0 0 1.7rem",
                fontSize: "0.8rem",
                color: "var(--danger, #ef4444)",
              }}
            >
              {t(
                "torrents.deleteFilesWarning",
                undefined,
                "All downloaded files associated with this torrent will be permanently deleted.",
              )}
            </p>
          )}
        </div>

        {/* Actions */}
        <div
          className="modal-actions"
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.75rem",
          }}
        >
          <button
            type="button"
            className="btn btn-outline"
            onClick={handleClose}
            disabled={isPending}
            style={{ minWidth: "90px" }}
          >
            {t("common.cancel", undefined, "Cancel")}
          </button>
          <button
            ref={confirmButtonRef}
            type="button"
            className="btn btn-danger"
            onClick={handleConfirm}
            disabled={isPending || !canDeleteTorrent}
            title={
              !canDeleteTorrent
                ? t(
                    "torrents.deletePermissionRequired",
                    undefined,
                    "Deleting torrents requires operator or admin role",
                  )
                : undefined
            }
            style={{
              minWidth: "90px",
              display: "inline-flex",
              alignItems: "center",
              justifyContent: "center",
              gap: "0.5rem",
            }}
          >
            {isPending ? (
              <>
                <span
                  className="spinner"
                  style={{
                    display: "inline-block",
                    width: "0.85rem",
                    height: "0.85rem",
                    border: "2px solid rgba(255,255,255,0.3)",
                    borderTopColor: "#fff",
                    borderRadius: "50%",
                    animation: "spin 0.6s linear infinite",
                  }}
                />
                <span>{t("common.deleting", undefined, "Deleting...")}</span>
              </>
            ) : (
              <span>{t("common.delete", undefined, "Delete")}</span>
            )}
          </button>
        </div>
      </div>
    </div>
  );
}

export default DeleteTorrentModal;
