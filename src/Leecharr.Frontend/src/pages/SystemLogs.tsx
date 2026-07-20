import { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "../api/client";

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
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${ms}`;
}

function SystemLogs() {
  const [levelFilter, setLevelFilter] = useState<LogLevel | "All">("All");
  const {
    data: entries,
    isLoading,
    isError,
  } = useLogEntries(levelFilter === "All" ? null : levelFilter);
  const [searchText, setSearchText] = useState("");
  const [autoScroll, setAutoScroll] = useState(true);
  const [cleared, setCleared] = useState(false);
  const logContentRef = useRef<HTMLDivElement>(null);

  const filteredEntries = useMemo(() => {
    if (cleared) return [];
    if (!entries) return [];
    if (!searchText) return entries;
    const q = searchText.toLowerCase();
    return entries.filter(
      (entry) =>
        entry.message.toLowerCase().includes(q) ||
        entry.source.toLowerCase().includes(q) ||
        entry.level.toLowerCase().includes(q),
    );
  }, [entries, searchText, cleared]);

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
            <span className="badge badge-primary">Console</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Real-time server log stream and diagnostic output
          </div>
        </div>
      </div>

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
            <label className="log-auto-scroll">
              <input
                type="checkbox"
                checked={autoScroll}
                onChange={(e) => setAutoScroll(e.target.checked)}
              />
              <span>Auto-scroll</span>
            </label>
            <button className="btn btn-small btn-outline" onClick={handleClear}>
              Clear
            </button>
          </div>
        </div>

        <div className="log-content" ref={logContentRef}>
          {isLoading && <p className="loading">Loading logs...</p>}
          {!isLoading && isError && (
            <p className="log-empty">Failed to load log entries.</p>
          )}
          {!isLoading && !isError && filteredEntries.length === 0 && (
            <p className="log-empty">No log entries</p>
          )}
          {filteredEntries.map((entry) => (
            <div key={entry.id} className="log-entry">
              <span className="log-timestamp">
                {formatTimestamp(entry.timestamp)}
              </span>
              <span
                className={`log-level log-level-${entry.level.toLowerCase()}`}
              >
                {entry.level.toUpperCase().padEnd(5)}
              </span>
              <span className="log-source">{entry.source}</span>
              <span className="log-message" style={{ whiteSpace: "pre-wrap" }}>
                {entry.message}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

export default SystemLogs;
