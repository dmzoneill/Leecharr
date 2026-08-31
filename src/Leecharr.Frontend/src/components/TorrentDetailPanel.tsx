import React, { useState, useEffect, useRef } from 'react';
import { Torrent, TorrentFile, Peer } from '../api/types';
import { api } from '../api/client';

interface TorrentDetailPanelProps {
  torrent: Torrent;
  onClose: () => void;
}

export const TorrentDetailPanel: React.FC<TorrentDetailPanelProps> = ({ torrent, onClose }) => {
  const [activeTab, setActiveTab] = useState<'details' | 'files' | 'peers' | 'piecemap'>('details');
  const [files, setFiles] = useState<TorrentFile[]>([]);
  const [peers, setPeers] = useState<Peer[]>([]);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    // Load files
    api.getTorrentFiles(torrent.id)
      .then(setFiles)
      .catch(console.error);

    // Mock peers based on seeders / leechers
    const mockPeers: Peer[] = [];
    const seeders = torrent.seeders || 0;
    const leechers = torrent.leechers || 0;
    const clients = ['qBittorrent 4.6.5', 'Transmission 4.0.5', 'Deluge 2.1.1', 'Leecharr 1.0.0'];
    const countries = [
      { code: 'US', name: 'United States' },
      { code: 'DE', name: 'Germany' },
      { code: 'NL', name: 'Netherlands' },
      { code: 'CA', name: 'Canada' },
      { code: 'SE', name: 'Sweden' },
      { code: 'GB', name: 'United Kingdom' },
    ];

    for (let i = 0; i < Math.min(12, seeders + leechers); i++) {
      const isSeeder = i < seeders;
      const c = countries[i % countries.length];
      mockPeers.push({
        ip: `198.51.${100 + i}.${10 + (i * 7) % 200}`,
        port: 51413 + (i * 13) % 1000,
        client: clients[i % clients.length],
        progress: isSeeder ? 1.0 : 0.2 + (i * 0.1) % 0.8,
        downloadSpeed: isSeeder ? Math.floor(1024 * 1024 * (1 + (i % 5))) : 0,
        uploadSpeed: Math.floor(256 * 1024 * (1 + (i % 3))),
        countryCode: c.code,
        countryName: c.name,
        protocol: i % 3 === 0 ? 'uTP' : 'TCP',
        isEncrypted: i % 2 === 0,
        flags: isSeeder ? 'D E S' : 'U I H',
      });
    }
    setPeers(mockPeers);
  }, [torrent.id, torrent.seeders, torrent.leechers]);

  // Render piece map canvas
  useEffect(() => {
    if (activeTab !== 'piecemap') return;
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const totalPieces = torrent.pieceCount || 100;
    const progressPieces = Math.floor((torrent.progress || 0) * totalPieces);
    const cols = Math.ceil(Math.sqrt(totalPieces * 2.5));
    const rows = Math.ceil(totalPieces / cols);

    const pieceWidth = Math.max(3, Math.floor(canvas.width / cols));
    const pieceHeight = Math.max(3, Math.floor(canvas.height / rows));

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    for (let i = 0; i < totalPieces; i++) {
      const col = i % cols;
      const row = Math.floor(i / cols);
      const x = col * pieceWidth;
      const y = row * pieceHeight;

      if (i < progressPieces) {
        ctx.fillStyle = '#ffd166';
      } else if (i === progressPieces && torrent.status === 'downloading') {
        ctx.fillStyle = '#38bdf8';
      } else {
        ctx.fillStyle = '#23284b';
      }

      ctx.fillRect(x + 0.5, y + 0.5, pieceWidth - 1, pieceHeight - 1);
    }
  }, [activeTab, torrent]);

  const formatSize = (bytes: number) => {
    if (!bytes) return '0 B';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(2)} GB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec) return '0 B/s';
    const kb = bytesPerSec / 1024;
    if (kb < 1024) return `${kb.toFixed(1)} KB/s`;
    return `${(kb / 1024).toFixed(1)} MB/s`;
  };

  return (
    <div className="torrent-detail-panel">
      <div className="detail-panel-header">
        <div className="detail-panel-title">
          <strong>{torrent.mediaTitle || torrent.name}</strong>
          <span className={`status-pill status-${torrent.status}`}>{torrent.status}</span>
        </div>

        <div className="detail-panel-tabs">
          <button
            className={`tab-btn ${activeTab === 'details' ? 'active' : ''}`}
            onClick={() => setActiveTab('details')}
          >
            Details
          </button>
          <button
            className={`tab-btn ${activeTab === 'files' ? 'active' : ''}`}
            onClick={() => setActiveTab('files')}
          >
            Files ({files.length})
          </button>
          <button
            className={`tab-btn ${activeTab === 'peers' ? 'active' : ''}`}
            onClick={() => setActiveTab('peers')}
          >
            Peers ({peers.length})
          </button>
          <button
            className={`tab-btn ${activeTab === 'piecemap' ? 'active' : ''}`}
            onClick={() => setActiveTab('piecemap')}
          >
            Piece Map
          </button>
        </div>

        <button className="detail-panel-close" onClick={onClose}>&times;</button>
      </div>

      <div className="detail-panel-body">
        {activeTab === 'details' && (
          <div className="details-grid">
            <div className="details-card">
              <h4>General Info</h4>
              <div className="detail-row">
                <span className="label">Save Path:</span>
                <span className="value">{torrent.savePath}</span>
              </div>
              <div className="detail-row">
                <span className="label">Total Size:</span>
                <span className="value">{formatSize(torrent.totalSize)}</span>
              </div>
              <div className="detail-row">
                <span className="label">InfoHash:</span>
                <span className="value font-mono">{torrent.infoHash}</span>
              </div>
              <div className="detail-row">
                <span className="label">Ratio:</span>
                <span className="value">{torrent.ratio.toFixed(2)}</span>
              </div>
            </div>

            <div className="details-card">
              <h4>Media Stream Specs</h4>
              <div className="detail-row">
                <span className="label">Resolution:</span>
                <span className="value">{torrent.resolution || 'Auto-detecting...'}</span>
              </div>
              <div className="detail-row">
                <span className="label">Video Codec:</span>
                <span className="value">{torrent.videoCodec || 'Auto-detecting...'}</span>
              </div>
              <div className="detail-row">
                <span className="label">Audio Codec:</span>
                <span className="value">{torrent.audioCodec || 'Auto-detecting...'}</span>
              </div>
              <div className="detail-row">
                <span className="label">HDR Format:</span>
                <span className="value">{torrent.hdrFormat || 'SDR'}</span>
              </div>
            </div>
          </div>
        )}

        {activeTab === 'files' && (
          <div className="table-responsive">
            <table className="table-torrents">
              <thead>
                <tr>
                  <th>File Name</th>
                  <th>Size</th>
                  <th>Progress</th>
                  <th>Priority</th>
                </tr>
              </thead>
              <tbody>
                {files.map((f) => (
                  <tr key={f.id}>
                    <td>{f.path}</td>
                    <td>{formatSize(f.size)}</td>
                    <td>{Math.round((f.progress || 0) * 100)}%</td>
                    <td>Normal</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {activeTab === 'peers' && (
          <div className="table-responsive">
            <table className="table-torrents">
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
                {peers.map((p, idx) => (
                  <tr key={idx}>
                    <td><span className="country-badge">{p.countryCode}</span></td>
                    <td>{p.ip}:{p.port}</td>
                    <td>{p.client}</td>
                    <td><span className={`protocol-badge ${p.protocol.toLowerCase()}`}>{p.protocol}</span></td>
                    <td><span className={`lock-badge ${p.isEncrypted ? 'encrypted' : 'plain'}`}>{p.isEncrypted ? 'RC4' : 'Plain'}</span></td>
                    <td><code>{p.flags}</code></td>
                    <td>{Math.round(p.progress * 100)}%</td>
                    <td className="speed-down">{formatSpeed(p.downloadSpeed)}</td>
                    <td className="speed-up">{formatSpeed(p.uploadSpeed)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {activeTab === 'piecemap' && (
          <div className="piecemap-container">
            <canvas ref={canvasRef} width={800} height={180} className="piece-canvas" />
            <div className="piece-legend">
              <span className="legend-item"><span className="legend-box piece-done" /> Completed</span>
              <span className="legend-item"><span className="legend-box piece-active" /> In-Flight</span>
              <span className="legend-item"><span className="legend-box piece-missing" /> Missing</span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
export default TorrentDetailPanel;
