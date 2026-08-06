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
  onNavigateTab?: (nav: string, subNav?: string) => void;
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
  onNavigateTab,
}) => {
  const [viewMode, setViewMode] = useState<"grid" | "table">("table");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [selectedTorrent, setSelectedTorrent] = useState<Torrent | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
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

  const handleToggleSelect = (id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleSelectAll = (ids: number[]) => {
    setSelectedIds(new Set(ids));
  };

  const handleBulkStart = () => {
    selectedIds.forEach((id) => onResume(id));
    setSelectedIds(new Set());
  };

  const handleBulkStop = () => {
    selectedIds.forEach((id) => onPause(id));
    setSelectedIds(new Set());
  };

  const handleBulkDelete = () => {
    if (confirm(`Delete ${selectedIds.size} selected torrent(s)?`)) {
      selectedIds.forEach((id) => onDelete(id));
      setSelectedIds(new Set());
    }
  };

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "0.75rem",
        height: "100%",
        minHeight: 0,
      }}
    >
      {/* Page Header with Action Bar */}
      <div className="page-header" style={{ marginBottom: "0.25rem" }}>
        <div className="page-header-group">
          <h1 className="page-heading">Torrents ({torrents.length})</h1>
          <button
            type="button"
            className="btn btn-success"
            onClick={onOpenAddModal}
          >
            <PlusIcon size={13} /> Add Torrent
          </button>
          <button
            type="button"
            className="btn btn-outline"
            onClick={onOpenSearchModal}
          >
            🔍 Search Indexers
          </button>
        </div>

        <div className="page-header-actions">
          {selectedIds.size > 0 ? (
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
            >
              <span
                style={{
                  fontSize: "0.8rem",
                  color: "var(--accent, #ffd166)",
                  fontWeight: 600,
                }}
              >
                {selectedIds.size} Selected
              </span>
              <button
                type="button"
                className="btn btn-small btn-success"
                onClick={handleBulkStart}
              >
                ▶ Start
              </button>
              <button
                type="button"
                className="btn btn-small btn-warning"
                onClick={handleBulkStop}
              >
                ⏸ Pause
              </button>
              <button
                type="button"
                className="btn btn-small btn-danger"
                onClick={handleBulkDelete}
              >
                × Delete
              </button>
            </div>
          ) : (
            <>
              <button
                type="button"
                className="btn btn-success"
                onClick={handleStartAll}
                title="Resume all downloads"
              >
                <PlayIcon size={13} /> Start All
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={handleStopAll}
                title="Pause all downloads"
              >
                <StopIcon size={13} /> Stop All
              </button>
            </>
          )}

          <div
            className="view-mode-toggle"
            style={{
              display: "flex",
              gap: "2px",
              background: "var(--bg-secondary, #171b35)",
              padding: "2px",
              borderRadius: "4px",
              border: "1px solid var(--border-light, #1c203b)",
            }}
          >
            <button
              type="button"
              className={`btn btn-small ${viewMode === "table" ? "btn-primary" : ""}`}
              onClick={() => setViewMode("table")}
              title="Table View"
              style={{ padding: "4px 8px" }}
            >
              <TableIcon size={14} />
            </button>
            <button
              type="button"
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
              background: "var(--bg-secondary, #171b35)",
              border: "1px solid var(--border-light, #1c203b)",
              borderRadius: "4px",
              padding: "2px",
            }}
          >
            {["all", "downloading", "seeding", "paused"].map((s) => (
              <button
                key={s}
                type="button"
                className={`btn btn-small ${statusFilter === s ? "btn-primary" : ""}`}
                style={{
                  background:
                    statusFilter === s
                      ? "var(--accent, #ffd166)"
                      : "transparent",
                  color:
                    statusFilter === s
                      ? "#10111a"
                      : "var(--text-secondary, #c7c5d3)",
                  border: "none",
                  textTransform: "capitalize",
                  fontWeight: statusFilter === s ? 700 : 500,
                  fontSize: "0.75rem",
                  padding: "0.25rem 0.6rem",
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
              type="button"
              className={`badge ${selectedCategory === "all" ? "badge-accent" : ""}`}
              style={{
                cursor: "pointer",
                padding: "6px 12px",
                border: "1px solid var(--border-light, #1c203b)",
                backgroundColor:
                  selectedCategory === "all"
                    ? "var(--accent-bg, rgba(255, 209, 102, 0.15))"
                    : "var(--bg-secondary, #171b35)",
                color:
                  selectedCategory === "all"
                    ? "var(--accent, #ffd166)"
                    : "var(--text-secondary, #c7c5d3)",
                fontWeight: selectedCategory === "all" ? 700 : 500,
              }}
              onClick={() => onSelectCategory("all")}
            >
              ALL CATEGORIES
            </button>
            {categories.map((c) => (
              <button
                key={c.id}
                type="button"
                className={`badge ${selectedCategory === c.name ? "badge-accent" : ""}`}
                style={{
                  cursor: "pointer",
                  padding: "6px 12px",
                  border: "1px solid var(--border-light, #1c203b)",
                  backgroundColor:
                    selectedCategory === c.name
                      ? "var(--accent-bg, rgba(255, 209, 102, 0.15))"
                      : "var(--bg-secondary, #171b35)",
                  color:
                    selectedCategory === c.name
                      ? "var(--accent, #ffd166)"
                      : "var(--text-secondary, #c7c5d3)",
                  fontWeight: selectedCategory === c.name ? 700 : 500,
                }}
                onClick={() => onSelectCategory(c.name)}
              >
                {c.name.toUpperCase()}
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
      <div
        style={{
          flex: "1 1 auto",
          minHeight: 0,
          overflow: "hidden",
          display: "flex",
          flexDirection: "column",
        }}
      >
        {viewMode === "grid" ? (
          <div style={{ flex: "1 1 auto", overflowY: "auto" }}>
            <TorrentGrid
              torrents={filteredTorrents}
              selectedId={selectedTorrent?.id ?? null}
              onSelect={setSelectedTorrent}
              onPause={onPause}
              onResume={onResume}
              onDelete={onDelete}
            />
          </div>
        ) : (
          <TorrentTable
            torrents={filteredTorrents}
            selectedId={selectedTorrent?.id ?? null}
            onSelect={setSelectedTorrent}
            onPause={onPause}
            onResume={onResume}
            onDelete={onDelete}
            selectedIds={selectedIds}
            onToggleSelect={handleToggleSelect}
            onSelectAll={handleSelectAll}
            onSearchIndexers={(q) => onOpenSearchModal()}
            onNavigateTab={onNavigateTab}
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
