import { useState, useEffect } from "react";
import {
  usePeerProtocolConfig,
  useSavePeerProtocolConfig,
} from "../../api/hooks";
import type { PeerProtocolConfig } from "../../api/types";
import { SaveBar, NumberInput, SectionCard } from "./shared";

export function PeerProtocolTab() {
  const { data: config, isLoading } = usePeerProtocolConfig();
  const save = useSavePeerProtocolConfig();
  const [form, setForm] = useState<PeerProtocolConfig>({
    id: 1,
    handshakeTimeoutSeconds: 30,
    messageReadTimeoutSeconds: 60,
    keepAliveIntervalSeconds: 120,
    peerContactIntervalSeconds: 300,
    udpTrackerTimeoutSeconds: 5,
    httpTrackerTimeoutSeconds: 10,
    peerRequestCount: 200,
    seederUploadActivityProbability: 0.3,
    peerIdleChance: 0.3,
    peerDropoutProbability: 0.1,
    connectionRotationPercentage: 0.25,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof PeerProtocolConfig>(
    key: K,
    value: PeerProtocolConfig[K],
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
        title="Protocol Timeouts & Keepalives"
        description="Socket message read deadlines, handshake windows, and tracker request timeouts"
      >
        <NumberInput
          label="Handshake Timeout"
          value={form.handshakeTimeoutSeconds}
          onChange={(v) => set("handshakeTimeoutSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Maximum time allowed for BitTorrent handshake exchange"
        />
        <NumberInput
          label="Message Read Timeout"
          value={form.messageReadTimeoutSeconds}
          onChange={(v) => set("messageReadTimeoutSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Timeout for incoming piece/block message bytes"
        />
        <NumberInput
          label="Keep Alive Interval"
          value={form.keepAliveIntervalSeconds}
          onChange={(v) => set("keepAliveIntervalSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Interval to send 0-byte keepalive pings to active peers"
        />
        <NumberInput
          label="Peer Contact Interval"
          value={form.peerContactIntervalSeconds}
          onChange={(v) => set("peerContactIntervalSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Minimum elapsed time before re-contacting an idle peer"
        />
        <NumberInput
          label="UDP Tracker Timeout"
          value={form.udpTrackerTimeoutSeconds}
          onChange={(v) => set("udpTrackerTimeoutSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Timeout for UDP tracker connect/announce transactions"
        />
        <NumberInput
          label="HTTP Tracker Timeout"
          value={form.httpTrackerTimeoutSeconds}
          onChange={(v) => set("httpTrackerTimeoutSeconds", v)}
          min={1}
          suffix="seconds"
          hint="HTTP request timeout for tracker announces"
        />
        <NumberInput
          label="Peer Request Count"
          value={form.peerRequestCount}
          onChange={(v) => set("peerRequestCount", v)}
          min={1}
          suffix="peers"
          hint="Number of peers requested in standard announce payload (numwant)"
        />
      </SectionCard>

      <SectionCard
        title="Peer Behavior & Rotation"
        description="Probabilistic swarm models and active connection rotation"
      >
        <NumberInput
          label="Upload Activity Probability"
          value={form.seederUploadActivityProbability}
          onChange={(v) => set("seederUploadActivityProbability", v)}
          min={0}
          max={1}
          step={0.05}
          hint="Probability (0.0 - 1.0) that a connected peer actively requests upload"
        />
        <NumberInput
          label="Idle Chance"
          value={form.peerIdleChance}
          onChange={(v) => set("peerIdleChance", v)}
          min={0}
          max={1}
          step={0.05}
          hint="Probability of peer entering temporary choke / idle state"
        />
        <NumberInput
          label="Dropout Probability"
          value={form.peerDropoutProbability}
          onChange={(v) => set("peerDropoutProbability", v)}
          min={0}
          max={1}
          step={0.05}
          hint="Simulated disconnect probability for rare swarm members"
        />
        <NumberInput
          label="Connection Rotation"
          value={form.connectionRotationPercentage}
          onChange={(v) => set("connectionRotationPercentage", v)}
          min={0}
          max={1}
          step={0.05}
          hint="Percentage of oldest connections rotated periodically (optimistic unchoking)"
        />
      </SectionCard>
    </div>
  );
}
