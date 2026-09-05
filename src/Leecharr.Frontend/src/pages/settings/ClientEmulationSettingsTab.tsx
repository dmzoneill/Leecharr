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
        err?.message || "Failed to save client emulation settings",
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
        Loading client emulation settings...
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
        title="Client Profile Emulation & Private Tracker Stealth"
        description="Emulate real BitTorrent client signatures, peer ID prefixes, and Azureus handshake formats."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label="Primary Emulated Client"
            value={form.primaryClient}
            onChange={handlePrimaryClientChange}
            options={[
              {
                value: "qBittorrent",
                label: "qBittorrent (v4.6+ / libtorrent)",
              },
              { value: "Deluge", label: "Deluge (v2.1+)" },
              { value: "Transmission", label: "Transmission (v4.0+)" },
              { value: "uTorrent", label: "uTorrent (v3.5.5 Classic)" },
              { value: "BiglyBT", label: "BiglyBT / Vuze" },
              { value: "Leecharr", label: "Leecharr Native (-LC1000-)" },
            ]}
            hint="Determines default User-Agent, extension handshake bitmask, and peer ID structure"
          />

          <SelectInput
            label="Traffic Pattern Curve"
            value={form.trafficPatternProfile}
            onChange={(v) => update("trafficPatternProfile", v)}
            options={[
              {
                value: "HomeUser",
                label: "Home Broadband User (Organic Diurnal Curves)",
              },
              { value: "balanced", label: "Balanced Consistent Seedbox" },
              { value: "burst", label: "Burst / High Throughput" },
              { value: "stealth", label: "Stealth / Low Profile" },
            ]}
          />

          <TextInput
            label="Custom Tracker User-Agent"
            value={form.bitTorrentUserAgent}
            onChange={(v) => update("bitTorrentUserAgent", v)}
            hint="HTTP User-Agent sent during tracker announce queries"
          />

          <TextInput
            label="Custom Peer ID Prefix"
            value={form.peerIdPrefix}
            onChange={(v) => update("peerIdPrefix", v)}
            hint="8-character prefix sent in peer handshake (e.g. -LC1000-)"
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
              label="Enable Swarm Intelligence & Heuristics"
              checked={form.swarmIntelligenceEnabled}
              onChange={(v) => update("swarmIntelligenceEnabled", v)}
              hint="Dynamically prioritizes unchoking peers with the highest upload reciprocity"
            />

            <Toggle
              label="Diurnal Time-Based Activity Patterns"
              checked={form.timeBasedPatterns}
              onChange={(v) => update("timeBasedPatterns", v)}
              hint="Simulates human usage cycles between day and night hours"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Tracker Query Intervals & Peer Requests"
        description="Configure announce and scrape frequencies sent to BitTorrent trackers."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="Announce Interval"
            value={form.announceIntervalSeconds}
            onChange={(v) => update("announceIntervalSeconds", v)}
            min={60}
            max={7200}
            suffix="sec"
            hint="Standard tracker announce cadence"
          />

          <NumberInput
            label="Minimum Announce Clamp"
            value={form.minAnnounceIntervalSeconds}
            onChange={(v) => update("minAnnounceIntervalSeconds", v)}
            min={30}
            max={1800}
            suffix="sec"
            hint="Prevents rapid announce hammer bans"
          />

          <NumberInput
            label="Scrape Statistics Interval"
            value={form.scrapeIntervalSeconds}
            onChange={(v) => update("scrapeIntervalSeconds", v)}
            min={60}
            max={3600}
            suffix="sec"
            hint="Cadence for seeder/leecher counts"
          />

          <NumberInput
            label="Peers Requested (numwant)"
            value={form.peerRequestCount}
            onChange={(v) => update("peerRequestCount", v)}
            min={10}
            max={500}
            hint="Number of peer IPs requested per announce"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default ClientEmulationSettingsTab;
