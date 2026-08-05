import React, { useState } from "react";
import { Torrent, Category } from "../api/types";
import { TorrentGrid } from "../components/TorrentGrid";
import { TorrentTable } from "../components/TorrentTable";
import { TorrentDetailPanel } from "../components/TorrentDetailPanel";
import {
  PlusIcon,
  PlayIcon,
  StopIcon,
  TableIcon,
  GridIcon,
} from "../components/icons/UIIcons";

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
  onOpenSearchModal,
}) => {
  const [viewMode, setViewMode] = useState<"grid" | "table">("grid");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [selectedTorrent, setSelectedTorrent] = useState<Torrent | null>(null);
  const [searchQuery, setSearchQuery] = useState<string>("");

  // Filter by category, status, and search query
  const filteredTorrents = torrents.filter((t) => {
    const matchesCategory =
      selectedCategory === "all" ||
      (t.category || "").toLowerCase() === selectedCategory.toLowerCase();

    const matchesStatus =
      statusFilter === "all" ||
      t.status.toLowerCase() === statusFilter.toLowerCase();

    const matchesSearch =
      !searchQuery.trim() ||
      (t.name || "").toLowerCase().includes(searchQuery.toLowerCase()) ||
      (t.mediaTitle || "").toLowerCase().includes(searchQuery.toLowerCase());

    return matchesCategory && matchesStatus && matchesSearch;
  });

  const handleStartAll = () => {
    torrents
      .filter((t) => t.status === "paused")
      .forEach((t) => onResume(t.id));
  };

  const handleStopAll = () => {
    torrents.filter((t) => t.status !== "paused").forEach((t) => onPause(t.id));
  };

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "1.25rem",
        height: "100%",
      }}
    >
      {/* Page Header with Action Bar */}
      <div className="page-header">
        <div className="page-header-group">
          <h1 className="page-heading">Torrents ({torrents.length})</h1>
          <button className="btn btn-success" onClick={onOpenAddModal}>
            <PlusIcon size={13} /> Add Torrent
          </button>
          <button className="btn" onClick={onOpenSearchModal}>
            🔍 Search Indexers
          </button>
        </div>

        <div className="page-header-actions">
          <button
            className="btn btn-success"
            onClick={handleStartAll}
            title="Resume all downloads"
          >
            <PlayIcon size={13} /> Start All
          </button>
          <button
            className="btn"
            onClick={handleStopAll}
            title="Pause all downloads"
          >
            <StopIcon size={13} /> Stop All
          </button>
          <div
            className="view-mode-toggle"
            style={{
              display: "flex",
              gap: "2px",
              background: "var(--bg-secondary)",
              padding: "2px",
              borderRadius: "4px",
              border: "1px solid var(--border)",
            }}
          >
            <button
              className={`btn btn-small ${viewMode === "table" ? "btn-primary" : ""}`}
              onClick={() => setViewMode("table")}
              title="Table View"
              style={{ padding: "4px 8px" }}
            >
              <TableIcon size={14} />
            </button>
            <button
              className={`btn btn-small ${viewMode === "grid" ? "btn-primary" : ""}`}
              onClick={() => setViewMode("grid")}
              title="Poster Grid View"
              style={{ padding: "4px 8px" }}
            >
              <GridIcon size={14} />
            </button>
          </div>
        </div>
      </div>

      {/* Filter Toolbar (Status Pills, Category Chips & Search) */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          flexWrap: "wrap",
          gap: "12px",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "8px",
            flexWrap: "wrap",
          }}
        >
          {/* Status Pills */}
          <div
            style={{
              display: "flex",
              background: "var(--bg-secondary)",
              border: "1px solid var(--border)",
              borderRadius: "4px",
              padding: "2px",
            }}
          >
            {["all", "downloading", "seeding", "paused"].map((s) => (
              <button
                key={s}
                className={`btn btn-small ${statusFilter === s ? "btn-primary" : ""}`}
                style={{
                  background:
                    statusFilter === s ? "var(--accent)" : "transparent",
                  color:
                    statusFilter === s ? "#10111a" : "var(--text-secondary)",
                  border: "none",
                  textTransform: "capitalize",
                  fontWeight: statusFilter === s ? 700 : 500,
                }}
                onClick={() => setStatusFilter(s)}
              >
                {s} (
                {s === "all"
                  ? torrents.length
                  : torrents.filter((t) => t.status === s).length}
                )
              </button>
            ))}
          </div>

          {/* Category Chips */}
          <div style={{ display: "flex", gap: "6px", overflowX: "auto" }}>
            <button
              className={`badge ${selectedCategory === "all" ? "badge-accent" : ""}`}
              style={{
                cursor: "pointer",
                padding: "6px 12px",
                border: "1px solid var(--border)",
                backgroundColor:
                  selectedCategory === "all"
                    ? "var(--accent-bg)"
                    : "var(--bg-secondary)",
                color:
                  selectedCategory === "all"
                    ? "var(--accent)"
                    : "var(--text-secondary)",
              }}
              onClick={() => onSelectCategory("all")}
            >
              All Categories
            </button>
            {categories.map((c) => (
              <button
                key={c.id}
                className={`badge ${selectedCategory === c.name ? "badge-accent" : ""}`}
                style={{
                  cursor: "pointer",
                  padding: "6px 12px",
                  border: "1px solid var(--border)",
                  backgroundColor:
                    selectedCategory === c.name
                      ? "var(--accent-bg)"
                      : "var(--bg-secondary)",
                  color:
                    selectedCategory === c.name
                      ? "var(--accent)"
                      : "var(--text-secondary)",
                }}
                onClick={() => onSelectCategory(c.name)}
              >
                {c.name}
              </button>
            ))}
          </div>
        </div>

        {/* Search Filter Box */}
        <div className="search-input-wrapper">
          <input
            type="text"
            placeholder="Filter torrents..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="topbar-search-input"
            style={{ width: "240px" }}
          />
        </div>
      </div>

      {/* Main Grid or Table View */}
      <div style={{ flex: 1, minHeight: 0, overflowY: "auto" }}>
        {viewMode === "grid" ? (
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

      {/* Slide-Up Detail Drawer */}
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
