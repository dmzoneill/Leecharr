import { useState, useMemo } from "react";
import { useTranslation } from "../../i18n";
import type { Torrent } from "../../api/types";
import { useTorrentLogs } from "../../api/hooks";
import { formatDate } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";

function levelBadgeClass(level?: string): string {
  const l = (level || "info").toLowerCase();
  switch (l) {
    case "debug":
    case "trace":
      return "torrent-log-level-debug";
    case "warn":
    case "warning":
      return "torrent-log-level-warn";
    case "error":
    case "fatal":
      return "torrent-log-level-error";
    default:
      return "torrent-log-level-info";
  }
}

function sourceBadgeStyle(source?: string): React.CSSProperties {
  const s = (source || "engine").toLowerCase();
  switch (s) {
    case "tracker":
      return {
        backgroundColor: "rgba(59, 130, 246, 0.2)",
        color: "#60a5fa",
        borderColor: "rgba(59, 130, 246, 0.4)",
      };
    case "peers":
    case "peer":
      return {
        backgroundColor: "rgba(168, 85, 247, 0.2)",
        color: "#c084fc",
        borderColor: "rgba(168, 85, 247, 0.4)",
      };
    case "seeding":
    case "seeder":
      return {
        backgroundColor: "rgba(34, 197, 94, 0.2)",
        color: "#4ade80",
        borderColor: "rgba(34, 197, 94, 0.4)",
      };
    case "trackerboost":
      return {
        backgroundColor: "rgba(245, 158, 11, 0.2)",
        color: "#fbbf24",
        borderColor: "rgba(245, 158, 11, 0.4)",
      };
    default:
      return {
        backgroundColor: "rgba(148, 163, 184, 0.2)",
        color: "#cbd5e1",
        borderColor: "rgba(148, 163, 184, 0.4)",
      };
  }
}

