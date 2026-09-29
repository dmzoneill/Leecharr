import { useTranslation } from "../../i18n";
import { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { apiClient } from "../../api/client";
import { useToast } from "../../context/ToastContext";
import { FolderBrowserModal } from "../../components/FolderBrowserModal";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function WatchFolderSettingsTab() {
  const { t } = useTranslation();
  const { showToast } = useToast();

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
  const [showFolderBrowser, setShowFolderBrowser] = useState(false);
  const [scanning, setScanning] = useState(false);

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

  const handleScanNow = async () => {
    setScanning(true);
    try {
      let triggered = false;
      try {
        const tasks = await apiClient.get<
          Array<{ id: number; name?: string; typeName?: string }>
        >("/system/task");
        const watchTask = tasks?.find(
          (t) =>
            t.name === "WatchFolderScan" ||
            t.typeName === "WatchFolderScanTask",
        );
        if (watchTask?.id) {
          await apiClient.post(`/system/task/${watchTask.id}/execute`, {});
          triggered = true;
        }
      } catch {
        // Fallback to system/command endpoint below
      }

      if (!triggered) {
        await apiClient.post("/system/command", { name: "WatchFolderScan" });
      }

      showToast("Watch folder scan triggered successfully", "success");
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data
          ?.message ||
        (err as Error)?.message ||
        "Failed to trigger watch folder scan";
      showToast(msg, "error");
    } finally {
      setScanning(false);
    }
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
            rightElement={
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setShowFolderBrowser(true)}
                disabled={!form.watchFolderEnabled}
                title="Browse filesystem directories"
                style={{
                  whiteSpace: "nowrap",
                  padding: "0.4rem 0.65rem",
                  fontSize: "0.8rem",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.35rem",
                }}
              >
                <span>📁</span>
                <span>Browse...</span>
              </button>
            }
          />

          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              padding: "0.75rem 1rem",
              backgroundColor: "var(--bg-primary, #10111a)",
              borderRadius: "8px",
              border: "1px solid var(--border-light)",
              gap: "1rem",
              flexWrap: "wrap",
            }}
          >
            <div>
              <div
                style={{
                  fontWeight: 600,
                  fontSize: "0.85rem",
                  color: "var(--text-primary)",
                }}
              >
                Manual Directory Scan
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                Immediately scan the watch folder for newly added .torrent files.
              </div>
            </div>
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={handleScanNow}
              disabled={scanning || !form.watchFolderEnabled}
              title="Scan watch folder now"
              style={{
                whiteSpace: "nowrap",
                padding: "0.4rem 0.85rem",
                fontSize: "0.8rem",
                display: "flex",
                alignItems: "center",
                gap: "0.4rem",
              }}
            >
              <span>{scanning ? "⏳" : "🔍"}</span>
              <span>{scanning ? "Scanning..." : "Scan Now"}</span>
            </button>
          </div>

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
      {showFolderBrowser && (
        <FolderBrowserModal
          isOpen={showFolderBrowser}
          initialPath={form.watchFolderPath || "/downloads/watch"}
          title="Select Watch Folder"
          onSelect={(selectedPath) => {
            update("watchFolderPath", selectedPath);
            setShowFolderBrowser(false);
          }}
          onClose={() => setShowFolderBrowser(false)}
        />
      )}
    </div>
  );
}

export default WatchFolderSettingsTab;
