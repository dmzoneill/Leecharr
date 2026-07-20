import { useNetworkDiagnostics } from "../api/hooks";

function EncryptionDonut({
  encrypted,
  plaintext,
}: {
  encrypted: number;
  plaintext: number;
}) {
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
            Encrypted: <strong>{encrypted}</strong>
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
            Plaintext: <strong>{plaintext}</strong>
          </span>
        </div>
      </div>
    </div>
  );
}

function SystemNetwork() {
  const { data: diag, isLoading, isError } = useNetworkDiagnostics();

  if (isLoading) {
    return (
      <div className="content-area">
        <div className="page-header">
          <h1 className="page-heading">System: Network Diagnostics</h1>
        </div>
        <p className="loading">Loading network diagnostics...</p>
      </div>
    );
  }

  if (isError || !diag) {
    return (
      <div className="content-area">
        <div className="page-header">
          <h1 className="page-heading">System: Network Diagnostics</h1>
        </div>
        <p className="error">Failed to load network diagnostic data.</p>
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
              System: Network Diagnostics
            </h1>
            <span className="badge badge-primary">Networking</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Peer-to-peer connection endpoints, listening ports, proxy routes,
            DHT node counts, and encryption metrics
          </div>
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <span
            className="badge badge-seeding"
            style={{ padding: "0.3rem 0.65rem", fontSize: "0.82rem" }}
          >
            Port {diag.listeningPort} (TCP/UDP)
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
            Connection Endpoints
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">Local IP Address</span>
              <span className="status-value">
                <code>{diag.localIp}</code>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">External Public IP</span>
              <span className="status-value">
                <code>{diag.externalIp || "Unknown"}</code>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">BitTorrent Port</span>
              <span className="status-value" style={{ fontWeight: 600 }}>
                {diag.listeningPort}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Active Peer Connections</span>
              <span className="status-value">
                <span className="badge badge-primary">
                  {diag.activeConnections}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Configured Upload Slots</span>
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
            Services & Protocols
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">UPnP Port Forwarding</span>
              <span className="status-value">
                <span
                  className={`badge ${diag.upnpAvailable ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.upnpAvailable ? "Available" : "Unavailable"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Proxy Tunneling</span>
              <span className="status-value">
                <span
                  className={`badge ${diag.proxyEnabled ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.proxyEnabled ? "Enabled" : "Disabled"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Mainline DHT</span>
              <span className="status-value">
                <span
                  className={`badge ${diag.dhtEnabled ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.dhtEnabled ? "Enabled" : "Disabled"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Known DHT Routing Nodes</span>
              <span className="status-value">
                <span className="badge badge-secondary">
                  {diag.dhtNodeCount} nodes
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Protocol Encryption Mode</span>
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
            Encryption Distribution (24h)
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
                No peer connection sessions recorded in the last 24 hours.
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
            Detected Local Interfaces
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
                    Interface
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <div style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}>
              No local network interfaces found.
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
              Active Port Mappings (UPnP / NAT-PMP)
            </h2>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.2rem",
              }}
            >
              Router port redirections negotiated by the BitTorrent networking
              daemon
            </div>
          </div>

          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Protocol</th>
                  <th className="torrent-table-th">Internal Port</th>
                  <th className="torrent-table-th">External Port</th>
                  <th className="torrent-table-th">Description</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Status
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
