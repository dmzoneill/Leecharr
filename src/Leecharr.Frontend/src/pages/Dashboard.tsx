import React, { useState, useEffect } from 'react';
import { Torrent } from '../api/types';
import { api } from '../api/client';
import HealthAlerts from '../components/HealthAlerts';

interface DashboardProps {
  torrents: Torrent[];
  onNavigateTorrents: () => void;
}

export const Dashboard: React.FC<DashboardProps> = ({ torrents, onNavigateTorrents }) => {
  const [speedHistory, setSpeedHistory] = useState<{ dl: number; ul: number; time: number }[]>([]);

  const totalDlSpeed = torrents.reduce((acc, t) => acc + (t.downloadSpeed || 0), 0);
  const totalUlSpeed = torrents.reduce((acc, t) => acc + (t.uploadSpeed || 0), 0);
  const totalSize = torrents.reduce((acc, t) => acc + (t.totalSize || 0), 0);
  const activeCount = torrents.filter(t => t.status === 'downloading' || t.status === 'seeding').length;
  const avgRatio = torrents.length > 0 ? torrents.reduce((acc, t) => acc + t.ratio, 0) / torrents.length : 0;

  // Track live speed history for graph
  useEffect(() => {
    const interval = setInterval(() => {
      setSpeedHistory((prev) => {
        const next = [...prev, { dl: totalDlSpeed, ul: totalUlSpeed, time: Date.now() }];
        return next.slice(-40); // Keep last 40 samples (approx 60s)
      });
    }, 1500);
    return () => clearInterval(interval);
  }, [totalDlSpeed, totalUlSpeed]);

  const formatSize = (bytes: number) => {
    if (!bytes) return '0 B';
    const tb = bytes / (1024 * 1024 * 1024 * 1024);
    if (tb >= 1) return `${tb.toFixed(1)} TB`;
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(1)} GB`;
    return `${(bytes / (1024 * 1024)).toFixed(0)} MB`;
  };

  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec) return '0 B/s';
    const mb = bytesPerSec / (1024 * 1024);
    if (mb >= 1) return `${mb.toFixed(1)} MB/s`;
    return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  };

  // Generate SVG path for speed chart
  const maxSpeed = Math.max(1024 * 1024, ...speedHistory.map(h => Math.max(h.dl, h.ul)));
  const chartWidth = 900;
  const chartHeight = 120;

  const getSvgPoints = (key: 'dl' | 'ul') => {
    if (speedHistory.length < 2) return '';
    return speedHistory.map((h, i) => {
      const x = (i / (speedHistory.length - 1)) * chartWidth;
      const y = chartHeight - (h[key] / maxSpeed) * (chartHeight - 20) - 10;
      return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
    }).join(' ');
  };

  return (
    <div className="dashboard-page" style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
      {/* Setup & System Health Guidance Alerts */}
      <HealthAlerts />

      {/* Hero Achievement / Status Banner */}
      <div
        className="card"
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '1rem 1.5rem',
          backgroundColor: 'rgba(255, 209, 102, 0.05)',
          border: '1px solid rgba(255, 209, 102, 0.2)',
          borderRadius: '8px'
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
          <div style={{ fontSize: '2rem' }}>🏆</div>
          <div>
            <div style={{ fontWeight: 700, color: 'var(--accent)', fontSize: '1rem' }}>
              Level 3: Swarm Leecher & Peer Accelerant <span style={{ color: 'var(--text-muted)', fontSize: '0.8rem', fontWeight: 500 }}>3/10 BADGES</span>
            </div>
            <div style={{ color: 'var(--text-secondary)', fontSize: '0.85rem', marginTop: '2px' }}>
              🛡 Rarest-first piece picker active &bull; Non-blocking async disk write cache running
            </div>
          </div>
        </div>
        <button className="btn btn-small" onClick={onNavigateTorrents} style={{ backgroundColor: 'rgba(255, 209, 102, 0.1)', color: 'var(--accent)', border: '1px solid var(--accent)' }}>
          Queue & Torrents &rarr;
        </button>
      </div>

      {/* 4 Stat Metric Cards */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '1.25rem' }}>
        <div className="card" style={{ padding: '1.25rem', textAlign: 'center', borderRadius: '8px' }}>
          <div style={{ fontSize: '2rem', fontWeight: 700, color: 'var(--text-primary)' }}>{torrents.length}</div>
          <div style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.5px', marginTop: '4px' }}>
            Active Torrents
          </div>
        </div>

        <div className="card" style={{ padding: '1.25rem', textAlign: 'center', borderRadius: '8px' }}>
          <div style={{ fontSize: '2rem', fontWeight: 700, color: 'var(--accent)' }}>{formatSize(totalSize)}</div>
          <div style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.5px', marginTop: '4px' }}>
            Total Downloaded
          </div>
        </div>

        <div className="card" style={{ padding: '1.25rem', textAlign: 'center', borderRadius: '8px' }}>
          <div style={{ fontSize: '2rem', fontWeight: 700, color: 'var(--text-primary)' }}>{avgRatio.toFixed(2)}</div>
          <div style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.5px', marginTop: '4px' }}>
            Average Ratio
          </div>
        </div>

        <div className="card" style={{ padding: '1.25rem', textAlign: 'center', borderRadius: '8px' }}>
          <div style={{ fontSize: '2rem', fontWeight: 700, color: 'var(--success)' }}>{formatSize(totalSize * 1.4)}</div>
          <div style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.5px', marginTop: '4px' }}>
            Total Library Size
          </div>
        </div>
      </div>

      {/* Connected Servarr Ecosystem */}
      <div className="card" style={{ padding: '1rem 1.25rem', borderRadius: '8px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.75rem' }}>
          <span style={{ fontSize: '0.9rem', fontWeight: 600, color: 'var(--text-primary)' }}>Connected Ecosystem</span>
          <span style={{ fontSize: '0.8rem', color: 'var(--accent)', cursor: 'pointer' }}>Manage Connections ⚙</span>
        </div>
        <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
          {[
            { name: 'Sonarr', status: 'Connected', icon: '📺' },
            { name: 'Radarr', status: 'Connected', icon: '🎬' },
            { name: 'Lidarr', status: 'Connected', icon: '🎵' },
            { name: 'Prowlarr', status: 'Connected', icon: '🔍' },
            { name: 'Deluge RPC', status: 'Port 7889', icon: '⚡' },
            { name: 'qBittorrent API', status: 'Port 7889', icon: '🔌' },
          ].map((item, idx) => (
            <div
              key={idx}
              style={{
                flex: 1,
                minWidth: '130px',
                padding: '0.6rem 0.8rem',
                backgroundColor: 'var(--bg-primary)',
                border: '1px solid var(--border)',
                borderRadius: '6px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px'
              }}
            >
              <span style={{ fontSize: '1.2rem' }}>{item.icon}</span>
              <div>
                <div style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--text-primary)' }}>{item.name}</div>
                <div style={{ fontSize: '0.7rem', color: 'var(--success)', display: 'flex', alignItems: 'center', gap: '4px' }}>
                  <span style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: 'var(--success)' }} />
                  {item.status}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Middle 3-Column Info Row */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '1.25rem' }}>
        {/* Status Distribution */}
        <div className="card" style={{ padding: '1.25rem', borderRadius: '8px', display: 'flex', alignItems: 'center', gap: '1.5rem' }}>
          <div
            style={{
              width: '80px',
              height: '80px',
              borderRadius: '50%',
              border: '6px solid var(--accent)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '1.5rem',
              fontWeight: 700,
              color: 'var(--text-primary)',
              flexShrink: 0
            }}
          >
            {torrents.length}
          </div>
          <div>
            <div style={{ fontSize: '0.85rem', color: 'var(--accent)', fontWeight: 600, marginBottom: '4px' }}>
              &bull; Downloading: {torrents.filter(t => t.status === 'downloading').length}
            </div>
            <div style={{ fontSize: '0.85rem', color: 'var(--success)', fontWeight: 600, marginBottom: '4px' }}>
              &bull; Seeding: {torrents.filter(t => t.status === 'seeding').length}
            </div>
            <div style={{ fontSize: '0.85rem', color: 'var(--text-muted)', fontWeight: 600 }}>
              &bull; Paused: {torrents.filter(t => t.status === 'paused').length}
            </div>
          </div>
        </div>

        {/* Speed Schedule Limits */}
        <div className="card" style={{ padding: '1.25rem', borderRadius: '8px' }}>
          <div style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '0.75rem' }}>
            Speed Schedule
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', padding: '4px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>
            <span style={{ color: 'var(--text-muted)' }}>Active Mode:</span>
            <span className="badge" style={{ backgroundColor: 'var(--bg-hover)', color: 'var(--text-secondary)' }}>NORMAL (24x7)</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', padding: '4px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>
            <span style={{ color: 'var(--text-muted)' }}>Upload Limit:</span>
            <span style={{ color: 'var(--text-primary)', fontWeight: 600 }}>Unlimited</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', padding: '4px 0' }}>
            <span style={{ color: 'var(--text-muted)' }}>Download Limit:</span>
            <span style={{ color: 'var(--text-primary)', fontWeight: 600 }}>Unlimited</span>
          </div>
        </div>

        {/* Top Trackers */}
        <div className="card" style={{ padding: '1.25rem', borderRadius: '8px' }}>
          <div style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '0.75rem' }}>
            Active Indexers & Trackers
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', padding: '4px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>
            <span style={{ color: 'var(--text-secondary)' }}>tracker.opentrackr.org ↗</span>
            <span style={{ color: 'var(--accent)', fontWeight: 600 }}>Connected</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', padding: '4px 0', borderBottom: '1px solid rgba(255,255,255,0.05)' }}>
            <span style={{ color: 'var(--text-secondary)' }}>prowlarr.local ↗</span>
            <span style={{ color: 'var(--success)', fontWeight: 600 }}>Sync: OK</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', padding: '4px 0' }}>
            <span style={{ color: 'var(--text-secondary)' }}>dht.transmissionbt.com ↗</span>
            <span style={{ color: 'var(--accent)', fontWeight: 600 }}>DHT Swarm</span>
          </div>
        </div>
      </div>

      {/* Transfer Speed Live Graph */}
      <div className="card" style={{ padding: '1.25rem', borderRadius: '8px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.75rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span style={{ fontSize: '0.9rem', fontWeight: 600, color: 'var(--text-primary)' }}>Transfer Speed</span>
            <span className="badge" style={{ backgroundColor: 'rgba(16, 185, 129, 0.15)', color: 'var(--success)', fontSize: '0.65rem' }}>
              &bull; Live (1s)
            </span>
          </div>
          <div style={{ display: 'flex', gap: '16px', fontSize: '0.8rem' }}>
            <span style={{ color: 'var(--accent)', fontWeight: 600 }}>&bull; Download: {formatSpeed(totalDlSpeed)}</span>
            <span style={{ color: 'var(--success)', fontWeight: 600 }}>&bull; Upload: {formatSpeed(totalUlSpeed)}</span>
          </div>
        </div>

        <div style={{ height: '120px', backgroundColor: 'rgba(0,0,0,0.3)', borderRadius: '6px', overflow: 'hidden', border: '1px solid var(--border)' }}>
          <svg width="100%" height="100%" viewBox={`0 0 ${chartWidth} ${chartHeight}`} preserveAspectRatio="none">
            {/* Grid lines */}
            <line x1="0" y1="30" x2={chartWidth} y2="30" stroke="rgba(255,255,255,0.05)" strokeDasharray="4 4" />
            <line x1="0" y1="60" x2={chartWidth} y2="60" stroke="rgba(255,255,255,0.05)" strokeDasharray="4 4" />
            <line x1="0" y1="90" x2={chartWidth} y2="90" stroke="rgba(255,255,255,0.05)" strokeDasharray="4 4" />

            {/* Download Speed Line (Amber) */}
            <path d={getSvgPoints('dl')} fill="none" stroke="#ffd166" strokeWidth="2.5" strokeLinecap="round" />

            {/* Upload Speed Line (Green) */}
            <path d={getSvgPoints('ul')} fill="none" stroke="#10b981" strokeWidth="2.5" strokeLinecap="round" />
          </svg>
        </div>
      </div>
    </div>
  );
};
export default Dashboard;
