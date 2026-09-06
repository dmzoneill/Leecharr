import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useSeedingConfig,
  useSaveSeedingConfig,
} from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  SelectInput,
  Toggle,
} from "./shared";
import { CategorySettingsTab } from "./CategorySettingsTab";

export function QueueSettingsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();

  const { data: seedConfig, isLoading: seedLoading } = useSeedingConfig();
  const saveSeedMutation = useSaveSeedingConfig();

  const [form, setForm] = useState({
    downloadQueueSize: 5,
    seedQueueSize: 10,
    queueStalledEnabled: true,
    queueStalledMinutes: 30,
    idleSeedingLimitMinutes: 0,
    globalSeedRatioLimit: 0,
    globalShareLimitAction: "Pause",
    autoShutdownAction: "None",
    autoShutdownCondition: "WhenDownloadsComplete",
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (btConfig || seedConfig) {
      setForm({
        downloadQueueSize: btConfig?.downloadQueueSize ?? 5,
        seedQueueSize: btConfig?.seedQueueSize ?? 10,
        queueStalledEnabled: btConfig?.queueStalledEnabled ?? true,
        queueStalledMinutes: btConfig?.queueStalledMinutes ?? 30,
        idleSeedingLimitMinutes: btConfig?.idleSeedingLimitMinutes ?? 0,
        globalSeedRatioLimit: seedConfig?.globalSeedRatioLimit ?? 0,
        globalShareLimitAction: btConfig?.globalShareLimitAction || "Pause",
        autoShutdownAction: btConfig?.autoShutdownAction || "None",
        autoShutdownCondition:
          btConfig?.autoShutdownCondition || "WhenDownloadsComplete",
      });
      setDirty(false);
    }
  }, [btConfig, seedConfig]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const isPending = saveBtMutation.isPending || saveSeedMutation.isPending;
  const isError = saveBtMutation.isError || saveSeedMutation.isError;
  const isSuccess =
    (!btConfig || saveBtMutation.isSuccess) &&
    (!seedConfig || saveSeedMutation.isSuccess) &&
    (saveBtMutation.isSuccess || saveSeedMutation.isSuccess);
  const error = (saveBtMutation.error ||
    saveSeedMutation.error) as Error | null;

  const handleSave = () => {
    let pending = (btConfig ? 1 : 0) + (seedConfig ? 1 : 0);
    if (pending === 0) return;
    let hasError = false;

    const handleSuccess = () => {
      pending--;
      if (pending === 0 && !hasError) {
        setDirty(false);
      }
    };

    const handleError = (err: any) => {
      hasError = true;
      showToast(err?.message || t("settingsTabs.queue.failedToSave"), "error");
    };

    if (btConfig) {
      saveBtMutation.mutate(
        {
          ...btConfig,
          downloadQueueSize: form.downloadQueueSize,
          seedQueueSize: form.seedQueueSize,
          queueStalledEnabled: form.queueStalledEnabled,
          queueStalledMinutes: form.queueStalledMinutes,
          idleSeedingLimitMinutes: form.idleSeedingLimitMinutes,
          globalShareLimitAction: form.globalShareLimitAction,
          autoShutdownAction: form.autoShutdownAction,
          autoShutdownCondition: form.autoShutdownCondition,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
    if (seedConfig) {
      saveSeedMutation.mutate(
        {
          ...seedConfig,
          globalSeedRatioLimit: form.globalSeedRatioLimit,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
  };

  if (btLoading || seedLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.queue.loading")}
      </div>
    );
  }

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={isPending}
        isError={isError}
        isSuccess={isSuccess}
        error={error}
        onSave={handleSave}
      />

      <SectionCard
        title={t("settingsTabs.queue.concurrencyTitle")}
        description={t("settingsTabs.queue.concurrencyDesc")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.queue.maxActiveDownloads")}
            value={form.downloadQueueSize}
            onChange={(v) => update("downloadQueueSize", v)}
            min={1}
            max={100}
            hint={t("settingsTabs.queue.maxActiveDownloadsHint")}
          />

          <NumberInput
            label={t("settingsTabs.queue.maxActiveSeeds")}
            value={form.seedQueueSize}
            onChange={(v) => update("seedQueueSize", v)}
            min={1}
            max={500}
            hint={t("settingsTabs.queue.maxActiveSeedsHint")}
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.queue.stalledTitle")}
        description={t("settingsTabs.queue.stalledDesc")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settingsTabs.queue.ignoreStalled")}
            checked={form.queueStalledEnabled}
            onChange={(v) => update("queueStalledEnabled", v)}
            hint={t("settingsTabs.queue.ignoreStalledHint")}
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            <NumberInput
              label={t("settingsTabs.queue.stalledTimeout")}
              value={form.queueStalledMinutes}
              onChange={(v) => update("queueStalledMinutes", v)}
              disabled={!form.queueStalledEnabled}
              min={1}
              max={1440}
              suffix={t("settingsTabs.queue.minutes")}
              hint={t("settingsTabs.queue.stalledTimeoutHint")}
            />

            <NumberInput
              label={t("settingsTabs.queue.idleSeedingTimeout")}
              value={form.idleSeedingLimitMinutes}
              onChange={(v) => update("idleSeedingLimitMinutes", v)}
              min={0}
              max={10080}
              suffix={t("settingsTabs.queue.minutes")}
              hint={t("settingsTabs.queue.idleSeedingTimeoutHint")}
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.queue.shareRatioTitle")}
        description={t("settingsTabs.queue.shareRatioDesc")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.queue.globalShareRatio")}
            value={form.globalSeedRatioLimit}
            onChange={(v) => update("globalSeedRatioLimit", v)}
            min={0}
            max={100}
            step={0.1}
            hint={t("settingsTabs.queue.globalShareRatioHint")}
          />

          <SelectInput
            label={t("settingsTabs.queue.actionOnShareGoal")}
            value={form.globalShareLimitAction}
            onChange={(v) => update("globalShareLimitAction", v)}
            options={[
              { value: "Pause", label: t("settingsTabs.queue.actionPause") },
              {
                value: t("settingsTabs.batch2.remove"),
                label: t("settingsTabs.queue.actionRemove"),
              },
              {
                value: "RemoveWithData",
                label: t("settingsTabs.queue.actionRemoveData"),
              },
              {
                value: "SuperSeeding",
                label: t("settingsTabs.queue.actionSuperSeeding"),
              },
            ]}
            hint={t("settingsTabs.queue.actionOnShareGoalHint")}
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.queue.powerManagementTitle")}
        description={t("settingsTabs.queue.powerManagementDesc")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label={t("settingsTabs.queue.actionOnCompletion")}
            value={form.autoShutdownAction}
            onChange={(v) => update("autoShutdownAction", v)}
            options={[
              { value: "None", label: t("settingsTabs.queue.actionNone") },
              {
                value: "Shutdown",
                label: t("settingsTabs.queue.actionShutdown"),
              },
              {
                value: "Suspend",
                label: t("settingsTabs.queue.actionSuspend"),
              },
              {
                value: "Hibernate",
                label: t("settingsTabs.queue.actionHibernate"),
              },
              {
                value: "ExitApplication",
                label: t("settingsTabs.queue.actionExit"),
              },
            ]}
            hint={t("settingsTabs.queue.actionOnCompletionHint")}
          />

          <SelectInput
            label={t("settingsTabs.queue.completionCondition")}
            value={form.autoShutdownCondition}
            onChange={(v) => update("autoShutdownCondition", v)}
            disabled={form.autoShutdownAction === "None"}
            options={[
              {
                value: "WhenDownloadsComplete",
                label: t("settingsTabs.queue.conditionDownloadsComplete"),
              },
              {
                value: "WhenAllTorrentsComplete",
                label: t("settingsTabs.queue.conditionAllComplete"),
              },
            ]}
            hint={t("settingsTabs.queue.completionConditionHint")}
          />
        </div>
      </SectionCard>

      <CategorySettingsTab embedded />
    </div>
  );
}

export default QueueSettingsTab;
