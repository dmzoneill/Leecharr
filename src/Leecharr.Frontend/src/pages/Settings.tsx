import React from 'react';
import { useParams } from 'react-router';
import { GeneralTab } from './settings/GeneralTab';
import { SeedingTab } from './settings/SeedingTab';
import { BitTorrentTab } from './settings/BitTorrentTab';
import { NetworkTab } from './settings/NetworkTab';
import { PeerProtocolTab } from './settings/PeerProtocolTab';
import { ProtocolsTab } from './settings/ProtocolsTab';
import { SimulationTab } from './settings/SimulationTab';
import { TrackerServerTab } from './settings/TrackerServerTab';
import { SchedulerTab } from './settings/SchedulerTab';
import { AdvancedTab } from './settings/AdvancedTab';
import { IndexersTab } from './settings/IndexersTab';
import { ConnectionsTab } from './settings/ConnectionsTab';
import { DownloadClientsTab } from './settings/DownloadClientsTab';
import { NotificationsTab } from './settings/NotificationsTab';
import { WebUITab } from './settings/WebUITab';

interface SettingsProps {
  section?: string;
}

const sectionTitles: Record<string, string> = {
  general: 'General',
  webui: 'Web UI',
  notifications: 'Notifications & Webhooks',
  seeding: 'Categories & Storage',
  bittorrent: 'BitTorrent Engine',
  network: 'Network & VPN Kill Switch',
  'peer-protocol': 'Peer Protocol & Swarm',
  protocols: 'Protocols',
  simulation: 'Simulation',
  'tracker-server': 'Tracker Server',
  scheduler: 'Speed Scheduler',
  indexers: 'Torznab & Indexers',
  connections: 'Servarr Connections',
  'download-clients': 'Client Compatibility Adapters',
  advanced: 'Advanced & Diagnostics',
};

const sectionDescriptions: Record<string, string> = {
  general: 'Configure application host parameters, ports, and watch folder automation',
  webui: 'Configure web user interface access, authentication modes, and sessions',
  notifications: 'Configure webhooks and alerts for download grab, completion, and seed goals',
  seeding: 'Manage categories, incomplete storage paths, target ratios, and auto-stop rules',
  bittorrent: 'Configure MonoTorrent engine, rarest-first picker, write cache, and encryption',
  network: 'Network interface binding (tun0/wg0), VPN Kill Switch, and proxy settings',
  'peer-protocol': 'Peer handshake timeouts, keepalive intervals, and swarm behavior',
  protocols: 'BEP extensions, transport layers (TCP/uTP), DHT, and PEX peer exchange',
  simulation: 'Swarm simulation and peer heuristics',
  'tracker-server': 'Inbuilt tracker configuration and announce intervals',
  scheduler: '24x7 hourly speed throttling schedule matrix',
  indexers: 'Prowlarr synchronization and direct Torznab indexer discovery',
  connections: 'Integration with Sonarr, Radarr, Lidarr, and Readarr',
  'download-clients': 'Simultaneous qBittorrent WebAPI, Deluge JSON-RPC, and Transmission RPC adapters',
  advanced: 'Diagnostic logs, SQLite vacuum, and developer options',
};

export function Settings({ section: propSection }: SettingsProps) {
  const params = useParams<{ section?: string }>();
  const activeSection = propSection || params.section || 'general';
  const title = sectionTitles[activeSection] || 'Settings';
  const description =
    sectionDescriptions[activeSection] ||
    'Manage Leecharr application and operational parameters';

  return (
    <div className="content-area" style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
      <div
        className="page-header"
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: '0.5rem',
        }}
      >
        <div className="page-header-group">
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
            <h1 className="page-heading" style={{ margin: 0 }}>
              {title}
            </h1>
            <span className="badge badge-primary">Settings</span>
          </div>
          <div
            style={{
              fontSize: '0.85rem',
              color: 'var(--text-muted)',
              marginTop: '0.25rem',
            }}
          >
            {description}
          </div>
        </div>
      </div>

      {activeSection === 'general' && <GeneralTab />}
      {activeSection === 'webui' && <WebUITab />}
      {activeSection === 'notifications' && <NotificationsTab />}
      {activeSection === 'seeding' && <SeedingTab />}
      {activeSection === 'categories' && <SeedingTab />}
      {activeSection === 'bandwidth' && <NetworkTab />}
      {activeSection === 'bittorrent' && <BitTorrentTab />}
      {activeSection === 'network' && <NetworkTab />}
      {activeSection === 'peer-protocol' && <PeerProtocolTab />}
      {activeSection === 'protocols' && <ProtocolsTab />}
      {activeSection === 'simulation' && <SimulationTab />}
      {activeSection === 'tracker-server' && <TrackerServerTab />}
      {activeSection === 'scheduler' && <SchedulerTab />}
      {activeSection === 'indexers' && <IndexersTab />}
      {activeSection === 'connections' && <ConnectionsTab />}
      {activeSection === 'download-clients' && <DownloadClientsTab />}
      {activeSection === 'clients' && <DownloadClientsTab />}
      {activeSection === 'advanced' && <AdvancedTab />}
    </div>
  );
}

export default Settings;
