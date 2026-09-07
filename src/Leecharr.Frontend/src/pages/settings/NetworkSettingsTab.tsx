import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useNetworkConfig,
  useSaveNetworkConfig,
  useNetworkStatus,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function NetworkSettingsTab() {
  const { t } = useTranslation();

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
        {t("settingsTabs.batch2.loadingNetworkSettings")}
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
            {t("settingsTabs.batch2.liveNetworkInterfaceStatus")}
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
              {t("settingsTabs.batch2.localIp")}:{" "}
              <strong>
                {netStatus.localIp || t("settingsTabs.batch2.defaultIp")}
              </strong>
            </div>
            <div>
              {t("settingsTabs.batch2.publicIp")}:{" "}
              <strong>
                {netStatus.externalIp || t("settingsTabs.batch2.notDetected")}
              </strong>
            </div>
            <div>
              {t("settingsTabs.batch2.upnpActive")}:{" "}
              <strong>
                {netStatus.upnpAvailable
                  ? t("settingsTabs.batch2.yes")
                  : t("settingsTabs.batch2.no")}
              </strong>
            </div>
            <div>
              {t("settingsTabs.batch2.activePortMappings")}:{" "}
              <strong>{netStatus.portMappings?.length ?? 0}</strong>
            </div>
          </div>
        </div>
      )}

      <SectionCard
        title={t("settingsTabs.batch2.incomingPeerListeningPorts")}
        description={t("settingsTabs.batch2.configureListeningPorts")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.batch2.bitTorrentListeningPort")}
            value={form.listeningPort}
            onChange={(v) => update("listeningPort", v)}
            min={1}
            max={65535}
            hint={t("settingsTabs.batch2.tcpUdpPort")}
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
              label={t("settingsTabs.batch2.enableUpnpNatPmp")}
              checked={form.upnpEnabled}
              onChange={(v) => update("upnpEnabled", v)}
              hint={t(
                "settingsTabs.batch2.automaticallyNegotiatePortForwarding",
              )}
            />

            <Toggle
              label={t("settingsTabs.batch2.enableIpv6DualStack")}
              checked={form.enableIPv6}
              onChange={(v) => update("enableIPv6", v)}
              hint={t("settingsTabs.batch2.listensOnBothIpv4AndIpv6")}
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.batch2.networkInterfaceBinding")}
        description={t("settingsTabs.batch2.bindBitTorrentSockets")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label={t("settingsTabs.batch2.bindNetworkInterface")}
            value={form.bindInterface}
            onChange={(v) => update("bindInterface", v)}
            hint={t("settingsTabs.batch2.interfaceNameOrIp")}
          />

          <Toggle
            label={t("settingsTabs.batch2.enableAutomatedVpnKillSwitch")}
            checked={form.enableVpnKillSwitch}
            onChange={(v) => update("enableVpnKillSwitch", v)}
            hint={t(
              "settingsTabs.batch2.immediatelyDropAllBitTorrentTransfers",
            )}
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.batch2.socketConnectionLimits")}
        description={t("settingsTabs.batch2.tuneActiveSocketPools")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.batch2.maximumGlobalConnections")}
            value={form.maxGlobalConnections}
            onChange={(v) => update("maxGlobalConnections", v)}
            min={10}
            max={5000}
            hint={t(
              "settingsTabs.batch2.totalSimultaneousPeerSocketConnections",
            )}
          />

          <NumberInput
            label={t("settingsTabs.batch2.maxConnectionsPerTorrent")}
            value={form.maxPerTorrentConnections}
            onChange={(v) => update("maxPerTorrentConnections", v)}
            min={1}
            max={500}
          />

          <NumberInput
            label={t("settingsTabs.batch2.maxUploadSlotsPerTorrent")}
            value={form.maxUploadSlots}
            onChange={(v) => update("maxUploadSlots", v)}
            min={1}
            max={100}
          />

          <NumberInput
            label={t("settingsTabs.batch2.maxConnectionsPerRemoteIp")}
            value={form.maxConnectionsPerIp}
            onChange={(v) => update("maxConnectionsPerIp", v)}
            min={1}
            max={50}
          />

          <NumberInput
            label={t("settingsTabs.batch2.maxHalfOpenConnections")}
            value={form.maximumHalfOpenConnections}
            onChange={(v) => update("maximumHalfOpenConnections", v)}
            min={5}
            max={500}
            hint={t("settingsTabs.batch2.maximumPendingTcpSocketHandshakes")}
          />

          <NumberInput
            label={t("settingsTabs.batch2.ipPacketDscpQosMarking")}
            value={form.peerDscp}
            onChange={(v) => update("peerDscp", v)}
            min={0}
            max={63}
            hint={t("settingsTabs.batch2.diffServCodePoint")}
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default NetworkSettingsTab;
