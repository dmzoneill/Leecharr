import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useSeedingConfig,
  useSaveSeedingConfig,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, SelectInput, Toggle } from "./shared";

export function QueueSettingsTab() {
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
        autoShutdownCondition: btConfig?.autoShutdownCondition || "WhenDownloadsComplete",
      });
      setDirty(false);
    }
  }, [btConfig, seedConfig]);

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const isPending = saveBtMutation.isPending || saveSeedMutation.isPending;
  const isError = saveBtMutation.isError || saveSeedMutation.isError;
  const isSuccess = saveBtMutation.isSuccess && saveSeedMutation.isSuccess;
  const error = (saveBtMutation.error || saveSeedMutation.error) as Error | null;

  const handleSave = () => {
    if (btConfig) {
      saveBtMutation.mutate({
        ...btConfig,
        downloadQueueSize: form.downloadQueueSize,
        seedQueueSize: form.seedQueueSize,
        queueStalledEnabled: form.queueStalledEnabled,
        queueStalledMinutes: form.queueStalledMinutes,
        idleSeedingLimitMinutes: form.idleSeedingLimitMinutes,
        globalShareLimitAction: form.globalShareLimitAction,
        autoShutdownAction: form.autoShutdownAction,
        autoShutdownCondition: form.autoShutdownCondition,
      });
    }
    if (seedConfig) {
      saveSeedMutation.mutate({
        ...seedConfig,
        globalSeedRatioLimit: form.globalSeedRatioLimit,
      });
    }
    setDirty(false);
  };

  if (btLoading || seedLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading queue parameters...
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
        title="Active Queue Concurrency Limits"
        description="Limit the number of simultaneous active downloading and seeding swarms."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="Maximum Active Downloads"
            value={form.downloadQueueSize}
            onChange={(v) => update("downloadQueueSize", v)}
            min={1}
            max={100}
            hint="Maximum simultaneous torrents actively downloading"
          />

          <NumberInput
            label="Maximum Active Seeds"
            value={form.seedQueueSize}
            onChange={(v) => update("seedQueueSize", v)}
            min={1}
            max={500}
            hint="Maximum simultaneous torrents actively seeding"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="Stalled Transfer Detection & Idle Limits"
        description="Prevent dead or inactive swarms from consuming active queue concurrency slots."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Ignore Stalled Torrents in Active Queue Quota"
            checked={form.queueStalledEnabled}
            onChange={(v) => update("queueStalledEnabled", v)}
            hint="If a transfer is stalled at 0 KB/s, promote the next queued torrent automatically"
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            <NumberInput
              label="Stalled Inactivity Timeout"
              value={form.queueStalledMinutes}
              onChange={(v) => update("queueStalledMinutes", v)}
              disabled={!form.queueStalledEnabled}
              min={1}
              max={1440}
              suffix="minutes"
              hint="Inactivity threshold before marking a transfer stalled"
            />

            <NumberInput
              label="Idle Seeding Timeout"
              value={form.idleSeedingLimitMinutes}
              onChange={(v) => update("idleSeedingLimitMinutes", v)}
              min={0}
              max={10080}
              suffix="minutes"
              hint="Automatically pause seeds if no peer requests data for N minutes (0 = disabled)"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Global Share Ratio & Seeding Goals"
        description="Configure target share ratio goals before automatically pausing or stopping seeds."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="Global Share Ratio Target"
            value={form.globalSeedRatioLimit}
            onChange={(v) => update("globalSeedRatioLimit", v)}
            min={0}
            max={100}
            step={0.1}
            hint="Target ratio (e.g. 1.0 or 2.0). 0 = seed indefinitely until manual action."
          />

          <SelectInput
            label="Action on Reaching Share Goal"
            value={form.globalShareLimitAction}
            onChange={(v) => update("globalShareLimitAction", v)}
            options={[
              { value: "Pause", label: "Pause Seeding" },
              { value: "Remove", label: "Remove Torrent (Keep Data Files)" },
              { value: "RemoveWithData", label: "Remove Torrent & Delete Data" },
              { value: "SuperSeeding", label: "Switch to Super Seeding" },
            ]}
            hint="Automated lifecycle trigger executed when torrents meet target seed goals."
          />
        </div>
      </SectionCard>

      <SectionCard
        title="Automated Power Management & OS Actions"
        description="Trigger operating system sleep, hibernation, or shutdown when queue downloads complete."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label="Action on Completion"
            value={form.autoShutdownAction}
            onChange={(v) => update("autoShutdownAction", v)}
            options={[
              { value: "None", label: "None (Do Nothing)" },
              { value: "Shutdown", label: "Shutdown Computer" },
              { value: "Suspend", label: "Suspend / Sleep System" },
              { value: "Hibernate", label: "Hibernate System" },
              { value: "ExitApplication", label: "Exit Leecharr Process" },
            ]}
            hint="Operating system power command to execute automatically."
          />

          <SelectInput
            label="Completion Trigger Condition"
            value={form.autoShutdownCondition}
            onChange={(v) => update("autoShutdownCondition", v)}
            disabled={form.autoShutdownAction === "None"}
            options={[
              { value: "WhenDownloadsComplete", label: "When Active Downloads Complete" },
              { value: "WhenAllTorrentsComplete", label: "When All Torrents Finish (Queue Empty)" },
            ]}
            hint="Condition required to trigger the selected power management action."
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default QueueSettingsTab;
