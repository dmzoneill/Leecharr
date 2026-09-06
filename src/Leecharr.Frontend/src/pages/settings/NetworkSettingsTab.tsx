import React, { useState, useEffect } from "react";
import {
  useNetworkConfig,
  useSaveNetworkConfig,
  useNetworkStatus,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function NetworkSettingsTab() {
  const { data: config, isLoading } = useNetworkConfig();
  const saveMutation = useSaveNetworkConfig();
  const { data: netStatus } = useNetworkStatus();

  const [form, setForm] = useState({
    listeningPort: 51413,
    upnpEnabled: true,
    enableIPv6: true,
    bindInterface: "",
    enableVpnKillSwitch: false,
    maxGlobalConnections: 300,
    maxPerTorrentConnections: 50,
    maxUploadSlots: 8,
    maxConnectionsPerIp: 5,
    maximumHalfOpenConnections: 50,
    peerDscp: 4,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        listeningPort: config.listeningPort ?? 51413,
        upnpEnabled: config.upnpEnabled ?? true,
        enableIPv6: config.enableIPv6 ?? true,
        bindInterface: config.bindInterface || "",
        enableVpnKillSwitch: config.enableVpnKillSwitch ?? false,
        maxGlobalConnections: config.maxGlobalConnections ?? 300,
        maxPerTorrentConnections: config.maxPerTorrentConnections ?? 50,
        maxUploadSlots: config.maxUploadSlots ?? 8,
        maxConnectionsPerIp: config.maxConnectionsPerIp ?? 5,
        maximumHalfOpenConnections: config.maximumHalfOpenConnections ?? 50,
        peerDscp: config.peerDscp ?? 4,
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
        listeningPort: form.listeningPort,
        upnpEnabled: form.upnpEnabled,
        enableIPv6: form.enableIPv6,
        bindInterface: form.bindInterface,
        enableVpnKillSwitch: form.enableVpnKillSwitch,
        maxGlobalConnections: form.maxGlobalConnections,
        maxPerTorrentConnections: form.maxPerTorrentConnections,
        maxUploadSlots: form.maxUploadSlots,
        maxConnectionsPerIp: form.maxConnectionsPerIp,
        maximumHalfOpenConnections: form.maximumHalfOpenConnections,
        peerDscp: form.peerDscp,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading network settings...
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

      {netStatus && (
        <div
          className="card"
          style={{
            padding: "1rem",
            borderRadius: "8px",
            border: "1px solid var(--border)",
            marginBottom: "1.25rem",
            backgroundColor: "var(--bg-secondary)",
          }}
        >
          <div
            style={{
              fontSize: "0.85rem",
              fontWeight: 600,
              color: "var(--text-secondary)",
              marginBottom: "0.5rem",
            }}
          >
            Live Network Interface Status
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: "0.75rem",
              fontSize: "0.82rem",
            }}
          >
            <div>
              Local IP: <strong>{netStatus.localIp || "0.0.0.0"}</strong>
            </div>
            <div>
              Public IP:{" "}
              <strong>{netStatus.externalIp || "Not Detected"}</strong>
            </div>
            <div>
              UPnP Active:{" "}
              <strong>{netStatus.upnpAvailable ? "✓ Yes" : "✗ No"}</strong>
            </div>
            <div>
              Active Port Mappings:{" "}
              <strong>{netStatus.portMappings?.length ?? 0}</strong>
            </div>
          </div>
        </div>
      )}

      <SectionCard
        title="Incoming Peer Listening Ports & UPnP"
        description="Configure listening ports for inbound BitTorrent peer connections."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="BitTorrent Listening Port"
            value={form.listeningPort}
            onChange={(v) => update("listeningPort", v)}
            min={1}
            max={65535}
            hint="TCP & UDP port for incoming peer connections (default: 51413)"
          />

          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "0.75rem",
              justifyContent: "center",
            }}
          >
            <Toggle
              label="Enable UPnP / NAT-PMP Port Forwarding"
              checked={form.upnpEnabled}
              onChange={(v) => update("upnpEnabled", v)}
              hint="Automatically negotiate port forwarding with your router"
            />

            <Toggle
              label="Enable IPv6 Dual-Stack Swarm Listener"
              checked={form.enableIPv6}
              onChange={(v) => update("enableIPv6", v)}
              hint="Listens on both IPv4 (0.0.0.0) and IPv6 ([::]) for incoming peer handshakes"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Network Interface Binding & VPN Kill Switch"
        description="Bind BitTorrent sockets to a specific network interface (e.g. tun0, wg0) and halt traffic on disconnect."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label="Bind Network Interface"
            value={form.bindInterface}
            onChange={(v) => update("bindInterface", v)}
            hint="Interface name (e.g. tun0, wg0, eth0) or specific IP (leave blank for all)"
          />

          <Toggle
            label="Enable Automated VPN Kill Switch"
            checked={form.enableVpnKillSwitch}
            onChange={(v) => update("enableVpnKillSwitch", v)}
            hint="Immediately drop all BitTorrent transfers if the bound VPN interface goes down"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="Socket Connection Limits & Packet QoS"
        description="Tune active socket pools, half-open connection queues, and IP DSCP priority flags."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="Maximum Global Connections"
            value={form.maxGlobalConnections}
            onChange={(v) => update("maxGlobalConnections", v)}
            min={10}
            max={5000}
            hint="Total simultaneous peer socket connections"
          />

          <NumberInput
            label="Max Connections per Torrent"
            value={form.maxPerTorrentConnections}
            onChange={(v) => update("maxPerTorrentConnections", v)}
            min={1}
            max={500}
          />

          <NumberInput
            label="Max Upload Slots per Torrent"
            value={form.maxUploadSlots}
            onChange={(v) => update("maxUploadSlots", v)}
            min={1}
            max={100}
          />

          <NumberInput
            label="Max Connections per Remote IP"
            value={form.maxConnectionsPerIp}
            onChange={(v) => update("maxConnectionsPerIp", v)}
            min={1}
            max={50}
          />

          <NumberInput
            label="Max Half-Open Connections"
            value={form.maximumHalfOpenConnections}
            onChange={(v) => update("maximumHalfOpenConnections", v)}
            min={5}
            max={500}
            hint="Maximum pending TCP socket handshakes"
          />

          <NumberInput
            label="IP Packet DSCP QoS Marking"
            value={form.peerDscp}
            onChange={(v) => update("peerDscp", v)}
            min={0}
            max={63}
            hint="DiffServ code point (4 = Background CS1, 0 = Normal)"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default NetworkSettingsTab;
