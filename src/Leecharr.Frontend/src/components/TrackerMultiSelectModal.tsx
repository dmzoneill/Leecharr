import React, { useState, useMemo } from "react";
import TrackerFavicon from "./TrackerFavicon";
import { useEscapeKey } from "../hooks/useEscapeKey";

export interface TrackerPickerItem {
  url: string;
  host?: string;
  protocol?: string;
  isAttached: boolean;
  isVerified?: boolean;
  isAlive?: boolean;
  isSlow?: boolean;
  isOffline?: boolean;
  latencyMs?: number;
  seeders?: number;
  leechers?: number;
  statusLabel?: string;
}

export interface TrackerMultiSelectModalProps {
  isOpen: boolean;
  onClose: () => void;
  trackers: TrackerPickerItem[];
  selectedUrls: Set<string>;
  onToggleUrl: (url: string) => void;
  onSelectBatch: (urls: string[]) => void;
  onClearSelection: () => void;
  onAddAndAnnounce: () => void;
  isAdding?: boolean;
}

export function TrackerMultiSelectModal({
  isOpen,
  onClose,
  trackers,
  selectedUrls,
  onToggleUrl,
  onSelectBatch,
  onClearSelection,
  onAddAndAnnounce,
  isAdding = false,
}: TrackerMultiSelectModalProps) {
  useEscapeKey(onClose, isOpen);

  const [searchTerm, setSearchTerm] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [customUrl, setCustomUrl] = useState("");

  // Sort trackers: Active / Verified -> Online -> Slow -> Untested -> Offline, then alphabetically
  const sortedTrackers = useMemo(() => {
    return [...trackers].sort((a, b) => {
      const getPriority = (item: TrackerPickerItem): number => {
        if (item.isAttached) return 99; // Attached at the bottom
        if (item.isVerified) return 1;
        if (item.isAlive) return 2;
        if (item.isSlow) return 3;
        if (!item.isOffline) return 4; // Untested
        return 5; // Offline
      };

      const pA = getPriority(a);
      const pB = getPriority(b);
      if (pA !== pB) return pA - pB;

      const hostA = (a.host || a.url).toLowerCase();
      const hostB = (b.host || b.url).toLowerCase();
      return hostA.localeCompare(hostB);
    });
  }, [trackers]);

  // Filter trackers by search and status
  const filteredTrackers = useMemo(() => {
    return sortedTrackers.filter((item) => {
      if (statusFilter === "verified" && !item.isVerified) return false;
      if (statusFilter === "online" && !item.isAlive && !item.isVerified)
        return false;
      if (statusFilter === "unattached" && item.isAttached) return false;

      if (!searchTerm.trim()) return true;
      const q = searchTerm.toLowerCase();
      return (
        item.url.toLowerCase().includes(q) ||
        (item.host && item.host.toLowerCase().includes(q)) ||
        (item.protocol && item.protocol.toLowerCase().includes(q)) ||
        (item.statusLabel && item.statusLabel.toLowerCase().includes(q))
      );
    });
  }, [sortedTrackers, searchTerm, statusFilter]);

  const verifiedUnattached = useMemo(
    () =>
      trackers.filter((t) => t.isVerified && !t.isAttached).map((t) => t.url),
    [trackers],
  );

  const onlineUnattached = useMemo(
    () =>
      trackers
        .filter((t) => (t.isAlive || t.isVerified) && !t.isAttached)
        .map((t) => t.url),
    [trackers],
  );

  const handleSelectAllFiltered = () => {
    const unattachedFiltered = filteredTrackers
      .filter((t) => !t.isAttached)
      .map((t) => t.url);
    onSelectBatch(unattachedFiltered);
  };

  const handleAddCustom = (e: React.FormEvent) => {
    e.preventDefault();
    if (!customUrl.trim()) return;
    const clean = customUrl.trim();
    if (
      clean.startsWith("http://") ||
      clean.startsWith("https://") ||
      clean.startsWith("udp://")
    ) {
      onToggleUrl(clean);
      setCustomUrl("");
    }
  };

  if (!isOpen) return null;

  return (
    <div
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.78)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 9999,
        padding: "1rem",
      }}
      onClick={onClose}
    >
      <div
        className="card"
        style={{
          width: "720px",
          maxWidth: "94vw",
          maxHeight: "88vh",
          display: "flex",
          flexDirection: "column",
          borderRadius: "10px",
          padding: 0,
          overflow: "hidden",
          border: "1px solid rgba(255, 255, 255, 0.16)",
          boxShadow: "0 20px 45px rgba(0, 0, 0, 0.6)",
          backgroundColor: "var(--bg-secondary, #171b35)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          style={{
            padding: "1rem 1.25rem",
            backgroundColor: "var(--bg-secondary, #171b35)",
            borderBottom: "1px solid var(--border-light, #1c203b)",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}>
            <span style={{ fontSize: "1.3rem" }}>🎯</span>
            <div>
              <h3
                style={{
                  margin: 0,
                  fontSize: "1.05rem",
                  fontWeight: 600,
                  color: "var(--text-primary, #f8f4ed)",
                }}
              >
                Select Trackers to Add & Announce
              </h3>
              <p
                style={{
                  margin: 0,
                  fontSize: "0.78rem",
                  color: "var(--text-muted, #7e8092)",
                }}
              >
                Choose verified and online tracker endpoints to attach to this
                swarm
              </p>
            </div>
          </div>
          <button
            type="button"
            className="btn btn-small btn-outline"
            onClick={onClose}
            style={{ padding: "0.2rem 0.5rem" }}
          >
            ✕
          </button>
        </div>

        {/* Toolbar & Filter */}
        <div
          style={{
            padding: "0.75rem 1.25rem",
            backgroundColor: "rgba(0, 0, 0, 0.15)",
            borderBottom: "1px solid var(--border-light, #1c203b)",
            display: "flex",
            flexDirection: "column",
            gap: "0.6rem",
          }}
        >
          <div
            style={{
              display: "flex",
              gap: "0.5rem",
              alignItems: "center",
              flexWrap: "wrap",
            }}
          >
            <input
              type="text"
              className="topbar-search-input"
              placeholder="🔍 Search by domain, protocol, status..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              style={{
                flex: "1 1 240px",
                padding: "0.4rem 0.75rem",
                fontSize: "0.85rem",
              }}
              autoFocus
            />

            <select
              className="form-control"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              style={{
                width: "160px",
                padding: "0.4rem 0.6rem",
                fontSize: "0.82rem",
                backgroundColor: "var(--bg-card, #171b35)",
                color: "var(--text-primary, #f8f4ed)",
                border: "1px solid var(--border-light, #1c203b)",
              }}
            >
              <option value="all">All Trackers ({trackers.length})</option>
              <option value="verified">
                🟢 Verified in Swarm ({verifiedUnattached.length})
              </option>
              <option value="online">
                🟢 Online & Verified ({onlineUnattached.length})
              </option>
              <option value="unattached">Unattached Only</option>
            </select>
          </div>

          {/* Quick Selection Shortcuts */}
          <div
            style={{
              display: "flex",
              gap: "0.4rem",
              alignItems: "center",
              flexWrap: "wrap",
              fontSize: "0.78rem",
            }}
          >
            <span
              style={{
                color: "var(--text-muted, #7e8092)",
                marginRight: "0.2rem",
              }}
            >
              Quick Select:
            </span>

            {verifiedUnattached.length > 0 && (
              <button
                type="button"
                className="btn btn-small btn-success"
                style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                onClick={() => onSelectBatch(verifiedUnattached)}
              >
                🟢 Verified Swarms ({verifiedUnattached.length})
              </button>
            )}

            {onlineUnattached.length > 0 && (
              <button
                type="button"
                className="btn btn-small btn-primary"
                style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                onClick={() => onSelectBatch(onlineUnattached)}
              >
                ⚡ All Online ({onlineUnattached.length})
              </button>
            )}

            <button
              type="button"
              className="btn btn-small btn-outline"
              style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
              onClick={handleSelectAllFiltered}
            >
              Select Filtered (
              {filteredTrackers.filter((t) => !t.isAttached).length})
            </button>

            {selectedUrls.size > 0 && (
              <button
                type="button"
                className="btn btn-small btn-outline"
                style={{
                  fontSize: "0.75rem",
                  padding: "0.2rem 0.5rem",
                  color: "var(--danger, #ef4444)",
                }}
                onClick={onClearSelection}
              >
                Clear ({selectedUrls.size})
              </button>
            )}
          </div>
        </div>

        {/* Tracker List with Checkboxes */}
        <div
          style={{
            flex: 1,
            overflowY: "auto",
            padding: "0.5rem 0.75rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.3rem",
            minHeight: "240px",
            maxHeight: "420px",
          }}
        >
          {filteredTrackers.map((item) => {
            const isSelected = selectedUrls.has(item.url);
            const isAttached = item.isAttached;

            return (
              <div
                key={item.url}
                onClick={() => {
                  if (!isAttached) onToggleUrl(item.url);
                }}
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  padding: "0.5rem 0.75rem",
                  borderRadius: "6px",
                  backgroundColor: isSelected
                    ? "rgba(34, 197, 94, 0.12)"
                    : isAttached
                      ? "rgba(255, 255, 255, 0.03)"
                      : "rgba(255, 255, 255, 0.05)",
                  border: isSelected
                    ? "1px solid rgba(34, 197, 94, 0.45)"
                    : isAttached
                      ? "1px solid rgba(255, 255, 255, 0.05)"
                      : "1px solid var(--border-light, #1c203b)",
                  cursor: isAttached ? "default" : "pointer",
                  transition: "background 0.15s ease",
                  opacity: isAttached ? 0.65 : 1,
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.6rem",
                    minWidth: 0,
                  }}
                >
                  <input
                    type="checkbox"
                    checked={isSelected || isAttached}
                    disabled={isAttached}
                    onChange={() => {
                      if (!isAttached) onToggleUrl(item.url);
                    }}
                    style={{
                      cursor: isAttached ? "default" : "pointer",
                      width: "16px",
                      height: "16px",
                    }}
                  />

                  <TrackerFavicon urlOrHost={item.url} size={18} />

                  <div style={{ minWidth: 0 }}>
                    <div
                      style={{
                        fontSize: "0.83rem",
                        fontFamily: "monospace",
                        fontWeight: 600,
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                        color: isSelected
                          ? "var(--accent, #ffd166)"
                          : "var(--text-primary, #f8f4ed)",
                      }}
                    >
                      {item.url}
                    </div>

                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.4rem",
                        marginTop: "0.15rem",
                        fontSize: "0.72rem",
                        color: "var(--text-muted, #7e8092)",
                      }}
                    >
                      {item.protocol && (
                        <span
                          className="badge"
                          style={{
                            fontSize: "0.65rem",
                            padding: "0.1rem 0.35rem",
                          }}
                        >
                          {item.protocol}
                        </span>
                      )}

                      {item.latencyMs !== undefined && item.latencyMs > 0 && (
                        <span>{item.latencyMs}ms</span>
                      )}

                      {item.seeders !== undefined && item.seeders > 0 && (
                        <span style={{ color: "var(--accent, #ffd166)" }}>
                          ⚡ {item.seeders} seeds / {item.leechers ?? 0} leeches
                        </span>
                      )}
                    </div>
                  </div>
                </div>

                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.4rem",
                    flexShrink: 0,
                    marginLeft: "0.5rem",
                  }}
                >
                  {isAttached ? (
                    <span className="badge" style={{ fontSize: "0.72rem" }}>
                      ✓ Already Attached
                    </span>
                  ) : item.isVerified ? (
                    <span
                      className="badge"
                      style={{
                        fontSize: "0.72rem",
                        backgroundColor: "rgba(34, 197, 94, 0.15)",
                        color: "var(--success, #22c55e)",
                      }}
                    >
                      🟢 Verified Swarm
                    </span>
                  ) : item.isAlive ? (
                    <span
                      className="badge"
                      style={{
                        fontSize: "0.72rem",
                        backgroundColor: "rgba(34, 197, 94, 0.15)",
                        color: "var(--success, #22c55e)",
                      }}
                    >
                      🟢 Alive
                    </span>
                  ) : item.isSlow ? (
                    <span
                      className="badge"
                      style={{
                        fontSize: "0.72rem",
                        backgroundColor: "rgba(234, 179, 8, 0.15)",
                        color: "var(--warning, #eab308)",
                      }}
                    >
                      🟡 Slow
                    </span>
                  ) : item.isOffline ? (
                    <span
                      className="badge"
                      style={{
                        fontSize: "0.72rem",
                        backgroundColor: "rgba(239, 68, 68, 0.15)",
                        color: "var(--danger, #ef4444)",
                      }}
                    >
                      🔴 Offline
                    </span>
                  ) : (
                    <span className="badge" style={{ fontSize: "0.72rem" }}>
                      ⚪ Untested
                    </span>
                  )}
                </div>
              </div>
            );
          })}

          {filteredTrackers.length === 0 && (
            <div
              style={{
                padding: "2.5rem 1rem",
                textAlign: "center",
                color: "var(--text-muted, #7e8092)",
                fontSize: "0.85rem",
              }}
            >
              No candidate trackers found matching {'"'}
              {searchTerm}
              {'"'}.
            </div>
          )}
        </div>

        {/* Custom Tracker input */}
        <form
          onSubmit={handleAddCustom}
          style={{
            padding: "0.6rem 1.25rem",
            backgroundColor: "rgba(0, 0, 0, 0.1)",
            borderTop: "1px solid var(--border-light, #1c203b)",
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
          }}
        >
          <input
            type="text"
            className="topbar-search-input"
            placeholder="Or enter custom URL (e.g. udp://tracker.example.com:1337/announce)"
            value={customUrl}
            onChange={(e) => setCustomUrl(e.target.value)}
            style={{ flex: 1, fontSize: "0.82rem", padding: "0.35rem 0.6rem" }}
          />
          <button
            type="submit"
            className="btn btn-small btn-primary"
            disabled={!customUrl.trim()}
          >
            + Add to List
          </button>
        </form>

        {/* Footer actions */}
        <div
          style={{
            padding: "0.75rem 1.25rem",
            backgroundColor: "var(--bg-secondary, #171b35)",
            borderTop: "1px solid var(--border-light, #1c203b)",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <div style={{ fontSize: "0.85rem", fontWeight: 500 }}>
            {selectedUrls.size === 0 ? (
              <span style={{ color: "var(--text-muted, #7e8092)" }}>
                0 trackers selected
              </span>
            ) : (
              <span style={{ color: "var(--accent, #ffd166)" }}>
                ✓ {selectedUrls.size} tracker(s) selected
              </span>
            )}
          </div>

          <div style={{ display: "flex", gap: "0.5rem" }}>
            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={onClose}
              style={{ fontSize: "0.82rem" }}
            >
              Done (Keep Selection)
            </button>
            <button
              type="button"
              className="btn btn-small btn-success"
              onClick={() => {
                onAddAndAnnounce();
                onClose();
              }}
              disabled={isAdding || selectedUrls.size === 0}
              style={{ fontSize: "0.82rem" }}
            >
              {isAdding
                ? "Adding & Announcing..."
                : `+ Add & Announce (${selectedUrls.size})`}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export default TrackerMultiSelectModal;