export function LogTab({
  torrent,
  torrentId,
}: {
  torrent?: Torrent;
  torrentId?: number;
}) {
  const { t } = useTranslation();
  const [isLive, setIsLive] = useState(true);
  const [levelFilter, setLevelFilter] = useState<string>("ALL");
  const [sourceFilter, setSourceFilter] = useState<string>("ALL");
  const [searchTerm, setSearchTerm] = useState<string>("");
  const [copied, setCopied] = useState(false);

  const effectiveId = torrentId ?? torrent?.id ?? 0;
  const {
    data: rawLogs,
    isLoading,
    isError,
    refetch,
  } = useTorrentLogs(effectiveId, {
    polling: isLive,
  });

  const logs = useMemo(() => {
    return (rawLogs ?? []).slice(0, 100);
  }, [rawLogs]);

  const sources = useMemo(() => {
    const set = new Set<string>();
    for (const log of logs) {
      if (log.source) set.add(log.source);
    }
    return Array.from(set).sort();
  }, [logs]);

  const filteredLogs = useMemo(() => {
    return logs.filter((entry) => {
      const entryLevel = (entry.level || "INFO").toUpperCase();
      if (levelFilter !== "ALL" && entryLevel !== levelFilter) {
        return false;
      }
      const entrySource = (entry.source || "Engine").toLowerCase();
      if (
        sourceFilter !== "ALL" &&
        entrySource !== sourceFilter.toLowerCase()
      ) {
        return false;
      }
      if (searchTerm) {
        const query = searchTerm.toLowerCase();
        const matchMsg = (entry.message || "").toLowerCase().includes(query);
        const matchSrc = entrySource.includes(query);
        const matchLvl = entryLevel.toLowerCase().includes(query);
        if (!matchMsg && !matchSrc && !matchLvl) return false;
      }
      return true;
    });
  }, [logs, levelFilter, sourceFilter, searchTerm]);

  function copyLogsToClipboard() {
    const text = filteredLogs
      .map(
        (l) =>
          `[${formatDate(l.timestamp || l.timeStamp || null)}] [${(l.level || "INFO").toUpperCase()}] [${l.source || "Engine"}] ${l.message || ""}`,
      )
      .join("\n");
    if (
      typeof navigator !== "undefined" &&
      navigator.clipboard &&
      navigator.clipboard.writeText
    ) {
      navigator.clipboard
        .writeText(text)
        .then(() => {
          setCopied(true);
          setTimeout(() => setCopied(false), 2000);
        })
        .catch((err) => {
          console.warn("Failed to copy logs to clipboard:", err);
        });
    }
  }

  if (isLoading && logs.length === 0)
    return <PanelLoading>{t("torrents.detail.loadingLogs")}</PanelLoading>;
  if (isError && logs.length === 0)
    return <PanelEmpty>{t("torrents.detail.failedToLoadLogs")}</PanelEmpty>;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
      {/* Controls toolbar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.4rem",
          paddingBottom: "0.35rem",
          borderBottom: "1px solid var(--border-light, rgba(255,255,255,0.08))",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.25rem",
              fontSize: "0.7rem",
              fontWeight: 500,
              padding: "0.15rem 0.4rem",
              borderRadius: "10px",
              backgroundColor: isLive
                ? "rgba(34, 197, 94, 0.15)"
                : "rgba(148, 163, 184, 0.15)",
              color: isLive ? "#4ade80" : "#94a3b8",
              border: `1px solid ${isLive ? "rgba(34, 197, 94, 0.3)" : "rgba(148, 163, 184, 0.3)"}`,
            }}
          >
            <span
              style={{
                width: "5px",
                height: "5px",
                borderRadius: "50%",
                backgroundColor: isLive ? "#22c55e" : "#94a3b8",
              }}
            />
            {isLive ? t("torrents.detail.live3s") : t("torrents.detail.paused")}
          </span>
          <span style={{ fontSize: "0.72rem", color: "var(--text-dim, #888)" }}>
            {t("torrents.detail.countOfLatest", {
              filtered: filteredLogs.length,
              total: logs.length,
            })}
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.3rem" }}>
          <button
            className={`btn btn-xs ${isLive ? "btn-outline" : "btn-primary"}`}
            style={{ fontSize: "0.72rem", padding: "0.15rem 0.4rem" }}
            onClick={() => setIsLive(!isLive)}
          >
            {isLive
              ? `⏸ ${t("torrents.detail.pauseLogs")}`
              : `▶ ${t("torrents.detail.resumeLogs")}`}
          </button>
          <button
            className="btn btn-outline btn-xs"
            style={{ fontSize: "0.72rem", padding: "0.15rem 0.4rem" }}
            onClick={() => refetch()}
          >
            🔄 {t("torrents.detail.refresh")}
          </button>
          <button
            className="btn btn-outline btn-xs"
            style={{ fontSize: "0.72rem", padding: "0.15rem 0.4rem" }}
            onClick={copyLogsToClipboard}
          >
            {copied
              ? `✓ ${t("common.copied")}`
              : `📋 ${t("torrents.detail.copyLogs")}`}
          </button>
        </div>
      </div>

      {/* Filter and search bar */}
      <div
        style={{
          display: "flex",
          gap: "0.4rem",
          flexWrap: "wrap",
          alignItems: "center",
        }}
      >
        <input
          type="text"
          className="form-control"
          placeholder={t("torrents.detail.filterLogsPlaceholder")}
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          style={{
            flex: "1 1 120px",
            padding: "0.2rem 0.45rem",
            fontSize: "0.75rem",
            height: "26px",
            backgroundColor: "var(--bg-primary)",
            color: "inherit",
          }}
        />
        <div style={{ display: "flex", gap: "0.2rem" }}>
          {["ALL", "INFO", "DEBUG", "WARN", "ERROR"].map((lvl) => (
            <button
              key={lvl}
              className={`btn btn-xs ${levelFilter === lvl ? "btn-primary" : "btn-outline"}`}
              style={{ fontSize: "0.68rem", padding: "0.1rem 0.35rem" }}
              onClick={() => setLevelFilter(lvl)}
            >
              {lvl}
            </button>
          ))}
        </div>
      </div>

      {/* Table wrap */}
      <div
        className="detail-panel-table-wrap"
        style={{
          maxHeight: "360px",
          overflowY: "auto",
          backgroundColor: "#0d1117",
          borderRadius: "4px",
        }}
      >
        <table
          className="torrent-table"
          style={{
            fontSize: "0.75rem",
            width: "100%",
            borderCollapse: "collapse",
          }}
        >
          <thead>
            <tr style={{ backgroundColor: "#161b22" }}>
              <th
                className="torrent-table-th"
                style={{ width: "130px", padding: "0.3rem 0.4rem" }}
              >
                {t("torrents.detail.colTime")}
              </th>
              <th
                className="torrent-table-th"
                style={{ width: "65px", padding: "0.3rem 0.4rem" }}
              >
                {t("torrents.detail.colLevel")}
              </th>
              <th
                className="torrent-table-th"
                style={{ width: "85px", padding: "0.3rem 0.4rem" }}
              >
                {t("torrents.detail.colSource")}
              </th>
              <th
                className="torrent-table-th"
                style={{ padding: "0.3rem 0.4rem" }}
              >
                {t("torrents.detail.colEventDetails")}
              </th>
            </tr>
          </thead>
          <tbody
            style={{
              fontFamily:
                "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace",
            }}
          >
            {filteredLogs.length === 0 ? (
              <tr className="torrent-table-row">
                <td
                  colSpan={4}
                  style={{
                    color: "var(--text-dim)",
                    textAlign: "center",
                    padding: "1.5rem",
                  }}
                >
                  {logs.length === 0
                    ? t("torrents.detail.noEventsRecorded")
                    : t("torrents.detail.noLogsMatch")}
                </td>
              </tr>
            ) : (
              filteredLogs.map((entry) => {
                const entryLevel = (entry.level || "INFO").toUpperCase();
                const entrySource = entry.source || "Engine";
                const entryTime = entry.timestamp || entry.timeStamp || null;
                return (
                  <tr
                    key={entry.id}
                    className="torrent-table-row"
                    style={{
                      borderBottom: "1px solid rgba(255,255,255,0.04)",
                      backgroundColor:
                        entryLevel === "ERROR"
                          ? "rgba(239, 68, 68, 0.08)"
                          : entryLevel === "WARN"
                            ? "rgba(245, 158, 11, 0.08)"
                            : "transparent",
                    }}
                  >
                    <td
                      style={{
                        color: "#8b949e",
                        whiteSpace: "nowrap",
                        padding: "0.25rem 0.4rem",
                      }}
                    >
                      {formatDate(entryTime)}
                    </td>
                    <td style={{ padding: "0.25rem 0.4rem" }}>
                      <span
                        className={`torrent-log-level ${levelBadgeClass(entry.level)}`}
                        style={{ fontSize: "0.65rem" }}
                      >
                        {entryLevel}
                      </span>
                    </td>
                    <td style={{ padding: "0.25rem 0.4rem" }}>
                      <span
                        style={{
                          display: "inline-block",
                          padding: "0.08rem 0.3rem",
                          borderRadius: "2px",
                          fontSize: "0.68rem",
                          fontWeight: 600,
                          border: "1px solid",
                          ...sourceBadgeStyle(entrySource),
                        }}
                      >
                        {entrySource}
                      </span>
                    </td>
                    <td
                      style={{
                        color: entryLevel === "ERROR" ? "#fca5a5" : "#e6edf3",
                        wordBreak: "break-word",
                        lineHeight: "1.3",
                        padding: "0.25rem 0.4rem",
                      }}
                    >
                      {entry.message}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
