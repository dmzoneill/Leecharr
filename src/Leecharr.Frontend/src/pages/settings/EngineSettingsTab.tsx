import React, { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useTorrentEngines,
  useActiveTorrentEngine,
  useSwitchTorrentEngine,
  useProbeTorrentEngine,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, SelectInput, Toggle } from "./shared";
import { useEscapeKey } from "../../hooks/useEscapeKey";
import { useToast } from "../../context/ToastContext";
import type { EngineProbeResult } from "../../api/types";

export function EngineSettingsTab() {
  const { showToast } = useToast();
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const { data: engines } = useTorrentEngines();
  const { data: activeEngineData } = useActiveTorrentEngine();
  const switchMutation = useSwitchTorrentEngine();
  const probeMutation = useProbeTorrentEngine();

  const [probeResult, setProbeResult] = useState<EngineProbeResult | null>(null);
  const [probingEngineId, setProbingEngineId] = useState<string | null>(null);

  const [form, setForm] = useState({
    activeTorrentEngine: "MonoTorrent",
    diskCacheMb: 64,
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
  const [selectedEngineForSwitch, setSelectedEngineForSwitch] = useState<string | null>(null);
  useEscapeKey(() => setSelectedEngineForSwitch(null), Boolean(selectedEngineForSwitch));
  useEscapeKey(() => setProbeResult(null), Boolean(probeResult));

  useEffect(() => {
    if (config) {
      setForm({
        activeTorrentEngine: config.activeTorrentEngine || "MonoTorrent",
        diskCacheMb: config.diskCacheBytes ? Math.round(config.diskCacheBytes / (1024 * 1024)) : 64,
        diskCachePolicy: config.diskCachePolicy || "ReadsAndWrites",
        fastResumeMode: config.fastResumeMode || "BestEffort",
        autoSaveFastResumeIntervalSeconds: config.autoSaveFastResumeIntervalSeconds ?? 300,
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

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
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
        autoSaveFastResumeIntervalSeconds: form.autoSaveFastResumeIntervalSeconds,
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
          showToast("Engine settings saved successfully", "success");
        },
        onError: (err: any) => {
          showToast(
            err?.response?.data?.message || err?.message || "Failed to save engine settings",
            "error"
          );
        },
      }
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
              res?.message || `Switched active torrent engine to ${targetEngine}`,
              "success"
            );
          },
          onError: (err: any) => {
            const errorMsg =
              err?.response?.data?.error || err?.response?.data?.message || err?.message || "Failed to switch torrent engine";
            showToast(errorMsg, "error");
            setSelectedEngineForSwitch(null);
          },
        }
      );
    }
  };

  const handleProbe = async (engineId: string) => {
    setProbingEngineId(engineId);
    try {
      const res = await probeMutation.mutateAsync(engineId);
      setProbeResult(res);
      if (res.isHealthy) {
        showToast(res.statusMessage || `${engineId} is healthy and operational.`, "success");
      } else {
        showToast(res.statusMessage || `${engineId} health check reported issues.`, "error");
      }
    } catch (err: any) {
      showToast(`Probe failed for ${engineId}: ${err?.message || "Unknown error"}`, "error");
    } finally {
      setProbingEngineId(null);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading engine settings...
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
        title="BitTorrent Engine Core & Runtime Hot-Swap"
        description="Select the active download engine powering all BitTorrent peer swarms and transfers."
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
                  backgroundColor: isActive ? "var(--bg-card-hover)" : "var(--bg-primary)",
                  border: isActive ? "2px solid var(--accent)" : "1px solid var(--border)",
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
                        ● Active
                      </span>
                    ) : (
                      <span
                        className={`badge ${eng.isAvailable ? "badge-info" : "badge-warning"}`}
                        style={{
                          fontSize: "0.7rem",
                          padding: "0.15rem 0.5rem",
                        }}
                      >
                        {eng.isAvailable ? "Ready" : "Unavailable"}
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
                    Version: <strong>{eng.version}</strong>
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
                    {probingEngineId === engineId ? "⏳ Probing..." : "🔍 Probe Health"}
                  </button>
                  {!isActive && (
                    <button
                      type="button"
                      className="btn btn-primary btn-small"
                      onClick={() => setSelectedEngineForSwitch(engineId)}
                      disabled={!eng.isAvailable || switchMutation.isPending}
                      style={{ flex: 1, fontSize: "0.75rem" }}
                    >
                      ⚡ Hot-Swap
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
        title="MonoTorrent Managed C# Engine Tuning"
        description="Fine-tune async RAM write buffers, piece pickers, and FastResume persistence."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="RAM Write Cache (MB)"
            value={form.diskCacheMb}
            onChange={(v) => update("diskCacheMb", v)}
            min={16}
            max={4096}
            step={16}
            suffix="MB"
            hint="Dirty block write buffer size before flushing to disk"
          />

          <SelectInput
            label="Disk Cache Policy"
            value={form.diskCachePolicy}
            onChange={(v) => update("diskCachePolicy", v)}
            options={[
              {
                value: "ReadsAndWrites",
                label: "Cache Reads & Writes (Recommended)",
              },
              { value: "WritesOnly", label: "Cache Writes Only" },
              {
                value: "None",
                label: "Disable Memory Caching (Direct Disk I/O)",
              },
            ]}
          />

          <SelectInput
            label="Piece Picker Strategy"
            value={form.piecePickerStrategy}
            onChange={(v) => update("piecePickerStrategy", v)}
            options={[
              {
                value: "RarestFirst",
                label: "Rarest First (Optimal Swarm Distribution)",
              },
              {
                value: "Sequential",
                label: "Sequential (Head-to-Tail for Instant Inspection)",
              },
              { value: "Streaming", label: "Streaming Buffer Priority" },
              { value: "Random", label: "Randomized Selection" },
            ]}
          />

          <NumberInput
            label="FastResume Autosave Interval (s)"
            value={form.autoSaveFastResumeIntervalSeconds}
            onChange={(v) => update("autoSaveFastResumeIntervalSeconds", v)}
            min={30}
            max={3600}
            suffix="sec"
            hint="Interval to persist verified piece bitfields to disk"
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
            label="Enable Endgame Mode"
            checked={form.endGamePickerEnabled}
            onChange={(v) => update("endGamePickerEnabled", v)}
            hint="Request the final remaining blocks from all available peers simultaneously to avoid stalled finishes"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="libtorrent (Rasterbar) Engine Tuning"
        description="Configure POSIX threading, OS page caching, and choking algorithms."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="SHA-1 Hashing Threads"
            value={form.hashingThreads}
            onChange={(v) => update("hashingThreads", v)}
            min={1}
            max={32}
            hint="Parallel CPU workers for SHA-1 piece checksum validation"
          />

          <NumberInput
            label="POSIX Async I/O Threads"
            value={form.aioThreads}
            onChange={(v) => update("aioThreads", v)}
            min={1}
            max={64}
            hint="libtorrent storage async disk I/O threads"
          />

          <SelectInput
            label="Leecher Choking Algorithm"
            value={form.chokingAlgorithm}
            onChange={(v) => update("chokingAlgorithm", v)}
            options={[
              { value: "FixedSlots", label: "Fixed Slots (Standard)" },
              { value: "RateBased", label: "Rate-Based Dynamic Tit-for-Tat" },
              {
                value: "BittorrentChoker",
                label: "Strict BitTorrent 1.0 Choker",
              },
            ]}
          />

          <SelectInput
            label="Seeder Choking Algorithm"
            value={form.seedChokingAlgorithm}
            onChange={(v) => update("seedChokingAlgorithm", v)}
            options={[
              { value: "RoundRobin", label: "Round Robin (Fair Distribution)" },
              { value: "FastestUpload", label: "Fastest Upload First" },
              { value: "AntiLeech", label: "Anti-Leech Priority" },
            ]}
          />
        </div>
      </SectionCard>

      <SectionCard
        title="Transmission Daemon Engine Tuning"
        description="Configure disk block prefetching and internal RPC whitelist."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <Toggle
            label="Prefetch Adjacent Disk Blocks"
            checked={form.prefetchEnabled}
            onChange={(v) => update("prefetchEnabled", v)}
            hint="Preload disk blocks into memory to improve upload efficiency"
          />

          <Toggle
            label="Scrape Paused Torrents"
            checked={form.scrapePausedTorrentsEnabled}
            onChange={(v) => update("scrapePausedTorrentsEnabled", v)}
            hint="Query tracker seeder/leecher counts even while torrents are paused"
          />
        </div>
      </SectionCard>

      {/* Hot-Swap Modal */}
      {selectedEngineForSwitch && (
        <div className="modal-overlay" onClick={() => setSelectedEngineForSwitch(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 460 }}>
            <h2 style={{ margin: "0 0 0.75rem", fontSize: "1.2rem" }}>
              Switch Active BitTorrent Engine
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
              All in-flight download bitfields and statistics will be atomically checkpointed and
              migrated without interrupting disk payloads.
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
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSwitchConfirm}
                disabled={switchMutation.isPending}
              >
                {switchMutation.isPending ? "Switching Engine..." : "Confirm Switch"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Probe Diagnostic Results Modal */}
      {probeResult && (
        <div className="modal-overlay" onClick={() => setProbeResult(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 520 }}>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "1rem",
              }}
            >
              <h2 style={{ margin: 0, fontSize: "1.2rem", color: "var(--text-primary)" }}>
                Probe Results: {probeResult.engineId}
              </h2>
              <span
                style={{
                  backgroundColor: probeResult.isHealthy ? "#27ae60" : "#e74c3c",
                  color: "#ffffff",
                  padding: "0.2rem 0.55rem",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 700,
                  letterSpacing: "0.03em",
                }}
              >
                {probeResult.isHealthy ? "HEALTHY / READY" : "WARNING / UNHEALTHY"}
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
              <strong>Status:</strong>{" "}
              {probeResult.statusMessage ||
                (probeResult.isHealthy ? "Operational" : "Health check reported issues.")}
            </div>

            {probeResult.dependencyChecks && probeResult.dependencyChecks.length > 0 && (
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
                  Dependency Checks
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
                          {check.passed ? "✅" : "❌"} <strong>{check.name}</strong>:{" "}
                          {check.message || (check.passed ? "Passed" : "Failed")}
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
                  Warnings & Diagnostics
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
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default EngineSettingsTab;
