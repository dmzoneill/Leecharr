import React, { useState, useEffect, useMemo } from "react";
import { useArrConnections, useDownloadHistory } from "../api/hooks";
import { getMediaDeepLink } from "../utils/arrLinks";
import { PromptModal } from "./PromptModal";
import { ErrorBoundary } from "./ErrorBoundary";
import { useTranslation } from "../i18n";
import type { Torrent } from "../api/types";

export interface TorrentContextMenuProps {
  x: number;
  y: number;
  torrent: Torrent | null;
  selectedTorrents?: Torrent[];
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
  onBatchStart?: (ids: number[]) => void;
  onBatchStop?: (ids: number[]) => void;
  onBatchAnnounce?: (ids: number[]) => void;
  onBatchRecheck?: (ids: number[]) => void;
  onBatchDelete?: (payload: { ids: number[]; deleteFiles: boolean }) => void;
  onBatchUpdate?: (torrents: Torrent[]) => void;
  onBatchMoveQueue?: (payload: {
    ids: number[];
    position: "top" | "up" | "down" | "bottom";
  }) => void;
  onSearchIndexers?: (query: string) => void;
  onNavigateTab?: (nav: string, subNav?: string) => void;
}

function buildMagnetLink(t: Torrent): string {
  const hash = t.infoHash?.trim() || "";
  let xt = `urn:btih:${hash}`;
  if (hash.length === 64) {
    xt = `urn:btmh:1220${hash}`;
  } else if (hash.length === 68 && hash.toLowerCase().startsWith("1220")) {
    xt = `urn:btmh:${hash}`;
  }
  let magnet = `magnet:?xt=${xt}&dn=${encodeURIComponent(t.name)}`;
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
  selectedTorrents,
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
  onBatchStart,
  onBatchStop,
  onBatchAnnounce,
  onBatchRecheck,
  onBatchDelete,
  onBatchUpdate,
  onBatchMoveQueue,
  onSearchIndexers,
  onNavigateTab,
}: TorrentContextMenuProps) {
  const { t } = useTranslation();
  const [openSubmenu, setOpenSubmenu] = useState<string | null>(null);
  const [promptConfig, setPromptConfig] = useState<PromptConfig | null>(null);

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

  // Determine effective torrents: if multiple are selected, use them; otherwise fallback to single right-clicked torrent
  const effectiveTorrents = useMemo(() => {
    if (selectedTorrents && selectedTorrents.length > 1) {
      return selectedTorrents;
    }
    return torrent ? [torrent] : [];
  }, [selectedTorrents, torrent]);

  const isMulti = effectiveTorrents.length > 1;
  const count = effectiveTorrents.length;
  const countSuffix = isMulti ? ` (${count})` : "";
  const ct =
    torrent || (effectiveTorrents.length > 0 ? effectiveTorrents[0] : null);

  const historyMatch =
    !isMulti && ct
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

  const hasActive = effectiveTorrents.some((t) => {
    const s = (t.status || "").toLowerCase();
    return (
      s === "downloading" ||
      s === "seeding" ||
      s === "checking" ||
      s === "active"
    );
  });
  const hasInactive = effectiveTorrents.some((t) => {
    const s = (t.status || "").toLowerCase();
    return (
      s !== "downloading" &&
      s !== "seeding" &&
      s !== "checking" &&
      s !== "active"
    );
  });

  const handleStartAll = () => {
    if (isMulti && onBatchStart) {
      onBatchStart(effectiveTorrents.map((t) => t.id));
    } else {
      effectiveTorrents.forEach((t) => onStart(t.id));
    }
    onClose();
  };

  const handleStopAll = () => {
    if (isMulti && onBatchStop) {
      onBatchStop(effectiveTorrents.map((t) => t.id));
    } else {
      effectiveTorrents.forEach((t) => onStop(t.id));
    }
    onClose();
  };

  const handleAnnounceAll = () => {
    if (isMulti && onBatchAnnounce) {
      onBatchAnnounce(effectiveTorrents.map((t) => t.id));
    } else {
      effectiveTorrents.forEach((t) => onAnnounce(t.id));
    }
    onClose();
  };

  const handleRecheckAll = () => {
    if (isMulti && onBatchRecheck) {
      onBatchRecheck(effectiveTorrents.map((t) => t.id));
    } else {
      effectiveTorrents.forEach((t) => onRecheck(t.id));
    }
    onClose();
  };

  const handleDeleteAll = (deleteFiles: boolean) => {
    if (isMulti && onBatchDelete) {
      onBatchDelete({ ids: effectiveTorrents.map((t) => t.id), deleteFiles });
    } else {
      effectiveTorrents.forEach((t) => onDelete({ id: t.id, deleteFiles }));
    }
    onClose();
  };

  const handleMoveQueueAll = (position: "top" | "up" | "down" | "bottom") => {
    if (isMulti && onBatchMoveQueue) {
      onBatchMoveQueue({ ids: effectiveTorrents.map((t) => t.id), position });
    } else {
      effectiveTorrents.forEach((t) => onMoveQueue({ id: t.id, position }));
    }
    onClose();
  };

  const handleUpdateAll = (updater: (t: Torrent) => Torrent) => {
    const updated = effectiveTorrents.map(updater);
    if (isMulti && onBatchUpdate) {
      onBatchUpdate(updated);
    } else {
      updated.forEach((t) => onUpdate(t));
    }
    onClose();
  };

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
        {effectiveTorrents.length > 0 ? (
          <>
            {/* Multi-selection header indicator */}
            {isMulti && (
              <div
                style={{
                  padding: "6px 12px",
                  fontSize: "0.75rem",
                  fontWeight: 700,
                  letterSpacing: "0.03em",
                  color: "var(--accent, #ffd166)",
                  backgroundColor: "rgba(255, 209, 102, 0.08)",
                  borderBottom:
                    "1px solid var(--border, rgba(255, 255, 255, 0.1))",
                  borderRadius: "4px 4px 0 0",
                  display: "flex",
                  alignItems: "center",
                  gap: "6px",
                }}
              >
                <span>✓</span>
                <span>
                  {t("torrents.contextMenu.selectedItems", {
                    count,
                    defaultValue: `${count} items selected`,
                  })}
                </span>
              </div>
            )}

            {/* Quick Queue Move Bar */}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(4, 1fr)",
                gap: "3px",
                padding: "4px 6px",
                background: "rgba(255, 255, 255, 0.03)",
                borderBottom:
                  "1px solid var(--border, rgba(255, 255, 255, 0.1))",
              }}
            >
              <button
                type="button"
                className="context-menu-item"
                style={{
                  justifyContent: "center",
                  padding: "4px 2px",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 600,
                }}
                onClick={() => handleMoveQueueAll("top")}
                title={t("torrents.contextMenu.top", {
                  defaultValue: "Move to Top",
                })}
              >
                ⤒ {t("torrents.contextMenu.top", { defaultValue: "Top" })}
              </button>
              <button
                type="button"
                className="context-menu-item"
                style={{
                  justifyContent: "center",
                  padding: "4px 2px",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 600,
                }}
                onClick={() => handleMoveQueueAll("up")}
                title={t("torrents.contextMenu.up", {
                  defaultValue: "Move Up",
                })}
              >
                ▲ {t("torrents.contextMenu.up", { defaultValue: "Up" })}
              </button>
              <button
                type="button"
                className="context-menu-item"
                style={{
                  justifyContent: "center",
                  padding: "4px 2px",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 600,
                }}
                onClick={() => handleMoveQueueAll("down")}
                title={t("torrents.contextMenu.down", {
                  defaultValue: "Move Down",
                })}
              >
                ▼ {t("torrents.contextMenu.down", { defaultValue: "Down" })}
              </button>
              <button
                type="button"
                className="context-menu-item"
                style={{
                  justifyContent: "center",
                  padding: "4px 2px",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 600,
                }}
                onClick={() => handleMoveQueueAll("bottom")}
                title={t("torrents.contextMenu.bottom", {
                  defaultValue: "Move to Bottom",
                })}
              >
                ⤓ {t("torrents.contextMenu.bottom", { defaultValue: "Bottom" })}
              </button>
            </div>

            {/* Arr Direct Jump Link (Single selection only) */}
            {!isMulti && arrLink && (
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
            {isMulti ? (
              <>
                {hasInactive && (
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={handleStartAll}
                  >
                    ▶ {t("torrents.contextMenu.resumeDownload")}
                    {countSuffix}
                  </button>
                )}
                {hasActive && (
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={handleStopAll}
                  >
                    ⏸ {t("torrents.contextMenu.pauseDownload")}
                    {countSuffix}
                  </button>
                )}
              </>
            ) : !hasActive ? (
              <button
                type="button"
                className="context-menu-item"
                onClick={handleStartAll}
              >
                ▶ {t("torrents.contextMenu.resumeDownload")}
              </button>
            ) : (
              <button
                type="button"
                className="context-menu-item"
                onClick={handleStopAll}
              >
                ⏸ {t("torrents.contextMenu.pauseDownload")}
              </button>
            )}

            <button
              type="button"
              className="context-menu-item"
              onClick={handleAnnounceAll}
            >
              ⚡ {t("torrents.contextMenu.updateTracker")}
              {countSuffix}
            </button>
            <button
              type="button"
              className="context-menu-item"
              onClick={handleRecheckAll}
            >
              🛡 {t("torrents.contextMenu.forceRecheck")}
              {countSuffix}
            </button>

            <div className="context-menu-separator" />

            {/* Usability & Navigation Actions (Single selection only) */}
            {!isMulti && ct && (
              <>
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
              </>
            )}

            {/* Copy submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("copy")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              {t("torrents.contextMenu.copy")}
              {countSuffix} ▶
              {openSubmenu === "copy" && (
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() =>
                      handleCopy(
                        effectiveTorrents.map((t) => t.name).join("\n"),
                      )
                    }
                  >
                    {t("torrents.contextMenu.copyName")}
                    {countSuffix}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() =>
                      handleCopy(
                        effectiveTorrents
                          .map((t) => t.infoHash)
                          .filter(Boolean)
                          .join("\n"),
                      )
                    }
                  >
                    {t("torrents.contextMenu.copyInfoHash")}
                    {countSuffix}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() =>
                      handleCopy(
                        effectiveTorrents
                          .map((t) => buildMagnetLink(t))
                          .join("\n"),
                      )
                    }
                  >
                    {t("torrents.contextMenu.copyMagnetLink")}
                    {countSuffix}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() =>
                      handleCopy(
                        effectiveTorrents
                          .map((t) => t.trackerUrl ?? "")
                          .filter(Boolean)
                          .join("\n"),
                      )
                    }
                  >
                    {t("torrents.contextMenu.copyTrackerUrl")}
                    {countSuffix}
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
              {t("torrents.contextMenu.priority")}
              {countSuffix} ▶
              {openSubmenu === "priority" && (
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() =>
                      handleUpdateAll((t) => ({ ...t, priority: 2 }))
                    }
                  >
                    {!isMulti && ct?.priority === 2 ? "✓ " : ""}
                    {t("torrents.contextMenu.highPriority")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() =>
                      handleUpdateAll((t) => ({ ...t, priority: 1 }))
                    }
                  >
                    {!isMulti && ct?.priority === 1 ? "✓ " : ""}
                    {t("torrents.contextMenu.normalPriority")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() =>
                      handleUpdateAll((t) => ({ ...t, priority: 0 }))
                    }
                  >
                    {!isMulti && ct?.priority === 0 ? "✓ " : ""}
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
              {t("torrents.contextMenu.speedLimit")}
              {countSuffix} ▶
              {openSubmenu === "speed" && (
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => {
                      setPromptConfig({
                        title: `${t("torrents.contextMenu.setUploadLimit")}${countSuffix}`,
                        message: `${t("torrents.table.uploadLimit")} (KB/s):`,
                        defaultValue: String(ct?.uploadLimit || 0),
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
                            handleUpdateAll((t) => ({
                              ...t,
                              uploadLimit: val,
                            }));
                          }
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
                        title: `${t("torrents.contextMenu.setDownloadLimit")}${countSuffix}`,
                        message: `${t("torrents.table.downloadLimit")} (KB/s):`,
                        defaultValue: String(ct?.downloadLimit || 0),
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
                            handleUpdateAll((t) => ({
                              ...t,
                              downloadLimit: val,
                            }));
                          }
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
                      handleUpdateAll((t) => ({
                        ...t,
                        uploadLimit: 0,
                        downloadLimit: 0,
                      }));
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
              {t("torrents.contextMenu.queue")}
              {countSuffix} ▶
              {openSubmenu === "queue" && (
                <div
                  className={`context-menu context-menu-submenu ${flipSubmenu ? "flip-left" : ""}`}
                >
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("top")}
                  >
                    {t("torrents.contextMenu.top")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("up")}
                  >
                    {t("torrents.contextMenu.up")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("down")}
                  >
                    {t("torrents.contextMenu.down")}
                  </button>
                  <button
                    type="button"
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("bottom")}
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
                  title: `${t("torrents.contextMenu.setCategory")}${countSuffix}`,
                  message: `${t("torrents.contextMenu.setCategory")}:`,
                  defaultValue: !isMulti
                    ? (ct?.category ?? ct?.label ?? "")
                    : "",
                  inputType: "text",
                  placeholder: "e.g. movies, tv, music",
                  confirmText: t("common.save"),
                  onConfirm: (l) => {
                    const trimmed = l.trim();
                    handleUpdateAll((t) => ({
                      ...t,
                      category: trimmed,
                      label: trimmed,
                    }));
                  },
                });
              }}
            >
              {t("torrents.contextMenu.setCategory")}...
              {!isMulti && ct?.category ? ` (${ct.category})` : countSuffix}
            </button>
            <button
              type="button"
              className="context-menu-item"
              onClick={() => {
                if (isMulti) {
                  const anyDisabled = effectiveTorrents.some(
                    (t) => !t.sequentialDownload,
                  );
                  handleUpdateAll((t) => ({
                    ...t,
                    sequentialDownload: anyDisabled,
                  }));
                } else if (ct) {
                  handleUpdateAll((t) => ({
                    ...t,
                    sequentialDownload: !t.sequentialDownload,
                  }));
                }
              }}
            >
              {isMulti
                ? `Sequential Download${countSuffix}`
                : ct?.sequentialDownload
                  ? t("torrents.contextMenu.disableSequential")
                  : t("torrents.contextMenu.enableSequential")}
            </button>

            <div className="context-menu-separator" />

            {/* Delete button */}
            <button
              type="button"
              className="context-menu-item context-menu-item-danger"
              onClick={() => handleDeleteAll(false)}
            >
              🗑 {t("torrents.contextMenu.removeTorrent")}
              {countSuffix}
            </button>

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
        <ErrorBoundary title="Prompt Dialog">
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
        </ErrorBoundary>
      )}
    </>
  );
}

export default TorrentContextMenu;
