import { useTranslation } from "../../i18n";
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
  const { t } = useTranslation();

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
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.proxy.loading")}
      </div>
    );
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
        title={t("settings.outboundSOCKS5HTTPProxyT")}
        description={t("settings.routeBitTorrentTrackerQueri")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label={t("settings.proxyProtocolType")}
            value={form.proxyType}
            onChange={(v) => update("proxyType", v)}
            options={[
              { value: "none", label: t("settingsTabs.proxy.typeNone") },
              { value: "socks5", label: t("settingsTabs.proxy.typeSocks5") },
              { value: "socks4", label: t("settingsTabs.proxy.typeSocks4") },
              { value: "http", label: t("settingsTabs.proxy.typeHttp") },
            ]}
          />

          <TextInput
            label={t("settings.proxyHostnameOrIP")}
            value={form.proxyHost}
            onChange={(v) => update("proxyHost", v)}
            disabled={!isProxyActive}
            hint={t("settingsTabs.proxy.hostHint")}
          />

          <NumberInput
            label={t("settings.proxyPort")}
            value={form.proxyPort}
            onChange={(v) => update("proxyPort", v)}
            disabled={!isProxyActive}
            min={1}
            max={65535}
            hint={t("settingsTabs.proxy.portHint")}
          />
        </div>

        {isProxyActive && (
          <div
            style={{
              marginTop: "1rem",
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <Toggle
              label={t("settings.enableProxyAuthentication")}
              checked={form.proxyAuthEnabled}
              onChange={(v) => update("proxyAuthEnabled", v)}
            />

            {form.proxyAuthEnabled && (
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
                  gap: "1rem",
                  marginTop: "0.75rem",
                }}
              >
                <TextInput
                  label={t("settings.proxyUsername")}
                  value={form.proxyUsername}
                  onChange={(v) => update("proxyUsername", v)}
                />

                <TextInput
                  label={t("settings.proxyPassword")}
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
        title={t("settings.privacyAnonymousRoutingPo")}
        description={t("settings.enforceStrictProxyRoutingA")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <Toggle
            label={t("settings.anonymousMode")}
            checked={form.anonymousMode}
            onChange={(v) => update("anonymousMode", v)}
            hint={t("settingsTabs.proxy.anonymousModeHint")}
          />

          <Toggle
            label={t("settings.strictProxyEnforcementKill")}
            checked={form.forceProxy}
            onChange={(v) => update("forceProxy", v)}
            disabled={!isProxyActive}
            hint={t("settingsTabs.proxy.forceProxyHint")}
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default ProxySettingsTab;
