import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useProtocolsConfig,
  useSaveProtocolsConfig,
  usePeerProtocolConfig,
  useSavePeerProtocolConfig,
} from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  SelectInput,
  Toggle,
} from "./shared";

export function ProtocolsSettingsTab() {
  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();

  const { data: protoConfig, isLoading: protoLoading } = useProtocolsConfig();
  const saveProtoMutation = useSaveProtocolsConfig();

  const { data: peerConfig, isLoading: peerLoading } = usePeerProtocolConfig();
  const savePeerMutation = useSavePeerProtocolConfig();

  const [form, setForm] = useState({
    encryptionMode: "preferEncrypted",
    extensionUtMetadata: true,
    extensionUtPex: true,
    extensionLtDontHave: true,
    extensionFastExtension: true,
    utpEnabled: true,
    tcpFallback: true,
    transportConnectionTimeoutSeconds: 30,
    handshakeTimeoutSeconds: 30,
    messageReadTimeoutSeconds: 60,
    keepAliveIntervalSeconds: 120,
    peerContactIntervalSeconds: 30,
    multiTrackerEnabled: true,
    multiTrackerFailoverEnabled: true,
    announceToAllTiers: true,
    announceToAllInTier: false,
    failoverMaxConsecutiveFailures: 3,
    failoverBackoffBaseSeconds: 30,
    failoverMaxBackoffSeconds: 1800,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (btConfig || protoConfig || peerConfig) {
      setForm({
        encryptionMode: btConfig?.encryptionMode || "preferEncrypted",
        extensionUtMetadata: protoConfig?.extensionUtMetadata ?? true,
        extensionUtPex: protoConfig?.extensionUtPex ?? true,
        extensionLtDontHave: protoConfig?.extensionLtDontHave ?? true,
        extensionFastExtension: protoConfig?.extensionFastExtension ?? true,
        utpEnabled: protoConfig?.utpEnabled ?? true,
        tcpFallback: protoConfig?.tcpFallback ?? true,
        transportConnectionTimeoutSeconds: protoConfig?.transportConnectionTimeoutSeconds ?? 30,
        handshakeTimeoutSeconds: peerConfig?.handshakeTimeoutSeconds ?? 30,
        messageReadTimeoutSeconds: peerConfig?.messageReadTimeoutSeconds ?? 60,
        keepAliveIntervalSeconds: peerConfig?.keepAliveIntervalSeconds ?? 120,
        peerContactIntervalSeconds: peerConfig?.peerContactIntervalSeconds ?? 30,
        multiTrackerEnabled: protoConfig?.multiTrackerEnabled ?? true,
        multiTrackerFailoverEnabled: protoConfig?.multiTrackerFailoverEnabled ?? true,
        announceToAllTiers: protoConfig?.announceToAllTiers ?? true,
        announceToAllInTier: protoConfig?.announceToAllInTier ?? false,
        failoverMaxConsecutiveFailures: protoConfig?.failoverMaxConsecutiveFailures ?? 3,
        failoverBackoffBaseSeconds: protoConfig?.failoverBackoffBaseSeconds ?? 30,
        failoverMaxBackoffSeconds: protoConfig?.failoverMaxBackoffSeconds ?? 1800,
      });
      setDirty(false);
    }
  }, [btConfig, protoConfig, peerConfig]);

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const isPending = saveBtMutation.isPending || saveProtoMutation.isPending || savePeerMutation.isPending;
  const isError = saveBtMutation.isError || saveProtoMutation.isError || savePeerMutation.isError;
  const isSuccess = saveBtMutation.isSuccess && saveProtoMutation.isSuccess && savePeerMutation.isSuccess;
  const error = (saveBtMutation.error || saveProtoMutation.error || savePeerMutation.error) as Error | null;

  const handleSave = () => {
    if (btConfig) {
      saveBtMutation.mutate({
        ...btConfig,
        encryptionMode: form.encryptionMode,
      });
    }
    if (protoConfig) {
      saveProtoMutation.mutate({
        ...protoConfig,
        extensionUtMetadata: form.extensionUtMetadata,
        extensionUtPex: form.extensionUtPex,
        extensionLtDontHave: form.extensionLtDontHave,
        extensionFastExtension: form.extensionFastExtension,
        utpEnabled: form.utpEnabled,
        tcpFallback: form.tcpFallback,
        transportConnectionTimeoutSeconds: form.transportConnectionTimeoutSeconds,
        multiTrackerEnabled: form.multiTrackerEnabled,
        multiTrackerFailoverEnabled: form.multiTrackerFailoverEnabled,
        announceToAllTiers: form.announceToAllTiers,
        announceToAllInTier: form.announceToAllInTier,
        failoverMaxConsecutiveFailures: form.failoverMaxConsecutiveFailures,
        failoverBackoffBaseSeconds: form.failoverBackoffBaseSeconds,
        failoverMaxBackoffSeconds: form.failoverMaxBackoffSeconds,
      });
    }
    if (peerConfig) {
      savePeerMutation.mutate({
        ...peerConfig,
        handshakeTimeoutSeconds: form.handshakeTimeoutSeconds,
        messageReadTimeoutSeconds: form.messageReadTimeoutSeconds,
        keepAliveIntervalSeconds: form.keepAliveIntervalSeconds,
        peerContactIntervalSeconds: form.peerContactIntervalSeconds,
      });
    }
    setDirty(false);
  };

  if (btLoading || protoLoading || peerLoading) {
    return <div className="loading" style={{ padding: "2rem" }}>Loading protocol settings...</div>;
  }

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={isPending}
        isError={isError}
        isSuccess={isSuccess}
        error={error}
        onSave={handleSave}
      />

      <SectionCard
        title="Protocol Encryption & Transport Standards"
        description="Configure BitTorrent Enhancement Proposals (BEPs), MSE/PE encryption, and uTP transport."
      >
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem" }}>
          <SelectInput
            label="Protocol Encryption (MSE/PE)"
            value={form.encryptionMode}
            onChange={(v) => update("encryptionMode", v)}
            options={[
              { value: "preferEncrypted", label: "Prefer Encryption (Compatible & Secure)" },
              { value: "forceEncrypted", label: "Require Encryption (Strict, Drops Plaintext)" },
              { value: "allowPlaintext", label: "Allow Plaintext & Encryption" },
              { value: "disabled", label: "Disable Encryption" },
            ]}
          />

          <NumberInput
            label="Transport Connection Timeout"
            value={form.transportConnectionTimeoutSeconds}
            onChange={(v) => update("transportConnectionTimeoutSeconds", v)}
            min={5}
            max={120}
            suffix="sec"
            hint="Timeout for establishing TCP/uTP socket connections"
          />
        </div>

        <div style={{ marginTop: "1rem", borderTop: "1px solid var(--border-light)", paddingTop: "1rem" }}>
          <div style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--text-secondary)", marginBottom: "0.75rem" }}>
            BitTorrent Enhancement Proposals (BEP Suite)
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: "0.75rem" }}>
            <Toggle
              label="ut_metadata (BEP 9)"
              checked={form.extensionUtMetadata}
              onChange={(v) => update("extensionUtMetadata", v)}
              hint="Magnet link metadata exchange"
            />
            <Toggle
              label="ut_pex (BEP 11)"
              checked={form.extensionUtPex}
              onChange={(v) => update("extensionUtPex", v)}
              hint="uTorrent Peer Exchange"
            />
            <Toggle
              label="lt_donthave (BEP 54)"
              checked={form.extensionLtDontHave}
              onChange={(v) => update("extensionLtDontHave", v)}
              hint="Prune unwanted piece messages"
            />
            <Toggle
              label="Fast Extension (BEP 6)"
              checked={form.extensionFastExtension}
              onChange={(v) => update("extensionFastExtension", v)}
              hint="Allowed Fast & Suggest Pieces"
            />
            <Toggle
              label="uTP LEDBAT (BEP 29)"
              checked={form.utpEnabled}
              onChange={(v) => update("utpEnabled", v)}
              hint="Micro Transport Protocol over UDP"
            />
            <Toggle
              label="TCP Fallback"
              checked={form.tcpFallback}
              onChange={(v) => update("tcpFallback", v)}
              hint="Fall back to TCP on uTP timeout"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Protocol Timeouts & Keepalive Cadence"
        description="Fine-tune network socket read/write deadlines and keepalive intervals."
      >
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem" }}>
          <NumberInput
            label="Handshake Timeout"
            value={form.handshakeTimeoutSeconds}
            onChange={(v) => update("handshakeTimeoutSeconds", v)}
            min={5}
            max={120}
            suffix="sec"
            hint="Maximum duration for wire handshake"
          />

          <NumberInput
            label="Message Read Timeout"
            value={form.messageReadTimeoutSeconds}
            onChange={(v) => update("messageReadTimeoutSeconds", v)}
            min={10}
            max={300}
            suffix="sec"
            hint="Deadline for reading incoming protocol frames"
          />

          <NumberInput
            label="Keepalive Interval"
            value={form.keepAliveIntervalSeconds}
            onChange={(v) => update("keepAliveIntervalSeconds", v)}
            min={30}
            max={600}
            suffix="sec"
            hint="Frequency of 0-byte keepalive pings"
          />

          <NumberInput
            label="Peer Re-contact Cooldown"
            value={form.peerContactIntervalSeconds}
            onChange={(v) => update("peerContactIntervalSeconds", v)}
            min={10}
            max={300}
            suffix="sec"
            hint="Cooldown before re-connecting to idle peers"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="Multi-Tracker Tier Policies & Failover"
        description="Configure announcement behavior across tiered tracker lists (BEP 12)."
      >
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem" }}>
          <Toggle
            label="Enable Multi-Tracker Management (BEP 12)"
            checked={form.multiTrackerEnabled}
            onChange={(v) => update("multiTrackerEnabled", v)}
          />

          <Toggle
            label="Automatic Tier Failover"
            checked={form.multiTrackerFailoverEnabled}
            onChange={(v) => update("multiTrackerFailoverEnabled", v)}
            hint="Switch to secondary tracker tiers when primary is offline"
          />

          <Toggle
            label="Announce to All Tiers in Parallel"
            checked={form.announceToAllTiers}
            onChange={(v) => update("announceToAllTiers", v)}
          />

          <Toggle
            label="Announce to All Trackers in Tier"
            checked={form.announceToAllInTier}
            onChange={(v) => update("announceToAllInTier", v)}
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default ProtocolsSettingsTab;
