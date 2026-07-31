import React, { useState, useEffect, useCallback } from "react";
import {
  useCreateIndexer,
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
import { TextInput, SelectInput, Toggle, NumberInput } from "../pages/settings/shared";
import LeecharrLogo from "./icons/LeecharrLogo";
import LeecharrText from "./icons/LeecharrText";

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
  { id: "welcome", stepNum: 0, shortName: "Welcome", title: "Welcome to Leecharr" },
  { id: "prowlarr", stepNum: 1, shortName: "Prowlarr", title: "Add Indexer" },
  { id: "sonarr", stepNum: 2, shortName: "Sonarr", title: "Connect Sonarr TV" },
  { id: "radarr", stepNum: 3, shortName: "Radarr", title: "Connect Radarr Movies" },
  { id: "lidarr", stepNum: 4, shortName: "Lidarr", title: "Connect Lidarr Music" },
  { id: "finish", stepNum: 5, shortName: "Finished", title: "Setup Complete" },
];

export function GettingStartedModal({
  isOpen,
  onClose,
  onNavigateSettings,
  onNavigateTorrents,
  onNavigateIndexers,
}: GettingStartedModalProps) {
  const [currentStep, setCurrentStep] = useState(0);
  const [mode, setMode] = useState<GuideMode>("readonly");
  const [dontShowAgain, setDontShowAgain] = useState<boolean>(() => {
    return localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) === "true";
  });

  // Prowlarr Indexer Form State
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
  const [indexerTestResult, setIndexerTestResult] = useState<IndexerTestResult | null>(null);
  const [indexerSaved, setIndexerSaved] = useState(false);

  // Sonarr Form State
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
  const [sonarrTestResult, setSonarrTestResult] = useState<ArrTestResult | null>(null);
  const [sonarrSaved, setSonarrSaved] = useState(false);

  // Radarr Form State
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
  const [radarrTestResult, setRadarrTestResult] = useState<ArrTestResult | null>(null);
  const [radarrSaved, setRadarrSaved] = useState(false);

  // Lidarr Form State
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
  const [lidarrTestResult, setLidarrTestResult] = useState<ArrTestResult | null>(null);
  const [lidarrSaved, setLidarrSaved] = useState(false);

  // API Mutations
  const testIndexerMutation = useTestDirectIndexer();
  const createIndexerMutation = useCreateIndexer();

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

  // Test Handlers
  const handleTestIndexer = () => {
    setIndexerTestResult(null);
    testIndexerMutation.mutate(indexerForm, {
      onSuccess: (data) => setIndexerTestResult(data),
      onError: (err) => setIndexerTestResult({ success: false, message: err.message }),
    });
  };

  const handleSaveIndexer = () => {
    createIndexerMutation.mutate(
      {
        ...indexerForm,
        name: indexerForm.name?.trim() || "Prowlarr",
        implementation: `${indexerForm.indexerType || "Prowlarr"}Indexer`,
        configContract: "IndexerDefinition",
      },
      {
        onSuccess: () => {
          setIndexerSaved(true);
          handleNext();
        },
      }
    );
  };

  const handleTestArr = (form: Partial<ArrConnection>, setResult: (res: ArrTestResult | null) => void) => {
    setResult(null);
    testArrMutation.mutate(form, {
      onSuccess: (data) => setResult(data),
      onError: (err) => setResult({ success: false, message: err.message }),
    });
  };

  const handleSaveArr = (form: Partial<ArrConnection>, setSaved: (v: boolean) => void) => {
    createArrMutation.mutate(
      {
        ...form,
        name: form.name?.trim() || form.arrType || "Arr Connection",
        implementation: `${form.arrType || "Sonarr"}Connection`,
        configContract: "ArrConnectionDefinition",
      },
      {
        onSuccess: () => {
          setSaved(true);
          handleNext();
        },
      }
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
          <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
            <LeecharrLogo size={32} />
            <div>
              <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
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
                  SETUP GUIDE
                </span>
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginTop: "2px" }}>
                {STEPS[currentStep].title}
              </div>
            </div>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
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
                    backgroundColor: isReadOnly ? "var(--bg-card-hover, #23284b)" : "transparent",
                    color: isReadOnly ? "var(--accent, #ffd166)" : "var(--text-muted)",
                    transition: "all 0.2s",
                  }}
                >
                  📖 Guide
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
                    backgroundColor: !isReadOnly ? "var(--accent, #ffd166)" : "transparent",
                    color: !isReadOnly ? "#0d0e17" : "var(--text-muted)",
                    transition: "all 0.2s",
                  }}
                >
                  ⚡ Live Setup
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
              title="Close (Esc)"
            >
              &times;
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
                  backgroundColor: isActive ? "rgba(255, 209, 102, 0.05)" : "transparent",
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
            <div style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}>
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
                  Welcome to Leecharr
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
                  Leecharr is a high-performance BitTorrent and media downloader purpose-built for
                  the Servarr (<code>*arr</code>) ecosystem with deep media library enrichment,
                  4K/HDR stream inspection, and multi-client drop-in compatibility.
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
                  <div style={{ fontSize: "1.5rem", marginBottom: "0.4rem" }}>⚡</div>
                  <div style={{ fontWeight: 700, fontSize: "0.9rem", color: "var(--text-primary)" }}>
                    Port 7889 Client
                  </div>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-secondary)", marginTop: "4px" }}>
                    Acts directly as a qBittorrent, Deluge, or Transmission client for Sonarr and Radarr.
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
                  <div style={{ fontSize: "1.5rem", marginBottom: "0.4rem" }}>🎬</div>
                  <div style={{ fontWeight: 700, fontSize: "0.9rem", color: "var(--text-primary)" }}>
                    Media Enrichment
                  </div>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-secondary)", marginTop: "4px" }}>
                    Automatically correlates active downloads with high-res posters, banners, and 4K stream specs.
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
                  <div style={{ fontSize: "1.5rem", marginBottom: "0.4rem" }}>🔍</div>
                  <div style={{ fontWeight: 700, fontSize: "0.9rem", color: "var(--text-primary)" }}>
                    Prowlarr Sync
                  </div>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-secondary)", marginTop: "4px" }}>
                    Synchronize your Torznab indexers directly from Prowlarr for search and one-click grab.
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
                💡 <strong>Getting Started:</strong> This walkthrough will help you connect{" "}
                <strong>Prowlarr</strong>, <strong>Sonarr</strong>, <strong>Radarr</strong>, and{" "}
                <strong>Lidarr</strong> so downloads and media cards populate seamlessly.
              </div>
            </div>
          )}

          {/* STEP 1: PROWLARR */}
          {currentStep === 1 && (
            <div>
              {isReadOnly ? (
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <div style={{ fontSize: "0.9rem", color: "var(--text-secondary)", lineHeight: 1.5 }}>
                    Connect Prowlarr to automatically synchronize your configured BitTorrent indexers
                    into Leecharr for integrated search, Freeleech filtering, and RSS rules.
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      Option A: Add Leecharr as Download Client in Prowlarr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Prowlarr, go to <strong>Settings &rarr; Download Clients &rarr; Add (+)</strong>.</li>
                      <li>Select <strong>qBittorrent</strong>.</li>
                      <li>Host: <code>leecharr</code> (or your server IP), Port: <code>7889</code>.</li>
                      <li>Click <strong>Test</strong> then <strong>Save</strong>.</li>
                    </ol>
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      Option B: Import Indexers from Prowlarr into Leecharr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Prowlarr, copy your API key from <strong>Settings &rarr; General &rarr; API Key</strong>.</li>
                      <li>Switch to <strong>⚡ Live Setup</strong> above or go to <strong>Settings &rarr; Indexers</strong> in Leecharr.</li>
                      <li>Enter your Prowlarr URL (e.g. <code>http://prowlarr:9696</code>) and API Key.</li>
                      <li>Click <strong>Test & Save</strong> &mdash; indexers will sync automatically!</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <TextInput
                    label="Prowlarr Server URL"
                    value={indexerForm.url || ""}
                    onChange={(v) => setIndexerForm({ ...indexerForm, url: v })}
                    placeholder="http://localhost:9696"
                    hint="Full URL to your Prowlarr instance"
                  />
                  <TextInput
                    label="Prowlarr API Key"
                    value={indexerForm.apiKey || ""}
                    onChange={(v) => setIndexerForm({ ...indexerForm, apiKey: v })}
                    placeholder="Paste Prowlarr API key here"
                    hint="Found in Prowlarr > Settings > General"
                  />
                  <TextInput
                    label="Categories"
                    value={indexerForm.categories || "2000,5000"}
                    onChange={(v) => setIndexerForm({ ...indexerForm, categories: v })}
                    placeholder="2000,5000"
                    hint="Newznab/Torznab categories (2000=Movies, 5000=TV, 3000=Audio)"
                  />
                  <div style={{ display: "flex", gap: "1rem" }}>
                    <Toggle
                      label="Enable Search"
                      checked={indexerForm.enableSearch ?? true}
                      onChange={(v) => setIndexerForm({ ...indexerForm, enableSearch: v })}
                    />
                    <Toggle
                      label="Enable RSS Sync"
                      checked={indexerForm.enableRss ?? true}
                      onChange={(v) => setIndexerForm({ ...indexerForm, enableRss: v })}
                    />
                  </div>

                  {indexerTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: indexerTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: indexerTestResult.success ? "var(--success)" : "var(--danger)",
                        border: `1px solid ${indexerTestResult.success ? "var(--success)" : "var(--danger)"}`,
                      }}
                    >
                      {indexerTestResult.success ? "✓ Connection successful!" : `✗ ${indexerTestResult.message || "Connection failed"}`}
                    </div>
                  )}

                  <div style={{ display: "flex", gap: "0.75rem", marginTop: "0.5rem" }}>
                    <button
                      className="btn btn-secondary"
                      onClick={handleTestIndexer}
                      disabled={testIndexerMutation.isPending}
                    >
                      {testIndexerMutation.isPending ? "Testing..." : "Test Connection"}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={handleSaveIndexer}
                      disabled={createIndexerMutation.isPending}
                    >
                      {createIndexerMutation.isPending ? "Saving..." : "Save & Continue"}
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
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <div style={{ fontSize: "0.9rem", color: "var(--text-secondary)", lineHeight: 1.5 }}>
                    Connect Sonarr to Leecharr for 1:1 TV episode correlation, high-res season banners,
                    and episode thumbnails.
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      1. Add Leecharr in Sonarr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Sonarr, go to <strong>Settings &rarr; Download Clients &rarr; Add (+)</strong>.</li>
                      <li>Select <strong>qBittorrent</strong>.</li>
                      <li>Name: <code>Leecharr</code></li>
                      <li>Host: <code>leecharr</code> (or server IP / localhost), Port: <code>7889</code>.</li>
                      <li>Category: <code>tv</code> (optional, maps to TV incomplete folder).</li>
                      <li>Click <strong>Test</strong> and <strong>Save</strong>.</li>
                    </ol>
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      2. Add Sonarr API in Leecharr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Sonarr, copy your API key from <strong>Settings &rarr; General &rarr; API Key</strong>.</li>
                      <li>Switch to <strong>⚡ Live Setup</strong> above or go to <strong>Settings &rarr; Connections</strong> in Leecharr.</li>
                      <li>Enter Sonarr URL (e.g. <code>http://sonarr:8989</code>) and API Key.</li>
                      <li>Click <strong>Test & Save</strong> &mdash; media cards will immediately enrich!</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <TextInput
                    label="Sonarr Server URL"
                    value={sonarrForm.url || ""}
                    onChange={(v) => setSonarrForm({ ...sonarrForm, url: v })}
                    placeholder="http://localhost:8989"
                    hint="Full URL to your Sonarr instance"
                  />
                  <TextInput
                    label="Sonarr API Key"
                    value={sonarrForm.apiKey || ""}
                    onChange={(v) => setSonarrForm({ ...sonarrForm, apiKey: v })}
                    placeholder="Paste Sonarr API key here"
                    hint="Found in Sonarr > Settings > General"
                  />
                  <Toggle
                    label="Enable Media Enrichment Sync"
                    checked={sonarrForm.syncEnabled ?? true}
                    onChange={(v) => setSonarrForm({ ...sonarrForm, syncEnabled: v })}
                    hint="Fetch posters, banners, and episode titles for TV torrents"
                  />

                  {sonarrTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: sonarrTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: sonarrTestResult.success ? "var(--success)" : "var(--danger)",
                        border: `1px solid ${sonarrTestResult.success ? "var(--success)" : "var(--danger)"}`,
                      }}
                    >
                      {sonarrTestResult.success ? "✓ Connection successful!" : `✗ ${sonarrTestResult.message || "Connection failed"}`}
                    </div>
                  )}

                  <div style={{ display: "flex", gap: "0.75rem", marginTop: "0.5rem" }}>
                    <button
                      className="btn btn-secondary"
                      onClick={() => handleTestArr(sonarrForm, setSonarrTestResult)}
                      disabled={testArrMutation.isPending}
                    >
                      {testArrMutation.isPending ? "Testing..." : "Test Connection"}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(sonarrForm, setSonarrSaved)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending ? "Saving..." : "Save & Continue"}
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
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <div style={{ fontSize: "0.9rem", color: "var(--text-secondary)", lineHeight: 1.5 }}>
                    Connect Radarr to Leecharr for high-res movie backdrops, posters, cast overviews,
                    and 4K/HDR10+/Dolby Vision stream metadata.
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      1. Add Leecharr in Radarr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Radarr, go to <strong>Settings &rarr; Download Clients &rarr; Add (+)</strong>.</li>
                      <li>Select <strong>qBittorrent</strong>.</li>
                      <li>Name: <code>Leecharr</code></li>
                      <li>Host: <code>leecharr</code> (or server IP / localhost), Port: <code>7889</code>.</li>
                      <li>Category: <code>movies</code>.</li>
                      <li>Click <strong>Test</strong> and <strong>Save</strong>.</li>
                    </ol>
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      2. Add Radarr API in Leecharr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Radarr, copy your API key from <strong>Settings &rarr; General &rarr; API Key</strong>.</li>
                      <li>Switch to <strong>⚡ Live Setup</strong> above or go to <strong>Settings &rarr; Connections</strong> in Leecharr.</li>
                      <li>Enter Radarr URL (e.g. <code>http://radarr:7878</code>) and API Key.</li>
                      <li>Click <strong>Test & Save</strong>.</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <TextInput
                    label="Radarr Server URL"
                    value={radarrForm.url || ""}
                    onChange={(v) => setRadarrForm({ ...radarrForm, url: v })}
                    placeholder="http://localhost:7878"
                    hint="Full URL to your Radarr instance"
                  />
                  <TextInput
                    label="Radarr API Key"
                    value={radarrForm.apiKey || ""}
                    onChange={(v) => setRadarrForm({ ...radarrForm, apiKey: v })}
                    placeholder="Paste Radarr API key here"
                    hint="Found in Radarr > Settings > General"
                  />
                  <Toggle
                    label="Enable Movie Enrichment Sync"
                    checked={radarrForm.syncEnabled ?? true}
                    onChange={(v) => setRadarrForm({ ...radarrForm, syncEnabled: v })}
                    hint="Fetch posters, backdrops, and movie details"
                  />

                  {radarrTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: radarrTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: radarrTestResult.success ? "var(--success)" : "var(--danger)",
                        border: `1px solid ${radarrTestResult.success ? "var(--success)" : "var(--danger)"}`,
                      }}
                    >
                      {radarrTestResult.success ? "✓ Connection successful!" : `✗ ${radarrTestResult.message || "Connection failed"}`}
                    </div>
                  )}

                  <div style={{ display: "flex", gap: "0.75rem", marginTop: "0.5rem" }}>
                    <button
                      className="btn btn-secondary"
                      onClick={() => handleTestArr(radarrForm, setRadarrTestResult)}
                      disabled={testArrMutation.isPending}
                    >
                      {testArrMutation.isPending ? "Testing..." : "Test Connection"}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(radarrForm, setRadarrSaved)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending ? "Saving..." : "Save & Continue"}
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
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <div style={{ fontSize: "0.9rem", color: "var(--text-secondary)", lineHeight: 1.5 }}>
                    Connect Lidarr to Leecharr for album artwork, artist backdrops, track lists,
                    and FLAC/lossless audio stream specs.
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      1. Add Leecharr in Lidarr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Lidarr, go to <strong>Settings &rarr; Download Clients &rarr; Add (+)</strong>.</li>
                      <li>Select <strong>qBittorrent</strong>.</li>
                      <li>Name: <code>Leecharr</code></li>
                      <li>Host: <code>leecharr</code> (or server IP / localhost), Port: <code>7889</code>.</li>
                      <li>Category: <code>music</code>.</li>
                      <li>Click <strong>Test</strong> and <strong>Save</strong>.</li>
                    </ol>
                  </div>

                  <div
                    className="card"
                    style={{
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      backgroundColor: "rgba(0, 0, 0, 0.2)",
                      border: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <div style={{ fontWeight: 600, color: "var(--accent, #ffd166)", marginBottom: "0.5rem" }}>
                      2. Add Lidarr API in Leecharr
                    </div>
                    <ol style={{ paddingLeft: "1.25rem", margin: 0, fontSize: "0.85rem", color: "var(--text-secondary)", display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                      <li>In Lidarr, copy your API key from <strong>Settings &rarr; General &rarr; API Key</strong>.</li>
                      <li>Switch to <strong>⚡ Live Setup</strong> above or go to <strong>Settings &rarr; Connections</strong> in Leecharr.</li>
                      <li>Enter Lidarr URL (e.g. <code>http://lidarr:8686</code>) and API Key.</li>
                      <li>Click <strong>Test & Save</strong>.</li>
                    </ol>
                  </div>
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
                  <TextInput
                    label="Lidarr Server URL"
                    value={lidarrForm.url || ""}
                    onChange={(v) => setLidarrForm({ ...lidarrForm, url: v })}
                    placeholder="http://localhost:8686"
                    hint="Full URL to your Lidarr instance"
                  />
                  <TextInput
                    label="Lidarr API Key"
                    value={lidarrForm.apiKey || ""}
                    onChange={(v) => setLidarrForm({ ...lidarrForm, apiKey: v })}
                    placeholder="Paste Lidarr API key here"
                    hint="Found in Lidarr > Settings > General"
                  />
                  <Toggle
                    label="Enable Music Enrichment Sync"
                    checked={lidarrForm.syncEnabled ?? true}
                    onChange={(v) => setLidarrForm({ ...lidarrForm, syncEnabled: v })}
                    hint="Fetch album art, artist backdrops, and track details"
                  />

                  {lidarrTestResult && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                        fontSize: "0.85rem",
                        backgroundColor: lidarrTestResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: lidarrTestResult.success ? "var(--success)" : "var(--danger)",
                        border: `1px solid ${lidarrTestResult.success ? "var(--success)" : "var(--danger)"}`,
                      }}
                    >
                      {lidarrTestResult.success ? "✓ Connection successful!" : `✗ ${lidarrTestResult.message || "Connection failed"}`}
                    </div>
                  )}

                  <div style={{ display: "flex", gap: "0.75rem", marginTop: "0.5rem" }}>
                    <button
                      className="btn btn-secondary"
                      onClick={() => handleTestArr(lidarrForm, setLidarrTestResult)}
                      disabled={testArrMutation.isPending}
                    >
                      {testArrMutation.isPending ? "Testing..." : "Test Connection"}
                    </button>
                    <button
                      className="btn btn-primary"
                      onClick={() => handleSaveArr(lidarrForm, setLidarrSaved)}
                      disabled={createArrMutation.isPending}
                    >
                      {createArrMutation.isPending ? "Saving..." : "Save & Continue"}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* STEP 5: FINISHED */}
          {currentStep === 5 && (
            <div style={{ display: "flex", flexDirection: "column", gap: "1.25rem", textAlign: "center" }}>
              <div style={{ fontSize: "3rem" }}>🎉</div>
              <h2 style={{ fontSize: "1.5rem", fontWeight: 700, margin: 0, color: "var(--text-primary)" }}>
                You're Ready to Download!
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
                Leecharr is now configured and actively serving on port <code>7889</code>. You can grab
                torrents directly from integrated search or push them automatically from Sonarr and Radarr.
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
                    📁 Go to Queue & Torrents
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
                    🔍 Search Indexers
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
                    ⚙️ View Connections
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
            Don't show this guide on startup
          </label>

          <div style={{ display: "flex", gap: "0.75rem" }}>
            {currentStep > 0 && (
              <button className="btn btn-secondary btn-small" onClick={handlePrev}>
                &larr; Previous
              </button>
            )}
            <button className="btn btn-primary btn-small" onClick={handleNext}>
              {currentStep === STEPS.length - 1 ? "Finish & Close" : "Next &rarr;"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export default GettingStartedModal;
