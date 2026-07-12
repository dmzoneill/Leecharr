import React, { useEffect, useState } from 'react';
import { api } from './api/client';
import { signalRManager } from './api/signalr';
import { Torrent, Category } from './api/types';
import { LeecharrLogo } from './components/icons/LeecharrLogo';
import { LeecharrText } from './components/icons/LeecharrText';
import {
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
    <div className="app">
      {/* Servarr Left Sidebar */}
      <aside className="sidebar">
        <div className="sidebar-logo">
          <LeecharrLogo size={36} className="brand-logo" />
          <LeecharrText width={120} className="brand-text" />
        </div>

        <nav className="sidebar-nav">
          {/* Torrents / Downloads */}
          <button
            className={`sidebar-nav-item ${activeNav === 'torrents' ? 'active' : ''}`}
            onClick={() => setActiveNav('torrents')}
          >
            <TorrentIcon size={16} />
            <span>Torrents</span>
          </button>

          {/* Indexer Search */}
          <button
            className={`sidebar-nav-item ${activeNav === 'indexers' ? 'active' : ''}`}
            onClick={() => setActiveNav('indexers')}
          >
            <SearchIcon size={16} />
            <span>Indexers</span>
          </button>

          {/* Activity / Swarm */}
          <button
            className={`sidebar-nav-item ${activeNav === 'activity' ? 'active' : ''}`}
            onClick={() => setActiveNav('activity')}
          >
            <ActivityIcon size={16} />
            <span>Activity</span>
          </button>

          {/* Peer Map */}
          <button
            className={`sidebar-nav-item ${activeNav === 'peermap' ? 'active' : ''}`}
            onClick={() => setActiveNav('peermap')}
          >
            <PeerMapIcon size={16} />
            <span>Peer Map</span>
          </button>

          {/* Speed Schedule */}
          <button
            className={`sidebar-nav-item ${activeNav === 'schedule' ? 'active' : ''}`}
            onClick={() => setActiveNav('schedule')}
          >
            <ScheduleIcon size={16} />
            <span>Schedule</span>
          </button>

          {/* Settings Top-Level & Submenu */}
          <button
            className={`sidebar-nav-item ${activeNav === 'settings' ? 'active' : ''}`}
            onClick={() => {
              setActiveNav('settings');
              setActiveSubNav('general');
            }}
          >
            <SettingsIcon size={16} />
            <span>Settings</span>
          </button>
          {activeNav === 'settings' &&
            settingsSubItems.map((item) => (
              <button
                key={item.id}
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === item.id ? 'active' : ''}`}
                onClick={() => setActiveSubNav(item.id)}
              >
                <span>{item.label}</span>
              </button>
            ))}

          {/* System Top-Level & Submenu */}
          <button
            className={`sidebar-nav-item ${activeNav === 'system' ? 'active' : ''}`}
            onClick={() => {
              setActiveNav('system');
              setActiveSubNav('status');
            }}
          >
            <SystemIcon size={16} />
            <span>System</span>
          </button>
          {activeNav === 'system' &&
            systemSubItems.map((item) => (
              <button
                key={item.id}
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === item.id ? 'active' : ''}`}
                onClick={() => setActiveSubNav(item.id)}
              >
                <span>{item.label}</span>
              </button>
            ))}
        </nav>

        <div className="sidebar-footer" style={{ marginTop: 'auto', padding: '1rem', textAlign: 'center', borderTop: '1px solid var(--border-light)' }}>
          <span style={{ color: 'var(--text-dim)', fontSize: '0.75rem' }}>Leecharr v0.1.0</span>
        </div>
      </aside>

      {/* Main Wrapper */}
      <div className="main-wrapper">
        {/* Topbar Header */}
        <header className="topbar">
          <div
            className="topbar-search"
            onClick={() => setShowSearchModal(true)}
            style={{ cursor: 'pointer' }}
            title="Search Indexers (Ctrl+K or /)"
          >
            <SearchIcon size={14} />
            <input
              type="text"
              placeholder="Search Indexers... (Ctrl+K or /)"
              className="topbar-search-input"
              readOnly
              style={{ cursor: 'pointer' }}
            />
            <kbd style={{ backgroundColor: 'rgba(255, 255, 255, 0.08)', border: '1px solid rgba(255, 255, 255, 0.16)', borderRadius: '3px', padding: '0.1rem 0.4rem', fontSize: '0.7rem', color: 'var(--text-muted)', fontFamily: 'monospace' }}>
              Ctrl+K
            </kbd>
          </div>

          <div className="topbar-actions" style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
            <div className="topbar-speed-meter" style={{ display: 'flex', gap: '0.75rem', background: 'rgba(0,0,0,0.3)', padding: '0.2rem 0.6rem', borderRadius: '4px', border: '1px solid var(--border)' }}>
              <span style={{ color: 'var(--accent)', fontSize: '0.8rem', fontWeight: 600 }}>↓ {formatSpeed(totalDlSpeed)}</span>
              <span style={{ color: 'var(--success)', fontSize: '0.8rem', fontWeight: 600 }}>↑ {formatSpeed(totalUlSpeed)}</span>
            </div>

            <button className="btn btn-small btn-success" onClick={() => setShowAddModal(true)}>
              + Add Torrent
            </button>
          </div>
        </header>

        {/* Page Content */}
        <main className="content">
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

        {/* Status Bar */}
        <StatusBar
          totalTorrents={torrents.length}
          activeTorrents={activeCount}
          totalDlSpeed={totalDlSpeed}
          totalUlSpeed={totalUlSpeed}
          connected={connected}
        />
      </div>

      {/* Add Torrent Modal */}
      {showAddModal && (
        <div className="modal-overlay" onClick={() => setShowAddModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '500px' }}>
            <div className="modal-header">
              <h2 className="modal-title">Add Torrent Download</h2>
              <button className="modal-close" onClick={() => setShowAddModal(false)}>&times;</button>
            </div>
            <form onSubmit={handleAddMagnet} style={{ display: 'flex', flexDirection: 'column', gap: '1rem', marginTop: '1rem' }}>
              <div className="form-group">
                <label className="form-label">Magnet Link or InfoHash</label>
                <input
                  type="text"
                  placeholder="magnet:?xt=urn:btih:..."
                  value={magnetInput}
                  onChange={(e) => setMagnetInput(e.target.value)}
                  className="form-input"
                  required
                />
              </div>

              <div className="form-group">
                <label className="form-label">Category</label>
                <select
                  value={categoryInput}
                  onChange={(e) => setCategoryInput(e.target.value)}
                  className="form-input"
                >
                  <option value="">(None)</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.name}>
                      {c.name} ({c.savePath})
                    </option>
                  ))}
                </select>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <input
                  type="checkbox"
                  id="pausedCheck"
                  checked={isPausedInput}
                  onChange={(e) => setIsPausedInput(e.target.checked)}
                />
                <label htmlFor="pausedCheck" style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
                  Start in Paused State
                </label>
              </div>

              <div className="modal-footer" style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.5rem', marginTop: '1rem' }}>
                <button type="button" className="btn" onClick={() => setShowAddModal(false)}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-success">
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
