import React, { useState } from 'react';
import { Torrent, Category } from '../api/types';
import { TorrentGrid } from '../components/TorrentGrid';
import { TorrentTable } from '../components/TorrentTable';
import { TorrentDetailPanel } from '../components/TorrentDetailPanel';

interface TorrentIndexProps {
  torrents: Torrent[];
  categories: Category[];
  selectedCategory: string;
  onSelectCategory: (cat: string) => void;
  onPause: (id: number) => void;
  onResume: (id: number) => void;
  onDelete: (id: number) => void;
  onOpenAddModal: () => void;
  onOpenSearchModal: () => void;
}

export const TorrentIndex: React.FC<TorrentIndexProps> = ({
  torrents,
  categories,
  selectedCategory,
  onSelectCategory,
  onPause,
  onResume,
  onDelete,
  onOpenAddModal,
  onOpenSearchModal
}) => {
  const [viewMode, setViewMode] = useState<'grid' | 'table'>('grid');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [selectedTorrent, setSelectedTorrent] = useState<Torrent | null>(null);
  const [searchQuery, setSearchQuery] = useState<string>('');

  // Filter by category, status, and search query
  const filteredTorrents = torrents.filter((t) => {
    const matchesCategory =
      selectedCategory === 'all' ||
      (t.category || '').toLowerCase() === selectedCategory.toLowerCase();

    const matchesStatus =
      statusFilter === 'all' ||
      t.status.toLowerCase() === statusFilter.toLowerCase();

    const matchesSearch =
      !searchQuery.trim() ||
      (t.name || '').toLowerCase().includes(searchQuery.toLowerCase()) ||
      (t.mediaTitle || '').toLowerCase().includes(searchQuery.toLowerCase());

    return matchesCategory && matchesStatus && matchesSearch;
  });

  return (
    <div className="torrent-index-page">
      {/* Top Action Toolbar */}
      <div className="index-toolbar">
        <div className="toolbar-left">
          <div className="filter-pill-group">
            <button
              className={`filter-pill ${statusFilter === 'all' ? 'active' : ''}`}
              onClick={() => setStatusFilter('all')}
            >
              All ({torrents.length})
            </button>
            <button
              className={`filter-pill ${statusFilter === 'downloading' ? 'active' : ''}`}
              onClick={() => setStatusFilter('downloading')}
            >
              Downloading ({torrents.filter(t => t.status === 'downloading').length})
            </button>
            <button
              className={`filter-pill ${statusFilter === 'seeding' ? 'active' : ''}`}
              onClick={() => setStatusFilter('seeding')}
            >
              Seeding ({torrents.filter(t => t.status === 'seeding').length})
            </button>
            <button
              className={`filter-pill ${statusFilter === 'paused' ? 'active' : ''}`}
              onClick={() => setStatusFilter('paused')}
            >
              Paused ({torrents.filter(t => t.status === 'paused').length})
            </button>
          </div>

          <div className="search-box">
            <input
              type="text"
              placeholder="Filter downloads..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="input-search"
            />
          </div>
        </div>

        <div className="toolbar-right">
          <div className="view-toggle">
            <button
              className={`toggle-btn ${viewMode === 'grid' ? 'active' : ''}`}
              onClick={() => setViewMode('grid')}
              title="Poster Grid View"
            >
              Grid
            </button>
            <button
              className={`toggle-btn ${viewMode === 'table' ? 'active' : ''}`}
              onClick={() => setViewMode('table')}
              title="High-Density Table View"
            >
              Table
            </button>
          </div>

          <button className="btn btn-secondary" onClick={onOpenSearchModal}>
            🔍 Search Indexers
          </button>
          <button className="btn btn-primary" onClick={onOpenAddModal}>
            + Add Torrent
          </button>
        </div>
      </div>

      {/* Category Tabs */}
      <div className="category-bar">
        <button
          className={`category-chip ${selectedCategory === 'all' ? 'active' : ''}`}
          onClick={() => onSelectCategory('all')}
        >
          All Categories
        </button>
        {categories.map((c) => (
          <button
            key={c.id}
            className={`category-chip ${selectedCategory === c.name ? 'active' : ''}`}
            onClick={() => onSelectCategory(c.name)}
          >
            {c.name}
          </button>
        ))}
      </div>

      {/* View Container */}
      <div className="torrent-view-container">
        {viewMode === 'grid' ? (
          <TorrentGrid
            torrents={filteredTorrents}
            selectedId={selectedTorrent?.id ?? null}
            onSelect={setSelectedTorrent}
            onPause={onPause}
            onResume={onResume}
            onDelete={onDelete}
          />
        ) : (
          <TorrentTable
            torrents={filteredTorrents}
            selectedId={selectedTorrent?.id ?? null}
            onSelect={setSelectedTorrent}
            onPause={onPause}
            onResume={onResume}
            onDelete={onDelete}
          />
        )}
      </div>

      {/* Slide-Up Detail Panel */}
      {selectedTorrent && (
        <TorrentDetailPanel
          torrent={selectedTorrent}
          onClose={() => setSelectedTorrent(null)}
        />
      )}
    </div>
  );
};
export default TorrentIndex;
