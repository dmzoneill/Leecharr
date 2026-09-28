import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import type {
  TracepointDefinition,
  TracepointSnapshot,
  DebuggerStatusReport,
} from "../api/types";

export default function DeveloperDebugger() {
  const { t } = useTranslation();
  const [status, setStatus] = useState<DebuggerStatusReport | null>(null);
  const [tracepoints, setTracepoints] = useState<TracepointDefinition[]>([]);
  const [snapshots, setSnapshots] = useState<TracepointSnapshot[]>([]);
  const [selectedSnapshot, setSelectedSnapshot] = useState<TracepointSnapshot | null>(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [newFilePath, setNewFilePath] = useState("src/NzbDrone.Core/Torrents/TorrentService.cs");
  const [newLineNumber, setNewLineNumber] = useState(125);
  const [newCondition, setNewCondition] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchAll = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const [statusData, tpData, snapData] = await Promise.all([
        apiClient.get<DebuggerStatusReport>("/system/developer/debugger/status"),
        apiClient.get<TracepointDefinition[]>("/system/developer/debugger/tracepoints"),
        apiClient.get<TracepointSnapshot[]>("/system/developer/debugger/snapshots?limit=50"),
      ]);
      setStatus(statusData);
      setTracepoints(tpData || []);
      setSnapshots(snapData || []);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(msg || "Failed to load debugger data.");
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchAll();
    const interval = setInterval(fetchAll, 4000);
    return () => clearInterval(interval);
  }, [fetchAll]);

  const handleAddTracepoint = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post<TracepointDefinition>("/system/developer/debugger/tracepoints", {
        filePath: newFilePath,
        lineNumber: Number(newLineNumber),
        condition: newCondition || undefined,
      });
      setShowAddModal(false);
      setNewCondition("");
      fetchAll();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(msg || "Failed to add tracepoint.");
    }
  };

  const handleRemoveTracepoint = async (id: string) => {
    try {
      await apiClient.delete(`/system/developer/debugger/tracepoints/${id}`);
      fetchAll();
    } catch {
      // Ignored
    }
  };

  const handleClearSnapshots = async () => {
    try {
      await apiClient.delete("/system/developer/debugger/snapshots");
      setSnapshots([]);
      setSelectedSnapshot(null);
    } catch {
      // Ignored
    }
  };

  const handleSimulateSnapshot = async () => {
    try {
      const testTp = tracepoints[0]?.id || "manual-probe";
      await apiClient.post("/system/developer/debugger/snapshots", {
        tracepointId: testTp,
        filePath: "src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs",
        lineNumber: 592,
        threadId: 4,
        callStack: "at MonoTorrentDownloadEngine.InitializeEngine()\n   at MonoTorrentDownloadEngine.StartAsync()",
        variablesJson: JSON.stringify({
          activeEngine: "MonoTorrent",
          dhtNodes: 128,
          rateLimits: { downloadKbps: 0, uploadKbps: 0 },
        }, null, 2),
      });
      fetchAll();
    } catch {
      // Ignored
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 border-b border-border pb-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <span>🐞</span> {t("developer.debugger", "Web Debugger & Flight Recorder")}
          </h1>
          <p className="text-sm text-text-secondary mt-1">
            Set non-halting live tracepoints to capture runtime call stacks and local variable snapshots without stopping the server.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={() => setShowAddModal(true)}
            className="px-4 py-2 bg-accent text-white font-semibold text-sm rounded-md shadow hover:opacity-90 flex items-center gap-2"
          >
            <span>➕</span> Add Tracepoint
          </button>
          <button
            onClick={handleSimulateSnapshot}
            className="px-3 py-2 bg-surface text-text border border-border rounded-md text-sm hover:bg-surface-hover"
            title="Inject test snapshot"
          >
            📸 Test Snapshot
          </button>
        </div>
      </div>

      {error && (
        <div className="p-3 bg-red-500/15 border border-red-500/30 text-red-400 rounded-md text-sm">
          {error}
        </div>
      )}

      {/* DAP Status Banner */}
      <div className="p-4 bg-surface rounded-lg border border-border grid grid-cols-2 sm:grid-cols-4 gap-4">
        <div>
          <div className="text-xs uppercase tracking-wider text-text-secondary font-medium">DAP Engine</div>
          <div className="text-base font-bold mt-1 flex items-center gap-2">
            <span
              className={`w-2.5 h-2.5 rounded-full ${
                status?.isDapAvailable ? "bg-emerald-400" : "bg-amber-400"
              }`}
            />
            {status?.isDapAvailable ? "DAP Ready" : "In-Process Trace"}
          </div>
          {status?.dapPath && <div className="text-[11px] text-text-secondary truncate mt-0.5">{status.dapPath}</div>}
        </div>
        <div>
          <div className="text-xs uppercase tracking-wider text-text-secondary font-medium">Active Tracepoints</div>
          <div className="text-xl font-bold mt-1">{status?.activeTracepointsCount ?? tracepoints.length}</div>
        </div>
        <div>
          <div className="text-xs uppercase tracking-wider text-text-secondary font-medium">Captured Snapshots</div>
          <div className="text-xl font-bold mt-1 text-cyan-400">{status?.capturedSnapshotsCount ?? snapshots.length}</div>
        </div>
        <div>
          <div className="text-xs uppercase tracking-wider text-text-secondary font-medium">Protocol</div>
          <div className="text-base font-bold mt-1">FlightRecorder v1.0</div>
        </div>
      </div>

      {/* Tracepoints Table */}
      <div className="space-y-3">
        <h2 className="text-lg font-semibold flex items-center gap-2">
          <span>📍</span> Active Tracepoints ({tracepoints.length})
        </h2>

        {tracepoints.length === 0 ? (
          <div className="p-6 bg-surface rounded-lg border border-border text-center text-text-secondary text-sm">
            No active tracepoints configured. Click "Add Tracepoint" above to set a watch on any C# source line.
          </div>
        ) : (
          <div className="bg-surface rounded-lg border border-border overflow-hidden">
            <table className="w-full text-left text-xs">
              <thead className="bg-surface-hover/50 text-text-secondary uppercase border-b border-border">
                <tr>
                  <th className="p-3">File Path</th>
                  <th className="p-3">Line</th>
                  <th className="p-3">Condition</th>
                  <th className="p-3">Hits</th>
                  <th className="p-3 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60">
                {tracepoints.map((tp) => (
                  <tr key={tp.id} className="hover:bg-surface-hover/30">
                    <td className="p-3 font-mono font-medium text-text">{tp.filePath}</td>
                    <td className="p-3 font-mono text-accent">{tp.lineNumber}</td>
                    <td className="p-3 font-mono text-text-secondary">{tp.condition || "none"}</td>
                    <td className="p-3">
                      <span className="px-2 py-0.5 bg-cyan-500/20 text-cyan-400 rounded font-mono font-bold">
                        {tp.hitCount ?? 0}
                      </span>
                    </td>
                    <td className="p-3 text-right">
                      <button
                        onClick={() => tp.id && handleRemoveTracepoint(tp.id)}
                        className="text-rose-400 hover:text-rose-300 font-medium"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Captured Flight Snapshots */}
      <div className="space-y-3 pt-4 border-t border-border">
        <div className="flex justify-between items-center">
          <h2 className="text-lg font-semibold flex items-center gap-2">
            <span>📸</span> Captured Frame Snapshots ({snapshots.length})
          </h2>
          {snapshots.length > 0 && (
            <button
              onClick={handleClearSnapshots}
              className="text-xs text-text-secondary hover:text-rose-400"
            >
              Clear Snapshots
            </button>
          )}
        </div>

        {snapshots.length === 0 ? (
          <div className="p-6 bg-surface rounded-lg border border-border text-center text-text-secondary text-sm">
            No snapshots captured yet. When code executes through an active tracepoint, frame variables and stack traces appear here automatically.
          </div>
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
            <div className="lg:col-span-5 bg-surface rounded-lg border border-border divide-y divide-border/60 overflow-hidden max-h-[450px] overflow-y-auto">
              {snapshots.map((snap) => (
                <div
                  key={snap.snapshotId}
                  onClick={() => setSelectedSnapshot(snap)}
                  className={`p-3 text-xs cursor-pointer transition-colors ${
                    selectedSnapshot?.snapshotId === snap.snapshotId
                      ? "bg-accent/15 border-l-4 border-accent"
                      : "hover:bg-surface-hover"
                  }`}
                >
                  <div className="font-mono font-medium text-text truncate">
                    {snap.filePath}:{snap.lineNumber}
                  </div>
                  <div className="flex justify-between text-text-secondary text-[11px] mt-1">
                    <span>Thread #{snap.threadId}</span>
                    <span>{snap.timestampUtc ? new Date(snap.timestampUtc).toLocaleTimeString() : ""}</span>
                  </div>
                </div>
              ))}
            </div>

            <div className="lg:col-span-7 bg-surface rounded-lg border border-border p-4 flex flex-col space-y-3">
              <div className="text-xs font-semibold text-text-secondary uppercase tracking-wider">
                Snapshot Frame Inspector
              </div>

              {!selectedSnapshot ? (
                <div className="flex-1 flex items-center justify-center text-text-secondary text-xs min-h-[250px]">
                  Select a captured snapshot on the left to view call stack and local variables.
                </div>
              ) : (
                <div className="space-y-4">
                  <div>
                    <div className="text-xs font-medium text-text mb-1">Local Variables Scope</div>
                    <pre className="p-3 bg-black/50 border border-border rounded font-mono text-xs text-cyan-300 overflow-auto max-h-[200px]">
                      {selectedSnapshot.variablesJson}
                    </pre>
                  </div>

                  <div>
                    <div className="text-xs font-medium text-text mb-1">Call Stack</div>
                    <pre className="p-3 bg-black/50 border border-border rounded font-mono text-xs text-emerald-300 whitespace-pre-wrap overflow-auto max-h-[160px]">
                      {selectedSnapshot.callStack}
                    </pre>
                  </div>
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Add Tracepoint Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-surface border border-border rounded-lg max-w-md w-full p-6 space-y-4 shadow-xl">
            <h3 className="text-lg font-bold text-text">Add Tracepoint</h3>

            <form onSubmit={handleAddTracepoint} className="space-y-4 text-xs">
              <div>
                <label className="block text-text-secondary mb-1">Target File Path</label>
                <input
                  type="text"
                  value={newFilePath}
                  onChange={(e) => setNewFilePath(e.target.value)}
                  className="w-full p-2 bg-surface-hover border border-border rounded text-text font-mono"
                  required
                />
              </div>

              <div>
                <label className="block text-text-secondary mb-1">Line Number</label>
                <input
                  type="number"
                  value={newLineNumber}
                  onChange={(e) => setNewLineNumber(Number(e.target.value))}
                  className="w-full p-2 bg-surface-hover border border-border rounded text-text font-mono"
                  required
                />
              </div>

              <div>
                <label className="block text-text-secondary mb-1">Hit Condition (optional expression)</label>
                <input
                  type="text"
                  placeholder="e.g. torrent.Id > 0"
                  value={newCondition}
                  onChange={(e) => setNewCondition(e.target.value)}
                  className="w-full p-2 bg-surface-hover border border-border rounded text-text font-mono"
                />
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setShowAddModal(false)}
                  className="px-4 py-2 border border-border rounded text-text-secondary hover:text-text"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="px-4 py-2 bg-accent text-white font-semibold rounded hover:opacity-90"
                >
                  Save Tracepoint
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
