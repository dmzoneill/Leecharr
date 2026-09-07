import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useTorrentEngines,
  useActiveTorrentEngine,
  useSwitchTorrentEngine,
  useProbeTorrentEngine,
} from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  TextInput,
  SelectInput,
  Toggle,
} from "./shared";
import { useEscapeKey } from "../../hooks/useEscapeKey";
import { useToast } from "../../context/ToastContext";
import type { EngineProbeResult } from "../../api/types";

export function EngineSettingsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const { data: engines } = useTorrentEngines();
  const { data: activeEngineData } = useActiveTorrentEngine();
  const switchMutation = useSwitchTorrentEngine();
  const probeMutation = useProbeTorrentEngine();

  const [probeResult, setProbeResult] = useState<EngineProbeResult | null>(
    null,
  );
  const [probingEngineId, setProbingEngineId] = useState<string | null>(null);

  const [form, setForm] = useState({
    activeTorrentEngine: "MonoTorrent",
    diskCacheMb: 128,
    diskCachePolicy: "ReadsAndWrites",
    fastResumeMode: "BestEffort",
    autoSaveFastResumeIntervalSeconds: 300,
    piecePickerStrategy: "RarestFirst",
    endGamePickerEnabled: true,
    staleRequestTimeoutSeconds: 20,
    webSeedDelaySeconds: 30,
    hashingThreads: 2,
    aioThreads: 4,
    diskIoWriteMode: "OsCacheEnabled",
    filePoolSize: 256,
    chokingAlgorithm: "FixedSlots",
    seedChokingAlgorithm: "RoundRobin",
    mixedModeAlgorithm: "PeerProportional",
    prefetchEnabled: true,
    scrapePausedTorrentsEnabled: true,
    rpcWhitelistEnabled: false,
    rpcWhitelist: "127.0.0.1,::1",
  });

  const [dirty, setDirty] = useState(false);
  const [selectedEngineForSwitch, setSelectedEngineForSwitch] = useState<
    string | null
  >(null);
  useEscapeKey(
    () => setSelectedEngineForSwitch(null),
    Boolean(selectedEngineForSwitch),
  );
  useEscapeKey(() => setProbeResult(null), Boolean(probeResult));

  useEffect(() => {
    if (config) {
      setForm({
        activeTorrentEngine: config.activeTorrentEngine || "MonoTorrent",
        diskCacheMb: config.diskCacheBytes
          ? Math.round(config.diskCacheBytes / (1024 * 1024))
          : 64,
        diskCachePolicy: config.diskCachePolicy || "ReadsAndWrites",
        fastResumeMode: config.fastResumeMode || "BestEffort",
        autoSaveFastResumeIntervalSeconds:
          config.autoSaveFastResumeIntervalSeconds ?? 300,
        piecePickerStrategy: config.piecePickerStrategy || "RarestFirst",
        endGamePickerEnabled: config.endGamePickerEnabled ?? true,
        staleRequestTimeoutSeconds: config.staleRequestTimeoutSeconds ?? 20,
        webSeedDelaySeconds: config.webSeedDelaySeconds ?? 30,
        hashingThreads: config.hashingThreads ?? 2,
        aioThreads: config.aioThreads ?? 4,
        diskIoWriteMode: config.diskIoWriteMode || "OsCacheEnabled",
        filePoolSize: config.filePoolSize ?? 256,
        chokingAlgorithm: config.chokingAlgorithm || "FixedSlots",
        seedChokingAlgorithm: config.seedChokingAlgorithm || "RoundRobin",
        mixedModeAlgorithm: config.mixedModeAlgorithm || "PeerProportional",
        prefetchEnabled: config.prefetchEnabled ?? true,
        scrapePausedTorrentsEnabled: config.scrapePausedTorrentsEnabled ?? true,
        rpcWhitelistEnabled: config.rpcWhitelistEnabled ?? false,
        rpcWhitelist: config.rpcWhitelist || "127.0.0.1,::1",
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
        activeTorrentEngine: form.activeTorrentEngine,
        diskCacheBytes: form.diskCacheMb * 1024 * 1024,
        diskCachePolicy: form.diskCachePolicy,
        fastResumeMode: form.fastResumeMode,
        autoSaveFastResumeIntervalSeconds:
          form.autoSaveFastResumeIntervalSeconds,
        piecePickerStrategy: form.piecePickerStrategy,
        endGamePickerEnabled: form.endGamePickerEnabled,
        staleRequestTimeoutSeconds: form.staleRequestTimeoutSeconds,
        webSeedDelaySeconds: form.webSeedDelaySeconds,
        hashingThreads: form.hashingThreads,
        aioThreads: form.aioThreads,
        diskIoWriteMode: form.diskIoWriteMode,
        filePoolSize: form.filePoolSize,
        chokingAlgorithm: form.chokingAlgorithm,
        seedChokingAlgorithm: form.seedChokingAlgorithm,
        mixedModeAlgorithm: form.mixedModeAlgorithm,
        prefetchEnabled: form.prefetchEnabled,
        scrapePausedTorrentsEnabled: form.scrapePausedTorrentsEnabled,
        rpcWhitelistEnabled: form.rpcWhitelistEnabled,
        rpcWhitelist: form.rpcWhitelist,
      },
      {
        onSuccess: () => {
          setDirty(false);
          showToast(
            t("settingsTabs.batch2.engineSettingsSavedSuccessfully"),
            "success",
          );
        },
        onError: (err: any) => {
          showToast(
            err?.response?.data?.message ||
              err?.message ||
              t("settingsTabs.batch2.failedToSaveEngineSettings"),
            "error",
          );
        },
      },
    );
  };

  const handleSwitchConfirm = () => {
    if (selectedEngineForSwitch) {
      const targetEngine = selectedEngineForSwitch;
      switchMutation.mutate(
        { engineId: targetEngine, preserveTransfers: true },
        {
          onSuccess: (res: any) => {
            setSelectedEngineForSwitch(null);
            update("activeTorrentEngine", targetEngine);
            showToast(
              res?.message ||
                `Switched active torrent engine to ${targetEngine}`,
              "success",
            );
          },
          onError: (err: any) => {
            const errorMsg =
              err?.response?.data?.error ||
              err?.response?.data?.message ||
              err?.message ||
              t("settingsTabs.batch2.failedToSwitchTorrentEngine");
            showToast(errorMsg, "error");
            setSelectedEngineForSwitch(null);
          },
        },
      );
    }
  };

  const handleProbe = async (engineId: string) => {
    setProbingEngineId(engineId);
    try {
      const res = await probeMutation.mutateAsync(engineId);
      setProbeResult(res);
      if (res.isHealthy) {
        showToast(
          res.statusMessage || `${engineId} is healthy and operational.`,
          "success",
        );
      } else {
        showToast(
          res.statusMessage || `${engineId} health check reported issues.`,
          "error",
        );
      }
    } catch (err: any) {
      showToast(
        `Probe failed for ${engineId}: ${err?.message || t("settingsTabs.notifications.unknownError")}`,
        "error",
      );
    } finally {
      setProbingEngineId(null);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.batch2.loadingEngineSettings")}
      </div>
    );
  }

  const currentActiveEngine = (
    activeEngineData?.engineId ||
    form.activeTorrentEngine ||
    "MonoTorrent"
  ).toLowerCase();

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
        title={t("settingsTabs.batch2.bitTorrentEngineCore")}
        description={t("settingsTabs.batch2.selectActiveDownloadEngine")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
            marginBottom: "1rem",
          }}
        >
          {engines?.map((eng) => {
            const engineId = eng.engineId || (eng as any).engineType || "";
            const isActive = engineId.toLowerCase() === currentActiveEngine;
            return (
              <div
                key={engineId || eng.displayName}
                className="card"
                style={{
                  padding: "1rem",
                  borderRadius: "8px",
                  backgroundColor: isActive
                    ? "var(--bg-card-hover)"
                    : "var(--bg-primary)",
                  border: isActive
                    ? "2px solid var(--accent)"
                    : "1px solid var(--border)",
                  display: "flex",
                  flexDirection: "column",
                  justifyContent: "space-between",
                  gap: "0.75rem",
                }}
              >
                <div>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      marginBottom: "0.4rem",
                    }}
                  >
                    <span
                      style={{
                        fontWeight: 700,
                        fontSize: "1rem",
                        color: "var(--text-primary)",
                      }}
                    >
                      {eng.displayName}
                    </span>
                    {isActive ? (
                      <span
                        className="badge badge-success"
                        style={{
                          fontSize: "0.7rem",
                          padding: "0.15rem 0.5rem",
                        }}
                      >
                        {t("settingsTabs.batch2.activeBadge")}
                      </span>
                    ) : (
                      <span
                        className={`badge ${eng.isAvailable ? "badge-info" : "badge-warning"}`}
                        style={{
                          fontSize: "0.7rem",
                          padding: "0.15rem 0.5rem",
                        }}
                      >
                        {eng.isAvailable
                          ? t("settingsTabs.batch2.ready")
                          : t("settingsTabs.batch2.unavailable")}
                      </span>
                    )}
                  </div>
                  <div
                    style={{
                      fontSize: "0.8rem",
                      color: "var(--text-muted)",
                      marginBottom: "0.5rem",
                    }}
                  >
                    {eng.description}
                  </div>
                  <div
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-secondary)",
                    }}
                  >
                    {t("settingsTabs.batch2.version")}:{" "}
                    <strong>{eng.version}</strong>
                  </div>
                </div>

                <div
                  style={{
                    display: "flex",
                    gap: "0.5rem",
                    marginTop: "0.5rem",
                  }}
                >
                  <button
                    type="button"
                    className="btn btn-outline btn-small"
                    onClick={() => handleProbe(engineId)}
                    disabled={probingEngineId !== null}
                    style={{ flex: 1, fontSize: "0.75rem" }}
                  >
                    {probingEngineId === engineId
                      ? t("settingsTabs.batch2.probing")
                      : t("settingsTabs.batch2.probeHealth")}
                  </button>
                  {!isActive && (
                    <button
                      type="button"
                      className="btn btn-primary btn-small"
                      onClick={() => setSelectedEngineForSwitch(engineId)}
                      disabled={!eng.isAvailable || switchMutation.isPending}
                      style={{ flex: 1, fontSize: "0.75rem" }}
                    >
                      {t("settingsTabs.batch2.hotSwap")}
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </SectionCard>

      {/* Engine-Specific Cards */}
      <SectionCard
        title={t("settingsTabs.batch2.monoTorrentManagedEngineTuning")}
        description={t("settingsTabs.batch2.fineTuneAsyncRamWriteBuffers")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.batch2.ramWriteCacheMb")}
            value={form.diskCacheMb}
            onChange={(v) => update("diskCacheMb", v)}
            min={16}
            max={4096}
            step={16}
            suffix="MB"
            hint={t("settingsTabs.batch2.dirtyBlockWriteBufferSize")}
          />

          <SelectInput
            label={t("settingsTabs.batch2.diskCachePolicy")}
            value={form.diskCachePolicy}
            onChange={(v) => update("diskCachePolicy", v)}
            options={[
              {
                value: "ReadsAndWrites",
                label: t("settingsTabs.batch2.cacheReadsAndWrites"),
              },
              {
                value: "WritesOnly",
                label: t("settingsTabs.batch2.cacheWritesOnly"),
              },
              {
                value: "None",
                label: t("settingsTabs.batch2.disableMemoryCaching"),
              },
            ]}
          />

          <SelectInput
            label={t("settingsTabs.batch2.piecePickerStrategy")}
            value={form.piecePickerStrategy}
            onChange={(v) => update("piecePickerStrategy", v)}
            options={[
              {
                value: "RarestFirst",
                label: t("settingsTabs.batch2.rarestFirst"),
              },
              {
                value: "Sequential",
                label: t("settingsTabs.batch2.sequential"),
              },
              {
                value: "Streaming",
                label: t("settingsTabs.batch2.streamingBufferPriority"),
              },
              {
                value: "Random",
                label: t("settingsTabs.batch2.randomizedSelection"),
              },
            ]}
          />

          <NumberInput
            label={t("settingsTabs.batch2.fastResumeAutosaveInterval")}
            value={form.autoSaveFastResumeIntervalSeconds}
            onChange={(v) => update("autoSaveFastResumeIntervalSeconds", v)}
            min={30}
            max={3600}
            suffix={t("settingsTabs.batch2.sec")}
            hint={t(
              "settingsTabs.batch2.intervalToPersistVerifiedPieceBitfields",
            )}
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
            label={t("settingsTabs.batch2.enableEndgameMode")}
            checked={form.endGamePickerEnabled}
            onChange={(v) => update("endGamePickerEnabled", v)}
            hint={t("settingsTabs.batch2.requestFinalRemainingBlocks")}
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.batch2.libtorrentEngineTuning")}
        description={t("settingsTabs.batch2.configurePosixThreading")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.batch2.sha1HashingThreads")}
            value={form.hashingThreads}
            onChange={(v) => update("hashingThreads", v)}
            min={1}
            max={32}
            hint={t("settingsTabs.batch2.parallelCpuWorkers")}
          />

          <NumberInput
            label={t("settingsTabs.batch2.posixAsyncIoThreads")}
            value={form.aioThreads}
            onChange={(v) => update("aioThreads", v)}
            min={1}
            max={64}
            hint={t("settingsTabs.batch2.libtorrentStorageAsyncDiskIoThreads")}
          />

          <SelectInput
            label={t("settingsTabs.batch2.leecherChokingAlgorithm")}
            value={form.chokingAlgorithm}
            onChange={(v) => update("chokingAlgorithm", v)}
            options={[
              {
                value: "FixedSlots",
                label: t("settingsTabs.batch2.fixedSlotsStandard"),
              },
              {
                value: "RateBased",
                label: t("settingsTabs.batch2.rateBasedDynamicTitForTat"),
              },
              {
                value: "BittorrentChoker",
                label: t("settingsTabs.batch2.strictBitTorrent10Choker"),
              },
            ]}
          />

          <SelectInput
            label={t("settingsTabs.batch2.seederChokingAlgorithm")}
            value={form.seedChokingAlgorithm}
            onChange={(v) => update("seedChokingAlgorithm", v)}
            options={[
              {
                value: "RoundRobin",
                label: t("settingsTabs.batch2.roundRobinFairDistribution"),
              },
              {
                value: "FastestUpload",
                label: t("settingsTabs.batch2.fastestUploadFirst"),
              },
              {
                value: "AntiLeech",
                label: t("settingsTabs.batch2.antiLeechPriority"),
              },
            ]}
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.batch2.transmissionDaemonEngineTuning")}
        description={t("settingsTabs.batch2.configureDiskBlockPrefetching")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <Toggle
            label={t("settingsTabs.batch2.prefetchAdjacentDiskBlocks")}
            checked={form.prefetchEnabled}
            onChange={(v) => update("prefetchEnabled", v)}
            hint={t("settingsTabs.batch2.preloadDiskBlocksIntoMemory")}
          />

          <Toggle
            label={t("settingsTabs.batch2.scrapePausedTorrents")}
            checked={form.scrapePausedTorrentsEnabled}
            onChange={(v) => update("scrapePausedTorrentsEnabled", v)}
            hint={t("settingsTabs.batch2.queryTrackerSeederLeecherCounts")}
          />
        </div>
      </SectionCard>

      {/* Hot-Swap Modal */}
      {selectedEngineForSwitch && (
        <div
          className="modal-overlay"
          onClick={() => setSelectedEngineForSwitch(null)}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{ maxWidth: 460 }}
          >
            <h2 style={{ margin: "0 0 0.75rem", fontSize: "1.2rem" }}>
              {t("settingsTabs.batch2.switchActiveBitTorrentEngine")}
            </h2>
            <p
              style={{
                color: "var(--text-secondary)",
                fontSize: "0.9rem",
                lineHeight: 1.4,
              }}
            >
              Are you sure you want to switch the active BitTorrent engine to{" "}
              <strong>{selectedEngineForSwitch}</strong>?
            </p>
            <p
              style={{
                color: "var(--text-muted)",
                fontSize: "0.82rem",
                lineHeight: 1.4,
              }}
            >
              All in-flight download bitfields and statistics will be atomically
              checkpointed and migrated without interrupting disk payloads.
            </p>
            <div
              style={{
                display: "flex",
                gap: "0.5rem",
                justifyContent: "flex-end",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setSelectedEngineForSwitch(null)}
              >
                {t("settingsTabs.categories.modal.cancel")}
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSwitchConfirm}
                disabled={switchMutation.isPending}
              >
                {switchMutation.isPending
                  ? t("settingsTabs.batch2.switchingEngine")
                  : t("settingsTabs.batch2.confirmSwitch")}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Probe Diagnostic Results Modal */}
      {probeResult && (
        <div className="modal-overlay" onClick={() => setProbeResult(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{ maxWidth: 520 }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "1rem",
              }}
            >
              <h2
                style={{
                  margin: 0,
                  fontSize: "1.2rem",
                  color: "var(--text-primary)",
                }}
              >
                Probe Results: {probeResult.engineId}
              </h2>
              <span
                style={{
                  backgroundColor: probeResult.isHealthy
                    ? "#27ae60"
                    : "#e74c3c",
                  color: "#ffffff",
                  padding: "0.2rem 0.55rem",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 700,
                  letterSpacing: "0.03em",
                }}
              >
                {probeResult.isHealthy
                  ? t("settingsTabs.batch2.healthyReady")
                  : t("settingsTabs.batch2.warningUnhealthy")}
              </span>
            </div>

            <div
              style={{
                padding: "0.75rem 1rem",
                borderRadius: "6px",
                marginBottom: "1rem",
                backgroundColor: probeResult.isHealthy
                  ? "rgba(39, 174, 96, 0.15)"
                  : "rgba(231, 76, 60, 0.15)",
                border: `1px solid ${probeResult.isHealthy ? "#27ae60" : "#e74c3c"}`,
                color: probeResult.isHealthy ? "#2ecc71" : "#e74c3c",
                fontSize: "0.9rem",
                lineHeight: 1.4,
              }}
            >
              <strong>{t("settingsTabs.subsystems.status")}</strong>{" "}
              {probeResult.statusMessage ||
                (probeResult.isHealthy
                  ? t("settingsTabs.batch2.operational")
                  : t("settingsTabs.batch2.healthCheckReportedIssuesTxt"))}
            </div>

            {probeResult.dependencyChecks &&
              probeResult.dependencyChecks.length > 0 && (
                <div style={{ marginBottom: "1rem" }}>
                  <h4
                    style={{
                      fontSize: "0.85rem",
                      fontWeight: 600,
                      color: "var(--text-secondary)",
                      margin: "0 0 0.4rem 0",
                      textTransform: "uppercase",
                      letterSpacing: "0.04em",
                    }}
                  >
                    {t("settingsTabs.batch2.dependencyChecks")}
                  </h4>
                  <ul
                    style={{
                      margin: "0.35rem 0 0 0",
                      paddingLeft: "1.2rem",
                      fontSize: "0.83rem",
                      color: "var(--text-secondary)",
                      lineHeight: 1.5,
                    }}
                  >
                    {probeResult.dependencyChecks.map((check, idx) => {
                      if (typeof check === "object" && check !== null) {
                        return (
                          <li
                            key={idx}
                            style={{
                              color: check.passed ? "#2ecc71" : "#e74c3c",
                            }}
                          >
                            {check.passed ? "✅" : "❌"}{" "}
                            <strong>{check.name}</strong>:{" "}
                            {check.message ||
                              (check.passed
                                ? t("settingsTabs.batch2.passed")
                                : t("settingsTabs.batch2.failed"))}
                          </li>
                        );
                      }
                      return (
                        <li key={idx} style={{ color: "#2ecc71" }}>
                          ✅ {check}
                        </li>
                      );
                    })}
                  </ul>
                </div>
              )}

            {probeResult.warnings && probeResult.warnings.length > 0 && (
              <div style={{ marginBottom: "1rem" }}>
                <h4
                  style={{
                    fontSize: "0.85rem",
                    fontWeight: 600,
                    color: "#f39c12",
                    margin: "0 0 0.4rem 0",
                    textTransform: "uppercase",
                    letterSpacing: "0.04em",
                  }}
                >
                  {t("settingsTabs.batch2.warningsAndDiagnostics")}
                </h4>
                <ul
                  style={{
                    margin: "0.35rem 0 0 0",
                    paddingLeft: "1.2rem",
                    fontSize: "0.83rem",
                    color: "#f39c12",
                    lineHeight: 1.5,
                  }}
                >
                  {probeResult.warnings.map((warning, idx) => (
                    <li key={idx}>⚠️ {warning}</li>
                  ))}
                </ul>
              </div>
            )}

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                marginTop: "1.25rem",
              }}
            >
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={() => setProbeResult(null)}
              >
                {t("settingsTabs.batch2.close")}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default EngineSettingsTab;
