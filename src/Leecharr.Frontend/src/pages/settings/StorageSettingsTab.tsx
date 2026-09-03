import React, { useState, useEffect } from "react";
import { useBitTorrentConfig, useSaveBitTorrentConfig } from "../../api/hooks";
import { SaveBar, SectionCard, TextInput, SelectInput, Toggle } from "./shared";

export function StorageSettingsTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const [form, setForm] = useState({
    downloadDir: "/downloads",
    enableIncompleteDir: true,
    incompleteDownloadDir: "/downloads/incomplete",
    preallocationMode: "Sparse",
    renamePartialFiles: true,
    incompleteExtension: ".!leech",
    umask: "022",
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        downloadDir: config.downloadDir || "/downloads",
        enableIncompleteDir: config.enableIncompleteDir ?? true,
        incompleteDownloadDir: config.incompleteDownloadDir || "/downloads/incomplete",
        preallocationMode: config.preallocationMode || "Sparse",
        renamePartialFiles: config.renamePartialFiles ?? true,
        incompleteExtension: config.incompleteExtension || ".!leech",
        umask: config.umask || "022",
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
        downloadDir: form.downloadDir,
        enableIncompleteDir: form.enableIncompleteDir,
        incompleteDownloadDir: form.incompleteDownloadDir,
        preallocationMode: form.preallocationMode,
        renamePartialFiles: form.renamePartialFiles,
        incompleteExtension: form.incompleteExtension,
        umask: form.umask,
      },
      {
        onSuccess: () => setDirty(false),
      }
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading storage parameters...
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
        title="Download Staging & File Storage"
        description="Configure completed download storage destinations, incomplete download staging paths, and file preallocation strategies."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label="Default Completed Download Directory"
            value={form.downloadDir}
            onChange={(v) => update("downloadDir", v)}
            hint="Root directory where completed downloads are placed upon 100% verification (e.g. /downloads)"
          />

          <Toggle
            label="Stage Incomplete Downloads in Temporary Folder"
            checked={form.enableIncompleteDir}
            onChange={(v) => update("enableIncompleteDir", v)}
            hint="Keeps files isolated until 100% verified, then moves them to the target destination"
          />

          <TextInput
            label="Incomplete Download Directory"
            value={form.incompleteDownloadDir}
            onChange={(v) => update("incompleteDownloadDir", v)}
            disabled={!form.enableIncompleteDir}
            hint="Path where in-progress downloads are written (e.g. /downloads/incomplete)"
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            <SelectInput
              label="Disk Preallocation Mode"
              value={form.preallocationMode}
              onChange={(v) => update("preallocationMode", v)}
              options={[
                {
                  value: "Sparse",
                  label: "Sparse Allocation (Instant Non-Blocking, Recommended)",
                },
                {
                  value: "Full",
                  label: "Full Preallocation (Zero-fill, Prevents Fragmentation)",
                },
                { value: "Off", label: "Disabled / Compact (Grow On Write)" },
              ]}
              hint="Sparse creates files instantly without freezing I/O during torrent startup"
            />

            <TextInput
              label="POSIX Permission Mask (umask)"
              value={form.umask}
              onChange={(v) => update("umask", v)}
              hint="Octal permission mask for created files & directories (022 = 755/644, 002 = 775/664)"
            />
          </div>

          <div
            style={{
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <Toggle
              label="Append Custom Extension to Incomplete Files"
              checked={form.renamePartialFiles}
              onChange={(v) => update("renamePartialFiles", v)}
              hint="Renames in-progress downloading files to prevent external media managers from processing partial files"
            />

            {form.renamePartialFiles && (
              <div style={{ marginTop: "0.75rem" }}>
                <TextInput
                  label="Incomplete File Extension"
                  value={form.incompleteExtension}
                  onChange={(v) => update("incompleteExtension", v)}
                  hint="Extension suffix appended during transfer and stripped upon 100% verification (e.g. .!leech, .!qB, or .part)"
                />
              </div>
            )}
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default StorageSettingsTab;
