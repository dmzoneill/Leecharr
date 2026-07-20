import { useState, useRef, useMemo, useCallback } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
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

interface EventEntry {
  id: number;
  timestamp: string;
  level: LogLevel;
  component: string;
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

function useEventEntries() {
  return useQuery<EventEntry[]>({
    queryKey: ["system", "events"],
    queryFn: async () => {
      const data = await apiClient.get<ApiLogEntry[]>("/log");
      return data.map((entry) => ({
        id: entry.id,
        timestamp: entry.time,
        level: toLogLevel(entry.level),
        component: entry.logger,
        message: entry.exception
          ? `${entry.message}\n${entry.exception}`
          : entry.message,
      }));
    },
    refetchInterval: 10000,
  });
}

function formatEventTime(iso: string): string {
  const d = new Date(iso);
  let hours = d.getHours();
  const minutes = d.getMinutes().toString().padStart(2, "0");
  const ampm = hours >= 12 ? "pm" : "am";
  hours = hours % 12;
  if (hours === 0) hours = 12;
  return `${hours}:${minutes}${ampm}`;
}

function RefreshIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="23 4 23 10 17 10" />
      <polyline points="1 20 1 14 7 14" />
      <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
    </svg>
  );
}

function ClearIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}

function SortArrow({ direction }: { direction: "asc" | "desc" }) {
  return (
    <svg
      width="10"
      height="10"
      viewBox="0 0 10 10"
      fill="currentColor"
      style={{ marginLeft: "4px", opacity: 0.8 }}
    >
      {direction === "asc" ? (
        <polygon points="5,2 9,8 1,8" />
      ) : (
        <polygon points="5,8 1,2 9,2" />
      )}
    </svg>
  );
}

function EventLevelIcon({ level }: { level: LogLevel }) {
  switch (level) {
    case "Info":
      return (
        <span
          className="badge badge-primary"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          INFO
        </span>
      );
    case "Warn":
      return (
        <span
          className="badge badge-queued"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          WARN
        </span>
      );
    case "Error":
      return (
        <span
          className="badge badge-error"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          ERROR
        </span>
      );
    case "Debug":
      return (
        <span
          className="badge badge-secondary"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          DEBUG
        </span>
      );
    case "Trace":
      return (
        <span
          className="badge badge-secondary"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          TRACE
        </span>
      );
  }
}

function SystemEvents() {
  const { data: entries, isLoading, isError } = useEventEntries();
  const queryClient = useQueryClient();
  const [sortDirection, setSortDirection] = useState<"asc" | "desc">("desc");
  const [cleared, setCleared] = useState(false);
  const tableBodyRef = useRef<HTMLDivElement>(null);

  const displayEntries = useMemo(() => {
    if (cleared) return [];
    if (!entries) return [];
    const sorted = [...entries];
    sorted.sort((a, b) => {
      const ta = new Date(a.timestamp).getTime();
      const tb = new Date(b.timestamp).getTime();
      return sortDirection === "desc" ? tb - ta : ta - tb;
    });
    return sorted;
  }, [entries, sortDirection, cleared]);

  const handleRefresh = useCallback(() => {
    setCleared(false);
    queryClient.invalidateQueries({ queryKey: ["system", "events"] });
  }, [queryClient]);

  const handleClear = useCallback(() => {
    setCleared(true);
  }, []);

  const toggleSort = useCallback(() => {
    setSortDirection((prev) => (prev === "desc" ? "asc" : "desc"));
  }, []);

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
              System: Events
            </h1>
            <span className="badge badge-primary">Audit Log</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Real-time audit log events, application exceptions, and background
            routine updates
          </div>
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <button
            className="btn btn-outline btn-small"
            onClick={handleRefresh}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <RefreshIcon />
            <span>Refresh</span>
          </button>
          <button
            className="btn btn-outline btn-small"
            onClick={handleClear}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <ClearIcon />
            <span>Clear</span>
          </button>
        </div>
      </div>

      {/* Events Table Card */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          border: "1px solid rgba(255, 255, 255, 0.08)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        <div className="torrent-table-wrapper" ref={tableBodyRef}>
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th" style={{ width: "90px" }}>
                  Level
                </th>
                <th
                  className="torrent-table-th"
                  onClick={toggleSort}
                  style={{
                    width: "120px",
                    cursor: "pointer",
                    userSelect: "none",
                  }}
                  title="Click to sort by timestamp"
                >
                  <span
                    style={{ display: "inline-flex", alignItems: "center" }}
                  >
                    Time <SortArrow direction={sortDirection} />
                  </span>
                </th>
                <th className="torrent-table-th" style={{ width: "200px" }}>
                  Component / Logger
                </th>
                <th className="torrent-table-th">Event Message</th>
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    Loading event stream...
                  </td>
                </tr>
              )}
              {!isLoading && isError && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    Failed to load events.
                  </td>
                </tr>
              )}
              {!isLoading && !isError && displayEntries.length === 0 && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    No recent events logged.
                  </td>
                </tr>
              )}
              {displayEntries.map((entry) => (
                <tr key={entry.id} className="torrent-table-row">
                  <td>
                    <EventLevelIcon level={entry.level} />
                  </td>
                  <td
                    style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}
                  >
                    {formatEventTime(entry.timestamp)}
                  </td>
                  <td>
                    <code
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--accent, #c8a84e)",
                      }}
                    >
                      {entry.component}
                    </code>
                  </td>
                  <td style={{ fontSize: "0.85rem", whiteSpace: "pre-wrap" }}>
                    {entry.message}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

export default SystemEvents;
