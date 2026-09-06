import React from "react";
import { useEscapeKey } from "../hooks/useEscapeKey";

export interface ConfirmModalProps {
  isOpen: boolean;
  title?: string;
  message: string | React.ReactNode;
  confirmText?: string;
  cancelText?: string;
  danger?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmModal({
  isOpen,
  title,
  message,
  confirmText = "Confirm",
  cancelText = "Cancel",
  danger = false,
  onConfirm,
  onCancel,
}: ConfirmModalProps) {
  useEscapeKey(onCancel, isOpen);

  if (!isOpen) return null;

  return (
    <div
      className="modal-overlay"
      onClick={onCancel}
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-modal-title"
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
          border: danger
            ? "1px solid rgba(230, 57, 70, 0.45)"
            : "1px solid rgba(255, 209, 102, 0.35)",
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
              backgroundColor: danger
                ? "rgba(230, 57, 70, 0.15)"
                : "rgba(255, 209, 102, 0.15)",
              color: danger ? "#e63946" : "#ffd166",
              fontSize: "1.2rem",
              flexShrink: 0,
            }}
          >
            {danger ? "⚠️" : "ℹ️"}
          </div>
          <h3
            id="confirm-modal-title"
            style={{
              margin: 0,
              fontSize: "1.15rem",
              fontWeight: 600,
              color: "var(--text-primary, #f8f4ed)",
            }}
          >
            {title || (danger ? "Confirm Action" : "Confirmation")}
          </h3>
        </div>

        {/* Message */}
        <div
          style={{
            fontSize: "0.95rem",
            lineHeight: 1.55,
            color: "rgba(248, 244, 237, 0.85)",
            marginBottom: "1.5rem",
            paddingLeft: "0.25rem",
            wordBreak: "break-word",
          }}
        >
          {message}
        </div>

        {/* Action Buttons */}
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.75rem",
            marginTop: "auto",
          }}
        >
          <button
            type="button"
            className="btn btn-outline"
            onClick={onCancel}
            style={{
              padding: "0.5rem 1rem",
              fontSize: "0.9rem",
              borderRadius: "6px",
              border: "1px solid rgba(255, 255, 255, 0.18)",
              color: "var(--text-primary, #f8f4ed)",
              backgroundColor: "transparent",
              cursor: "pointer",
            }}
          >
            {cancelText}
          </button>
          <button
            type="button"
            className="btn"
            onClick={onConfirm}
            autoFocus
            style={{
              padding: "0.5rem 1.25rem",
              fontSize: "0.9rem",
              fontWeight: 600,
              borderRadius: "6px",
              border: "none",
              backgroundColor: danger ? "#e63946" : "#ffd166",
              color: danger ? "#ffffff" : "#10111a",
              cursor: "pointer",
              transition: "opacity 0.15s ease",
            }}
          >
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  );
}

export default ConfirmModal;
