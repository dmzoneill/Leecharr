import React, { useState, useEffect } from "react";
import { useArrConnections, useDownloadHistory } from "../api/hooks";
import { getMediaDeepLink } from "../utils/arrLinks";
import { useConfirm } from "../context/ConfirmContext";
import { PromptModal } from "./PromptModal";
import { useTranslation } from "../i18n";
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

const getAdjustedPosition = (x: number, y: number) => {
  const menuWidth = 200;
  const menuHeight = 450;
  const margin = 10;
  return {
    left: Math.max(margin, Math.min(x, window.innerWidth - menuWidth - margin)),
    top: Math.max(
      margin,
      Math.min(y, window.innerHeight - menuHeight - margin),
    ),
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
  const { t } = useTranslation();
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
                ▶ {t("torrents.contextMenu.resumeDownload")}
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
                ⏸ {t("torrents.contextMenu.pauseDownload")}
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
              ⚡ {t("torrents.contextMenu.updateTracker")}
            </button>
            <button
              type="button"
              className="context-menu-item"
              onClick={() => {
                onRecheck(ct.id);
                onClose();
              }}
            >
              🛡 {t("torrents.contextMenu.forceRecheck")}
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
              🔍 {t("torrents.contextMenu.searchIndexers")}
            </button>

            <button
              type="button"
              className="context-menu-item"
              onClick={() => {
                if (onNavigateTab) onNavigateTab("peermap");
                onClose();
              }}
            >
              🗺️ {t("torrents.contextMenu.trackInPeerMap")}
            </button>

            <div className="context-menu-separator" />

            {/* Copy submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("copy")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              {t("torrents.contextMenu.copy")} ▶
              {openSubmenu === "copy" && (
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleCopy(ct.name)}
                  >
                    {t("torrents.contextMenu.copyName")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleCopy(ct.infoHash)}
                  >
                    {t("torrents.contextMenu.copyInfoHash")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleCopy(buildMagnetLink(ct))}
                  >
                    {t("torrents.contextMenu.copyMagnetLink")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleCopy(ct.trackerUrl ?? "")}
                  >
                    {t("torrents.contextMenu.copyTrackerUrl")}
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
              {t("torrents.contextMenu.priority")} ▶
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
                    {ct.priority === 2 ? "✓ " : ""}
                    {t("torrents.contextMenu.highPriority")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      onUpdate({ ...ct, priority: 1 });
                      onClose();
                    }}
                  >
                    {ct.priority === 1 ? "✓ " : ""}
                    {t("torrents.contextMenu.normalPriority")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      onUpdate({ ...ct, priority: 0 });
                      onClose();
                    }}
                  >
                    {ct.priority === 0 ? "✓ " : ""}
                    {t("torrents.contextMenu.lowPriority")}
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
              {t("torrents.contextMenu.speedLimit")} ▶
              {openSubmenu === "speed" && (
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      setPromptConfig({
                        title: t("torrents.contextMenu.setUploadLimit"),
                        message: `${t("torrents.table.uploadLimit")} (KB/s):`,
                        defaultValue: String(ct.uploadLimit || 0),
                        inputType: "number",
                        min: 0,
                        confirmText: t("common.save"),
                        validate: (val) => {
                          const num = parseInt(val, 10);
                          if (isNaN(num) || num < 0) {
                            return t(
                              "torrents.contextMenu.invalidLimitValidation",
                              "Please enter a valid non-negative number (0 = unlimited)",
                            );
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
                    {t("torrents.contextMenu.setUploadLimit")}...
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      setPromptConfig({
                        title: t("torrents.contextMenu.setDownloadLimit"),
                        message: `${t("torrents.table.downloadLimit")} (KB/s):`,
                        defaultValue: String(ct.downloadLimit || 0),
                        inputType: "number",
                        min: 0,
                        confirmText: t("common.save"),
                        validate: (val) => {
                          const num = parseInt(val, 10);
                          if (isNaN(num) || num < 0) {
                            return t(
                              "torrents.contextMenu.invalidLimitValidation",
                              "Please enter a valid non-negative number (0 = unlimited)",
                            );
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
                    {t("torrents.contextMenu.setDownloadLimit")}...
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      onUpdate({ ...ct, uploadLimit: 0, downloadLimit: 0 });
                      onClose();
                    }}
                  >
                    {t("torrents.contextMenu.resetToGlobalLimits")}
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
              {t("torrents.contextMenu.queue")} ▶
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
                    {t("torrents.contextMenu.top")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      onMoveQueue({ id: ct.id, position: "up" });
                      onClose();
                    }}
                  >
                    {t("torrents.contextMenu.up")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      onMoveQueue({ id: ct.id, position: "down" });
                      onClose();
                    }}
                  >
                    {t("torrents.contextMenu.down")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      onMoveQueue({ id: ct.id, position: "bottom" });
                      onClose();
                    }}
                  >
                    {t("torrents.contextMenu.bottom")}
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
                  title: t("torrents.contextMenu.setCategory"),
                  message: `${t("torrents.contextMenu.setCategory")}:`,
                  defaultValue: ct.category ?? ct.label ?? "",
                  inputType: "text",
                  placeholder: "e.g. movies, tv, music",
                  confirmText: t("common.save"),
                  onConfirm: (l) => {
                    const trimmed = l.trim();
                    onUpdate({
                      ...ct,
                      category: trimmed,
                      label: trimmed,
                    });
                    onClose();
                  },
                });
              }}
            >
              {t("torrents.contextMenu.setCategory")}...
              {ct.category ? ` (${ct.category})` : ""}
            </button>
            <button
              type="button"
              className="context-menu-item"
              onClick={() => {
                onUpdate({ ...ct, sequentialDownload: !ct.sequentialDownload });
                onClose();
              }}
            >
              {ct.sequentialDownload
                ? t("torrents.contextMenu.disableSequential")
                : t("torrents.contextMenu.enableSequential")}
            </button>

            <div className="context-menu-separator" />

            {/* Remove submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("remove")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              {t("torrents.contextMenu.remove")} ▶
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
                        title: t("torrents.contextMenu.removeTorrent"),
                        message: t(
                          "torrents.contextMenu.removeTorrentConfirm",
                          { name: ct.name },
                        ),
                        danger: true,
                        confirmText: t("common.delete"),
                      });
                      if (ok) onDelete({ id: ct.id, deleteFiles: false });
                    }}
                  >
                    {t("torrents.contextMenu.removeTorrent")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item context-menu-item-danger"
                    onClick={async () => {
                      onClose();
                      const ok = await confirm({
                        title: t(
                          "torrents.contextMenu.removeTorrentAndDeleteFiles",
                        ),
                        message: t(
                          "torrents.contextMenu.removeTorrentAndDeleteFilesConfirm",
                          { name: ct.name },
                        ),
                        danger: true,
                        confirmText: t("common.delete"),
                      });
                      if (ok) onDelete({ id: ct.id, deleteFiles: true });
                    }}
                  >
                    {t("torrents.contextMenu.removeTorrentAndDeleteFiles")}
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
          {t("torrents.contextMenu.columns")} ▶
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
