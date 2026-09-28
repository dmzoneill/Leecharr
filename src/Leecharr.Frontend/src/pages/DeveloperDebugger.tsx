import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import type {
  TracepointDefinition,
  TracepointSnapshot,
  DebuggerStatusReport,
} from "../api/types";

const TRACEPOINT_PRESETS = [
  {
    name: "TorrentService.GetAll",
    filePath: "src/NzbDrone.Core/Torrents/TorrentService.cs",
    lineNumber: 100,
    condition: "torrents.Count > 0",
  },
  {
    name: "MonoTorrent Engine Init",
    filePath: "src/NzbDrone.Core/BitTorrent/MonoTorrentDownloadEngine.cs",
    lineNumber: 592,
    condition: "this.engine != null",
  },
  {
    name: "Storage Path Resolution",
    filePath: "src/NzbDrone.Core/Download/StoragePathService.cs",
    lineNumber: 45,
    condition: "",
  },
  {
    name: "Tracker Announce Probe",
    filePath: "src/NzbDrone.Core/Trackers/TrackerBoostService.cs",
    lineNumber: 70,
    condition: "",
  },
  {
    name: "RSS Rule Evaluation",
    filePath: "src/NzbDrone.Core/Indexers/RssSyncService.cs",
    lineNumber: 90,
    condition: "",
  },
];

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
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const fetchAll = useCallback(async () => {
    setIsLoading(true);
    try {
      const [statusData, tpData, snapData] = await Promise.all([
        apiClient.get<DebuggerStatusReport>("/system/developer/debugger/status"),
        apiClient.get<TracepointDefinition[]>("/system/developer/debugger/tracepoints"),
        apiClient.get<TracepointSnapshot[]>("/system/developer/debugger/snapshots?limit=50"),
      ]);
      setStatus(statusData);
      setTracepoints(tpData || []);
      setSnapshots(snapData || []);
      if (snapData && snapData.length > 0 && !selectedSnapshot) {
        setSelectedSnapshot(snapData[0]);
      }
    } catch {
      setActionMessage({ text: "Failed to load debugger data.", type: "error" });
    } finally {
      setIsLoading(false);
    }
  }, [selectedSnapshot]);

  useEffect(() => {
    fetchAll();
    const interval = setInterval(fetchAll, 4000);
    return () => clearInterval(interval);
  }, [fetchAll]);

  const handleAddTracepoint = async (e: React.FormEvent) => {
    e.preventDefault();
    setActionMessage(null);
    try {
      await apiClient.post<TracepointDefinition>("/system/developer/debugger/tracepoints", {
        filePath: newFilePath,
        lineNumber: Number(newLineNumber),
        condition: newCondition || undefined,
      });
      setShowAddModal(false);
      setNewCondition("");
      setActionMessage({ text: `Tracepoint added at ${newFilePath}:${newLineNumber}`, type: "success" });
      fetchAll();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setActionMessage({ text: msg || "Failed to add tracepoint.", type: "error" });
    }
  };

  const handleRemoveTracepoint = async (id: string) => {
    try {
      await apiClient.delete(`/system/developer/debugger/tracepoints/${id}`);
      setActionMessage({ text: "Tracepoint removed.", type: "success" });
      fetchAll();
    } catch {
      setActionMessage({ text: "Failed to remove tracepoint.", type: "error" });
    }
  };

  const handleClearSnapshots = async () => {
    try {
      await apiClient.delete("/system/developer/debugger/snapshots");
      setSnapshots([]);
      setSelectedSnapshot(null);
      setActionMessage({ text: "Snapshots cleared.", type: "success" });
    } catch {
      setActionMessage({ text: "Failed to clear snapshots.", type: "error" });
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
      setActionMessage({ text: "Simulated tracepoint snapshot injected.", type: "success" });
      fetchAll();
    } catch {
      setActionMessage({ text: "Failed to inject test snapshot.", type: "error" });
    }
  };

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Top Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "12px",
          marginBottom: "16px",
        }}
      >
        <div>
          <h2 style={{ margin: "0 0 4px 0", fontSize: "1.4rem", fontWeight: 700 }}>
            🐞 Web Debugger & Flight Recorder
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Non-halting live tracepoints to capture runtime call stacks and local variable snapshots without stopping the server.
          </p>
        </div>

        <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" }}>
          <button
            onClick={() => setShowAddModal(true)}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "6px",
              padding: "7px 14px",
              borderRadius: "6px",
              border: "none",
              backgroundColor: "var(--accent, #3b82f6)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.83rem",
              fontWeight: 600,
            }}
          >
            <span>➕</span> Add Tracepoint
          </button>

          <button
            onClick={handleSimulateSnapshot}
            style={{
              padding: "7px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "var(--text-secondary, #94a3b8)",
              cursor: "pointer",
              fontSize: "0.83rem",
            }}
            title="Inject test snapshot"
          >
            📸 Test Snapshot
          </button>

          <button
            onClick={fetchAll}
            style={{
              padding: "7px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.83rem",
            }}
          >
            🔄 Refresh
          </button>
        </div>
      </div>

      {actionMessage && (
        <div
          style={{
            padding: "10px 14px",
            borderRadius: "6px",
            marginBottom: "16px",
            fontSize: "0.85rem",
            backgroundColor: actionMessage.type === "success" ? "rgba(16, 185, 129, 0.15)" : "rgba(239, 68, 68, 0.15)",
            border: `1px solid ${actionMessage.type === "success" ? "#10b981" : "#ef4444"}`,
            color: actionMessage.type === "success" ? "#34d399" : "#f87171",
          }}
        >
          {actionMessage.text}
        </div>
      )}

      {/* DAP Status Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
          gap: "12px",
          marginBottom: "16px",
        }}
      >
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600 }}>
            DAP Engine
          </div>
          <div style={{ fontSize: "1.1rem", fontWeight: 700, marginTop: "4px", display: "flex", alignItems: "center", gap: "8px" }}>
            <span
              style={{
                width: "10px",
                height: "10px",
                borderRadius: "50%",
                backgroundColor: status?.isDapAvailable ? "#34d399" : "#fbbf24",
              }}
            />
            {status?.isDapAvailable ? "DAP Ready" : "In-Process Mode"}
          </div>
          {status?.dapPath && (
            <div style={{ fontSize: "0.72rem", color: "var(--text-secondary, #94a3b8)", marginTop: "2px", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
              {status.dapPath}
            </div>
          )}
        </div>

        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600 }}>
            Active Tracepoints
          </div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700, marginTop: "4px" }}>
            {status?.activeTracepointsCount ?? tracepoints.length}
          </div>
        </div>

        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "#38bdf8", fontWeight: 600 }}>
            Captured Snapshots
          </div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700, marginTop: "4px", color: "#38bdf8" }}>
            {status?.capturedSnapshotsCount ?? snapshots.length}
          </div>
        </div>

        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600 }}>
            Protocol Version
          </div>
          <div style={{ fontSize: "1.1rem", fontWeight: 700, marginTop: "6px" }}>
            FlightRecorder v1.0
          </div>
        </div>
      </div>

      {/* Active Tracepoints Table */}
      <div
        style={{
          backgroundColor: "var(--bg-surface, #1e293b)",
          border: "1px solid var(--border, #334155)",
          borderRadius: "8px",
          overflow: "hidden",
          marginBottom: "16px",
        }}
      >
        <div style={{ padding: "12px 14px", borderBottom: "1px solid var(--border, #334155)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <span style={{ fontSize: "0.9rem", fontWeight: 700, color: "#fff" }}>
            Active Tracepoints ({tracepoints.length})
          </span>
        </div>

        {tracepoints.length === 0 ? (
          <div style={{ padding: "24px", textAlign: "center", color: "var(--text-secondary, #94a3b8)", fontSize: "0.83rem" }}>
            No active tracepoints configured. Click "Add Tracepoint" above to set a watch on any C# source line.
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.82rem", textAlign: "left" }}>
            <thead>
              <tr style={{ backgroundColor: "rgba(0,0,0,0.2)", borderBottom: "1px solid var(--border, #334155)", color: "var(--text-secondary, #94a3b8)" }}>
                <th style={{ padding: "8px 14px" }}>File Path</th>
                <th style={{ padding: "8px 14px", width: "80px" }}>Line</th>
                <th style={{ padding: "8px 14px" }}>Hit Condition</th>
                <th style={{ padding: "8px 14px", width: "90px" }}>Hit Count</th>
                <th style={{ padding: "8px 14px", textAlign: "right", width: "90px" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {tracepoints.map((tp) => (
                <tr key={tp.id} style={{ borderBottom: "1px solid var(--border, #334155)" }}>
                  <td style={{ padding: "8px 14px", fontFamily: "monospace", color: "#fff", fontWeight: 600 }}>{tp.filePath}</td>
                  <td style={{ padding: "8px 14px", fontFamily: "monospace", color: "var(--accent, #3b82f6)" }}>{tp.lineNumber}</td>
                  <td style={{ padding: "8px 14px", fontFamily: "monospace", color: "var(--text-secondary, #94a3b8)" }}>{tp.condition || "none"}</td>
                  <td style={{ padding: "8px 14px" }}>
                    <span
                      style={{
                        padding: "2px 8px",
                        borderRadius: "4px",
                        backgroundColor: "rgba(56, 189, 248, 0.2)",
                        color: "#38bdf8",
                        fontFamily: "monospace",
                        fontWeight: 700,
                        fontSize: "0.75rem",
                      }}
                    >
                      {tp.hitCount ?? 0}
                    </span>
                  </td>
                  <td style={{ padding: "8px 14px", textAlign: "right" }}>
                    <button
                      onClick={() => tp.id && handleRemoveTracepoint(tp.id)}
                      style={{
                        background: "transparent",
                        border: "none",
                        color: "#f87171",
                        cursor: "pointer",
                        fontSize: "0.78rem",
                        fontWeight: 600,
                      }}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Split Snapshots View */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 500px", gap: "16px" }}>
        {/* Left: Snapshots List */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            overflow: "hidden",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 450px)",
            minHeight: "350px",
          }}
        >
          <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border, #334155)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <span style={{ fontSize: "0.85rem", fontWeight: 700, color: "#fff" }}>
              Captured Frame Snapshots ({snapshots.length})
            </span>
            {snapshots.length > 0 && (
              <button
                onClick={handleClearSnapshots}
                style={{
                  background: "transparent",
                  border: "none",
                  color: "#f87171",
                  cursor: "pointer",
                  fontSize: "0.75rem",
                }}
              >
                Clear
              </button>
            )}
          </div>

          <div style={{ flex: 1, overflowY: "auto" }}>
            {snapshots.length === 0 ? (
              <div style={{ padding: "24px", textAlign: "center", color: "var(--text-secondary, #94a3b8)", fontSize: "0.83rem" }}>
                No frame snapshots captured yet.
              </div>
            ) : (
              snapshots.map((snap) => {
                const isSelected = selectedSnapshot?.snapshotId === snap.snapshotId;
                return (
                  <div
                    key={snap.snapshotId}
                    onClick={() => setSelectedSnapshot(snap)}
                    style={{
                      padding: "10px 14px",
                      borderBottom: "1px solid var(--border, #334155)",
                      backgroundColor: isSelected ? "rgba(59, 130, 246, 0.15)" : "transparent",
                      cursor: "pointer",
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      fontSize: "0.8rem",
                    }}
                  >
                    <div>
                      <div style={{ fontFamily: "monospace", fontWeight: 600, color: "#fff" }}>
                        {snap.filePath}:{snap.lineNumber}
                      </div>
                      <div style={{ fontSize: "0.72rem", color: "var(--text-secondary, #94a3b8)", marginTop: "2px" }}>
                        Thread #{snap.threadId}
                      </div>
                    </div>
                    <div style={{ color: "var(--text-secondary, #94a3b8)", fontFamily: "monospace", fontSize: "0.75rem" }}>
                      {snap.timestampUtc ? new Date(snap.timestampUtc).toLocaleTimeString() : ""}
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>

        {/* Right: Snapshot Detail Frame Inspector */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "16px",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 450px)",
            minHeight: "350px",
            overflow: "hidden",
          }}
        >
          <div style={{ fontSize: "0.85rem", fontWeight: 700, color: "#fff", marginBottom: "12px", borderBottom: "1px solid var(--border, #334155)", paddingBottom: "8px" }}>
            Frame Inspector
          </div>

          {!selectedSnapshot ? (
            <div style={{ height: "100%", display: "flex", alignItems: "center", justifyContent: "center", color: "var(--text-secondary, #94a3b8)", fontSize: "0.85rem" }}>
              Select a snapshot on the left to inspect call stack and variables.
            </div>
          ) : (
            <div style={{ flex: 1, minHeight: 0, overflowY: "auto", display: "flex", flexDirection: "column", gap: "12px" }}>
              <div>
                <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600, marginBottom: "4px" }}>
                  Local Variables Scope
                </div>
                <pre
                  style={{
                    margin: 0,
                    padding: "10px",
                    borderRadius: "6px",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    border: "1px solid var(--border, #334155)",
                    fontFamily: "monospace",
                    fontSize: "0.78rem",
                    color: "#38bdf8",
                    whiteSpace: "pre-wrap",
                    overflowX: "auto",
                    maxHeight: "160px",
                  }}
                >
                  {selectedSnapshot.variablesJson}
                </pre>
              </div>

              <div>
                <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600, marginBottom: "4px" }}>
                  Call Stack
                </div>
                <pre
                  style={{
                    margin: 0,
                    padding: "10px",
                    borderRadius: "6px",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    border: "1px solid var(--border, #334155)",
                    fontFamily: "monospace",
                    fontSize: "0.75rem",
                    color: "#34d399",
                    whiteSpace: "pre-wrap",
                    overflowX: "auto",
                    maxHeight: "140px",
                  }}
                >
                  {selectedSnapshot.callStack}
                </pre>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Add Tracepoint Modal */}
      {showAddModal && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "rgba(0, 0, 0, 0.65)",
            backdropFilter: "blur(4px)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
            padding: "16px",
          }}
        >
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "20px",
              maxWidth: "460px",
              width: "100%",
            }}
          >
            <h3 style={{ margin: "0 0 14px 0", fontSize: "1.15rem", fontWeight: 700 }}>
              Add Source Tracepoint
            </h3>

            <div style={{ marginBottom: "12px" }}>
              <div style={{ fontSize: "0.75rem", color: "var(--text-secondary, #94a3b8)", marginBottom: "6px", fontWeight: 600 }}>
                Quick Presets:
              </div>
              <div style={{ display: "flex", gap: "6px", flexWrap: "wrap" }}>
                {TRACEPOINT_PRESETS.map((p) => (
                  <button
                    key={p.name}
                    type="button"
                    onClick={() => {
                      setNewFilePath(p.filePath);
                      setNewLineNumber(p.lineNumber);
                      setNewCondition(p.condition);
                    }}
                    style={{
                      padding: "3px 8px",
                      borderRadius: "4px",
                      fontSize: "0.72rem",
                      border: "1px solid var(--border, #334155)",
                      backgroundColor: newFilePath === p.filePath && newLineNumber === p.lineNumber ? "var(--accent, #3b82f6)" : "var(--bg-primary, #0f172a)",
                      color: "#fff",
                      cursor: "pointer",
                    }}
                  >
                    {p.name}
                  </button>
                ))}
              </div>
            </div>

            <form onSubmit={handleAddTracepoint} style={{ display: "flex", flexDirection: "column", gap: "12px", fontSize: "0.83rem" }}>
              <div>
                <label style={{ display: "block", color: "var(--text-secondary, #94a3b8)", marginBottom: "4px" }}>
                  Target File Path
                </label>
                <input
                  type="text"
                  value={newFilePath}
                  onChange={(e) => setNewFilePath(e.target.value)}
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    color: "#fff",
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                    boxSizing: "border-box",
                  }}
                  required
                />
              </div>

              <div>
                <label style={{ display: "block", color: "var(--text-secondary, #94a3b8)", marginBottom: "4px" }}>
                  Line Number
                </label>
                <input
                  type="number"
                  value={newLineNumber}
                  onChange={(e) => setNewLineNumber(Number(e.target.value))}
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    color: "#fff",
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                    boxSizing: "border-box",
                  }}
                  required
                />
              </div>

              <div>
                <label style={{ display: "block", color: "var(--text-secondary, #94a3b8)", marginBottom: "4px" }}>
                  Hit Condition (optional C# expression)
                </label>
                <input
                  type="text"
                  placeholder="e.g. torrent.Id > 0"
                  value={newCondition}
                  onChange={(e) => setNewCondition(e.target.value)}
                  style={{
                    width: "100%",
                    padding: "8px 10px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    color: "#fff",
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                    boxSizing: "border-box",
                  }}
                />
              </div>

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "12px" }}>
                <button
                  type="button"
                  onClick={() => setShowAddModal(false)}
                  style={{
                    padding: "7px 14px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "transparent",
                    color: "var(--text-secondary, #94a3b8)",
                    cursor: "pointer",
                    fontSize: "0.83rem",
                  }}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  style={{
                    padding: "7px 16px",
                    borderRadius: "6px",
                    border: "none",
                    backgroundColor: "var(--accent, #3b82f6)",
                    color: "#fff",
                    cursor: "pointer",
                    fontSize: "0.83rem",
                    fontWeight: 600,
                  }}
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
