import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useSimulationConfig,
  useSaveSimulationConfig,
  usePeerProtocolConfig,
  useSavePeerProtocolConfig,
} from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  TextInput,
  SelectInput,
  Toggle,
} from "./shared";
import { useToast } from "../../context/ToastContext";

const CLIENT_PRESETS: Record<
  string,
  { userAgent: string; peerIdPrefix: string }
> = {
  qBittorrent: { userAgent: "qBittorrent/4.4.2", peerIdPrefix: "-qB4420-" },
  Deluge: {
    userAgent: "Deluge/2.0.5 libtorrent/1.2.14.0",
    peerIdPrefix: "-DE2050-",
  },
  Transmission: { userAgent: "Transmission/3.00", peerIdPrefix: "-TR3000-" },
  uTorrent: { userAgent: "uTorrent/3550", peerIdPrefix: "-UT3550-" },
  BiglyBT: { userAgent: "BiglyBT/3.4.0.0", peerIdPrefix: "-AZ3400-" },
  Leecharr: { userAgent: "Leecharr/1.0.0", peerIdPrefix: "-LC1000-" },
};

export function ClientEmulationSettingsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();

  const { data: simConfig, isLoading: simLoading } = useSimulationConfig();
  const saveSimMutation = useSaveSimulationConfig();

  const { data: peerConfig, isLoading: peerLoading } = usePeerProtocolConfig();
  const savePeerMutation = useSavePeerProtocolConfig();

  const [form, setForm] = useState({
    clientBehaviorEngineEnabled: true,
    primaryClient: "qBittorrent",
    bitTorrentUserAgent: "qBittorrent/4.4.2",
    peerIdPrefix: "-qB4420-",
    behaviorVariation: 0.15,
    clientProfileSwitching: false,
    switchClientProbability: 0.05,
    trafficPatternProfile: "HomeUser",
    realisticVariations: true,
    timeBasedPatterns: true,
    swarmIntelligenceEnabled: true,
    swarmAdaptationRate: 0.1,
    swarmPeerAnalysisDepth: 10,
    seederUploadActivityProbability: 0.7,
    peerIdleChance: 0.1,
    peerDropoutProbability: 0.05,
    connectionRotationPercentage: 0.2,
    announceIntervalSeconds: 1800,
    minAnnounceIntervalSeconds: 300,
    scrapeIntervalSeconds: 900,
    peerRequestCount: 16,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (btConfig || simConfig || peerConfig) {
      const primaryClient = simConfig?.primaryClient || "qBittorrent";
      const preset =
        CLIENT_PRESETS[primaryClient] || CLIENT_PRESETS.qBittorrent;
      const bitTorrentUserAgent =
        btConfig?.bitTorrentUserAgent &&
        btConfig.bitTorrentUserAgent !== "Leecharr/1.0"
          ? btConfig.bitTorrentUserAgent
          : primaryClient === "Leecharr"
            ? "Leecharr/1.0.0"
            : preset.userAgent;
      const peerIdPrefix =
        btConfig?.peerIdPrefix && btConfig.peerIdPrefix !== "-LC1000-"
          ? btConfig.peerIdPrefix
          : primaryClient === "Leecharr"
            ? "-LC1000-"
            : preset.peerIdPrefix;

      setForm({
        clientBehaviorEngineEnabled:
          simConfig?.clientBehaviorEngineEnabled ?? true,
        primaryClient,
        bitTorrentUserAgent,
        peerIdPrefix,
        behaviorVariation: simConfig?.behaviorVariation ?? 0.15,
        clientProfileSwitching: simConfig?.clientProfileSwitching ?? false,
        switchClientProbability: simConfig?.switchClientProbability ?? 0.05,
        trafficPatternProfile: simConfig?.trafficPatternProfile || "HomeUser",
        realisticVariations: simConfig?.realisticVariations ?? true,
        timeBasedPatterns: simConfig?.timeBasedPatterns ?? true,
        swarmIntelligenceEnabled: simConfig?.swarmIntelligenceEnabled ?? true,
        swarmAdaptationRate: simConfig?.swarmAdaptationRate ?? 0.1,
        swarmPeerAnalysisDepth: simConfig?.swarmPeerAnalysisDepth ?? 10,
        seederUploadActivityProbability:
          peerConfig?.seederUploadActivityProbability ?? 0.7,
        peerIdleChance: peerConfig?.peerIdleChance ?? 0.1,
        peerDropoutProbability: peerConfig?.peerDropoutProbability ?? 0.05,
        connectionRotationPercentage:
          peerConfig?.connectionRotationPercentage ?? 0.2,
        announceIntervalSeconds: btConfig?.announceIntervalSeconds ?? 1800,
        minAnnounceIntervalSeconds: btConfig?.minAnnounceIntervalSeconds ?? 300,
        scrapeIntervalSeconds: btConfig?.scrapeIntervalSeconds ?? 900,
        peerRequestCount: peerConfig?.peerRequestCount ?? 16,
      });
      setDirty(false);
    }
  }, [btConfig, simConfig, peerConfig]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handlePrimaryClientChange = (client: string) => {
    const preset = CLIENT_PRESETS[client] || CLIENT_PRESETS.qBittorrent;
    setForm((prev) => ({
      ...prev,
      primaryClient: client,
      bitTorrentUserAgent: preset.userAgent,
      peerIdPrefix: preset.peerIdPrefix,
    }));
    setDirty(true);
  };

  const isPending =
    saveBtMutation.isPending ||
    saveSimMutation.isPending ||
    savePeerMutation.isPending;
  const isError =
    saveBtMutation.isError ||
    saveSimMutation.isError ||
    savePeerMutation.isError;
  const isSuccess =
    (!btConfig || saveBtMutation.isSuccess) &&
    (!simConfig || saveSimMutation.isSuccess) &&
    (!peerConfig || savePeerMutation.isSuccess) &&
    (saveBtMutation.isSuccess ||
      saveSimMutation.isSuccess ||
      savePeerMutation.isSuccess);
  const error = (saveBtMutation.error ||
    saveSimMutation.error ||
    savePeerMutation.error) as Error | null;

  const handleSave = () => {
    let pending =
      (btConfig ? 1 : 0) + (simConfig ? 1 : 0) + (peerConfig ? 1 : 0);
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
      showToast(
        err?.message || t("settingsTabs.clientEmulation.saveError"),
        "error",
      );
    };

    if (btConfig) {
      saveBtMutation.mutate(
        {
          ...btConfig,
          bitTorrentUserAgent: form.bitTorrentUserAgent,
          peerIdPrefix: form.peerIdPrefix,
          announceIntervalSeconds: form.announceIntervalSeconds,
          minAnnounceIntervalSeconds: form.minAnnounceIntervalSeconds,
          scrapeIntervalSeconds: form.scrapeIntervalSeconds,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
    if (simConfig) {
      saveSimMutation.mutate(
        {
          ...simConfig,
          clientBehaviorEngineEnabled: form.clientBehaviorEngineEnabled,
          primaryClient: form.primaryClient,
          behaviorVariation: form.behaviorVariation,
          clientProfileSwitching: form.clientProfileSwitching,
          switchClientProbability: form.switchClientProbability,
          trafficPatternProfile: form.trafficPatternProfile,
          realisticVariations: form.realisticVariations,
          timeBasedPatterns: form.timeBasedPatterns,
          swarmIntelligenceEnabled: form.swarmIntelligenceEnabled,
          swarmAdaptationRate: form.swarmAdaptationRate,
          swarmPeerAnalysisDepth: form.swarmPeerAnalysisDepth,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
    if (peerConfig) {
      savePeerMutation.mutate(
        {
          ...peerConfig,
          seederUploadActivityProbability: form.seederUploadActivityProbability,
          peerIdleChance: form.peerIdleChance,
          peerDropoutProbability: form.peerDropoutProbability,
          connectionRotationPercentage: form.connectionRotationPercentage,
          peerRequestCount: form.peerRequestCount,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
  };

  if (btLoading || simLoading || peerLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.clientEmulation.loading")}
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
        title={t("settingsTabs.clientEmulation.title")}
        description={t("settingsTabs.clientEmulation.description")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label={t("settingsTabs.clientEmulation.primaryClient")}
            value={form.primaryClient}
            onChange={handlePrimaryClientChange}
            options={[
              {
                value: "qBittorrent",
                label: t("settingsTabs.clientEmulation.options.qBittorrent"),
              },
              {
                value: "Deluge",
                label: t("settingsTabs.clientEmulation.options.deluge"),
              },
              {
                value: "Transmission",
                label: t("settingsTabs.clientEmulation.options.transmission"),
              },
              {
                value: "uTorrent",
                label: t("settingsTabs.clientEmulation.options.uTorrent"),
              },
              {
                value: "BiglyBT",
                label: t("settingsTabs.clientEmulation.options.biglyBT"),
              },
              {
                value: "Leecharr",
                label: t("settingsTabs.clientEmulation.options.leecharr"),
              },
            ]}
            hint={t("settingsTabs.clientEmulation.primaryClientHint")}
          />

          <SelectInput
            label={t("settingsTabs.clientEmulation.trafficPattern")}
            value={form.trafficPatternProfile}
            onChange={(v) => update("trafficPatternProfile", v)}
            options={[
              {
                value: "HomeUser",
                label: t("settingsTabs.clientEmulation.options.homeUser"),
              },
              {
                value: "balanced",
                label: t("settingsTabs.clientEmulation.options.balanced"),
              },
              {
                value: "burst",
                label: t("settingsTabs.clientEmulation.options.burst"),
              },
              {
                value: "stealth",
                label: t("settingsTabs.clientEmulation.options.stealth"),
              },
            ]}
          />

          <TextInput
            label={t("settingsTabs.clientEmulation.customUserAgent")}
            value={form.bitTorrentUserAgent}
            onChange={(v) => update("bitTorrentUserAgent", v)}
            hint={t("settingsTabs.clientEmulation.customUserAgentHint")}
          />

          <TextInput
            label={t("settingsTabs.clientEmulation.customPeerId")}
            value={form.peerIdPrefix}
            onChange={(v) => update("peerIdPrefix", v)}
            hint={t("settingsTabs.clientEmulation.customPeerIdHint")}
          />
        </div>

        <div
          style={{
            marginTop: "1rem",
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
              label={t("settingsTabs.clientEmulation.enableSwarmIntelligence")}
              checked={form.swarmIntelligenceEnabled}
              onChange={(v) => update("swarmIntelligenceEnabled", v)}
              hint={t(
                "settingsTabs.clientEmulation.enableSwarmIntelligenceHint",
              )}
            />

            <Toggle
              label={t("settingsTabs.clientEmulation.diurnalPatterns")}
              checked={form.timeBasedPatterns}
              onChange={(v) => update("timeBasedPatterns", v)}
              hint={t("settingsTabs.clientEmulation.diurnalPatternsHint")}
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.clientEmulation.intervalsTitle")}
        description={t("settingsTabs.clientEmulation.intervalsDescription")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.clientEmulation.announceInterval")}
            value={form.announceIntervalSeconds}
            onChange={(v) => update("announceIntervalSeconds", v)}
            min={60}
            max={7200}
            suffix={t("settingsTabs.batch2.sec")}
            hint={t("settingsTabs.clientEmulation.announceIntervalHint")}
          />

          <NumberInput
            label={t("settingsTabs.clientEmulation.minAnnounceClamp")}
            value={form.minAnnounceIntervalSeconds}
            onChange={(v) => update("minAnnounceIntervalSeconds", v)}
            min={30}
            max={1800}
            suffix={t("settingsTabs.batch2.sec")}
            hint={t("settingsTabs.clientEmulation.minAnnounceClampHint")}
          />

          <NumberInput
            label={t("settingsTabs.clientEmulation.scrapeInterval")}
            value={form.scrapeIntervalSeconds}
            onChange={(v) => update("scrapeIntervalSeconds", v)}
            min={60}
            max={3600}
            suffix={t("settingsTabs.batch2.sec")}
            hint={t("settingsTabs.clientEmulation.scrapeIntervalHint")}
          />

          <NumberInput
            label={t("settingsTabs.clientEmulation.peersRequested")}
            value={form.peerRequestCount}
            onChange={(v) => update("peerRequestCount", v)}
            min={10}
            max={500}
            hint={t("settingsTabs.clientEmulation.peersRequestedHint")}
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default ClientEmulationSettingsTab;
