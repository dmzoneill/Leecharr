import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function WatchFolderSettingsTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [form, setForm] = useState({
    watchFolderEnabled: false,
    watchFolderPath: "/downloads/watch",
    watchFolderScanIntervalSeconds: 10,
    watchFolderAutoStartTorrents: true,
    watchFolderDeleteAddedTorrents: false,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        watchFolderEnabled: config.watchFolderEnabled ?? false,
        watchFolderPath: config.watchFolderPath || "/downloads/watch",
        watchFolderScanIntervalSeconds:
          config.watchFolderScanIntervalSeconds ?? 10,
        watchFolderAutoStartTorrents:
          config.watchFolderAutoStartTorrents ?? true,
        watchFolderDeleteAddedTorrents:
          config.watchFolderDeleteAddedTorrents ?? false,
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
        watchFolderEnabled: form.watchFolderEnabled,
        watchFolderPath: form.watchFolderPath,
        watchFolderScanIntervalSeconds: form.watchFolderScanIntervalSeconds,
        watchFolderAutoStartTorrents: form.watchFolderAutoStartTorrents,
        watchFolderDeleteAddedTorrents: form.watchFolderDeleteAddedTorrents,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading watch folder settings...
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
        title="Automated Directory Monitoring"
        description="Monitor local directories for dropped .torrent and .magnet files and automatically ingest them."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Enable Watch Folder Monitoring"
            checked={form.watchFolderEnabled}
            onChange={(v) => update("watchFolderEnabled", v)}
            hint="Background service will periodically scan the directory for new payload files"
          />

          <TextInput
            label="Watch Directory Path"
            value={form.watchFolderPath}
            onChange={(v) => update("watchFolderPath", v)}
            disabled={!form.watchFolderEnabled}
            hint="Filesystem directory where .torrent files are dropped (e.g. /downloads/watch)"
          />

          <NumberInput
            label="Scan Cadence (Seconds)"
            value={form.watchFolderScanIntervalSeconds}
            onChange={(v) => update("watchFolderScanIntervalSeconds", v)}
            disabled={!form.watchFolderEnabled}
            min={5}
            max={3600}
            suffix="sec"
            hint="Interval between directory scans"
          />

          <div
            style={{
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                gap: "1rem",
              }}
            >
              <Toggle
                label="Auto-Start Ingested Torrents"
                checked={form.watchFolderAutoStartTorrents}
                onChange={(v) => update("watchFolderAutoStartTorrents", v)}
                disabled={!form.watchFolderEnabled}
                hint="Start downloading immediately upon importing from the watch directory"
              />

              <Toggle
                label="Delete .torrent Files After Import"
                checked={form.watchFolderDeleteAddedTorrents}
                onChange={(v) => update("watchFolderDeleteAddedTorrents", v)}
                disabled={!form.watchFolderEnabled}
                hint="Remove source .torrent file from disk once imported into database"
              />
            </div>
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default WatchFolderSettingsTab;
