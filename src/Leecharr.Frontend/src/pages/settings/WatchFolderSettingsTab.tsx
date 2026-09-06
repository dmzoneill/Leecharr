import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function WatchFolderSettingsTab() {
  const { t } = useTranslation();

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
        {t("settingsTabs.batch2.loadingWatchFolderSettings")}
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
        title={t("settingsTabs.batch2.automatedDirectoryMonitoring")}
        description={t("settingsTabs.batch2.monitorLocalDirectories")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settingsTabs.batch2.enableWatchFolderMonitoring")}
            checked={form.watchFolderEnabled}
            onChange={(v) => update("watchFolderEnabled", v)}
            hint={t(
              "settingsTabs.batch2.backgroundServiceWillPeriodicallyScan",
            )}
          />

          <TextInput
            label={t("settingsTabs.batch2.watchDirectoryPath")}
            value={form.watchFolderPath}
            onChange={(v) => update("watchFolderPath", v)}
            disabled={!form.watchFolderEnabled}
            hint={t(
              "settingsTabs.batch2.filesystemDirectoryWhereTorrentFilesAreDropped",
            )}
          />

          <NumberInput
            label={t("settingsTabs.batch2.scanCadenceSeconds")}
            value={form.watchFolderScanIntervalSeconds}
            onChange={(v) => update("watchFolderScanIntervalSeconds", v)}
            disabled={!form.watchFolderEnabled}
            min={5}
            max={3600}
            suffix={t("settingsTabs.batch2.sec")}
            hint={t("settingsTabs.batch2.intervalBetweenDirectoryScans")}
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
                label={t("settingsTabs.batch2.autoStartIngestedTorrents")}
                checked={form.watchFolderAutoStartTorrents}
                onChange={(v) => update("watchFolderAutoStartTorrents", v)}
                disabled={!form.watchFolderEnabled}
                hint={t(
                  "settingsTabs.batch2.startDownloadingImmediatelyUponImporting",
                )}
              />

              <Toggle
                label={t("settingsTabs.batch2.deleteTorrentFilesAfterImport")}
                checked={form.watchFolderDeleteAddedTorrents}
                onChange={(v) => update("watchFolderDeleteAddedTorrents", v)}
                disabled={!form.watchFolderEnabled}
                hint={t("settingsTabs.batch2.removeSourceTorrentFileFromDisk")}
              />
            </div>
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default WatchFolderSettingsTab;
