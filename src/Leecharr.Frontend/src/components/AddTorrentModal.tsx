import React from "react";
import { useTranslation } from "../i18n";
import AddTorrentForm, { InputMode } from "./AddTorrentForm";
import { useFocusTrap } from "../hooks/useFocusTrap";

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
  const trapRef = useFocusTrap<HTMLDivElement>({ isOpen, onClose });

  if (!isOpen) return null;

  return (
    <div
      className="modal-overlay"
      onClick={onClose}
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
        ref={trapRef}
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
            {t("addTorrent.title")}
          </h2>
          <button
            type="button"
            className="modal-close"
            onClick={onClose}
            aria-label={t("common.close")}
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
      </div>
    </div>
  );
}

export default AddTorrentModal;

