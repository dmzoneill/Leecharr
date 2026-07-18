import React from 'react';

interface StatusBarProps {
  totalTorrents: number;
  activeTorrents: number;
  totalDlSpeed: number;
  totalUlSpeed: number;
  connected: boolean;
}

export const StatusBar: React.FC<StatusBarProps> = ({
  totalTorrents,
  activeTorrents,
  totalDlSpeed,
  totalUlSpeed,
  connected
}) => {
  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec) return '0 B/s';
    const mb = bytesPerSec / (1024 * 1024);
    if (mb >= 1) return `${mb.toFixed(1)} MB/s`;
    return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  };

  return (
    <footer className="status-bar">
      <div className="status-bar-left">
        <span className={`status-bar-dot ${connected ? 'connected' : 'disconnected'}`} />
        <span className="status-bar-item">
          {connected ? 'SignalR Connected' : 'Connecting...'}
        </span>
        <span className="status-bar-separator">|</span>
        <span className="status-bar-item">
          Torrents: <strong>{totalTorrents}</strong> ({activeTorrents} active)
        </span>
      </div>

      <div className="status-bar-right">
        <span className="status-bar-item" style={{ color: 'var(--accent)' }}>
          ↓ {formatSpeed(totalDlSpeed)}
        </span>
        <span className="status-bar-item" style={{ color: 'var(--success)' }}>
          ↑ {formatSpeed(totalUlSpeed)}
        </span>
        <span className="status-bar-separator">|</span>
        <span className="status-bar-item">
          Port: <strong>7889</strong>
        </span>
        <span className="status-bar-separator">|</span>
        <span className="status-bar-item">
          Leecharr v0.1.0
        </span>
      </div>
    </footer>
  );
};
export default StatusBar;
