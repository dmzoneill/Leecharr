import React from 'react';
import { Torrent } from '../api/types';
import { PlayIcon, StopIcon } from './icons/UIIcons';

interface TorrentTableProps {
  torrents: Torrent[];
  selectedId: number | null;
  onSelect: (torrent: Torrent) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (id: number) => void;
}

export const TorrentTable: React.FC<TorrentTableProps> = ({
  torrents,
  selectedId,
  onSelect,
  onPause,
  onResume,
  onDelete
}) => {
  const formatSize = (bytes: number) => {
    if (!bytes) return '0 B';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(2)} GB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec || bytesPerSec === 0) return '—';
    const mb = bytesPerSec / (1024 * 1024);
    if (mb >= 1) return `${mb.toFixed(1)} MB/s`;
    return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  };

  if (torrents.length === 0) {
    return (
      <div className="card" style={{ padding: '3rem', textAlign: 'center', margin: '2rem auto', maxWidth: '600px' }}>
        <div style={{ fontSize: '3rem', marginBottom: '1rem' }}>📁</div>
        <h3 style={{ color: 'var(--text-primary)', marginBottom: '0.5rem' }}>No Torrents in Queue</h3>
        <p style={{ color: 'var(--text-muted)' }}>Add a magnet link or search indexers to begin downloading.</p>
      </div>
    );
  }

  return (
    <div className="torrent-table-wrapper">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">Name</th>
            <th className="torrent-table-th">Category</th>
            <th className="torrent-table-th">Size</th>
            <th className="torrent-table-th">Progress</th>
            <th className="torrent-table-th">Status</th>
            <th className="torrent-table-th">Down Speed</th>
            <th className="torrent-table-th">Up Speed</th>
            <th className="torrent-table-th">Seeds</th>
            <th className="torrent-table-th">Peers</th>
            <th className="torrent-table-th">Ratio</th>
            <th className="torrent-table-th" style={{ textAlign: 'right' }}>Actions</th>
          </tr>
        </thead>
        <tbody>
          {torrents.map((t) => {
            const isSelected = t.id === selectedId;
            const isPaused = t.status === 'paused';

            return (
              <tr
                key={t.id}
                className={`torrent-table-row ${isSelected ? 'torrent-table-row-selected' : ''}`}
                onClick={() => onSelect(t)}
                style={{ cursor: 'pointer' }}
              >
                <td>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', maxWidth: '380px' }}>
                    <span style={{ fontWeight: 600, color: 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={t.name}>
                      {t.mediaTitle || t.name}
                    </span>
                    {t.resolution && (
                      <span className="badge" style={{ backgroundColor: '#8b5cf6', color: '#fff', fontSize: '0.65rem' }}>
                        {t.resolution}
                      </span>
                    )}
                  </div>
                </td>
                <td>
                  <span className="badge badge-accent">
                    {t.category || 'none'}
                  </span>
                </td>
                <td style={{ color: 'var(--text-secondary)' }}>{formatSize(t.totalSize)}</td>
                <td>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', width: '120px' }}>
                    <div style={{ flex: 1, height: '5px', backgroundColor: 'rgba(255,255,255,0.08)', borderRadius: '3px', overflow: 'hidden' }}>
                      <div
                        style={{
                          height: '100%',
                          width: `${Math.min(100, Math.max(0, t.progress * 100))}%`,
                          backgroundColor: t.status === 'seeding' ? 'var(--success)' : 'var(--accent)',
                        }}
                      />
                    </div>
                    <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)', width: '38px', textAlign: 'right' }}>
                      {(t.progress * 100).toFixed(0)}%
                    </span>
                  </div>
                </td>
                <td>
                  <span
                    className="badge"
                    style={{
                      backgroundColor: t.status === 'seeding' ? 'var(--success-bg)' : t.status === 'downloading' ? 'var(--accent-bg)' : 'var(--muted-bg)',
                      color: t.status === 'seeding' ? 'var(--success)' : t.status === 'downloading' ? 'var(--accent)' : 'var(--text-muted)',
                      textTransform: 'capitalize'
                    }}
                  >
                    {t.status}
                  </span>
                </td>
                <td style={{ color: 'var(--accent)', fontWeight: 600 }}>{formatSpeed(t.downloadSpeed)}</td>
                <td style={{ color: 'var(--success)', fontWeight: 600 }}>{formatSpeed(t.uploadSpeed)}</td>
                <td style={{ color: 'var(--text-secondary)' }}>{t.seeders || 0}</td>
                <td style={{ color: 'var(--text-secondary)' }}>{t.leechers || 0}</td>
                <td style={{ color: 'var(--text-muted)' }}>{t.ratio.toFixed(2)}</td>
                <td style={{ textAlign: 'right' }} onClick={(e) => e.stopPropagation()}>
                  <div style={{ display: 'inline-flex', gap: '4px' }}>
                    {isPaused ? (
                      <button className="btn btn-small btn-success" onClick={() => onResume(t.id)} title="Resume">
                        <PlayIcon size={10} />
                      </button>
                    ) : (
                      <button className="btn btn-small" onClick={() => onPause(t.id)} title="Pause">
                        <StopIcon size={10} />
                      </button>
                    )}
                    <button className="btn btn-small btn-danger" onClick={() => onDelete(t.id)} title="Delete">
                      ✕
                    </button>
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};
export default TorrentTable;
