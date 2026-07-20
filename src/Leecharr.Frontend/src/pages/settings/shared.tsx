import React, { useState, useEffect, useRef } from "react";
import { useLocation } from "react-router";

export function SaveFeedback({
  isPending: _isPending,
  isError,
  isSuccess,
  error,
  dirty,
}: {
  isPending: boolean;
  isError: boolean;
  isSuccess: boolean;
  error: Error | null;
  dirty: boolean;
}) {
  return (
    <>
      {isError && (
        <span
          className="error"
          style={{
            marginLeft: "0.75rem",
            fontSize: "0.85rem",
            color: "var(--danger)",
          }}
        >
          Failed to save: {error?.message}
        </span>
      )}
      {isSuccess && !dirty && (
        <span
          style={{
            marginLeft: "0.75rem",
            fontSize: "0.85rem",
            color: "var(--success, #27ae60)",
            fontWeight: 600,
          }}
        >
          ✓ Changes Saved Successfully
        </span>
      )}
    </>
  );
}

export function PendingChangesModal({
  onSave,
  onDiscard,
  onCancel,
}: {
  onSave: () => void;
  onDiscard: () => void;
  onCancel: () => void;
}) {
  return (
    <div className="modal-overlay" onClick={onCancel}>
      <div
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          maxWidth: 420,
          borderRadius: "8px",
          boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
          border: "1px solid rgba(255, 255, 255, 0.12)",
        }}
      >
        <h2 style={{ margin: "0 0 0.75rem", fontSize: "1.2rem" }}>
          Unsaved Changes
        </h2>
        <p
          style={{
            margin: "0 0 1.25rem",
            color: "var(--text-muted)",
            fontSize: "0.9rem",
            lineHeight: 1.4,
          }}
        >
          You have unsaved changes in this settings section. What would you like
          to do?
        </p>
        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button
            className="btn btn-outline btn-small"
            onClick={onCancel}
            type="button"
          >
            Stay on Page
          </button>
          <button
            className="btn btn-danger btn-small"
            onClick={onDiscard}
            type="button"
          >
            Discard Changes
          </button>
          <button
            className="btn btn-primary btn-small"
            onClick={onSave}
            type="button"
          >
            Save Changes
          </button>
        </div>
      </div>
    </div>
  );
}

export function useUnsavedGuard(dirty: boolean) {
  const location = useLocation();
  const [pendingNav, setPendingNav] = useState(false);
  const prevPathRef = useRef(location.pathname);

  useEffect(() => {
    if (!dirty) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty]);

  useEffect(() => {
    if (dirty && location.pathname !== prevPathRef.current) {
      setPendingNav(true);
    }
  }, [dirty, location.pathname]);

  return {
    blocked: pendingNav,
    dismiss: () => setPendingNav(false),
  };
}

export function SaveBar({
  dirty,
  isPending,
  isError,
  isSuccess,
  error,
  onSave,
}: {
  dirty: boolean;
  isPending: boolean;
  isError: boolean;
  isSuccess: boolean;
  error: Error | null;
  onSave: () => void;
}) {
  const guard = useUnsavedGuard(dirty);

  return (
    <>
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          padding: "0.75rem 1.25rem",
          marginBottom: "1.25rem",
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: dirty
            ? "1px solid rgba(200, 168, 78, 0.5)"
            : "1px solid rgba(255, 255, 255, 0.08)",
          backgroundColor: "var(--bg-secondary)",
          transition: "all 0.2s ease",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.75rem",
            flexWrap: "wrap",
          }}
        >
          <button
            className={`btn ${dirty ? "btn-primary" : "btn-outline"}`}
            onClick={onSave}
            disabled={!dirty || isPending}
            style={{ minWidth: "120px" }}
          >
            {isPending
              ? "Saving Changes..."
              : dirty
                ? "💾 Save Changes"
                : "✓ No Changes"}
          </button>
          <SaveFeedback
            isPending={isPending}
            isError={isError}
            isSuccess={isSuccess}
            error={error}
            dirty={dirty}
          />
        </div>
        {dirty && (
          <span
            className="badge badge-warning"
            style={{ fontSize: "0.75rem", padding: "0.25rem 0.6rem" }}
          >
            ● Unsaved Changes
          </span>
        )}
      </div>
      {guard.blocked && (
        <PendingChangesModal
          onSave={() => {
            onSave();
            guard.dismiss();
          }}
          onDiscard={guard.dismiss}
          onCancel={guard.dismiss}
        />
      )}
    </>
  );
}

export function SectionCard({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <div
      className="card"
      style={{
        padding: "1.25rem",
        marginBottom: "1.25rem",
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        border: "1px solid rgba(255, 255, 255, 0.08)",
      }}
    >
      <div
        style={{
          marginBottom: "1.25rem",
          paddingBottom: "0.6rem",
          borderBottom: "1px solid var(--border-light)",
        }}
      >
        <h3
          style={{
            margin: 0,
            fontSize: "1.1rem",
            color: "var(--text-primary)",
            fontWeight: 600,
          }}
        >
          {title}
        </h3>
        {description && (
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.25rem",
            }}
          >
            {description}
          </div>
        )}
      </div>
      {children}
    </div>
  );
}

export function NumberInput({
  label,
  value,
  onChange,
  min,
  max,
  step,
  hint,
  suffix,
  disabled,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  min?: number;
  max?: number;
  step?: number;
  hint?: string;
  suffix?: string;
  disabled?: boolean;
}) {
  const inputEl = (
    <input
      type="number"
      className="form-input"
      value={value}
      onChange={(e) =>
        onChange(
          step && step < 1
            ? parseFloat(e.target.value) || 0
            : parseInt(e.target.value, 10) || 0,
        )
      }
      min={min}
      max={max}
      step={step}
      disabled={disabled}
      style={{ borderRadius: suffix ? "6px 0 0 6px" : "6px" }}
    />
  );

  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        {suffix ? (
          <div className="form-input-with-suffix">
            {inputEl}
            <span className="form-input-suffix">{suffix}</span>
          </div>
        ) : (
          inputEl
        )}
        {hint && <span className="form-hint">{hint}</span>}
      </div>
    </div>
  );
}

export function TextInput({
  label,
  value,
  onChange,
  placeholder,
  hint,
  disabled,
  type,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  hint?: string;
  disabled?: boolean;
  type?: string;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <input
          type={type || "text"}
          className="form-input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          disabled={disabled}
          style={{ borderRadius: "6px" }}
        />
        {hint && <span className="form-hint">{hint}</span>}
      </div>
    </div>
  );
}

export function Toggle({
  label,
  checked,
  onChange,
  hint,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  hint?: string;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <div className="form-toggle-row">
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={checked}
              onChange={(e) => onChange(e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
          {hint && <span className="form-toggle-description">{hint}</span>}
        </div>
      </div>
    </div>
  );
}

export function SelectInput({
  label,
  value,
  onChange,
  options,
  hint,
  disabled,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <select
          className="form-select"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
          style={{ borderRadius: "6px" }}
        >
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        {hint && <span className="form-hint">{hint}</span>}
      </div>
    </div>
  );
}

export function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <div
      className="form-section-title"
      style={{
        fontSize: "1.05rem",
        fontWeight: 600,
        color: "var(--accent, #c8a84e)",
        padding: "0.75rem 0 0.5rem",
        marginTop: "1rem",
        marginBottom: "0.5rem",
        borderBottom: "1px solid var(--border-light)",
      }}
    >
      {children}
    </div>
  );
}
