import React, { useState, useEffect } from "react";
import { useArrConnections, useDownloadHistory } from "../api/hooks";
import { getMediaDeepLink } from "../utils/arrLinks";
import type { Torrent } from "../api/types";

export interface TorrentContextMenuProps {
  x: number;
  y: number;
  torrent: Torrent | null;
  visibleColumns: Set<string>;
  allColumns: ReadonlyArray<{ key: string; label: string }>;
  onClose: () => void;
  onToggleColumn: (key: string) => void;
  onStart: (id: number) => void;
  onStop: (id: number) => void;
  onUpdate: (torrent: Torrent) => void;
  onAnnounce: (id: number) => void;
  onRecheck: (id: number) => void;
  onDelete: (payload: { id: number; deleteFiles?: boolean }) => void;
  onMoveQueue: (payload: {
    id: number;
    position: "top" | "up" | "down" | "bottom";
  }) => void;
  onSearchIndexers?: (query: string) => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
}

function buildMagnetLink(t: Torrent): string {
  let magnet = `magnet:?xt=urn:btih:${t.infoHash}&dn=${encodeURIComponent(t.name)}`;
  if (t.trackerUrl) magnet += `&tr=${encodeURIComponent(t.trackerUrl)}`;
  return magnet;
}

export function TorrentContextMenu({
  x,
  y,
  torrent,
  visibleColumns,
  allColumns,
  onClose,
  onToggleColumn,
  onStart,
  onStop,
  onUpdate,
  onAnnounce,
  onRecheck,
  onDelete,
  onMoveQueue,
  onSearchIndexers,
  onNavigateTab,
}: TorrentContextMenuProps) {
  const [openSubmenu, setOpenSubmenu] = useState<string | null>(null);

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  useEffect(() => {
    const handleClick = () => onClose();
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("click", handleClick);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("click", handleClick);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [onClose]);

  function handleCopy(text: string) {
    navigator.clipboard
      .writeText(text)
      .catch((err) => console.warn("Clipboard write failed:", err));
    onClose();
  }

  const ct = torrent;

  const historyMatch = ct
    ? history?.find(
        (h) =>
          (ct.infoHash &&
            h.infoHash?.toLowerCase() === ct.infoHash.toLowerCase()) ||
          h.title?.toLowerCase() === ct.name?.toLowerCase(),
      )
    : null;

  const arrLink = historyMatch
    ? getMediaDeepLink(historyMatch, arrConnections)
    : null;

  const isPaused = ct?.status?.toLowerCase() === "paused";

  return (
    <div
      className="context-menu"
      style={{ left: x, top: y }}
      onClick={(e) => e.stopPropagation()}
    >
      {ct ? (
        <>
          {/* Arr Direct Jump Link */}
          {arrLink && (
            <button
              type="button"
              className="context-menu-item"
              style={{ fontWeight: 600, color: "var(--accent, #ffd166)" }}
              onClick={() => {
                window.open(arrLink.url, "_blank", "noopener,noreferrer");
                onClose();
              }}
            >
              🔗 {arrLink.label} ↗
            </button>
          )}

          {/* Pause / Resume */}
          {isPaused ? (
            <button
              type="button"
              className="context-menu-item"
              onClick={() => {
                onStart(ct.id);
                onClose();
              }}
            >
              ▶ Resume Download
            </button>
          ) : (
            <button
              type="button"
              className="context-menu-item"
              onClick={() => {
                onStop(ct.id);
                onClose();
              }}
            >
              ⏸ Pause Download
            </button>
          )}

          <button
            type="button"
            className="context-menu-item"
            onClick={() => {
              onAnnounce(ct.id);
              onClose();
            }}
          >
            ⚡ Update Tracker (Announce)
          </button>
          <button
            type="button"
            className="context-menu-item"
            onClick={() => {
              onRecheck(ct.id);
              onClose();
            }}
          >
            🛡 Force Recheck Integrity
          </button>

          <div className="context-menu-separator" />

          {/* Usability & Navigation Actions */}
          <button
            type="button"
            className="context-menu-item"
            onClick={() => {
              if (onSearchIndexers) {
                onSearchIndexers(ct.name);
              }
              onClose();
            }}
          >
            🔍 Search on Indexers
          </button>

          <button
            type="button"
            className="context-menu-item"
            onClick={() => {
              if (onNavigateTab) onNavigateTab("peermap");
              onClose();
            }}
          >
            🗺️ Track in Peer Map
          </button>

          <div className="context-menu-separator" />

          {/* Copy submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("copy")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Copy ▶
            {openSubmenu === "copy" && (
              <div className="context-menu context-menu-submenu">
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.name)}
                >
                  Name
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.infoHash)}
                >
                  Info Hash
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => handleCopy(buildMagnetLink(ct))}
                >
                  Magnet Link
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.trackerUrl ?? "")}
                >
                  Tracker URL
                </button>
              </div>
            )}
          </div>

          {/* Priority submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("priority")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Priority ▶
            {openSubmenu === "priority" && (
              <div className="context-menu context-menu-submenu">
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, priority: 2 });
                    onClose();
                  }}
                >
                  {ct.priority === 2 ? "✓ " : ""}High Priority
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, priority: 1 });
                    onClose();
                  }}
                >
                  {ct.priority === 1 ? "✓ " : ""}Normal Priority
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, priority: 0 });
                    onClose();
                  }}
                >
                  {ct.priority === 0 ? "✓ " : ""}Low Priority
                </button>
              </div>
            )}
          </div>

          {/* Speed Limit submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("speed")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Speed Limit ▶
            {openSubmenu === "speed" && (
              <div className="context-menu context-menu-submenu">
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    const limit = window.prompt(
                      "Upload limit in KB/s (0 = unlimited):",
                      String(ct.uploadLimit || 0),
                    );
                    if (limit !== null) {
                      const val = parseInt(limit, 10);
                      if (!isNaN(val) && val >= 0)
                        onUpdate({ ...ct, uploadLimit: val });
                    }
                    onClose();
                  }}
                >
                  Set Upload Limit...
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    const limit = window.prompt(
                      "Download limit in KB/s (0 = unlimited):",
                      String(ct.downloadLimit || 0),
                    );
                    if (limit !== null) {
                      const val = parseInt(limit, 10);
                      if (!isNaN(val) && val >= 0)
                        onUpdate({ ...ct, downloadLimit: val });
                    }
                    onClose();
                  }}
                >
                  Set Download Limit...
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, uploadLimit: 0, downloadLimit: 0 });
                    onClose();
                  }}
                >
                  Reset to Global Limits
                </button>
              </div>
            )}
          </div>

          {/* Queue submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("queue")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Queue ▶
            {openSubmenu === "queue" && (
              <div className="context-menu context-menu-submenu">
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "top" });
                    onClose();
                  }}
                >
                  Top
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "up" });
                    onClose();
                  }}
                >
                  Up
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "down" });
                    onClose();
                  }}
                >
                  Down
                </button>
                <button
                  type="button"
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "bottom" });
                    onClose();
                  }}
                >
                  Bottom
                </button>
              </div>
            )}
          </div>

          <div className="context-menu-separator" />

          {/* Category / Label / Sequential Download */}
          <button
            type="button"
            className="context-menu-item"
            onClick={() => {
              const l = window.prompt(
                "Set category / label:",
                ct.category ?? ct.label ?? "",
              );
              if (l !== null)
                onUpdate({
                  ...ct,
                  category: l || undefined,
                  label: l || undefined,
                });
              onClose();
            }}
          >
            Set Category...{ct.category ? ` (${ct.category})` : ""}
          </button>
          <button
            type="button"
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, sequentialDownload: !ct.sequentialDownload });
              onClose();
            }}
          >
            {ct.sequentialDownload ? "Disable" : "Enable"} Sequential Download
            (Head/Tail Priority)
          </button>

          <div className="context-menu-separator" />

          {/* Remove submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("remove")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Remove ▶
            {openSubmenu === "remove" && (
              <div className="context-menu context-menu-submenu">
                <button
                  type="button"
                  className="context-menu-item context-menu-item-danger"
                  onClick={() => {
                    if (confirm(`Remove "${ct.name}"?`))
                      onDelete({ id: ct.id });
                    onClose();
                  }}
                >
                  Remove Torrent
                </button>
                <button
                  type="button"
                  className="context-menu-item context-menu-item-danger"
                  onClick={() => {
                    if (
                      confirm(
                        `Remove "${ct.name}" and delete all downloaded data from disk?`,
                      )
                    )
                      onDelete({ id: ct.id, deleteFiles: true });
                    onClose();
                  }}
                >
                  Remove Torrent and Delete Files
                </button>
              </div>
            )}
          </div>

          <div className="context-menu-separator" />
        </>
      ) : null}

      {/* Columns section - always shown */}
      <div
        className="context-menu-item context-menu-submenu-trigger"
        onMouseEnter={() => setOpenSubmenu("columns")}
        onMouseLeave={() => setOpenSubmenu(null)}
      >
        Columns ▶
        {openSubmenu === "columns" && (
          <div className="context-menu context-menu-submenu context-menu-columns">
            {allColumns.map((col) => (
              <label key={col.key} className="column-menu-item">
                <input
                  type="checkbox"
                  checked={visibleColumns.has(col.key)}
                  onChange={() => onToggleColumn(col.key)}
                />
                {col.label}
              </label>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default TorrentContextMenu;
