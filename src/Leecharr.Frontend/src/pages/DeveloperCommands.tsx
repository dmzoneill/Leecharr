import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import type {
  DeveloperCommandDescriptor,
  DeveloperCommandHistoryItem,
  DeveloperCommandsResponse,
} from "../api/types";

export default function DeveloperCommands() {
  const [commands, setCommands] = useState<DeveloperCommandDescriptor[]>([]);
  const [history, setHistory] = useState<DeveloperCommandHistoryItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [selectedCommand, setSelectedCommand] = useState<DeveloperCommandDescriptor | null>(null);
  const [formParams, setFormParams] = useState<Record<string, any>>({});
  const [isExecuting, setIsExecuting] = useState(false);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const fetchCommands = useCallback(async () => {
    try {
      const data = await apiClient.get<DeveloperCommandsResponse>("/system/developer/commands");
      setCommands(data.commands || []);
      setHistory(data.recentHistory || []);
    } catch {
      // background poll failure
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchCommands();
    const interval = setInterval(fetchCommands, 3500);
    return () => clearInterval(interval);
  }, [fetchCommands]);

  const openExecuteModal = (cmd: DeveloperCommandDescriptor) => {
    setSelectedCommand(cmd);
    const initialParams: Record<string, any> = {};
    cmd.properties.forEach((p) => {
      initialParams[p.name] = p.defaultValue ?? (p.type === "Boolean" ? false : p.type === "Int32" || p.type === "Int64" ? 0 : "");
    });
    setFormParams(initialParams);
  };

  const handleExecute = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedCommand) return;
    setIsExecuting(true);
    setActionMessage(null);
    try {
      await apiClient.post("/system/developer/commands/execute", {
        commandName: selectedCommand.name,
        parameters: formParams,
      });
      setActionMessage({ text: `Command '${selectedCommand.name}' dispatched successfully to queue.`, type: "success" });
      setSelectedCommand(null);
      fetchCommands();
    } catch (err: any) {
      setActionMessage({ text: err?.message || "Failed to execute command.", type: "error" });
    } finally {
      setIsExecuting(false);
    }
  };

  const filteredCommands = commands.filter((c) =>
    c.name.toLowerCase().includes(search.toLowerCase()) ||
    c.description.toLowerCase().includes(search.toLowerCase())
  );

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
            ⚡ Command Console & Background Tasks
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Inspect, configure, and dynamically dispatch any registered <code>Command</code> onto the background worker.
          </p>
        </div>

        <button
          onClick={fetchCommands}
          style={{
            padding: "7px 14px",
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

      {actionMessage && (
        <div
          style={{
            padding: "10px 14px",
            borderRadius: "6px",
            marginBottom: "12px",
            fontSize: "0.85rem",
            backgroundColor: actionMessage.type === "success" ? "rgba(16, 185, 129, 0.15)" : "rgba(239, 68, 68, 0.15)",
            border: `1px solid ${actionMessage.type === "success" ? "#10b981" : "#ef4444"}`,
            color: actionMessage.type === "success" ? "#34d399" : "#f87171",
          }}
        >
          {actionMessage.text}
        </div>
      )}

      {/* Split View: Command Catalog & Live Execution History */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
        {/* Left: Command Catalog */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "16px",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 270px)",
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
            <h3 style={{ margin: 0, fontSize: "1rem", fontWeight: 600 }}>Command Catalog ({commands.length})</h3>
            <input
              type="text"
              placeholder="Search commands..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              style={{
                padding: "6px 10px",
                borderRadius: "6px",
                border: "1px solid var(--border, #334155)",
                backgroundColor: "var(--bg-primary, #0f172a)",
                color: "#fff",
                fontSize: "0.8rem",
                width: "200px",
              }}
            />
          </div>

          <div style={{ flex: 1, overflowY: "auto", display: "flex", flexDirection: "column", gap: "8px" }}>
            {isLoading && commands.length === 0 ? (
              <div style={{ textAlign: "center", padding: "20px", color: "var(--text-secondary)" }}>
                Loading command catalog...
              </div>
            ) : filteredCommands.length === 0 ? (
              <div style={{ textAlign: "center", padding: "20px", color: "var(--text-secondary)" }}>
                No commands matching search.
              </div>
            ) : (
              filteredCommands.map((cmd) => (
                <div
                  key={cmd.name}
                  style={{
                    padding: "12px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    gap: "12px",
                  }}
                >
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 600, fontSize: "0.88rem", color: "var(--text-primary, #f8fafc)" }}>
                      {cmd.name}
                    </div>
                    <div style={{ fontSize: "0.78rem", color: "var(--text-secondary, #94a3b8)", marginTop: "2px" }}>
                      {cmd.description}
                    </div>
                    {cmd.properties.length > 0 && (
                      <div style={{ display: "flex", gap: "4px", flexWrap: "wrap", marginTop: "6px" }}>
                        {cmd.properties.map((p) => (
                          <span
                            key={p.name}
                            style={{
                              padding: "2px 6px",
                              borderRadius: "4px",
                              backgroundColor: "rgba(255,255,255,0.06)",
                              fontSize: "0.72rem",
                              fontFamily: "monospace",
                              color: "#cbd5e1",
                            }}
                          >
                            {p.name}: {p.type}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>

                  <button
                    onClick={() => openExecuteModal(cmd)}
                    style={{
                      padding: "6px 12px",
                      borderRadius: "6px",
                      border: "none",
                      backgroundColor: "var(--accent, #3b82f6)",
                      color: "#fff",
                      fontSize: "0.78rem",
                      fontWeight: 600,
                      cursor: "pointer",
                      whiteSpace: "nowrap",
                    }}
                  >
                    ▶ Dispatch
                  </button>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Right: Live Command Queue & History */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "16px",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 270px)",
          }}
        >
          <h3 style={{ margin: "0 0 12px 0", fontSize: "1rem", fontWeight: 600 }}>
            Execution History ({history.length})
          </h3>

          <div style={{ flex: 1, overflowY: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.8rem", textAlign: "left" }}>
              <thead>
                <tr style={{ backgroundColor: "rgba(0,0,0,0.2)", borderBottom: "1px solid var(--border, #334155)" }}>
                  <th style={{ padding: "8px 10px" }}>Command</th>
                  <th style={{ padding: "8px 10px" }}>Status</th>
                  <th style={{ padding: "8px 10px" }}>Queued (UTC)</th>
                  <th style={{ padding: "8px 10px" }}>Duration</th>
                  <th style={{ padding: "8px 10px" }}>Result Message</th>
                </tr>
              </thead>
              <tbody>
                {history.length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>
                      No recent commands executed.
                    </td>
                  </tr>
                ) : (
                  history.map((item) => {
                    const isSuccess = item.status === "Completed" || item.status === "Successful";
                    const isRunning = item.status === "Running";
                    const isFailed = item.status === "Failed";
                    return (
                      <tr key={item.id} style={{ borderBottom: "1px solid var(--border, #334155)" }}>
                        <td style={{ padding: "8px 10px", fontWeight: 600 }}>{item.name}</td>
                        <td style={{ padding: "8px 10px" }}>
                          <span
                            style={{
                              padding: "2px 6px",
                              borderRadius: "4px",
                              fontSize: "0.72rem",
                              fontWeight: 600,
                              backgroundColor: isRunning
                                ? "rgba(245, 158, 11, 0.2)"
                                : isSuccess
                                ? "rgba(16, 185, 129, 0.2)"
                                : isFailed
                                ? "rgba(239, 68, 68, 0.2)"
                                : "rgba(148, 163, 184, 0.2)",
                              color: isRunning
                                ? "#fbbf24"
                                : isSuccess
                                ? "#34d399"
                                : isFailed
                                ? "#f87171"
                                : "#cbd5e1",
                            }}
                          >
                            {item.status}
                          </span>
                        </td>
                        <td style={{ padding: "8px 10px", color: "var(--text-secondary)", fontFamily: "monospace" }}>
                          {new Date(item.queuedAt).toLocaleTimeString()}
                        </td>
                        <td style={{ padding: "8px 10px", color: "var(--text-secondary)" }}>
                          {item.durationMs > 0 ? `${item.durationMs}ms` : "-"}
                        </td>
                        <td
                          style={{
                            padding: "8px 10px",
                            color: "var(--text-secondary)",
                            maxWidth: "180px",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {item.message || "-"}
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* Dynamic Command Parameter Dispatch Modal */}
      {selectedCommand && (
        <div
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0,0,0,0.6)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 9999,
          }}
        >
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "20px",
              width: "520px",
              maxWidth: "90%",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "14px" }}>
              <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 700 }}>
                Execute: {selectedCommand.name}
              </h3>
              <button
                onClick={() => setSelectedCommand(null)}
                style={{ background: "none", border: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "1.2rem" }}
              >
                ✕
              </button>
            </div>

            <p style={{ margin: "0 0 14px 0", fontSize: "0.82rem", color: "var(--text-secondary)" }}>
              {selectedCommand.description}
            </p>

            <form onSubmit={handleExecute}>
              {selectedCommand.properties.length === 0 ? (
                <div style={{ padding: "14px", backgroundColor: "var(--bg-primary, #0f172a)", borderRadius: "6px", marginBottom: "16px", fontSize: "0.82rem", color: "var(--text-secondary)" }}>
                  This command takes no parameters. It will execute with default system options.
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "10px", marginBottom: "16px", maxHeight: "300px", overflowY: "auto" }}>
                  {selectedCommand.properties.map((p) => (
                    <div key={p.name}>
                      <label style={{ display: "block", fontSize: "0.8rem", fontWeight: 600, marginBottom: "4px" }}>
                        {p.name} <span style={{ color: "var(--text-secondary)", fontWeight: 400 }}>({p.type})</span>
                      </label>
                      {p.type === "Boolean" ? (
                        <input
                          type="checkbox"
                          checked={!!formParams[p.name]}
                          onChange={(e) => setFormParams({ ...formParams, [p.name]: e.target.checked })}
                        />
                      ) : (
                        <input
                          type={p.type === "Int32" || p.type === "Int64" ? "number" : "text"}
                          value={formParams[p.name] ?? ""}
                          onChange={(e) => setFormParams({ ...formParams, [p.name]: p.type.startsWith("Int") ? parseInt(e.target.value, 10) || 0 : e.target.value })}
                          style={{
                            width: "100%",
                            padding: "8px 10px",
                            borderRadius: "6px",
                            border: "1px solid var(--border, #334155)",
                            backgroundColor: "var(--bg-primary, #0f172a)",
                            color: "#fff",
                            fontSize: "0.82rem",
                          }}
                        />
                      )}
                    </div>
                  ))}
                </div>
              )}

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px" }}>
                <button
                  type="button"
                  onClick={() => setSelectedCommand(null)}
                  style={{
                    padding: "8px 14px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "transparent",
                    color: "var(--text-secondary)",
                    cursor: "pointer",
                    fontSize: "0.85rem",
                  }}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isExecuting}
                  style={{
                    padding: "8px 16px",
                    borderRadius: "6px",
                    border: "none",
                    backgroundColor: "var(--accent, #3b82f6)",
                    color: "#fff",
                    cursor: isExecuting ? "not-allowed" : "pointer",
                    fontSize: "0.85rem",
                    fontWeight: 600,
                  }}
                >
                  {isExecuting ? "Dispatching..." : "Dispatch Command"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
