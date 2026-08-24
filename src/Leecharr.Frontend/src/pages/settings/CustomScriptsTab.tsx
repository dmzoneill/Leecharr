import React, { useState, useEffect } from "react";
import { useBitTorrentConfig, useSaveBitTorrentConfig } from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  TextInput,
} from "./shared";

export function CustomScriptsTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const [form, setForm] = useState({
    onDownloadCompleteScript: "",
    onSeedGoalReachedScript: "",
    scriptTorrentDoneFilename: "",
    scriptTorrentAddedFilename: "",
    scriptTorrentDoneSeedingFilename: "",
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        onDownloadCompleteScript: config.onDownloadCompleteScript || "",
        onSeedGoalReachedScript: config.onSeedGoalReachedScript || "",
        scriptTorrentDoneFilename: config.scriptTorrentDoneFilename || "",
        scriptTorrentAddedFilename: config.scriptTorrentAddedFilename || "",
        scriptTorrentDoneSeedingFilename: config.scriptTorrentDoneSeedingFilename || "",
      });
      setDirty(false);
    }
  }, [config]);

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        onDownloadCompleteScript: form.onDownloadCompleteScript,
        onSeedGoalReachedScript: form.onSeedGoalReachedScript,
        scriptTorrentDoneFilename: form.scriptTorrentDoneFilename,
        scriptTorrentAddedFilename: form.scriptTorrentAddedFilename,
        scriptTorrentDoneSeedingFilename: form.scriptTorrentDoneSeedingFilename,
      },
      {
        onSuccess: () => setDirty(false),
      }
    );
  };

  if (isLoading) {
    return <div className="loading" style={{ padding: "2rem" }}>Loading custom script settings...</div>;
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
        title="Event Lifecycle Shell Scripts"
        description="Execute local OS bash / python shell scripts upon download completion and seeding ratio satisfaction."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label="On Download Complete Script"
            value={form.onDownloadCompleteScript}
            onChange={(v) => update("onDownloadCompleteScript", v)}
            hint="Absolute path to executable script (e.g. /usr/local/bin/notify-plex.sh)"
          />

          <TextInput
            label="On Seed Goal Reached Script"
            value={form.onSeedGoalReachedScript}
            onChange={(v) => update("onSeedGoalReachedScript", v)}
            hint="Absolute path to executable script triggered when target ratio or seed time is satisfied"
          />

          <div style={{ backgroundColor: "var(--bg-primary)", padding: "1rem", borderRadius: "6px", border: "1px solid var(--border)" }}>
            <div style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--text-secondary)", marginBottom: "0.5rem" }}>
              Standard Environment Variables Passed to Scripts:
            </div>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", fontFamily: "monospace", display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "0.35rem" }}>
              <div>• TORRENT_ID</div>
              <div>• TORRENT_NAME</div>
              <div>• TORRENT_PATH</div>
              <div>• TORRENT_CATEGORY</div>
              <div>• TORRENT_INFOHASH</div>
              <div>• TORRENT_SIZE_BYTES</div>
            </div>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Transmission Daemon Engine Hooks"
        description="Native script hooks invoked when running under the Transmission engine provider."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label="script-torrent-done-filename"
            value={form.scriptTorrentDoneFilename}
            onChange={(v) => update("scriptTorrentDoneFilename", v)}
            hint="Native Transmission on-done executable path"
          />

          <TextInput
            label="script-torrent-added-filename"
            value={form.scriptTorrentAddedFilename}
            onChange={(v) => update("scriptTorrentAddedFilename", v)}
            hint="Native Transmission on-added executable path"
          />

          <TextInput
            label="script-torrent-done-seeding-filename"
            value={form.scriptTorrentDoneSeedingFilename}
            onChange={(v) => update("scriptTorrentDoneSeedingFilename", v)}
            hint="Native Transmission on-seeding-complete executable path"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default CustomScriptsTab;
