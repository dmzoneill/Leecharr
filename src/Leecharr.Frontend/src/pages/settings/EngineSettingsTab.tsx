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

export function EngineSettingsTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const { data: engines } = useTorrentEngines();
  const { data: activeEngineData } = useActiveTorrentEngine();
  const switchMutation = useSwitchTorrentEngine();
  const probeMutation = useProbeTorrentEngine();

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
  const [selectedEngineForSwitch, setSelectedEngineForSwitch] = useState<
    string | null
  >(null);

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
        onSuccess: () => setDirty(false),
      },
    );
  };

  const handleSwitchConfirm = () => {
    if (selectedEngineForSwitch) {
      switchMutation.mutate(
        { engineId: selectedEngineForSwitch, preserveTransfers: true },
        {
          onSuccess: () => {
            setSelectedEngineForSwitch(null);
            update("activeTorrentEngine", selectedEngineForSwitch);
          },
        },
      );
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
                    onClick={() => probeMutation.mutate(engineId)}
                    disabled={probeMutation.isPending}
                    style={{ flex: 1, fontSize: "0.75rem" }}
                  >
                    🔍 Probe Health
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
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSwitchConfirm}
                disabled={switchMutation.isPending}
              >
                {switchMutation.isPending
                  ? "Switching Engine..."
                  : "Confirm Switch"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default EngineSettingsTab;
