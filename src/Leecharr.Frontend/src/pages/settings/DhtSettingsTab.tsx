import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useProtocolsConfig,
  useSaveProtocolsConfig,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";
import { useToast } from "../../context/ToastContext";

export function DhtSettingsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();

  const { data: protoConfig, isLoading: protoLoading } = useProtocolsConfig();
  const saveProtoMutation = useSaveProtocolsConfig();

  const [form, setForm] = useState({
    enableDht: true,
    dhtBootstrapNodes:
      "router.bittorrent.com:6881,dht.transmissionbt.com:6881,router.utorrent.com:6881,dht.aelitis.com:6881",
    dhtRoutingTableSize: 200,
    dhtAnnouncementInterval: 1800,
    dhtBootstrapTimeout: 30,
    dhtQueryTimeout: 15,
    dhtMaxNodes: 1000,
    dhtBucketSize: 8,
    dhtConcurrentQueries: 4,
    dhtAutoBootstrap: true,
    dhtRateLimitEnabled: true,
    dhtMaxQueriesPerSecond: 30,
    enablePex: true,
    pexInterval: 60,
    pexMaxPeersPerMessage: 50,
    enableLpd: true,
    defaultTrackers: "",
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (btConfig || protoConfig) {
      setForm({
        enableDht: btConfig?.enableDht ?? true,
        dhtBootstrapNodes:
          btConfig?.dhtBootstrapNodes ||
          "router.bittorrent.com:6881,dht.transmissionbt.com:6881,router.utorrent.com:6881,dht.aelitis.com:6881",
        dhtRoutingTableSize: protoConfig?.dhtRoutingTableSize ?? 200,
        dhtAnnouncementInterval: protoConfig?.dhtAnnouncementInterval ?? 1800,
        dhtBootstrapTimeout: protoConfig?.dhtBootstrapTimeout ?? 30,
        dhtQueryTimeout: protoConfig?.dhtQueryTimeout ?? 15,
        dhtMaxNodes: protoConfig?.dhtMaxNodes ?? 1000,
        dhtBucketSize: protoConfig?.dhtBucketSize ?? 8,
        dhtConcurrentQueries: protoConfig?.dhtConcurrentQueries ?? 4,
        dhtAutoBootstrap: protoConfig?.dhtAutoBootstrap ?? true,
        dhtRateLimitEnabled: protoConfig?.dhtRateLimitEnabled ?? true,
        dhtMaxQueriesPerSecond: protoConfig?.dhtMaxQueriesPerSecond ?? 30,
        enablePex: btConfig?.enablePex ?? true,
        pexInterval: protoConfig?.pexInterval ?? 60,
        pexMaxPeersPerMessage: protoConfig?.pexMaxPeersPerMessage ?? 50,
        enableLpd: btConfig?.enableLpd ?? true,
        defaultTrackers: btConfig?.defaultTrackers || "",
      });
      setDirty(false);
    }
  }, [btConfig, protoConfig]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const isPending = saveBtMutation.isPending || saveProtoMutation.isPending;
  const isError = saveBtMutation.isError || saveProtoMutation.isError;
  const isSuccess =
    (!btConfig || saveBtMutation.isSuccess) &&
    (!protoConfig || saveProtoMutation.isSuccess) &&
    (saveBtMutation.isSuccess || saveProtoMutation.isSuccess);
  const error = (saveBtMutation.error ||
    saveProtoMutation.error) as Error | null;

  const handleSave = () => {
    let pending = (btConfig ? 1 : 0) + (protoConfig ? 1 : 0);
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
      showToast(
        err?.message || "Failed to save DHT discovery settings",
        "error",
      );
    };

    if (btConfig) {
      saveBtMutation.mutate(
        {
          ...btConfig,
          enableDht: form.enableDht,
          enablePex: form.enablePex,
          enableLpd: form.enableLpd,
          dhtBootstrapNodes: form.dhtBootstrapNodes,
          defaultTrackers: form.defaultTrackers,
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
          dhtRoutingTableSize: form.dhtRoutingTableSize,
          dhtAnnouncementInterval: form.dhtAnnouncementInterval,
          dhtBootstrapTimeout: form.dhtBootstrapTimeout,
          dhtQueryTimeout: form.dhtQueryTimeout,
          dhtMaxNodes: form.dhtMaxNodes,
          dhtBucketSize: form.dhtBucketSize,
          dhtConcurrentQueries: form.dhtConcurrentQueries,
          dhtAutoBootstrap: form.dhtAutoBootstrap,
          dhtRateLimitEnabled: form.dhtRateLimitEnabled,
          dhtMaxQueriesPerSecond: form.dhtMaxQueriesPerSecond,
          pexInterval: form.pexInterval,
          pexMaxPeersPerMessage: form.pexMaxPeersPerMessage,
        },
        {
          onSuccess: handleSuccess,
          onError: handleError,
        },
      );
    }
  };

  if (btLoading || protoLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading DHT discovery settings...
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
        title={t("settings.mainlineDistributedHashTabl")}
        description={t("settings.trackerlessSwarmDiscoveryAn")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settings.enableMainlineDHT")}
            checked={form.enableDht}
            onChange={(v) => update("enableDht", v)}
            hint="Find peers without requiring centralized tracker responses"
          />

          <TextInput
            label={t("settings.bootstrapRouterNodes")}
            value={form.dhtBootstrapNodes}
            onChange={(v) => update("dhtBootstrapNodes", v)}
            disabled={!form.enableDht}
            hint="Comma-separated bootstrap routers used on startup"
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
              gap: "1rem",
            }}
          >
            <NumberInput
              label={t("settings.routingTableSizeKBuckets")}
              value={form.dhtRoutingTableSize}
              onChange={(v) => update("dhtRoutingTableSize", v)}
              disabled={!form.enableDht}
              min={10}
              max={1000}
            />

            <NumberInput
              label={t("settings.kBucketCapacity")}
              value={form.dhtBucketSize}
              onChange={(v) => update("dhtBucketSize", v)}
              disabled={!form.enableDht}
              min={4}
              max={32}
            />

            <NumberInput
              label={t("settings.maxActiveRoutingNodes")}
              value={form.dhtMaxNodes}
              onChange={(v) => update("dhtMaxNodes", v)}
              disabled={!form.enableDht}
              min={100}
              max={10000}
            />

            <NumberInput
              label={t("settings.concurrentAlphaQueries")}
              value={form.dhtConcurrentQueries}
              onChange={(v) => update("dhtConcurrentQueries", v)}
              disabled={!form.enableDht}
              min={1}
              max={16}
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
                gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                gap: "1rem",
              }}
            >
              <Toggle
                label={t("settings.rateLimitInboundDHTQueries")}
                checked={form.dhtRateLimitEnabled}
                onChange={(v) => update("dhtRateLimitEnabled", v)}
                disabled={!form.enableDht}
                hint="Protects upstream bandwidth from UDP query floods"
              />

              <NumberInput
                label={t("settings.maxDHTQueriesSec")}
                value={form.dhtMaxQueriesPerSecond}
                onChange={(v) => update("dhtMaxQueriesPerSecond", v)}
                disabled={!form.enableDht || !form.dhtRateLimitEnabled}
                min={5}
                max={500}
                suffix="q/s"
              />
            </div>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settings.peerExchangePEXLocalDi")}
        description={t("settings.exchangeConnectedPeerLists")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <div>
            <Toggle
              label={t("settings.enablePeerExchangePEXBEP")}
              checked={form.enablePex}
              onChange={(v) => update("enablePex", v)}
              hint="Exchanges known peer IP addresses directly with connected swarm peers"
            />
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr",
                gap: "0.5rem",
                marginTop: "0.5rem",
              }}
            >
              <NumberInput
                label={t("settings.pEXBroadcastCadence")}
                value={form.pexInterval}
                onChange={(v) => update("pexInterval", v)}
                disabled={!form.enablePex}
                min={15}
                max={300}
                suffix={t("settingsTabs.batch2.sec")}
              />
              <NumberInput
                label={t("settings.maxPeersPerPEXFrame")}
                value={form.pexMaxPeersPerMessage}
                onChange={(v) => update("pexMaxPeersPerMessage", v)}
                disabled={!form.enablePex}
                min={10}
                max={100}
              />
            </div>
          </div>

          <div>
            <Toggle
              label={t("settings.localPeerDiscoveryLPDLS")}
              checked={form.enableLpd}
              onChange={(v) => update("enableLpd", v)}
              hint="Multicast subnet broadcasts (239.192.152.143:6771) for maximum LAN speeds"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settings.defaultFallbackPublicTracke")}
        description={t("settings.publicFallbackAnnounceURLs")}
      >
        <TextInput
          label={t("settings.defaultTrackersCommaOrNew")}
          value={form.defaultTrackers}
          onChange={(v) => update("defaultTrackers", v)}
          hint="e.g. udp://tracker.opentrackr.org:1337/announce, udp://open.stealth.si:80/announce"
        />
      </SectionCard>
    </div>
  );
}

export default DhtSettingsTab;
