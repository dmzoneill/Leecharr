import React, { useState, useMemo } from "react";
import { useTrackerBoostLogs, useClearTrackerBoostLogs } from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import TrackerFavicon from "../../components/TrackerFavicon";

export function LogViewer() {
  const { showToast } = useToast();

  const [logLevelFilter, setLogLevelFilter] = useState<string>("all");
  const [logCategoryFilter, setLogCategoryFilter] = useState<string>("all");
  const [logSearch, setLogSearch] = useState<string>("");
  const [logAutoRefresh, setLogAutoRefresh] = useState<boolean>(true);

  const {
    data: boostLogs,
    isLoading: logsLoading,
    refetch: refetchLogs,
  } = useTrackerBoostLogs(
    250,
    logCategoryFilter,
    logLevelFilter,
    logAutoRefresh ? 3000 : false,
  );
  const clearLogs = useClearTrackerBoostLogs();

  const handleClearLogs = () => {
    clearLogs.mutate(undefined, {
      onSuccess: () => {
        showToast("Activity logs cleared", "info");
      },
      onError: (err) => {
        showToast(`Failed to clear logs: ${err.message}`, "error");
      },
    });
  };

  const filteredLogs = useMemo(() => {
    return (boostLogs ?? []).filter((l) => {
      if (!logSearch.trim()) return true;
      const q = logSearch.toLowerCase();
      return (
        (l.message && l.message.toLowerCase().includes(q)) ||
        (l.trackerUrl && l.trackerUrl.toLowerCase().includes(q)) ||
        (l.infoHash && l.infoHash.toLowerCase().includes(q)) ||
        (l.category && l.category.toLowerCase().includes(q)) ||
        (l.level && l.level.toLowerCase().includes(q))
      );
    });
  }, [boostLogs, logSearch]);

  return (
    <div
      className="card"
      style={{
        padding: "1.25rem",
        flex: "1 1 auto",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        marginBottom: "0.5rem",
      }}
    >
      {/* Controls bar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1rem",
          paddingBottom: "1rem",
          borderBottom: "1px solid var(--border-color)",
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <select
            className="form-control"
            style={{
              width: "150px",
              padding: "0.4rem 0.6rem",
              fontSize: "0.82rem",
            }}
            value={logCategoryFilter}
            onChange={(e) => setLogCategoryFilter(e.target.value)}
          >
            <option value="all">{t("trackerBoost.allCategories", "All Categories")}</option>
            <option value="Scrape">🔍 Scrapes</option>
            <option value="Health">🩺 Health Probes</option>
            <option value="Discovery">📡 Discovery</option>
            <option value="Inject">⚡ Injections</option>
            <option value="Cycle">⚙️ Daemon Cycles</option>
            <option value="General">{t("trackerBoost.general", "General")}</option>
          </select>

          <select
            className="form-control"
            style={{
              width: "130px",
              padding: "0.4rem 0.6rem",
              fontSize: "0.82rem",
            }}
            value={logLevelFilter}
            onChange={(e) => setLogLevelFilter(e.target.value)}
          >
            <option value="all">{t("trackerBoost.allLevels", "All Levels")}</option>
            <option value="Success">🟢 Success</option>
            <option value="Info">🔵 Info</option>
            <option value="Warn">🟡 Warning</option>
            <option value="Error">🔴 Error</option>
          </select>

          <input
            type="text"
            className="form-control"
            style={{
              width: "240px",
              padding: "0.4rem 0.75rem",
              fontSize: "0.82rem",
            }}
            placeholder={t("trackerBoost.searchLogs", "Search logs, hosts, hashes...")}
            value={logSearch}
            onChange={(e) => setLogSearch(e.target.value)}
          />
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              fontSize: "0.82rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={logAutoRefresh}
              onChange={(e) => setLogAutoRefresh(e.target.checked)}
            />
            <span>{t("trackerBoost.liveRefresh", "Live Refresh (3s)")}</span>
          </label>

          <button
            className="btn btn-outline"
            style={{ fontSize: "0.82rem", padding: "0.4rem 0.75rem" }}
            onClick={() => refetchLogs()}
            title={t("common.refresh", "Refresh log entries")}
          >
            🔄 Refresh
          </button>

          <button
            className="btn btn-danger"
            style={{ fontSize: "0.82rem", padding: "0.4rem 0.75rem" }}
            onClick={handleClearLogs}
            disabled={clearLogs.isPending || (boostLogs ?? []).length === 0}
            title={t("common.clear", "Clear current log buffer")}
          >
            🗑️ Clear Logs
          </button>
        </div>
      </div>

      {/* Logs Table / Console */}
      {logsLoading ? (
        <div
          style={{
            padding: "3rem",
            textAlign: "center",
            color: "var(--text-muted)",
          }}
        >
          Loading daemon activity logs...
        </div>
      ) : filteredLogs.length === 0 ? (
        <div
          style={{
            padding: "3rem",
            textAlign: "center",
            color: "var(--text-muted)",
          }}
        >
          No log entries found matching current filter.
        </div>
      ) : (
        <div
          className="torrent-table-wrapper"
          style={{
            borderRadius: "6px",
            border: "1px solid var(--border)",
            flex: "1 1 auto",
            minHeight: 0,
            overflowY: "auto",
            backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
          }}
        >
          <table
            className="torrent-table"
            style={{ width: "100%", fontSize: "0.82rem" }}
          >
            <thead
              style={{
                position: "sticky",
                top: 0,
                zIndex: 2,
                backgroundColor: "var(--bg-secondary)",
              }}
            >
              <tr>
                <th className="torrent-table-th" style={{ width: "10%" }}>
                  Time
                </th>
                <th className="torrent-table-th" style={{ width: "9%" }}>
                  Level
                </th>
                <th className="torrent-table-th" style={{ width: "12%" }}>
                  Category
                </th>
                <th className="torrent-table-th" style={{ width: "24%" }}>
                  Tracker / InfoHash
                </th>
                <th className="torrent-table-th" style={{ width: "45%" }}>
                  Activity Message
                </th>
              </tr>
            </thead>
            <tbody>
              {filteredLogs.map((log) => {
                const levelClass =
                  log.level === "Success"
                    ? "badge-success"
                    : log.level === "Error"
                      ? "badge-danger"
                      : log.level === "Warn"
                        ? "badge-warning"
                        : "badge-primary";

                return (
                  <tr key={log.id} className="torrent-table-row">
                    <td
                      style={{
                        fontFamily: "monospace",
                        color: "var(--text-muted)",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {new Date(log.timestamp).toLocaleTimeString()}
                    </td>
                    <td>
                      <span
                        className={`badge ${levelClass}`}
                        style={{ fontSize: "0.72rem" }}
                      >
                        {log.level === "Success"
                          ? "🟢 Success"
                          : log.level === "Error"
                            ? "🔴 Error"
                            : log.level === "Warn"
                              ? "🟡 Warn"
                              : "🔵 Info"}
                      </span>
                    </td>
                    <td>
                      <span
                        className="badge badge-secondary"
                        style={{ fontSize: "0.72rem" }}
                      >
                        {log.category}
                      </span>
                    </td>
                    <td
                      style={{
                        fontFamily: "monospace",
                        fontSize: "0.78rem",
                        wordBreak: "break-all",
                      }}
                    >
                      {log.trackerUrl ? (
                        <div
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.35rem",
                          }}
                        >
                          <TrackerFavicon
                            urlOrHost={log.trackerUrl}
                            size={13}
                          />
                          <span style={{ color: "var(--accent)" }}>
                            {log.trackerUrl}
                          </span>
                        </div>
                      ) : log.infoHash ? (
                        <span style={{ color: "var(--text-muted)" }}>
                          {log.infoHash.slice(0, 16)}...
                        </span>
                      ) : (
                        <span style={{ color: "var(--text-dim)" }}>-</span>
                      )}
                    </td>
                    <td style={{ wordBreak: "break-word" }}>{log.message}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default LogViewer;
