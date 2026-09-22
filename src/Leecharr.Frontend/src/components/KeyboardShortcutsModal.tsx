import React from "react";
import { useTranslation } from "../i18n";
import { useFocusTrap } from "../hooks/useFocusTrap";

export interface KeyboardShortcutsModalProps {
  isOpen: boolean;
  onClose: () => void;
}

interface ShortcutItem {
  keys: string[];
  description: string;
}

interface ShortcutGroup {
  category: string;
  icon: string;
  shortcuts: ShortcutItem[];
}

export function KeyboardShortcutsModal({
  isOpen,
  onClose,
}: KeyboardShortcutsModalProps) {
  const { t } = useTranslation();

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onClose,
  });

  const isMac =
    typeof navigator !== "undefined" &&
    /Mac|iPod|iPhone|iPad/.test(navigator.platform);

  const modKey = isMac ? "⌘" : "Ctrl";

  const groups: ShortcutGroup[] = [
    {
      category: "Global & Search",
      icon: "🌐",
      shortcuts: [
        {
          keys: [`${modKey}`, "K"],
          description: "Open Command Palette / Quick Search",
        },
        {
          keys: ["/"],
          description: "Focus Global Search / Command Palette",
        },
        {
          keys: ["?"],
          description: "Open this Keyboard Shortcuts cheatsheet",
        },
        {
          keys: ["q"],
          description: "Toggle Quick Settings & Bandwidth drawer",
        },
        {
          keys: ["Alt", "M"],
          description: "Toggle navigation sidebar collapse",
        },
        {
          keys: ["Esc"],
          description: "Close active modal, drawer, or detail panel",
        },
      ],
    },
    {
      category: "Two-Key Sequence Navigation",
      icon: "⚡",
      shortcuts: [
        {
          keys: ["g", "d"],
          description: "Go to Dashboard",
        },
        {
          keys: ["g", "t"],
          description: "Go to Torrents view",
        },
        {
          keys: ["g", "a", "or", "h"],
          description: "Go to Activity Hub & Download History",
        },
        {
          keys: ["g", "s"],
          description: "Go to Host Settings",
        },
        {
          keys: ["g", "f"],
          description: "Go to File Browser",
        },
        {
          keys: ["g", "c"],
          description: "Go to Terminal CLI Shell",
        },
        {
          keys: ["g", "i"],
          description: "Go to Indexers & Discovery",
        },
      ],
    },
    {
      category: "Torrent Table & Selection",
      icon: "📋",
      shortcuts: [
        {
          keys: ["↑", "↓"],
          description: "Navigate up and down torrent table rows",
        },
        {
          keys: ["Shift", "↑ / ↓"],
          description: "True range select (dynamically expand/contract)",
        },
        {
          keys: ["Home", "End"],
          description: "Jump to first or last row in table",
        },
        {
          keys: ["Enter"],
          description: "Open & focus torrent detail panel",
        },
        {
          keys: [`${modKey}`, "A"],
          description: "Select all filtered torrents",
        },
        {
          keys: [`${modKey}`, "Shift", "I"],
          description: "Invert torrent selection",
        },
        {
          keys: ["Space", "or", "p"],
          description: "Pause / Resume selected torrent(s)",
        },
        {
          keys: ["Delete"],
          description: "Delete selected torrent(s) (multi-select supported)",
        },
      ],
    },
    {
      category: "Queue Priority & Quick Actions",
      icon: "🚀",
      shortcuts: [
        {
          keys: [`${modKey}`, "↑ / ↓"],
          description: "Move selected torrent up / down in queue",
        },
        {
          keys: [`${modKey}`, "Shift", "↑ / ↓"],
          description: "Move selected torrent to top / bottom of queue",
        },
        {
          keys: [`${modKey}`, "R", "or", "F5"],
          description: "Force recheck selected torrent(s)",
        },
        {
          keys: ["F6"],
          description: "Announce / update tracker for selected torrent(s)",
        },
      ],
    },
    {
      category: "Media Player",
      icon: "🎬",
      shortcuts: [
        {
          keys: ["Space"],
          description: "Play / Pause media playback",
        },
        {
          keys: ["←", "→"],
          description: "Seek -/+ 5 seconds (with Shift: 30s)",
        },
        {
          keys: ["↑", "↓"],
          description: "Adjust volume -/+ 5%",
        },
        {
          keys: ["m"],
          description: "Mute / unmute audio",
        },
        {
          keys: ["f"],
          description: "Toggle fullscreen playback",
        },
        {
          keys: ["c"],
          description: "Cycle subtitle tracks",
        },
        {
          keys: ["[", "]"],
          description: "Cycle speed (0.75x, 1x, 1.25x, 1.5x, 2x)",
        },
      ],
    },
    {
      category: "Detail Panel Files & Script Editor",
      icon: "📁",
      shortcuts: [
        {
          keys: ["↑", "↓"],
          description: "Navigate files and folders tree",
        },
        {
          keys: ["←", "→"],
          description: "Collapse / expand folder or traverse depth",
        },
        {
          keys: ["Space"],
          description: "Toggle selective download checkbox",
        },
        {
          keys: ["0", "1", "2"],
          description: "Set file priority (0=Skip, 1=Low, 2=Normal)",
        },
        {
          keys: ["Esc", "or", "Shift+Tab"],
          description: "Unfocus code editor (WCAG keyboard trap escape)",
        },
      ],
    },
  ];

  if (!isOpen) return null;

  return (
    <div
      className="modal-overlay"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      style={{
        position: "fixed",
        inset: 0,
        backgroundColor: "rgba(10, 11, 20, 0.82)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 9999,
        padding: "1.5rem",
      }}
    >
      <div
        ref={trapRef}
        className="modal-content"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: "680px",
          backgroundColor: "var(--bg-card, #171b35)",
          borderRadius: "12px",
          border: "1px solid rgba(255, 209, 102, 0.35)",
          boxShadow: "0 24px 60px rgba(0, 0, 0, 0.75)",
          overflow: "hidden",
          display: "flex",
          flexDirection: "column",
          maxHeight: "85vh",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "1rem 1.4rem",
            backgroundColor: "var(--bg-primary, #10111a)",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}>
            <span style={{ fontSize: "1.3rem" }}>⌨️</span>
            <div>
              <h3
                style={{
                  margin: 0,
                  fontSize: "1.1rem",
                  fontWeight: 600,
                  color: "var(--text-primary, #f8f4ed)",
                }}
              >
                Keyboard Shortcuts
              </h3>
              <p
                style={{
                  margin: 0,
                  fontSize: "0.78rem",
                  color: "var(--text-muted, #8a879e)",
                }}
              >
                Quick hotkeys for fast client ergonomics and workflow navigation
              </p>
            </div>
          </div>

          <button
            type="button"
            className="btn btn-outline btn-small"
            onClick={onClose}
            style={{ padding: "0.25rem 0.6rem", fontSize: "0.85rem" }}
          >
            ✕
          </button>
        </div>

        {/* Content Body */}
        <div
          style={{
            flex: 1,
            overflowY: "auto",
            padding: "1.25rem 1.4rem",
            display: "flex",
            flexDirection: "column",
            gap: "1.25rem",
          }}
        >
          {groups.map((grp) => (
            <div key={grp.category}>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.45rem",
                  fontSize: "0.88rem",
                  fontWeight: 600,
                  color: "var(--accent, #ffd166)",
                  marginBottom: "0.6rem",
                  paddingBottom: "0.35rem",
                  borderBottom: "1px solid var(--border-light)",
                }}
              >
                <span>{grp.icon}</span>
                <span>{grp.category}</span>
              </div>

              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))",
                  gap: "0.6rem",
                }}
              >
                {grp.shortcuts.map((sc, i) => (
                  <div
                    key={i}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      gap: "0.75rem",
                      padding: "0.5rem 0.75rem",
                      backgroundColor: "var(--bg-primary, #10111a)",
                      borderRadius: "6px",
                      border: "1px solid var(--border-light)",
                    }}
                  >
                    <span
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--text-secondary, #c7c5d3)",
                      }}
                    >
                      {sc.description}
                    </span>

                    <div
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.25rem",
                        flexShrink: 0,
                      }}
                    >
                      {sc.keys.map((k, ki) =>
                        k === "or" ? (
                          <span
                            key={ki}
                            style={{
                              fontSize: "0.72rem",
                              color: "var(--text-muted)",
                              padding: "0 2px",
                            }}
                          >
                            or
                          </span>
                        ) : (
                          <kbd
                            key={ki}
                            style={{
                              display: "inline-block",
                              padding: "0.2rem 0.45rem",
                              fontSize: "0.75rem",
                              fontWeight: 600,
                              lineHeight: 1,
                              color: "var(--text-primary, #f8f4ed)",
                              backgroundColor: "rgba(255, 255, 255, 0.08)",
                              border: "1px solid var(--border)",
                              borderRadius: "4px",
                              boxShadow: "0 1px 2px rgba(0,0,0,0.3)",
                              fontFamily: "monospace",
                            }}
                          >
                            {k}
                          </kbd>
                        ),
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>

        {/* Footer */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "flex-end",
            padding: "0.75rem 1.4rem",
            backgroundColor: "var(--bg-primary, #10111a)",
            borderTop: "1px solid var(--border-light)",
          }}
        >
          <button
            type="button"
            className="btn btn-primary btn-small"
            onClick={onClose}
          >
            {t("common.close", "Close")}
          </button>
        </div>
      </div>
    </div>
  );
}

export default KeyboardShortcutsModal;
