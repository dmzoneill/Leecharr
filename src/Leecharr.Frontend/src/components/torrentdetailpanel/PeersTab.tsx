import { usePeers } from "../../api/hooks";
import { formatBytes, formatSpeed } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";
import CountryFlag from "../CountryFlag";
import PeerClientBadge from "../PeerClientBadge";
import type { Torrent } from "../../api/types";

export function PeersTab({
  torrent,
  torrentId,
}: {
  torrent?: Torrent;
  torrentId?: number;
}) {
  const effectiveId = torrentId ?? torrent?.id ?? 0;
  const { data: peers, isLoading, isError } = usePeers(effectiveId);

  if (isLoading) return <PanelLoading>Loading peers...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load peers.</PanelEmpty>;
  if (!peers || peers.length === 0)
    return <PanelEmpty>No peers connected</PanelEmpty>;

  return (
    <div className="detail-panel-table-wrap">
      {torrent?.isPrivate && (
        <div
          style={{
            padding: "0.4rem 0.6rem",
            marginBottom: "0.5rem",
            borderRadius: "4px",
            fontSize: "0.72rem",
            backgroundColor: "rgba(239, 68, 68, 0.12)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            color: "#fca5a5",
            display: "flex",
            alignItems: "center",
            gap: "6px",
          }}
        >
          <i className="fas fa-lock" />
          <span>
            <strong>BEP 27 Private Swarm:</strong> Peer discovery is restricted
            exclusively to tracker announces. DHT and PEX peer exchanges are
            disabled to protect passkey privacy.
          </span>
        </div>
      )}
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">Address</th>
            <th className="torrent-table-th">Client</th>
            <th className="torrent-table-th">Progress</th>
            <th className="torrent-table-th">Up Speed</th>
            <th className="torrent-table-th">Down Speed</th>
            <th className="torrent-table-th">Uploaded</th>
            <th className="torrent-table-th">Downloaded</th>
            <th className="torrent-table-th">Flags</th>
          </tr>
        </thead>
        <tbody>
          {peers.map((p) => (
            <tr key={`${p.ip}:${p.port || p.id}`} className="torrent-table-row">
              <td className="mono" style={{ whiteSpace: "nowrap" }}>
                <div
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <CountryFlag
                    ip={p.ip}
                    countryCode={p.countryCode}
                    countryName={p.countryName}
                  />
                  <span>
                    {p.ip}:{p.port}
                  </span>
                </div>
              </td>
              <td>
                <PeerClientBadge client={p.client} flags={p.flags} />
              </td>
              <td>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <div
                    style={{
                      width: "45px",
                      height: "6px",
                      backgroundColor: "rgba(255,255,255,0.1)",
                      borderRadius: "3px",
                      overflow: "hidden",
                    }}
                  >
                    <div
                      style={{
                        width: `${Math.min(100, Math.max(0, p.progress * 100))}%`,
                        height: "100%",
                        backgroundColor: "var(--success, #27ae60)",
                      }}
                    />
                  </div>
                  <span style={{ fontSize: "0.78rem" }}>
                    {(p.progress * 100).toFixed(1)}%
                  </span>
                </div>
              </td>
              <td>{formatSpeed(p.uploadSpeed)}</td>
              <td>{formatSpeed(p.downloadSpeed)}</td>
              <td>{formatBytes(p.uploaded)}</td>
              <td>{formatBytes(p.downloaded)}</td>
              <td className="mono" style={{ fontSize: "0.75rem" }}>
                {p.flags}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
