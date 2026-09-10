import { useTranslation } from "../i18n";
import React, { useState, useEffect, useCallback } from "react";
import {
  useCreateIndexer,
  useSyncProwlarr,
  useTestDirectIndexer,
  useCreateArrConnection,
  useTestDirectArrConnection,
} from "../api/hooks";
import type {
  IndexerDefinition,
  IndexerTestResult,
  ArrConnection,
  ArrTestResult,
} from "../api/types";
import { TextInput, SelectInput, Toggle } from "../pages/settings/shared";
import { normalizeIndexerPayload } from "../pages/settings/IndexersTab";
import LeecharrLogo from "./icons/LeecharrLogo";
import LeecharrText from "./icons/LeecharrText";
import { useFocusTrap } from "../hooks/useFocusTrap";
import { LanguageSelector } from "./LanguageSelector";

export const STORAGE_KEY_HIDE_GUIDE = "leecharr_hide_getting_started";

interface GettingStartedModalProps {
  isOpen: boolean;
  onClose: () => void;
  onNavigateSettings?: (tab: string) => void;
  onNavigateTorrents?: () => void;
  onNavigateIndexers?: () => void;
}

type GuideMode = "readonly" | "interactive";

interface StepMeta {
  id: string;
  stepNum: number;
  shortNameKey: string;
  titleKey: string;
}

const STEPS: StepMeta[] = [
  {
    id: "welcome",
    stepNum: 0,
    shortNameKey: "gettingStarted.stepWelcome",
    titleKey: "gettingStarted.stepWelcomeTitle",
  },
  {
    id: "prowlarr",
    stepNum: 1,
    shortNameKey: "gettingStarted.stepProwlarr",
    titleKey: "gettingStarted.stepProwlarrTitle",
  },
  {
    id: "sonarr",
    stepNum: 2,
    shortNameKey: "gettingStarted.stepSonarr",
    titleKey: "gettingStarted.stepSonarrTitle",
  },
  {
    id: "radarr",
    stepNum: 3,
    shortNameKey: "gettingStarted.stepRadarr",
    titleKey: "gettingStarted.stepRadarrTitle",
  },
  {
    id: "lidarr",
    stepNum: 4,
    shortNameKey: "gettingStarted.stepLidarr",
    titleKey: "gettingStarted.stepLidarrTitle",
  },
  {
    id: "finish",
    stepNum: 5,
    shortNameKey: "gettingStarted.stepFinished",
    titleKey: "gettingStarted.stepFinishedTitle",
  },
];

