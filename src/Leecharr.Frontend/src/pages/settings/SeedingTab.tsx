import { useState, useEffect } from "react";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import type { SeedingConfig } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  NumberInput,
  SectionCard,
} from "./shared";

export function SeedingTab() {
  const { data: config, isLoading } = useSeedingConfig();
  const save = useSaveSeedingConfig();
  const [form, setForm] = useState<SeedingConfig>({
    id: 1,
    maxUploadSpeedKbps: 625,
    maxDownloadSpeedKbps: 1250,
    alternativeSpeedEnabled: false,
    altUploadSpeedKbps: 50,
    altDownloadSpeedKbps: 100,
    globalSeedRatioLimit: 0,
    speedVariationMin: 0.2,
    speedVariationMax: 0.8,
    uploadDistributionAlgorithm: "Equal",
    uploadDistributionSpreadPercentage: 50,
    uploadRedistributionMode: "tick",
    uploadCustomIntervalMinutes: 5,
    uploadStoppedMinPercentage: 20,
    uploadStoppedMaxPercentage: 40,
    downloadDistributionAlgorithm: "Equal",
    downloadDistributionSpreadPercentage: 50,
    downloadRedistributionMode: "tick",
    downloadCustomIntervalMinutes: 5,
    downloadStoppedMinPercentage: 20,
    downloadStoppedMaxPercentage: 40,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof SeedingConfig>(
    key: K,
    value: SeedingConfig[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const distOptions = [
    { value: "Equal", label: "Equal Distribution" },
    { value: "Pareto", label: "Pareto (80/20 Rule)" },
    { value: "PowerLaw", label: "Power Law" },
    { value: "LogNormal", label: "Log Normal" },
  ];

  const redistOptions = [
    { value: "tick", label: "Every Tick (Real-Time)" },
    { value: "interval", label: "Custom Interval" },
    { value: "fixed", label: "Fixed" },
  ];

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
        title="Quick Seeding Profiles"
        description="One-click presets to configure speed limits, distribution algorithms, and ratio rules"
      >
        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            marginBottom: "0.75rem",
            flexWrap: "wrap",
          }}
        >
          <button
            className="btn btn-outline btn-small"
            onClick={() => {
              setForm((prev) => ({
                ...prev,
                maxUploadSpeedKbps: 100,
                maxDownloadSpeedKbps: 200,
                uploadDistributionAlgorithm: "Equal",
                uploadDistributionSpreadPercentage: 30,
                downloadDistributionAlgorithm: "Equal",
                downloadDistributionSpreadPercentage: 30,
                globalSeedRatioLimit: 1.5,
              }));
              setDirty(true);
            }}
          >
            🛡️ Conservative (100 KB/s, 1.5x)
          </button>
          <button
            className="btn btn-primary btn-small"
            onClick={() => {
              setForm((prev) => ({
                ...prev,
                maxUploadSpeedKbps: 625,
                maxDownloadSpeedKbps: 1250,
                uploadDistributionAlgorithm: "Pareto",
                uploadDistributionSpreadPercentage: 50,
                downloadDistributionAlgorithm: "Pareto",
                downloadDistributionSpreadPercentage: 50,
                globalSeedRatioLimit: 2.0,
              }));
              setDirty(true);
            }}
          >
            ⚡ Balanced (625 KB/s, 2.0x)
          </button>
          <button
            className="btn btn-outline btn-small"
            onClick={() => {
              setForm((prev) => ({
                ...prev,
                maxUploadSpeedKbps: 2500,
                maxDownloadSpeedKbps: 5000,
                uploadDistributionAlgorithm: "PowerLaw",
                uploadDistributionSpreadPercentage: 80,
                downloadDistributionAlgorithm: "PowerLaw",
                downloadDistributionSpreadPercentage: 80,
                globalSeedRatioLimit: 0,
              }));
              setDirty(true);
            }}
          >
            🚀 Aggressive (2.5 MB/s, Unlimited)
          </button>
        </div>
        <div
          style={{
            fontSize: "0.8rem",
            color: "var(--text-muted)",
            lineHeight: 1.4,
          }}
        >
          Preset details — <strong>Conservative</strong>: low rate throttle,
          equal spread, 1.5x ratio goal. <strong>Balanced</strong>: standard
          default rates (5/10 Mbit), Pareto distribution, 2.0x ratio.{" "}
          <strong>Aggressive</strong>: high speed caps (20/40 Mbit), power-law
          curve, no ratio limit.
        </div>
      </SectionCard>

      <SectionCard
        title="Global Speed & Seeding Limits"
        description="Base rate caps and automatic ratio completion thresholds"
      >
        <NumberInput
          label="Max Upload Speed"
          value={form.maxUploadSpeedKbps}
          onChange={(v) => set("maxUploadSpeedKbps", v)}
          min={1}
          suffix="KB/s"
          hint="Default 625 KB/s (5 Mbit/s)"
        />
        <NumberInput
          label="Max Download Speed"
          value={form.maxDownloadSpeedKbps}
          onChange={(v) => set("maxDownloadSpeedKbps", v)}
          min={1}
          suffix="KB/s"
          hint="Default 1250 KB/s (10 Mbit/s)"
        />
        <NumberInput
          label="Global Seed Ratio Limit"
          value={form.globalSeedRatioLimit}
          onChange={(v) => set("globalSeedRatioLimit", v)}
          min={0}
          step={0.1}
          hint="Stop seeding automatically when ratio reaches this value (0 = unlimited)"
        />
      </SectionCard>

      <SectionCard
        title="Alternative Speed Limits (Secondary Throttling)"
        description="Reduced secondary bandwidth limits that can be toggled on demand or via scheduler"
      >
        <Toggle
          label="Enable Alt Speeds"
          checked={form.alternativeSpeedEnabled}
          onChange={(v) => set("alternativeSpeedEnabled", v)}
          hint="Enable secondary throttled speed profile"
        />
        <NumberInput
          label="Alt Upload Speed"
          value={form.altUploadSpeedKbps}
          onChange={(v) => set("altUploadSpeedKbps", v)}
          min={1}
          suffix="KB/s"
          disabled={!form.alternativeSpeedEnabled}
        />
        <NumberInput
          label="Alt Download Speed"
          value={form.altDownloadSpeedKbps}
          onChange={(v) => set("altDownloadSpeedKbps", v)}
          min={1}
          suffix="KB/s"
          disabled={!form.alternativeSpeedEnabled}
        />
      </SectionCard>

      <SectionCard
        title="Upload Bandwidth Distribution Engine"
        description="Mathematical curve allocating bandwidth across active torrents"
      >
        <SelectInput
          label="Algorithm"
          value={form.uploadDistributionAlgorithm}
          onChange={(v) => set("uploadDistributionAlgorithm", v)}
          options={distOptions}
          hint="Distribution algorithm used to split available upload capacity"
        />
        <NumberInput
          label="Spread %"
          value={form.uploadDistributionSpreadPercentage}
          onChange={(v) => set("uploadDistributionSpreadPercentage", v)}
          min={0}
          max={100}
          hint="Variance spread percentage across active swarms"
        />
        <SelectInput
          label="Redistribution Mode"
          value={form.uploadRedistributionMode}
          onChange={(v) => set("uploadRedistributionMode", v)}
          options={redistOptions}
          hint="Rebalancing interval"
        />
        <NumberInput
          label="Custom Interval"
          value={form.uploadCustomIntervalMinutes}
          onChange={(v) => set("uploadCustomIntervalMinutes", v)}
          min={1}
          suffix="minutes"
          disabled={form.uploadRedistributionMode !== "interval"}
        />
        <NumberInput
          label="Stopped Min %"
          value={form.uploadStoppedMinPercentage}
          onChange={(v) => set("uploadStoppedMinPercentage", v)}
          min={0}
          max={100}
          hint="Minimum percentage of torrents randomly paused/stopped to simulate natural seeding"
        />
        <NumberInput
          label="Stopped Max %"
          value={form.uploadStoppedMaxPercentage}
          onChange={(v) => set("uploadStoppedMaxPercentage", v)}
          min={0}
          max={100}
          hint="Maximum percentage of torrents randomly paused/stopped"
        />
      </SectionCard>

      <SectionCard
        title="Download Bandwidth Distribution Engine"
        description="Mathematical curve allocating bandwidth across active downloads"
      >
        <SelectInput
          label="Algorithm"
          value={form.downloadDistributionAlgorithm}
          onChange={(v) => set("downloadDistributionAlgorithm", v)}
          options={distOptions}
          hint="Distribution algorithm used to split available download capacity"
        />
        <NumberInput
          label="Spread %"
          value={form.downloadDistributionSpreadPercentage}
          onChange={(v) => set("downloadDistributionSpreadPercentage", v)}
          min={0}
          max={100}
          hint="Variance spread percentage across active downloads"
        />
        <SelectInput
          label="Redistribution Mode"
          value={form.downloadRedistributionMode}
          onChange={(v) => set("downloadRedistributionMode", v)}
          options={redistOptions}
          hint="Rebalancing interval"
        />
        <NumberInput
          label="Custom Interval"
          value={form.downloadCustomIntervalMinutes}
          onChange={(v) => set("downloadCustomIntervalMinutes", v)}
          min={1}
          suffix="minutes"
          disabled={form.downloadRedistributionMode !== "interval"}
        />
        <NumberInput
          label="Stopped Min %"
          value={form.downloadStoppedMinPercentage}
          onChange={(v) => set("downloadStoppedMinPercentage", v)}
          min={0}
          max={100}
        />
        <NumberInput
          label="Stopped Max %"
          value={form.downloadStoppedMaxPercentage}
          onChange={(v) => set("downloadStoppedMaxPercentage", v)}
          min={0}
          max={100}
        />
      </SectionCard>
    </div>
  );
}
