import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useAiConfig,
  useSaveAiConfig,
  useSubsystems,
  useSwitchSubsystem,
  useProbeSubsystemProvider,
} from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import { SectionCard, SaveBar } from "./shared";
import {
  SparklesIcon,
  BotIcon,
  ShieldCheckIcon,
  RefreshIcon,
  CheckCircleIcon,
  AlertIcon,
} from "../../components/icons/AiIcons";
import type { AiConfig, SubsystemProbeResult } from "../../api/types";

export function AiTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const { data: config, isLoading: configLoading } = useAiConfig();
  const saveConfig = useSaveAiConfig();
  const { data: subsystems } = useSubsystems();
  const switchSubsystem = useSwitchSubsystem();
  const probeProvider = useProbeSubsystemProvider();

  const [formData, setFormData] = useState<AiConfig>({
    activeAiProvider: "RuleHeuristic",
    ollamaHost: "http://127.0.0.1:11434",
    ollamaModel: "llama3",
    geminiApiKey: "",
    geminiModel: "gemini-2.0-flash",
    onnxModelPath: "/config/models/leecharr-ai.onnx",
    enableCopilotButton: true,
    enableNaturalSearch: true,
    enableSwarmDiagnostics: true,
  });

  const [probeResult, setProbeResult] = useState<SubsystemProbeResult | null>(
    null,
  );
  const [probeLoadingId, setProbeLoadingId] = useState<string | null>(null);
  const [switchSuccessMsg, setSwitchSuccessMsg] = useState<string | null>(null);

  useEffect(() => {
    if (config) {
      setFormData(config);
    }
  }, [config]);

  const aiSubsystem = subsystems?.find((s) => s.id === "ai");
  const activeProviderId =
    aiSubsystem?.activeProviderId ||
    formData.activeAiProvider ||
    "RuleHeuristic";

  const isDirty = config
    ? JSON.stringify(config) !== JSON.stringify(formData)
    : false;

  const handleSave = () => {
    saveConfig.mutate(formData);
  };

  const handleSwitchProvider = async (providerId: string) => {
    try {
      const res = await switchSubsystem.mutateAsync({
        subsystemId: "ai",
        providerId,
      });
      if (res.success) {
        setFormData((prev) => ({ ...prev, activeAiProvider: providerId }));
        showToast(`Switched active AI engine to ${providerId}.`, "success");
      } else {
        showToast(
          `Failed to switch AI engine: ${res.error || t("settingsTabs.notifications.unknownError")}`,
          "error",
        );
      }
    } catch (err: any) {
      showToast(
        `Failed to switch AI engine: ${err.message || t("settingsTabs.notifications.unknownError")}`,
        "error",
      );
    }
  };

  const handleProbe = async (providerId: string) => {
    setProbeLoadingId(providerId);
    try {
      const res = await probeProvider.mutateAsync({
        subsystemId: "ai",
        providerId,
      });
      setProbeResult(res);
    } catch (err: any) {
      showToast(
        `Probe failed: ${err.message || t("settingsTabs.notifications.unknownError")}`,
        "error",
      );
    } finally {
      setProbeLoadingId(null);
    }
  };

  const handleResetButtonPosition = () => {
    localStorage.removeItem("leecharr_copilot_btn_pos");
    showToast(t("settingsTabs.ai.resetSuccess"), "success");
  };

  if (configLoading) {
    return <div className="loading">{t("settingsTabs.ai.loading")}</div>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}>
      <SaveBar
        dirty={isDirty}
        isPending={saveConfig.isPending}
        isError={saveConfig.isError}
        isSuccess={saveConfig.isSuccess}
        error={saveConfig.error}
        onSave={handleSave}
      />

      {switchSuccessMsg && (
        <div
          style={{
            padding: "0.75rem 1rem",
            backgroundColor: "rgba(16, 185, 129, 0.15)",
            border: "1px solid rgba(16, 185, 129, 0.4)",
            borderRadius: "8px",
            color: "#6ee7b7",
            fontSize: "0.85rem",
            fontWeight: 600,
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
          }}
        >
          <CheckCircleIcon size={16} />
          <span>{switchSuccessMsg}</span>
        </div>
      )}

      {/* Pluggable AI Engine Providers */}
      <SectionCard
        title={t("settingsTabs.ai.engineTitle")}
        description={t("settingsTabs.ai.engineDescription")}
      >
        <div
          style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}
        >
          {aiSubsystem?.providers?.map((provider) => {
            const isActive = provider.providerId === activeProviderId;
            const isProbing = probeLoadingId === provider.providerId;

            return (
              <div
                key={provider.providerId}
                style={{
                  backgroundColor: isActive
                    ? "rgba(255, 209, 102, 0.06)"
                    : "var(--bg-secondary, #171B35)",
                  border: isActive
                    ? "1px solid var(--accent-gold, #FFD166)"
                    : "1px solid var(--border-color, #23284B)",
                  borderRadius: "8px",
                  padding: "1rem",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.75rem",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    flexWrap: "wrap",
                    gap: "0.5rem",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.6rem",
                    }}
                  >
                    <BotIcon
                      size={20}
                      style={{ color: isActive ? "#FFD166" : "#C7C5D3" }}
                    />
                    <div>
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.5rem",
                        }}
                      >
                        <strong
                          style={{
                            fontSize: "0.95rem",
                            color: "var(--text-primary, #F8F4ED)",
                          }}
                        >
                          {provider.displayName}
                        </strong>
                        <span
                          style={{
                            fontSize: "0.65rem",
                            fontFamily: "monospace",
                            padding: "0.1rem 0.35rem",
                            borderRadius: "4px",
                            backgroundColor: "#23284B",
                            color: "#C7C5D3",
                          }}
                        >
                          v{provider.version}
                        </span>
                        {isActive && (
                          <span
                            className="badge badge-success"
                            style={{
                              fontSize: "0.65rem",
                              padding: "0.1rem 0.4rem",
                              fontWeight: 700,
                            }}
                          >
                            {t("settingsTabs.subsystems.statusActive")}
                          </span>
                        )}
                      </div>
                      <p
                        style={{
                          margin: "0.2rem 0 0",
                          fontSize: "0.8rem",
                          color: "var(--text-muted, #C7C5D3)",
                        }}
                      >
                        {provider.description}
                      </p>
                    </div>
                  </div>

                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                    }}
                  >
                    <button
                      type="button"
                      onClick={() => handleProbe(provider.providerId)}
                      disabled={isProbing}
                      className="btn btn-outline btn-small"
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.3rem",
                        fontSize: "0.75rem",
                      }}
                      title={t("settingsTabs.ai.runDiagnosticDesc", "Run live diagnostic probe and model test")}
                    >
                      <RefreshIcon
                        size={12}
                        style={{
                          animation: isProbing
                            ? "spin 1s linear infinite"
                            : "none",
                        }}
                      />
                      <span>
                        {isProbing
                          ? t("settingsTabs.subsystems.probing")
                          : t("settingsTabs.ai.testHealth")}
                      </span>
                    </button>

                    {!isActive && (
                      <button
                        type="button"
                        onClick={() =>
                          handleSwitchProvider(provider.providerId)
                        }
                        disabled={switchSubsystem.isPending}
                        className="btn btn-primary btn-small"
                        style={{ fontSize: "0.75rem", fontWeight: 700 }}
                      >
                        {t("settingsTabs.ai.activateProvider")}
                      </button>
                    )}
                  </div>
                </div>

                {/* Probe result banner if applicable */}
                {probeResult &&
                  probeResult.providerId === provider.providerId && (
                    <div
                      style={{
                        padding: "0.6rem 0.8rem",
                        borderRadius: "6px",
                        backgroundColor: probeResult.isHealthy
                          ? "rgba(16, 185, 129, 0.12)"
                          : "rgba(225, 29, 72, 0.12)",
                        border: probeResult.isHealthy
                          ? "1px solid rgba(16, 185, 129, 0.3)"
                          : "1px solid rgba(225, 29, 72, 0.3)",
                        fontSize: "0.75rem",
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.3rem",
                      }}
                    >
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        {probeResult.isHealthy ? (
                          <CheckCircleIcon
                            size={14}
                            style={{ color: "#34d399" }}
                          />
                        ) : (
                          <AlertIcon size={14} style={{ color: "#f87171" }} />
                        )}
                        <strong
                          style={{
                            color: probeResult.isHealthy
                              ? "#6ee7b7"
                              : "#fca5a5",
                          }}
                        >
                          {probeResult.statusMessage}
                        </strong>
                      </div>
                      {probeResult.warnings?.map((w, i) => (
                        <div
                          key={i}
                          style={{ color: "#fcd34d", paddingLeft: "1.2rem" }}
                        >
                          &bull; {w}
                        </div>
                      ))}
                    </div>
                  )}
              </div>
            );
          })}
        </div>
      </SectionCard>

      {/* Provider Connection Parameters */}
      <SectionCard
        title={t("settingsTabs.ai.connectionTitle")}
        description={t("settingsTabs.ai.connectionDescription")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          {/* Ollama */}
          <div
            style={{
              padding: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "var(--bg-secondary, #171B35)",
              border: "1px solid var(--border-color, #23284B)",
            }}
          >
            <h4
              style={{
                margin: "0 0 0.5rem",
                fontSize: "0.85rem",
                color: "var(--text-primary, #F8F4ED)",
              }}
            >
              {t("settingsTabs.ai.ollamaTitle")}
            </h4>
            <div
              className="form-row"
              style={{
                display: "grid",
                gridTemplateColumns: "2fr 1fr",
                gap: "0.75rem",
              }}
            >
              <div className="form-group">
                <label style={{ fontSize: "0.75rem" }}>
                  {t("settingsTabs.ai.ollamaUrl")}
                </label>
                <input
                  type="text"
                  value={formData.ollamaHost}
                  onChange={(e) =>
                    setFormData({ ...formData, ollamaHost: e.target.value })
                  }
                  placeholder="http://127.0.0.1:11434"
                  className="form-control"
                />
              </div>
              <div className="form-group">
                <label style={{ fontSize: "0.75rem" }}>
                  {t("settingsTabs.ai.defaultModelName")}
                </label>
                <input
                  type="text"
                  value={formData.ollamaModel}
                  onChange={(e) =>
                    setFormData({ ...formData, ollamaModel: e.target.value })
                  }
                  placeholder="llama3, mistral, deepseek-r1"
                  className="form-control"
                />
              </div>
            </div>
          </div>

          {/* Google Gemini */}
          <div
            style={{
              padding: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "var(--bg-secondary, #171B35)",
              border: "1px solid var(--border-color, #23284B)",
            }}
          >
            <h4
              style={{
                margin: "0 0 0.5rem",
                fontSize: "0.85rem",
                color: "var(--text-primary, #F8F4ED)",
              }}
            >
              {t("settingsTabs.ai.geminiTitle")}
            </h4>
            <div
              className="form-row"
              style={{
                display: "grid",
                gridTemplateColumns: "2fr 1fr",
                gap: "0.75rem",
              }}
            >
              <div className="form-group">
                <label style={{ fontSize: "0.75rem" }}>
                  {t("settingsTabs.ai.geminiApiKey")}
                </label>
                <input
                  type="password"
                  value={formData.geminiApiKey}
                  onChange={(e) =>
                    setFormData({ ...formData, geminiApiKey: e.target.value })
                  }
                  placeholder={t("settingsTabs.ai.geminiApiKeyPlaceholder")}
                  className="form-control"
                />
              </div>
              <div className="form-group">
                <label style={{ fontSize: "0.75rem" }}>
                  {t("settingsTabs.ai.geminiModel")}
                </label>
                <select
                  value={formData.geminiModel}
                  onChange={(e) =>
                    setFormData({ ...formData, geminiModel: e.target.value })
                  }
                  className="form-control"
                >
                  <option value="gemini-2.0-flash">
                    {t("settingsTabs.ai.geminiOptions.flash2")}
                  </option>
                  <option value="gemini-1.5-flash">
                    {t("settingsTabs.ai.geminiOptions.flash15")}
                  </option>
                  <option value="gemini-1.5-pro">
                    {t("settingsTabs.ai.geminiOptions.pro15")}
                  </option>
                </select>
              </div>
            </div>
          </div>

          {/* Local ONNX */}
          <div
            style={{
              padding: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "var(--bg-secondary, #171B35)",
              border: "1px solid var(--border-color, #23284B)",
            }}
          >
            <h4
              style={{
                margin: "0 0 0.5rem",
                fontSize: "0.85rem",
                color: "var(--text-primary, #F8F4ED)",
              }}
            >
              {t("settingsTabs.ai.onnxTitle")}
            </h4>
            <div className="form-group">
              <label style={{ fontSize: "0.75rem" }}>
                {t("settingsTabs.ai.onnxPath")}
              </label>
              <input
                type="text"
                value={formData.onnxModelPath}
                onChange={(e) =>
                  setFormData({ ...formData, onnxModelPath: e.target.value })
                }
                placeholder="/config/models/leecharr-ai.onnx"
                className="form-control"
              />
            </div>
          </div>
        </div>
      </SectionCard>

      {/* Discrete UI Integration & Feature Controls */}
      <SectionCard
        title={t("settingsTabs.ai.uiFeaturesTitle")}
        description={t("settingsTabs.ai.uiFeaturesDescription")}
      >
        <div
          style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}
        >
          <label
            className="checkbox-label"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={formData.enableCopilotButton}
              onChange={(e) =>
                setFormData({
                  ...formData,
                  enableCopilotButton: e.target.checked,
                })
              }
            />
            <span
              style={{
                fontSize: "0.85rem",
                fontWeight: 600,
                color: "var(--text-primary, #F8F4ED)",
              }}
            >
              {t("settingsTabs.ai.enableCopilot")}
            </span>
          </label>
          <span
            style={{
              fontSize: "0.75rem",
              color: "var(--text-muted, #C7C5D3)",
              paddingLeft: "1.5rem",
            }}
          >
            Displays the subtle floating badge in the bottom-right corner. You
            can drag and drop it anywhere on your screen.
          </span>

          <label
            className="checkbox-label"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              cursor: "pointer",
              marginTop: "0.5rem",
            }}
          >
            <input
              type="checkbox"
              checked={formData.enableNaturalSearch}
              onChange={(e) =>
                setFormData({
                  ...formData,
                  enableNaturalSearch: e.target.checked,
                })
              }
            />
            <span
              style={{
                fontSize: "0.85rem",
                fontWeight: 600,
                color: "var(--text-primary, #F8F4ED)",
              }}
            >
              {t("settingsTabs.ai.enableSearch")}
            </span>
          </label>
          <span
            style={{
              fontSize: "0.75rem",
              color: "var(--text-muted, #C7C5D3)",
              paddingLeft: "1.5rem",
            }}
          >
            Adds the collapsible natural language filter accordion in the
            Torznab search modal.
          </span>

          <label
            className="checkbox-label"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              cursor: "pointer",
              marginTop: "0.5rem",
            }}
          >
            <input
              type="checkbox"
              checked={formData.enableSwarmDiagnostics}
              onChange={(e) =>
                setFormData({
                  ...formData,
                  enableSwarmDiagnostics: e.target.checked,
                })
              }
            />
            <span
              style={{
                fontSize: "0.85rem",
                fontWeight: 600,
                color: "var(--text-primary, #F8F4ED)",
              }}
            >
              {t("settingsTabs.ai.enableDiagnostics")}
            </span>
          </label>
          <span
            style={{
              fontSize: "0.75rem",
              color: "var(--text-muted, #C7C5D3)",
              paddingLeft: "1.5rem",
            }}
          >
            Provides 1-click diagnostic bottleneck analysis and remediation in
            the torrent detail panel.
          </span>

          <div
            style={{
              paddingTop: "0.75rem",
              borderTop: "1px solid var(--border-color, #23284B)",
              marginTop: "0.5rem",
            }}
          >
            <button
              type="button"
              onClick={handleResetButtonPosition}
              className="btn btn-outline btn-small"
              style={{ fontSize: "0.75rem" }}
            >
              {t("settingsTabs.ai.resetButtonPosition")}
            </button>
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default AiTab;
