import React, { useEffect, useState } from 'react';
import { api } from './api/client';
import { signalRManager } from './api/signalr';
import { Torrent, Category } from './api/types';
import './App.css';

export function App() {
  const [torrents, setTorrents] = useState<Torrent[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [showAddModal, setShowAddModal] = useState<boolean>(false);
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

    const unsubscribe = signalRManager.subscribe((msg) => {
      if (msg.name === 'torrent' || msg.name === 'speedpulse') {
        loadData();
      }
    });

    return () => unsubscribe();
  }, []);

  const totalDlSpeed = torrents.reduce((acc, t) => acc + (t.downloadSpeed || 0), 0);
  const totalUlSpeed = torrents.reduce((acc, t) => acc + (t.uploadSpeed || 0), 0);

  const filteredTorrents = selectedCategory === 'all'
    ? torrents
    : torrents.filter((t) => (t.category || '').toLowerCase() === selectedCategory.toLowerCase());

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

  const formatSize = (bytes: number) => {
    if (!bytes || bytes === 0) return '0 MB';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(2)} GB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <div className="app-container">
      {/* Header */}
      <header className="app-header">
        <div className="brand-section">
          <svg width="28" height="28" viewBox="0 0 32 32" fill="none">
            <rect width="32" height="32" rx="8" fill="#0284c7" />
            <path d="M16 6 L16 20 M10 14 L16 20 L22 14 M8 24 L24 24" stroke="#ffffff" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          <span className="brand-title">Leecharr</span>
        </div>

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

        <div className="header-actions">
          <button className="btn btn-primary" onClick={() => setShowAddModal(true)}>
            + Add Torrent
          </button>
        </div>
      </header>

      {/* Main Content */}
      <main className="main-content">
        <div className="toolbar">
          <div className="category-tabs">
            <button
              className={`category-tab ${selectedCategory === 'all' ? 'active' : ''}`}
              onClick={() => setSelectedCategory('all')}
            >
              All ({torrents.length})
            </button>
            {categories.map((c) => {
              const count = torrents.filter((t) => (t.category || '').toLowerCase() === c.name.toLowerCase()).length;
              return (
                <button
                  key={c.id}
                  className={`category-tab ${selectedCategory === c.name ? 'active' : ''}`}
                  onClick={() => setSelectedCategory(c.name)}
                >
                  {c.name} ({count})
                </button>
              );
            })}
          </div>
        </div>

        {/* Media Poster Grid */}
        <div className="poster-grid">
          {filteredTorrents.map((t) => (
            <div key={t.id} className="torrent-card">
              <div className="card-poster">
                {t.posterUrl ? (
                  <img src={t.posterUrl} alt={t.name} />
                ) : (
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%', color: '#64748b' }}>
                    No Poster
                  </div>
                )}
                <div className="poster-overlay">
                  {t.resolution && <span className="badge badge-4k">{t.resolution}</span>}
                  {t.hdrFormat && <span className="badge badge-hdr">{t.hdrFormat}</span>}
                  {t.audioCodec && <span className="badge badge-audio">{t.audioCodec}</span>}
                </div>
              </div>

              <div className="card-body">
                <div className="card-title" title={t.name}>
                  {t.mediaTitle || t.name}
                </div>
                <div className="card-subtitle">
                  <span>{formatSize(t.totalSize)}</span>
                  <span>{(t.progress * 100).toFixed(1)}%</span>
                </div>

                <div className="progress-bar-container">
                  <div
                    className={`progress-bar-fill ${t.status === 'seeding' ? 'seeding' : ''}`}
                    style={{ width: `${Math.min(100, Math.max(0, t.progress * 100))}%` }}
                  />
                </div>

                <div className="card-subtitle" style={{ marginTop: '4px' }}>
                  <span>{t.status}</span>
                  <span>Ratio: {t.ratio.toFixed(2)}</span>
                </div>

                <div className="card-actions">
                  {t.status === 'paused' ? (
                    <button onClick={() => api.resumeTorrent(t.id).then(loadData)}>Resume</button>
                  ) : (
                    <button onClick={() => api.pauseTorrent(t.id).then(loadData)}>Pause</button>
                  )}
                  <button onClick={() => api.recheckTorrent(t.id).then(loadData)}>Recheck</button>
                  <button onClick={() => api.deleteTorrent(t.id, false).then(loadData)} style={{ color: '#ef4444' }}>
                    Delete
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>
      </main>

      {/* Add Modal */}
      {showAddModal && (
        <div className="modal-backdrop" onClick={() => setShowAddModal(false)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h2>Add Torrent</h2>
            <form onSubmit={handleAddMagnet} style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
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
                      {c.name}
                    </option>
                  ))}
                </select>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <input
                  type="checkbox"
                  id="pausedCheck"
                  checked={isPausedInput}
                  onChange={(e) => setIsPausedInput(e.target.checked)}
                />
                <label htmlFor="pausedCheck">Start Paused</label>
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px' }}>
                <button type="button" className="btn btn-secondary" onClick={() => setShowAddModal(false)}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary">
                  Add Download
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
export default App;
