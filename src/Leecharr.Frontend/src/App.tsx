import React, { useEffect, useState } from 'react';
import { api } from './api/client';
import { signalRManager } from './api/signalr';
import { Torrent, Category } from './api/types';
import { LeecharrLogo } from './components/icons/LeecharrLogo';
import { LeecharrText } from './components/icons/LeecharrText';
import {
  DashboardIcon,
  TorrentIcon,
  SettingsIcon,
  SystemIcon,
} from './components/icons/NavIcons';
import { ActivityIcon } from './components/icons/UIIcons';
import {
  ScheduleIcon,
  SearchIcon,
  PeerMapIcon,
} from './components/icons/AppIcons';
import { TorrentIndex } from './pages/TorrentIndex';
import { SpeedSchedule } from './pages/SpeedSchedule';
import { Indexers } from './pages/Indexers';
import { Settings } from './pages/Settings';
import { SystemStatus } from './pages/SystemStatus';
import { StatusBar } from './components/StatusBar';
import { IndexerSearchModal } from './components/IndexerSearchModal';
import './App.css';

const settingsSubItems = [
  { id: 'general', label: 'General' },
  { id: 'categories', label: 'Categories' },
  { id: 'notifications', label: 'Notifications' },
  { id: 'bandwidth', label: 'Bandwidth' },
  { id: 'network', label: 'Network & VPN' },
  { id: 'clients', label: 'Client Adapters' },
  { id: 'indexers', label: 'Indexers' },
  { id: 'advanced', label: 'Advanced' },
];

const systemSubItems = [
  { id: 'status', label: 'Status' },
  { id: 'tasks', label: 'Tasks' },
  { id: 'backup', label: 'Backup' },
  { id: 'updates', label: 'Updates' },
  { id: 'events', label: 'Events' },
  { id: 'logs', label: 'Log Files' },
  { id: 'network', label: 'Network' },
];

