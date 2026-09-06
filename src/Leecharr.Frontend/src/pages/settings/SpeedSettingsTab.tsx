import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  SelectInput,
  Toggle,
} from "./shared";

export function SpeedSettingsTab() {
  const { t } = useTranslation();

  const { data: config, isLoading } = useSeedingConfig();
  const saveMutation = useSaveSeedingConfig();

  const [form, setForm] = useState({
    maxDownloadSpeedKbps: 0,
    maxUploadSpeedKbps: 0,
    alternativeSpeedEnabled: false,
    altDownloadSpeedKbps: 2000,
    altUploadSpeedKbps: 500,
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
    speedVariationMin: 0.2,
    speedVariationMax: 0.8,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        maxDownloadSpeedKbps: config.maxDownloadSpeedKbps ?? 0,
        maxUploadSpeedKbps: config.maxUploadSpeedKbps ?? 0,
        alternativeSpeedEnabled: config.alternativeSpeedEnabled ?? false,
        altDownloadSpeedKbps: config.altDownloadSpeedKbps ?? 2000,
        altUploadSpeedKbps: config.altUploadSpeedKbps ?? 500,
        uploadDistributionAlgorithm:
          config.uploadDistributionAlgorithm || "Equal",
        uploadDistributionSpreadPercentage:
          config.uploadDistributionSpreadPercentage ?? 50,
        uploadRedistributionMode: config.uploadRedistributionMode || "tick",
        uploadCustomIntervalMinutes: config.uploadCustomIntervalMinutes ?? 5,
        uploadStoppedMinPercentage: config.uploadStoppedMinPercentage ?? 20,
        uploadStoppedMaxPercentage: config.uploadStoppedMaxPercentage ?? 40,
        downloadDistributionAlgorithm:
          config.downloadDistributionAlgorithm || "Equal",
        downloadDistributionSpreadPercentage:
          config.downloadDistributionSpreadPercentage ?? 50,
        downloadRedistributionMode: config.downloadRedistributionMode || "tick",
        downloadCustomIntervalMinutes:
          config.downloadCustomIntervalMinutes ?? 5,
        downloadStoppedMinPercentage: config.downloadStoppedMinPercentage ?? 20,
        downloadStoppedMaxPercentage: config.downloadStoppedMaxPercentage ?? 40,
        speedVariationMin: config.speedVariationMin ?? 0.2,
        speedVariationMax: config.speedVariationMax ?? 0.8,
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
        maxDownloadSpeedKbps: form.maxDownloadSpeedKbps,
        maxUploadSpeedKbps: form.maxUploadSpeedKbps,
        alternativeSpeedEnabled: form.alternativeSpeedEnabled,
        altDownloadSpeedKbps: form.altDownloadSpeedKbps,
        altUploadSpeedKbps: form.altUploadSpeedKbps,
        uploadDistributionAlgorithm: form.uploadDistributionAlgorithm,
        uploadDistributionSpreadPercentage:
          form.uploadDistributionSpreadPercentage,
        uploadRedistributionMode: form.uploadRedistributionMode,
        uploadCustomIntervalMinutes: form.uploadCustomIntervalMinutes,
        uploadStoppedMinPercentage: form.uploadStoppedMinPercentage,
        uploadStoppedMaxPercentage: form.uploadStoppedMaxPercentage,
        downloadDistributionAlgorithm: form.downloadDistributionAlgorithm,
        downloadDistributionSpreadPercentage:
          form.downloadDistributionSpreadPercentage,
        downloadRedistributionMode: form.downloadRedistributionMode,
        downloadCustomIntervalMinutes: form.downloadCustomIntervalMinutes,
        downloadStoppedMinPercentage: form.downloadStoppedMinPercentage,
        downloadStoppedMaxPercentage: form.downloadStoppedMaxPercentage,
        speedVariationMin: form.speedVariationMin,
        speedVariationMax: form.speedVariationMax,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.loadingSpeedLimitParameters")}
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
        title={t("settingsTabs.globalBandwidthRateLimits")}
        description={t("settingsTabs.globalBandwidthRateLimitsDesc")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.globalMaxDownloadSpeed")}
            value={form.maxDownloadSpeedKbps}
            onChange={(v) => update("maxDownloadSpeedKbps", v)}
            min={0}
            max={10000000}
            step={100}
            suffix="KB/s"
            hint={t("settingsTabs.unlimitedBandwidthHint")}
          />

          <NumberInput
            label={t("settingsTabs.globalMaxUploadSpeed")}
            value={form.maxUploadSpeedKbps}
            onChange={(v) => update("maxUploadSpeedKbps", v)}
            min={0}
            max={10000000}
            step={100}
            suffix="KB/s"
            hint={t("settingsTabs.unlimitedBandwidthHint")}
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.alternativeThrottlingProfile")}
        description={t("settingsTabs.alternativeThrottlingProfileDesc")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settingsTabs.engageAlternativeSpeedLimits")}
            checked={form.alternativeSpeedEnabled}
            onChange={(v) => update("alternativeSpeedEnabled", v)}
            hint={t("settingsTabs.engageAlternativeSpeedLimitsHint")}
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            <NumberInput
              label={t("settingsTabs.alternativeDownloadCap")}
              value={form.altDownloadSpeedKbps}
              onChange={(v) => update("altDownloadSpeedKbps", v)}
              min={1}
              max={10000000}
              step={100}
              suffix="KB/s"
            />

            <NumberInput
              label={t("settingsTabs.alternativeUploadCap")}
              value={form.altUploadSpeedKbps}
              onChange={(v) => update("altUploadSpeedKbps", v)}
              min={1}
              max={10000000}
              step={100}
              suffix="KB/s"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.swarmBandwidthDistributionCurves")}
        description={t("settingsTabs.swarmBandwidthDistributionCurvesDesc")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label={t("settingsTabs.uploadDistributionCurve")}
            value={form.uploadDistributionAlgorithm}
            onChange={(v) => update("uploadDistributionAlgorithm", v)}
            options={[
              {
                value: "Equal",
                label: t("settingsTabs.equalDistribution"),
              },
              {
                value: "Pareto",
                label: t("settingsTabs.pareto8020ActiveSwarms"),
              },
              { value: "PowerLaw", label: t("settingsTabs.powerLawCurve") },
              {
                value: "LogNormal",
                label: t("settingsTabs.logNormalDistribution"),
              },
            ]}
          />

          <SelectInput
            label={t("settingsTabs.downloadDistributionCurve")}
            value={form.downloadDistributionAlgorithm}
            onChange={(v) => update("downloadDistributionAlgorithm", v)}
            options={[
              {
                value: "Equal",
                label: t("settingsTabs.equalDistribution"),
              },
              {
                value: "Pareto",
                label: t("settingsTabs.pareto8020PrimaryDownloads"),
              },
              { value: "PowerLaw", label: t("settingsTabs.powerLawCurve") },
              {
                value: "LogNormal",
                label: t("settingsTabs.logNormalDistribution"),
              },
            ]}
          />

          <NumberInput
            label={t("settingsTabs.distributionSpreadFactor")}
            value={form.uploadDistributionSpreadPercentage}
            onChange={(v) => update("uploadDistributionSpreadPercentage", v)}
            min={10}
            max={90}
            suffix="%"
            hint={t("settingsTabs.distributionSpreadFactorHint")}
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default SpeedSettingsTab;
