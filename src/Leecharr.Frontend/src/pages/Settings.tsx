import React, { useState } from 'react';
import { Category } from '../api/types';

interface SettingsProps {
  categories: Category[];
}

export const Settings: React.FC<SettingsProps> = ({ categories }) => {
  const [activeTab, setActiveTab] = useState<'general' | 'categories' | 'bandwidth' | 'network'>('general');

  return (
    <div className="settings-page">
      <div className="page-header">
        <h2>Leecharr Settings</h2>
        <p className="text-muted">Manage storage paths, category routing, speed limits, and VPN safety.</p>
      </div>

      <div className="settings-tabs-header">
        <button
          className={`settings-tab-btn ${activeTab === 'general' ? 'active' : ''}`}
          onClick={() => setActiveTab('general')}
        >
          General
        </button>
        <button
          className={`settings-tab-btn ${activeTab === 'categories' ? 'active' : ''}`}
          onClick={() => setActiveTab('categories')}
        >
          Categories ({categories.length})
        </button>
        <button
          className={`settings-tab-btn ${activeTab === 'bandwidth' ? 'active' : ''}`}
          onClick={() => setActiveTab('bandwidth')}
        >
          Bandwidth & Limits
        </button>
        <button
          className={`settings-tab-btn ${activeTab === 'network' ? 'active' : ''}`}
          onClick={() => setActiveTab('network')}
        >
          Network & VPN Kill Switch
        </button>
      </div>

      <div className="settings-tab-content">
        {activeTab === 'general' && (
          <div className="settings-card">
            <h3>Host & API Integration</h3>
            <div className="form-group">
              <label>Listen Port</label>
              <input type="number" defaultValue={7889} disabled />
              <span className="help-text">Simultaneous API port for Native REST, qBittorrent WebAPI v2, Deluge, and Transmission RPC.</span>
            </div>
            <div className="form-group">
              <label>Incomplete Download Folder</label>
              <input type="text" defaultValue="/downloads/incomplete" />
              <span className="help-text">Temporary staging directory during piece downloading with sparse file allocation.</span>
            </div>
          </div>
        )}

        {activeTab === 'categories' && (
          <div className="settings-card">
            <h3>Configured Categories</h3>
            <div className="table-responsive">
              <table className="table-torrents">
                <thead>
                  <tr>
                    <th>Category</th>
                    <th>Destination Path</th>
                    <th>Target Ratio</th>
                    <th>Auto Stop</th>
                  </tr>
                </thead>
                <tbody>
                  {categories.map((c) => (
                    <tr key={c.id}>
                      <td><strong>{c.name}</strong></td>
                      <td><code>{c.savePath}</code></td>
                      <td>{c.targetRatio || 'Unlimited'}</td>
                      <td>{c.autoStop ? 'Yes' : 'No'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {activeTab === 'bandwidth' && (
          <div className="settings-card">
            <h3>Global Bandwidth Limits</h3>
            <div className="form-group">
              <label>Global Download Limit (KB/s)</label>
              <input type="number" defaultValue={0} />
              <span className="help-text">0 = Unlimited</span>
            </div>
            <div className="form-group">
              <label>Global Upload Limit (KB/s)</label>
              <input type="number" defaultValue={0} />
              <span className="help-text">0 = Unlimited</span>
            </div>
          </div>
        )}

        {activeTab === 'network' && (
          <div className="settings-card">
            <h3>Network Security & VPN Protection</h3>
            <div className="form-group">
              <label>Network Interface Binding</label>
              <input type="text" placeholder="e.g. tun0, wg0, eth0" defaultValue="tun0" />
            </div>
            <div className="form-group">
              <label className="checkbox-label">
                <input type="checkbox" defaultChecked />
                <strong>Enable Automated VPN Kill Switch</strong>
              </label>
              <span className="help-text">Instantly drops all tracker announces and BitTorrent sockets if the bound interface disconnects.</span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
export default Settings;
