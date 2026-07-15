import { useState, useEffect } from "react";
import { useProtocolsConfig, useSaveProtocolsConfig } from "../../api/hooks";
import type { ProtocolsConfig } from "../../api/types";
import { SaveBar, Toggle, NumberInput, SectionCard } from "./shared";

export function ProtocolsTab() {
  const { data: config, isLoading } = useProtocolsConfig();
  const save = useSaveProtocolsConfig();
  const [form, setForm] = useState<ProtocolsConfig>({
    id: 1,
    extensionUtMetadata: true,
    extensionUtPex: true,
    extensionLtDontHave: true,
    extensionFastExtension: true,
    utpEnabled: false,
    tcpFallback: true,
    transportConnectionTimeoutSeconds: 30,
    pexInterval: 60,
    pexMaxPeersPerMessage: 50,
    multiTrackerEnabled: true,
    multiTrackerFailoverEnabled: true,
    announceToAllTiers: false,
    announceToAllInTier: false,
    failoverMaxConsecutiveFailures: 5,
    failoverBackoffBaseSeconds: 60,
    failoverMaxBackoffSeconds: 3600,
    dhtRoutingTableSize: 160,
    dhtAnnouncementInterval: 1800,
    dhtBootstrapTimeout: 30,
    dhtQueryTimeout: 10,
    dhtMaxNodes: 1000,
    dhtBucketSize: 8,
    dhtConcurrentQueries: 3,
    dhtAutoBootstrap: true,
    dhtRateLimitEnabled: true,
    dhtMaxQueriesPerSecond: 10,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof ProtocolsConfig>(
    key: K,
    value: ProtocolsConfig[K],
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
        title="BEP Protocol Extensions"
        description="Standard BitTorrent Enhancement Proposals for metadata and exchange"
      >
        <Toggle
          label="ut_metadata"
          checked={form.extensionUtMetadata}
          onChange={(v) => set("extensionUtMetadata", v)}
          hint="Exchange metadata (.torrent files) over magnet links (BEP 9)"
        />
        <Toggle
          label="ut_pex"
          checked={form.extensionUtPex}
          onChange={(v) => set("extensionUtPex", v)}
          hint="Peer Exchange extension messages (BEP 11)"
        />
        <Toggle
          label="lt_donthave"
          checked={form.extensionLtDontHave}
          onChange={(v) => set("extensionLtDontHave", v)}
          hint="Signal uninteresting or dropped pieces to peers (lt_donthave)"
        />
        <Toggle
          label="Fast Extension"
          checked={form.extensionFastExtension}
          onChange={(v) => set("extensionFastExtension", v)}
          hint="Fast Extension allowed fast and suggest piece sets (BEP 6)"
        />
      </SectionCard>

      <SectionCard
        title="Transport Layer"
        description="Micro Transport Protocol (uTP) and TCP fallback parameters"
      >
        <Toggle
          label="uTP Protocol"
          checked={form.utpEnabled}
          onChange={(v) => set("utpEnabled", v)}
          hint="Micro Transport Protocol LEDBAT congestion control (BEP 29)"
        />
        <Toggle
          label="TCP Fallback"
          checked={form.tcpFallback}
          onChange={(v) => set("tcpFallback", v)}
          hint="Automatically fallback to standard TCP if uTP connection fails"
        />
        <NumberInput
          label="Connection Timeout"
          value={form.transportConnectionTimeoutSeconds}
          onChange={(v) => set("transportConnectionTimeoutSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Transport connection handshake timeout"
        />
      </SectionCard>

      <SectionCard
        title="Peer Exchange (PEX)"
        description="PEX message broadcast intervals and peer capacity"
      >
        <NumberInput
          label="PEX Interval"
          value={form.pexInterval}
          onChange={(v) => set("pexInterval", v)}
          min={10}
          suffix="seconds"
          hint="Interval between outgoing PEX update messages"
        />
        <NumberInput
          label="Max Peers Per Message"
          value={form.pexMaxPeersPerMessage}
          onChange={(v) => set("pexMaxPeersPerMessage", v)}
          min={1}
          hint="Maximum added/dropped peer entries per PEX message"
        />
      </SectionCard>

      <SectionCard
        title="Multi-Tracker Specification (BEP 12)"
        description="Tier-based multi-tracker announces and failover recovery"
      >
        <Toggle
          label="Multi-Tracker Enabled"
          checked={form.multiTrackerEnabled}
          onChange={(v) => set("multiTrackerEnabled", v)}
          hint="Enable multi-tracker tiered announce management (BEP 12)"
        />
        <Toggle
          label="Failover Enabled"
          checked={form.multiTrackerFailoverEnabled}
          onChange={(v) => set("multiTrackerFailoverEnabled", v)}
          hint="Automatically failover to secondary tracker tiers upon timeout"
        />
        <Toggle
          label="Announce to All Tiers"
          checked={form.announceToAllTiers}
          onChange={(v) => set("announceToAllTiers", v)}
          hint="Broadcast announces across all tiers simultaneously"
        />
        <Toggle
          label="Announce to All in Tier"
          checked={form.announceToAllInTier}
          onChange={(v) => set("announceToAllInTier", v)}
          hint="Announce to every tracker in the active tier"
        />
        <NumberInput
          label="Max Consecutive Failures"
          value={form.failoverMaxConsecutiveFailures}
          onChange={(v) => set("failoverMaxConsecutiveFailures", v)}
          min={1}
          hint="Failures before tier is marked unhealthy"
        />
        <NumberInput
          label="Backoff Base"
          value={form.failoverBackoffBaseSeconds}
          onChange={(v) => set("failoverBackoffBaseSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Base exponential backoff time"
        />
        <NumberInput
          label="Max Backoff"
          value={form.failoverMaxBackoffSeconds}
          onChange={(v) => set("failoverMaxBackoffSeconds", v)}
          min={1}
          suffix="seconds"
          hint="Maximum retry backoff cap"
        />
      </SectionCard>

      <SectionCard
        title="Distributed Hash Table (DHT)"
        description="Mainline DHT routing table size, bootstrap rules, and query rate limiting"
      >
        <Toggle
          label="Auto Bootstrap"
          checked={form.dhtAutoBootstrap}
          onChange={(v) => set("dhtAutoBootstrap", v)}
          hint="Automatically bootstrap from standard router nodes (router.bittorrent.com, etc.)"
        />
        <Toggle
          label="Rate Limiting"
          checked={form.dhtRateLimitEnabled}
          onChange={(v) => set("dhtRateLimitEnabled", v)}
          hint="Throttle outgoing DHT query packets to avoid ISP flood detection"
        />
        <NumberInput
          label="Max Queries/sec"
          value={form.dhtMaxQueriesPerSecond}
          onChange={(v) => set("dhtMaxQueriesPerSecond", v)}
          min={1}
          disabled={!form.dhtRateLimitEnabled}
        />
        <NumberInput
          label="Routing Table Size"
          value={form.dhtRoutingTableSize}
          onChange={(v) => set("dhtRoutingTableSize", v)}
          min={1}
          hint="Target Kademlia routing table node capacity"
        />
        <NumberInput
          label="Announcement Interval"
          value={form.dhtAnnouncementInterval}
          onChange={(v) => set("dhtAnnouncementInterval", v)}
          min={60}
          suffix="seconds"
          hint="Frequency to announce infohashes on the DHT"
        />
        <NumberInput
          label="Bootstrap Timeout"
          value={form.dhtBootstrapTimeout}
          onChange={(v) => set("dhtBootstrapTimeout", v)}
          min={1}
          suffix="seconds"
        />
        <NumberInput
          label="Query Timeout"
          value={form.dhtQueryTimeout}
          onChange={(v) => set("dhtQueryTimeout", v)}
          min={1}
          suffix="seconds"
        />
        <NumberInput
          label="Max Nodes"
          value={form.dhtMaxNodes}
          onChange={(v) => set("dhtMaxNodes", v)}
          min={1}
        />
        <NumberInput
          label="Bucket Size (K)"
          value={form.dhtBucketSize}
          onChange={(v) => set("dhtBucketSize", v)}
          min={1}
        />
        <NumberInput
          label="Concurrent Queries"
          value={form.dhtConcurrentQueries}
          onChange={(v) => set("dhtConcurrentQueries", v)}
          min={1}
        />
      </SectionCard>
    </div>
  );
}
