import React from 'react';
import { Torrent } from '../api/types';

interface TorrentGridProps {
  torrents: Torrent[];
  selectedId: number | null;
  onSelect: (torrent: Torrent) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (id: number) => void;
}

export const TorrentGrid: React.FC<TorrentGridProps> = ({
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
    if (!bytesPerSec) return '0 KB/s';
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
    <div className="torrent-grid">
      {torrents.map((t) => {
        const isSelected = t.id === selectedId;
        const isDownloading = t.status === 'downloading';
        const isSeeding = t.status === 'seeding';
        const isPaused = t.status === 'paused';

        return (
          <div
            key={t.id}
            className={`torrent-grid-card ${isSelected ? 'selected' : ''}`}
            onClick={() => onSelect(t)}
          >
            <div className="grid-card-poster">
              {t.posterUrl ? (
                <img src={t.posterUrl} alt={t.name} />
              ) : (
                <div className="poster-placeholder">
                  <span className="poster-placeholder-icon">🎬</span>
                  <span className="poster-placeholder-name">{t.mediaTitle || t.name}</span>
                </div>
              )}

              {/* Media Spec Badges */}
              <div className="grid-card-badges">
                {t.resolution && <span className="media-badge badge-resolution">{t.resolution}</span>}
                {t.hdrFormat && <span className="media-badge badge-hdr">{t.hdrFormat}</span>}
                {t.audioCodec && <span className="media-badge badge-audio">{t.audioCodec}</span>}
              </div>

              {/* Status Badge */}
              <div className={`grid-card-status status-${t.status}`}>
                {t.status}
              </div>
            </div>

            <div className="grid-card-content">
              <div className="grid-card-title" title={t.name}>
                {t.mediaTitle || t.name}
              </div>

              <div className="grid-card-meta">
                <span>{formatSize(t.totalSize)}</span>
                <span>{(t.progress * 100).toFixed(1)}%</span>
              </div>

              <div className="grid-card-progress-bar">
                <div
                  className={`progress-fill ${isSeeding ? 'seeding' : ''}`}
                  style={{ width: `${Math.min(100, Math.max(0, t.progress * 100))}%` }}
                />
              </div>

              <div className="grid-card-speeds">
                {isDownloading && <span className="speed-down">↓ {formatSpeed(t.downloadSpeed)}</span>}
                {isSeeding && <span className="speed-up">↑ {formatSpeed(t.uploadSpeed)}</span>}
                <span className="ratio-text">Ratio: {t.ratio.toFixed(2)}</span>
              </div>

              <div className="grid-card-actions" onClick={(e) => e.stopPropagation()}>
                {isPaused ? (
                  <button className="btn-action" onClick={() => onResume(t.id)} title="Resume">▶</button>
                ) : (
                  <button className="btn-action" onClick={() => onPause(t.id)} title="Pause">⏸</button>
                )}
                <button className="btn-action btn-delete" onClick={() => onDelete(t.id)} title="Delete">🗑</button>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
};
export default TorrentGrid;
