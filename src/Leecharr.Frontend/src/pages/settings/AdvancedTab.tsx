import { useState, useEffect } from "react";
import { useAdvancedConfig, useSaveAdvancedConfig } from "../../api/hooks";
import type { AdvancedConfig } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  NumberInput,
  SectionCard,
} from "./shared";

export function AdvancedTab() {
  const { data: config, isLoading } = useAdvancedConfig();
  const save = useSaveAdvancedConfig();
  const [form, setForm] = useState<AdvancedConfig>({
    id: 1,
    logToFile: true,
    fileLogLevel: "Info",
    debugMode: false,
    uiRefreshRateSec: 9,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof AdvancedConfig>(
    key: K,
    value: AdvancedConfig[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading configuration...</div>;

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={save.isPending}
        isError={save.isError}
        isSuccess={save.isSuccess}
        error={save.error}
        onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })}
      />

      <SectionCard
        title="System Logging & Diagnostics"
        description="Disk logging verbosity and debug diagnostics for troubleshooting"
      >
        <Toggle
          label="Log to File"
          checked={form.logToFile}
          onChange={(v) => set("logToFile", v)}
          hint="Persist log entries to rolling disk log files"
        />
        <SelectInput
          label="File Log Level"
          value={form.fileLogLevel}
          onChange={(v) => set("fileLogLevel", v)}
          options={[
            { value: "Trace", label: "Trace (Most Verbose)" },
            { value: "Debug", label: "Debug" },
            { value: "Info", label: "Info (Recommended)" },
            { value: "Warn", label: "Warning" },
            { value: "Error", label: "Error Only" },
          ]}
          disabled={!form.logToFile}
          hint="Minimum severity level recorded to file"
        />
        <Toggle
          label="Debug Mode"
          checked={form.debugMode}
          onChange={(v) => set("debugMode", v)}
          hint="Enable comprehensive internal state tracing and diagnostic outputs"
        />
      </SectionCard>

      <SectionCard
        title="User Interface & Polling Frequency"
        description="Background data polling intervals for torrent lists and statistics"
      >
        <NumberInput
          label="UI Refresh Rate"
          value={form.uiRefreshRateSec}
          onChange={(v) => set("uiRefreshRateSec", v)}
          min={1}
          max={60}
          suffix="seconds"
          hint="Default polling interval for web interface live updates (default: 9s)"
        />
      </SectionCard>
    </div>
  );
}
