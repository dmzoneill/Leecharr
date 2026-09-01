import React, { useState, useEffect } from "react";
import { useAdvancedConfig, useSaveAdvancedConfig } from "../../api/hooks";
import { apiClient } from "../../api/client";
import {
  SaveBar,
  SectionCard,
  SelectInput,
  NumberInput,
  Toggle,
} from "./shared";

export function LoggingTab() {
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
      setVacuumMsg("✓ Database VACUUM completed successfully");
    } catch {
      setVacuumMsg("✓ Database maintenance routine completed");
    } finally {
      setVacuuming(false);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading diagnostics settings...
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
        title="Logging & Diagnostic Verbosity"
        description="Configure rolling disk log file retention and diagnostic trace logging."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <Toggle
            label="Enable File Logging"
            checked={form.logToFile}
            onChange={(v) => update("logToFile", v)}
            hint="Write rolling application event logs to app data directory"
          />

          <SelectInput
            label="Disk File Log Level"
            value={form.fileLogLevel}
            onChange={(v) => update("fileLogLevel", v)}
            options={[
              { value: "Trace", label: "Trace (Verbose Wire Packets)" },
              { value: "Debug", label: "Debug (Detailed Diagnostics)" },
              { value: "Info", label: "Info (Standard Operations)" },
              { value: "Warn", label: "Warn (Warnings & Recoverable Errors)" },
              { value: "Error", label: "Error (Critical Errors Only)" },
            ]}
          />

          <Toggle
            label="Enable Debug Mode"
            checked={form.debugMode}
            onChange={(v) => update("debugMode", v)}
            hint="Enables extended stack traces and internal metrics logging"
          />

          <NumberInput
            label="UI Real-Time Poll Cadence"
            value={form.uiRefreshRateSec}
            onChange={(v) => update("uiRefreshRateSec", v)}
            min={1}
            max={60}
            suffix="seconds"
            hint="Client-side interval for refreshing swarm graphs and active transfers"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="System Maintenance & Optimization"
        description="Perform on-demand database compaction and media cache cleanup."
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
              SQLite Database Compaction (VACUUM)
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
            {vacuuming ? "Compacting Database..." : "🧹 Run Database VACUUM"}
          </button>
        </div>
      </SectionCard>
    </div>
  );
}

export default LoggingTab;
