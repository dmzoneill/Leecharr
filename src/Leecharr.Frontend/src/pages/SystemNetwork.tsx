import { useTranslation } from "../i18n";
import { useNetworkDiagnostics } from "../api/hooks";

function EncryptionDonut({
  encrypted,
  plaintext,
}: {
  encrypted: number;
  plaintext: number;
}) {
  const { t } = useTranslation();

  const total = encrypted + plaintext;
  if (total === 0) return null;

  const encPct = encrypted / total;
  const radius = 35;
  const circumference = 2 * Math.PI * radius;

  return (
    <div style={{ display: "flex", alignItems: "center", gap: 20 }}>
      <svg width={84} height={84} viewBox="0 0 80 80">
        <circle
          cx={40}
          cy={40}
          r={radius}
          fill="none"
          stroke="var(--success, #28a745)"
          strokeWidth={10}
          strokeDasharray={`${encPct * circumference} ${circumference}`}
          strokeDashoffset={0}
          transform="rotate(-90 40 40)"
        />
        <circle
          cx={40}
          cy={40}
          r={radius}
          fill="none"
          stroke="var(--danger, #dc3545)"
          strokeWidth={10}
          strokeDasharray={`${(1 - encPct) * circumference} ${circumference}`}
          strokeDashoffset={-encPct * circumference}
          transform="rotate(-90 40 40)"
        />
        <text
          x={40}
          y={45}
          textAnchor="middle"
          fontSize={13}
          fontWeight={700}
          fill="var(--text-primary, #fff)"
        >
          {Math.round(encPct * 100)}%
        </text>
      </svg>
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          gap: 6,
          fontSize: "0.85rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <div
            style={{
              width: 10,
              height: 10,
              borderRadius: "50%",
              backgroundColor: "var(--success, #28a745)",
            }}
          />
          <span>
            {t("autogen.t_encrypted")}
            <strong>{encrypted}</strong>
          </span>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <div
            style={{
              width: 10,
              height: 10,
              borderRadius: "50%",
              backgroundColor: "var(--danger, #dc3545)",
            }}
          />
          <span>
            {t("autogen.t_plaintext")}
            <strong>{plaintext}</strong>
          </span>
        </div>
      </div>
    </div>
  );
}

