import React, { useState, useEffect, useCallback, useRef } from "react";
import { apiClient } from "../api/client";
import type { DeveloperEventItem, DeveloperEventsResponse } from "../api/types";

export default function DeveloperEvents() {
  const [events, setEvents] = useState<DeveloperEventItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [selectedEvent, setSelectedEvent] = useState<DeveloperEventItem | null>(null);
  const [isPaused, setIsPaused] = useState(false);
  const [showPublishModal, setShowPublishModal] = useState(false);
  const [publishEventName, setPublishEventName] = useState("TorrentAddedEvent");
  const [publishPayload, setPublishPayload] = useState('{\n  "torrentId": 42,\n  "name": "Ubuntu-24.04-live-server.iso",\n  "sizeBytes": 2048576000\n}');
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const isPausedRef = useRef(isPaused);
  isPausedRef.current = isPaused;

  const fetchEvents = useCallback(async () => {
    if (isPausedRef.current) return;
    try {
      const data = await apiClient.get<DeveloperEventsResponse>(`/system/developer/events?limit=150${search ? `&search=${encodeURIComponent(search)}` : ""}`);
      setEvents(data.events || []);
    } catch {
      // Background poll failure
    } finally {
      setIsLoading(false);
    }
  }, [search]);

  useEffect(() => {
    fetchEvents();
    const interval = setInterval(fetchEvents, 2500);
    return () => clearInterval(interval);
  }, [fetchEvents]);

  const handleClear = async () => {
    try {
      await apiClient.delete("/system/developer/events");
      setEvents([]);
      setSelectedEvent(null);
      setActionMessage({ text: "Event log cleared.", type: "success" });
    } catch {
      setActionMessage({ text: "Failed to clear events.", type: "error" });
    }
  };

  const handlePublish = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post("/system/developer/events/publish", {
        eventName: publishEventName,
        payloadJson: publishPayload,
      });
      setShowPublishModal(false);
      setActionMessage({ text: `Synthetic event '${publishEventName}' published.`, type: "success" });
      fetchEvents();
    } catch (err: any) {
      setActionMessage({ text: err?.message || "Failed to publish event.", type: "error" });
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
            📡 Event Bus Wiretap
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Live observation tap into the internal <code>IEventAggregator</code> domain messaging bus.
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
            onClick={() => setShowPublishModal(true)}
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
            ➕ Dispatch Synthetic Event
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
      <div
        style={{
          display: "flex",
          gap: "10px",
          alignItems: "center",
          marginBottom: "12px",
        }}
      >
        <input
          type="text"
          placeholder="Filter by event name, namespace, or payload content..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{
            flex: 1,
            padding: "8px 12px",
            borderRadius: "6px",
            border: "1px solid var(--border, #334155)",
            backgroundColor: "var(--bg-surface, #1e293b)",
            color: "var(--text-primary, #f8fafc)",
            fontSize: "0.85rem",
          }}
        />
        <span style={{ fontSize: "0.8rem", color: "var(--text-secondary, #94a3b8)", whiteSpace: "nowrap" }}>
          Showing {events.length} events
        </span>
      </div>

      {/* Events Table & Payload Inspector Side-by-Side */}
      <div style={{ display: "grid", gridTemplateColumns: selectedEvent ? "1fr 480px" : "1fr", gap: "16px" }}>
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
                  <th style={{ padding: "10px 12px", width: "160px" }}>Time (UTC)</th>
                  <th style={{ padding: "10px 12px" }}>Event Name</th>
                  <th style={{ padding: "10px 12px" }}>Source Namespace</th>
                  <th style={{ padding: "10px 12px" }}>Payload Preview</th>
                  <th style={{ padding: "10px 12px", textAlign: "right", width: "80px" }}>Action</th>
                </tr>
              </thead>
              <tbody>
                {isLoading && events.length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>
                      Connecting to event tap...
                    </td>
                  </tr>
                ) : events.length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>
                      No events recorded yet. Perform actions in the application or dispatch a synthetic event.
                    </td>
                  </tr>
                ) : (
                  events.map((ev) => {
                    const isSelected = selectedEvent?.id === ev.id;
                    return (
                      <tr
                        key={ev.id}
                        onClick={() => setSelectedEvent(ev)}
                        style={{
                          borderBottom: "1px solid var(--border, #334155)",
                          backgroundColor: isSelected ? "rgba(59, 130, 246, 0.15)" : "transparent",
                          cursor: "pointer",
                        }}
                      >
                        <td style={{ padding: "8px 12px", whiteSpace: "nowrap", fontFamily: "monospace", color: "var(--text-secondary)" }}>
                          {new Date(ev.timestampUtc).toLocaleTimeString()}
                        </td>
                        <td style={{ padding: "8px 12px", fontWeight: 600 }}>
                          <span
                            style={{
                              padding: "2px 8px",
                              borderRadius: "4px",
                              backgroundColor: ev.eventName.includes("Failed") ? "rgba(239, 68, 68, 0.2)" : "rgba(59, 130, 246, 0.2)",
                              color: ev.eventName.includes("Failed") ? "#f87171" : "#60a5fa",
                              fontSize: "0.78rem",
                            }}
                          >
                            {ev.eventName}
                          </span>
                        </td>
                        <td style={{ padding: "8px 12px", color: "var(--text-secondary)", fontFamily: "monospace", fontSize: "0.76rem" }}>
                          {ev.sourceNamespace}
                        </td>
                        <td
                          style={{
                            padding: "8px 12px",
                            fontFamily: "monospace",
                            fontSize: "0.76rem",
                            color: "var(--text-secondary)",
                            maxWidth: "400px",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {ev.payloadJson}
                        </td>
                        <td style={{ padding: "8px 12px", textAlign: "right" }}>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              setSelectedEvent(ev);
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
                            Inspect
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

        {/* Selected Event Payload Inspector Drawer */}
        {selectedEvent && (
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "16px",
              display: "flex",
              flexDirection: "column",
              height: "calc(100vh - 280px)",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
              <h3 style={{ margin: 0, fontSize: "1rem", fontWeight: 600 }}>
                {selectedEvent.eventName}
              </h3>
              <button
                onClick={() => setSelectedEvent(null)}
                style={{
                  background: "none",
                  border: "none",
                  color: "var(--text-secondary)",
                  cursor: "pointer",
                  fontSize: "1.1rem",
                }}
              >
                ✕
              </button>
            </div>

            <div style={{ fontSize: "0.78rem", color: "var(--text-secondary)", marginBottom: "8px" }}>
              <strong>Timestamp:</strong> {new Date(selectedEvent.timestampUtc).toISOString()}
            </div>
            <div style={{ fontSize: "0.78rem", color: "var(--text-secondary)", marginBottom: "12px" }}>
              <strong>Namespace:</strong> <code>{selectedEvent.sourceNamespace}</code>
            </div>

            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
              <span style={{ fontSize: "0.8rem", fontWeight: 600 }}>Payload JSON</span>
              <button
                onClick={() => {
                  navigator.clipboard.writeText(selectedEvent.payloadJson);
                  setActionMessage({ text: "Payload JSON copied to clipboard!", type: "success" });
                }}
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
                📋 Copy JSON
              </button>
            </div>

            <pre
              style={{
                flex: 1,
                margin: 0,
                padding: "12px",
                backgroundColor: "var(--bg-primary, #0f172a)",
                border: "1px solid var(--border, #334155)",
                borderRadius: "6px",
                overflow: "auto",
                fontSize: "0.78rem",
                fontFamily: "monospace",
                color: "#e2e8f0",
                lineHeight: 1.4,
              }}
            >
              {(() => {
                try {
                  return JSON.stringify(JSON.parse(selectedEvent.payloadJson), null, 2);
                } catch {
                  return selectedEvent.payloadJson;
                }
              })()}
            </pre>
          </div>
        )}
      </div>

      {/* Publish Synthetic Event Modal */}
      {showPublishModal && (
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
              width: "560px",
              maxWidth: "90%",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "14px" }}>
              <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 700 }}>Dispatch Synthetic Event</h3>
              <button
                onClick={() => setShowPublishModal(false)}
                style={{ background: "none", border: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "1.2rem" }}
              >
                ✕
              </button>
            </div>

            <form onSubmit={handlePublish}>
              <div style={{ marginBottom: "12px" }}>
                <label style={{ display: "block", fontSize: "0.82rem", fontWeight: 600, marginBottom: "4px" }}>
                  Event Name
                </label>
                <input
                  type="text"
                  value={publishEventName}
                  onChange={(e) => setPublishEventName(e.target.value)}
                  required
                  style={{
                    width: "100%",
                    padding: "8px 12px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    color: "#fff",
                    fontSize: "0.85rem",
                  }}
                />
              </div>

              <div style={{ marginBottom: "16px" }}>
                <label style={{ display: "block", fontSize: "0.82rem", fontWeight: 600, marginBottom: "4px" }}>
                  Payload JSON
                </label>
                <textarea
                  rows={8}
                  value={publishPayload}
                  onChange={(e) => setPublishPayload(e.target.value)}
                  style={{
                    width: "100%",
                    padding: "8px 12px",
                    borderRadius: "6px",
                    border: "1px solid var(--border, #334155)",
                    backgroundColor: "var(--bg-primary, #0f172a)",
                    color: "#fff",
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                  }}
                />
              </div>

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px" }}>
                <button
                  type="button"
                  onClick={() => setShowPublishModal(false)}
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
                  style={{
                    padding: "8px 16px",
                    borderRadius: "6px",
                    border: "none",
                    backgroundColor: "var(--accent, #3b82f6)",
                    color: "#fff",
                    cursor: "pointer",
                    fontSize: "0.85rem",
                    fontWeight: 600,
                  }}
                >
                  Publish Event
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
