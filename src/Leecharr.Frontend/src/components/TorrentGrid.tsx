import React from 'react';
import { Torrent } from '../api/types';
import { PlayIcon, StopIcon } from './icons/UIIcons';

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
      <div className="card" style={{ padding: '3rem', textAlign: 'center', margin: '2rem auto', maxWidth: '600px' }}>
        <div style={{ fontSize: '3rem', marginBottom: '1rem' }}>📁</div>
        <h3 style={{ color: 'var(--text-primary)', marginBottom: '0.5rem' }}>No Torrents in Queue</h3>
        <p style={{ color: 'var(--text-muted)' }}>Add a magnet link or search indexers to begin downloading.</p>
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
            className={`card torrent-grid-card ${isSelected ? 'torrent-grid-card-selected' : ''}`}
            onClick={() => onSelect(t)}
            style={{
              display: 'flex',
              flexDirection: 'column',
              cursor: 'pointer',
              overflow: 'hidden',
              borderRadius: '6px',
              border: isSelected ? '1px solid var(--accent)' : '1px solid var(--border)',
              backgroundColor: 'var(--bg-secondary)',
              transition: 'all 0.15s ease-in-out',
            }}
          >
            {/* Poster Header */}
            <div
              style={{
                height: '240px',
                backgroundColor: 'rgba(0,0,0,0.4)',
                position: 'relative',
                overflow: 'hidden',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              {t.posterUrl ? (
                <img
                  src={t.posterUrl}
                  alt={t.name}
                  style={{ width: '100%', height: '100%', objectFit: 'cover' }}
                />
              ) : (
                <div style={{ textAlign: 'center', padding: '1rem', color: 'var(--text-muted)' }}>
                  <div style={{ fontSize: '2.5rem', marginBottom: '0.5rem' }}>🎬</div>
                  <div style={{ fontSize: '0.85rem', fontWeight: 600 }}>{t.mediaTitle || t.name}</div>
                </div>
              )}

              {/* Media Badges */}
              <div style={{ position: 'absolute', top: '8px', left: '8px', display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                {t.resolution && (
                  <span className="badge" style={{ backgroundColor: '#8b5cf6', color: '#fff', fontSize: '0.65rem' }}>
                    {t.resolution}
                  </span>
                )}
                {t.hdrFormat && (
                  <span className="badge" style={{ backgroundColor: '#f43f5e', color: '#fff', fontSize: '0.65rem' }}>
                    {t.hdrFormat}
                  </span>
                )}
                {t.audioCodec && (
                  <span className="badge" style={{ backgroundColor: '#3b82f6', color: '#fff', fontSize: '0.65rem' }}>
                    {t.audioCodec}
                  </span>
                )}
              </div>

              {/* Status Pill */}
              <div
                style={{
                  position: 'absolute',
                  top: '8px',
                  right: '8px',
                  padding: '2px 8px',
                  borderRadius: '4px',
                  fontSize: '0.7rem',
                  fontWeight: 700,
                  textTransform: 'capitalize',
                  backgroundColor: 'rgba(16, 17, 26, 0.85)',
                  border: '1px solid var(--border)',
                  color: isSeeding ? 'var(--success)' : isDownloading ? 'var(--accent)' : 'var(--text-muted)'
                }}
              >
                {t.status}
              </div>
            </div>

            {/* Card Body */}
            <div style={{ padding: '0.75rem', display: 'flex', flexDirection: 'column', gap: '0.5rem', flex: 1 }}>
              <div
                style={{
                  fontSize: '0.9rem',
                  fontWeight: 600,
                  color: 'var(--text-primary)',
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis'
                }}
                title={t.name}
              >
                {t.mediaTitle || t.name}
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>
                <span>{formatSize(t.totalSize)}</span>
                <span style={{ fontWeight: 600 }}>{(t.progress * 100).toFixed(1)}%</span>
              </div>

              {/* Progress Bar */}
              <div style={{ height: '4px', backgroundColor: 'rgba(255,255,255,0.08)', borderRadius: '2px', overflow: 'hidden' }}>
                <div
                  style={{
                    height: '100%',
                    width: `${Math.min(100, Math.max(0, t.progress * 100))}%`,
                    backgroundColor: isSeeding ? 'var(--success)' : 'var(--accent)',
                    transition: 'width 0.3s'
                  }}
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', marginTop: '2px' }}>
                {isDownloading && <span style={{ color: 'var(--accent)', fontWeight: 600 }}>↓ {formatSpeed(t.downloadSpeed)}</span>}
                {isSeeding && <span style={{ color: 'var(--success)', fontWeight: 600 }}>↑ {formatSpeed(t.uploadSpeed)}</span>}
                <span style={{ color: 'var(--text-dim)' }}>Ratio: {t.ratio.toFixed(2)}</span>
              </div>

              {/* Card Footer Actions */}
              <div style={{ display: 'flex', gap: '6px', marginTop: 'auto', paddingTop: '6px' }} onClick={(e) => e.stopPropagation()}>
                {isPaused ? (
                  <button className="btn btn-small btn-success" style={{ flex: 1 }} onClick={() => onResume(t.id)}>
                    <PlayIcon size={11} /> Resume
                  </button>
                ) : (
                  <button className="btn btn-small" style={{ flex: 1 }} onClick={() => onPause(t.id)}>
                    <StopIcon size={11} /> Pause
                  </button>
                )}
                <button className="btn btn-small btn-danger" onClick={() => onDelete(t.id)}>
                  Delete
                </button>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
};
export default TorrentGrid;