function SystemNetwork() {
  const { t } = useTranslation();
  const { data: diag, isLoading, isError } = useNetworkDiagnostics();

  if (isLoading) {
    return (
      <div className="content-area">
        <div className="page-header">
          <h1 className="page-heading">
            {t("autogen.t_system_network_diagnostics")}
          </h1>
        </div>
        <p className="loading">{t("autogen.t_loading_network_diagnostics")}</p>
      </div>
    );
  }

  if (isError || !diag) {
    return (
      <div className="content-area">
        <div className="page-header">
          <h1 className="page-heading">
            {t("autogen.t_system_network_diagnostics")}
          </h1>
        </div>
        <p className="error">
          {t("autogen.t_failed_to_load_network_diagnostic_data")}
        </p>
      </div>
    );
  }

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
              {t("autogen.t_system_network_diagnostics")}
            </h1>
            <span className="badge badge-primary">
              {t("autogen.t_networking")}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("autogen.t_peer_to_peer_connection_endpoints_listen")}
          </div>
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <span
            className="badge badge-seeding"
            style={{ padding: "0.3rem 0.65rem", fontSize: "0.82rem" }}
          >
            {t("autogen.t_port")}
            {diag.listeningPort} {t("autogen.t_tcp_udp")}
          </span>
        </div>
      </div>

      {/* Primary 2-Column Metric Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(340px, 1fr))",
          gap: "1.25rem",
          marginBottom: "1.25rem",
        }}
      >
        {/* Connection Endpoints Card */}
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
            {t("autogen.t_connection_endpoints")}
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_local_ip_address")}
              </span>
              <span className="status-value">
                <code>{diag.localIp}</code>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_external_public_ip")}
              </span>
              <span className="status-value">
                <code>{diag.externalIp || "Unknown"}</code>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_bittorrent_port")}
              </span>
              <span className="status-value" style={{ fontWeight: 600 }}>
                {diag.listeningPort}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_active_peer_connections")}
              </span>
              <span className="status-value">
                <span className="badge badge-primary">
                  {diag.activeConnections}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_configured_upload_slots")}
              </span>
              <span className="status-value">{diag.uploadSlots}</span>
            </div>
          </div>
        </div>

        {/* Network Services & Protocols Card */}
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
            {t("autogen.t_services_protocols")}
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_upnp_port_forwarding")}
              </span>
              <span className="status-value">
                <span
                  className={`badge ${diag.upnpAvailable ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.upnpAvailable ? "Available" : "Unavailable"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_proxy_tunneling")}
              </span>
              <span className="status-value">
                <span
                  className={`badge ${diag.proxyEnabled ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.proxyEnabled ? "Enabled" : "Disabled"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_mainline_dht")}
              </span>
              <span className="status-value">
                <span
                  className={`badge ${diag.dhtEnabled ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.dhtEnabled ? "Enabled" : "Disabled"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_known_dht_routing_nodes")}
              </span>
              <span className="status-value">
                <span className="badge badge-secondary">
                  {diag.dhtNodeCount} {t("autogen.t_nodes")}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">
                {t("autogen.t_protocol_encryption_mode")}
              </span>
              <span className="status-value">
                <span className="badge badge-primary">
                  {diag.encryptionMode}
                </span>
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Secondary 2-Column Grid */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(340px, 1fr))",
          gap: "1.25rem",
          marginBottom: "1.25rem",
        }}
      >
        {/* Encryption Donut Card */}
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
            {t("autogen.t_encryption_distribution_24h")}
          </h2>
          <div style={{ marginTop: "0.5rem" }}>
            <EncryptionDonut
              encrypted={diag.encryptedConnections}
              plaintext={diag.plaintextConnections}
            />
            {diag.encryptedConnections + diag.plaintextConnections === 0 && (
              <p
                style={{
                  color: "var(--text-muted)",
                  fontSize: "0.85rem",
                  margin: "0.5rem 0 0",
                }}
              >
                {t("autogen.t_no_peer_connection_sessions_recorded_in_")}
              </p>
            )}
          </div>
        </div>

        {/* Local Addresses Card */}
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
            {t("autogen.t_detected_local_interfaces")}
          </h2>
          {diag.localAddresses.length > 0 ? (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.5rem",
                marginTop: "0.5rem",
              }}
            >
              {diag.localAddresses.map((addr) => (
                <div
                  key={addr}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "0.4rem 0.75rem",
                    backgroundColor: "rgba(255, 255, 255, 0.03)",
                    borderRadius: "4px",
                    border: "1px solid rgba(255, 255, 255, 0.05)",
                  }}
                >
                  <code style={{ fontSize: "0.85rem" }}>{addr}</code>
                  <span
                    className="badge badge-secondary"
                    style={{ fontSize: "0.72rem" }}
                  >
                    {t("autogen.t_interface")}
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <div style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}>
              {t("autogen.t_no_local_network_interfaces_found")}
            </div>
          )}
        </div>
      </div>

      {/* Port Mappings Card (if any) */}
      {diag.portMappings.length > 0 && (
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
              {t("autogen.t_active_port_mappings_upnp_nat_pmp")}
            </h2>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              {t("autogen.t_router_port_redirections_negotiated_by_t")}
            </div>
          </div>

          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">
                    {t("autogen.t_protocol")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_internal_port")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_external_port")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_description")}
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    {t("autogen.t_status")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {diag.portMappings.map((pm, i) => (
                  <tr key={i} className="torrent-table-row">
                    <td>
                      <span className="badge badge-primary">{pm.protocol}</span>
                    </td>
                    <td>{pm.internalPort}</td>
                    <td>{pm.externalPort}</td>
                    <td>{pm.description}</td>
                    <td style={{ textAlign: "right" }}>
                      <span
                        className={`badge ${pm.isActive ? "badge-seeding" : "badge-stopped"}`}
                      >
                        {pm.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemNetwork;
