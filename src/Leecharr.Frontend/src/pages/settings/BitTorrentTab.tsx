import { useState, useEffect } from "react";
import { useBitTorrentConfig, useSaveBitTorrentConfig } from "../../api/hooks";
import type { BitTorrentConfig } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  TextInput,
  NumberInput,
  SectionCard,
} from "./shared";

export function BitTorrentTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const save = useSaveBitTorrentConfig();
  const [form, setForm] = useState<BitTorrentConfig>({
    id: 1,
    enableDht: true,
    enablePex: true,
    enableLpd: true,
    encryptionMode: "enabled",
    bitTorrentUserAgent: "qBittorrent/4.4.2",
    peerIdPrefix: "-qB4420-",
    announceIntervalSeconds: 1800,
    minAnnounceIntervalSeconds: 300,
    scrapeIntervalSeconds: 900,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof BitTorrentConfig>(
    key: K,
    value: BitTorrentConfig[K],
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
        title="Protocol Features"
        description="Core BitTorrent discovery extensions, peer exchange, and encryption levels"
      >
        <Toggle
          label="DHT"
          checked={form.enableDht}
          onChange={(v) => set("enableDht", v)}
          hint="Distributed Hash Table (Mainline DHT)"
        />
        <Toggle
          label="PEX"
          checked={form.enablePex}
          onChange={(v) => set("enablePex", v)}
          hint="Peer Exchange protocol (BEP 11)"
        />
        <Toggle
          label="LPD"
          checked={form.enableLpd}
          onChange={(v) => set("enableLpd", v)}
          hint="Local Peer Discovery / Local Service Discovery (LSD)"
        />
        <SelectInput
          label="Encryption"
          value={form.encryptionMode}
          onChange={(v) => set("encryptionMode", v)}
          options={[
            { value: "disabled", label: "Disabled (Plain Only)" },
            { value: "enabled", label: "Enabled (Prefer Encrypted)" },
            { value: "forced", label: "Forced (Encrypted Only)" },
          ]}
          hint="Message Stream Encryption (MSE) / Protocol Encryption (PE)"
        />
      </SectionCard>

      <SectionCard
        title="Client Emulation & Identity"
        description="Headers and Peer ID fingerprints presented to trackers and swarms"
      >
        <TextInput
          label="User Agent"
          value={form.bitTorrentUserAgent}
          onChange={(v) => set("bitTorrentUserAgent", v)}
          hint="HTTP tracker User-Agent string"
        />
        <TextInput
          label="Peer ID Prefix"
          value={form.peerIdPrefix}
          onChange={(v) => set("peerIdPrefix", v)}
          hint="8-character Azureus-style Peer ID prefix"
        />
      </SectionCard>

      <SectionCard
        title="Tracker Timing & Scrape Intervals"
        description="Periodic announce cycles and scrape timing"
      >
        <NumberInput
          label="Announce Interval"
          value={form.announceIntervalSeconds}
          onChange={(v) => set("announceIntervalSeconds", v)}
          min={60}
          suffix="seconds"
          hint="Standard periodic announce duration"
        />
        <NumberInput
          label="Min Announce Interval"
          value={form.minAnnounceIntervalSeconds}
          onChange={(v) => set("minAnnounceIntervalSeconds", v)}
          min={30}
          suffix="seconds"
          hint="Minimum interval between back-to-back announces"
        />
        <NumberInput
          label="Scrape Interval"
          value={form.scrapeIntervalSeconds}
          onChange={(v) => set("scrapeIntervalSeconds", v)}
          min={60}
          suffix="seconds"
          hint="Periodic scrape frequency for seed/peer counts"
        />
      </SectionCard>
    </div>
  );
}
