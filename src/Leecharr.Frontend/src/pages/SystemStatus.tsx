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
              System: Status
            </h1>
            <span className="badge badge-primary">Environment</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Runtime environment, service health checks, disk allocations, and
            integrated ecosystem endpoints
          </div>
        </div>

        {status && (
          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <span
              className="badge badge-seeding"
              style={{ padding: "0.3rem 0.65rem", fontSize: "0.82rem" }}
            >
              ● Uptime: {formatUptime(status.uptimeSeconds)}
            </span>
          </div>
        )}
      </div>

      {isLoading && <p className="loading">Loading system status...</p>}

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
              System Health & Diagnostics
            </h2>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              Automated configuration verifiers and daemon liveness checks
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
              All background tasks and service configuration checks passed with
              no warnings.
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
                    Fix in Settings ⚙️
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
                Ecosystem & Integration Endpoints
              </h2>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted)",
                  marginTop: "0.2rem",
                }}
              >
                Connected media managers (*arr), indexers, and injection clients
              </div>
            </div>
            <Link
              to="/settings/connections"
              className="btn btn-outline btn-small"
              style={{ textDecoration: "none" }}
            >
              Manage in Settings →
            </Link>
          </div>

          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Service Name</th>
                  <th className="torrent-table-th">Type</th>
                  <th className="torrent-table-th">Endpoint / Host</th>
                  <th className="torrent-table-th">State</th>
                  <th className="torrent-table-th">Integration Features</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Actions
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
                          Open ↗
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
                        Download Agent Client
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
                          Open ↗
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
                          Open ↗
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
            Disk Space & Mount Volumes
          </h2>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Host storage drives and mount points available for torrent payload
            persistence
          </div>
        </div>

        {diskSpace && diskSpace.length > 0 ? (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Location</th>
                  <th className="torrent-table-th">Free Space</th>
                  <th className="torrent-table-th">Total Space</th>
                  <th className="torrent-table-th" style={{ width: "35%" }}>
                    Usage
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
            No disk volume information available.
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
              About Seedarr
            </h2>
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.6rem",
              }}
            >
              <div className="status-row">
                <span className="status-label">Version</span>
                <span className="status-value" style={{ fontWeight: 600 }}>
                  v{status.version}
                </span>
              </div>
              {status.instanceUuid && (
                <div className="status-row">
                  <span className="status-label">Instance UUID</span>
                  <span className="status-value">
                    <code style={{ fontSize: "0.82rem" }}>
                      {status.instanceUuid}
                    </code>
                  </span>
                </div>
              )}
              <div className="status-row">
                <span className="status-label">.NET Runtime</span>
                <span className="status-value">
                  {status.runtimeName} ({status.runtimeVersion})
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">Database</span>
                <span className="status-value">{status.databaseVersion}</span>
              </div>
              <div className="status-row">
                <span className="status-label">Database Migration</span>
                <span className="status-value">
                  Schema #{status.databaseMigration}
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">AppData Directory</span>
                <span className="status-value">
                  <code>{status.appDataPath}</code>
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">Startup Directory</span>
                <span className="status-value">
                  <code>{status.startupPath}</code>
                </span>
              </div>
              <div className="status-row">
                <span className="status-label">Execution Mode</span>
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
            Resources & Links
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">Official Website</span>
              <span className="status-value">
                <a
                  href="https://www.seedarr.net"
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    color: "var(--accent, #c8a84e)",
                    textDecoration: "none",
                  }}
                >
                  🌐 www.seedarr.net ↗
                </a>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Source Code</span>
              <span className="status-value">
                <a
                  href="https://github.com/dmzoneill/Seedarr"
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    color: "var(--accent, #c8a84e)",
                    textDecoration: "none",
                  }}
                >
                  📦 GitHub Repository ↗
                </a>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Issue Tracker</span>
              <span className="status-value">
                <a
                  href="https://github.com/dmzoneill/Seedarr/issues"
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    color: "var(--accent, #c8a84e)",
                    textDecoration: "none",
                  }}
                >
                  🐛 Report an Issue ↗
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
