import React, { useState, useEffect, useCallback, useRef } from "react";
import { apiClient } from "../api/client";
import { DeveloperNav } from "../components/DeveloperNav";
import type { DeveloperHttpTrafficItem, DeveloperHttpTrafficResponse } from "../api/types";

export default function DeveloperNetwork() {
  const [traffic, setTraffic] = useState<DeveloperHttpTrafficItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [selectedItem, setSelectedItem] = useState<DeveloperHttpTrafficItem | null>(null);
  const [isPaused, setIsPaused] = useState(false);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const isPausedRef = useRef(isPaused);
  isPausedRef.current = isPaused;

  const fetchTraffic = useCallback(async () => {
    if (isPausedRef.current) return;
    try {
      const data = await apiClient.get<DeveloperHttpTrafficResponse>(
        `/system/developer/network?limit=150${search ? `&search=${encodeURIComponent(search)}` : ""}`
      );
      setTraffic(data.items || []);
    } catch {
      // background poll
    } finally {
      setIsLoading(false);
    }
  }, [search]);

  useEffect(() => {
    fetchTraffic();
    const interval = setInterval(fetchTraffic, 2500);
    return () => clearInterval(interval);
  }, [fetchTraffic]);

  const handleClear = async () => {
    try {
      await apiClient.delete("/system/developer/network");
      setTraffic([]);
      setSelectedItem(null);
      setActionMessage({ text: "Network traffic log cleared.", type: "success" });
    } catch {
      setActionMessage({ text: "Failed to clear network traffic.", type: "error" });
    }
  };

  const copyAsCurl = (item: DeveloperHttpTrafficItem) => {
    let curl = `curl -X ${item.method} "${item.url}"`;
    if (item.requestHeaders) {
      Object.entries(item.requestHeaders).forEach(([k, v]) => {
        curl += ` \\\n  -H "${k}: ${v}"`;
      });
    }
    navigator.clipboard.writeText(curl);
    setActionMessage({ text: "cURL command copied to clipboard!", type: "success" });
  };

  return (
    <div style={{ padding: "20px", maxWidth: "1600px", margin: "0 auto" }}>
      <DeveloperNav />

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
            🌐 Network & HTTP Wiretap
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            In-memory ring buffer recording outgoing HTTP requests to Sonarr, Radarr, Prowlarr, Jackett, and Trackers.
          </p>
        </div>

        <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" }}>
          <button
            onClick={() => setIsPaused(!isPaused)}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "6px",
              padding: "7px 14px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: isPaused ? "#f59e0b" : "var(--bg-surface, #1e293b)",
              color: isPaused ? "#000" : "#fff",
              cursor: "pointer",
              fontSize: "0.83rem",
              fontWeight: 600,
            }}
          >
            {isPaused ? "▶ Resume Stream" : "⏸ Pause Stream"}
          </button>

          <button
            onClick={handleClear}
            style={{
              padding: "7px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "var(--text-secondary, #94a3b8)",
              cursor: "pointer",
              fontSize: "0.83rem",
            }}
          >
            Clear Log
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

      {/* Filter Toolbar */}
      <div style={{ display: "flex", gap: "10px", alignItems: "center", marginBottom: "12px" }}>
        <input
          type="text"
          placeholder="Filter by URL, host, method, or status code..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{
            flex: 1,
            padding: "8px 12px",
            borderRadius: "6px",
            border: "1px solid var(--border, #334155)",
            backgroundColor: "var(--bg-surface, #1e293b)",
            color: "#fff",
            fontSize: "0.85rem",
          }}
        />
        <span style={{ fontSize: "0.8rem", color: "var(--text-secondary, #94a3b8)", whiteSpace: "nowrap" }}>
          Showing {traffic.length} requests
        </span>
      </div>

      {/* Split Table & Inspector */}
      <div style={{ display: "grid", gridTemplateColumns: selectedItem ? "1fr 500px" : "1fr", gap: "16px" }}>
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            overflow: "hidden",
          }}
        >
          <div style={{ maxHeight: "calc(100vh - 280px)", overflowY: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.82rem", textAlign: "left" }}>
              <thead>
                <tr style={{ backgroundColor: "rgba(0,0,0,0.2)", borderBottom: "1px solid var(--border, #334155)" }}>
                  <th style={{ padding: "10px 12px", width: "110px" }}>Time (UTC)</th>
                  <th style={{ padding: "10px 12px", width: "70px" }}>Method</th>
                  <th style={{ padding: "10px 12px", width: "70px" }}>Status</th>
                  <th style={{ padding: "10px 12px" }}>Target URL</th>
                  <th style={{ padding: "10px 12px", width: "90px" }}>Latency</th>
                  <th style={{ padding: "10px 12px", width: "110px" }}>Engine</th>
                  <th style={{ padding: "10px 12px", textAlign: "right", width: "70px" }}>Action</th>
                </tr>
              </thead>
              <tbody>
                {isLoading && traffic.length === 0 ? (
                  <tr>
                    <td colSpan={7} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>
                      Connecting to HTTP wiretap...
                    </td>
                  </tr>
                ) : traffic.length === 0 ? (
                  <tr>
                    <td colSpan={7} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>
                      No outbound HTTP requests captured yet.
                    </td>
                  </tr>
                ) : (
                  traffic.map((item) => {
                    const isSelected = selectedItem?.id === item.id;
                    const isSuccess = item.isSuccess;
                    const is4xx = item.statusCode >= 400 && item.statusCode < 500;
                    const is5xx = item.statusCode >= 500;

                    return (
                      <tr
                        key={item.id}
                        onClick={() => setSelectedItem(item)}
                        style={{
                          borderBottom: "1px solid var(--border, #334155)",
                          backgroundColor: isSelected ? "rgba(59, 130, 246, 0.15)" : "transparent",
                          cursor: "pointer",
                        }}
                      >
                        <td style={{ padding: "8px 12px", whiteSpace: "nowrap", fontFamily: "monospace", color: "var(--text-secondary)" }}>
                          {new Date(item.timestampUtc).toLocaleTimeString()}
                        </td>
                        <td style={{ padding: "8px 12px" }}>
                          <span
                            style={{
                              padding: "2px 6px",
                              borderRadius: "4px",
                              fontWeight: 700,
                              fontSize: "0.72rem",
                              backgroundColor: item.method === "GET"
                                ? "rgba(59, 130, 246, 0.2)"
                                : item.method === "POST"
                                ? "rgba(16, 185, 129, 0.2)"
                                : item.method === "DELETE"
                                ? "rgba(239, 68, 68, 0.2)"
                                : "rgba(245, 158, 11, 0.2)",
                              color: item.method === "GET"
                                ? "#60a5fa"
                                : item.method === "POST"
                                ? "#34d399"
                                : item.method === "DELETE"
                                ? "#f87171"
                                : "#fbbf24",
                            }}
                          >
                            {item.method}
                          </span>
                        </td>
                        <td style={{ padding: "8px 12px" }}>
                          <span
                            style={{
                              padding: "2px 6px",
                              borderRadius: "4px",
                              fontWeight: 700,
                              fontSize: "0.72rem",
                              backgroundColor: isSuccess
                                ? "rgba(16, 185, 129, 0.2)"
                                : is4xx
                                ? "rgba(245, 158, 11, 0.2)"
                                : is5xx
                                ? "rgba(239, 68, 68, 0.2)"
                                : "rgba(148, 163, 184, 0.2)",
                              color: isSuccess
                                ? "#34d399"
                                : is4xx
                                ? "#fbbf24"
                                : is5xx
                                ? "#f87171"
                                : "#cbd5e1",
                            }}
                          >
                            {item.statusCode || "ERR"}
                          </span>
                        </td>
                        <td
                          style={{
                            padding: "8px 12px",
                            fontFamily: "monospace",
                            fontSize: "0.76rem",
                            maxWidth: "400px",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          <span style={{ color: "#fff", fontWeight: 600 }}>{item.host}</span>
                          <span style={{ color: "var(--text-secondary)" }}>
                            {item.url.replace(`http://${item.host}`, "").replace(`https://${item.host}`, "")}
                          </span>
                        </td>
                        <td style={{ padding: "8px 12px", color: "var(--text-secondary)", fontFamily: "monospace" }}>
                          {item.durationMs}ms
                        </td>
                        <td style={{ padding: "8px 12px", color: "var(--text-secondary)", fontSize: "0.76rem" }}>
                          {item.transportEngine}
                        </td>
                        <td style={{ padding: "8px 12px", textAlign: "right" }}>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              setSelectedItem(item);
                            }}
                            style={{
                              padding: "4px 8px",
                              borderRadius: "4px",
                              border: "1px solid var(--border, #334155)",
                              backgroundColor: "var(--bg-primary, #0f172a)",
                              color: "#fff",
                              fontSize: "0.75rem",
                              cursor: "pointer",
                            }}
                          >
                            View
                          </button>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Selected Request Inspector Drawer */}
        {selectedItem && (
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "16px",
              display: "flex",
              flexDirection: "column",
              height: "calc(100vh - 280px)",
              overflowY: "auto",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
              <h3 style={{ margin: 0, fontSize: "1rem", fontWeight: 600 }}>Request Details</h3>
              <div style={{ display: "flex", gap: "6px" }}>
                <button
                  onClick={() => copyAsCurl(selectedItem)}
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
                  📋 cURL
                </button>
                <button
                  onClick={() => setSelectedItem(null)}
                  style={{ background: "none", border: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "1.1rem" }}
                >
                  ✕
                </button>
              </div>
            </div>

            <div style={{ fontSize: "0.78rem", color: "var(--text-secondary)", marginBottom: "6px", wordBreak: "break-all" }}>
              <strong>URL:</strong> <code>{selectedItem.url}</code>
            </div>
            <div style={{ fontSize: "0.78rem", color: "var(--text-secondary)", marginBottom: "6px" }}>
              <strong>Status:</strong> {selectedItem.statusCode} | <strong>Latency:</strong> {selectedItem.durationMs}ms | <strong>Engine:</strong> {selectedItem.transportEngine}
            </div>

            {selectedItem.errorMessage && (
              <div style={{ padding: "8px", borderRadius: "4px", backgroundColor: "rgba(239, 68, 68, 0.15)", color: "#f87171", fontSize: "0.78rem", marginBottom: "10px" }}>
                {selectedItem.errorMessage}
              </div>
            )}

            {/* Request Headers */}
            <div style={{ marginTop: "10px", marginBottom: "6px", fontSize: "0.8rem", fontWeight: 600 }}>Request Headers</div>
            <pre style={{ margin: 0, padding: "8px", backgroundColor: "var(--bg-primary, #0f172a)", borderRadius: "4px", fontSize: "0.74rem", fontFamily: "monospace", color: "#cbd5e1" }}>
              {Object.keys(selectedItem.requestHeaders).length > 0
                ? Object.entries(selectedItem.requestHeaders).map(([k, v]) => `${k}: ${v}`).join("\n")
                : "(No headers recorded)"}
            </pre>

            {/* Response Headers */}
            <div style={{ marginTop: "10px", marginBottom: "6px", fontSize: "0.8rem", fontWeight: 600 }}>Response Headers</div>
            <pre style={{ margin: 0, padding: "8px", backgroundColor: "var(--bg-primary, #0f172a)", borderRadius: "4px", fontSize: "0.74rem", fontFamily: "monospace", color: "#cbd5e1" }}>
              {Object.keys(selectedItem.responseHeaders).length > 0
                ? Object.entries(selectedItem.responseHeaders).map(([k, v]) => `${k}: ${v}`).join("\n")
                : "(No headers recorded)"}
            </pre>

            {/* Response Body Preview */}
            <div style={{ marginTop: "10px", marginBottom: "6px", fontSize: "0.8rem", fontWeight: 600 }}>Response Body Preview</div>
            <pre
              style={{
                flex: 1,
                margin: 0,
                padding: "8px",
                backgroundColor: "var(--bg-primary, #0f172a)",
                borderRadius: "4px",
                fontSize: "0.74rem",
                fontFamily: "monospace",
                color: "#e2e8f0",
                overflow: "auto",
              }}
            >
              {selectedItem.responseBodyPreview || "(Empty body)"}
            </pre>
          </div>
        )}
      </div>
    </div>
  );
}
