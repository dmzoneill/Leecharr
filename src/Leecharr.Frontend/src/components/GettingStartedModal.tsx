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
import { useEscapeKey } from "../hooks/useEscapeKey";

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
  shortName: string;
  title: string;
}

const STEPS: StepMeta[] = [
  {
    id: "welcome",
    stepNum: 0,
    shortName: "Welcome",
    title: "Welcome to Leecharr",
  },
  {
    id: "prowlarr",
    stepNum: 1,
    shortName: "Prowlarr",
    title: "Add Prowlarr Indexer",
  },
  {
    id: "sonarr",
    stepNum: 2,
    shortName: "Sonarr",
    title: "Add Sonarr Connection",
  },
  {
    id: "radarr",
    stepNum: 3,
    shortName: "Radarr",
    title: "Add Radarr Connection",
  },
  {
    id: "lidarr",
    stepNum: 4,
    shortName: "Lidarr",
    title: "Add Lidarr Connection",
  },
  { id: "finish", stepNum: 5, shortName: "Finished", title: "Setup Complete" },
];

export function GettingStartedModal({
  isOpen,
  onClose,
  onNavigateSettings,
  onNavigateTorrents,
  onNavigateIndexers,
}: GettingStartedModalProps) {
  const { t } = useTranslation();
  useEscapeKey(onClose, isOpen);

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
      onClick={handleClose}
    >
      <div
        className="modal-content"
        style={{
          width: "100%",
          maxWidth: "860px",
          maxHeight: "90vh",
          backgroundColor: "var(--bg-secondary, #171b35)",
          borderRadius: "12px",
          border: "1px solid var(--border-light, #1c203b)",
          boxShadow: "0 20px 50px rgba(0, 0, 0, 0.6)",
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          style={{
            padding: "1.25rem 1.75rem",
            borderBottom: "1px solid var(--border-light, #1c203b)",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            backgroundColor: "rgba(0, 0, 0, 0.2)",
          }}
        >
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <LeecharrLogo size={32} />
            <div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
              >
                <LeecharrText width={90} />
                <span
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.15rem 0.5rem",
                    borderRadius: "4px",
                    backgroundColor: "rgba(255, 209, 102, 0.15)",
                    color: "var(--accent, #ffd166)",
                    fontWeight: 600,
                  }}
                >
                  {t("autogen.t_setup_guide")}
                </span>
              </div>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted)",
                  marginTop: "2px",
                }}
              >
                {STEPS[currentStep].title}
              </div>
            </div>
          </div>

          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            {/* Mode Switcher */}
            {currentStep > 0 && currentStep < STEPS.length - 1 && (
              <div
                style={{
                  display: "flex",
                  backgroundColor: "rgba(0, 0, 0, 0.3)",
                  borderRadius: "6px",
                  padding: "2px",
                  border: "1px solid var(--border-light, #1c203b)",
                }}
              >
                <button
                  type="button"
                  onClick={() => setMode("readonly")}
                  style={{
                    padding: "0.3rem 0.7rem",
                    borderRadius: "4px",
                    border: "none",
                    fontSize: "0.75rem",
                    fontWeight: 600,
                    cursor: "pointer",
                    backgroundColor: isReadOnly
                      ? "var(--bg-card-hover, #23284b)"
                      : "transparent",
                    color: isReadOnly
                      ? "var(--accent, #ffd166)"
                      : "var(--text-muted)",
                    transition: "all 0.2s",
                  }}
                >
                  {t("autogen.t_guide")}
                </button>
                <button
                  type="button"
                  onClick={() => setMode("interactive")}
                  style={{
                    padding: "0.3rem 0.7rem",
                    borderRadius: "4px",
                    border: "none",
                    fontSize: "0.75rem",
                    fontWeight: 600,
                    cursor: "pointer",
                    backgroundColor: !isReadOnly
                      ? "var(--accent, #ffd166)"
                      : "transparent",
                    color: !isReadOnly ? "#0d0e17" : "var(--text-muted)",
                    transition: "all 0.2s",
                  }}
                >
                  {t("autogen.t_live_setup")}
                </button>
              </div>
            )}

            <button
              onClick={handleClose}
              style={{
                background: "transparent",
                border: "none",
                color: "var(--text-muted)",
                fontSize: "1.5rem",
                cursor: "pointer",
                padding: "0.25rem 0.5rem",
                borderRadius: "4px",
                lineHeight: 1,
              }}
              title={t("autogen.t_close_esc")}
            >
              {t("autogen.t_times")}
            </button>
          </div>
        </div>

        {/* Step Progress Bar */}
        <div
          style={{
            display: "flex",
            borderBottom: "1px solid var(--border-light, #1c203b)",
            backgroundColor: "rgba(0, 0, 0, 0.15)",
          }}
        >
          {STEPS.map((s, idx) => {
            const isActive = idx === currentStep;
            const isCompleted = idx < currentStep;
            return (
              <div
                key={s.id}
                onClick={() => setCurrentStep(idx)}
                style={{
                  flex: 1,
                  padding: "0.6rem 0.5rem",
                  textAlign: "center",
                  cursor: "pointer",
                  fontSize: "0.75rem",
                  fontWeight: isActive ? 700 : 500,
                  color: isActive
                    ? "var(--accent, #ffd166)"
                    : isCompleted
                      ? "var(--success, #28a745)"
                      : "var(--text-muted)",
                  borderBottom: isActive
                    ? "2px solid var(--accent, #ffd166)"
                    : isCompleted
                      ? "2px solid var(--success, #28a745)"
                      : "2px solid transparent",
                  backgroundColor: isActive
                    ? "rgba(255, 209, 102, 0.05)"
                    : "transparent",
                  transition: "all 0.2s",
                  whiteSpace: "nowrap",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                }}
              >
                {isCompleted ? "✓ " : `${idx + 1}. `}
                {s.shortName}
              </div>
            );
          })}
        </div>

        {/* Body Content */}
        <div
          style={{
            flex: 1,
            overflowY: "auto",
            padding: "1.5rem 2rem",
            display: "flex",
            flexDirection: "column",
            gap: "1.25rem",
          }}
        >
          {/* STEP 0: WELCOME */}
          {currentStep === 0 && (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "1.25rem",
              }}
            >
              <div
                style={{
                  textAlign: "center",
                  padding: "1.5rem 1rem",
                  backgroundColor: "rgba(255, 209, 102, 0.04)",
                  borderRadius: "10px",
                  border: "1px solid rgba(255, 209, 102, 0.15)",
                }}
              >
                <LeecharrLogo size={72} />
                <h2
                  style={{
                    fontSize: "1.5rem",
                    fontWeight: 700,
                    marginTop: "0.75rem",
                    marginBottom: "0.5rem",
                    color: "var(--text-primary)",
                  }}
                >
                  {t("autogen.t_welcome_to_leecharr")}
                </h2>
                <p
                  style={{
                    color: "var(--text-secondary)",
                    maxWidth: "580px",
                    margin: "0 auto",
                    fontSize: "0.9rem",
                    lineHeight: 1.5,
                  }}
                >
                  {t("autogen.t_leecharr_is_a_high_performance_bittorren")}
                  <code>{t("autogen.t_arr")}</code>
                  {t("autogen.t_ecosystem_with_deep_media_library_enrich")}
                </p>
              </div>

              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(3, 1fr)",
                  gap: "1rem",
                }}
              >
                <div
                  className="card"
                  style={{
                    padding: "1rem",
                    borderRadius: "8px",
                    backgroundColor: "rgba(0, 0, 0, 0.2)",
                    border: "1px solid var(--border-light, #1c203b)",
                  }}
                >
                  <div style={{ fontSize: "1.5rem", marginBottom: "0.4rem" }}>
                    ⚡
                  </div>
                  <div
                    style={{
                      fontWeight: 700,
                      fontSize: "0.9rem",
                      color: "var(--text-primary)",
                    }}
                  >
                    {t("autogen.t_port_7889_engine")}
                  </div>
                  <div
                    style={{
                      fontSize: "0.8rem",
                      color: "var(--text-secondary)",
                      marginTop: "4px",
                    }}
                  >
                    {t("autogen.t_pure_c_bittorrent_engine_running_with_si")}
                  </div>
                </div>

                <div
                  className="card"
                  style={{
                    padding: "1rem",
                    borderRadius: "8px",
                    backgroundColor: "rgba(0, 0, 0, 0.2)",
                    border: "1px solid var(--border-light, #1c203b)",
                  }}
                >
                  <div style={{ fontSize: "1.5rem", marginBottom: "0.4rem" }}>
                    🎬
                  </div>
                  <div
                    style={{
                      fontWeight: 700,
                      fontSize: "0.9rem",
                      color: "var(--text-primary)",
                    }}
                  >
                    {t("autogen.t_media_enrichment")}
                  </div>
                  <div
                    style={{
                      fontSize: "0.8rem",
                      color: "var(--text-secondary)",
                      marginTop: "4px",
                    }}
                  >
                    {t("autogen.t_correlates_active_downloads_with_high_re")}
                  </div>
                </div>

                <div
                  className="card"
                  style={{
                    padding: "1rem",
                    borderRadius: "8px",
                    backgroundColor: "rgba(0, 0, 0, 0.2)",
                    border: "1px solid var(--border-light, #1c203b)",
                  }}
                >
                  <div style={{ fontSize: "1.5rem", marginBottom: "0.4rem" }}>
                    🔍
                  </div>
                  <div
                    style={{
                      fontWeight: 700,
                      fontSize: "0.9rem",
                      color: "var(--text-primary)",
                    }}
                  >
                    {t("autogen.t_prowlarr_sync")}
                  </div>
                  <div
                    style={{
                      fontSize: "0.8rem",
                      color: "var(--text-secondary)",
                      marginTop: "4px",
                    }}
                  >
                    {t("autogen.t_synchronize_torznab_indexers_directly_fr")}
                  </div>
                </div>
              </div>

              <div
                style={{
                  backgroundColor: "rgba(255, 209, 102, 0.08)",
                  border: "1px solid rgba(255, 209, 102, 0.2)",
                  borderRadius: "8px",
                  padding: "0.9rem 1.2rem",
                  fontSize: "0.85rem",
                  color: "var(--text-secondary)",
                }}
              >
                💡 <strong>{t("autogen.t_getting_started")}</strong>{" "}
                {t("autogen.t_follow_these_steps_to_connect")}
                <strong>{t("autogen.t_prowlarr")}</strong>,{" "}
                <strong>{t("autogen.t_sonarr")}</strong>,{" "}
                <strong>{t("autogen.t_radarr")}</strong>
                {t("autogen.t_and")}
                <strong>{t("autogen.t_lidarr")}</strong>{" "}
                {t("autogen.t_so_downloads_and_media_cards_populate_se")}
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
                    {t("autogen.t_connect_prowlarr_to_automatically_import")}
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
                      {t("autogen.t_import_indexers_from_prowlarr")}
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
                      <li>
                        {t("autogen.t_in_prowlarr_go_to")}{" "}
                        <strong>
                          {t("autogen.t_settings_general_security")}
                        </strong>{" "}
                        {t("autogen.t_and_copy_your")}
                        <strong>{t("autogen.t_api_key")}</strong>.
                      </li>
                      <li>
                        {t("autogen.t_switch_to")}
                        <strong>{t("autogen.t_live_setup")}</strong>{" "}
                        {t("autogen.t_above_or_go_to")}{" "}
                        <strong>{t("autogen.t_settings_indexers")}</strong>).
                      </li>
                      <li>
                        {t("autogen.t_enter_your_prowlarr_server_url_e_g")}{" "}
                        <code>{t("autogen.t_http_localhost_9696")}</code>{" "}
                        {t("autogen.t_or")}{" "}
                        <code>{t("autogen.t_http_prowlarr_9696")}</code>
                        {t("autogen.t_and_paste_your_api_key")}
                      </li>
                      <li>
                        {t("autogen.t_click")}
                        <strong>{t("autogen.t_test_connection")}</strong>{" "}
                        {t("autogen.t_and")}{" "}
                        <strong>{t("autogen.t_save_continue")}</strong>{" "}
                        {t(
                          "autogen.t_leecharr_will_automatically_import_and_s",
                        )}
                      </li>
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
                    label={t("autogen.t_name")}
                    value={indexerForm.name || ""}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, name: v });
                      setIndexerTestResult(null);
                    }}
                    placeholder={t("autogen.t_my_prowlarr")}
                  />
                  <SelectInput
                    label={t("autogen.t_type")}
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
                    label={t("autogen.t_url")}
                    value={indexerForm.url || ""}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, url: v });
                      setIndexerTestResult(null);
                    }}
                    placeholder={t("autogen.t_http_localhost_9696")}
                  />
                  <TextInput
                    label={t("autogen.t_api_key")}
                    value={indexerForm.apiKey || ""}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, apiKey: v });
                      setIndexerTestResult(null);
                    }}
                    type="password"
                  />
                  <TextInput
                    label={t("autogen.t_api_path")}
                    value={indexerForm.apiPath || "/api"}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, apiPath: v });
                      setIndexerTestResult(null);
                    }}
                    placeholder={t("autogen.t_api")}
                  />
                  <TextInput
                    label={t("autogen.t_categories")}
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
                    label={t("autogen.t_enable")}
                    checked={indexerForm.enable ?? true}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, enable: v });
                      setIndexerTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_rss")}
                    checked={indexerForm.enableRss ?? true}
                    onChange={(v) => {
                      setIndexerForm({ ...indexerForm, enableRss: v });
                      setIndexerTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_search")}
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
                        ? "✓ Connection successful!"
                        : `✗ ${indexerTestResult.message || "Connection failed"}`}
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
                        ? "Testing..."
                        : "Test Connection"}
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
                        ? "Saving..."
                        : "Save & Continue"}
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
                    {t("autogen.t_connect_sonarr_to_leecharr_for_1_1_tv_ep")}
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
                      {t("autogen.t_connect_sonarr_library")}
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
                      <li>
                        {t("autogen.t_in_sonarr_go_to")}{" "}
                        <strong>
                          {t("autogen.t_settings_general_security")}
                        </strong>{" "}
                        {t("autogen.t_and_copy_your")}
                        <strong>{t("autogen.t_api_key")}</strong>.
                      </li>
                      <li>
                        {t("autogen.t_switch_to")}
                        <strong>{t("autogen.t_live_setup")}</strong>{" "}
                        {t("autogen.t_above_or_go_to")}{" "}
                        <strong>{t("autogen.t_settings_connections")}</strong>).
                      </li>
                      <li>
                        {t("autogen.t_enter_sonarr_url_e_g")}{" "}
                        <code>{t("autogen.t_http_localhost_8989")}</code>{" "}
                        {t("autogen.t_or")}{" "}
                        <code>{t("autogen.t_http_sonarr_8989")}</code>
                        {t("autogen.t_and_paste_your_api_key")}
                      </li>
                      <li>
                        {t("autogen.t_keep")}
                        <strong>{t("autogen.t_sync_enabled")}</strong>{" "}
                        {t("autogen.t_and")}{" "}
                        <strong>{t("autogen.t_webhook")}</strong>{" "}
                        {t("autogen.t_active")}
                      </li>
                      <li>
                        {t("autogen.t_click")}
                        <strong>{t("autogen.t_test_connection")}</strong>{" "}
                        {t("autogen.t_and")}{" "}
                        <strong>{t("autogen.t_save_continue")}</strong>{" "}
                        {t("autogen.t_media_cards_will_enrich_instantly")}
                      </li>
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
                    label={t("autogen.t_name")}
                    value={sonarrForm.name || ""}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, name: v });
                      setSonarrTestResult(null);
                    }}
                    placeholder={t("autogen.t_sonarr")}
                  />
                  <SelectInput
                    label={t("autogen.t_type")}
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
                    label={t("autogen.t_url")}
                    value={sonarrForm.url || ""}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, url: v });
                      setSonarrTestResult(null);
                    }}
                    placeholder={t("autogen.t_http_localhost_8989")}
                  />
                  <TextInput
                    label={t("autogen.t_api_key")}
                    value={sonarrForm.apiKey || ""}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, apiKey: v });
                      setSonarrTestResult(null);
                    }}
                    type="password"
                  />
                  <Toggle
                    label={t("autogen.t_enable_connection")}
                    checked={sonarrForm.enable ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, enable: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_sync_enabled")}
                    checked={sonarrForm.syncEnabled ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, syncEnabled: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_auto_add")}
                    checked={sonarrForm.enableAutomaticAdd ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, enableAutomaticAdd: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_webhook")}
                    checked={sonarrForm.webhookEnabled ?? true}
                    onChange={(v) => {
                      setSonarrForm({ ...sonarrForm, webhookEnabled: v });
                      setSonarrTestResult(null);
                    }}
                  />
                  {sonarrForm.webhookEnabled !== false && (
                    <TextInput
                      label={t("autogen.t_webhook_host")}
                      value={sonarrForm.webhookHost || ""}
                      onChange={(v) => {
                        setSonarrForm({ ...sonarrForm, webhookHost: v });
                        setSonarrTestResult(null);
                      }}
                      placeholder={t("autogen.t_leecharr")}
                      hint="Hostname or IP for Sonarr to reach Leecharr (leave empty for default)"
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
                        ? "✓ Connection successful!"
                        : `✗ ${sonarrTestResult.message || "Connection failed"}`}
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
                        ? "Testing..."
                        : "Test Connection"}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(sonarrForm)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending
                        ? "Saving..."
                        : "Save & Continue"}
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
                    {t("autogen.t_connect_radarr_to_leecharr_for_high_res_")}
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
                      {t("autogen.t_connect_radarr_library")}
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
                      <li>
                        {t("autogen.t_in_radarr_go_to")}{" "}
                        <strong>
                          {t("autogen.t_settings_general_security")}
                        </strong>{" "}
                        {t("autogen.t_and_copy_your")}
                        <strong>{t("autogen.t_api_key")}</strong>.
                      </li>
                      <li>
                        {t("autogen.t_switch_to")}
                        <strong>{t("autogen.t_live_setup")}</strong>{" "}
                        {t("autogen.t_above_or_go_to")}{" "}
                        <strong>{t("autogen.t_settings_connections")}</strong>).
                      </li>
                      <li>
                        {t("autogen.t_enter_radarr_url_e_g")}{" "}
                        <code>{t("autogen.t_http_localhost_7878")}</code>{" "}
                        {t("autogen.t_or")}{" "}
                        <code>{t("autogen.t_http_radarr_7878")}</code>
                        {t("autogen.t_and_paste_your_api_key")}
                      </li>
                      <li>
                        {t("autogen.t_click")}
                        <strong>{t("autogen.t_test_connection")}</strong>{" "}
                        {t("autogen.t_and")}{" "}
                        <strong>{t("autogen.t_save_continue")}</strong>.
                      </li>
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
                    label={t("autogen.t_name")}
                    value={radarrForm.name || ""}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, name: v });
                      setRadarrTestResult(null);
                    }}
                    placeholder={t("autogen.t_radarr")}
                  />
                  <SelectInput
                    label={t("autogen.t_type")}
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
                    label={t("autogen.t_url")}
                    value={radarrForm.url || ""}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, url: v });
                      setRadarrTestResult(null);
                    }}
                    placeholder={t("autogen.t_http_localhost_7878")}
                  />
                  <TextInput
                    label={t("autogen.t_api_key")}
                    value={radarrForm.apiKey || ""}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, apiKey: v });
                      setRadarrTestResult(null);
                    }}
                    type="password"
                  />
                  <Toggle
                    label={t("autogen.t_enable_connection")}
                    checked={radarrForm.enable ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, enable: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_sync_enabled")}
                    checked={radarrForm.syncEnabled ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, syncEnabled: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_auto_add")}
                    checked={radarrForm.enableAutomaticAdd ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, enableAutomaticAdd: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_webhook")}
                    checked={radarrForm.webhookEnabled ?? true}
                    onChange={(v) => {
                      setRadarrForm({ ...radarrForm, webhookEnabled: v });
                      setRadarrTestResult(null);
                    }}
                  />
                  {radarrForm.webhookEnabled !== false && (
                    <TextInput
                      label={t("autogen.t_webhook_host")}
                      value={radarrForm.webhookHost || ""}
                      onChange={(v) => {
                        setRadarrForm({ ...radarrForm, webhookHost: v });
                        setRadarrTestResult(null);
                      }}
                      placeholder={t("autogen.t_leecharr")}
                      hint="Hostname or IP for Radarr to reach Leecharr (leave empty for default)"
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
                        ? "✓ Connection successful!"
                        : `✗ ${radarrTestResult.message || "Connection failed"}`}
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
                        ? "Testing..."
                        : "Test Connection"}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(radarrForm)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending
                        ? "Saving..."
                        : "Save & Continue"}
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
                    {t("autogen.t_connect_lidarr_to_leecharr_for_album_art")}
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
                      {t("autogen.t_connect_lidarr_library")}
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
                      <li>
                        {t("autogen.t_in_lidarr_go_to")}{" "}
                        <strong>
                          {t("autogen.t_settings_general_security")}
                        </strong>{" "}
                        {t("autogen.t_and_copy_your")}
                        <strong>{t("autogen.t_api_key")}</strong>.
                      </li>
                      <li>
                        {t("autogen.t_switch_to")}
                        <strong>{t("autogen.t_live_setup")}</strong>{" "}
                        {t("autogen.t_above_or_go_to")}{" "}
                        <strong>{t("autogen.t_settings_connections")}</strong>).
                      </li>
                      <li>
                        {t("autogen.t_enter_lidarr_url_e_g")}{" "}
                        <code>{t("autogen.t_http_localhost_8686")}</code>{" "}
                        {t("autogen.t_or")}{" "}
                        <code>{t("autogen.t_http_lidarr_8686")}</code>
                        {t("autogen.t_and_paste_your_api_key")}
                      </li>
                      <li>
                        {t("autogen.t_click")}
                        <strong>{t("autogen.t_test_connection")}</strong>{" "}
                        {t("autogen.t_and")}{" "}
                        <strong>{t("autogen.t_save_continue")}</strong>.
                      </li>
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
                    label={t("autogen.t_name")}
                    value={lidarrForm.name || ""}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, name: v });
                      setLidarrTestResult(null);
                    }}
                    placeholder={t("autogen.t_lidarr")}
                  />
                  <SelectInput
                    label={t("autogen.t_type")}
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
                    label={t("autogen.t_url")}
                    value={lidarrForm.url || ""}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, url: v });
                      setLidarrTestResult(null);
                    }}
                    placeholder={t("autogen.t_http_localhost_8686")}
                  />
                  <TextInput
                    label={t("autogen.t_api_key")}
                    value={lidarrForm.apiKey || ""}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, apiKey: v });
                      setLidarrTestResult(null);
                    }}
                    type="password"
                  />
                  <Toggle
                    label={t("autogen.t_enable_connection")}
                    checked={lidarrForm.enable ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, enable: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_sync_enabled")}
                    checked={lidarrForm.syncEnabled ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, syncEnabled: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_auto_add")}
                    checked={lidarrForm.enableAutomaticAdd ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, enableAutomaticAdd: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  <Toggle
                    label={t("autogen.t_webhook")}
                    checked={lidarrForm.webhookEnabled ?? true}
                    onChange={(v) => {
                      setLidarrForm({ ...lidarrForm, webhookEnabled: v });
                      setLidarrTestResult(null);
                    }}
                  />
                  {lidarrForm.webhookEnabled !== false && (
                    <TextInput
                      label={t("autogen.t_webhook_host")}
                      value={lidarrForm.webhookHost || ""}
                      onChange={(v) => {
                        setLidarrForm({ ...lidarrForm, webhookHost: v });
                        setLidarrTestResult(null);
                      }}
                      placeholder={t("autogen.t_leecharr")}
                      hint="Hostname or IP for Lidarr to reach Leecharr (leave empty for default)"
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
                        ? "✓ Connection successful!"
                        : `✗ ${lidarrTestResult.message || "Connection failed"}`}
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
                        ? "Testing..."
                        : "Test Connection"}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(lidarrForm)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending
                        ? "Saving..."
                        : "Save & Continue"}
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
                {t("autogen.t_you_apos_re_ready_to_download")}
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
                {t("autogen.t_leecharr_is_now_configured_and_actively_")}{" "}
                <code>7889</code>
                {t("autogen.t_you_can_grab_torrents_directly_from_inte")}
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
                    {t("autogen.t_go_to_queue_torrents")}
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
                    {t("autogen.t_search_indexers")}
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
                    {t("autogen.t_view_connections")}
                  </button>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div
          style={{
            padding: "1rem 1.75rem",
            borderTop: "1px solid var(--border-light, #1c203b)",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            backgroundColor: "rgba(0, 0, 0, 0.2)",
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={dontShowAgain}
              onChange={(e) => handleDontShowChange(e.target.checked)}
            />
            {t("autogen.t_don_apos_t_show_this_guide_on_startup")}
          </label>

          <div style={{ display: "flex", gap: "0.75rem" }}>
            {currentStep > 0 && (
              <button
                className="btn btn-secondary btn-small"
                onClick={handlePrev}
              >
                {t("autogen.t_previous")}
              </button>
            )}
            <button className="btn btn-primary btn-small" onClick={handleNext}>
              {currentStep === STEPS.length - 1 ? "Finish & Close" : "Next →"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export default GettingStartedModal;
