import { useTranslation } from "../i18n";
import { Link } from "react-router";
import {
  useSystemStatus,
  useHealthChecks,
  useDiskSpace,
  useArrConnections,
  useDownloadClients,
  useIndexers,
} from "../api/hooks";
import { formatBytes, formatUptime } from "../utils/formatters";

function SystemStatus() {
  const { t } = useTranslation();
  const { data: status, isLoading: statusLoading } = useSystemStatus();
  const { data: health, isLoading: healthLoading } = useHealthChecks();
  const { data: diskSpace, isLoading: diskLoading } = useDiskSpace();
  const { data: arrConnections } = useArrConnections();
  const { data: downloadClients } = useDownloadClients();
  const { data: indexers } = useIndexers();

  const isLoading = statusLoading || healthLoading || diskLoading;

  const warningOrErrorChecks =
    health?.filter((c) => c.type === "Warning" || c.type === "Error") ?? [];

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
              {t("system.statusTitle")}
            </h1>
            <span className="badge badge-primary">
              {t("system.environment")}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("system.statusSubtitle")}
          </div>
        </div>

        {status && (
          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <Link
              to="/system/resources"
              className="btn btn-small"
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.35rem",
                backgroundColor: "rgba(255, 209, 102, 0.15)",
                color: "var(--accent, #ffd166)",
                border: "1px solid rgba(255, 209, 102, 0.3)",
              }}
            >
              <span>📊</span> {t("system.liveTelemetry")}
            </Link>
            <span
              className="badge badge-seeding"
              style={{ padding: "0.3rem 0.65rem", fontSize: "0.82rem" }}
            >
              {t("system.uptime")}{" "}
              {formatUptime(
                status.uptimeSeconds ??
                  (status.startTime
                    ? Math.floor(
                        (Date.now() - new Date(status.startTime).getTime()) /
                          1000,
                      )
                    : 0),
              )}
            </span>
          </div>
        )}
      </div>

      {isLoading && <p className="loading">{t("system.loadingStatus")}</p>}

      {/* Health Section Card */}
      <div
        className="card"
        style={{
          marginBottom: "1.25rem",
          borderRadius: "8px",
          border: "1px solid rgba(255, 255, 255, 0.08)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: "1.25rem",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "0.85rem",
          }}
        >
          <div>
            <h2
              style={{
                fontSize: "1.05rem",
                fontWeight: 600,
                color: "var(--accent, #c8a84e)",
                margin: 0,
              }}
            >
              {t("system.healthDiagnostics")}
            </h2>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {t("system.healthDiagnosticsSubtitle")}
            </div>
          </div>
          <span
            className={`badge ${warningOrErrorChecks.length === 0 ? "badge-seeding" : "badge-error"}`}
          >
            {warningOrErrorChecks.length === 0
              ? t("system.allSystemsOperational")
              : warningOrErrorChecks.length === 1
                ? t("system.healthIssuesSingle", { count: 1 })
                : t("system.healthIssuesPlural", {
                    count: warningOrErrorChecks.length,
                  })}
          </span>
        </div>

        {warningOrErrorChecks.length === 0 ? (
          <div
            style={{
              padding: "0.85rem 1.15rem",
              borderRadius: "6px",
              backgroundColor: "rgba(40, 167, 69, 0.12)",
              border: "1px solid rgba(40, 167, 69, 0.3)",
              color: "var(--success, #28a745)",
              fontSize: "0.875rem",
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>✓</span>
            <span>{t("system.allSystemsOperational")}</span>
          </div>
        ) : (
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            {warningOrErrorChecks.map((check, i) => {
              const isError = check.type === "Error";
              return (
                <div
                  key={i}
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    padding: "0.75rem 1rem",
                    borderRadius: "6px",
                    backgroundColor: isError
                      ? "rgba(220, 53, 69, 0.15)"
                      : "rgba(255, 193, 7, 0.12)",
                    border: `1px solid ${
                      isError
                        ? "rgba(220, 53, 69, 0.35)"
                        : "rgba(255, 193, 7, 0.3)"
                    }`,
                    color: isError
                      ? "var(--danger, #dc3545)"
                      : "var(--warning, #ffc107)",
                    fontSize: "0.875rem",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                    }}
                  >
                    <strong>
                      {t(`health.${check.source}.title`, check.source)}:
                    </strong>
                    <span>
                      {t(
                        `health.${check.source}.message`,
                        check.message || check.type,
                      )}
                    </span>
                  </div>
                  <Link
                    to="/settings/general"
                    className="btn btn-outline btn-small"
                    style={{
                      fontSize: "0.75rem",
                      textDecoration: "none",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {t("system.fixInSettings")}
                  </Link>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Arr & Download Client Integrations Diagnostic Table Card */}
      {((arrConnections && arrConnections.length > 0) ||
        (downloadClients && downloadClients.length > 0) ||
        (indexers && indexers.length > 0)) && (
        <div
          className="card"
          style={{
            marginBottom: "1.25rem",
            borderRadius: "8px",
            border: "1px solid rgba(255, 255, 255, 0.08)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            padding: 0,
            overflow: "hidden",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              padding: "1.1rem 1.25rem 0.85rem",
              borderBottom: "1px solid rgba(255, 255, 255, 0.06)",
            }}
          >
            <div>
              <h2
                style={{
                  fontSize: "1.05rem",
                  fontWeight: 600,
                  color: "var(--accent, #c8a84e)",
                  margin: 0,
                }}
              >
                {t("system.ecosystemEndpoints")}
              </h2>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted)",
                  marginTop: "0.2rem",
                }}
              >
                {t("system.ecosystemEndpointsSubtitle")}
              </div>
            </div>
            <Link
              to="/settings/connections"
              className="btn btn-outline btn-small"
              style={{ textDecoration: "none" }}
            >
              {t("system.manageInSettings")}
            </Link>
          </div>

          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">
                    {t("system.serviceName")}
                  </th>
                  <th className="torrent-table-th">{t("common.type")}</th>
                  <th className="torrent-table-th">
                    {t("system.endpointHost")}
                  </th>
                  <th className="torrent-table-th">{t("common.status")}</th>
                  <th className="torrent-table-th">
                    {t("system.integrationFeatures")}
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    {t("common.actions")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {arrConnections?.map((conn) => (
                  <tr key={`arr-${conn.id}`} className="torrent-table-row">
                    <td>
                      <strong style={{ color: "var(--text-primary)" }}>
                        {conn.name}
                      </strong>
                    </td>
                    <td>
                      <span className="badge badge-primary">
                        {conn.arrType}
                      </span>
                    </td>
                    <td>
                      <code style={{ fontSize: "0.8rem" }}>{conn.url}</code>
                    </td>
                    <td>
                      <span
                        className={`badge ${conn.enable ? "badge-seeding" : "badge-stopped"}`}
                      >
                        {conn.enable
                          ? t("common.enabled")
                          : t("common.disabled")}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          fontSize: "0.8rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {[
                          conn.syncEnabled && t("system.features.sync", "Sync"),
                          conn.enableAutomaticAdd &&
                            t("system.features.autoAdd", "Auto-Add"),
                          conn.webhookEnabled &&
                            t("system.features.webhook", "Webhook"),
                        ]
                          .filter(Boolean)
                          .join(" • ") || t("common.none", "None")}
                      </span>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {conn.url && (
                        <a
                          href={conn.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="btn btn-small btn-outline"
                          style={{
                            fontSize: "0.75rem",
                            textDecoration: "none",
                          }}
                          title={`Open ${conn.name} Web UI`}
                        >
                          {t("system.open")}
                        </a>
                      )}
                    </td>
                  </tr>
                ))}

                {downloadClients?.map((client) => (
                  <tr key={`client-${client.id}`} className="torrent-table-row">
                    <td>
                      <strong style={{ color: "var(--text-primary)" }}>
                        {client.name}
                      </strong>
                    </td>
                    <td>
                      <span className="badge badge-secondary">
                        {t(
                          `downloadClients.types.${client.clientType?.toLowerCase()}`,
                          client.clientType,
                        )}
                      </span>
                    </td>
                    <td>
                      <code style={{ fontSize: "0.8rem" }}>
                        {client.host}:{client.port}
                      </code>
                    </td>
                    <td>
                      <span
                        className={`badge ${client.enable ? "badge-seeding" : "badge-stopped"}`}
                      >
                        {client.enable
                          ? t("common.enabled")
                          : t("common.disabled")}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          fontSize: "0.8rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {t("system.downloadAgentClient")}
                      </span>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {client.host && (
                        <a
                          href={`${client.useSsl ? "https" : "http"}://${client.host}${client.port ? `:${client.port}` : ""}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="btn btn-small btn-outline"
                          style={{
                            fontSize: "0.75rem",
                            textDecoration: "none",
                          }}
                          title={`Open ${client.name} Web UI`}
                        >
                          {t("system.open")}
                        </a>
                      )}
                    </td>
                  </tr>
                ))}

                {indexers?.map((idx) => (
                  <tr key={`indexer-${idx.id}`} className="torrent-table-row">
                    <td>
                      <strong style={{ color: "var(--text-primary)" }}>
                        {idx.name}
                      </strong>
                    </td>
                    <td>
                      <span className="badge badge-secondary">
                        {t(
                          `indexers.types.${idx.indexerType?.toLowerCase()}`,
                          idx.indexerType,
                        )}
                      </span>
                    </td>
                    <td>
                      <code style={{ fontSize: "0.8rem" }}>
                        {idx.url || "-"}
                      </code>
                    </td>
                    <td>
                      <span
                        className={`badge ${idx.enable ? "badge-seeding" : "badge-stopped"}`}
                      >
                        {idx.enable
                          ? t("common.enabled")
                          : t("common.disabled")}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          fontSize: "0.8rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {[
                          idx.enableRss && t("system.features.rss", "RSS"),
                          idx.enableSearch &&
                            t("system.features.search", "Search"),
                        ]
                          .filter(Boolean)
                          .join(" • ") ||
                          t("system.features.indexer", "Indexer")}
                      </span>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {idx.url && (
                        <a
                          href={idx.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="btn btn-small btn-outline"
                          style={{
                            fontSize: "0.75rem",
                            textDecoration: "none",
                          }}
                          title={`Open ${idx.name} Web UI`}
                        >
                          {t("system.open")}
                        </a>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Disk Space Section Card */}
      <div
        className="card"
        style={{
          marginBottom: "1.25rem",
          borderRadius: "8px",
          border: "1px solid rgba(255, 255, 255, 0.08)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            padding: "1.1rem 1.25rem 0.85rem",
            borderBottom: "1px solid rgba(255, 255, 255, 0.06)",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              margin: 0,
            }}
          >
            {t("system.diskSpace")}
          </h2>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("system.diskSpaceSubtitle")}
          </div>
        </div>

        {diskSpace && diskSpace.length > 0 ? (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">{t("system.location")}</th>
                  <th className="torrent-table-th">{t("system.freeSpace")}</th>
                  <th className="torrent-table-th">{t("system.totalSpace")}</th>
                  <th className="torrent-table-th" style={{ width: "35%" }}>
                    {t("system.usage")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {diskSpace.map((d, i) => {
                  const usedPercent =
                    d.totalSpace > 0
                      ? ((d.totalSpace - d.freeSpace) / d.totalSpace) * 100
                      : 0;
                  let barClass = "disk-progress-bar";
                  if (usedPercent >= 90)
                    barClass += " disk-progress-bar-danger";
                  else if (usedPercent >= 75)
                    barClass += " disk-progress-bar-warning";
                  return (
                    <tr key={i} className="torrent-table-row">
                      <td>
                        <strong style={{ color: "var(--text-primary)" }}>
                          {d.label}
                        </strong>{" "}
                        <span
                          style={{
                            color: "var(--text-muted)",
                            fontSize: "0.8rem",
                          }}
                        >
                          ({d.path})
                        </span>
                      </td>
                      <td>{formatBytes(d.freeSpace)}</td>
                      <td>{formatBytes(d.totalSpace)}</td>
                      <td>
                        <div
                          className="disk-progress"
                          style={{ borderRadius: "4px", height: "18px" }}
                        >
                          <div
                            className={barClass}
                            style={{
                              width: `${usedPercent}%`,
                              borderRadius: "4px",
                            }}
                          />
                          <span
                            className="disk-progress-text"
                            style={{ fontWeight: 600 }}
                          >
                            {usedPercent.toFixed(1)}%
                          </span>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div style={{ padding: "1.25rem", color: "var(--text-muted)" }}>
            {t("system.noDiskVolumes")}
          </div>
        )}
      </div>

      {/* Grid for About & Resources */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
          gap: "1.25rem",
          marginBottom: "1.25rem",
        }}
      >
        {/* About Section Card */}
        {status && (
          <div
            className="card"
            style={{
              borderRadius: "8px",
              border: "1px solid rgba(255, 255, 255, 0.08)",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
              padding: "1.25rem",
            }}
          >
            <h2
              style={{
                fontSize: "1.05rem",
                fontWeight: 600,
                color: "var(--accent, #c8a84e)",
                marginTop: 0,
                marginBottom: "0.85rem",
              }}
            >
              {t("system.aboutTitle")}
            </h2>
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.6rem",
              }}
            >
              <div className="status-row">
                <span className="status-label">{t("system.version")}</span>
                <span className="status-value" style={{ fontWeight: 600 }}>
                  v{status.version}
                </span>
              </div>
              {status.instanceUuid && (
                <div className="status-row">
                  <span className="status-label">
                    {t("system.instanceUuid")}
                  </span>
                  <span className="status-value">
                    <code style={{ fontSize: "0.82rem" }}>
                      {status.instanceUuid}
                    </code>
                  </span>
                </div>
              )}
              <div className="status-row">
                <span className="status-label">{t("system.netRuntime")}</span>
                <span className="status-value">
                  {status.runtimeName || ".NET"} ({status.runtimeVersion})
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">{t("system.database")}</span>
                <span className="status-value">
                  {status.databaseVersion || "SQLite"}
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">
                  {t("system.databaseMigration")}
                </span>
                <span className="status-value">
                  {status.databaseMigration
                    ? t("system.schemaVersion", {
                        version: status.databaseMigration,
                      })
                    : t("system.schemaCurrent")}
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">{t("system.appdataDir")}</span>
                <span className="status-value">
                  <code>
                    {status.appDataPath || status.appDataFolder || "-"}
                  </code>
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">{t("system.startupDir")}</span>
                <span className="status-value">
                  <code>{status.startupPath || "-"}</code>
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">
                  {t("system.executionMode")}
                </span>
                <span className="status-value">
                  <span className="badge badge-primary">
                    {status.isDocker
                      ? t("system.executionDocker")
                      : t("system.executionConsole")}
                    {status.isDebug ? " (Debug)" : ""}
                  </span>
                </span>
              </div>
            </div>
          </div>
        )}

        {/* Resources & Links Card */}
        <div
          className="card"
          style={{
            borderRadius: "8px",
            border: "1px solid rgba(255, 255, 255, 0.08)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            padding: "1.25rem",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              marginTop: 0,
              marginBottom: "0.85rem",
            }}
          >
            {t("system.resourcesLinks")}
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">
                {t("system.officialWebsite")}
              </span>
              <span className="status-value">
                <a
                  href="https://www.leecharr.net"
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    color: "var(--accent, #c8a84e)",
                    textDecoration: "none",
                  }}
                >
                  {t("system.websiteUrl")}
                </a>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">{t("system.sourceCode")}</span>
              <span className="status-value">
                <a
                  href="https://github.com/dmzoneill/Leecharr"
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    color: "var(--accent, #c8a84e)",
                    textDecoration: "none",
                  }}
                >
                  {t("system.githubRepo")}
                </a>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">{t("system.issueTracker")}</span>
              <span className="status-value">
                <a
                  href="https://github.com/dmzoneill/Leecharr/issues"
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    color: "var(--accent, #c8a84e)",
                    textDecoration: "none",
                  }}
                >
                  {t("system.reportIssue")}
                </a>
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default SystemStatus;
