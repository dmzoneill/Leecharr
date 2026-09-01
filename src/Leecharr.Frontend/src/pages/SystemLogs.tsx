import { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "../api/client";
import SystemLogFiles from "./SystemLogFiles";

type LogLevel = "Trace" | "Debug" | "Info" | "Warn" | "Error";

interface ApiLogEntry {
  id: number;
  time: string;
  level: string;
  logger: string;
  message: string;
  exception: string | null;
}

interface LogEntry {
  id: number;
  timestamp: string;
  level: LogLevel;
  source: string;
  message: string;
}

const ALL_LEVELS: LogLevel[] = ["Trace", "Debug", "Info", "Warn", "Error"];

function toLogLevel(level: string): LogLevel {
  const normalized =
    level.charAt(0).toUpperCase() + level.slice(1).toLowerCase();
  if (ALL_LEVELS.includes(normalized as LogLevel)) {
    return normalized as LogLevel;
  }
  return "Info";
}

function useLogEntries(levelParam: LogLevel | null) {
  return useQuery<LogEntry[]>({
    queryKey: ["system", "log", levelParam],
    queryFn: async () => {
      const query = levelParam
        ? `?level=${encodeURIComponent(levelParam.toLowerCase())}`
        : "";
      const data = await apiClient.get<ApiLogEntry[]>(`/log${query}`);
      return data.map((entry) => ({
        id: entry.id,
        timestamp: entry.time,
        level: toLogLevel(entry.level),
        source: entry.logger,
        message: entry.exception
          ? `${entry.message}\n${entry.exception}`
          : entry.message,
      }));
    },
    refetchInterval: 10000,
  });
}

function formatTimestamp(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => n.toString().padStart(2, "0");
  const ms = d.getMilliseconds().toString().padStart(3, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${ms}`;
}

export function SystemLogs() {
  const [activeTab, setActiveTab] = useState<"live" | "files">("live");
  const [levelFilter, setLevelFilter] = useState<LogLevel | "All">("All");
  const [searchText, setSearchText] = useState("");
  const [autoScroll, setAutoScroll] = useState(true);
  const [cleared, setCleared] = useState(false);
  const logContentRef = useRef<HTMLDivElement>(null);

  const queryLevel = levelFilter === "All" ? null : levelFilter;
  const { data: rawEntries = [], isLoading } = useLogEntries(queryLevel);

  const filteredEntries = useMemo(() => {
    if (cleared) return [];
    let list = rawEntries;
    if (searchText.trim()) {
      const q = searchText.toLowerCase();
      list = list.filter(
        (e) =>
          e.message.toLowerCase().includes(q) ||
          e.source.toLowerCase().includes(q) ||
          e.level.toLowerCase().includes(q),
      );
    }
    return list;
  }, [rawEntries, searchText, cleared]);

  const handleClear = useCallback(() => {
    setCleared(true);
  }, []);

  // Auto-scroll to bottom
  useEffect(() => {
    if (autoScroll && logContentRef.current) {
      logContentRef.current.scrollTop = logContentRef.current.scrollHeight;
    }
  }, [filteredEntries, autoScroll]);

  return (
    <div className="content-area">
      {/* Page Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.25rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              System: Logs
            </h1>
            <span className="badge badge-primary">
              {activeTab === "live" ? "Live Stream" : "Log Files"}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Real-time server log stream, rolling disk log files, and diagnostic
            output
          </div>
        </div>

        {/* View Switcher Tabs */}
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button
            type="button"
            className={`btn btn-small ${activeTab === "live" ? "btn-primary" : "btn-outline"}`}
            onClick={() => setActiveTab("live")}
            style={{ fontSize: "0.8rem" }}
          >
            📺 Live Stream
          </button>
          <button
            type="button"
            className={`btn btn-small ${activeTab === "files" ? "btn-primary" : "btn-outline"}`}
            onClick={() => setActiveTab("files")}
            style={{ fontSize: "0.8rem" }}
          >
            📁 Disk Log Files
          </button>
        </div>
      </div>

      {activeTab === "files" ? (
        <SystemLogFiles />
      ) : (
        <div className="log-viewer">
          <div className="log-toolbar">
            <div className="log-toolbar-filters">
              {(["All", ...ALL_LEVELS] as const).map((level) => (
                <button
                  key={level}
                  title={
                    level === "All"
                      ? "Shows entries at or above the log level configured in Settings > Advanced"
                      : `Show ${level} entries and above`
                  }
                  className={`btn btn-small ${levelFilter === level ? "log-filter-active" : ""} ${level !== "All" ? `log-filter-${level.toLowerCase()}` : ""}`}
                  onClick={() => {
                    setLevelFilter(level);
                    setCleared(false);
                  }}
                >
                  {level}
                </button>
              ))}
            </div>

            <div className="log-toolbar-actions">
              <input
                type="text"
                className="search-input"
                placeholder="Filter logs..."
                value={searchText}
                onChange={(e) => {
                  setSearchText(e.target.value);
                  setCleared(false);
                }}
              />

              <label className="log-autoscroll-toggle">
                <input
                  type="checkbox"
                  checked={autoScroll}
                  onChange={(e) => setAutoScroll(e.target.checked)}
                />
                Auto-scroll
              </label>

              <button
                className="btn btn-small btn-secondary"
                onClick={handleClear}
                title="Clear current log display (does not delete logs from server)"
              >
                Clear
              </button>
            </div>
          </div>

          <div className="log-content" ref={logContentRef}>
            {isLoading ? (
              <div className="log-empty-state">Loading log entries...</div>
            ) : filteredEntries.length === 0 ? (
              <div className="log-empty-state">
                {cleared
                  ? "Log display cleared. New entries will appear above."
                  : searchText
                    ? "No log entries match the search filter."
                    : "No log entries available."}
              </div>
            ) : (
              <table className="log-table">
                <thead>
                  <tr>
                    <th style={{ width: "180px" }}>Timestamp</th>
                    <th style={{ width: "80px" }}>Level</th>
                    <th style={{ width: "160px" }}>Logger</th>
                    <th>Message</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredEntries.map((entry) => (
                    <tr
                      key={entry.id}
                      className={`log-row log-row-${entry.level.toLowerCase()}`}
                    >
                      <td className="log-cell-time">
                        {formatTimestamp(entry.timestamp)}
                      </td>
                      <td className="log-cell-level">
                        <span
                          className={`log-badge log-badge-${entry.level.toLowerCase()}`}
                        >
                          {entry.level}
                        </span>
                      </td>
                      <td className="log-cell-source" title={entry.source}>
                        {entry.source}
                      </td>
                      <td className="log-cell-message">{entry.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemLogs;
