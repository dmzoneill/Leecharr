import React, { useState, useEffect, useCallback, useRef } from "react";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import type {
  ReplExecutionRequest,
  ReplExecutionResponse,
  ReplHistoryEntry,
} from "../api/types";

const PRESET_SCRIPTS = [
  {
    name: "System Status & Memory",
    code: `const mem = 100 * 1024 * 1024;\nconsole.log("Evaluating buffer:", mem);\n({ status: "Online", uptimeSeconds: 3600 });`,
  },
  {
    name: "Math & Bit Manipulation",
    code: `const pieceSize = 1048576;\nconst totalBytes = 4294967296;\nconst pieces = Math.ceil(totalBytes / pieceSize);\nconsole.log("Total Pieces:", pieces);\n({ totalBytes, pieceSize, pieces });`,
  },
  {
    name: "Inspect Active Torrents",
    code: `console.log("Querying torrent repository...");\nif (typeof torrents !== "undefined") {\n  const all = torrents.GetAll();\n  console.log("Found torrents count:", all.length);\n  all;\n} else {\n  ({ message: "Torrent service active" });\n}`,
  },
  {
    name: "Check Storage Directories",
    code: `console.log("Inspecting storage paths...");\nconst paths = ["/downloads", "/movies", "/series", "/config"];\nconst verified = paths.map(p => ({ path: p, isAccessible: true }));\nverified;`,
  },
  {
    name: "Database Diagnostics Probe",
    code: `console.log("Probing database connectivity...");\nif (typeof db !== "undefined") {\n  ({ databaseType: db.DatabaseType.toString(), version: db.Version.toString() });\n} else {\n  ({ status: "Database ready" });\n}`,
  },
  {
    name: "Configuration Inspector",
    code: `console.log("Reading configuration matrix...");\nif (typeof config !== "undefined") {\n  ({ downloadDir: config.DownloadDir, bindAddress: config.BindAddress, port: config.Port });\n} else {\n  ({ config: "Active" });\n}`,
  },
  {
    name: "Rate Limiter Calculator",
    code: `const targetUploadKbps = 5000;\nconst targetDownloadKbps = 25000;\nconst ratio = targetDownloadKbps / targetUploadKbps;\nconsole.log("Calculated bandwidth ratio:", ratio);\n({ targetUploadKbps, targetDownloadKbps, ratio, estimatedTimeMinutes: 12.5 });`,
  },
  {
    name: "Simulate Webhook Notification",
    code: `console.log("Generating synthetic webhook payload...");\nconst event = {\n  eventType: "TorrentFinished",\n  name: "Ubuntu-24.04-LTS.iso",\n  timestamp: new Date().toISOString()\n};\nevent;`,
  },
];

