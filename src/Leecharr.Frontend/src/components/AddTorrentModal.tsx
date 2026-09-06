import React from "react";
import AddTorrentForm, { InputMode } from "./AddTorrentForm";
import { useEscapeKey } from "../hooks/useEscapeKey";
import { useTranslation } from "../i18n";

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
  useEscapeKey(onClose, isOpen);

  if (!isOpen) return null;

  return (
    <div
      className="modal-overlay"
      onClick={onClose}
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
        zIndex: 9999,
        padding: "1rem",
      }}
    >
      <div
        className="modal-content"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: "720px",
          maxHeight: "90vh",
          backgroundColor: "var(--bg-secondary, #171b35)",
          borderRadius: "12px",
          border: "1px solid var(--border-light, #1c203b)",
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
            borderBottom: "1px solid var(--border-light, #1c203b)",
          }}
        >
          <h2
            style={{
              margin: 0,
              fontSize: "1.25rem",
              color: "var(--text-primary, #f8f4ed)",
            }}
          >
            {t("autogen.t_add_create_torrent")}
          </h2>
          <button
            type="button"
            className="modal-close"
            onClick={onClose}
            style={{
              background: "none",
              border: "none",
              fontSize: "1.5rem",
              color: "var(--text-muted, #7e8092)",
              cursor: "pointer",
              lineHeight: 1,
            }}
          >
            {t("autogen.t_times")}
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