export function App() {
  const [activeNav, setActiveNav] = useState<string>('torrents');
  const [activeSubNav, setActiveSubNav] = useState<string>('general');
  const [torrents, setTorrents] = useState<Torrent[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [connected, setConnected] = useState<boolean>(false);

  // Modals state
  const [showAddModal, setShowAddModal] = useState<boolean>(false);
  const [showSearchModal, setShowSearchModal] = useState<boolean>(false);
  const [magnetInput, setMagnetInput] = useState<string>('');
  const [categoryInput, setCategoryInput] = useState<string>('');
  const [isPausedInput, setIsPausedInput] = useState<boolean>(false);

  const loadData = async () => {
    try {
      const [tList, cList] = await Promise.all([
        api.getTorrents(),
        api.getCategories(),
      ]);
      setTorrents(tList);
      setCategories(cList);
    } catch (err) {
      console.error('Failed to load initial data:', err);
    }
  };

  useEffect(() => {
    loadData();
    signalRManager.start();
    setConnected(true);

    const unsubscribe = signalRManager.subscribe((msg) => {
      if (msg.name === 'torrent' || msg.name === 'speedpulse') {
        loadData();
      }
    });

    return () => unsubscribe();
  }, []);

  const totalDlSpeed = torrents.reduce((acc, t) => acc + (t.downloadSpeed || 0), 0);
  const totalUlSpeed = torrents.reduce((acc, t) => acc + (t.uploadSpeed || 0), 0);
  const activeCount = torrents.filter(t => t.status === 'downloading' || t.status === 'seeding').length;

  const handlePause = async (id: number) => {
    await api.pauseTorrent(id);
    loadData();
  };

  const handleResume = async (id: number) => {
    await api.resumeTorrent(id);
    loadData();
  };

  const handleDelete = async (id: number) => {
    if (window.confirm('Are you sure you want to delete this torrent?')) {
      await api.deleteTorrent(id, false);
      loadData();
    }
  };

  const handleAddMagnet = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!magnetInput) return;

    try {
      await api.addTorrentMagnet(magnetInput, categoryInput, '', isPausedInput);
      setMagnetInput('');
      setShowAddModal(false);
      loadData();
    } catch (err) {
      alert(`Error adding torrent: ${err}`);
    }
  };

  const formatSpeed = (bytesPerSec: number) => {
    if (!bytesPerSec || bytesPerSec === 0) return '0 KB/s';
    const mb = bytesPerSec / (1024 * 1024);
    if (mb >= 1) return `${mb.toFixed(1)} MB/s`;
    return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  };

  return (
    <div className="app-layout">
      {/* Servarr Left Sidebar */}
      <aside className="app-sidebar">
        <div className="sidebar-logo">
          <LeecharrLogo size={42} className="brand-logo" />
          <LeecharrText width={130} className="brand-text" />
        </div>

        <nav className="sidebar-nav">
          {/* Torrents / Downloads */}
          <button
            className={`sidebar-nav-item ${activeNav === 'torrents' ? 'active' : ''}`}
            onClick={() => setActiveNav('torrents')}
          >
            <TorrentIcon size={18} />
            <span className="sidebar-nav-label">Torrents</span>
            {torrents.length > 0 && <span className="nav-badge">{torrents.length}</span>}
          </button>

          {/* Indexer Search */}
          <button
            className={`sidebar-nav-item ${activeNav === 'indexers' ? 'active' : ''}`}
            onClick={() => setActiveNav('indexers')}
          >
            <SearchIcon size={18} />
            <span className="sidebar-nav-label">Indexers</span>
          </button>

          {/* Activity / Swarm */}
          <button
            className={`sidebar-nav-item ${activeNav === 'activity' ? 'active' : ''}`}
            onClick={() => setActiveNav('activity')}
          >
            <ActivityIcon size={18} />
            <span className="sidebar-nav-label">Activity</span>
          </button>

          {/* Peer Map */}
          <button
            className={`sidebar-nav-item ${activeNav === 'peermap' ? 'active' : ''}`}
            onClick={() => setActiveNav('peermap')}
          >
            <PeerMapIcon size={18} />
            <span className="sidebar-nav-label">Peer Map</span>
          </button>

          {/* Speed Schedule */}
          <button
            className={`sidebar-nav-item ${activeNav === 'schedule' ? 'active' : ''}`}
            onClick={() => setActiveNav('schedule')}
          >
            <ScheduleIcon size={18} />
            <span className="sidebar-nav-label">Schedule</span>
          </button>

          {/* Settings Top-Level & Submenu */}
          <button
            className={`sidebar-nav-item ${activeNav === 'settings' ? 'active' : ''}`}
            onClick={() => {
              setActiveNav('settings');
              setActiveSubNav('general');
            }}
          >
            <SettingsIcon size={18} />
            <span className="sidebar-nav-label">Settings</span>
          </button>
          {activeNav === 'settings' && (
            <div className="sidebar-submenu">
              {settingsSubItems.map((item) => (
                <button
                  key={item.id}
                  className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === item.id ? 'active' : ''}`}
                  onClick={() => setActiveSubNav(item.id)}
                >
                  <span>{item.label}</span>
                </button>
              ))}
            </div>
          )}

          {/* System Top-Level & Submenu */}
          <button
            className={`sidebar-nav-item ${activeNav === 'system' ? 'active' : ''}`}
            onClick={() => {
              setActiveNav('system');
              setActiveSubNav('status');
            }}
          >
            <SystemIcon size={18} />
            <span className="sidebar-nav-label">System</span>
          </button>
          {activeNav === 'system' && (
            <div className="sidebar-submenu">
              {systemSubItems.map((item) => (
                <button
                  key={item.id}
                  className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === item.id ? 'active' : ''}`}
                  onClick={() => setActiveSubNav(item.id)}
                >
                  <span>{item.label}</span>
                </button>
              ))}
            </div>
          )}
        </nav>

        <div className="sidebar-footer">
          <div className="version-info">Leecharr v0.1.0</div>
        </div>
      </aside>

      {/* Main Area */}
      <div className="app-main">
        {/* Topbar Header */}
        <header className="app-header">
          <div className="header-title">
            <h2>
              {activeNav === 'torrents' && 'Downloads'}
              {activeNav === 'indexers' && 'Torznab Search'}
              {activeNav === 'activity' && 'Swarm Activity'}
              {activeNav === 'peermap' && 'Peer Map'}
              {activeNav === 'schedule' && 'Speed Schedule'}
              {activeNav === 'settings' && `Settings — ${settingsSubItems.find(s => s.id === activeSubNav)?.label || 'General'}`}
              {activeNav === 'system' && `System — ${systemSubItems.find(s => s.id === activeSubNav)?.label || 'Status'}`}
            </h2>
          </div>

          <div className="header-actions">
            <div className="speed-meters">
              <div className="meter-item dl">
                <span>↓</span>
                <span>{formatSpeed(totalDlSpeed)}</span>
              </div>
              <div className="meter-item ul">
                <span>↑</span>
                <span>{formatSpeed(totalUlSpeed)}</span>
              </div>
            </div>

            <button className="btn btn-primary" onClick={() => setShowAddModal(true)}>
              + Add Torrent
            </button>
          </div>
        </header>

        {/* Content Page */}
        <main className="app-content">
          {activeNav === 'torrents' && (
            <TorrentIndex
              torrents={torrents}
              categories={categories}
              selectedCategory={selectedCategory}
              onSelectCategory={setSelectedCategory}
              onPause={handlePause}
              onResume={handleResume}
              onDelete={handleDelete}
              onOpenAddModal={() => setShowAddModal(true)}
              onOpenSearchModal={() => setShowSearchModal(true)}
            />
          )}

          {activeNav === 'indexers' && <Indexers />}
          {activeNav === 'activity' && <SystemStatus />}
          {activeNav === 'peermap' && <Indexers />}
          {activeNav === 'schedule' && <SpeedSchedule />}
          {activeNav === 'settings' && <Settings categories={categories} />}
          {activeNav === 'system' && <SystemStatus />}
        </main>

        {/* Bottom Status Bar */}
        <StatusBar
          totalTorrents={torrents.length}
          activeTorrents={activeCount}
          totalDlSpeed={totalDlSpeed}
          totalUlSpeed={totalUlSpeed}
          connected={connected}
        />
      </div>

      {/* Add Modal */}
      {showAddModal && (
        <div className="modal-backdrop" onClick={() => setShowAddModal(false)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h2>Add Torrent Download</h2>
            <form onSubmit={handleAddMagnet} className="modal-form">
              <div className="form-group">
                <label>Magnet Link or InfoHash</label>
                <input
                  type="text"
                  placeholder="magnet:?xt=urn:btih:..."
                  value={magnetInput}
                  onChange={(e) => setMagnetInput(e.target.value)}
                  required
                />
              </div>

              <div className="form-group">
                <label>Category</label>
                <select value={categoryInput} onChange={(e) => setCategoryInput(e.target.value)}>
                  <option value="">(None)</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.name}>
                      {c.name} ({c.savePath})
                    </option>
                  ))}
                </select>
              </div>

              <div className="checkbox-row">
                <input
                  type="checkbox"
                  id="pausedCheck"
                  checked={isPausedInput}
                  onChange={(e) => setIsPausedInput(e.target.checked)}
                />
                <label htmlFor="pausedCheck">Start in Paused State</label>
              </div>

              <div className="modal-actions">
                <button type="button" className="btn btn-secondary" onClick={() => setShowAddModal(false)}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary">
                  + Add Download
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Indexer Search Modal */}
      {showSearchModal && (
        <IndexerSearchModal
          onClose={() => setShowSearchModal(false)}
          onTorrentAdded={loadData}
        />
      )}
    </div>
  );
}
export default App;
