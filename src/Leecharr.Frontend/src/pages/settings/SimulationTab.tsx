import { useState, useEffect } from "react";
import { useSimulationConfig, useSaveSimulationConfig } from "../../api/hooks";
import type { SimulationConfig } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  NumberInput,
  SectionCard,
} from "./shared";

export function SimulationTab() {
  const { data: config, isLoading } = useSimulationConfig();
  const save = useSaveSimulationConfig();
  const [form, setForm] = useState<SimulationConfig>({
    id: 1,
    clientBehaviorEngineEnabled: true,
    primaryClient: "qBittorrent",
    behaviorVariation: 0.3,
    clientProfileSwitching: true,
    switchClientProbability: 0.05,
    trafficPatternProfile: "balanced",
    realisticVariations: true,
    timeBasedPatterns: true,
    swarmIntelligenceEnabled: true,
    swarmAdaptationRate: 0.5,
    swarmPeerAnalysisDepth: 10,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof SimulationConfig>(
    key: K,
    value: SimulationConfig[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

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
        title="Client Behavior Simulation Engine"
        description="Emulates realistic BitTorrent client behaviors and peer negotiation patterns"
      >
        <Toggle
          label="Behavior Engine"
          checked={form.clientBehaviorEngineEnabled}
          onChange={(v) => set("clientBehaviorEngineEnabled", v)}
          hint="Enable organic peer client simulation"
        />
        <SelectInput
          label="Primary Client Profile"
          value={form.primaryClient}
          onChange={(v) => set("primaryClient", v)}
          options={[
            { value: "qBittorrent", label: "qBittorrent (v4.4.2+)" },
            { value: "Deluge", label: "Deluge (libtorrent 1.2+)" },
            { value: "Transmission", label: "Transmission (v3.0+)" },
            { value: "uTorrent", label: "µTorrent (v3.5.5)" },
            { value: "BiglyBT", label: "BiglyBT (v3.0+)" },
          ]}
          disabled={!form.clientBehaviorEngineEnabled}
          hint="Default identity template for announce signatures"
        />
        <NumberInput
          label="Behavior Variation"
          value={form.behaviorVariation}
          onChange={(v) => set("behaviorVariation", v)}
          min={0}
          max={1}
          step={0.05}
          hint="Random variance factor (0.0 - 1.0) applied to packet timing"
          disabled={!form.clientBehaviorEngineEnabled}
        />
      </SectionCard>

      <SectionCard
        title="Dynamic Profile Switching"
        description="Periodically rotate client identity fingerprints between announce cycles"
      >
        <Toggle
          label="Client Identity Rotation"
          checked={form.clientProfileSwitching}
          onChange={(v) => set("clientProfileSwitching", v)}
          hint="Allow occasional subtle rotation of client identities"
        />
        <NumberInput
          label="Switch Probability"
          value={form.switchClientProbability}
          onChange={(v) => set("switchClientProbability", v)}
          min={0}
          max={1}
          step={0.01}
          suffix="/ announce"
          hint="Probability per announce cycle (0.0 - 1.0)"
          disabled={!form.clientProfileSwitching}
        />
      </SectionCard>

      <SectionCard
        title="Traffic Patterns & Diurnal Variation"
        description="Time-of-day traffic modeling and realistic seeding spikes"
      >
        <SelectInput
          label="Pattern Profile"
          value={form.trafficPatternProfile}
          onChange={(v) => set("trafficPatternProfile", v)}
          options={[
            { value: "conservative", label: "Conservative (Low Jitter)" },
            { value: "balanced", label: "Balanced (Natural ISP Curves)" },
            { value: "aggressive", label: "Aggressive (High Bursting)" },
          ]}
          hint="Overall traffic envelope shape"
        />
        <Toggle
          label="Realistic Variations"
          checked={form.realisticVariations}
          onChange={(v) => set("realisticVariations", v)}
          hint="Inject natural noise and micro-bursting into upload streams"
        />
        <Toggle
          label="Time-Based Diurnal Cycles"
          checked={form.timeBasedPatterns}
          onChange={(v) => set("timeBasedPatterns", v)}
          hint="Automatically dip speed during peak business hours and boost late at night"
        />
      </SectionCard>

      <SectionCard
        title="Swarm Intelligence"
        description="Adapt upload rate based on swarm seeder/leecher health ratios"
      >
        <Toggle
          label="Swarm Intelligence"
          checked={form.swarmIntelligenceEnabled}
          onChange={(v) => set("swarmIntelligenceEnabled", v)}
          hint="Dynamically prioritize dying swarms with few seeders"
        />
        <NumberInput
          label="Adaptation Rate"
          value={form.swarmAdaptationRate}
          onChange={(v) => set("swarmAdaptationRate", v)}
          min={0}
          max={1}
          step={0.1}
          hint="Rate of responsiveness to peer churn (0.0 - 1.0)"
          disabled={!form.swarmIntelligenceEnabled}
        />
        <NumberInput
          label="Peer Analysis Depth"
          value={form.swarmPeerAnalysisDepth}
          onChange={(v) => set("swarmPeerAnalysisDepth", v)}
          min={1}
          suffix="peers"
          hint="Number of peers sampled to evaluate swarm health"
          disabled={!form.swarmIntelligenceEnabled}
        />
      </SectionCard>
    </div>
  );
}
