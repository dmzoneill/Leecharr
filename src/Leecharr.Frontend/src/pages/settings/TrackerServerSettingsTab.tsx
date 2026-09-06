import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useTrackerServerConfig,
  useSaveTrackerServerConfig,
  useTrackerServerStats,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function TrackerServerSettingsTab() {
  const { t } = useTranslation();

  const { data: config, isLoading } = useTrackerServerConfig();
  const saveMutation = useSaveTrackerServerConfig();
  const { data: stats } = useTrackerServerStats();

  const [form, setForm] = useState({
    trackerServerEnabled: false,
    trackerHttpEnabled: true,
    trackerHttpPort: 6969,
    trackerUdpEnabled: true,
    trackerUdpPort: 6969,
    trackerBindAddress: t("settingsTabs.batch2.defaultIp"),
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
      setForm({
        trackerServerEnabled: config.trackerServerEnabled ?? false,
        trackerHttpEnabled: config.trackerHttpEnabled ?? true,
        trackerHttpPort: config.trackerHttpPort ?? 6969,
        trackerUdpEnabled: config.trackerUdpEnabled ?? true,
        trackerUdpPort: config.trackerUdpPort ?? 6969,
        trackerBindAddress:
          config.trackerBindAddress || t("settingsTabs.batch2.defaultIp"),
        trackerAnnounceInterval: config.trackerAnnounceInterval ?? 1800,
        trackerMaxPeersPerAnnounce: config.trackerMaxPeersPerAnnounce ?? 50,
        trackerEnableScrape: config.trackerEnableScrape ?? true,
        trackerPrivateMode: config.trackerPrivateMode ?? false,
        trackerLogAnnounces: config.trackerLogAnnounces ?? false,
        trackerRateLimitPerMinute: config.trackerRateLimitPerMinute ?? 60,
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
        trackerServerEnabled: form.trackerServerEnabled,
        trackerHttpEnabled: form.trackerHttpEnabled,
        trackerHttpPort: form.trackerHttpPort,
        trackerUdpEnabled: form.trackerUdpEnabled,
        trackerUdpPort: form.trackerUdpPort,
        trackerBindAddress: form.trackerBindAddress,
        trackerAnnounceInterval: form.trackerAnnounceInterval,
        trackerMaxPeersPerAnnounce: form.trackerMaxPeersPerAnnounce,
        trackerEnableScrape: form.trackerEnableScrape,
        trackerPrivateMode: form.trackerPrivateMode,
        trackerLogAnnounces: form.trackerLogAnnounces,
        trackerRateLimitPerMinute: form.trackerRateLimitPerMinute,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.trackerServer.loading")}
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

      {stats && (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
            gap: "0.75rem",
            marginBottom: "1.25rem",
          }}
        >
          <div
            className="card"
            style={{
              padding: "0.85rem",
              textAlign: "center",
              borderRadius: "8px",
              border: "1px solid var(--border)",
            }}
          >
            <div
              style={{
                fontSize: "0.75rem",
                color: "var(--text-muted)",
                marginBottom: "0.2rem",
              }}
            >
              {t("settingsTabs.trackerServer.trackedTorrents")}
            </div>
            <div
              style={{
                fontSize: "1.25rem",
                fontWeight: 700,
                color: "var(--accent)",
              }}
            >
              {stats.totalTorrents ?? (stats as any).activeSwarms ?? 0}
            </div>
          </div>
          <div
            className="card"
            style={{
              padding: "0.85rem",
              textAlign: "center",
              borderRadius: "8px",
              border: "1px solid var(--border)",
            }}
          >
            <div
              style={{
                fontSize: "0.75rem",
                color: "var(--text-muted)",
                marginBottom: "0.2rem",
              }}
            >
              {t("settingsTabs.trackerServer.activePeers")}
            </div>
            <div
              style={{
                fontSize: "1.25rem",
                fontWeight: 700,
                color: "var(--text-primary)",
              }}
            >
              {stats.totalPeers ?? (stats as any).activePeers ?? 0}
            </div>
          </div>
          <div
            className="card"
            style={{
              padding: "0.85rem",
              textAlign: "center",
              borderRadius: "8px",
              border: "1px solid var(--border)",
            }}
          >
            <div
              style={{
                fontSize: "0.75rem",
                color: "var(--text-muted)",
                marginBottom: "0.2rem",
              }}
            >
              {t("settingsTabs.trackerServer.totalAnnounces")}
            </div>
            <div
              style={{
                fontSize: "1.25rem",
                fontWeight: 700,
                color: "var(--text-primary)",
              }}
            >
              {stats.totalAnnounces ?? 0}
            </div>
          </div>
          <div
            className="card"
            style={{
              padding: "0.85rem",
              textAlign: "center",
              borderRadius: "8px",
              border: "1px solid var(--border)",
            }}
          >
            <div
              style={{
                fontSize: "0.75rem",
                color: "var(--text-muted)",
                marginBottom: "0.2rem",
              }}
            >
              {t("settingsTabs.trackerServer.totalScrapes")}
            </div>
            <div
              style={{
                fontSize: "1.25rem",
                fontWeight: 700,
                color: "var(--text-primary)",
              }}
            >
              {stats.totalScrapes ?? 0}
            </div>
          </div>
        </div>
      )}

      <SectionCard
        title={t("settingsTabs.trackerServer.title")}
        description={t("settingsTabs.trackerServer.description")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settingsTabs.trackerServer.enableServer")}
            checked={form.trackerServerEnabled}
            onChange={(v) => update("trackerServerEnabled", v)}
            hint={t("settingsTabs.trackerServer.enableServerHint")}
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            <TextInput
              label={t("settingsTabs.trackerServer.bindAddress")}
              value={form.trackerBindAddress}
              onChange={(v) => update("trackerBindAddress", v)}
              disabled={!form.trackerServerEnabled}
              hint={t("settingsTabs.trackerServer.bindAddressHint")}
            />

            <NumberInput
              label={t("settingsTabs.trackerServer.announceInterval")}
              value={form.trackerAnnounceInterval}
              onChange={(v) => update("trackerAnnounceInterval", v)}
              disabled={!form.trackerServerEnabled}
              min={60}
              max={7200}
              suffix={t("settingsTabs.batch2.sec")}
              hint={t("settingsTabs.trackerServer.announceIntervalHint")}
            />
          </div>

          <div
            style={{
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
                gap: "1rem",
              }}
            >
              <div>
                <Toggle
                  label={t("settingsTabs.trackerServer.enableHttp")}
                  checked={form.trackerHttpEnabled}
                  onChange={(v) => update("trackerHttpEnabled", v)}
                  disabled={!form.trackerServerEnabled}
                />
                <NumberInput
                  label={t("settingsTabs.trackerServer.httpPort")}
                  value={form.trackerHttpPort}
                  onChange={(v) => update("trackerHttpPort", v)}
                  disabled={
                    !form.trackerServerEnabled || !form.trackerHttpEnabled
                  }
                  min={1}
                  max={65535}
                  hint={t("settingsTabs.trackerServer.httpPortHint")}
                />
              </div>

              <div>
                <Toggle
                  label={t("settingsTabs.trackerServer.enableUdp")}
                  checked={form.trackerUdpEnabled}
                  onChange={(v) => update("trackerUdpEnabled", v)}
                  disabled={!form.trackerServerEnabled}
                />
                <NumberInput
                  label={t("settingsTabs.trackerServer.udpPort")}
                  value={form.trackerUdpPort}
                  onChange={(v) => update("trackerUdpPort", v)}
                  disabled={
                    !form.trackerServerEnabled || !form.trackerUdpEnabled
                  }
                  min={1}
                  max={65535}
                  hint={t("settingsTabs.trackerServer.udpPortHint")}
                />
              </div>
            </div>
          </div>

          <div
            style={{
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
                gap: "1rem",
              }}
            >
              <Toggle
                label={t("settingsTabs.trackerServer.enableScrape")}
                checked={form.trackerEnableScrape}
                onChange={(v) => update("trackerEnableScrape", v)}
                disabled={!form.trackerServerEnabled}
              />

              <Toggle
                label={t("settingsTabs.trackerServer.privateMode")}
                checked={form.trackerPrivateMode}
                onChange={(v) => update("trackerPrivateMode", v)}
                disabled={!form.trackerServerEnabled}
                hint={t("settingsTabs.trackerServer.privateModeHint")}
              />

              <Toggle
                label={t("settingsTabs.trackerServer.logAnnounces")}
                checked={form.trackerLogAnnounces}
                onChange={(v) => update("trackerLogAnnounces", v)}
                disabled={!form.trackerServerEnabled}
                hint={t("settingsTabs.trackerServer.logAnnouncesHint")}
              />

              <NumberInput
                label={t("settingsTabs.trackerServer.rateLimit")}
                value={form.trackerRateLimitPerMinute}
                onChange={(v) => update("trackerRateLimitPerMinute", v)}
                disabled={!form.trackerServerEnabled}
                min={10}
                max={1000}
                suffix={t("settingsTabs.trackerServer.reqMin")}
              />
            </div>
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default TrackerServerSettingsTab;
