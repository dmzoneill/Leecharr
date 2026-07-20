import { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import type { GeneralConfig } from "../../api/types";
import { SaveBar, Toggle, TextInput, NumberInput, SectionCard } from "./shared";

export function WebUITab() {
  const { data: config, isLoading } = useGeneralConfig();
  const save = useSaveGeneralConfig();
  const [form, setForm] = useState<GeneralConfig>({
    id: 1,
    autoStart: false,
    themeStyle: "system",
    colorScheme: "auto",
    watchFolderEnabled: false,
    watchFolderPath: "",
    watchFolderScanIntervalSeconds: 10,
    watchFolderAutoStartTorrents: true,
    watchFolderDeleteAddedTorrents: false,
    port: 9898,
    bindAddress: "0.0.0.0",
    urlBase: "",
    authenticationEnabled: false,
    apiKey: "",
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof GeneralConfig>(
    key: K,
    value: GeneralConfig[K],
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
        title="Web Interface Connection"
        description="Configure HTTP port and network interface bindings for the Web UI"
      >
        <NumberInput
          label="Port"
          value={form.port}
          onChange={(v) => set("port", v)}
          min={1}
          max={65535}
          hint="HTTP port to access Seedarr Web UI"
        />
        <TextInput
          label="Bind Address"
          value={form.bindAddress}
          onChange={(v) => set("bindAddress", v)}
          placeholder="0.0.0.0"
          hint="Network address interface to bind (* or 0.0.0.0 for all)"
        />
      </SectionCard>

      <SectionCard
        title="Session Security & Authentication"
        description="Control access permissions and credential requirements"
      >
        <Toggle
          label="Authentication Enabled"
          checked={form.authenticationEnabled}
          onChange={(v) => set("authenticationEnabled", v)}
          hint="Require user login credentials for Web UI access"
        />
      </SectionCard>
    </div>
  );
}
