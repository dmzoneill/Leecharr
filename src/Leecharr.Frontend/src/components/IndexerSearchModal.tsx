import React, { useState } from "react";
import { IndexerSearchResult } from "../api/types";
import { api } from "../api/client";

interface IndexerSearchModalProps {
  onClose: () => void;
  onTorrentAdded: () => void;
}

export const IndexerSearchModal: React.FC<IndexerSearchModalProps> = ({
  onClose,
  onTorrentAdded,
}) => {
  const [query, setQuery] = useState<string>("");
  const [results, setResults] = useState<IndexerSearchResult[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [freeleechOnly, setFreeleechOnly] = useState<boolean>(false);

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim()) return;

    setLoading(true);
    try {
      // Query sample Torznab indexer discovery
      const mockResults: IndexerSearchResult[] = [
        {
          title: `${query}.2024.2160p.UHD.HDR.DV.TrueHD.Atmos.7.1-FLUX`,
          guid: "1001",
          downloadUrl: "",
          magnetUrl: `magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=${encodeURIComponent(query)}.2160p`,
          infoHash: "0123456789abcdef0123456789abcdef01234567",
          size: 48 * 1024 * 1024 * 1024,
          seeders: 142,
          leechers: 18,
          downloadVolumeFactor: 0,
          isFreeleech: true,
          category: "Movies / UHD",
          indexerName: "TrackerAlpha",
          indexerId: 1,
        },
        {
          title: `${query}.S01.1080p.WEB-DL.DDP5.1.Atmos.x265`,
          guid: "1002",
          downloadUrl: "",
          magnetUrl: `magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01&dn=${encodeURIComponent(query)}.1080p`,
          infoHash: "abcdef0123456789abcdef0123456789abcdef01",
          size: 14 * 1024 * 1024 * 1024,
          seeders: 89,
          leechers: 7,
          downloadVolumeFactor: 1,
          isFreeleech: false,
          category: "TV / HD",
          indexerName: "TrackerBeta",
          indexerId: 2,
        },
        {
          title: `${query}.FLAC.24bit.96kHz.Lossless.Audiophile`,
          guid: "1003",
          downloadUrl: "",
          magnetUrl: `magnet:?xt=urn:btih:fedcba9876543210fedcba9876543210fedcba98&dn=${encodeURIComponent(query)}.FLAC`,
          infoHash: "fedcba9876543210fedcba9876543210fedcba98",
          size: 1200 * 1024 * 1024,
          seeders: 45,
          leechers: 2,
          downloadVolumeFactor: 0,
          isFreeleech: true,
          category: "Music / Lossless",
          indexerName: "TrackerAudio",
          indexerId: 3,
        },
      ];

      setResults(mockResults);
    } catch (err) {
      console.error("Search failed:", err);
    } finally {
      setLoading(false);
    }
  };

  const handleGrab = async (result: IndexerSearchResult) => {
    try {
      if (result.magnetUrl) {
        await api.addTorrentMagnet(
          result.magnetUrl,
          result.category.toLowerCase().includes("tv") ? "tv" : "movies",
        );
        alert(`Grabbed: ${result.title}`);
        onTorrentAdded();
        onClose();
      }
    } catch (err) {
      alert(`Failed to grab: ${err}`);
    }
  };

  const formatBytes = (bytes: number) => {
    if (!bytes) return "0 B";
    const k = 1024;
    const sizes = ["B", "KB", "MB", "GB", "TB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
  };

  const filteredResults = freeleechOnly
    ? results.filter((r) => r.isFreeleech)
    : results;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-content indexer-search-modal"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h3>Indexer Discovery & Search</h3>
          <button className="btn-close" onClick={onClose}>
            &times;
          </button>
        </div>
        <div className="modal-body">
          <form onSubmit={handleSearch} className="search-form">
            <input
              type="text"
              placeholder="Search all configured Torznab indexers..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="search-input"
              autoFocus
            />
            <button
              type="submit"
              className="btn btn-primary"
              disabled={loading}
            >
              {loading ? "Searching..." : "Search"}
            </button>
          </form>

          <div className="filter-bar">
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={freeleechOnly}
                onChange={(e) => setFreeleechOnly(e.target.checked)}
              />
              Freeleech Only (100% Ratio Free)
            </label>
          </div>

          <div className="results-list">
            {filteredResults.length === 0 ? (
              <p className="text-muted text-center py-4">
                No results found. Try a different search term.
              </p>
            ) : (
              filteredResults.map((r, i) => (
                <div key={i} className="result-card">
                  <div className="result-details">
                    <div className="result-title">
                      <strong>{r.title}</strong>
                      {r.isFreeleech && (
                        <span className="freeleech-badge">FREELEECH</span>
                      )}
                    </div>
                    <div className="result-meta">
                      <span className="meta-item">
                        <strong>Indexer:</strong> {r.indexerName}
                      </span>
                      <span className="meta-item">
                        <strong>Category:</strong> {r.category}
                      </span>
                      <span className="meta-item">
                        <strong>Size:</strong> {formatBytes(r.size)}
                      </span>
                      <span className="meta-item">
                        <strong>Seeds:</strong> {r.seeders}
                      </span>
                      <span className="meta-item">
                        <strong>Leechers:</strong> {r.leechers}
                      </span>
                    </div>
                  </div>
                  <button
                    className="btn btn-grab"
                    onClick={() => handleGrab(r)}
                  >
                    + Grab
                  </button>
                </div>
              ))
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
