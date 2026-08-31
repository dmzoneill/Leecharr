import { useState, useEffect } from "react";
import {
  useTrackerServerConfig,
  useSaveTrackerServerConfig,
  useTrackerServerStats,
} from "../../api/hooks";
import type { TrackerServerConfig } from "../../api/types";
import { formatUptime } from "../../utils/formatters";
import { SaveBar, Toggle, TextInput, NumberInput, SectionCard } from "./shared";

export function TrackerServerTab() {
  const { data: config, isLoading: configLoading } = useTrackerServerConfig();
  const { data: stats, isLoading: statsLoading } = useTrackerServerStats();
  const save = useSaveTrackerServerConfig();
  const [form, setForm] = useState<TrackerServerConfig>({
    id: 1,
    trackerServerEnabled: false,
    trackerHttpEnabled: true,
    trackerHttpPort: 9696,
    trackerUdpEnabled: true,
    trackerUdpPort: 6969,
    trackerBindAddress: "0.0.0.0",
    trackerAnnounceInterval: 1800,
    trackerMaxPeersPerAnnounce: 50,
    trackerEnableScrape: true,
    trackerPrivateMode: false,
    trackerLogAnnounces: false,
    trackerRateLimitPerMinute: 60,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof TrackerServerConfig>(
    key: K,
    value: TrackerServerConfig[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (configLoading)
    return <div className="loading">Loading configuration...</div>;

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
        title="Tracker Server Status & Live Metrics"
        description="Operational metrics for the inbuilt BitTorrent tracker daemon"
      >
        {statsLoading ? (
          <div className="loading">Loading stats...</div>
        ) : stats ? (
          <>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
                gap: "1rem",
                marginBottom: "1rem",
              }}
            >
              <div
                className="stat-card"
                style={{
                  padding: "1rem",
                  borderRadius: "8px",
                  border: "1px solid rgba(255, 255, 255, 0.08)",
                  boxShadow: "0 4px 14px rgba(0, 0, 0, 0.2)",
                  background: "var(--bg-secondary)",
                  textAlign: "center",
                }}
              >
                <div
                  className="stat-value"
                  style={{
                    fontSize: "1.5rem",
                    fontWeight: 700,
                    color: "var(--accent, #c8a84e)",
                  }}
                >
                  {stats.totalTorrents.toLocaleString()}
                </div>
                <div
                  className="stat-label"
                  style={{
                    fontSize: "0.75rem",
                    color: "var(--text-muted)",
                    textTransform: "uppercase",
                  }}
                >
                  Torrents
                </div>
              </div>
              <div
                className="stat-card"
                style={{
                  padding: "1rem",
                  borderRadius: "8px",
                  border: "1px solid rgba(255, 255, 255, 0.08)",
                  boxShadow: "0 4px 14px rgba(0, 0, 0, 0.2)",
                  background: "var(--bg-secondary)",
                  textAlign: "center",
                }}
              >
                <div
                  className="stat-value"
                  style={{
                    fontSize: "1.5rem",
                    fontWeight: 700,
                    color: "var(--color-primary, #3498db)",
                  }}
                >
                  {stats.totalPeers.toLocaleString()}
                </div>
                <div
                  className="stat-label"
                  style={{
                    fontSize: "0.75rem",
                    color: "var(--text-muted)",
                    textTransform: "uppercase",
                  }}
                >
                  Peers
                </div>
              </div>
              <div
                className="stat-card"
                style={{
                  padding: "1rem",
                  borderRadius: "8px",
                  border: "1px solid rgba(255, 255, 255, 0.08)",
                  boxShadow: "0 4px 14px rgba(0, 0, 0, 0.2)",
                  background: "var(--bg-secondary)",
                  textAlign: "center",
                }}
              >
                <div
                  className="stat-value"
                  style={{
                    fontSize: "1.5rem",
                    fontWeight: 700,
                    color: "var(--color-success, #2ecc71)",
                  }}
                >
                  {stats.totalAnnounces.toLocaleString()}
                </div>
                <div
                  className="stat-label"
                  style={{
                    fontSize: "0.75rem",
                    color: "var(--text-muted)",
                    textTransform: "uppercase",
                  }}
                >
                  Announces
                </div>
              </div>
              <div
                className="stat-card"
                style={{
                  padding: "1rem",
                  borderRadius: "8px",
                  border: "1px solid rgba(255, 255, 255, 0.08)",
                  boxShadow: "0 4px 14px rgba(0, 0, 0, 0.2)",
                  background: "var(--bg-secondary)",
                  textAlign: "center",
                }}
              >
                <div
                  className="stat-value"
                  style={{
                    fontSize: "1.5rem",
                    fontWeight: 700,
                    color: "#9b59b6",
                  }}
                >
                  {stats.totalScrapes.toLocaleString()}
                </div>
                <div
                  className="stat-label"
                  style={{
                    fontSize: "0.75rem",
                    color: "var(--text-muted)",
                    textTransform: "uppercase",
                  }}
                >
                  Scrapes
                </div>
              </div>
            </div>
            <div
              className="status-row"
              style={{
                display: "flex",
                justifyContent: "space-between",
                padding: "0.5rem 0",
                borderTop: "1px solid var(--border-light)",
              }}
            >
              <span
                className="status-label"
                style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}
              >
                Tracker Uptime
              </span>
              <span
                className="status-value"
                style={{
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  color: "var(--text-primary)",
                }}
              >
                {formatUptime(stats.uptime)}
              </span>
            </div>
          </>
        ) : (
          <div className="loading" style={{ margin: 0 }}>
            No stats available
          </div>
        )}
      </SectionCard>

      <SectionCard
        title="Server Daemon & Network Binding"
        description="Enable/disable the inbuilt tracker and specify listening IP interface"
      >
        <Toggle
          label="Tracker Daemon Enabled"
          checked={form.trackerServerEnabled}
          onChange={(v) => set("trackerServerEnabled", v)}
          hint="Enable Seedarr inbuilt HTTP/UDP BitTorrent tracker"
        />
        <TextInput
          label="Bind Address"
          value={form.trackerBindAddress}
          onChange={(v) => set("trackerBindAddress", v)}
          placeholder="0.0.0.0"
          disabled={!form.trackerServerEnabled}
          hint="Interface IP address to bind (* or 0.0.0.0 for all interfaces)"
        />
      </SectionCard>

      <SectionCard
        title="HTTP Tracker Protocol"
        description="HTTP GET /announce and /scrape endpoint parameters"
      >
        <Toggle
          label="HTTP Tracker"
          checked={form.trackerHttpEnabled}
          onChange={(v) => set("trackerHttpEnabled", v)}
          hint="Enable HTTP GET /announce endpoint"
        />
        <NumberInput
          label="HTTP Port"
          value={form.trackerHttpPort}
          onChange={(v) => set("trackerHttpPort", v)}
          min={1}
          max={65535}
          disabled={!form.trackerHttpEnabled}
          hint="Port for HTTP announces (e.g. 9696)"
        />
      </SectionCard>

      <SectionCard
        title="UDP Tracker Protocol"
        description="Binary UDP BitTorrent tracker protocol (BEP 15) endpoint parameters"
      >
        <Toggle
          label="UDP Tracker"
          checked={form.trackerUdpEnabled}
          onChange={(v) => set("trackerUdpEnabled", v)}
          hint="Enable BEP 15 UDP binary tracker endpoint"
        />
        <NumberInput
          label="UDP Port"
          value={form.trackerUdpPort}
          onChange={(v) => set("trackerUdpPort", v)}
          min={1}
          max={65535}
          disabled={!form.trackerUdpEnabled}
          hint="UDP port (e.g. 6969)"
        />
      </SectionCard>

      <SectionCard
        title="Tracker Rules & Security"
        description="Announce interval intervals, rate throttling, and private tracker mode"
      >
        <NumberInput
          label="Announce Interval"
          value={form.trackerAnnounceInterval}
          onChange={(v) => set("trackerAnnounceInterval", v)}
          min={60}
          suffix="seconds"
          hint="Standard announce interval returned to connecting peers"
        />
        <NumberInput
          label="Max Peers Per Announce"
          value={form.trackerMaxPeersPerAnnounce}
          onChange={(v) => set("trackerMaxPeersPerAnnounce", v)}
          min={1}
          hint="Maximum peer IPs returned per announce response"
        />
        <Toggle
          label="Enable Scrape"
          checked={form.trackerEnableScrape}
          onChange={(v) => set("trackerEnableScrape", v)}
          hint="Allow peers to query /scrape for seeder/leecher counts"
        />
        <Toggle
          label="Private Mode"
          checked={form.trackerPrivateMode}
          onChange={(v) => set("trackerPrivateMode", v)}
          hint="Only track pre-registered infohashes; reject unregistered torrents"
        />
        <Toggle
          label="Log Announces"
          checked={form.trackerLogAnnounces}
          onChange={(v) => set("trackerLogAnnounces", v)}
          hint="Write verbose announce transactions to tracker log file"
        />
        <NumberInput
          label="Rate Limit"
          value={form.trackerRateLimitPerMinute}
          onChange={(v) => set("trackerRateLimitPerMinute", v)}
          min={1}
          suffix="/ min"
          hint="Maximum announces permitted per IP per minute"
        />
      </SectionCard>
    </div>
  );
}
