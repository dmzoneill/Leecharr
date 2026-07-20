import { useState, useEffect } from "react";
import {
  useNetworkStatus,
  useNetworkConfig,
  useSaveNetworkConfig,
} from "../../api/hooks";
import type { NetworkConfig } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  TextInput,
  NumberInput,
  SectionCard,
} from "./shared";

export function NetworkTab() {
  const { data: status } = useNetworkStatus();
  const { data: config, isLoading } = useNetworkConfig();
  const save = useSaveNetworkConfig();
  const [form, setForm] = useState<NetworkConfig>({
    id: 1,
    listeningPort: 6881,
    upnpEnabled: true,
    maxGlobalConnections: 200,
    maxPerTorrentConnections: 50,
    maxUploadSlots: 4,
    proxyType: "none",
    proxyHost: "",
    proxyPort: 8080,
    proxyAuthEnabled: false,
    proxyUsername: "",
    proxyPassword: "",
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof NetworkConfig>(
    key: K,
    value: NetworkConfig[K],
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
        title="Network Status"
        description="Current local and public IP addresses detected for this Seedarr instance"
      >
        <div className="status-row">
          <span className="status-label">Local IP Address</span>
          <span className="status-value" style={{ fontFamily: "monospace" }}>
            {status?.localIp ?? "-"}
          </span>
        </div>
        <div className="status-row">
          <span className="status-label">Public / External IP Address</span>
          <span
            className="status-value"
            style={{ fontFamily: "monospace", color: "var(--accent, #c8a84e)" }}
          >
            {status?.externalIp || "-"}
          </span>
        </div>
      </SectionCard>

      <SectionCard
        title="Listening Port & UPnP"
        description="Port for incoming peer connections and router NAT-PMP/UPnP automatic traversal"
      >
        <NumberInput
          label="Listening Port"
          value={form.listeningPort}
          onChange={(v) => set("listeningPort", v)}
          min={1}
          max={65535}
          hint="Port used for incoming peer socket connections (standard 6881-6889)"
        />
        <Toggle
          label="UPnP / NAT-PMP"
          checked={form.upnpEnabled}
          onChange={(v) => set("upnpEnabled", v)}
          hint="Automatically request router port forwarding via UPnP / NAT-PMP"
        />
      </SectionCard>

      <SectionCard
        title="Connection Limits & Slots"
        description="Global socket limits and concurrent upload slot allocation"
      >
        <NumberInput
          label="Max Global Connections"
          value={form.maxGlobalConnections}
          onChange={(v) => set("maxGlobalConnections", v)}
          min={1}
          hint="Maximum concurrent peer connections across all swarms"
        />
        <NumberInput
          label="Max Per-Torrent Connections"
          value={form.maxPerTorrentConnections}
          onChange={(v) => set("maxPerTorrentConnections", v)}
          min={1}
          hint="Maximum peer connections allocated per individual torrent"
        />
        <NumberInput
          label="Max Upload Slots"
          value={form.maxUploadSlots}
          onChange={(v) => set("maxUploadSlots", v)}
          min={1}
          hint="Number of peers actively unchoked simultaneously per torrent"
        />
      </SectionCard>

      <SectionCard
        title="Proxy & Privacy Routing"
        description="Route outgoing tracker announces and peer traffic through SOCKS5/HTTP proxies"
      >
        <SelectInput
          label="Proxy Type"
          value={form.proxyType}
          onChange={(v) => set("proxyType", v)}
          options={[
            { value: "none", label: "None (Direct Connection)" },
            { value: "socks5", label: "SOCKS5 Proxy" },
            { value: "socks4", label: "SOCKS4 Proxy" },
            { value: "http", label: "HTTP Proxy" },
          ]}
          hint="Proxy protocol type"
        />
        <TextInput
          label="Proxy Host"
          value={form.proxyHost}
          onChange={(v) => set("proxyHost", v)}
          placeholder="proxy.example.com"
          disabled={form.proxyType === "none"}
          hint="Proxy server hostname or IP address"
        />
        <NumberInput
          label="Proxy Port"
          value={form.proxyPort}
          onChange={(v) => set("proxyPort", v)}
          min={1}
          max={65535}
          disabled={form.proxyType === "none"}
          hint="Proxy server port (e.g. 1080 or 8080)"
        />
        <Toggle
          label="Proxy Authentication"
          checked={form.proxyAuthEnabled}
          onChange={(v) => set("proxyAuthEnabled", v)}
          hint="Require username/password authentication for the proxy"
        />
        <TextInput
          label="Proxy Username"
          value={form.proxyUsername}
          onChange={(v) => set("proxyUsername", v)}
          disabled={!form.proxyAuthEnabled}
        />
        <TextInput
          label="Proxy Password"
          type="password"
          value={form.proxyPassword}
          onChange={(v) => set("proxyPassword", v)}
          disabled={!form.proxyAuthEnabled}
        />
      </SectionCard>
    </div>
  );
}
