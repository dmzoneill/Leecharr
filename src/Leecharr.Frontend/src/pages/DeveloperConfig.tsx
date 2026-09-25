import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import type { DeveloperConfigResponse, DeveloperConfigEntry, DeveloperHostEnvironment } from "../api/types";

export default function DeveloperConfig() {
  const [activeTab, setActiveTab] = useState<"config" | "env" | "host">("config");
  const [entries, setEntries] = useState<DeveloperConfigEntry[]>([]);
  const [hostEnv, setHostEnv] = useState<DeveloperHostEnvironment | null>(null);
  const [unmask, setUnmask] = useState(false);
  const [search, setSearch] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const fetchConfig = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await apiClient.get<DeveloperConfigResponse>(`/system/developer/config?unmask=${unmask}`);
      setEntries(data.entries || []);
      setHostEnv(data.environment || null);
    } catch {
      // error
    } finally {
      setIsLoading(false);
    }
  }, [unmask]);

  useEffect(() => {
    fetchConfig();
  }, [fetchConfig]);

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setActionMessage({ text: `${label} copied to clipboard!`, type: "success" });
  };

  const exportJson = () => {
    const jsonStr = JSON.stringify({ entries, environment: hostEnv }, null, 2);
    const blob = new Blob([jsonStr], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "developer-config-export.json";
    a.click();
    URL.revokeObjectURL(url);
  };

  const filteredEntries = entries.filter((e) =>
    e.key.toLowerCase().includes(search.toLowerCase()) ||
    e.value.toLowerCase().includes(search.toLowerCase())
  );

  const envEntries = hostEnv?.environmentVariables
    ? Object.entries(hostEnv.environmentVariables).filter(([k, v]) =>
        k.toLowerCase().includes(search.toLowerCase()) ||
        v.toLowerCase().includes(search.toLowerCase())
      )
    : [];

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
            ⚙️ Configuration & Environment Matrix
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Inspect effective runtime configuration across database, config files, and host container environment.
          </p>
        </div>

        <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          <button
            onClick={() => setUnmask(!unmask)}
            style={{
              padding: "7px 14px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: unmask ? "#ef4444" : "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.83rem",
              fontWeight: 600,
            }}
          >
            {unmask ? "🔒 Mask Secrets" : "👁 Reveal Secrets"}
          </button>

          <button
            onClick={exportJson}
            style={{
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
            📥 Export JSON
          </button>
        </div>
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

      {/* Tabs & Search */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px", flexWrap: "wrap", gap: "8px" }}>
        <div style={{ display: "flex", gap: "6px" }}>
          <button
            onClick={() => setActiveTab("config")}
            style={{
              padding: "6px 14px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: activeTab === "config" ? "var(--accent, #3b82f6)" : "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.82rem",
              fontWeight: 600,
            }}
          >
            Config Keys ({entries.length})
          </button>
          <button
            onClick={() => setActiveTab("env")}
            style={{
              padding: "6px 14px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: activeTab === "env" ? "var(--accent, #3b82f6)" : "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.82rem",
              fontWeight: 600,
            }}
          >
            Environment Variables ({envEntries.length})
          </button>
          <button
            onClick={() => setActiveTab("host")}
            style={{
              padding: "6px 14px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: activeTab === "host" ? "var(--accent, #3b82f6)" : "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.82rem",
              fontWeight: 600,
            }}
          >
            Host & Runtime Info
          </button>
        </div>

        {activeTab !== "host" && (
          <input
            type="text"
            placeholder="Search key or value..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{
              padding: "6px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "#fff",
              fontSize: "0.8rem",
              width: "240px",
            }}
          />
        )}
      </div>

      {/* Tab 1: Config Keys */}
      {activeTab === "config" && (
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            overflow: "hidden",
          }}
        >
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.82rem", textAlign: "left" }}>
            <thead>
              <tr style={{ backgroundColor: "rgba(0,0,0,0.2)", borderBottom: "1px solid var(--border, #334155)" }}>
                <th style={{ padding: "10px 14px", width: "280px" }}>Configuration Key</th>
                <th style={{ padding: "10px 14px" }}>Resolved Value</th>
                <th style={{ padding: "10px 14px", width: "120px" }}>Source</th>
                <th style={{ padding: "10px 14px", textAlign: "right", width: "80px" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <tr><td colSpan={4} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>Loading configuration...</td></tr>
              ) : filteredEntries.length === 0 ? (
                <tr><td colSpan={4} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>No keys matching search.</td></tr>
              ) : (
                filteredEntries.map((e) => (
                  <tr key={e.key} style={{ borderBottom: "1px solid var(--border, #334155)" }}>
                    <td style={{ padding: "8px 14px", fontWeight: 600, fontFamily: "monospace" }}>
                      {e.key}
                      {e.isSecret && (
                        <span style={{ marginLeft: "6px", fontSize: "0.72rem", color: "#f59e0b" }}>🔒</span>
                      )}
                    </td>
                    <td style={{ padding: "8px 14px", fontFamily: "monospace", color: e.isSecret ? "#94a3b8" : "#fff", wordBreak: "break-all" }}>
                      {e.value || "(empty)"}
                    </td>
                    <td style={{ padding: "8px 14px" }}>
                      <span
                        style={{
                          padding: "2px 6px",
                          borderRadius: "4px",
                          fontSize: "0.72rem",
                          backgroundColor: e.source === "Database" ? "rgba(59, 130, 246, 0.15)" : "rgba(16, 185, 129, 0.15)",
                          color: e.source === "Database" ? "#60a5fa" : "#34d399",
                        }}
                      >
                        {e.source}
                      </span>
                    </td>
                    <td style={{ padding: "8px 14px", textAlign: "right" }}>
                      <button
                        onClick={() => copyToClipboard(e.value, e.key)}
                        style={{
                          padding: "3px 8px",
                          borderRadius: "4px",
                          border: "1px solid var(--border, #334155)",
                          backgroundColor: "var(--bg-primary, #0f172a)",
                          color: "#fff",
                          fontSize: "0.72rem",
                          cursor: "pointer",
                        }}
                      >
                        Copy
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Tab 2: Environment Variables */}
      {activeTab === "env" && (
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            overflow: "hidden",
          }}
        >
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.82rem", textAlign: "left" }}>
            <thead>
              <tr style={{ backgroundColor: "rgba(0,0,0,0.2)", borderBottom: "1px solid var(--border, #334155)" }}>
                <th style={{ padding: "10px 14px", width: "320px" }}>Environment Variable</th>
                <th style={{ padding: "10px 14px" }}>Value</th>
                <th style={{ padding: "10px 14px", textAlign: "right", width: "80px" }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {envEntries.length === 0 ? (
                <tr><td colSpan={3} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>No environment variables matching search.</td></tr>
              ) : (
                envEntries.map(([k, v]) => (
                  <tr key={k} style={{ borderBottom: "1px solid var(--border, #334155)" }}>
                    <td style={{ padding: "8px 14px", fontWeight: 600, fontFamily: "monospace", color: "#38bdf8" }}>{k}</td>
                    <td style={{ padding: "8px 14px", fontFamily: "monospace", color: "#fff", wordBreak: "break-all" }}>{v}</td>
                    <td style={{ padding: "8px 14px", textAlign: "right" }}>
                      <button
                        onClick={() => copyToClipboard(v, k)}
                        style={{
                          padding: "3px 8px",
                          borderRadius: "4px",
                          border: "1px solid var(--border, #334155)",
                          backgroundColor: "var(--bg-primary, #0f172a)",
                          color: "#fff",
                          fontSize: "0.72rem",
                          cursor: "pointer",
                        }}
                      >
                        Copy
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Tab 3: Host & Runtime Environment */}
      {activeTab === "host" && hostEnv && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "16px",
            }}
          >
            <h3 style={{ margin: "0 0 12px 0", fontSize: "0.95rem", fontWeight: 600 }}>System & Process</h3>
            <div style={{ display: "flex", flexDirection: "column", gap: "8px", fontSize: "0.82rem" }}>
              <div><strong>Operating System:</strong> {hostEnv.operatingSystem}</div>
              <div><strong>OS Architecture:</strong> {hostEnv.osArchitecture}</div>
              <div><strong>Process Architecture:</strong> {hostEnv.processArchitecture}</div>
              <div><strong>Framework:</strong> {hostEnv.frameworkDescription}</div>
              <div><strong>Host Name:</strong> {hostEnv.hostName}</div>
              <div><strong>Process ID:</strong> {hostEnv.processId}</div>
              <div><strong>Uptime:</strong> {Math.floor(hostEnv.processUptimeSeconds / 60)} minutes ({hostEnv.processUptimeSeconds}s)</div>
              <div><strong>Working Set Memory:</strong> {Math.round(hostEnv.workingSetBytes / (1024 * 1024))} MB</div>
            </div>
          </div>

          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "16px",
            }}
          >
            <h3 style={{ margin: "0 0 12px 0", fontSize: "0.95rem", fontWeight: 600 }}>Paths & Directories</h3>
            <div style={{ display: "flex", flexDirection: "column", gap: "8px", fontSize: "0.82rem" }}>
              <div><strong>App Data Directory:</strong> <code>{hostEnv.appDataDirectory}</code></div>
              <div><strong>Temp Directory:</strong> <code>{hostEnv.tempDirectory}</code></div>
              <div><strong>Current Directory:</strong> <code>{hostEnv.currentDirectory}</code></div>
              <div><strong>Process Start (UTC):</strong> {new Date(hostEnv.processStartTimeUtc).toLocaleString()}</div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
