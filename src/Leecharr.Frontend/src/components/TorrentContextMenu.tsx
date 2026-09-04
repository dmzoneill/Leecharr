import React, { useState, useEffect } from "react";
import { useArrConnections, useDownloadHistory } from "../api/hooks";
import { getMediaDeepLink } from "../utils/arrLinks";
import { useConfirm } from "../context/ConfirmContext";
import { PromptModal } from "./PromptModal";
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
  onDelete: (payload: { id: number; deleteFiles: boolean }) => void;
  onMoveQueue: (payload: { id: number; position: "top" | "up" | "down" | "bottom" }) => void;
  onSearchIndexers?: (query: string) => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
}

function buildMagnetLink(t: Torrent): string {
  let magnet = `magnet:?xt=urn:btih:${t.infoHash}&dn=${encodeURIComponent(t.name)}`;
  if (t.trackerUrl) magnet += `&tr=${encodeURIComponent(t.trackerUrl)}`;
  return magnet;
}

const getAdjustedPosition = (x: number, y: number) => {
  const menuWidth = 200;
  const menuHeight = 450;
  const margin = 10;
  return {
    left: Math.max(margin, Math.min(x, window.innerWidth - menuWidth - margin)),
    top: Math.max(margin, Math.min(y, window.innerHeight - menuHeight - margin)),
    flipSubmenu: x + menuWidth + 200 > window.innerWidth,
  };
};

interface PromptConfig {
  title: string;
  message?: string;
  defaultValue?: string;
  placeholder?: string;
  inputType?: "text" | "number";
  min?: number;
  confirmText?: string;
  validate?: (value: string) => string | null;
  onConfirm: (value: string) => void;
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
  const [promptConfig, setPromptConfig] = useState<PromptConfig | null>(null);

  const confirm = useConfirm();
  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const { left, top, flipSubmenu } = getAdjustedPosition(x, y);

  useEffect(() => {
    if (promptConfig !== null) return;
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
  }, [onClose, promptConfig]);

  const handlePromptCancel = () => {
    setPromptConfig(null);
    onClose();
  };

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
          (ct.infoHash && h.infoHash?.toLowerCase() === ct.infoHash.toLowerCase()) ||
          h.title?.toLowerCase() === ct.name?.toLowerCase()
      )
    : null;

  const arrLink = historyMatch ? getMediaDeepLink(historyMatch, arrConnections) : null;

  const isPaused = ct?.status?.toLowerCase() === "paused";

  return (
    <>
      <div
        className="context-menu"
        style={{
          left,
          top,
          display: promptConfig ? "none" : undefined,
        }}
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
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
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
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
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
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      setPromptConfig({
                        title: "Set Upload Limit",
                        message: "Upload limit in KB/s (0 = unlimited):",
                        defaultValue: String(ct.uploadLimit || 0),
                        inputType: "number",
                        min: 0,
                        confirmText: "Save",
                        validate: (val) => {
                          const num = parseInt(val, 10);
                          if (isNaN(num) || num < 0) {
                            return "Please enter a valid non-negative number (0 = unlimited)";
                          }
                          return null;
                        },
                        onConfirm: (limit) => {
                          const val = parseInt(limit, 10);
                          if (!isNaN(val) && val >= 0) {
                            onUpdate({ ...ct, uploadLimit: val });
                          }
                          onClose();
                        },
                      });
                    }}
                  >
                    Set Upload Limit...
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      setPromptConfig({
                        title: "Set Download Limit",
                        message: "Download limit in KB/s (0 = unlimited):",
                        defaultValue: String(ct.downloadLimit || 0),
                        inputType: "number",
                        min: 0,
                        confirmText: "Save",
                        validate: (val) => {
                          const num = parseInt(val, 10);
                          if (isNaN(num) || num < 0) {
                            return "Please enter a valid non-negative number (0 = unlimited)";
                          }
                          return null;
                        },
                        onConfirm: (limit) => {
                          const val = parseInt(limit, 10);
                          if (!isNaN(val) && val >= 0) {
                            onUpdate({ ...ct, downloadLimit: val });
                          }
                          onClose();
                        },
                      });
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
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
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
                setPromptConfig({
                  title: "Set Category / Label",
                  message: "Set category / label:",
                  defaultValue: ct.category ?? ct.label ?? "",
                  inputType: "text",
                  placeholder: "e.g. movies, tv, music",
                  confirmText: "Save",
                  onConfirm: (l) => {
                    const trimmed = l.trim();
                    onUpdate({
                      ...ct,
                      category: trimmed || null,
                      label: trimmed || null,
                    });
                    onClose();
                  },
                });
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
              {ct.sequentialDownload ? "Disable" : "Enable"} Sequential Download (Head/Tail
              Priority)
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
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item context-menu-item-danger"
                    onClick={async () => {
                      onClose();
                      const ok = await confirm({
                        title: "Remove Torrent",
                        message: `Remove "${ct.name}"?`,
                        danger: true,
                        confirmText: "Remove",
                      });
                      if (ok) onDelete({ id: ct.id, deleteFiles: false });
                    }}
                  >
                    Remove Torrent
                  </button>
                  <button
                    type="button"
                    className="context-menu-item context-menu-item-danger"
                    onClick={async () => {
                      onClose();
                      const ok = await confirm({
                        title: "Remove Torrent and Delete Files",
                        message: `Remove "${ct.name}" and delete all downloaded data from disk?`,
                        danger: true,
                        confirmText: "Delete Files",
                      });
                      if (ok) onDelete({ id: ct.id, deleteFiles: true });
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
            <div
              className={`context-menu context-menu-submenu context-menu-columns ${flipSubmenu ? "flip-left" : ""}`}
            >
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
      {promptConfig && (
        <PromptModal
          isOpen={true}
          title={promptConfig.title}
          message={promptConfig.message}
          defaultValue={promptConfig.defaultValue}
          placeholder={promptConfig.placeholder}
          inputType={promptConfig.inputType}
          min={promptConfig.min}
          confirmText={promptConfig.confirmText}
          validate={promptConfig.validate}
          onConfirm={promptConfig.onConfirm}
          onCancel={handlePromptCancel}
        />
      )}
    </>
  );
}

export default TorrentContextMenu;
