import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function HostSettingsTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [form, setForm] = useState({
    port: 7889,
    bindAddress: "0.0.0.0",
    urlBase: "",
    autoStart: true,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        port: config.port ?? 7889,
        bindAddress: config.bindAddress ?? "0.0.0.0",
        urlBase: config.urlBase ?? "",
        autoStart: config.autoStart ?? true,
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
        port: form.port,
        bindAddress: form.bindAddress,
        urlBase: form.urlBase,
        autoStart: form.autoStart,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading host parameters...
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

      <SectionCard
        title="Web Server Runtime & Ports"
        description="Configure Kestrel HTTP web server hosting parameters and reverse proxy routing."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="HTTP Port"
            value={form.port}
            onChange={(v) => update("port", v)}
            min={1}
            max={65535}
            hint="Port for Leecharr Web UI & REST API (default: 7889)"
          />

          <TextInput
            label="Bind Address"
            value={form.bindAddress}
            onChange={(v) => update("bindAddress", v)}
            hint="IP address/interface to bind web server (0.0.0.0 or * for all interfaces)"
          />

          <TextInput
            label="URL Base (Sub-path)"
            value={form.urlBase}
            onChange={(v) => update("urlBase", v)}
            hint="Prefix for reverse proxies (e.g. /leecharr, leave empty for root /)"
          />
        </div>

        <div
          style={{
            marginTop: "1rem",
            borderTop: "1px solid var(--border-light)",
            paddingTop: "1rem",
          }}
        >
          <Toggle
            label="Auto-Resume Torrents on Startup"
            checked={form.autoStart}
            onChange={(v) => update("autoStart", v)}
            hint="Automatically restart active torrent swarms upon Leecharr daemon initialization"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default HostSettingsTab;