export function GettingStartedModal({
  isOpen,
  onClose,
  onNavigateSettings,
  onNavigateTorrents,
  onNavigateIndexers,
}: GettingStartedModalProps) {
  const { t } = useTranslation();
  const trapRef = useFocusTrap<HTMLDivElement>({ isOpen, onClose });

  const [currentStep, setCurrentStep] = useState(0);
  const [mode, setMode] = useState<GuideMode>("readonly");
  const [dontShowAgain, setDontShowAgain] = useState<boolean>(() => {
    return localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) === "true";
  });

  // Prowlarr Indexer Form State (Full Real Form)
  const [indexerForm, setIndexerForm] = useState<Partial<IndexerDefinition>>({
    name: "Prowlarr",
    indexerType: "Prowlarr",
    url: "http://localhost:9696",
    apiKey: "",
    apiPath: "/api",
    categories: "2000,5000",
    enable: true,
    enableRss: true,
    enableSearch: true,
  });
  const [indexerTestResult, setIndexerTestResult] =
    useState<IndexerTestResult | null>(null);

  // Sonarr Form State (Full Real Form)
  const [sonarrForm, setSonarrForm] = useState<Partial<ArrConnection>>({
    name: "Sonarr",
    arrType: "Sonarr",
    url: "http://localhost:8989",
    externalUrl: "",
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    webhookHost: "leecharr",
  });
  const [sonarrTestResult, setSonarrTestResult] =
    useState<ArrTestResult | null>(null);

  // Radarr Form State (Full Real Form)
  const [radarrForm, setRadarrForm] = useState<Partial<ArrConnection>>({
    name: "Radarr",
    arrType: "Radarr",
    url: "http://localhost:7878",
    externalUrl: "",
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    webhookHost: "leecharr",
  });
  const [radarrTestResult, setRadarrTestResult] =
    useState<ArrTestResult | null>(null);

  // Lidarr Form State (Full Real Form)
  const [lidarrForm, setLidarrForm] = useState<Partial<ArrConnection>>({
    name: "Lidarr",
    arrType: "Lidarr",
    url: "http://localhost:8686",
    externalUrl: "",
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    webhookHost: "leecharr",
  });
  const [lidarrTestResult, setLidarrTestResult] =
    useState<ArrTestResult | null>(null);

  // API Mutations
  const testIndexerMutation = useTestDirectIndexer();
  const createIndexerMutation = useCreateIndexer();
  const syncProwlarrMutation = useSyncProwlarr();

  const testArrMutation = useTestDirectArrConnection();
  const createArrMutation = useCreateArrConnection();

  const handleClose = useCallback(() => {
    if (dontShowAgain) {
      localStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "true");
    }
    onClose();
  }, [dontShowAgain, onClose]);

  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        handleClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, handleClose]);

  if (!isOpen) return null;

  const handleDontShowChange = (checked: boolean) => {
    setDontShowAgain(checked);
    if (checked) {
      localStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "true");
    } else {
      localStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "false");
    }
  };

  const handleNext = () => {
    if (currentStep < STEPS.length - 1) {
      setCurrentStep((p) => p + 1);
    } else {
      handleClose();
    }
  };

  const handlePrev = () => {
    if (currentStep > 0) {
      setCurrentStep((p) => p - 1);
    }
  };

  const isReadOnly = mode === "readonly";

  // Test Connection Handlers
  const handleTestIndexer = () => {
    setIndexerTestResult(null);
    const payload = normalizeIndexerPayload(indexerForm);
    testIndexerMutation.mutate(payload, {
      onSuccess: (data) => setIndexerTestResult(data),
      onError: (err) =>
        setIndexerTestResult({ success: false, message: err.message }),
    });
  };

  const handleSaveIndexer = () => {
    if (indexerForm.indexerType === "Prowlarr") {
      syncProwlarrMutation.mutate(
        {
          url: indexerForm.url || "http://localhost:9696",
          apiKey: indexerForm.apiKey || "",
        },
        {
          onSuccess: () => {
            handleNext();
          },
          onError: (err) => {
            setIndexerTestResult({
              success: false,
              message: `Sync failed: ${err.message}`,
            });
          },
        },
      );
    } else {
      const payload = normalizeIndexerPayload(indexerForm);
      createIndexerMutation.mutate(payload, {
        onSuccess: () => {
          handleNext();
        },
      });
    }
  };

  const handleTestArr = (
    form: Partial<ArrConnection>,
    setResult: (res: ArrTestResult | null) => void,
  ) => {
    setResult(null);
    testArrMutation.mutate(form, {
      onSuccess: (data) => setResult(data),
      onError: (err) => setResult({ success: false, message: err.message }),
    });
  };

  const handleSaveArr = (form: Partial<ArrConnection>) => {
    createArrMutation.mutate(
      {
        ...form,
        name: form.name?.trim() || form.arrType || "Arr Connection",
        implementation: `${form.arrType || "Sonarr"}Connection`,
        configContract: "ArrConnectionDefinition",
      },
      {
        onSuccess: () => {
          handleNext();
        },
      },
    );
  };

  return (
    <div
      className="modal-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="getting-started-modal-title"
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
      onClick={(e) => {
        if (e.target === e.currentTarget) handleClose();
      }}
    >
      <div
        ref={trapRef}
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          maxWidth: 540,
          width: "92vw",
          maxHeight: "90vh",
          overflowY: "auto",
          backgroundColor: "var(--bg-secondary, #171b35)",
          borderRadius: "8px",
          boxShadow: "0 16px 40px rgba(0, 0, 0, 0.7)",
          border: "1px solid var(--border-light, rgba(255, 255, 255, 0.12))",
          padding: "1.5rem",
          display: "flex",
          flexDirection: "column",
        }}
      >
        {/* Top Header Controls: Mode Selector, Language Selector & Close Button */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
          }}
        >
          {/* Mode Selector */}
          <div
            style={{
              display: "inline-flex",
              background: "var(--bg-primary, #101322)",
              padding: "2px",
              borderRadius: "20px",
              border: "1px solid var(--border-light, #1c203b)",
              fontSize: "0.75rem",
            }}
          >
            <button
              type="button"
              onClick={() => setMode("readonly")}
              style={{
                background:
                  mode === "readonly"
                    ? "var(--accent, #ffd166)"
                    : "transparent",
                color: mode === "readonly" ? "#0d0e17" : "var(--text-muted, #aaa)",
                border: "none",
                padding: "3px 10px",
                borderRadius: "16px",
                fontWeight: mode === "readonly" ? 600 : 400,
                cursor: "pointer",
                transition: "all 0.2s",
              }}
              title="Tour mode with example preview"
            >
              👁️ {t("gettingStarted.guideMode")}
            </button>
            <button
              type="button"
              onClick={() => setMode("interactive")}
              style={{
                background:
                  mode === "interactive"
                    ? "var(--accent, #ffd166)"
                    : "transparent",
                color:
                  mode === "interactive" ? "#0d0e17" : "var(--text-muted, #aaa)",
                border: "none",
                padding: "3px 10px",
                borderRadius: "16px",
                fontWeight: mode === "interactive" ? 600 : 400,
                cursor: "pointer",
                transition: "all 0.2s",
              }}
              title="Live setup to test and save credentials"
            >
              ⚡ {t("gettingStarted.liveSetupMode")}
            </button>
          </div>

          {/* Right Controls */}
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <LanguageSelector align="right" />
            <button
              type="button"
              onClick={handleClose}
              style={{
                background: "transparent",
                border: "none",
                color: "var(--text-muted)",
                fontSize: "1.25rem",
                cursor: "pointer",
                padding: "0.2rem 0.4rem",
                borderRadius: "4px",
                lineHeight: 1,
              }}
              title={t("gettingStarted.close")}
            >
              ✕
            </button>
          </div>
        </div>

        {/* Step Indicator Breadcrumbs (Pill style matching Seedarr) */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: "1.25rem",
            paddingBottom: "0.75rem",
            borderBottom:
              "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            gap: "0.25rem",
            overflowX: "auto",
          }}
        >
          {STEPS.map((s, idx) => {
            const isActive = idx === currentStep;
            const isCompleted = idx < currentStep;
            return (
              <button
                key={s.id}
                type="button"
                onClick={() => setCurrentStep(idx)}
                style={{
                  background: isActive
                    ? "var(--accent, #ffd166)"
                    : isCompleted
                      ? "rgba(255, 255, 255, 0.08)"
                      : "transparent",
                  color: isActive
                    ? "#0d0e17"
                    : isCompleted
                      ? "var(--text-primary)"
                      : "var(--text-muted)",
                  border: "none",
                  borderRadius: "12px",
                  padding: "2px 8px",
                  fontSize: "0.72rem",
                  fontWeight: isActive ? 600 : 400,
                  cursor: "pointer",
                  whiteSpace: "nowrap",
                  transition: "all 0.15s",
                }}
              >
                {t(s.shortNameKey)}
              </button>
            );
          })}
        </div>

        {/* Modal Title */}
        <div
          id="getting-started-modal-title"
          style={{
            fontSize: "1.2rem",
            marginBottom: "1.25rem",
            color: "var(--text-primary)",
            fontWeight: 600,
          }}
        >
          {t(STEPS[currentStep].titleKey)}
        </div>

        {/* Body Content */}
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "1rem",
          }}
        >
          {/* STEP 0: WELCOME */}
          {currentStep === 0 && (
            <div style={{ textAlign: "center", padding: "0.5rem 0" }}>
              <div style={{ marginBottom: "0.75rem" }}>
                <LeecharrLogo size={72} />
              </div>
              <div style={{ marginBottom: "1.25rem" }}>
                <LeecharrText width={140} />
              </div>
              <p
                style={{
                  color: "var(--text-muted, #aaa)",
                  fontSize: "0.9rem",
                  lineHeight: 1.5,
                  margin: "0 0 1.25rem",
                }}
              >
                {t("gettingStarted.welcomeDescription")}
              </p>

              {/* Overview Points Card matching Seedarr layout */}
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.6rem",
                  textAlign: "left",
                  backgroundColor: "rgba(0, 0, 0, 0.25)",
                  padding: "0.9rem 1rem",
                  borderRadius: "6px",
                  border:
                    "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
                  marginBottom: "1.25rem",
                  fontSize: "0.85rem",
                  lineHeight: 1.4,
                }}
              >
                <div>
                  <strong>1. Prowlarr:</strong> {t("gettingStarted.prowlarrSyncDesc")}
                </div>
                <div>
                  <strong>2. Sonarr / Radarr / Lidarr:</strong>{" "}
                  {t("gettingStarted.sonarrDescription")}
                </div>
                <div>
                  <strong>3. Port 7889 & Metadata:</strong>{" "}
                  {t("gettingStarted.port7889Desc")}
                </div>
              </div>

              {/* Language Choice Selection Row */}
              <div
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  marginBottom: "1.25rem",
                  padding: "0.3rem 0.75rem",
                  backgroundColor: "rgba(0, 0, 0, 0.2)",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light, #1c203b)",
                }}
              >
                <span
                  style={{
                    fontSize: "0.8rem",
                    color: "var(--text-secondary)",
                  }}
                >
                  🌐 {t("gettingStarted.selectLanguage")}:
                </span>
                <LanguageSelector showFullLabel={true} align="left" />
              </div>

              {/* Action Buttons matching Seedarr */}
              <div
                style={{
                  display: "flex",
                  justifyContent: "center",
                  gap: "0.75rem",
                }}
              >
                <button
                  type="button"
                  className="btn btn-primary btn-small"
                  onClick={() => {
                    setMode("readonly");
                    setCurrentStep(1);
                  }}
                  style={{ padding: "0.45rem 1.25rem" }}
                >
                  {t("gettingStarted.startExampleTour", "Start Example Tour →")}
                </button>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={() => {
                    setMode("interactive");
                    setCurrentStep(1);
                  }}
                  style={{ padding: "0.45rem 1.25rem" }}
                >
                  ⚡ {t("gettingStarted.startLiveSetup", "Start Live Setup")}
                </button>
              </div>
            </div>
          )}

          {/* STEP 1: PROWLARR */}
          {currentStep === 1 && (
            <div>
              {isReadOnly ? (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "1rem",
                  }}
                >
                  <div
                    style={{
                      fontSize: "0.9rem",
                      color: "var(--text-secondary)",
                      lineHeight: 1.5,
                    }}
                  >
                    {t("gettingStarted.prowlarrDesc")}
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div
                      style={{
                        fontWeight: 600,
                        color: "var(--accent, #ffd166)",
                        marginBottom: "0.75rem",
                      }}
                    >
                      {t("gettingStarted.prowlarrInstructionsCardTitle")}
                    </div>
                    <ol
                      style={{
                        paddingLeft: "1.25rem",
                        margin: 0,
                        fontSize: "0.875rem",
                        color: "var(--text-secondary)",
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.5rem",
                      }}
                    >
                      <li>{t("gettingStarted.prowlarrStep1")}</li>
                      <li>{t("gettingStarted.prowlarrStep2")}</li>
                      <li>{t("gettingStarted.prowlarrStep3")}</li>
                      <li>{t("gettingStarted.prowlarrStep4")}</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.9rem",
                  }}
                >
                  <TextInput
                    label={t("common.name")}
                    value={indexerForm.name || ""}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, name: v });
                      setIndexerTestResult(null);
                    }}
                    placeholder="Prowlarr"
                  />
                  <SelectInput
                    label={t("common.type")}
                    value={indexerForm.indexerType || "Prowlarr"}
                    onChange={(v) => {
                      const defaults: Record<string, string> = {
                        Prowlarr: "http://localhost:9696",
                        Torznab: "http://localhost:9117",
                        Newznab: "http://localhost:5076",
                      };
                      setIndexerForm({
                        ...indexerForm,
                        indexerType: v,
                        url: defaults[v] || indexerForm.url || "",
                      });
                      setIndexerTestResult(null);
                    }}
                    options={[
                      { value: "Prowlarr", label: "Prowlarr" },
                      { value: "Torznab", label: "Torznab" },
                      { value: "Newznab", label: "Newznab" },
                    ]}
                  />
                  <TextInput
                    label="URL"
                    value={indexerForm.url || ""}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, url: v });
                      setIndexerTestResult(null);
                    }}
                    placeholder="http://localhost:9696"
                  />
                  <TextInput
                    label={t("settings.apiKey")}
                    value={indexerForm.apiKey || ""}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, apiKey: v });
                      setIndexerTestResult(null);
                    }}
                    type="password"
                  />
                  <TextInput
                    label={t("settingsTabs.indexers.apiPathLabel")}
                    value={indexerForm.apiPath || "/api"}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, apiPath: v });
                      setIndexerTestResult(null);
                    }}
                    placeholder="/api"
                  />
                  <TextInput
                    label={t("settingsTabs.indexers.categoriesLabel")}
                    value={
                      Array.isArray(indexerForm.categories)
                        ? indexerForm.categories.join(",")
                        : indexerForm.categories || ""
                    }
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, categories: v });
                      setIndexerTestResult(null);
                    }}
                    placeholder="2000,5000"
                  />
                  <Toggle
                    label={t("common.enabled")}
                    checked={indexerForm.enable ?? true}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, enable: v });
                      setIndexerTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("settingsTabs.indexers.rssLabel")}
                    checked={indexerForm.enableRss ?? true}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, enableRss: v });
                      setIndexerTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("common.search")}
                    checked={indexerForm.enableSearch ?? true}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, enableSearch: v });
                      setIndexerTestResult(null);
                    }}
                  />

                  {indexerTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: indexerTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: indexerTestResult.success
                          ? "var(--success, #28a745)"
                          : "var(--danger, #dc3545)",
                        border: `1px solid ${indexerTestResult.success ? "var(--success, #28a745)" : "var(--danger, #dc3545)"}`,
                      }}
                    >
                      {indexerTestResult.success
                        ? `✓ ${t("gettingStarted.connectedSuccess")}`
                        : `✗ ${indexerTestResult.message || t("settingsTabs.indexers.connectionFailed")}`}
                    </div>
                  )}

                  <div
                    style={{
                      display: "flex",
                      gap: "0.75rem",
                      marginTop: "0.5rem",
                    }}
                  >
                    <button
                      className="btn btn-secondary"
                      onClick={handleTestIndexer}
                      disabled={testIndexerMutation.isPending}
                    >
                      {testIndexerMutation.isPending
                        ? t("gettingStarted.testing")
                        : t("gettingStarted.testConnection")}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={handleSaveIndexer}
                      disabled={
                        createIndexerMutation.isPending ||
                        syncProwlarrMutation.isPending
                      }
                    >
                      {createIndexerMutation.isPending ||
                      syncProwlarrMutation.isPending
                        ? t("gettingStarted.saving")
                        : t("gettingStarted.saveAndContinue")}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* STEP 2: SONARR */}
          {currentStep === 2 && (
            <div>
              {isReadOnly ? (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "1rem",
                  }}
                >
                  <div
                    style={{
                      fontSize: "0.9rem",
                      color: "var(--text-secondary)",
                      lineHeight: 1.5,
                    }}
                  >
                    {t("gettingStarted.sonarrDescription")}
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div
                      style={{
                        fontWeight: 600,
                        color: "var(--accent, #ffd166)",
                        marginBottom: "0.75rem",
                      }}
                    >
                      {t("gettingStarted.instructionsSonarrCardTitle")}
                    </div>
                    <ol
                      style={{
                        paddingLeft: "1.25rem",
                        margin: 0,
                        fontSize: "0.875rem",
                        color: "var(--text-secondary)",
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.5rem",
                      }}
                    >
                      <li>{t("gettingStarted.sonarrStep1")}</li>
                      <li>{t("gettingStarted.sonarrStep2")}</li>
                      <li>{t("gettingStarted.sonarrStep3")}</li>
                      <li>{t("gettingStarted.sonarrStep4")}</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.9rem",
                  }}
                >
                  <TextInput
                    label={t("gettingStarted.name")}
                    value={sonarrForm.name || ""}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, name: v });
                      setSonarrTestResult(null);
                    }}
                    placeholder="Sonarr"
                  />
                  <SelectInput
                    label={t("gettingStarted.type")}
                    value={sonarrForm.arrType || "Sonarr"}
                    onChange={(v) => {
                      const defaults: Record<string, string> = {
                        Sonarr: "http://localhost:8989",
                        Radarr: "http://localhost:7878",
                        Lidarr: "http://localhost:8686",
                      };
                      setSonarrForm({
                        ...sonarrForm,
                        arrType: v,
                        url: defaults[v] || sonarrForm.url || "",
                      });
                      setSonarrTestResult(null);
                    }}
                    options={[
                      { value: "Sonarr", label: "Sonarr" },
                      { value: "Radarr", label: "Radarr" },
                      { value: "Lidarr", label: "Lidarr" },
                    ]}
                  />
                  <TextInput
                    label={t("gettingStarted.url")}
                    value={sonarrForm.url || ""}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, url: v });
                      setSonarrTestResult(null);
                    }}
                    placeholder="http://localhost:8989"
                  />
                  <TextInput
                    label={t(
                      "gettingStarted.externalUrl",
                      "Public / External URL (Optional)",
                    )}
                    value={sonarrForm.externalUrl || ""}
                    onChange={(v) => {
                      setSonarrForm({
                        ...sonarrForm,
                        externalUrl: v,
                        publicUrl: v,
                      });
                      setSonarrTestResult(null);
                    }}
                    placeholder="http://my-domain.com:8989"
                    hint={t(
                      "gettingStarted.externalUrlHint",
                      "Optional public URL for browser deep links (e.g. when accessing Leecharr remotely).",
                    )}
                  />
                  <TextInput
                    label={t("gettingStarted.apiKey")}
                    value={sonarrForm.apiKey || ""}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, apiKey: v });
                      setSonarrTestResult(null);
                    }}
                    type="password"
                  />
                  <Toggle
                    label={t("gettingStarted.enableConnection")}
                    checked={sonarrForm.enable ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, enable: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.syncEnabled")}
                    checked={sonarrForm.syncEnabled ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, syncEnabled: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.autoAdd")}
                    checked={sonarrForm.enableAutomaticAdd ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, enableAutomaticAdd: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.webhook")}
                    checked={sonarrForm.webhookEnabled ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, webhookEnabled: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  {sonarrForm.webhookEnabled !== false && (
                    <TextInput
                      label={t("gettingStarted.webhookHost")}
                      value={sonarrForm.webhookHost || ""}
                      onChange={(v) => {
                        setSonarrForm({ ...sonarrForm, webhookHost: v });
                        setSonarrTestResult(null);
                      }}
                      placeholder="leecharr"
                      hint={t(
                        "gettingStarted.webhookHostHintSonarr",
                        "Hostname or IP for Sonarr to reach Leecharr (leave empty for default)",
                      )}
                    />
                  )}

                  {sonarrTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: sonarrTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: sonarrTestResult.success
                          ? "var(--success, #28a745)"
                          : "var(--danger, #dc3545)",
                        border: `1px solid ${sonarrTestResult.success ? "var(--success, #28a745)" : "var(--danger, #dc3545)"}`,
                      }}
                    >
                      {sonarrTestResult.success
                        ? t("gettingStarted.connectionSuccess")
                        : t("gettingStarted.connectionFailed", {
                            message:
                              sonarrTestResult.message || "Connection failed",
                          })}
                    </div>
                  )}

                  <div
                    style={{
                      display: "flex",
                      gap: "0.75rem",
                      marginTop: "0.5rem",
                    }}
                  >
                    <button
                      className="btn btn-secondary"
                      onClick={() =>
                        handleTestArr(sonarrForm, setSonarrTestResult)
                      }
                      disabled={testArrMutation.isPending}
                    >
                      {testArrMutation.isPending
                        ? t("gettingStarted.testing")
                        : t("gettingStarted.testConnection")}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(sonarrForm)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending
                        ? t("gettingStarted.saving")
                        : t("gettingStarted.saveAndContinue")}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* STEP 3: RADARR */}
          {currentStep === 3 && (
            <div>
              {isReadOnly ? (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "1rem",
                  }}
                >
                  <div
                    style={{
                      fontSize: "0.9rem",
                      color: "var(--text-secondary)",
                      lineHeight: 1.5,
                    }}
                  >
                    {t("gettingStarted.radarrDescription")}
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div
                      style={{
                        fontWeight: 600,
                        color: "var(--accent, #ffd166)",
                        marginBottom: "0.75rem",
                      }}
                    >
                      {t("gettingStarted.instructionsRadarrCardTitle")}
                    </div>
                    <ol
                      style={{
                        paddingLeft: "1.25rem",
                        margin: 0,
                        fontSize: "0.875rem",
                        color: "var(--text-secondary)",
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.5rem",
                      }}
                    >
                      <li>{t("gettingStarted.radarrStep1")}</li>
                      <li>{t("gettingStarted.radarrStep2")}</li>
                      <li>{t("gettingStarted.radarrStep3")}</li>
                      <li>{t("gettingStarted.radarrStep4")}</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.9rem",
                  }}
                >
                  <TextInput
                    label={t("gettingStarted.name")}
                    value={radarrForm.name || ""}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, name: v });
                      setRadarrTestResult(null);
                    }}
                    placeholder="Radarr"
                  />
                  <SelectInput
                    label={t("gettingStarted.type")}
                    value={radarrForm.arrType || "Radarr"}
                    onChange={(v) => {
                      const defaults: Record<string, string> = {
                        Sonarr: "http://localhost:8989",
                        Radarr: "http://localhost:7878",
                        Lidarr: "http://localhost:8686",
                      };
                      setRadarrForm({
                        ...radarrForm,
                        arrType: v,
                        url: defaults[v] || radarrForm.url || "",
                      });
                      setRadarrTestResult(null);
                    }}
                    options={[
                      { value: "Sonarr", label: "Sonarr" },
                      { value: "Radarr", label: "Radarr" },
                      { value: "Lidarr", label: "Lidarr" },
                    ]}
                  />
                  <TextInput
                    label={t("gettingStarted.url")}
                    value={radarrForm.url || ""}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, url: v });
                      setRadarrTestResult(null);
                    }}
                    placeholder="http://localhost:7878"
                  />
                  <TextInput
                    label={t(
                      "gettingStarted.externalUrl",
                      "Public / External URL (Optional)",
                    )}
                    value={radarrForm.externalUrl || ""}
                    onChange={(v) => {
                      setRadarrForm({
                        ...radarrForm,
                        externalUrl: v,
                        publicUrl: v,
                      });
                      setRadarrTestResult(null);
                    }}
                    placeholder="http://my-domain.com:7878"
                    hint={t(
                      "gettingStarted.externalUrlHint",
                      "Optional public URL for browser deep links (e.g. when accessing Leecharr remotely).",
                    )}
                  />
                  <TextInput
                    label={t("gettingStarted.apiKey")}
                    value={radarrForm.apiKey || ""}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, apiKey: v });
                      setRadarrTestResult(null);
                    }}
                    type="password"
                  />
                  <Toggle
                    label={t("gettingStarted.enableConnection")}
                    checked={radarrForm.enable ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, enable: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.syncEnabled")}
                    checked={radarrForm.syncEnabled ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, syncEnabled: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.autoAdd")}
                    checked={radarrForm.enableAutomaticAdd ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, enableAutomaticAdd: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.webhook")}
                    checked={radarrForm.webhookEnabled ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, webhookEnabled: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  {radarrForm.webhookEnabled !== false && (
                    <TextInput
                      label={t("gettingStarted.webhookHost")}
                      value={radarrForm.webhookHost || ""}
                      onChange={(v) => {
                        setRadarrForm({ ...radarrForm, webhookHost: v });
                        setRadarrTestResult(null);
                      }}
                      placeholder="leecharr"
                      hint={t(
                        "gettingStarted.webhookHostHintRadarr",
                        "Hostname or IP for Radarr to reach Leecharr (leave empty for default)",
                      )}
                    />
                  )}

                  {radarrTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: radarrTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: radarrTestResult.success
                          ? "var(--success, #28a745)"
                          : "var(--danger, #dc3545)",
                        border: `1px solid ${radarrTestResult.success ? "var(--success, #28a745)" : "var(--danger, #dc3545)"}`,
                      }}
                    >
                      {radarrTestResult.success
                        ? t("gettingStarted.connectionSuccess")
                        : t("gettingStarted.connectionFailed", {
                            message:
                              radarrTestResult.message || "Connection failed",
                          })}
                    </div>
                  )}

                  <div
                    style={{
                      display: "flex",
                      gap: "0.75rem",
                      marginTop: "0.5rem",
                    }}
                  >
                    <button
                      className="btn btn-secondary"
                      onClick={() =>
                        handleTestArr(radarrForm, setRadarrTestResult)
                      }
                      disabled={testArrMutation.isPending}
                    >
                      {testArrMutation.isPending
                        ? t("gettingStarted.testing")
                        : t("gettingStarted.testConnection")}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(radarrForm)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending
                        ? t("gettingStarted.saving")
                        : t("gettingStarted.saveAndContinue")}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* STEP 4: LIDARR */}
          {currentStep === 4 && (
            <div>
              {isReadOnly ? (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "1rem",
                  }}
                >
                  <div
                    style={{
                      fontSize: "0.9rem",
                      color: "var(--text-secondary)",
                      lineHeight: 1.5,
                    }}
                  >
                    {t("gettingStarted.lidarrDescription")}
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div
                      style={{
                        fontWeight: 600,
                        color: "var(--accent, #ffd166)",
                        marginBottom: "0.75rem",
                      }}
                    >
                      {t("gettingStarted.instructionsLidarrCardTitle")}
                    </div>
                    <ol
                      style={{
                        paddingLeft: "1.25rem",
                        margin: 0,
                        fontSize: "0.875rem",
                        color: "var(--text-secondary)",
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.5rem",
                      }}
                    >
                      <li>{t("gettingStarted.lidarrStep1")}</li>
                      <li>{t("gettingStarted.lidarrStep2")}</li>
                      <li>{t("gettingStarted.lidarrStep3")}</li>
                      <li>{t("gettingStarted.lidarrStep4")}</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.9rem",
                  }}
                >
                  <TextInput
                    label={t("gettingStarted.name")}
                    value={lidarrForm.name || ""}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, name: v });
                      setLidarrTestResult(null);
                    }}
                    placeholder="Lidarr"
                  />
                  <SelectInput
                    label={t("gettingStarted.type")}
                    value={lidarrForm.arrType || "Lidarr"}
                    onChange={(v) => {
                      const defaults: Record<string, string> = {
                        Sonarr: "http://localhost:8989",
                        Radarr: "http://localhost:7878",
                        Lidarr: "http://localhost:8686",
                      };
                      setLidarrForm({
                        ...lidarrForm,
                        arrType: v,
                        url: defaults[v] || lidarrForm.url || "",
                      });
                      setLidarrTestResult(null);
                    }}
                    options={[
                      { value: "Sonarr", label: "Sonarr" },
                      { value: "Radarr", label: "Radarr" },
                      { value: "Lidarr", label: "Lidarr" },
                    ]}
                  />
                  <TextInput
                    label={t("gettingStarted.url")}
                    value={lidarrForm.url || ""}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, url: v });
                      setLidarrTestResult(null);
                    }}
                    placeholder="http://localhost:8686"
                  />
                  <TextInput
                    label={t(
                      "gettingStarted.externalUrl",
                      "Public / External URL (Optional)",
                    )}
                    value={lidarrForm.externalUrl || ""}
                    onChange={(v) => {
                      setLidarrForm({
                        ...lidarrForm,
                        externalUrl: v,
                        publicUrl: v,
                      });
                      setLidarrTestResult(null);
                    }}
                    placeholder="http://my-domain.com:8686"
                    hint={t(
                      "gettingStarted.externalUrlHint",
                      "Optional public URL for browser deep links (e.g. when accessing Leecharr remotely).",
                    )}
                  />
                  <TextInput
                    label={t("gettingStarted.apiKey")}
                    value={lidarrForm.apiKey || ""}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, apiKey: v });
                      setLidarrTestResult(null);
                    }}
                    type="password"
                  />
                  <Toggle
                    label={t("gettingStarted.enableConnection")}
                    checked={lidarrForm.enable ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, enable: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.syncEnabled")}
                    checked={lidarrForm.syncEnabled ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, syncEnabled: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.autoAdd")}
                    checked={lidarrForm.enableAutomaticAdd ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, enableAutomaticAdd: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("gettingStarted.webhook")}
                    checked={lidarrForm.webhookEnabled ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, webhookEnabled: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  {lidarrForm.webhookEnabled !== false && (
                    <TextInput
                      label={t("gettingStarted.webhookHost")}
                      value={lidarrForm.webhookHost || ""}
                      onChange={(v) => {
                        setLidarrForm({ ...lidarrForm, webhookHost: v });
                        setLidarrTestResult(null);
                      }}
                      placeholder="leecharr"
                      hint={t(
                        "gettingStarted.webhookHostHintLidarr",
                        "Hostname or IP for Lidarr to reach Leecharr (leave empty for default)",
                      )}
                    />
                  )}

                  {lidarrTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: lidarrTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: lidarrTestResult.success
                          ? "var(--success, #28a745)"
                          : "var(--danger, #dc3545)",
                        border: `1px solid ${lidarrTestResult.success ? "var(--success, #28a745)" : "var(--danger, #dc3545)"}`,
                      }}
                    >
                      {lidarrTestResult.success
                        ? t("gettingStarted.connectionSuccess")
                        : t("gettingStarted.connectionFailed", {
                            message:
                              lidarrTestResult.message || "Connection failed",
                          })}
                    </div>
                  )}

                  <div
                    style={{
                      display: "flex",
                      gap: "0.75rem",
                      marginTop: "0.5rem",
                    }}
                  >
                    <button
                      className="btn btn-secondary"
                      onClick={() =>
                        handleTestArr(lidarrForm, setLidarrTestResult)
                      }
                      disabled={testArrMutation.isPending}
                    >
                      {testArrMutation.isPending
                        ? t("gettingStarted.testing")
                        : t("gettingStarted.testConnection")}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(lidarrForm)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending
                        ? t("gettingStarted.saving")
                        : t("gettingStarted.saveAndContinue")}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* STEP 5: FINISHED */}
          {currentStep === 5 && (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "1.25rem",
                textAlign: "center",
              }}
            >
              <div style={{ fontSize: "3rem" }}>🎉</div>
              <h2
                style={{
                  fontSize: "1.5rem",
                  fontWeight: 700,
                  margin: 0,
                  color: "var(--text-primary)",
                }}
              >
                {t("gettingStarted.finishTitle")}
              </h2>
              <p
                style={{
                  color: "var(--text-secondary)",
                  maxWidth: "540px",
                  margin: "0 auto",
                  fontSize: "0.9rem",
                  lineHeight: 1.5,
                }}
              >
                {t("gettingStarted.finishDescription", { port: "7889" })}
              </p>

              <div
                style={{
                  display: "flex",
                  justifyContent: "center",
                  gap: "1rem",
                  marginTop: "0.5rem",
                }}
              >
                {onNavigateTorrents && (
                  <button
                    className="btn btn-primary"
                    onClick={() => {
                      handleClose();
                      onNavigateTorrents();
                    }}
                    style={{ padding: "0.6rem 1.25rem" }}
                  >
                    {t("gettingStarted.goToQueue")}
                  </button>
                )}
                {onNavigateIndexers && (
                  <button
                    className="btn btn-secondary"
                    onClick={() => {
                      handleClose();
                      onNavigateIndexers();
                    }}
                    style={{ padding: "0.6rem 1.25rem" }}
                  >
                    {t("gettingStarted.searchIndexers")}
                  </button>
                )}
                {onNavigateSettings && (
                  <button
                    className="btn btn-secondary"
                    onClick={() => {
                      handleClose();
                      onNavigateSettings("connections");
                    }}
                    style={{ padding: "0.6rem 1.25rem" }}
                  >
                    {t("gettingStarted.viewConnections")}
                  </button>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Bottom Footer: "Don't show this guide on startup" & Step indicator */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginTop: "1.25rem",
            paddingTop: "0.75rem",
            borderTop:
              "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
            fontSize: "0.8rem",
            color: "var(--text-muted)",
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              cursor: "pointer",
              userSelect: "none",
            }}
          >
            <input
              type="checkbox"
              checked={dontShowAgain}
              onChange={(e) => handleDontShowChange(e.target.checked)}
              style={{
                cursor: "pointer",
                accentColor: "var(--accent, #ffd166)",
              }}
            />
            <span>{t("gettingStarted.dontShowAgain")}</span>
          </label>

          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span style={{ marginRight: "0.25rem" }}>
              {t("gettingStarted.stepCount", "Step {{current}} of {{total}}", {
                current: currentStep + 1,
                total: STEPS.length,
              })}
            </span>
            {currentStep > 0 && (
              <button
                type="button"
                className="btn btn-secondary btn-small"
                onClick={handlePrev}
                style={{ padding: "0.25rem 0.6rem", fontSize: "0.75rem" }}
              >
                {t("gettingStarted.previous")}
              </button>
            )}
            {currentStep > 0 && (
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleNext}
                style={{ padding: "0.25rem 0.6rem", fontSize: "0.75rem" }}
              >
                {currentStep === STEPS.length - 1
                  ? t("gettingStarted.finishAndClose")
                  : t("gettingStarted.next")}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default GettingStartedModal;
