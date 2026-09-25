import React, { useState, useEffect, useRef, useCallback } from "react";
import { api } from "../api/client";
import type { CurrentUser } from "../api/types";
import { useTranslation } from "../i18n";
import { LeecharrLogo } from "./icons/LeecharrLogo";
import { LeecharrText } from "./icons/LeecharrText";
import { LockIcon } from "./icons/UIIcons";

export interface IdleLockModalProps {
  isOpen: boolean;
  currentUser: CurrentUser | null;
  lockReason?: "idle" | "expired";
  onUnlock: () => void;
  onLogout: () => void;
}

export function IdleLockModal({
  isOpen,
  currentUser,
  lockReason = "idle",
  onUnlock,
  onLogout,
}: IdleLockModalProps) {
  const { t } = useTranslation();
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const passwordInputRef = useRef<HTMLInputElement | null>(null);
  const resumeButtonRef = useRef<HTMLButtonElement | null>(null);

  const isAnonymous =
    !currentUser ||
    !currentUser.isAuthenticated ||
    currentUser.username === "Anonymous";

  const requiresPassword =
    currentUser?.requiresPassword !== undefined
      ? currentUser.requiresPassword
      : (currentUser?.authenticationEnabled ?? !isAnonymous);

  useEffect(() => {
    if (isOpen) {
      setPassword("");
      setError(null);
      setIsSubmitting(false);
      // Focus password input or resume button after modal renders
      const timer = setTimeout(() => {
        if (requiresPassword) {
          passwordInputRef.current?.focus();
        } else {
          resumeButtonRef.current?.focus();
        }
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [isOpen, requiresPassword]);

  // When no password is required (screen saver mode), pressing Enter or Space unlocks
  useEffect(() => {
    if (!isOpen || requiresPassword) return;

    const handleKeyResume = (e: KeyboardEvent) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        onUnlock();
      }
    };

    window.addEventListener("keydown", handleKeyResume);
    return () => window.removeEventListener("keydown", handleKeyResume);
  }, [isOpen, requiresPassword, onUnlock]);

  // Trap Escape key and prevent propagation (or unlock if screen saver)
  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        if (!requiresPassword) {
          onUnlock();
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [isOpen, requiresPassword, onUnlock]);

  const handleUnlock = useCallback(
    async (e?: React.FormEvent) => {
      if (e) {
        e.preventDefault();
      }

      // If user is unauthenticated, auth is disabled, or no password is required, unlock directly
      if (!requiresPassword) {
        onUnlock();
        return;
      }

      if (!password.trim()) {
        setError(t("auth.passwordRequired", "Password or API key is required"));
        passwordInputRef.current?.focus();
        return;
      }

      try {
        setIsSubmitting(true);
        setError(null);

        const username = currentUser?.username || "admin";
        await api.loginWithRetry({
          username,
          password: password.trim(),
        });

        setPassword("");
        onUnlock();
      } catch (err: unknown) {
        const msg =
          (err as Error)?.message ||
          t("auth.invalidPassword", "Invalid password or credentials");
        setError(msg);
        passwordInputRef.current?.select();
      } finally {
        setIsSubmitting(false);
      }
    },
    [currentUser, password, requiresPassword, onUnlock, t],
  );

  if (!isOpen) {
    return null;
  }

  const isExpired = lockReason === "expired";
  const displayName =
    currentUser?.displayName ||
    currentUser?.username ||
    (requiresPassword ? "Administrator" : "Guest");
  const userInitials = displayName.slice(0, 2).toUpperCase();
  const roleName =
    currentUser?.roles && currentUser.roles.length > 0
      ? currentUser.roles[0]
      : "Admin";

  return (
    <div
      className="idle-lock-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="idle-lock-title"
      onClick={() => {
        if (!requiresPassword) {
          onUnlock();
        }
      }}
    >
      <div className="idle-lock-card" onClick={(e) => e.stopPropagation()}>
        {/* Leecharr Branding */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            marginBottom: "1.25rem",
          }}
        >
          <LeecharrLogo size={36} />
          <LeecharrText width={110} />
        </div>

        {/* Lock Icon */}
        <div className="idle-lock-icon" aria-hidden="true">
          <LockIcon size={26} />
        </div>

        {/* Title and Subtitle */}
        <div className="idle-lock-header">
          <h2
            id="idle-lock-title"
            style={{
              margin: "0 0 0.5rem",
              fontSize: "1.35rem",
              fontWeight: 600,
              color: "var(--text-primary)",
            }}
          >
            {!requiresPassword
              ? t("auth.sessionPaused", "Session Paused")
              : isExpired
                ? t("auth.sessionExpired", "Session Expired")
                : t("auth.screenLocked", "Screen Locked")}
          </h2>
          <p
            style={{
              margin: 0,
              fontSize: "0.875rem",
              color: "var(--text-secondary)",
              lineHeight: 1.45,
            }}
          >
            {!requiresPassword
              ? t(
                  "auth.screenSaverDescription",
                  "Leecharr is paused due to inactivity. Click Resume or press Enter to continue.",
                )
              : isExpired
                ? t(
                    "auth.expiredDescription",
                    "Your session has expired. Enter your password to resume without losing your current work.",
                  )
                : t(
                    "auth.lockedDescription",
                    "Your session was locked due to inactivity. Enter your password or PIN to unlock.",
                  )}
          </p>
        </div>

        {/* User Badge */}
        <div className="idle-lock-user-badge">
          <div className="idle-lock-avatar" aria-hidden="true">
            {userInitials}
          </div>
          <div style={{ textAlign: "left", lineHeight: 1.2 }}>
            <div
              style={{
                fontWeight: 600,
                fontSize: "0.9rem",
                color: "var(--text-primary)",
              }}
            >
              {displayName}
            </div>
            <div
              style={{
                fontSize: "0.75rem",
                color: "var(--text-secondary)",
                textTransform: "capitalize",
              }}
            >
              {roleName}
            </div>
          </div>
        </div>

        {/* Error Alert */}
        {error && (
          <div
            className="alert alert-danger"
            style={{
              width: "100%",
              marginBottom: "1rem",
              padding: "0.6rem 0.75rem",
              fontSize: "0.85rem",
              textAlign: "left",
            }}
            role="alert"
          >
            {error}
          </div>
        )}

        {/* Unlock Form */}
        <form
          onSubmit={handleUnlock}
          style={{
            width: "100%",
            display: "flex",
            flexDirection: "column",
            gap: "1rem",
          }}
        >
          {requiresPassword && (
            <div style={{ position: "relative", width: "100%" }}>
              <input
                ref={passwordInputRef}
                type={showPassword ? "text" : "password"}
                className="form-input"
                placeholder={t(
                  "auth.passwordPlaceholder",
                  "Enter password or API key",
                )}
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  if (error) setError(null);
                }}
                disabled={isSubmitting}
                autoComplete="current-password"
                style={{
                  width: "100%",
                  paddingRight: "2.75rem",
                  boxSizing: "border-box",
                }}
              />
              <button
                type="button"
                className="btn btn-ghost"
                onClick={() => setShowPassword((prev) => !prev)}
                title={showPassword ? "Hide password" : "Show password"}
                aria-label={showPassword ? "Hide password" : "Show password"}
                style={{
                  position: "absolute",
                  right: "0.5rem",
                  top: "50%",
                  transform: "translateY(-50%)",
                  padding: "0.25rem 0.4rem",
                  fontSize: "0.8rem",
                  color: "var(--text-secondary)",
                  background: "transparent",
                  border: "none",
                  cursor: "pointer",
                }}
              >
                {showPassword ? "Hide" : "Show"}
              </button>
            </div>
          )}

          <button
            ref={resumeButtonRef}
            type="submit"
            className="btn btn-primary"
            disabled={isSubmitting}
            style={{
              width: "100%",
              padding: "0.65rem 1rem",
              fontWeight: 600,
              fontSize: "0.95rem",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              gap: "0.5rem",
            }}
          >
            {isSubmitting ? (
              <>
                <span className="spinner-small" aria-hidden="true" />
                <span>{t("auth.unlocking", "Unlocking...")}</span>
              </>
            ) : (
              <span>
                {!requiresPassword
                  ? t("auth.resumeSession", "Resume Session")
                  : t("auth.unlockButton", "Unlock Session")}
              </span>
            )}
          </button>
        </form>

        {/* Sign Out Button */}
        {requiresPassword && (
          <div style={{ marginTop: "1.5rem", width: "100%" }}>
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={onLogout}
              style={{
                width: "100%",
                fontSize: "0.85rem",
                color: "var(--danger, #ef4444)",
                borderColor: "rgba(239, 68, 68, 0.4)",
              }}
            >
              {t("auth.signOut", "Sign Out")}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

export interface IdleCountdownModalProps {
  isOpen: boolean;
  remainingSeconds: number;
  onStayLoggedIn: () => void;
  onLockNow: () => void;
  onLogout: () => void;
}

export function IdleCountdownModal({
  isOpen,
  remainingSeconds,
  onStayLoggedIn,
  onLockNow,
  onLogout,
}: IdleCountdownModalProps) {
  const { t } = useTranslation();

  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      // Space or Enter on the warning modal stays logged in
      if (e.key === "Enter" || e.key === " ") {
        const active = document.activeElement;
        if (
          active &&
          (active.tagName === "INPUT" || active.tagName === "TEXTAREA")
        ) {
          return;
        }
        e.preventDefault();
        onStayLoggedIn();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, onStayLoggedIn]);

  if (!isOpen) {
    return null;
  }

  const minutes = Math.floor(remainingSeconds / 60);
  const seconds = remainingSeconds % 60;
  const formattedTime = `${minutes}:${String(seconds).padStart(2, "0")}`;

  return (
    <div
      className="idle-countdown-overlay"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="idle-countdown-title"
      onClick={(e) => e.stopPropagation()}
    >
      <div className="idle-countdown-card">
        {/* Warning Icon */}
        <div
          style={{
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            width: 48,
            height: 48,
            borderRadius: "50%",
            background: "rgba(245, 158, 11, 0.15)",
            color: "var(--warning, #f59e0b)",
            marginBottom: "1rem",
          }}
          aria-hidden="true"
        >
          <svg
            width="24"
            height="24"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <circle cx="12" cy="12" r="10" />
            <polyline points="12 6 12 12 16 14" />
          </svg>
        </div>

        <h2
          id="idle-countdown-title"
          style={{
            margin: "0 0 0.5rem",
            fontSize: "1.25rem",
            fontWeight: 600,
            color: "var(--text-primary)",
          }}
        >
          {t("auth.inactivityWarning", "Inactivity Warning")}
        </h2>

        <p
          style={{
            margin: "0 0 1rem",
            fontSize: "0.875rem",
            color: "var(--text-secondary)",
            lineHeight: 1.45,
          }}
        >
          {t(
            "auth.inactivityWarningDesc",
            "Your session will automatically lock due to inactivity in:",
          )}
        </p>

        {/* Countdown Big Display */}
        <div className="idle-countdown-timer" aria-live="polite">
          {formattedTime}
        </div>

        <p
          style={{
            fontSize: "0.8rem",
            color: "var(--text-muted, #888)",
            margin: "0 0 1.5rem",
          }}
        >
          {t(
            "auth.activityHint",
            "Move your mouse or press any key to remain active.",
          )}
        </p>

        {/* Action Buttons */}
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "0.65rem",
            width: "100%",
          }}
        >
          <button
            type="button"
            className="btn btn-primary"
            onClick={onStayLoggedIn}
            style={{ width: "100%", padding: "0.6rem 1rem", fontWeight: 600 }}
          >
            {t("auth.stayLoggedIn", "Stay Logged In")}
          </button>
          <div style={{ display: "flex", gap: "0.5rem", width: "100%" }}>
            <button
              type="button"
              className="btn btn-outline"
              onClick={onLockNow}
              style={{ flex: 1, fontSize: "0.85rem" }}
            >
              {t("auth.lockNow", "Lock Now")}
            </button>
            <button
              type="button"
              className="btn btn-outline"
              onClick={onLogout}
              style={{
                flex: 1,
                fontSize: "0.85rem",
                color: "var(--danger, #ef4444)",
                borderColor: "rgba(239, 68, 68, 0.4)",
              }}
            >
              {t("auth.signOut", "Sign Out")}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
