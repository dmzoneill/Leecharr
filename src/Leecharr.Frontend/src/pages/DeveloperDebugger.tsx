import React, { useState, useEffect, useCallback, useMemo } from "react";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import type {
  TracepointDefinition,
  TracepointSnapshot,
  DebuggerStatusReport,
  DebuggerSourceFileItem,
  DebuggerSourceCodeResponse,
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
  const [knownFiles, setKnownFiles] = useState<DebuggerSourceFileItem[]>([]);
  const [selectedFilePath, setSelectedFilePath] = useState<string>("src/NzbDrone.Core/Torrents/TorrentService.cs");
  const [sourceCode, setSourceCode] = useState<DebuggerSourceCodeResponse | null>(null);
  const [isLoadingSource, setIsLoadingSource] = useState(false);
  const [fileFilter, setFileFilter] = useState("");
  const [showAddModal, setShowAddModal] = useState(false);
  const [newFilePath, setNewFilePath] = useState("src/NzbDrone.Core/Torrents/TorrentService.cs");
  const [newLineNumber, setNewLineNumber] = useState(100);
  const [newCondition, setNewCondition] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const fetchSource = useCallback(async (path: string) => {
    if (!path) return;
    setIsLoadingSource(true);
    try {
      const data = await apiClient.get<DebuggerSourceCodeResponse>(
        `/system/developer/debugger/source?path=${encodeURIComponent(path)}`
      );
      setSourceCode(data);
    } catch {
      setSourceCode({
        filePath: path,
        content: "// Failed to load source preview.",
        lineCount: 1,
        exists: false,
      });
    } finally {
      setIsLoadingSource(false);
    }
  }, []);

  const fetchAll = useCallback(async () => {
    setIsLoading(true);
    try {
      const [statusData, tpData, snapData, filesData] = await Promise.all([
        apiClient.get<DebuggerStatusReport>("/system/developer/debugger/status"),
        apiClient.get<TracepointDefinition[]>("/system/developer/debugger/tracepoints"),
        apiClient.get<TracepointSnapshot[]>("/system/developer/debugger/snapshots?limit=50"),
        apiClient.get<DebuggerSourceFileItem[]>("/system/developer/debugger/files").catch(() => []),
      ]);
      setStatus(statusData);
      setTracepoints(tpData || []);
      setSnapshots(snapData || []);
      setKnownFiles(filesData || []);
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
    const interval = setInterval(fetchAll, 5000);
    return () => clearInterval(interval);
  }, [fetchAll]);

  useEffect(() => {
    fetchSource(selectedFilePath);
  }, [selectedFilePath, fetchSource]);

  const filteredFiles = useMemo(() => {
    if (!fileFilter) return knownFiles;
    const q = fileFilter.toLowerCase();
    return knownFiles.filter(
      (f) =>
        f.filePath.toLowerCase().includes(q) ||
        f.className.toLowerCase().includes(q) ||
        f.namespace.toLowerCase().includes(q)
    );
  }, [knownFiles, fileFilter]);

  const activeBreakpointsForSelectedFile = useMemo(() => {
    const map = new Map<number, TracepointDefinition>();
    tracepoints.forEach((tp) => {
      if (
        tp.filePath.toLowerCase() === selectedFilePath.toLowerCase() ||
        selectedFilePath.toLowerCase().endsWith(tp.filePath.toLowerCase()) ||
        tp.filePath.toLowerCase().endsWith(selectedFilePath.toLowerCase())
      ) {
        map.set(tp.lineNumber, tp);
      }
    });
    return map;
  }, [tracepoints, selectedFilePath]);

  const handleToggleBreakpoint = async (lineNum: number) => {
    const existing = activeBreakpointsForSelectedFile.get(lineNum);
    setActionMessage(null);

    if (existing && existing.id) {
      try {
        await apiClient.delete(`/system/developer/debugger/tracepoints/${existing.id}`);
        setActionMessage({ text: `Removed breakpoint at line ${lineNum}`, type: "success" });
        fetchAll();
      } catch {
        setActionMessage({ text: "Failed to remove breakpoint.", type: "error" });
      }
    } else {
      try {
        await apiClient.post<TracepointDefinition>("/system/developer/debugger/tracepoints", {
          filePath: selectedFilePath,
          lineNumber: lineNum,
        });
        setActionMessage({ text: `Set breakpoint at line ${lineNum}`, type: "success" });
        fetchAll();
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        setActionMessage({ text: msg || "Failed to set breakpoint.", type: "error" });
      }
    }
  };

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
      setSelectedFilePath(newFilePath);
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
        filePath: selectedFilePath,
        lineNumber: 100,
        threadId: 4,
        callStack: `at ${selectedFilePath.split("/").pop()?.replace(".cs", "")}.Execute()\n   at SystemDeveloperDebuggerController.Simulate()`,
        variablesJson: JSON.stringify(
          {
            activeEngine: "MonoTorrent",
            dhtNodes: 128,
            timestamp: new Date().toISOString(),
            rateLimits: { downloadKbps: 0, uploadKbps: 0 },
          },
          null,
          2
        ),
      });
      setActionMessage({ text: "Simulated tracepoint snapshot injected.", type: "success" });
      fetchAll();
    } catch {
      setActionMessage({ text: "Failed to inject test snapshot.", type: "error" });
    }
  };

  const sourceLines = useMemo(() => {
    return (sourceCode?.content || "").split("\n");
  }, [sourceCode]);

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
            🐞 Web Debugger & Source Code Breakpoints
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Select any C# source file, click line numbers in the gutter to toggle breakpoints/tracepoints, and inspect flight snapshots.
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
            <span>➕</span> Custom Breakpoint
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

      {/* Dynamic Source File Selector Toolbar */}
      <div
        style={{
          backgroundColor: "var(--bg-surface, #1e293b)",
          border: "1px solid var(--border, #334155)",
          borderRadius: "8px",
          padding: "12px 16px",
          marginBottom: "16px",
          display: "flex",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "12px",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "8px", flex: 1, minWidth: "300px" }}>
          <span style={{ fontSize: "0.82rem", fontWeight: 700, color: "#fff", whiteSpace: "nowrap" }}>
            📂 Target Source File:
          </span>

          <select
            value={selectedFilePath}
            onChange={(e) => setSelectedFilePath(e.target.value)}
            style={{
              flex: 1,
              padding: "7px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-primary, #0f172a)",
              color: "#38bdf8",
              fontFamily: "monospace",
              fontSize: "0.82rem",
              outline: "none",
              cursor: "pointer",
            }}
          >
            {filteredFiles.map((file) => (
              <option key={file.filePath} value={file.filePath}>
                {file.className} ({file.filePath})
              </option>
            ))}
          </select>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
          <input
            type="text"
            placeholder="Filter files..."
            value={fileFilter}
            onChange={(e) => setFileFilter(e.target.value)}
            style={{
              padding: "6px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-primary, #0f172a)",
              color: "#fff",
              fontSize: "0.8rem",
              width: "160px",
            }}
          />
          <span style={{ fontSize: "0.75rem", color: "var(--text-secondary, #94a3b8)", fontFamily: "monospace" }}>
            {filteredFiles.length} files
          </span>
        </div>
      </div>

      {/* Main Split Workbench: Source Preview on Left, Tracepoints/Snapshots on Right */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 450px", gap: "16px" }}>
        {/* Left: Code Viewer with Breakpoint Gutter */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            overflow: "hidden",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 340px)",
            minHeight: "500px",
          }}
        >
          <div
            style={{
              padding: "10px 14px",
              backgroundColor: "rgba(0,0,0,0.2)",
              borderBottom: "1px solid var(--border, #334155)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              fontSize: "0.8rem",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
              <span style={{ fontWeight: 700, color: "#fff" }}>
                {selectedFilePath.split("/").pop()}
              </span>
              <span style={{ color: "var(--text-secondary, #94a3b8)", fontFamily: "monospace", fontSize: "0.75rem" }}>
                ({sourceLines.length} lines)
              </span>
            </div>

            <span style={{ color: "var(--text-secondary, #94a3b8)", fontSize: "0.75rem" }}>
              Tip: Click any line number to toggle a breakpoint 🔴
            </span>
          </div>

          <div
            style={{
              flex: 1,
              overflowY: "auto",
              overflowX: "auto",
              backgroundColor: "var(--bg-primary, #0f172a)",
              fontFamily: "monospace",
              fontSize: "0.8rem",
              lineHeight: "1.6",
            }}
          >
            {isLoadingSource ? (
              <div style={{ padding: "40px", textAlign: "center", color: "var(--text-secondary, #94a3b8)" }}>
                Loading source code preview...
              </div>
            ) : sourceLines.length === 0 ? (
              <div style={{ padding: "40px", textAlign: "center", color: "var(--text-secondary, #94a3b8)" }}>
                No source code available for this file.
              </div>
            ) : (
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <tbody>
                  {sourceLines.map((line, idx) => {
                    const lineNum = idx + 1;
                    const bp = activeBreakpointsForSelectedFile.get(lineNum);
                    const isSnapshotLine =
                      selectedSnapshot &&
                      selectedSnapshot.lineNumber === lineNum &&
                      selectedSnapshot.filePath.toLowerCase().endsWith(selectedFilePath.toLowerCase());

                    return (
                      <tr
                        key={lineNum}
                        style={{
                          backgroundColor: isSnapshotLine
                            ? "rgba(59, 130, 246, 0.25)"
                            : bp
                            ? "rgba(239, 68, 68, 0.12)"
                            : "transparent",
                        }}
                      >
                        {/* Gutter: Line Number & Breakpoint Dot */}
                        <td
                          onClick={() => handleToggleBreakpoint(lineNum)}
                          style={{
                            width: "55px",
                            padding: "0 8px 0 10px",
                            textAlign: "right",
                            userSelect: "none",
                            cursor: "pointer",
                            color: bp ? "#f87171" : "var(--text-secondary, #64748b)",
                            borderRight: "1px solid var(--border, #334155)",
                            backgroundColor: "rgba(0,0,0,0.15)",
                            verticalAlign: "top",
                          }}
                          title={bp ? `Active Breakpoint (Hits: ${bp.hitCount}). Click to remove.` : `Click to set breakpoint at line ${lineNum}`}
                        >
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                            <span>{bp ? "🔴" : ""}</span>
                            <span>{lineNum}</span>
                          </div>
                        </td>

                        {/* Code Line Content */}
                        <td
                          style={{
                            padding: "0 14px",
                            whiteSpace: "pre",
                            color: isSnapshotLine ? "#93c5fd" : "#e2e8f0",
                          }}
                        >
                          {line || " "}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </div>
        </div>

        {/* Right: Tracepoints Roster & Frame Snapshot Inspector */}
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          {/* Active Tracepoints List */}
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              overflow: "hidden",
              display: "flex",
              flexDirection: "column",
              maxHeight: "260px",
            }}
          >
            <div
              style={{
                padding: "10px 14px",
                borderBottom: "1px solid var(--border, #334155)",
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <span style={{ fontSize: "0.85rem", fontWeight: 700, color: "#fff" }}>
                Active Breakpoints ({tracepoints.length})
              </span>
            </div>

            <div style={{ flex: 1, overflowY: "auto" }}>
              {tracepoints.length === 0 ? (
                <div style={{ padding: "20px", textAlign: "center", color: "var(--text-secondary, #94a3b8)", fontSize: "0.8rem" }}>
                  No active breakpoints. Click any line number in the source preview to set one.
                </div>
              ) : (
                tracepoints.map((tp) => (
                  <div
                    key={tp.id}
                    role="button"
                    tabIndex={0}
                    onClick={() => {
                      setSelectedFilePath(tp.filePath);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        setSelectedFilePath(tp.filePath);
                      }
                    }}
                    style={{
                      padding: "8px 12px",
                      borderBottom: "1px solid var(--border, #334155)",
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      fontSize: "0.78rem",
                      cursor: "pointer",
                      backgroundColor:
                        selectedFilePath === tp.filePath ? "rgba(59, 130, 246, 0.15)" : "transparent",
                    }}
                  >
                    <div style={{ overflow: "hidden", textOverflow: "ellipsis" }}>
                      <div style={{ fontWeight: 600, color: "#fff" }}>
                        🔴 {tp.filePath.split("/").pop()}:{tp.lineNumber}
                      </div>
                      {tp.condition && (
                        <div style={{ fontSize: "0.7rem", color: "var(--text-secondary, #94a3b8)" }}>
                          When: {tp.condition}
                        </div>
                      )}
                    </div>

                    <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                      <span
                        style={{
                          padding: "2px 6px",
                          borderRadius: "4px",
                          backgroundColor: "rgba(56, 189, 248, 0.2)",
                          color: "#38bdf8",
                          fontFamily: "monospace",
                          fontSize: "0.72rem",
                          fontWeight: 700,
                        }}
                      >
                        {tp.hitCount ?? 0} hits
                      </span>
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          if (tp.id) handleRemoveTracepoint(tp.id);
                        }}
                        style={{
                          background: "transparent",
                          border: "none",
                          color: "#f87171",
                          cursor: "pointer",
                          fontSize: "0.78rem",
                        }}
                      >
                        ✕
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>

          {/* Captured Flight Snapshots Inspector */}
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "14px",
              display: "flex",
              flexDirection: "column",
              flex: 1,
              minHeight: "260px",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "10px",
                borderBottom: "1px solid var(--border, #334155)",
                paddingBottom: "8px",
              }}
            >
              <span style={{ fontSize: "0.85rem", fontWeight: 700, color: "#fff" }}>
                Captured Flight Frame
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
                  Clear ({snapshots.length})
                </button>
              )}
            </div>

            {!selectedSnapshot ? (
              <div
                style={{
                  height: "100%",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  color: "var(--text-secondary, #94a3b8)",
                  fontSize: "0.8rem",
                  textAlign: "center",
                }}
              >
                No captured execution snapshots. Hit a breakpoint to capture flight variables.
              </div>
            ) : (
              <div style={{ flex: 1, overflowY: "auto", display: "flex", flexDirection: "column", gap: "10px" }}>
                <div style={{ fontSize: "0.75rem", color: "var(--text-secondary, #94a3b8)" }}>
                  Captured at: {selectedSnapshot.filePath}:{selectedSnapshot.lineNumber} · Thread #{selectedSnapshot.threadId}
                </div>

                <div>
                  <div style={{ fontSize: "0.72rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600, marginBottom: "4px" }}>
                    Variables Scope
                  </div>
                  <pre
                    style={{
                      margin: 0,
                      padding: "8px",
                      borderRadius: "6px",
                      backgroundColor: "var(--bg-primary, #0f172a)",
                      border: "1px solid var(--border, #334155)",
                      fontFamily: "monospace",
                      fontSize: "0.75rem",
                      color: "#38bdf8",
                      whiteSpace: "pre-wrap",
                      maxHeight: "130px",
                      overflowY: "auto",
                    }}
                  >
                    {selectedSnapshot.variablesJson}
                  </pre>
                </div>

                <div>
                  <div style={{ fontSize: "0.72rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600, marginBottom: "4px" }}>
                    Call Stack
                  </div>
                  <pre
                    style={{
                      margin: 0,
                      padding: "8px",
                      borderRadius: "6px",
                      backgroundColor: "var(--bg-primary, #0f172a)",
                      border: "1px solid var(--border, #334155)",
                      fontFamily: "monospace",
                      fontSize: "0.72rem",
                      color: "#34d399",
                      whiteSpace: "pre-wrap",
                      maxHeight: "100px",
                      overflowY: "auto",
                    }}
                  >
                    {selectedSnapshot.callStack}
                  </pre>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Add Custom Tracepoint Modal */}
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
              maxWidth: "480px",
              width: "100%",
            }}
          >
            <h3 style={{ margin: "0 0 14px 0", fontSize: "1.15rem", fontWeight: 700 }}>
              Add Breakpoint / Tracepoint
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
                  Hit Condition (optional expression)
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
                  Save Breakpoint
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
