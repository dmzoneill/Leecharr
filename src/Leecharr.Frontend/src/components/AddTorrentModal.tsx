import React, { useRef } from "react";
import { useTranslation } from "../i18n";
import AddTorrentForm, { InputMode } from "./AddTorrentForm";
import { useFocusTrap } from "../hooks/useFocusTrap";
import { useModalRegistration } from "./ModalProvider";
import { usePermissions } from "../hooks/usePermissions";

export interface AddTorrentModalProps {
  isOpen?: boolean;
  initialMode?: InputMode;
  initialQuery?: string;
  onClose: () => void;
  onSuccess?: () => void;
}

export function AddTorrentModal({
  isOpen = true,
  initialMode = "file",
  initialQuery = "",
  onClose,
  onSuccess,
}: AddTorrentModalProps) {
  const { t } = useTranslation();
  const { canAddTorrent } = usePermissions();
  const modalRef = useRef<HTMLDivElement>(null);

  useModalRegistration({
    id: "add-torrent-modal",
    isOpen,
    onClose,
    modalRef,
  });

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onClose,
  });

  const setContainerRef = (el: HTMLDivElement | null) => {
    (modalRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
    (trapRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
  };

  if (!isOpen) return null;

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  return (
    <div
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="add-torrent-modal-title"
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "var(--overlay, rgba(10, 11, 18, 0.85))",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 9999,
        padding: "1rem",
      }}
    >
      <div
        ref={setContainerRef}
        className="modal-content"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: "720px",
          maxHeight: "90vh",
          backgroundColor: "var(--bg-secondary)",
          borderRadius: "12px",
          border: "1px solid var(--border-light)",
          boxShadow: "0 20px 50px rgba(0, 0, 0, 0.6)",
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
          padding: "1.5rem",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
            paddingBottom: "0.75rem",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <h2
            id="add-torrent-modal-title"
            style={{
              margin: 0,
              fontSize: "1.25rem",
              color: "var(--text-primary)",
            }}
          >
            {t("addTorrent.title", undefined, "Add New Torrent")}
          </h2>
          <button
            type="button"
            className="modal-close"
            onClick={onClose}
            aria-label={t(
              "addTorrent.closeDialog",
              undefined,
              "Close add torrent dialog",
            )}
            title={t("common.close", undefined, "Close dialog")}
            style={{
              background: "none",
              border: "none",
              fontSize: "1.5rem",
              color: "var(--text-muted)",
              cursor: "pointer",
              lineHeight: 1,
            }}
          >
            &times;
          </button>
        </div>

        {!canAddTorrent ? (
          <div style={{ padding: "1rem 0" }}>
            <div
              role="alert"
              style={{
                padding: "0.75rem 1rem",
                backgroundColor: "rgba(239, 68, 68, 0.1)",
                border: "1px solid rgba(239, 68, 68, 0.3)",
                borderRadius: "6px",
                color: "#fca5a5",
                fontSize: "0.875rem",
              }}
            >
              {t(
                "addTorrent.readOnlyWarning",
                undefined,
                "🔒 You have ReadOnly permissions. Adding torrents is not permitted.",
              )}
            </div>
            <div
              style={{
                marginTop: "1rem",
                display: "flex",
                justifyContent: "flex-end",
              }}
            >
              <button className="btn btn-outline" onClick={onClose}>
                {t("common.close", undefined, "Close")}
              </button>
            </div>
          </div>
        ) : (
          <div
            style={{
              flex: "1 1 auto",
              minHeight: 0,
              overflow: "hidden",
              display: "flex",
              flexDirection: "column",
            }}
          >
            <AddTorrentForm
              initialMode={initialMode}
              initialQuery={initialQuery}
              isModal={true}
              onClose={onClose}
              onSuccess={onSuccess}
            />
          </div>
        )}
      </div>
    </div>
  );
}

export default AddTorrentModal;
