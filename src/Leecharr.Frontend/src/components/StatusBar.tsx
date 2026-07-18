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
    <footer className="app-statusbar">
      <div className="statusbar-left">
        <span className={`status-indicator ${connected ? 'connected' : 'disconnected'}`} />
        <span className="statusbar-text">
          {connected ? 'SignalR Connected' : 'Connecting to Server...'}
        </span>
        <span className="statusbar-divider">|</span>
        <span className="statusbar-text">
          Torrents: <strong>{totalTorrents}</strong> ({activeTorrents} active)
        </span>
      </div>

      <div className="statusbar-right">
        <span className="statusbar-item status-dl">
          ↓ {formatSpeed(totalDlSpeed)}
        </span>
        <span className="statusbar-item status-ul">
          ↑ {formatSpeed(totalUlSpeed)}
        </span>
        <span className="statusbar-divider">|</span>
        <span className="statusbar-text">
          Port: <strong>7889</strong>
        </span>
      </div>
    </footer>
  );
};
export default StatusBar;
