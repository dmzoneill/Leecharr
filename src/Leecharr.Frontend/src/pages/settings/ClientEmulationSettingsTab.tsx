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

export function ClientEmulationSettingsTab() {
  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();

  const { data: simConfig, isLoading: simLoading } = useSimulationConfig();
  const saveSimMutation = useSaveSimulationConfig();

  const { data: peerConfig, isLoading: peerLoading } = usePeerProtocolConfig();
  const savePeerMutation = useSavePeerProtocolConfig();

  const [form, setForm] = useState({
    clientBehaviorEngineEnabled: true,
    primaryClient: "qBittorrent",
    bitTorrentUserAgent: "Leecharr/1.0",
    peerIdPrefix: "-LC1000-",
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
      setForm({
        clientBehaviorEngineEnabled: simConfig?.clientBehaviorEngineEnabled ?? true,
        primaryClient: simConfig?.primaryClient || "qBittorrent",
        bitTorrentUserAgent: btConfig?.bitTorrentUserAgent || "Leecharr/1.0",
        peerIdPrefix: btConfig?.peerIdPrefix || "-LC1000-",
        behaviorVariation: simConfig?.behaviorVariation ?? 0.15,
        clientProfileSwitching: simConfig?.clientProfileSwitching ?? false,
        switchClientProbability: simConfig?.switchClientProbability ?? 0.05,
        trafficPatternProfile: simConfig?.trafficPatternProfile || "HomeUser",
        realisticVariations: simConfig?.realisticVariations ?? true,
        timeBasedPatterns: simConfig?.timeBasedPatterns ?? true,
        swarmIntelligenceEnabled: simConfig?.swarmIntelligenceEnabled ?? true,
        swarmAdaptationRate: simConfig?.swarmAdaptationRate ?? 0.1,
        swarmPeerAnalysisDepth: simConfig?.swarmPeerAnalysisDepth ?? 10,
        seederUploadActivityProbability: peerConfig?.seederUploadActivityProbability ?? 0.7,
        peerIdleChance: peerConfig?.peerIdleChance ?? 0.1,
        peerDropoutProbability: peerConfig?.peerDropoutProbability ?? 0.05,
        connectionRotationPercentage: peerConfig?.connectionRotationPercentage ?? 0.2,
        announceIntervalSeconds: btConfig?.announceIntervalSeconds ?? 1800,
        minAnnounceIntervalSeconds: btConfig?.minAnnounceIntervalSeconds ?? 300,
        scrapeIntervalSeconds: btConfig?.scrapeIntervalSeconds ?? 900,
        peerRequestCount: peerConfig?.peerRequestCount ?? 16,
      });
      setDirty(false);
    }
  }, [btConfig, simConfig, peerConfig]);

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const isPending = saveBtMutation.isPending || saveSimMutation.isPending || savePeerMutation.isPending;
  const isError = saveBtMutation.isError || saveSimMutation.isError || savePeerMutation.isError;
  const isSuccess = saveBtMutation.isSuccess && saveSimMutation.isSuccess && savePeerMutation.isSuccess;
  const error = (saveBtMutation.error || saveSimMutation.error || savePeerMutation.error) as Error | null;

  const handleSave = () => {
    if (btConfig) {
      saveBtMutation.mutate({
        ...btConfig,
        bitTorrentUserAgent: form.bitTorrentUserAgent,
        peerIdPrefix: form.peerIdPrefix,
        announceIntervalSeconds: form.announceIntervalSeconds,
        minAnnounceIntervalSeconds: form.minAnnounceIntervalSeconds,
        scrapeIntervalSeconds: form.scrapeIntervalSeconds,
      });
    }
    if (simConfig) {
      saveSimMutation.mutate({
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
      });
    }
    if (peerConfig) {
      savePeerMutation.mutate({
        ...peerConfig,
        seederUploadActivityProbability: form.seederUploadActivityProbability,
        peerIdleChance: form.peerIdleChance,
        peerDropoutProbability: form.peerDropoutProbability,
        connectionRotationPercentage: form.connectionRotationPercentage,
        peerRequestCount: form.peerRequestCount,
      });
    }
    setDirty(false);
  };

  if (btLoading || simLoading || peerLoading) {
    return <div className="loading" style={{ padding: "2rem" }}>Loading client emulation settings...</div>;
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
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem" }}>
          <SelectInput
            label="Primary Emulated Client"
            value={form.primaryClient}
            onChange={(v) => update("primaryClient", v)}
            options={[
              { value: "qBittorrent", label: "qBittorrent (v4.6+ / libtorrent)" },
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
              { value: "HomeUser", label: "Home Broadband User (Organic Diurnal Curves)" },
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

        <div style={{ marginTop: "1rem", borderTop: "1px solid var(--border-light)", paddingTop: "1rem" }}>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem" }}>
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
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: "1rem" }}>
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
