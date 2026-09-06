import { useTranslation } from "../i18n";
import React, { useState, useEffect, useRef } from "react";
import { useEscapeKey } from "../hooks/useEscapeKey";

export interface PromptModalProps {
  isOpen: boolean;
  title: string;
  message?: string;
  defaultValue?: string;
  placeholder?: string;
  inputType?: "text" | "number";
  min?: number;
  confirmText?: string;
  cancelText?: string;
  validate?: (value: string) => string | null;
  onConfirm: (value: string) => void;
  onCancel: () => void;
}

export function PromptModal({
  isOpen,
  title,
  message,
  defaultValue = "",
  placeholder = "",
  inputType = "text",
  min,
  confirmText = "Save",
  cancelText = "Cancel",
  validate,
  onConfirm,
  onCancel,
}: PromptModalProps) {
  const { t } = useTranslation();

  useEscapeKey(onCancel, isOpen);

  const [value, setValue] = useState(defaultValue);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setValue(defaultValue);
    setError(null);
  }, [defaultValue, isOpen]);

  useEffect(() => {
    if (isOpen) {
      const timer = setTimeout(() => {
        if (inputRef.current) {
          inputRef.current.focus();
          inputRef.current.select();
        }
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const handleSubmit = (e: React.FormEvent) => {
    const { t } = useTranslation();

    e.preventDefault();
    if (validate) {
      const validationError = validate(value);
      if (validationError) {
        setError(validationError);
        return;
      }
    }
    onConfirm(value);
  };

  return (
    <div
      className="modal-overlay"
      onClick={onCancel}
      role="dialog"
      aria-modal="true"
      aria-labelledby="prompt-modal-title"
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(16, 17, 26, 0.85)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 10000,
        padding: "1rem",
      }}
    >
      <div
        className="modal-content"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: "440px",
          backgroundColor: "var(--bg-secondary, #171b35)",
          borderRadius: "12px",
          border: "1px solid rgba(255, 209, 102, 0.35)",
          boxShadow: "0 20px 50px rgba(0, 0, 0, 0.75)",
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
          padding: "1.5rem",
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
              backgroundColor: "rgba(255, 209, 102, 0.15)",
              color: "#ffd166",
              fontSize: "1.2rem",
              flexShrink: 0,
            }}
          >
            ✏️
          </div>
          <h3
            id="prompt-modal-title"
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
        {message && (
          <div
            style={{
              fontSize: "0.95rem",
              lineHeight: 1.55,
              color: "var(--text-secondary, #c7c5d3)",
              marginBottom: "1rem",
              paddingLeft: "0.25rem",
              wordBreak: "break-word",
            }}
          >
            {message}
          </div>
        )}

        {/* Form */}
        <form
          onSubmit={handleSubmit}
          style={{ display: "flex", flexDirection: "column" }}
        >
          <input
            ref={inputRef}
            type={inputType}
            min={min}
            placeholder={placeholder}
            value={value}
            onChange={(e) => {
              setValue(e.target.value);
              if (error) setError(null);
            }}
            style={{
              width: "100%",
              padding: "0.6rem 0.85rem",
              backgroundColor: "var(--bg-primary, #10111a)",
              border: error
                ? "1px solid rgba(230, 57, 70, 0.7)"
                : "1px solid rgba(255, 209, 102, 0.35)",
              borderRadius: "8px",
              color: "var(--text-primary, #f8f4ed)",
              fontSize: "0.95rem",
              outline: "none",
              boxSizing: "border-box",
            }}
          />

          {error && (
            <div
              style={{
                fontSize: "0.82rem",
                color: "#e63946",
                marginTop: "0.4rem",
                paddingLeft: "0.25rem",
              }}
            >
              {error}
            </div>
          )}

          {/* Action Buttons */}
          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: "0.75rem",
              marginTop: "1.25rem",
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
              type="submit"
              className="btn"
              style={{
                padding: "0.5rem 1.25rem",
                fontSize: "0.9rem",
                fontWeight: 600,
                borderRadius: "6px",
                border: "none",
                backgroundColor: "#ffd166",
                color: "#10111a",
                cursor: "pointer",
                transition: "opacity 0.15s ease",
              }}
            >
              {confirmText}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export const PromptDialog = PromptModal;
export default PromptModal;
