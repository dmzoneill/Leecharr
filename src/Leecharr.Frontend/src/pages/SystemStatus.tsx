import React, { useEffect, useState } from 'react';
import { SystemStatus as SystemStatusType } from '../api/types';
import { api } from '../api/client';

export const SystemStatus: React.FC = () => {
  const [status, setStatus] = useState<SystemStatusType | null>(null);

  useEffect(() => {
    api.getSystemStatus().then(setStatus).catch(console.error);
  }, []);

  return (
    <div className="system-status-page">
      <div className="page-header">
        <h2>System Status & Health</h2>
        <p className="text-muted">Host runtime, operating system, and data directories.</p>
      </div>

      <div className="status-grid">
        <div className="status-card">
          <h3>Application Details</h3>
          <div className="status-row">
            <span className="label">Application:</span>
            <span className="value">Leecharr</span>
          </div>
          <div className="status-row">
            <span className="label">Version:</span>
            <span className="value">{status?.version || '0.1.0'}</span>
          </div>
          <div className="status-row">
            <span className="label">Runtime:</span>
            <span className="value">{status?.runtimeVersion || '.NET 10.0'}</span>
          </div>
          <div className="status-row">
            <span className="label">Operating System:</span>
            <span className="value">{status?.osName || 'Linux'} ({status?.osVersion || 'x64'})</span>
          </div>
          <div className="status-row">
            <span className="label">AppData Directory:</span>
            <span className="value"><code>{status?.appDataFolder || '/config'}</code></span>
          </div>
        </div>

        <div className="status-card">
          <h3>Simultaneous Client Adapters</h3>
          <div className="adapter-list">
            <div className="adapter-item">
              <span className="adapter-badge badge-qbit">qBittorrent WebAPI v2</span>
              <span className="adapter-path"><code>/api/v2/*</code></span>
            </div>
            <div className="adapter-item">
              <span className="adapter-badge badge-deluge">Deluge JSON-RPC</span>
              <span className="adapter-path"><code>/json</code></span>
            </div>
            <div className="adapter-item">
              <span className="adapter-badge badge-trans">Transmission RPC</span>
              <span className="adapter-path"><code>/transmission/rpc</code></span>
            </div>
            <div className="adapter-item">
              <span className="adapter-badge badge-rest">Native REST v1</span>
              <span className="adapter-path"><code>/api/v1/*</code></span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
export default SystemStatus;
