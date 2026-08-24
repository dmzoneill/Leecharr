import React, { useState, useEffect } from "react";
import { useNetworkConfig, useSaveNetworkConfig } from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  TextInput,
  SelectInput,
  Toggle,
} from "./shared";

export function ProxySettingsTab() {
  const { data: config, isLoading } = useNetworkConfig();
  const saveMutation = useSaveNetworkConfig();

  const [form, setForm] = useState({
    proxyType: "none",
    proxyHost: "",
    proxyPort: 8080,
    proxyAuthEnabled: false,
    proxyUsername: "",
    proxyPassword: "",
    anonymousMode: false,
    forceProxy: false,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        proxyType: config.proxyType || "none",
        proxyHost: config.proxyHost || "",
        proxyPort: config.proxyPort ?? 8080,
        proxyAuthEnabled: config.proxyAuthEnabled ?? false,
        proxyUsername: config.proxyUsername || "",
        proxyPassword: config.proxyPassword || "",
        anonymousMode: config.anonymousMode ?? false,
        forceProxy: config.forceProxy ?? false,
      });
      setDirty(false);
    }
  }, [config]);

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        proxyType: form.proxyType,
        proxyHost: form.proxyHost,
        proxyPort: form.proxyPort,
        proxyAuthEnabled: form.proxyAuthEnabled,
        proxyUsername: form.proxyUsername,
        proxyPassword: form.proxyPassword,
        anonymousMode: form.anonymousMode,
        forceProxy: form.forceProxy,
      },
      {
        onSuccess: () => setDirty(false),
      }
    );
  };

  if (isLoading) {
    return <div className="loading" style={{ padding: "2rem" }}>Loading proxy settings...</div>;
  }

  const isProxyActive = form.proxyType !== "none";

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

      <SectionCard
        title="Outbound SOCKS5 / HTTP Proxy Tunnel"
        description="Route BitTorrent tracker queries and peer data transfers through secure proxy tunnels."
      >
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem" }}>
          <SelectInput
            label="Proxy Protocol Type"
            value={form.proxyType}
            onChange={(v) => update("proxyType", v)}
            options={[
              { value: "none", label: "None / Direct Connection" },
              { value: "socks5", label: "SOCKS5 Proxy (Recommended)" },
              { value: "socks4", label: "SOCKS4 Proxy" },
              { value: "http", label: "HTTP / HTTPS Proxy" },
            ]}
          />

          <TextInput
            label="Proxy Hostname or IP"
            value={form.proxyHost}
            onChange={(v) => update("proxyHost", v)}
            disabled={!isProxyActive}
            hint="e.g. 127.0.0.1 or proxy.vpnprovider.com"
          />

          <NumberInput
            label="Proxy Port"
            value={form.proxyPort}
            onChange={(v) => update("proxyPort", v)}
            disabled={!isProxyActive}
            min={1}
            max={65535}
            hint="Standard SOCKS5 port: 1080"
          />
        </div>

        {isProxyActive && (
          <div style={{ marginTop: "1rem", borderTop: "1px solid var(--border-light)", paddingTop: "1rem" }}>
            <Toggle
              label="Enable Proxy Authentication"
              checked={form.proxyAuthEnabled}
              onChange={(v) => update("proxyAuthEnabled", v)}
            />

            {form.proxyAuthEnabled && (
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: "1rem", marginTop: "0.75rem" }}>
                <TextInput
                  label="Proxy Username"
                  value={form.proxyUsername}
                  onChange={(v) => update("proxyUsername", v)}
                />

                <TextInput
                  label="Proxy Password"
                  value={form.proxyPassword}
                  onChange={(v) => update("proxyPassword", v)}
                  type="password"
                />
              </div>
            )}
          </div>
        )}
      </SectionCard>

      <SectionCard
        title="Privacy & Anonymous Routing Policy"
        description="Enforce strict proxy routing and suppress client identification headers."
      >
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem" }}>
          <Toggle
            label="Anonymous Mode"
            checked={form.anonymousMode}
            onChange={(v) => update("anonymousMode", v)}
            hint="Strips user-agent strings, suppresses client identification headers, and hides listening ports"
          />

          <Toggle
            label="Strict Proxy Enforcement (Kill on Failure)"
            checked={form.forceProxy}
            onChange={(v) => update("forceProxy", v)}
            disabled={!isProxyActive}
            hint="Completely abort network transfers if the proxy server becomes unreachable"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default ProxySettingsTab;
