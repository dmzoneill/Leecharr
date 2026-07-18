import React from 'react';
import { Torrent } from '../api/types';

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
      <div className="empty-state">
        <div className="empty-icon">📁</div>
        <h3>No Torrents Found</h3>
        <p className="text-muted">Add a magnet link or .torrent file to begin downloading.</p>
      </div>
    );
  }

  return (
    <div className="table-responsive">
      <table className="table-torrents">
        <thead>
          <tr>
            <th>Name</th>
            <th>Category</th>
            <th>Size</th>
            <th>Progress</th>
            <th>Status</th>
            <th>Down Speed</th>
            <th>Up Speed</th>
            <th>Seeds</th>
            <th>Peers</th>
            <th>Ratio</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {torrents.map((t) => {
            const isSelected = t.id === selectedId;
            const isPaused = t.status === 'paused';

            return (
              <tr
                key={t.id}
                className={isSelected ? 'selected-row' : ''}
                onClick={() => onSelect(t)}
              >
                <td className="cell-title" title={t.name}>
                  <div className="title-container">
                    <span className="torrent-name">{t.mediaTitle || t.name}</span>
                    {t.resolution && <span className="cell-badge">{t.resolution}</span>}
                  </div>
                </td>
                <td>
                  <span className="category-tag">{t.category || 'none'}</span>
                </td>
                <td>{formatSize(t.totalSize)}</td>
                <td>
                  <div className="table-progress">
                    <div className="progress-bar-mini">
                      <div
                        className={`progress-fill-mini ${t.status === 'seeding' ? 'seeding' : ''}`}
                        style={{ width: `${Math.min(100, Math.max(0, t.progress * 100))}%` }}
                      />
                    </div>
                    <span className="progress-percent">{(t.progress * 100).toFixed(1)}%</span>
                  </div>
                </td>
                <td>
                  <span className={`status-pill status-${t.status}`}>
                    {t.status}
                  </span>
                </td>
                <td className="speed-down">{formatSpeed(t.downloadSpeed)}</td>
                <td className="speed-up">{formatSpeed(t.uploadSpeed)}</td>
                <td>{t.seeders || 0}</td>
                <td>{t.leechers || 0}</td>
                <td>{t.ratio.toFixed(2)}</td>
                <td onClick={(e) => e.stopPropagation()}>
                  <div className="table-actions-group">
                    {isPaused ? (
                      <button className="btn-icon" onClick={() => onResume(t.id)} title="Resume">▶</button>
                    ) : (
                      <button className="btn-icon" onClick={() => onPause(t.id)} title="Pause">⏸</button>
                    )}
                    <button className="btn-icon btn-delete" onClick={() => onDelete(t.id)} title="Delete">🗑</button>
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
