import { useTranslation } from "../../i18n";
import { useState, useEffect } from "react";
import { useAdvancedConfig, useSaveAdvancedConfig } from "../../api/hooks";
import { apiClient } from "../../api/client";
import { useToast } from "../../context/ToastContext";
import {
  SaveBar,
  SectionCard,
  SelectInput,
  NumberInput,
  Toggle,
} from "./shared";

export function LoggingTab() {
  const { t } = useTranslation();

  const { data: config, isLoading } = useAdvancedConfig();
  const saveMutation = useSaveAdvancedConfig();

  const [form, setForm] = useState({
    logToFile: true,
    fileLogLevel: "Info",
    debugMode: false,
    uiRefreshRateSec: 2,
  });

  const [dirty, setDirty] = useState(false);
  const [vacuuming, setVacuuming] = useState(false);
  const [vacuumMsg, setVacuumMsg] = useState<string | null>(null);
  const [testingLog, setTestingLog] = useState(false);
  const [testLogLevel, setTestLogLevel] = useState<string>("Info");
  const { showToast } = useToast();

  useEffect(() => {
    if (config) {
      setForm({
        logToFile: config.logToFile ?? true,
        fileLogLevel: config.fileLogLevel || "Info",
        debugMode: config.debugMode ?? false,
        uiRefreshRateSec: config.uiRefreshRateSec ?? 2,
      });
      setDirty(false);
    }
  }, [config]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        logToFile: form.logToFile,
        fileLogLevel: form.fileLogLevel,
        debugMode: form.debugMode,
        uiRefreshRateSec: form.uiRefreshRateSec,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  const handleVacuum = async () => {
    setVacuuming(true);
    setVacuumMsg(null);
    try {
      await apiClient.post("/system/maintenance/vacuum", {});
      setVacuumMsg(t("settingsTabs.logging.vacuumSuccess"));
    } catch (err: unknown) {
      const errObj = err as Error | null;
      setVacuumMsg(
        t("settingsTabs.logging.vacuumError", {
          error: errObj?.message || t("settingsTabs.logging.internalServerError"),
        }),
      );
    } finally {
      setVacuuming(false);
    }
  };

  const handleTestLog = async () => {
    setTestingLog(true);
    try {
      const res = await apiClient.post<{ success: boolean; level: string; message: string }>("/log/test", {
        level: testLogLevel,
        message: `Diagnostic test log event emitted from settings [${testLogLevel}]`,
      });
      showToast(
        t("settingsTabs.logging.testLogSuccess", {
          defaultValue: `Diagnostic ${res?.level || testLogLevel} test log emitted successfully`,
          level: res?.level || testLogLevel,
        }),
        "success",
      );
    } catch (err: unknown) {
      const errObj = err as Error | null;
      showToast(
        t("settingsTabs.logging.testLogError", {
          defaultValue: `Failed to emit diagnostic test log: ${errObj?.message || "Unknown error"}`,
          error: errObj?.message || t("settingsTabs.logging.internalServerError"),
        }),
        "error",
      );
    } finally {
      setTestingLog(false);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.logging.loading")}
      </div>
    );
  }

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={saveMutation.isPending}
        isError={saveMutation.isError}
        isSuccess={saveMutation.isSuccess}
        error={saveMutation.error as Error | null}
        onSave={handleSave}
      />

      <SectionCard
        title={t("settingsTabs.logging.diagnosticsTitle")}
        description={t("settingsTabs.logging.diagnosticsDescription")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <Toggle
            label={t("settingsTabs.logging.enableFileLogging")}
            checked={form.logToFile}
            onChange={(v) => update("logToFile", v)}
            hint={t("settingsTabs.logging.enableFileLoggingHint")}
          />

          <SelectInput
            label={t("settingsTabs.logging.diskLogLevel")}
            value={form.fileLogLevel}
            onChange={(v) => update("fileLogLevel", v)}
            options={[
              {
                value: "Trace",
                label: t("settingsTabs.logging.logLevels.trace"),
              },
              {
                value: "Debug",
                label: t("settingsTabs.logging.logLevels.debug"),
              },
              {
                value: "Info",
                label: t("settingsTabs.logging.logLevels.info"),
              },
              {
                value: "Warn",
                label: t("settingsTabs.logging.logLevels.warn"),
              },
              {
                value: "Error",
                label: t("settingsTabs.logging.logLevels.error"),
              },
            ]}
          />

          <Toggle
            label={t("settingsTabs.logging.enableDebugMode")}
            checked={form.debugMode}
            onChange={(v) => update("debugMode", v)}
            hint={t("settingsTabs.logging.enableDebugModeHint")}
          />

          <NumberInput
            label={t("settingsTabs.logging.uiRefreshRate")}
            value={form.uiRefreshRateSec}
            onChange={(v) => update("uiRefreshRateSec", v)}
            min={1}
            max={60}
            suffix={t("settingsTabs.notifications.timeoutSuffix")}
            hint={t("settingsTabs.logging.uiRefreshRateHint")}
          />
        </div>

        <div
          style={{
            marginTop: "1.25rem",
            paddingTop: "1.25rem",
            borderTop: "1px solid var(--border-color, rgba(255, 255, 255, 0.1))",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "1rem",
          }}
        >
          <div>
            <div
              style={{
                fontWeight: 600,
                color: "var(--text-primary)",
                fontSize: "0.95rem",
              }}
            >
              {t("settingsTabs.logging.testLogTitle")}
            </div>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {t("settingsTabs.logging.testLogDescription")}
            </div>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <select
              className="form-select"
              value={testLogLevel}
              aria-label={t("settingsTabs.logging.testLogLevelLabel")}
              onChange={(e) => setTestLogLevel(e.target.value)}
              style={{
                borderRadius: "6px",
                padding: "0.4rem 0.6rem",
                fontSize: "0.85rem",
              }}
            >
              <option value="Trace">{t("settingsTabs.logging.logLevels.trace")}</option>
              <option value="Debug">{t("settingsTabs.logging.logLevels.debug")}</option>
              <option value="Info">{t("settingsTabs.logging.logLevels.info")}</option>
              <option value="Warn">{t("settingsTabs.logging.logLevels.warn")}</option>
              <option value="Error">{t("settingsTabs.logging.logLevels.error")}</option>
            </select>
            <button
              type="button"
              className="btn btn-outline"
              onClick={handleTestLog}
              disabled={testingLog}
            >
              {testingLog
                ? t("settingsTabs.logging.testLogRunning")
                : t("settingsTabs.logging.testLogButton")}
            </button>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.logging.maintenanceTitle")}
        description={t("settingsTabs.logging.maintenanceDescription")}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "1rem",
          }}
        >
          <div>
            <div
              style={{
                fontWeight: 600,
                color: "var(--text-primary)",
                fontSize: "0.95rem",
              }}
            >
              {t("settingsTabs.logging.vacuumTitle")}
            </div>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              Rebuilds the SQLite database file to reclaim unused disk space and
              defragment database indices.
            </div>
            {vacuumMsg && (
              <div
                style={{
                  fontSize: "0.85rem",
                  color: "var(--success, #27ae60)",
                  marginTop: "0.4rem",
                  fontWeight: 600,
                }}
              >
                {vacuumMsg}
              </div>
            )}
          </div>

          <button
            type="button"
            className="btn btn-outline"
            onClick={handleVacuum}
            disabled={vacuuming}
          >
            {vacuuming
              ? t("settingsTabs.logging.vacuumRunning")
              : t("settingsTabs.logging.vacuumButton")}
          </button>
        </div>
      </SectionCard>
    </div>
  );
}

export default LoggingTab;
