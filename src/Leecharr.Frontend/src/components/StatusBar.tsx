import React, { useEffect, useState } from 'react';
import {
  UploadIcon,
  DownloadIcon,
  UsersIcon,
  WifiIcon,
  ActivityIcon,
  InfoIcon,
  SeedingIcon,
} from './icons/UIIcons';

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
  const [uptimeSeconds, setUptimeSeconds] = useState(3600 * 11 + 60 * 14);

  useEffect(() => {
    const timer = setInterval(() => {
      setUptimeSeconds(prev => prev + 1);
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  const formatUptime = (sec: number) => {
    const h = Math.floor(sec / 3600);
    const m = Math.floor((sec % 3600) / 60);
    return `${h}h ${m}m`;
  };

  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec) return '0 B/s';
    const mb = bytesPerSec / (1024 * 1024);
    if (mb >= 1) return `${mb.toFixed(1)} MB/s`;
    return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  };

  return (
    <footer className="status-bar">
      <div className="status-bar-content" style={{ display: 'flex', alignItems: 'center', width: '100%', fontSize: '0.75rem' }}>
        <span className="status-bar-item">
          <InfoIcon size={14} /> v0.1.0
        </span>
        <span className="status-bar-item">
          <ActivityIcon size={14} /> Uptime: {formatUptime(uptimeSeconds)}
        </span>
        <span className="status-bar-item" style={{ color: connected ? 'var(--success)' : 'var(--danger)' }}>
          <InfoIcon size={14} /> Health: {connected ? 'OK' : 'Connecting'}
        </span>

        <div style={{ flexGrow: 1 }} />

        <span className="status-bar-item">
          <SeedingIcon size={14} /> Active: {activeTorrents}
        </span>
        <span className="status-bar-item status-bar-upload">
          <UploadIcon size={14} /> {formatSpeed(totalUlSpeed)}
        </span>
        <span className="status-bar-item status-bar-download">
          <DownloadIcon size={14} /> {formatSpeed(totalDlSpeed)}
        </span>
        <span className="status-bar-item">
          <UsersIcon size={14} /> Torrents: {totalTorrents}
        </span>
        <span className="status-bar-item">
          <WifiIcon size={14} /> Port: 7889
        </span>
      </div>
    </footer>
  );
};
export default StatusBar;
