import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "../i18n";
import { useUpdates, useInstallUpdate } from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { ConfirmModal } from "../components/ConfirmModal";
import { trackSystemLifecycle } from "../utils/analytics";
import type { UpdateEntry } from "../api/types";

function CheckIcon() {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
  );
}

function RefreshIcon() {
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
      <polyline points="23 4 23 10 17 10" />
      <polyline points="1 20 1 14 7 14" />
      <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
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

function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return Number.isNaN(d.getTime())
      ? iso
      : d.toLocaleDateString(undefined, {
          year: "numeric",
          month: "long",
          day: "numeric",
        });
  } catch {
    return iso;
  }
}

function SystemUpdates() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const { data: updates, isLoading, isFetching, error } = useUpdates();
  const installUpdate = useInstallUpdate();

  const [showConfirmModal, setShowConfirmModal] = useState<boolean>(false);
  const [targetUpdate, setTargetUpdate] = useState<UpdateEntry | null>(null);

  const isUpToDate =
    updates &&
    updates.length > 0 &&
    updates.every((u) => !u.latest || u.installed);

  const latestUpdate =
    updates?.find((u) => u.latest && !u.installed) ??
    updates?.find((u) => !u.installed);

  const handleCheckForUpdates = () => {
    trackSystemLifecycle("update_check");
    queryClient.invalidateQueries({ queryKey: ["updates"] });
  };

  const handleInitiateInstall = (update?: UpdateEntry) => {
    setTargetUpdate(update ?? latestUpdate ?? null);
    setShowConfirmModal(true);
  };

  const handleConfirmInstall = () => {
    setShowConfirmModal(false);
    const version = targetUpdate?.version;
    installUpdate.mutate(
      version ? { version } : undefined,
      {
        onSuccess: (data) => {
          toast.showToast(
            data?.message || t("system.updateInstallStarted"),
            "success",
          );
        },
        onError: (err) => {
          toast.showToast(
            t("system.updateInstallFailed", { error: err.message }),
            "error",
          );
        },
      },
    );
  };

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Page Header */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>🚀</span> {t("system.updatesTitle")}
            <span
              className="badge badge-primary"
              style={{ fontSize: "0.8rem", marginLeft: "0.25rem" }}
            >
              {t("system.releases")}
            </span>
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {t("system.updatesSubtitle")}
          </p>
        </div>

        <button
          className="btn btn-outline btn-small"
          onClick={handleCheckForUpdates}
          disabled={isFetching}
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: "0.4rem",
          }}
        >
          <RefreshIcon />
          <span>
            {isFetching
              ? t("system.checkingForUpdates")
              : t("system.checkForUpdates")}
          </span>
        </button>
      </div>

      {isLoading && <p className="loading">{t("system.checkingForUpdates")}</p>}

      {error && (
        <div className="card" style={{ marginBottom: "1rem" }}>
          <p className="error">{t("system.failedToCheckUpdates")}</p>
        </div>
      )}

      {updates && (
        <>
          {/* Status Alert Banner */}
          <div
            className="card"
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              gap: "0.75rem",
              padding: "1rem 1.25rem",
              marginBottom: "1.25rem",
              borderRadius: "8px",
              backgroundColor: isUpToDate
                ? "rgba(40, 167, 69, 0.12)"
                : "rgba(200, 168, 78, 0.12)",
              border: `1px solid ${
                isUpToDate
                  ? "rgba(40, 167, 69, 0.35)"
                  : "rgba(200, 168, 78, 0.35)"
              }`,
              color: isUpToDate
                ? "var(--success, #28a745)"
                : "var(--accent, #c8a84e)",
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
              }}
            >
              <span style={{ display: "flex", alignItems: "center" }}>
                <CheckIcon />
              </span>
              <div style={{ fontSize: "0.9rem", fontWeight: 600 }}>
                {isUpToDate
                  ? t("system.currentVersion")
                  : t("system.updateAvailable")}
              </div>
            </div>

            {!isUpToDate && latestUpdate && (
              <button
                className="btn btn-primary btn-small"
                onClick={() => handleInitiateInstall(latestUpdate)}
                disabled={installUpdate.isPending}
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  flexShrink: 0,
                }}
              >
                <DownloadIcon />
                <span>
                  {installUpdate.isPending &&
                  (!targetUpdate ||
                    targetUpdate.version === latestUpdate.version)
                    ? t("system.installingUpdate")
                    : t("system.installUpdate")}
                </span>
              </button>
            )}
          </div>

          {/* Release History Cards */}
          <div
            style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
          >
            {updates.map((update) => {
              const isThisUpdating =
                installUpdate.isPending &&
                (targetUpdate?.version === update.version ||
                  (!targetUpdate && update.latest));

              return (
                <div
                  key={update.version}
                  className="card"
                  style={{
                    padding: "1.25rem 1.5rem",
                    borderRadius: "8px",
                    border: "1px solid var(--border-light)",
                    boxShadow:
                      "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.75rem",
                      marginBottom: "1rem",
                      borderBottom: "1px solid var(--border-light)",
                      paddingBottom: "0.75rem",
                      flexWrap: "wrap",
                    }}
                  >
                    <span
                      style={{
                        fontSize: "1.1rem",
                        fontWeight: 700,
                        color: "var(--accent, #c8a84e)",
                      }}
                    >
                      v{update.version}
                    </span>
                    <span
                      style={{
                        color: "var(--text-muted)",
                        fontSize: "0.85rem",
                      }}
                    >
                      📅 {formatDate(update.releaseDate)}
                    </span>
                    {update.installed && (
                      <span
                        className="badge badge-seeding"
                        style={{ marginLeft: "auto" }}
                      >
                        {t("system.currentlyInstalled")}
                      </span>
                    )}
                    {!update.installed && (
                      <div
                        style={{
                          marginLeft: "auto",
                          display: "flex",
                          alignItems: "center",
                          gap: "0.5rem",
                        }}
                      >
                        {update.latest && (
                          <span className="badge badge-queued">
                            {t("system.latestRelease")}
                          </span>
                        )}
                        <button
                          className="btn btn-primary btn-small"
                          onClick={() => handleInitiateInstall(update)}
                          disabled={installUpdate.isPending}
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.4rem",
                          }}
                        >
                          <DownloadIcon />
                          <span>
                            {isThisUpdating
                              ? t("system.installingUpdate")
                              : t("system.installUpdate")}
                          </span>
                        </button>
                      </div>
                    )}
                  </div>

                  {update.changes &&
                    update.changes.new &&
                    update.changes.new.length > 0 && (
                      <div style={{ marginBottom: "0.85rem" }}>
                        <div
                          style={{
                            fontSize: "0.75rem",
                            fontWeight: 700,
                            textTransform: "uppercase",
                            letterSpacing: "0.05em",
                            color: "var(--success, #28a745)",
                            marginBottom: "0.4rem",
                          }}
                        >
                          {t("system.newFeatures")}
                        </div>
                        <ul
                          style={{
                            margin: 0,
                            paddingLeft: "1.25rem",
                            fontSize: "0.875rem",
                            color: "var(--text-secondary)",
                            lineHeight: 1.6,
                          }}
                        >
                          {update.changes.new.map((item, i) => (
                            <li key={i}>{item}</li>
                          ))}
                        </ul>
                      </div>
                    )}

                  {update.changes &&
                    update.changes.fixed &&
                    update.changes.fixed.length > 0 && (
                      <div>
                        <div
                          style={{
                            fontSize: "0.75rem",
                            fontWeight: 700,
                            textTransform: "uppercase",
                            letterSpacing: "0.05em",
                            color: "var(--accent, #c8a84e)",
                            marginBottom: "0.4rem",
                          }}
                        >
                          {t("system.bugFixesImprovements")}
                        </div>
                        <ul
                          style={{
                            margin: 0,
                            paddingLeft: "1.25rem",
                            fontSize: "0.875rem",
                            color: "var(--text-secondary)",
                            lineHeight: 1.6,
                          }}
                        >
                          {update.changes.fixed.map((item, i) => (
                            <li key={i}>{item}</li>
                          ))}
                        </ul>
                      </div>
                    )}
                </div>
              );
            })}
          </div>
        </>
      )}

      <ConfirmModal
        isOpen={showConfirmModal}
        title={t("system.installUpdate")}
        message={
          targetUpdate?.version
            ? t("system.confirmInstallSpecificUpdate", {
                version: targetUpdate.version,
              })
            : t("system.confirmInstallUpdate")
        }
        confirmText={t("system.installUpdate")}
        cancelText={t("common.cancel")}
        onConfirm={handleConfirmInstall}
        onCancel={() => {
          setShowConfirmModal(false);
          setTargetUpdate(null);
        }}
      />
    </div>
  );
}

export default SystemUpdates;
