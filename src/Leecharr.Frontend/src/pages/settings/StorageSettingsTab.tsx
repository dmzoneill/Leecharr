import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import { useBitTorrentConfig, useSaveBitTorrentConfig } from "../../api/hooks";
import { SaveBar, SectionCard, TextInput, SelectInput, Toggle } from "./shared";

export function StorageSettingsTab() {
  const { t } = useTranslation();

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
        incompleteDownloadDir:
          config.incompleteDownloadDir || "/downloads/incomplete",
        preallocationMode: config.preallocationMode || "Sparse",
        renamePartialFiles: config.renamePartialFiles ?? true,
        incompleteExtension: config.incompleteExtension || ".!leech",
        umask: config.umask || "022",
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
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.batch2.loadingStorageParameters")}
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
        title={t("settingsTabs.nav.groups.storageQueues.pages.storage.title")}
        description={t(
          "settingsTabs.batch2.configureCompletedDownloadStorageDestinations",
        )}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label={t("settingsTabs.batch2.defaultCompletedDownloadDirectory")}
            value={form.downloadDir}
            onChange={(v) => update("downloadDir", v)}
            hint={t(
              "settingsTabs.batch2.rootDirectoryWhereCompletedDownloadsArePlaced",
            )}
          />

          <Toggle
            label={t(
              "settingsTabs.batch2.stageIncompleteDownloadsInTemporaryFolder",
            )}
            checked={form.enableIncompleteDir}
            onChange={(v) => update("enableIncompleteDir", v)}
            hint={t("settingsTabs.batch2.keepsFilesIsolatedUntil100Verified")}
          />

          <TextInput
            label={t("settingsTabs.batch2.incompleteDownloadDirectory")}
            value={form.incompleteDownloadDir}
            onChange={(v) => update("incompleteDownloadDir", v)}
            disabled={!form.enableIncompleteDir}
            hint={t(
              "settingsTabs.batch2.pathWhereInProgressDownloadsAreWritten",
            )}
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            <SelectInput
              label={t("settingsTabs.batch2.diskPreallocationMode")}
              value={form.preallocationMode}
              onChange={(v) => update("preallocationMode", v)}
              options={[
                {
                  value: "Sparse",
                  label: t(
                    "settingsTabs.batch2.sparseAllocationInstantNonBlocking",
                  ),
                },
                {
                  value: "Full",
                  label: t("settingsTabs.batch2.fullPreallocationZeroFill"),
                },
                {
                  value: "Off",
                  label: t("settingsTabs.batch2.disabledCompactGrowOnWrite"),
                },
              ]}
              hint={t("settingsTabs.batch2.sparseCreatesFilesInstantly")}
            />

            <TextInput
              label={t("settingsTabs.batch2.posixPermissionMask")}
              value={form.umask}
              onChange={(v) => update("umask", v)}
              hint={t("settingsTabs.batch2.octalPermissionMaskForCreatedFiles")}
            />
          </div>

          <div
            style={{
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <Toggle
              label={t(
                "settingsTabs.batch2.appendCustomExtensionToIncompleteFiles",
              )}
              checked={form.renamePartialFiles}
              onChange={(v) => update("renamePartialFiles", v)}
              hint={t("settingsTabs.batch2.renamesInProgressDownloadingFiles")}
            />

            {form.renamePartialFiles && (
              <div style={{ marginTop: "0.75rem" }}>
                <TextInput
                  label={t("settingsTabs.batch2.incompleteFileExtension")}
                  value={form.incompleteExtension}
                  onChange={(v) => update("incompleteExtension", v)}
                  hint={t(
                    "settingsTabs.batch2.extensionSuffixAppendedDuringTransfer",
                  )}
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
