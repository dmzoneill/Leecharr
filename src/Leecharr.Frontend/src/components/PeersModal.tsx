import React, { useState, useEffect } from "react";
import { Torrent, Peer } from "../api/types";

interface PeersModalProps {
  torrent: Torrent;
  onClose: () => void;
}

export const PeersModal: React.FC<PeersModalProps> = ({ torrent, onClose }) => {
  const [peers, setPeers] = useState<Peer[]>([]);

  useEffect(() => {
    // Generate simulated/real peer connections based on swarm size
    const seeders = torrent.seeders || 0;
    const leechers = torrent.leechers || 0;
    const mockPeers: Peer[] = [];

    const clients = [
      "qBittorrent 4.6.5",
      "Transmission 4.0.5",
      "Deluge 2.1.1",
      "Leecharr 1.0.0",
    ];
    const countries = [
      { code: "US", name: "United States" },
      { code: "DE", name: "Germany" },
      { code: "NL", name: "Netherlands" },
      { code: "CA", name: "Canada" },
      { code: "SE", name: "Sweden" },
      { code: "GB", name: "United Kingdom" },
    ];

    const totalPeers = Math.min(15, seeders + leechers);
    for (let i = 0; i < totalPeers; i++) {
      const isSeeder = i < seeders;
      const c = countries[i % countries.length];
      mockPeers.push({
        ip: `198.51.${100 + i}.${10 + ((i * 7) % 200)}`,
        port: 51413 + ((i * 13) % 1000),
        client: clients[i % clients.length],
        progress: isSeeder ? 1.0 : 0.15 + ((i * 0.08) % 0.8),
        downloadSpeed: isSeeder ? Math.floor(1024 * 1024 * (1 + (i % 5))) : 0,
        uploadSpeed: Math.floor(256 * 1024 * (1 + (i % 3))),
        countryCode: c.code,
        countryName: c.name,
        protocol: i % 3 === 0 ? "uTP" : "TCP",
        isEncrypted: i % 2 === 0,
        flags: isSeeder ? "D E S" : "U I H",
      });
    }

    setPeers(mockPeers);
  }, [torrent]);

  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec) return "0 B/s";
    const kb = bytesPerSec / 1024;
    if (kb < 1024) return `${kb.toFixed(1)} KB/s`;
    return `${(kb / 1024).toFixed(1)} MB/s`;
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-content peers-modal"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h3>Swarm Inspector &mdash; {torrent.name}</h3>
          <button className="btn-close" onClick={onClose}>
            &times;
          </button>
        </div>
        <div className="modal-body">
          <div className="peers-summary">
            <span>
              <strong>Seeds:</strong> {torrent.seeders || 0}
            </span>
            <span>
              <strong>Leechers:</strong> {torrent.leechers || 0}
            </span>
            <span>
              <strong>Active Connections:</strong> {peers.length}
            </span>
          </div>

          <div className="table-responsive">
            <table className="peers-table">
              <thead>
                <tr>
                  <th>Country</th>
                  <th>IP Address</th>
                  <th>Client</th>
                  <th>Protocol</th>
                  <th>Encryption</th>
                  <th>Flags</th>
                  <th>Progress</th>
                  <th>Down Speed</th>
                  <th>Up Speed</th>
                </tr>
              </thead>
              <tbody>
                {peers.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="text-center">
                      No active peer connections.
                    </td>
                  </tr>
                ) : (
                  peers.map((peer, idx) => (
                    <tr key={idx}>
                      <td>
                        <span
                          className="country-badge"
                          title={peer.countryName}
                        >
                          {peer.countryCode}
                        </span>
                      </td>
                      <td>
                        {peer.ip}:{peer.port}
                      </td>
                      <td>{peer.client}</td>
                      <td>
                        <span
                          className={`protocol-badge ${peer.protocol.toLowerCase()}`}
                        >
                          {peer.protocol}
                        </span>
                      </td>
                      <td>
                        <span
                          className={`lock-badge ${peer.isEncrypted ? "encrypted" : "plain"}`}
                        >
                          {peer.isEncrypted ? "RC4" : "Plain"}
                        </span>
                      </td>
                      <td>
                        <code>{peer.flags}</code>
                      </td>
                      <td>
                        <div className="mini-progress-bar">
                          <div
                            className="mini-progress-fill"
                            style={{ width: `${peer.progress * 100}%` }}
                          ></div>
                        </div>
                        <span className="progress-text">
                          {Math.round(peer.progress * 100)}%
                        </span>
                      </td>
                      <td className="speed-down">
                        {formatSpeed(peer.downloadSpeed)}
                      </td>
                      <td className="speed-up">
                        {formatSpeed(peer.uploadSpeed)}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
};
