import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import type {
  DeveloperWebhookTemplate,
  DeveloperWebhookHistoryItem,
  DeveloperWebhookSimulateResponse,
} from "../api/types";

export default function DeveloperWebhooks() {
  const [templates, setTemplates] = useState<DeveloperWebhookTemplate[]>([]);
  const [history, setHistory] = useState<DeveloperWebhookHistoryItem[]>([]);
  const [selectedTemplate, setSelectedTemplate] = useState<DeveloperWebhookTemplate | null>(null);
  const [payloadText, setPayloadText] = useState("");
  const [isSimulating, setIsSimulating] = useState(false);
  const [simulationResult, setSimulationResult] = useState<DeveloperWebhookSimulateResponse | null>(null);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const fetchTemplatesAndHistory = useCallback(async () => {
    try {
      const [tList, hList] = await Promise.all([
        apiClient.get<DeveloperWebhookTemplate[]>("/system/developer/webhooks/templates"),
        apiClient.get<DeveloperWebhookHistoryItem[]>("/system/developer/webhooks/history"),
      ]);
      setTemplates(tList || []);
      setHistory(hList || []);
      if (!selectedTemplate && tList && tList.length > 0) {
        setSelectedTemplate(tList[0]);
        setPayloadText(tList[0].payloadJson);
      }
    } catch {
      // background poll
    }
  }, [selectedTemplate]);

  useEffect(() => {
    fetchTemplatesAndHistory();
  }, [fetchTemplatesAndHistory]);

  const selectTemplate = (t: DeveloperWebhookTemplate) => {
    setSelectedTemplate(t);
    setPayloadText(t.payloadJson);
    setSimulationResult(null);
  };

  const handleSimulate = async () => {
    setIsSimulating(true);
    setSimulationResult(null);
    setActionMessage(null);
    try {
      const res = await apiClient.post<DeveloperWebhookSimulateResponse>("/system/developer/webhooks/simulate", {
        eventType: selectedTemplate?.eventType || "Grab",
        payloadJson: payloadText,
      });
      setSimulationResult(res);
      setActionMessage({ text: "Simulation executed successfully.", type: "success" });
      const hList = await apiClient.get<DeveloperWebhookHistoryItem[]>("/system/developer/webhooks/history");
      setHistory(hList || []);
    } catch (err: any) {
      setActionMessage({ text: err?.message || "Simulation failed.", type: "error" });
    } finally {
      setIsSimulating(false);
    }
  };

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Top Banner */}
      <div style={{ marginBottom: "16px" }}>
        <h2 style={{ margin: "0 0 4px 0", fontSize: "1.4rem", fontWeight: 700 }}>
          🪝 Arr Webhook Sandbox & Replayer
        </h2>
        <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
          Simulate inbound webhooks from Sonarr, Radarr, and Prowlarr without needing an active media pipeline.
        </p>
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

      {/* Top 3-Column Sandbox: Templates | Editor | Trace Logs */}
      <div style={{ display: "grid", gridTemplateColumns: "280px 1fr 400px", gap: "16px", marginBottom: "20px" }}>
        {/* Templates List */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
            display: "flex",
            flexDirection: "column",
            height: "440px",
          }}
        >
          <h3 style={{ margin: "0 0 10px 0", fontSize: "0.92rem", fontWeight: 600 }}>Preset Templates</h3>
          <div style={{ flex: 1, overflowY: "auto", display: "flex", flexDirection: "column", gap: "6px" }}>
            {templates.map((tpl) => {
              const isSelected = selectedTemplate?.id === tpl.id;
              return (
                <div
                  key={tpl.id}
                  onClick={() => selectTemplate(tpl)}
                  style={{
                    padding: "10px",
                    borderRadius: "6px",
                    border: `1px solid ${isSelected ? "var(--accent, #3b82f6)" : "var(--border, #334155)"}`,
                    backgroundColor: isSelected ? "rgba(59, 130, 246, 0.15)" : "var(--bg-primary, #0f172a)",
                    cursor: "pointer",
                  }}
                >
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "4px" }}>
                    <span style={{ fontWeight: 600, fontSize: "0.82rem" }}>{tpl.name}</span>
                    <span
                      style={{
                        padding: "1px 6px",
                        borderRadius: "4px",
                        fontSize: "0.7rem",
                        backgroundColor: "rgba(255,255,255,0.08)",
                        color: "var(--text-secondary)",
                      }}
                    >
                      {tpl.source}
                    </span>
                  </div>
                  <div style={{ fontSize: "0.74rem", color: "var(--text-secondary)" }}>
                    {tpl.description}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* JSON Payload Editor */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
            display: "flex",
            flexDirection: "column",
            height: "440px",
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "8px" }}>
            <h3 style={{ margin: 0, fontSize: "0.92rem", fontWeight: 600 }}>Webhook Payload (JSON)</h3>
            <button
              onClick={handleSimulate}
              disabled={isSimulating}
              style={{
                padding: "6px 14px",
                borderRadius: "6px",
                border: "none",
                backgroundColor: "var(--accent, #3b82f6)",
                color: "#fff",
                fontSize: "0.8rem",
                fontWeight: 600,
                cursor: isSimulating ? "not-allowed" : "pointer",
              }}
            >
              {isSimulating ? "Simulating..." : "▶ Dispatch Simulation"}
            </button>
          </div>

          <textarea
            value={payloadText}
            onChange={(e) => setPayloadText(e.target.value)}
            style={{
              flex: 1,
              width: "100%",
              padding: "10px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-primary, #0f172a)",
              color: "#f8fafc",
              fontFamily: "monospace",
              fontSize: "0.8rem",
              lineHeight: 1.4,
              resize: "none",
            }}
          />
        </div>

        {/* Simulation Output Trace */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
            display: "flex",
            flexDirection: "column",
            height: "440px",
          }}
        >
          <h3 style={{ margin: "0 0 10px 0", fontSize: "0.92rem", fontWeight: 600 }}>Execution Trace</h3>
          <div
            style={{
              flex: 1,
              backgroundColor: "var(--bg-primary, #0f172a)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "6px",
              padding: "10px",
              overflowY: "auto",
              fontFamily: "monospace",
              fontSize: "0.76rem",
              color: "#cbd5e1",
            }}
          >
            {simulationResult ? (
              <div>
                <div style={{ marginBottom: "8px", paddingBottom: "6px", borderBottom: "1px solid #334155" }}>
                  <span
                    style={{
                      padding: "2px 6px",
                      borderRadius: "4px",
                      fontSize: "0.72rem",
                      fontWeight: 700,
                      backgroundColor: simulationResult.success ? "rgba(16, 185, 129, 0.2)" : "rgba(239, 68, 68, 0.2)",
                      color: simulationResult.success ? "#34d399" : "#f87171",
                    }}
                  >
                    HTTP {simulationResult.statusCode}
                  </span>
                  <span style={{ marginLeft: "8px", color: "var(--text-secondary)" }}>
                    {simulationResult.executionTimeMs}ms
                  </span>
                </div>
                <div style={{ color: "#38bdf8", marginBottom: "8px" }}>{simulationResult.message}</div>
                {simulationResult.traceLogs.map((log, i) => (
                  <div key={i} style={{ marginBottom: "3px", color: "#94a3b8" }}>{log}</div>
                ))}
              </div>
            ) : (
              <div style={{ color: "var(--text-secondary)", textAlign: "center", padding: "40px 10px" }}>
                Click "Dispatch Simulation" to execute the payload against the local webhook ingestion pipeline.
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Inbound Webhook Receipts Log */}
      <div
        style={{
          backgroundColor: "var(--bg-surface, #1e293b)",
          border: "1px solid var(--border, #334155)",
          borderRadius: "8px",
          padding: "14px",
        }}
      >
        <h3 style={{ margin: "0 0 10px 0", fontSize: "0.95rem", fontWeight: 600 }}>
          Recent Inbound Webhook Receipts ({history.length})
        </h3>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.8rem", textAlign: "left" }}>
          <thead>
            <tr style={{ backgroundColor: "rgba(0,0,0,0.2)", borderBottom: "1px solid var(--border, #334155)" }}>
              <th style={{ padding: "8px 10px" }}>Time (UTC)</th>
              <th style={{ padding: "8px 10px" }}>Source</th>
              <th style={{ padding: "8px 10px" }}>Event Type</th>
              <th style={{ padding: "8px 10px" }}>Source IP</th>
              <th style={{ padding: "8px 10px" }}>Status</th>
              <th style={{ padding: "8px 10px" }}>Result Message</th>
            </tr>
          </thead>
          <tbody>
            {history.length === 0 ? (
              <tr>
                <td colSpan={6} style={{ padding: "20px", textAlign: "center", color: "var(--text-secondary)" }}>
                  No inbound webhooks recorded yet.
                </td>
              </tr>
            ) : (
              history.map((h) => (
                <tr key={h.id} style={{ borderBottom: "1px solid var(--border, #334155)" }}>
                  <td style={{ padding: "8px 10px", fontFamily: "monospace", color: "var(--text-secondary)" }}>
                    {new Date(h.timestampUtc).toLocaleTimeString()}
                  </td>
                  <td style={{ padding: "8px 10px", fontWeight: 600 }}>{h.source}</td>
                  <td style={{ padding: "8px 10px" }}>
                    <span style={{ padding: "2px 6px", borderRadius: "4px", backgroundColor: "rgba(59, 130, 246, 0.15)", color: "#60a5fa" }}>
                      {h.eventType}
                    </span>
                  </td>
                  <td style={{ padding: "8px 10px", color: "var(--text-secondary)", fontFamily: "monospace" }}>{h.sourceIp}</td>
                  <td style={{ padding: "8px 10px" }}>
                    <span
                      style={{
                        padding: "2px 6px",
                        borderRadius: "4px",
                        fontSize: "0.72rem",
                        fontWeight: 600,
                        backgroundColor: h.success ? "rgba(16, 185, 129, 0.2)" : "rgba(239, 68, 68, 0.2)",
                        color: h.success ? "#34d399" : "#f87171",
                      }}
                    >
                      {h.statusCode}
                    </span>
                  </td>
                  <td style={{ padding: "8px 10px", color: "var(--text-secondary)" }}>{h.resultMessage}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
