import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useTorrentEngines,
  useActiveTorrentEngine,
  useSwitchTorrentEngine,
  useSwitchTorrentEngineVersion,
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
import { trackEngineSwitch, trackSettingSave } from "../../utils/analytics";

export function EngineSettingsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const { data: engines } = useTorrentEngines();
  const { data: activeEngineData } = useActiveTorrentEngine();
  const switchMutation = useSwitchTorrentEngine();
  const switchVersionMutation = useSwitchTorrentEngineVersion();
  const probeMutation = useProbeTorrentEngine();

  const [probeResult, setProbeResult] = useState<EngineProbeResult | null>(
    null,
  );
  const [probingEngineId, setProbingEngineId] = useState<string | null>(null);
  const [selectedEngineVersions, setSelectedEngineVersions] = useState<
    Record<string, string>
  >({});

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
  const [selectedEngineForSwitch, setSelectedEngineForSwitch] = useState<{
    engineId: string;
    version?: string;
  } | null>(null);
  const [selectedVersionForSwitch, setSelectedVersionForSwitch] = useState<{
    engineId: string;
    fromVersion: string;
    toVersion: string;
  } | null>(null);

  useEscapeKey(
    () => setSelectedEngineForSwitch(null),
    Boolean(selectedEngineForSwitch),
  );
  useEscapeKey(
    () => setSelectedVersionForSwitch(null),
    Boolean(selectedVersionForSwitch),
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
          trackSettingSave("engine", {
            activeTorrentEngine: form.activeTorrentEngine,
            diskCacheMb: form.diskCacheMb,
            piecePickerStrategy: form.piecePickerStrategy,
            diskIoWriteMode: form.diskIoWriteMode,
          });
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
      const targetEngine = selectedEngineForSwitch.engineId;
      const targetVersion = selectedEngineForSwitch.version;
      switchMutation.mutate(
        { engineId: targetEngine, version: targetVersion, preserveTransfers: true },
        {
          onSuccess: (res: any) => {
            setSelectedEngineForSwitch(null);
            update("activeTorrentEngine", targetEngine);
            trackEngineSwitch(targetEngine);
            showToast(
              res?.message ||
                t("settingsTabs.engine.switchedEngine", {
                  engine: targetVersion
                    ? `${targetEngine} (v${targetVersion})`
                    : targetEngine,
                  defaultValue: `Switched active torrent engine to ${targetEngine}${targetVersion ? ` (v${targetVersion})` : ""}`,
                }),
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

  const handleSwitchVersionConfirm = () => {
    if (selectedVersionForSwitch) {
      const targetVersion = selectedVersionForSwitch.toVersion;
      switchVersionMutation.mutate(
        { version: targetVersion, preserveTransfers: true },
        {
          onSuccess: (res: any) => {
            setSelectedVersionForSwitch(null);
            showToast(
              res?.message ||
                `Switched active engine version to v${targetVersion}`,
              "success",
            );
          },
          onError: (err: any) => {
            const errorMsg =
              err?.response?.data?.error ||
              err?.response?.data?.message ||
              err?.message ||
              "Failed to switch engine version";
            showToast(errorMsg, "error");
            setSelectedVersionForSwitch(null);
          },
        },
      );
    }
  };

  const handleProbe = async (engineId: string, version?: string) => {
    setProbingEngineId(engineId);
    try {
      const res = await probeMutation.mutateAsync({ engineId, version });
      setProbeResult(res);
      if (res.isHealthy) {
        showToast(
          res.statusMessage ||
            t("settingsTabs.engine.engineHealthy", {
              engine: version ? `${engineId} (v${version})` : engineId,
              defaultValue: `${engineId}${version ? ` (v${version})` : ""} is healthy and operational.`,
            }),
          "success",
        );
      } else {
        showToast(
          res.statusMessage ||
            t("settingsTabs.engine.engineIssues", {
              engine: version ? `${engineId} (v${version})` : engineId,
              defaultValue: `${engineId}${version ? ` (v${version})` : ""} health check reported issues.`,
            }),
          "error",
        );
      }
    } catch (err: any) {
      showToast(
        t("settingsTabs.engine.probeFailed", {
          engine: version ? `${engineId} (v${version})` : engineId,
          error: err?.message || t("settingsTabs.notifications.unknownError"),
          defaultValue: `Probe failed for ${engineId}${version ? ` (v${version})` : ""}: ${err?.message || ""}`,
        }),
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
            const supportedVersions =
              eng.supportedVersions && eng.supportedVersions.length > 0
                ? eng.supportedVersions
                : eng.version
                  ? [eng.version]
                  : [];
            const hasMultipleVersions = supportedVersions.length > 1;
            const currentSelectedVersion =
              selectedEngineVersions[engineId] ||
              eng.activeVersion ||
              supportedVersions[0] ||
              eng.version;
            const activeVersion = eng.activeVersion || eng.version;
            const isVersionActive =
              isActive && currentSelectedVersion === activeVersion;
            const selectedCaps =
              eng.versionCapabilities?.[currentSelectedVersion] ||
              eng.capabilities;

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

                  {/* Version Selection */}
                  <div
                    style={{
                      marginTop: "0.5rem",
                      marginBottom: "0.5rem",
                      display: "flex",
                      flexDirection: "column",
                      gap: "0.35rem",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      <label
                        style={{
                          fontSize: "0.75rem",
                          fontWeight: 600,
                          color: "var(--text-secondary)",
                          margin: 0,
                        }}
                      >
                        {t("settingsTabs.batch2.version")}:
                      </label>
                      {isActive && activeVersion && (
                        <span
                          style={{
                            fontSize: "0.7rem",
                            color: "var(--text-muted)",
                          }}
                        >
                          Running: <strong>v{activeVersion}</strong>
                        </span>
                      )}
                    </div>

                    {hasMultipleVersions ? (
                      <select
                        className="form-control"
                        value={currentSelectedVersion}
                        onChange={(e) =>
                          setSelectedEngineVersions((prev) => ({
                            ...prev,
                            [engineId]: e.target.value,
                          }))
                        }
                        style={{
                          fontSize: "0.8rem",
                          padding: "0.25rem 0.5rem",
                          height: "auto",
                          backgroundColor: "var(--bg-secondary)",
                          color: "var(--text-primary)",
                          borderColor: "var(--border)",
                          borderRadius: "4px",
                        }}
                      >
                        {supportedVersions.map((v) => (
                          <option key={v} value={v}>
                            v{v}
                            {isActive && v === activeVersion
                              ? " (Active)"
                              : ""}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <div
                        style={{
                          fontSize: "0.8rem",
                          color: "var(--text-primary)",
                          fontWeight: 600,
                        }}
                      >
                        {eng.version}
                      </div>
                    )}
                  </div>

                  {/* Dynamic Version Capabilities */}
                  <div
                    style={{
                      display: "flex",
                      flexWrap: "wrap",
                      gap: "0.3rem",
                      marginTop: "0.4rem",
                    }}
                  >
                    {selectedCaps?.supportsV2Torrents ? (
                      <span
                        className="badge badge-info"
                        style={{
                          fontSize: "0.65rem",
                          padding: "0.1rem 0.35rem",
                        }}
                        title="Supports BitTorrent v2 Merkle trees and hybrid swarms"
                      >
                        v1 + v2 Merkle
                      </span>
                    ) : (
                      <span
                        className="badge badge-secondary"
                        style={{
                          fontSize: "0.65rem",
                          padding: "0.1rem 0.35rem",
                        }}
                        title="BitTorrent v1 single SHA-1 hash only"
                      >
                        v1 Only
                      </span>
                    )}
                    {selectedCaps?.supportsMemoryMappedIo ? (
                      <span
                        className="badge badge-info"
                        style={{
                          fontSize: "0.65rem",
                          padding: "0.1rem 0.35rem",
                        }}
                        title="Asynchronous memory-mapped / disk AIO"
                      >
                        Async AIO
                      </span>
                    ) : (
                      <span
                        className="badge badge-secondary"
                        style={{
                          fontSize: "0.65rem",
                          padding: "0.1rem 0.35rem",
                        }}
                        title="POSIX synchronous disk I/O"
                      >
                        POSIX I/O
                      </span>
                    )}
                    {selectedCaps?.supportsUtp && (
                      <span
                        className="badge badge-secondary"
                        style={{
                          fontSize: "0.65rem",
                          padding: "0.1rem 0.35rem",
                        }}
                      >
                        uTP
                      </span>
                    )}
                  </div>
                </div>

                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.4rem",
                    marginTop: "0.5rem",
                  }}
                >
                  {isActive && hasMultipleVersions && !isVersionActive && (
                    <button
                      type="button"
                      className="btn btn-warning btn-small"
                      onClick={() =>
                        setSelectedVersionForSwitch({
                          engineId,
                          fromVersion: activeVersion,
                          toVersion: currentSelectedVersion,
                        })
                      }
                      disabled={switchVersionMutation.isPending}
                      style={{ fontSize: "0.75rem", width: "100%" }}
                    >
                      ⚡ Switch to v{currentSelectedVersion}
                    </button>
                  )}

                  <div
                    style={{
                      display: "flex",
                      gap: "0.5rem",
                    }}
                  >
                    <button
                      type="button"
                      className="btn btn-outline btn-small"
                      onClick={() =>
                        handleProbe(engineId, currentSelectedVersion)
                      }
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
                        onClick={() =>
                          setSelectedEngineForSwitch({
                            engineId,
                            version: currentSelectedVersion,
                          })
                        }
                        disabled={
                          !eng.isAvailable || switchMutation.isPending
                        }
                        style={{ flex: 1, fontSize: "0.75rem" }}
                      >
                        {t("settingsTabs.batch2.hotSwap")}
                      </button>
                    )}
                  </div>
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
          <SelectInput
            label="Fast Resume Mode"
            value={form.fastResumeMode}
            onChange={(v) => update("fastResumeMode", v)}
            options={[
              {
                value: "BestEffort",
                label: "Best Effort",
              },
              {
                value: "Accurate",
                label: "Accurate",
              },
            ]}
            hint="Strategy for validating fast resume data against existing disk files"
          />

          <NumberInput
            label="Stale Request Timeout"
            value={form.staleRequestTimeoutSeconds}
            onChange={(v) => update("staleRequestTimeoutSeconds", v)}
            min={5}
            max={120}
            suffix="s"
            hint="Timeout before cancelling unfulfilled block requests"
          />

          <NumberInput
            label="Web Seed Delay"
            value={form.webSeedDelaySeconds}
            onChange={(v) => update("webSeedDelaySeconds", v)}
            min={0}
            max={300}
            suffix="s"
            hint="Delay before requesting blocks from HTTP/FTP web seeds"
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
            label="Disk I/O Write Mode"
            value={form.diskIoWriteMode}
            onChange={(v) => update("diskIoWriteMode", v)}
            options={[
              {
                value: "OsCacheEnabled",
                label: "OS Cache Enabled",
              },
              {
                value: "WriteThrough",
                label: "Write Through (Direct I/O)",
              },
              {
                value: "Disabled",
                label: "Disabled",
              },
            ]}
            hint="Disk write caching strategy for libtorrent storage"
          />

          <NumberInput
            label="File Pool Size"
            value={form.filePoolSize}
            onChange={(v) => update("filePoolSize", v)}
            min={16}
            max={4096}
            hint="Maximum open file handles cached by libtorrent storage"
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

          <SelectInput
            label="Mixed Mode Algorithm"
            value={form.mixedModeAlgorithm}
            onChange={(v) => update("mixedModeAlgorithm", v)}
            options={[
              {
                value: "PeerProportional",
                label: "Peer Proportional",
              },
              {
                value: "PreferTCP",
                label: "Prefer TCP",
              },
              {
                value: "PreferuTP",
                label: "Prefer uTP",
              },
            ]}
            hint="Algorithm for balancing TCP and uTP transport connections"
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

          <Toggle
            label="Enable RPC Whitelist"
            checked={form.rpcWhitelistEnabled}
            onChange={(v) => update("rpcWhitelistEnabled", v)}
            hint="Restrict Transmission RPC access to specified IP addresses"
          />

          <TextInput
            label="RPC Whitelist"
            value={form.rpcWhitelist}
            onChange={(v) => update("rpcWhitelist", v)}
            disabled={!form.rpcWhitelistEnabled}
            hint="Comma-separated list of allowed IPv4/IPv6 addresses or subnets (e.g. 127.0.0.1,::1)"
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
              <strong>
                {selectedEngineForSwitch.engineId}
                {selectedEngineForSwitch.version
                  ? ` (v${selectedEngineForSwitch.version})`
                  : ""}
              </strong>
              ?
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

      {/* Version Switch Modal */}
      {selectedVersionForSwitch && (
        <div
          className="modal-overlay"
          onClick={() => setSelectedVersionForSwitch(null)}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{ maxWidth: 460 }}
          >
            <h2 style={{ margin: "0 0 0.75rem", fontSize: "1.2rem" }}>
              Switch Active Engine Version
            </h2>
            <p
              style={{
                color: "var(--text-secondary)",
                fontSize: "0.9rem",
                lineHeight: 1.4,
              }}
            >
              Are you sure you want to switch the active{" "}
              <strong>{selectedVersionForSwitch.engineId}</strong> engine version from{" "}
              <strong>v{selectedVersionForSwitch.fromVersion}</strong> to{" "}
              <strong>v{selectedVersionForSwitch.toVersion}</strong>?
            </p>
            <p
              style={{
                color: "var(--text-muted)",
                fontSize: "0.82rem",
                lineHeight: 1.4,
              }}
            >
              The engine session will be gracefully re-initialized with target version
              capabilities. Active torrent bitfields and transfer states are preserved.
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
                onClick={() => setSelectedVersionForSwitch(null)}
              >
                {t("settingsTabs.categories.modal.cancel")}
              </button>
              <button
                type="button"
                className="btn btn-warning btn-small"
                onClick={handleSwitchVersionConfirm}
                disabled={switchVersionMutation.isPending}
              >
                {switchVersionMutation.isPending
                  ? "Switching Version..."
                  : "Confirm Version Switch"}
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
                {probeResult.version ? ` (v${probeResult.version})` : ""}
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
