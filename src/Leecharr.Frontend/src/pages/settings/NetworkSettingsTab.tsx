import { useTranslation } from "../../i18n";
import { useState, useEffect } from "react";
import {
  useNetworkConfig,
  useSaveNetworkConfig,
  useNetworkStatus,
  useNetworkInterfaces,
  useTestPort,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, Toggle } from "./shared";
import { trackNetworkConfigSave } from "../../utils/analytics";
import type { PortTestResult } from "../../api/types";

export function NetworkSettingsTab() {
  const { t } = useTranslation();

  const { data: config, isLoading } = useNetworkConfig();
  const saveMutation = useSaveNetworkConfig();
  const { data: netStatus } = useNetworkStatus();
  const { data: interfaces } = useNetworkInterfaces();
  const testPortMutation = useTestPort();

  const [portTestResult, setPortTestResult] = useState<PortTestResult | null>(null);
  const [manualInterface, setManualInterface] = useState(false);

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
      const initialBindInterface =
        config.bindInterface || config.networkInterfaceBinding || "";
      setForm({
        listeningPort: config.listeningPort ?? 51413,
        upnpEnabled: config.upnpEnabled ?? true,
        enableIPv6: config.enableIPv6 ?? true,
        bindInterface: initialBindInterface,
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

  useEffect(() => {
    if (
      interfaces &&
      form.bindInterface &&
      !interfaces.includes(form.bindInterface)
    ) {
      setManualInterface(true);
    }
  }, [interfaces, form.bindInterface]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    trackNetworkConfigSave({
      upnp_enabled: form.upnpEnabled,
      has_vpn_interface: Boolean(
        form.bindInterface && form.bindInterface !== "",
      ),
    });
    saveMutation.mutate(
      {
        ...config,
        listeningPort: form.listeningPort,
        upnpEnabled: form.upnpEnabled,
        enableIPv6: form.enableIPv6,
        bindInterface: form.bindInterface,
        networkInterfaceBinding: form.bindInterface,
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

  const handleTestPort = () => {
    setPortTestResult(null);
    testPortMutation.mutate(
      { port: form.listeningPort },
      {
        onSuccess: (data) => setPortTestResult(data),
        onError: (err) =>
          setPortTestResult({
            port: form.listeningPort,
            isOpen: false,
            message:
              err.message ||
              t("settingsTabs.batch2.portCheckFailed", "Port check failed"),
          }),
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
          <div style={{ display: "flex", flexDirection: "column" }}>
            <div style={{ display: "flex", gap: "0.5rem", alignItems: "flex-start" }}>
              <div style={{ flex: 1 }}>
                <NumberInput
                  label={t("settingsTabs.batch2.bitTorrentListeningPort")}
                  value={form.listeningPort}
                  onChange={(v) => {
                    update("listeningPort", v);
                    setPortTestResult(null);
                  }}
                  min={1}
                  max={65535}
                  hint={t("settingsTabs.batch2.tcpUdpPort")}
                />
              </div>
              <div className="form-group" style={{ display: "flex", flexDirection: "column" }}>
                <label
                  htmlFor="test-port-button"
                  className="form-label"
                  style={{ visibility: "hidden" }}
                  aria-label={t("settingsTabs.batch2.testPort", "Test Port")}
                >
                  Test
                </label>
                <button
                  id="test-port-button"
                  type="button"
                  className="btn btn-outline"
                  onClick={handleTestPort}
                  disabled={testPortMutation.isPending || !form.listeningPort}
                  style={{ height: "38px", whiteSpace: "nowrap" }}
                >
                  {testPortMutation.isPending
                    ? t("settingsTabs.batch2.testingPort", "Testing...")
                    : t("settingsTabs.batch2.testPort", "Test Port")}
                </button>
              </div>
            </div>
            {portTestResult && (
              <div
                style={{
                  marginTop: "0.25rem",
                  padding: "0.5rem 0.75rem",
                  borderRadius: "6px",
                  fontSize: "0.85rem",
                  backgroundColor: portTestResult.isOpen
                    ? "rgba(46, 204, 113, 0.1)"
                    : "rgba(231, 76, 60, 0.1)",
                  border: portTestResult.isOpen
                    ? "1px solid rgba(46, 204, 113, 0.3)"
                    : "1px solid rgba(231, 76, 60, 0.3)",
                  color: portTestResult.isOpen ? "#2ecc71" : "#e74c3c",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                }}
              >
                <span>{portTestResult.isOpen ? "✓" : "✕"}</span>
                <span>{portTestResult.message}</span>
              </div>
            )}
          </div>

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
          <div>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "0.5rem",
              }}
            >
              <label className="form-label" style={{ marginBottom: 0 }}>
                {t("settingsTabs.batch2.bindNetworkInterface")}
              </label>
              <button
                type="button"
                className="btn btn-xs btn-outline"
                onClick={() => setManualInterface(!manualInterface)}
                style={{
                  fontSize: "0.75rem",
                  padding: "0.2rem 0.5rem",
                  cursor: "pointer",
                }}
              >
                {manualInterface
                  ? t(
                      "settingsTabs.batch2.selectFromDetected",
                      "Select from detected interfaces",
                    )
                  : t("settingsTabs.batch2.enterManually", "Enter manually")}
              </button>
            </div>

            {manualInterface || !interfaces || interfaces.length === 0 ? (
              <div className="form-group" style={{ marginBottom: 0 }}>
                <div className="form-input-wrapper">
                  <input
                    type="text"
                    className="form-input"
                    value={form.bindInterface}
                    onChange={(e) => update("bindInterface", e.target.value)}
                    placeholder="e.g. tun0, wg0, eth0"
                    style={{ borderRadius: "6px" }}
                  />
                  <span className="form-hint">
                    {t("settingsTabs.batch2.interfaceNameOrIp")}
                  </span>
                </div>
              </div>
            ) : (
              <div className="form-group" style={{ marginBottom: 0 }}>
                <div className="form-input-wrapper">
                  <select
                    className="form-select"
                    value={
                      interfaces.includes(form.bindInterface)
                        ? form.bindInterface
                        : form.bindInterface
                          ? "__custom__"
                          : ""
                    }
                    onChange={(e) => {
                      if (
                        e.target.value === "__manual__" ||
                        e.target.value === "__custom__"
                      ) {
                        setManualInterface(true);
                      } else {
                        update("bindInterface", e.target.value);
                      }
                    }}
                    style={{ borderRadius: "6px" }}
                  >
                    <option value="">
                      {t(
                        "settingsTabs.batch2.allInterfaces",
                        "All / Any interfaces (Default)",
                      )}
                    </option>
                    {interfaces.map((iface) => (
                      <option key={iface} value={iface}>
                        {iface}
                      </option>
                    ))}
                    {form.bindInterface &&
                      !interfaces.includes(form.bindInterface) && (
                        <option value="__custom__">
                          {form.bindInterface} (Custom)
                        </option>
                      )}
                    <option value="__manual__">
                      {t(
                        "settingsTabs.batch2.manualEntry",
                        "Custom / Manual entry...",
                      )}
                    </option>
                  </select>
                  <span className="form-hint">
                    {t("settingsTabs.batch2.interfaceNameOrIp")}
                  </span>
                </div>
              </div>
            )}
          </div>

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
