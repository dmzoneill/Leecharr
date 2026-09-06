import { useTranslation } from "../../i18n";
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
import { useToast } from "../../context/ToastContext";

export function ProtocolsSettingsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
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
    enableBep27PrivateTorrents: true,
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
        enableBep27PrivateTorrents:
          protoConfig?.enableBep27PrivateTorrents ?? true,
        utpEnabled: protoConfig?.utpEnabled ?? true,
        tcpFallback: protoConfig?.tcpFallback ?? true,
        transportConnectionTimeoutSeconds:
          protoConfig?.transportConnectionTimeoutSeconds ?? 30,
        handshakeTimeoutSeconds: peerConfig?.handshakeTimeoutSeconds ?? 30,
        messageReadTimeoutSeconds: peerConfig?.messageReadTimeoutSeconds ?? 60,
        keepAliveIntervalSeconds: peerConfig?.keepAliveIntervalSeconds ?? 120,
        peerContactIntervalSeconds:
          peerConfig?.peerContactIntervalSeconds ?? 30,
        multiTrackerEnabled: protoConfig?.multiTrackerEnabled ?? true,
        multiTrackerFailoverEnabled:
          protoConfig?.multiTrackerFailoverEnabled ?? true,
        announceToAllTiers: protoConfig?.announceToAllTiers ?? true,
        announceToAllInTier: protoConfig?.announceToAllInTier ?? false,
        failoverMaxConsecutiveFailures:
          protoConfig?.failoverMaxConsecutiveFailures ?? 3,
        failoverBackoffBaseSeconds:
          protoConfig?.failoverBackoffBaseSeconds ?? 30,
        failoverMaxBackoffSeconds:
          protoConfig?.failoverMaxBackoffSeconds ?? 1800,
      });
      setDirty(false);
    }
  }, [btConfig, protoConfig, peerConfig]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const isPending =
    saveBtMutation.isPending ||
    saveProtoMutation.isPending ||
    savePeerMutation.isPending;
  const isError =
    saveBtMutation.isError ||
    saveProtoMutation.isError ||
    savePeerMutation.isError;
  const isSuccess =
    (!btConfig || saveBtMutation.isSuccess) &&
    (!protoConfig || saveProtoMutation.isSuccess) &&
    (!peerConfig || savePeerMutation.isSuccess) &&
    (saveBtMutation.isSuccess ||
      saveProtoMutation.isSuccess ||
      savePeerMutation.isSuccess);
  const error = (saveBtMutation.error ||
    saveProtoMutation.error ||
    savePeerMutation.error) as Error | null;

  const handleSave = () => {
    let pending =
      (btConfig ? 1 : 0) + (protoConfig ? 1 : 0) + (peerConfig ? 1 : 0);
    if (pending === 0) return;
    let hasError = false;

    const handleSuccess = () => {
      pending--;
      if (pending === 0 && !hasError) {
        setDirty(false);
      }
    };

    const handleError = (err: any) => {
      hasError = true;
      showToast(err?.message || "Failed to save protocol settings", "error");
    };

    if (btConfig) {
      saveBtMutation.mutate(
        {
          ...btConfig,
          encryptionMode: form.encryptionMode,
          enableBep27PrivateTorrents: form.enableBep27PrivateTorrents,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
    if (protoConfig) {
      saveProtoMutation.mutate(
        {
          ...protoConfig,
          extensionUtMetadata: form.extensionUtMetadata,
          extensionUtPex: form.extensionUtPex,
          extensionLtDontHave: form.extensionLtDontHave,
          extensionFastExtension: form.extensionFastExtension,
          enableBep27PrivateTorrents: form.enableBep27PrivateTorrents,
          utpEnabled: form.utpEnabled,
          tcpFallback: form.tcpFallback,
          transportConnectionTimeoutSeconds:
            form.transportConnectionTimeoutSeconds,
          multiTrackerEnabled: form.multiTrackerEnabled,
          multiTrackerFailoverEnabled: form.multiTrackerFailoverEnabled,
          announceToAllTiers: form.announceToAllTiers,
          announceToAllInTier: form.announceToAllInTier,
          failoverMaxConsecutiveFailures: form.failoverMaxConsecutiveFailures,
          failoverBackoffBaseSeconds: form.failoverBackoffBaseSeconds,
          failoverMaxBackoffSeconds: form.failoverMaxBackoffSeconds,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
    if (peerConfig) {
      savePeerMutation.mutate(
        {
          ...peerConfig,
          handshakeTimeoutSeconds: form.handshakeTimeoutSeconds,
          messageReadTimeoutSeconds: form.messageReadTimeoutSeconds,
          keepAliveIntervalSeconds: form.keepAliveIntervalSeconds,
          peerContactIntervalSeconds: form.peerContactIntervalSeconds,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
  };

  if (btLoading || protoLoading || peerLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading protocol settings...
      </div>
    );
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
        title={t("settings.protocolEncryptionTranspor")}
        description={t("settings.configureBitTorrentEnhanceme")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label={t("settings.protocolEncryptionMSEPE")}
            value={form.encryptionMode}
            onChange={(v) => update("encryptionMode", v)}
            options={[
              {
                value: "preferEncrypted",
                label: "Prefer Encryption (Compatible & Secure)",
              },
              {
                value: "forceEncrypted",
                label: "Require Encryption (Strict, Drops Plaintext)",
              },
              {
                value: "allowPlaintext",
                label: "Allow Plaintext & Encryption",
              },
              { value: "disabled", label: "Disable Encryption" },
            ]}
          />

          <NumberInput
            label={t("settings.transportConnectionTimeout")}
            value={form.transportConnectionTimeoutSeconds}
            onChange={(v) => update("transportConnectionTimeoutSeconds", v)}
            min={5}
            max={120}
            suffix={t("settingsTabs.batch2.sec")}
            hint="Timeout for establishing TCP/uTP socket connections"
          />
        </div>

        <div
          style={{
            marginTop: "1rem",
            borderTop: "1px solid var(--border-light)",
            paddingTop: "1rem",
          }}
        >
          <div
            style={{
              fontSize: "0.85rem",
              fontWeight: 600,
              color: "var(--text-secondary)",
              marginBottom: "0.75rem",
            }}
          >
            BitTorrent Enhancement Proposals (BEP Suite)
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
              gap: "0.75rem",
            }}
          >
            <Toggle
              label={t("settings.utMetadataBEP9")}
              checked={form.extensionUtMetadata}
              onChange={(v) => update("extensionUtMetadata", v)}
              hint="Magnet link metadata exchange"
            />
            <Toggle
              label={t("settings.utPexBEP11")}
              checked={form.extensionUtPex}
              onChange={(v) => update("extensionUtPex", v)}
              hint="uTorrent Peer Exchange"
            />
            <Toggle
              label={t("settings.ltDonthaveBEP54")}
              checked={form.extensionLtDontHave}
              onChange={(v) => update("extensionLtDontHave", v)}
              hint="Prune unwanted piece messages"
            />
            <Toggle
              label={t("settings.fastExtensionBEP6")}
              checked={form.extensionFastExtension}
              onChange={(v) => update("extensionFastExtension", v)}
              hint="Allowed Fast & Suggest Pieces"
            />
            <Toggle
              label={t("settings.privateTorrentsBEP27")}
              checked={form.enableBep27PrivateTorrents}
              onChange={(v) => update("enableBep27PrivateTorrents", v)}
              hint="Strictly disables DHT, PEX, and Local Peer Discovery on private swarms"
            />
            <Toggle
              label={t("settings.uTPLEDBATBEP29")}
              checked={form.utpEnabled}
              onChange={(v) => update("utpEnabled", v)}
              hint="Micro Transport Protocol over UDP"
            />
            <Toggle
              label={t("settings.tCPFallback")}
              checked={form.tcpFallback}
              onChange={(v) => update("tcpFallback", v)}
              hint="Fall back to TCP on uTP timeout"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settings.protocolTimeoutsKeepalive")}
        description={t("settings.fineTuneNetworkSocketRead")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settings.handshakeTimeout")}
            value={form.handshakeTimeoutSeconds}
            onChange={(v) => update("handshakeTimeoutSeconds", v)}
            min={5}
            max={120}
            suffix={t("settingsTabs.batch2.sec")}
            hint="Maximum duration for wire handshake"
          />

          <NumberInput
            label={t("settings.messageReadTimeout")}
            value={form.messageReadTimeoutSeconds}
            onChange={(v) => update("messageReadTimeoutSeconds", v)}
            min={10}
            max={300}
            suffix={t("settingsTabs.batch2.sec")}
            hint="Deadline for reading incoming protocol frames"
          />

          <NumberInput
            label={t("settings.keepaliveInterval")}
            value={form.keepAliveIntervalSeconds}
            onChange={(v) => update("keepAliveIntervalSeconds", v)}
            min={30}
            max={600}
            suffix={t("settingsTabs.batch2.sec")}
            hint="Frequency of 0-byte keepalive pings"
          />

          <NumberInput
            label={t("settings.peerReContactCooldown")}
            value={form.peerContactIntervalSeconds}
            onChange={(v) => update("peerContactIntervalSeconds", v)}
            min={10}
            max={300}
            suffix={t("settingsTabs.batch2.sec")}
            hint="Cooldown before re-connecting to idle peers"
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settings.multiTrackerTierPolicies")}
        description={t("settings.configureAnnouncementBehavio")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <Toggle
            label={t("settings.enableMultiTrackerManagemen")}
            checked={form.multiTrackerEnabled}
            onChange={(v) => update("multiTrackerEnabled", v)}
          />

          <Toggle
            label={t("settings.automaticTierFailover")}
            checked={form.multiTrackerFailoverEnabled}
            onChange={(v) => update("multiTrackerFailoverEnabled", v)}
            hint="Switch to secondary tracker tiers when primary is offline"
          />

          <Toggle
            label={t("settings.announceToAllTiersInParal")}
            checked={form.announceToAllTiers}
            onChange={(v) => update("announceToAllTiers", v)}
          />

          <Toggle
            label={t("settings.announceToAllTrackersInTi")}
            checked={form.announceToAllInTier}
            onChange={(v) => update("announceToAllInTier", v)}
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default ProtocolsSettingsTab;
