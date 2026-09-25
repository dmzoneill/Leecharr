import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import { DeveloperNav } from "../components/DeveloperNav";
import type { DatabaseDiagnosticsResponse } from "../api/types";

function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`;
}

function formatUptime(seconds: number): string {
  const d = Math.floor(seconds / (3600 * 24));
  const h = Math.floor((seconds % (3600 * 24)) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  const parts = [];
  if (d > 0) parts.push(`${d}d`);
  if (h > 0) parts.push(`${h}h`);
  if (m > 0) parts.push(`${m}m`);
  parts.push(`${s}s`);
  return parts.join(" ");
}

export default function DeveloperDiagnostics() {
  const { t } = useTranslation();
  const [diag, setDiag] = useState<DatabaseDiagnosticsResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);
  const [isActionRunning, setIsActionRunning] = useState(false);

  const fetchDiagnostics = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await apiClient.get<DatabaseDiagnosticsResponse>("/system/database/diagnostics");
      setDiag(data);
    } catch (err: any) {
      setError(err?.message || "Failed to load runtime diagnostics.");
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchDiagnostics();
  }, [fetchDiagnostics]);

  const handlePragmaOptimize = async () => {
    setIsActionRunning(true);
    setActionMessage(null);
    try {
      await apiClient.post("/system/database/query", {
        query: "PRAGMA optimize;",
        readOnly: false,
      });
      setActionMessage({ text: "PRAGMA optimize completed successfully.", type: "success" });
      fetchDiagnostics();
    } catch (err: any) {
      setActionMessage({ text: err?.message || "PRAGMA optimize failed.", type: "error" });
    } finally {
      setIsActionRunning(false);
    }
  };

  const handleVacuum = async () => {
    if (!window.confirm("Run VACUUM to reclaim free space and defragment database file?")) return;
    setIsActionRunning(true);
    setActionMessage(null);
    try {
      await apiClient.post("/system/database/query", {
        query: "VACUUM;",
        readOnly: false,
      });
      setActionMessage({ text: "Database VACUUM completed successfully.", type: "success" });
      fetchDiagnostics();
    } catch (err: any) {
      setActionMessage({ text: err?.message || "VACUUM failed.", type: "error" });
    } finally {
      setIsActionRunning(false);
    }
  };

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflowY: "auto",
        padding: "1.5rem",
        boxSizing: "border-box",
      }}
    >
      <DeveloperNav />
      {/* Header */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1 style={{ margin: "0 0 0.25rem 0", fontSize: "1.6rem", display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span>🛠️</span> {t("developer.diagnostics", "Runtime & Diagnostics")}
          </h1>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "0.9rem" }}>
            Under-the-hood engine diagnostics, SQLite PRAGMAs, CLR garbage collection metrics, and thread pool telemetry.
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button
            className="btn btn-outline btn-small"
            onClick={fetchDiagnostics}
            disabled={isLoading || isActionRunning}
            title="Refresh Diagnostics"
          >
            {isLoading ? "Refreshing..." : "↻ Refresh"}
          </button>
        </div>
      </div>

      {actionMessage && (
        <div
          style={{
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            marginBottom: "1rem",
            backgroundColor: actionMessage.type === "success" ? "rgba(40, 167, 69, 0.15)" : "rgba(220, 53, 69, 0.15)",
            color: actionMessage.type === "success" ? "var(--success, #28a745)" : "var(--danger, #dc3545)",
            border: `1px solid ${actionMessage.type === "success" ? "rgba(40, 167, 69, 0.4)" : "rgba(220, 53, 69, 0.4)"}`,
          }}
        >
          {actionMessage.text}
        </div>
      )}

      {error && (
        <div
          style={{
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            marginBottom: "1rem",
            backgroundColor: "rgba(220, 53, 69, 0.15)",
            color: "var(--danger, #dc3545)",
            border: "1px solid rgba(220, 53, 69, 0.4)",
          }}
        >
          {error}
        </div>
      )}

      {/* Grid of Diagnostics Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
          gap: "1.25rem",
          marginBottom: "1.5rem",
        }}
      >
        {/* Card 1: SQLite Storage & PRAGMAs */}
        <div className="card" style={{ padding: "1.25rem" }}>
          <h3 style={{ margin: "0 0 1rem 0", fontSize: "1.1rem", display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span>💾</span> SQLite PRAGMA Engine
          </h3>
          <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem", fontSize: "0.88rem" }}>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Database File:</span>
              <span style={{ fontFamily: "monospace", maxWidth: "220px", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }} title={diag?.databasePath || "In-Memory / Default"}>
                {diag?.databasePath || "Main"}
              </span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Allocated Disk Size:</span>
              <span style={{ fontWeight: 600 }}>{diag ? formatBytes(diag.fileSizeBytes || diag.pageSize * diag.pageCount) : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Page Size:</span>
              <span>{diag ? `${diag.pageSize.toLocaleString()} bytes` : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Total Page Count:</span>
              <span>{diag ? diag.pageCount.toLocaleString() : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Freelist (Free Pages):</span>
              <span style={{ color: (diag?.freelistCount ?? 0) > 0 ? "var(--warning, #ffc107)" : "var(--text-muted)" }}>
                {diag ? `${diag.freelistCount.toLocaleString()} pages (${formatBytes(diag.freelistCount * diag.pageSize)})` : "-"}
              </span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Journal Mode:</span>
              <span style={{ fontWeight: 600, color: "var(--accent)" }}>{diag?.journalMode?.toUpperCase() || "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Synchronous Mode:</span>
              <span>{diag?.synchronous || "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Cache Size:</span>
              <span>{diag ? `${diag.cacheSize.toLocaleString()} pages` : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Integrity Status:</span>
              <span style={{ fontWeight: 600, color: diag?.integrityCheck === "ok" ? "var(--success, #28a745)" : "var(--danger, #dc3545)" }}>
                {diag?.integrityCheck === "ok" ? "✓ OK" : (diag?.integrityCheck || "-")}
              </span>
            </div>
          </div>
        </div>

        {/* Card 2: .NET Runtime & CLR Memory */}
        <div className="card" style={{ padding: "1.25rem" }}>
          <h3 style={{ margin: "0 0 1rem 0", fontSize: "1.1rem", display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span>⚡</span> CLR & Garbage Collection
          </h3>
          <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem", fontSize: "0.88rem" }}>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>.NET Version:</span>
              <span style={{ fontWeight: 600 }}>{diag?.dotNetVersion || "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Process Uptime:</span>
              <span>{diag ? formatUptime(diag.processUptimeSeconds) : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Working Set (RAM):</span>
              <span style={{ fontWeight: 600 }}>{diag ? formatBytes(diag.workingSetBytes) : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>GC Heap Allocated:</span>
              <span style={{ fontWeight: 600, color: "var(--accent)" }}>{diag ? formatBytes(diag.gcTotalMemoryBytes) : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Gen 0 Collections:</span>
              <span>{diag ? diag.gcGen0Collections.toLocaleString() : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Gen 1 Collections:</span>
              <span>{diag ? diag.gcGen1Collections.toLocaleString() : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Gen 2 Collections:</span>
              <span>{diag ? diag.gcGen2Collections.toLocaleString() : "-"}</span>
            </div>
          </div>
        </div>

        {/* Card 3: Thread Pool Telemetry */}
        <div className="card" style={{ padding: "1.25rem" }}>
          <h3 style={{ margin: "0 0 1rem 0", fontSize: "1.1rem", display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span>🧵</span> Thread Pool Telemetry
          </h3>
          <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem", fontSize: "0.88rem" }}>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Worker Threads Available:</span>
              <span style={{ fontWeight: 600 }}>{diag ? diag.threadPoolAvailableWorkerThreads.toLocaleString() : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>Max Worker Threads:</span>
              <span>{diag ? diag.threadPoolMaxWorkerThreads.toLocaleString() : "-"}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid var(--border-light)", paddingBottom: "0.35rem" }}>
              <span style={{ color: "var(--text-muted)" }}>I/O Completion Ports:</span>
              <span style={{ fontWeight: 600 }}>{diag ? diag.threadPoolAvailableCompletionPortThreads.toLocaleString() : "-"}</span>
            </div>
          </div>

          <h4 style={{ margin: "1.25rem 0 0.5rem 0", fontSize: "0.95rem" }}>Maintenance Triggers</h4>
          <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
            <button
              className="btn btn-outline btn-small"
              onClick={handlePragmaOptimize}
              disabled={isActionRunning}
              title="Run PRAGMA optimize to refresh query planner statistics"
            >
              PRAGMA Optimize
            </button>
            <button
              className="btn btn-outline btn-small"
              onClick={handleVacuum}
              disabled={isActionRunning}
              title="Defragment and reclaim unused SQLite disk space"
            >
              Run VACUUM
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
