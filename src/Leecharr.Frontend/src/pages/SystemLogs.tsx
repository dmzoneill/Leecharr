import { useTranslation } from "../i18n";
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
  const { t } = useTranslation();

  const [activeTab, setActiveTab] = useState<"live" | "files">("live");
  const [levelFilter, setLevelFilter] = useState<LogLevel | "All">("All");
  const [searchText, setSearchText] = useState("");
  const [autoScroll, setAutoScroll] = useState(true);
  const [clearedBeforeId, setClearedBeforeId] = useState<number | null>(null);
  const logContentRef = useRef<HTMLDivElement>(null);

  const queryLevel = levelFilter === "All" ? null : levelFilter;
  const { data: rawEntries = [], isLoading } = useLogEntries(queryLevel);

  const filteredEntries = useMemo(() => {
    let list = rawEntries;
    if (clearedBeforeId !== null) {
      list = list.filter((e) => e.id > clearedBeforeId);
    }
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
  }, [rawEntries, searchText, clearedBeforeId]);

  const handleClear = useCallback(() => {
    setClearedBeforeId(rawEntries.reduce((max, e) => Math.max(max, e.id), 0));
  }, [rawEntries]);

  // Auto-scroll to bottom
  useEffect(() => {
    if (autoScroll && logContentRef.current) {
      logContentRef.current.scrollTop = logContentRef.current.scrollHeight;
    }
  }, [filteredEntries, autoScroll]);

  return (
    <div className="content-area system-logs-page">
      {/* Page Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "0.85rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {t("system.logsTitle")}
            </h1>
            <span className="badge badge-primary">
              {activeTab === "live"
                ? t("system.liveStream")
                : t("system.diskFiles")}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("system.logsSubtitle")}
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
            {t("system.liveStream")}
          </button>
          <button
            type="button"
            className={`btn btn-small ${activeTab === "files" ? "btn-primary" : "btn-outline"}`}
            onClick={() => setActiveTab("files")}
            style={{ fontSize: "0.8rem" }}
          >
            {t("system.diskFiles")}
          </button>
        </div>
      </div>

      {activeTab === "files" ? (
        <div style={{ flex: 1, minHeight: 0, overflowY: "auto" }}>
          <SystemLogFiles embedded />
        </div>
      ) : (
        <div className="log-viewer">
          <div className="log-toolbar">
            <div className="log-toolbar-filters">
              {(["All", ...ALL_LEVELS] as const).map((level) => (
                <button
                  key={level}
                  title={
                    level === "All"
                      ? t(
                          "logging.showAllConfigured",
                          "Shows entries at or above the log level configured in Settings > Advanced",
                        )
                      : t(
                          "logging.showLevelAndAbove",
                          "Show {level} entries and above",
                          { level },
                        )
                  }
                  className={`btn btn-small ${levelFilter === level ? "log-filter-active" : ""} ${level !== "All" ? `log-filter-${level.toLowerCase()}` : ""}`}
                  onClick={() => {
                    setLevelFilter(level);
                    setClearedBeforeId(null);
                  }}
                >
                  {level === "All"
                    ? t("logging.levels.all", "All")
                    : t(`logging.levels.${level.toLowerCase()}`, level)}
                </button>
              ))}
            </div>

            <div className="log-toolbar-actions">
              <input
                type="text"
                className="search-input"
                placeholder={t("system.filterLogs")}
                value={searchText}
                onChange={(e) => {
                  setSearchText(e.target.value);
                  setClearedBeforeId(null);
                }}
              />

              <label className="log-autoscroll-toggle">
                <input
                  type="checkbox"
                  checked={autoScroll}
                  onChange={(e) => setAutoScroll(e.target.checked)}
                />
                {t("system.autoScroll")}
              </label>

              <button
                className="btn btn-small btn-secondary"
                onClick={handleClear}
                title={t("system.clearLogDisplay")}
              >
                {t("common.clear")}
              </button>
            </div>
          </div>

          <div className="log-content" ref={logContentRef}>
            {isLoading ? (
              <div className="log-empty-state">
                {t("system.loadingLogEntries")}
              </div>
            ) : filteredEntries.length === 0 ? (
              <div className="log-empty-state">
                {clearedBeforeId !== null
                  ? t(
                      "system.logClearedNotice",
                      "Log display cleared. New entries will appear above.",
                    )
                  : searchText
                    ? t(
                        "system.noLogMatches",
                        "No log entries match the search filter.",
                      )
                    : t("system.noLogEntries", "No log entries available.")}
              </div>
            ) : (
              <table className="log-table">
                <thead>
                  <tr>
                    <th style={{ width: "180px" }}>{t("system.timestamp")}</th>
                    <th style={{ width: "80px" }}>{t("system.severity")}</th>
                    <th style={{ width: "160px" }}>{t("system.logger")}</th>
                    <th>{t("system.message")}</th>
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
                          {t(
                            `logging.levels.${entry.level.toLowerCase()}`,
                            entry.level,
                          )}
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
