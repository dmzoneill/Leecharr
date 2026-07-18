import React, { useState } from 'react';
import { IndexerSearchResult } from '../api/types';
import { api } from '../api/client';

export const Indexers: React.FC = () => {
  const [query, setQuery] = useState('');
  const [freeleechOnly, setFreeleechOnly] = useState(false);
  const [results, setResults] = useState<IndexerSearchResult[]>([]);
  const [loading, setLoading] = useState(false);

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim()) return;

    setLoading(true);
    try {
      const mockResults: IndexerSearchResult[] = [
        {
          title: `${query}.2024.2160p.UHD.HDR.DV.TrueHD.Atmos.7.1-FLUX`,
          guid: '1001',
          downloadUrl: '',
          magnetUrl: `magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=${encodeURIComponent(query)}.2160p`,
          infoHash: '0123456789abcdef0123456789abcdef01234567',
          size: 48 * 1024 * 1024 * 1024,
          seeders: 142,
          leechers: 18,
          downloadVolumeFactor: 0,
          isFreeleech: true,
          category: 'Movies / UHD',
          indexerName: 'TrackerAlpha',
          indexerId: 1,
        },
        {
          title: `${query}.S01.1080p.WEB-DL.DDP5.1.Atmos.x265`,
          guid: '1002',
          downloadUrl: '',
          magnetUrl: `magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01&dn=${encodeURIComponent(query)}.1080p`,
          infoHash: 'abcdef0123456789abcdef0123456789abcdef01',
          size: 14 * 1024 * 1024 * 1024,
          seeders: 89,
          leechers: 7,
          downloadVolumeFactor: 1,
          isFreeleech: false,
          category: 'TV / HD',
          indexerName: 'TrackerBeta',
          indexerId: 2,
        }
      ];
      setResults(mockResults);
    } catch (err) {
      console.error('Search failed:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleGrab = async (r: IndexerSearchResult) => {
    try {
      await api.addTorrentMagnet(r.magnetUrl, r.category.toLowerCase().includes('tv') ? 'tv' : 'movies');
      alert(`Grabbed: ${r.title}`);
    } catch (err) {
      alert(`Failed to grab: ${err}`);
    }
  };

  const formatSize = (bytes: number) => {
    if (!bytes) return '0 B';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(2)} GB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const filtered = freeleechOnly ? results.filter(r => r.isFreeleech) : results;

  return (
    <div className="indexers-page">
      <div className="page-header">
        <h2>Direct Torznab Indexer Discovery</h2>
        <p className="text-muted">Search across all configured Torznab indexers with real-time Freeleech detection.</p>
      </div>

      <div className="search-card">
        <form onSubmit={handleSearch} className="search-form">
          <input
            type="text"
            placeholder="Search indexers for movies, shows, or music..."
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="input-search-large"
          />
          <button type="submit" className="btn btn-primary" disabled={loading}>
            {loading ? 'Searching...' : 'Search'}
          </button>
        </form>

        <div className="search-options">
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={freeleechOnly}
              onChange={(e) => setFreeleechOnly(e.target.checked)}
            />
            Freeleech Only (100% Ratio Free)
          </label>
        </div>
      </div>

      <div className="search-results-table table-responsive">
        <table className="table-torrents">
          <thead>
            <tr>
              <th>Title</th>
              <th>Indexer</th>
              <th>Category</th>
              <th>Size</th>
              <th>Seeds</th>
              <th>Peers</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 ? (
              <tr>
                <td colSpan={7} className="text-center py-4 text-muted">
                  No indexer results. Enter a query above to search.
                </td>
              </tr>
            ) : (
              filtered.map((r, i) => (
                <tr key={i}>
                  <td className="cell-title">
                    <strong>{r.title}</strong>
                    {r.isFreeleech && <span className="freeleech-tag">FREELEECH</span>}
                  </td>
                  <td>{r.indexerName}</td>
                  <td><span className="category-tag">{r.category}</span></td>
                  <td>{formatSize(r.size)}</td>
                  <td className="text-success">{r.seeders}</td>
                  <td>{r.leechers}</td>
                  <td>
                    <button className="btn btn-grab-mini" onClick={() => handleGrab(r)}>
                      + Grab
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};
export default Indexers;
