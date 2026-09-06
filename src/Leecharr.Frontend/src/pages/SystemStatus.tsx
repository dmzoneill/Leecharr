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
              {t("autogen.t_system_status")}
            </h1>
            <span className="badge badge-primary">
              {t("autogen.t_environment")}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("autogen.t_runtime_environment_service_health_check")}
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
              <span>📊</span> {t("autogen.t_live_telemetry")}
            </Link>
            <span
              className="badge badge-seeding"
              style={{ padding: "0.3rem 0.65rem", fontSize: "0.82rem" }}
            >
              {t("autogen.t_uptime")}{" "}
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

      {isLoading && (
        <p className="loading">{t("autogen.t_loading_system_status")}</p>
      )}

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
              {t("autogen.t_system_health_diagnostics")}
            </h2>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {t("autogen.t_automated_configuration_verifiers_and_da")}
            </div>
          </div>
          <span
            className={`badge ${warningOrErrorChecks.length === 0 ? "badge-seeding" : "badge-error"}`}
          >
            {warningOrErrorChecks.length === 0
              ? "All Systems Operational"
              : `${warningOrErrorChecks.length} Issue${warningOrErrorChecks.length > 1 ? "s" : ""}`}
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
            <span>
              {t("autogen.t_all_background_tasks_and_service_configu")}
            </span>
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
                    <strong>{check.source}:</strong>
                    <span>{check.message || check.type}</span>
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
                    {t("autogen.t_fix_in_settings")}
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
                {t("autogen.t_ecosystem_integration_endpoints")}
              </h2>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted)",
                  marginTop: "0.2rem",
                }}
              >
                {t("autogen.t_connected_media_managers_arr_indexers_an")}
              </div>
            </div>
            <Link
              to="/settings/connections"
              className="btn btn-outline btn-small"
              style={{ textDecoration: "none" }}
            >
              {t("autogen.t_manage_in_settings")}
            </Link>
          </div>

          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">
                    {t("autogen.t_service_name")}
                  </th>
                  <th className="torrent-table-th">{t("autogen.t_type")}</th>
                  <th className="torrent-table-th">
                    {t("autogen.t_endpoint_host")}
                  </th>
                  <th className="torrent-table-th">{t("autogen.t_state")}</th>
                  <th className="torrent-table-th">
                    {t("autogen.t_integration_features")}
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    {t("autogen.t_actions")}
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
                        {conn.enable ? "Enabled" : "Disabled"}
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
                          conn.syncEnabled && "Sync",
                          conn.enableAutomaticAdd && "Auto-Add",
                          conn.webhookEnabled && "Webhook",
                        ]
                          .filter(Boolean)
                          .join(" • ") || "None"}
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
                          {t("autogen.t_open")}
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
                        {client.clientType}
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
                        {client.enable ? "Enabled" : "Disabled"}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          fontSize: "0.8rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {t("autogen.t_download_agent_client")}
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
                          {t("autogen.t_open")}
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
                        {idx.indexerType}
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
                        {idx.enable ? "Enabled" : "Disabled"}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          fontSize: "0.8rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        {[idx.enableRss && "RSS", idx.enableSearch && "Search"]
                          .filter(Boolean)
                          .join(" • ") || "Indexer"}
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
                          {t("autogen.t_open")}
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
            {t("autogen.t_disk_space_mount_volumes")}
          </h2>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("autogen.t_host_storage_drives_and_mount_points_ava")}
          </div>
        </div>

        {diskSpace && diskSpace.length > 0 ? (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">
                    {t("autogen.t_location")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_free_space")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_total_space")}
                  </th>
                  <th className="torrent-table-th" style={{ width: "35%" }}>
                    {t("autogen.t_usage")}
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
            {t("autogen.t_no_disk_volume_information_available")}
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
              {t("autogen.t_about_leecharr")}
            </h2>
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.6rem",
              }}
            >
              <div className="status-row">
                <span className="status-label">{t("autogen.t_version")}</span>
                <span className="status-value" style={{ fontWeight: 600 }}>
                  v{status.version}
                </span>
              </div>
              {status.instanceUuid && (
                <div className="status-row">
                  <span className="status-label">
                    {t("autogen.t_instance_uuid")}
                  </span>
                  <span className="status-value">
                    <code style={{ fontSize: "0.82rem" }}>
                      {status.instanceUuid}
                    </code>
                  </span>
                </div>
              )}
              <div className="status-row">
                <span className="status-label">
                  {t("autogen.t_net_runtime")}
                </span>
                <span className="status-value">
                  {status.runtimeName || ".NET"} ({status.runtimeVersion})
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">{t("autogen.t_database")}</span>
                <span className="status-value">
                  {status.databaseVersion || "SQLite"}
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">
                  {t("autogen.t_database_migration")}
                </span>
                <span className="status-value">
                  {status.databaseMigration
                    ? `Schema #${status.databaseMigration}`
                    : "Current"}
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">
                  {t("autogen.t_appdata_directory")}
                </span>
                <span className="status-value">
                  <code>
                    {status.appDataPath || status.appDataFolder || "-"}
                  </code>
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">
                  {t("autogen.t_startup_directory")}
                </span>
                <span className="status-value">
                  <code>{status.startupPath || "-"}</code>
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">
                  {t("autogen.t_execution_mode")}
                </span>
                <span className="status-value">
                  <span className="badge badge-primary">
                    {status.isDocker ? "🐳 Docker" : "💻 Console"}
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
            {t("autogen.t_resources_links")}
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_official_website")}
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
                  {t("autogen.t_www_leecharr_net")}
                </a>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">{t("autogen.t_source_code")}</span>
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
                  {t("autogen.t_github_repository")}
                </a>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_issue_tracker")}
              </span>
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
                  {t("autogen.t_report_an_issue")}
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