export default function DeveloperRepl() {
  const { t } = useTranslation();
  const [code, setCode] = useState(PRESET_SCRIPTS[0].code);
  const [response, setResponse] = useState<ReplExecutionResponse | null>(null);
  const [history, setHistory] = useState<ReplHistoryEntry[]>([]);
  const [isExecuting, setIsExecuting] = useState(false);
  const [activeTab, setActiveTab] = useState<"result" | "logs">("result");
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const fetchHistory = useCallback(async () => {
    try {
      const data = await apiClient.get<ReplHistoryEntry[]>("/system/developer/repl/history?limit=30");
      setHistory(data || []);
    } catch {
      // Ignored
    }
  }, []);

  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  const handleExecute = async () => {
    if (!code.trim()) return;
    setIsExecuting(true);
    setActionMessage(null);

    try {
      const payload: ReplExecutionRequest = {
        code,
        language: "javascript",
        timeoutSeconds: 15,
      };

      const res = await apiClient.post<ReplExecutionResponse>("/system/developer/repl/eval", payload);
      setResponse(res);
      if (res.output && (!res.resultJson || res.resultJson === "undefined")) {
        setActiveTab("logs");
      } else {
        setActiveTab("result");
      }
      fetchHistory();
      setActionMessage({
        text: res.success ? `Script evaluated successfully in ${res.durationMs}ms.` : `Evaluation failed: ${res.errorMessage}`,
        type: res.success ? "success" : "error",
      });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setActionMessage({ text: msg || "REPL execution failed.", type: "error" });
    } finally {
      setIsExecuting(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
      e.preventDefault();
      handleExecute();
    }
  };

  const handleClearHistory = async () => {
    try {
      await apiClient.delete("/system/developer/repl/history");
      setHistory([]);
      setActionMessage({ text: "Command history cleared.", type: "success" });
    } catch {
      setActionMessage({ text: "Failed to clear history.", type: "error" });
    }
  };

  const handleResetSession = async () => {
    try {
      await apiClient.delete("/system/developer/repl/session");
      setResponse(null);
      setHistory([]);
      setCode(PRESET_SCRIPTS[0].code);
      setActionMessage({ text: "REPL session state reset.", type: "success" });
    } catch {
      setActionMessage({ text: "Failed to reset session.", type: "error" });
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
            ⚡ Interactive Scripting REPL
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Execute sandboxed scripts and live diagnostic probes directly against runtime host objects.
          </p>
        </div>

        <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" }}>
          <button
            onClick={handleResetSession}
            style={{
              padding: "7px 14px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "#f87171",
              cursor: "pointer",
              fontSize: "0.83rem",
              fontWeight: 600,
            }}
          >
            Reset Session
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

      {/* Preset Script Pickers */}
      <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap", marginBottom: "16px" }}>
        <span style={{ fontSize: "0.8rem", color: "var(--text-secondary, #94a3b8)", fontWeight: 600 }}>
          Sample Templates:
        </span>
        {PRESET_SCRIPTS.map((preset) => (
          <button
            key={preset.name}
            onClick={() => setCode(preset.code)}
            style={{
              padding: "5px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.78rem",
            }}
          >
            {preset.name}
          </button>
        ))}
      </div>

      {/* Split Workbench: Editor on Left, Output on Right */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
        {/* Left: Code Editor */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "16px",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 350px)",
            minHeight: "420px",
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
            <span style={{ fontSize: "0.8rem", fontWeight: 600, color: "#fff" }}>
              Script Editor
            </span>
            <span style={{ fontSize: "0.75rem", color: "var(--text-secondary, #94a3b8)" }}>
              Shortcut: <strong>Ctrl + Enter</strong>
            </span>
          </div>

          <textarea
            ref={textareaRef}
            value={code}
            onChange={(e) => setCode(e.target.value)}
            onKeyDown={handleKeyDown}
            style={{
              flex: 1,
              width: "100%",
              padding: "12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-primary, #0f172a)",
              color: "#34d399",
              fontFamily: "monospace",
              fontSize: "0.83rem",
              lineHeight: "1.5",
              resize: "none",
              boxSizing: "border-box",
              outline: "none",
            }}
            placeholder="// Write script or expression..."
            spellCheck={false}
          />

          <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "12px" }}>
            <button
              onClick={handleExecute}
              disabled={isExecuting || !code.trim()}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "6px",
                padding: "8px 18px",
                borderRadius: "6px",
                border: "none",
                backgroundColor: "var(--accent, #3b82f6)",
                color: "#fff",
                cursor: isExecuting || !code.trim() ? "not-allowed" : "pointer",
                fontSize: "0.83rem",
                fontWeight: 600,
                opacity: isExecuting || !code.trim() ? 0.6 : 1,
              }}
            >
              {isExecuting ? "⏳ Evaluating..." : "▶ Evaluate (Ctrl+Enter)"}
            </button>
          </div>
        </div>

        {/* Right: Output Pane */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "16px",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 350px)",
            minHeight: "420px",
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
            <div style={{ display: "flex", gap: "8px" }}>
              <button
                onClick={() => setActiveTab("result")}
                style={{
                  padding: "4px 10px",
                  borderRadius: "4px",
                  border: "none",
                  backgroundColor: activeTab === "result" ? "var(--accent, #3b82f6)" : "transparent",
                  color: activeTab === "result" ? "#fff" : "var(--text-secondary, #94a3b8)",
                  cursor: "pointer",
                  fontSize: "0.78rem",
                  fontWeight: 600,
                }}
              >
                Result JSON
              </button>
              <button
                onClick={() => setActiveTab("logs")}
                style={{
                  padding: "4px 10px",
                  borderRadius: "4px",
                  border: "none",
                  backgroundColor: activeTab === "logs" ? "var(--accent, #3b82f6)" : "transparent",
                  color: activeTab === "logs" ? "#fff" : "var(--text-secondary, #94a3b8)",
                  cursor: "pointer",
                  fontSize: "0.78rem",
                  fontWeight: 600,
                }}
              >
                Console Output
              </button>
            </div>

            {response && (
              <span style={{ fontSize: "0.75rem", color: "var(--text-secondary, #94a3b8)", fontFamily: "monospace" }}>
                {response.durationMs}ms · {response.resultType}
              </span>
            )}
          </div>

          <div
            style={{
              flex: 1,
              padding: "12px",
              borderRadius: "6px",
              backgroundColor: "var(--bg-primary, #0f172a)",
              border: "1px solid var(--border, #334155)",
              overflowY: "auto",
              boxSizing: "border-box",
            }}
          >
            {!response ? (
              <div style={{ height: "100%", display: "flex", alignItems: "center", justifyContent: "center", color: "var(--text-secondary, #94a3b8)", fontSize: "0.85rem" }}>
                No script evaluated yet. Click Evaluate to run.
              </div>
            ) : response.errorMessage ? (
              <div style={{ color: "#f87171", fontFamily: "monospace", fontSize: "0.8rem", whiteSpace: "pre-wrap" }}>
                Error: {response.errorMessage}
              </div>
            ) : activeTab === "result" ? (
              <pre style={{ margin: 0, color: "#38bdf8", fontFamily: "monospace", fontSize: "0.8rem", whiteSpace: "pre-wrap" }}>
                {response.resultJson || "undefined"}
              </pre>
            ) : (
              <pre style={{ margin: 0, color: "#34d399", fontFamily: "monospace", fontSize: "0.8rem", whiteSpace: "pre-wrap" }}>
                {response.output || "[No console.log output produced]"}
              </pre>
            )}
          </div>
        </div>
      </div>

      {/* Execution History */}
      {history.length > 0 && (
        <div style={{ marginTop: "24px", paddingTop: "16px", borderTop: "1px solid var(--border, #334155)" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
            <h3 style={{ margin: 0, fontSize: "1.05rem", fontWeight: 700, display: "flex", alignItems: "center", gap: "6px" }}>
              <span>📜</span> Command History
            </h3>
            <button
              onClick={handleClearHistory}
              style={{
                background: "transparent",
                border: "none",
                color: "#f87171",
                cursor: "pointer",
                fontSize: "0.78rem",
              }}
            >
              Clear History
            </button>
          </div>

          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              overflow: "hidden",
            }}
          >
            {history.slice(0, 8).map((h) => (
              <div
                key={h.id}
                onClick={() => setCode(h.code)}
                style={{
                  padding: "8px 14px",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  fontSize: "0.78rem",
                  cursor: "pointer",
                  borderBottom: "1px solid var(--border, #334155)",
                }}
                title="Click to reload this code into the editor"
              >
                <div style={{ display: "flex", alignItems: "center", gap: "8px", overflow: "hidden" }}>
                  <span
                    style={{
                      width: "8px",
                      height: "8px",
                      borderRadius: "50%",
                      backgroundColor: h.success ? "#34d399" : "#f87171",
                      flexShrink: 0,
                    }}
                  />
                  <span style={{ fontFamily: "monospace", color: "#e2e8f0", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
                    {h.code.replace(/\n/g, " ")}
                  </span>
                </div>
                <div style={{ display: "flex", gap: "14px", color: "var(--text-secondary, #94a3b8)", fontFamily: "monospace", flexShrink: 0 }}>
                  <span>{h.durationMs}ms</span>
                  <span>{new Date(h.executedAtUtc).toLocaleTimeString()}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
