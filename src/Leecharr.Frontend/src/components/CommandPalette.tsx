import React, { useState, useEffect, useMemo, useRef, useCallback } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "../i18n";
import { useTorrents } from "../api/hooks";
import { useTheme } from "../context/ThemeContext";
import { useToast } from "../context/ToastContext";
import { useTorrentStore } from "../stores/useTorrentStore";
import { api } from "../api/client";
import { SETTINGS_GROUPS } from "../pages/settings/settingsNavData";
import { formatBytes } from "../utils/formatters";
import { useFocusTrap } from "../hooks/useFocusTrap";

export interface CommandPaletteProps {
  isOpen: boolean;
  onClose: () => void;
  onOpenAddTorrent?: () => void;
  onOpenIndexerSearch?: () => void;
  onOpenShortcuts?: () => void;
  onOpenGettingStarted?: () => void;
}

type CommandCategory = "navigation" | "settings" | "actions" | "torrents";

interface CommandItem {
  id: string;
  category: CommandCategory;
  title: string;
  subtitle?: string;
  icon: string | React.ReactNode;
  badge?: string;
  keywords?: string[];
  action: () => void;
}

export function CommandPalette({
  isOpen,
  onClose,
  onOpenAddTorrent,
  onOpenIndexerSearch,
  onOpenShortcuts,
  onOpenGettingStarted,
}: CommandPaletteProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { theme, toggleTheme } = useTheme();
  const { showToast } = useToast();
  const { data: torrents = [] } = useTorrents();
  const setSelectedTorrentId = useTorrentStore(
    (state) => state.setSelectedTorrentId,
  );

  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onClose,
  });

  useEffect(() => {
    if (isOpen) {
      setQuery("");
      setSelectedIndex(0);
      setTimeout(() => {
        inputRef.current?.focus();
      }, 50);
    }
  }, [isOpen]);

  const handlePauseAll = useCallback(async () => {
    const active = torrents.filter(
      (tor) => (tor.status || "").toLowerCase() !== "paused",
    );
    if (active.length === 0) {
      showToast("No active torrents to pause", "info");
      return;
    }
    for (const tor of active) {
      try {
        await api.pauseTorrent(tor.id);
      } catch {
        /* continue */
      }
    }
    showToast(`Paused ${active.length} torrent(s)`, "info");
  }, [torrents, showToast]);

  const handleResumeAll = useCallback(async () => {
    const paused = torrents.filter(
      (tor) => (tor.status || "").toLowerCase() === "paused",
    );
    if (paused.length === 0) {
      showToast("No paused torrents to resume", "info");
      return;
    }
    for (const tor of paused) {
      try {
        await api.resumeTorrent(tor.id);
      } catch {
        /* continue */
      }
    }
    showToast(`Resumed ${paused.length} torrent(s)`, "success");
  }, [torrents, showToast]);

  const allCommands = useMemo<CommandItem[]>(() => {
    const items: CommandItem[] = [];

    // --- 1. Quick Actions ---
    items.push(
      {
        id: "act-add-torrent",
        category: "actions",
        title: t("modals.addTorrent", "Add Torrent"),
        subtitle: "Upload .torrent file or paste magnet link",
        icon: "➕",
        badge: "Action",
        keywords: ["add", "upload", "magnet", "url", "download", "create", "new"],
        action: () => {
          onClose();
          onOpenAddTorrent?.();
        },
      },
      {
        id: "act-search-indexers",
        category: "actions",
        title: t("nav.indexers", "Search Indexers"),
        subtitle: "Search integrated indexers and Torznab feeds",
        icon: "🔍",
        badge: "Action",
        keywords: ["search", "find", "prowlarr", "torznab", "tracker", "query"],
        action: () => {
          onClose();
          onOpenIndexerSearch?.();
        },
      },
      {
        id: "act-resume-all",
        category: "actions",
        title: "Resume All Torrents",
        subtitle: "Start all paused and stopped transfers",
        icon: "▶️",
        badge: "Bulk",
        keywords: ["resume", "start", "unpause", "all", "play"],
        action: () => {
          onClose();
          handleResumeAll();
        },
      },
      {
        id: "act-pause-all",
        category: "actions",
        title: "Pause All Torrents",
        subtitle: "Halt all active downloads and uploads",
        icon: "⏸️",
        badge: "Bulk",
        keywords: ["pause", "stop", "freeze", "halt", "all"],
        action: () => {
          onClose();
          handlePauseAll();
        },
      },
      {
        id: "act-toggle-theme",
        category: "actions",
        title: theme === "light" ? "Switch to Dark Mode" : "Switch to Light Mode",
        subtitle: `Currently using ${theme} theme`,
        icon: theme === "light" ? "🌙" : "☀️",
        badge: "UI",
        keywords: ["theme", "dark", "light", "mode", "color", "appearance"],
        action: () => {
          onClose();
          toggleTheme();
        },
      },
      {
        id: "act-shortcuts",
        category: "actions",
        title: "Keyboard Shortcuts Cheatsheet",
        subtitle: "View all hotkeys and keybindings (?)",
        icon: "⌨️",
        badge: "Help",
        keywords: ["shortcuts", "hotkeys", "keyboard", "help", "cheatsheet", "?"],
        action: () => {
          onClose();
          onOpenShortcuts?.();
        },
      },
      {
        id: "act-getting-started",
        category: "actions",
        title: t("nav.gettingStarted", "Getting Started Guide"),
        subtitle: "Interactive setup and onboarding checklist",
        icon: "🚀",
        badge: "Guide",
        keywords: ["guide", "getting started", "welcome", "setup", "tutorial", "help"],
        action: () => {
          onClose();
          onOpenGettingStarted?.();
        },
      },
    );

    // --- 2. Main Navigation Pages ---
    items.push(
      {
        id: "nav-dashboard",
        category: "navigation",
        title: t("nav.dashboard", "Dashboard"),
        subtitle: "Transfer overview, bandwidth speeds, and swarm telemetry",
        icon: "📊",
        keywords: ["dashboard", "home", "stats", "overview", "main"],
        action: () => {
          onClose();
          navigate("/");
        },
      },
      {
        id: "nav-torrents",
        category: "navigation",
        title: t("nav.torrents", "Torrents"),
        subtitle: "Active torrent transfers, queue list, and detail inspector",
        icon: "⬇️",
        keywords: ["torrents", "transfers", "downloads", "seeds", "queue", "table"],
        action: () => {
          onClose();
          navigate("/torrents");
        },
      },
      {
        id: "nav-activity-history",
        category: "navigation",
        title: t("nav.history", "Download History"),
        subtitle: "Historical completed transfers and audit logs",
        icon: "📜",
        keywords: ["history", "activity", "downloads", "audit", "completed"],
        action: () => {
          onClose();
          navigate("/activity/history");
        },
      },
      {
        id: "nav-activity-metrics",
        category: "navigation",
        title: t("nav.statistics", "Real-Time Activity Metrics"),
        subtitle: "Telemetry graphs and transfer activity",
        icon: "📈",
        keywords: ["activity", "metrics", "graphs", "charts", "live", "telemetry"],
        action: () => {
          onClose();
          navigate("/activity/metrics");
        },
      },
      {
        id: "nav-indexers",
        category: "navigation",
        title: t("nav.indexers", "Indexers & Search"),
        subtitle: "Prowlarr & Torznab indexer discovery",
        icon: "🔍",
        keywords: ["indexers", "search", "prowlarr", "torznab", "feeds"],
        action: () => {
          onClose();
          navigate("/indexers");
        },
      },
      {
        id: "nav-peermap",
        category: "navigation",
        title: t("nav.peerMap", "Peer Geo Map"),
        subtitle: "Global visual map of connected swarm peers",
        icon: "🗺️",
        keywords: ["peermap", "peer map", "map", "geo", "location", "countries", "ip"],
        action: () => {
          onClose();
          navigate("/peermap");
        },
      },
      {
        id: "nav-schedule",
        category: "navigation",
        title: t("nav.speedSchedule", "Speed Schedule"),
        subtitle: "Automated bandwidth throttling and calendar rules",
        icon: "⏰",
        keywords: ["schedule", "speed schedule", "calendar", "time", "rate limit", "throttle"],
        action: () => {
          onClose();
          navigate("/schedule");
        },
      },
      {
        id: "nav-statistics",
        category: "navigation",
        title: t("nav.statistics", "Statistics"),
        subtitle: "Long-term transfer metrics, ratio, and storage stats",
        icon: "📊",
        keywords: ["statistics", "stats", "ratio", "storage", "analytics"],
        action: () => {
          onClose();
          navigate("/statistics");
        },
      },
      {
        id: "nav-trackerboost",
        category: "navigation",
        title: t("nav.trackerBoost", "Tracker Boost"),
        subtitle: "Swarm optimization, public tracker pool, and health tester",
        icon: "⚡",
        keywords: ["trackerboost", "boost", "trackers", "swarm", "optimization", "pool"],
        action: () => {
          onClose();
          navigate("/trackerboost");
        },
      },
      {
        id: "nav-terminal",
        category: "navigation",
        title: t("nav.terminalCli", "Terminal CLI"),
        subtitle: "Interactive download shell & file inspector",
        icon: "💻",
        keywords: ["terminal", "cli", "shell", "bash", "command line", "pty"],
        action: () => {
          onClose();
          navigate("/terminal");
        },
      },
      {
        id: "nav-files",
        category: "navigation",
        title: t("nav.fileBrowser", "File Browser"),
        subtitle: "Manage downloads directory files and folders",
        icon: "📁",
        keywords: ["files", "file browser", "explorer", "manager", "download dir", "filesystem"],
        action: () => {
          onClose();
          navigate("/files");
        },
      },
      {
        id: "nav-automation",
        category: "navigation",
        title: "Automation & DSL Engine",
        subtitle: "Rule pipelines, webhook actions, and custom automation scripts",
        icon: "🤖",
        keywords: ["automation", "scripts", "dsl", "rules", "triggers", "webhooks", "marketplace"],
        action: () => {
          onClose();
          navigate("/automation");
        },
      },
      {
        id: "nav-system-status",
        category: "navigation",
        title: t("system.status", "System Status"),
        subtitle: "Host runtime, health checks, and engine diagnostics",
        icon: "🖥️",
        keywords: ["system", "status", "health", "uptime", "version", "engine"],
        action: () => {
          onClose();
          navigate("/system/status");
        },
      },
      {
        id: "nav-system-resources",
        category: "navigation",
        title: t("system.resources", "System Resources"),
        subtitle: "CPU, memory, disk I/O, and thread telemetry",
        icon: "📈",
        keywords: ["system", "resources", "cpu", "ram", "memory", "telemetry", "threads"],
        action: () => {
          onClose();
          navigate("/system/resources");
        },
      },
      {
        id: "nav-system-backup",
        category: "navigation",
        title: t("system.backup", "System Backup"),
        subtitle: "Database backups and configuration restore",
        icon: "💾",
        keywords: ["backup", "restore", "database", "export", "import"],
        action: () => {
          onClose();
          navigate("/system/backup");
        },
      },
      {
        id: "nav-system-logs",
        category: "navigation",
        title: t("system.logs", "System Logs"),
        subtitle: "Live application logs and log files",
        icon: "📄",
        keywords: ["logs", "system logs", "debug", "trace", "exceptions", "errors"],
        action: () => {
          onClose();
          navigate("/system/logs");
        },
      },
      {
        id: "nav-system-api",
        category: "navigation",
        title: t("system.apiReference", "API Docs / Swagger"),
        subtitle: "REST API interactive OpenAPI documentation",
        icon: "🔌",
        keywords: ["api", "swagger", "openapi", "rest", "docs", "endpoints", "reference"],
        action: () => {
          onClose();
          navigate("/system/api");
        },
      },
    );

    // --- 3. Settings Sub-Tabs ---
    for (const group of SETTINGS_GROUPS) {
      for (const page of group.pages) {
        items.push({
          id: `settings-${page.id}`,
          category: "settings",
          title: `Settings: ${t(page.shortLabel)}`,
          subtitle: t(page.description),
          icon: page.icon,
          badge: page.badge ? t(page.badge) : undefined,
          keywords: ["settings", "config", page.id, ...(page.keywords || [])],
          action: () => {
            onClose();
            navigate(`/settings/${page.id}`);
          },
        });
      }
    }

    // --- 4. Active Torrents ---
    for (const tor of torrents) {
      items.push({
        id: `torrent-${tor.id}`,
        category: "torrents",
        title: tor.name,
        subtitle: `${tor.status || "Idle"} • ${formatBytes(tor.totalSize || 0)} • ${((tor.progress || 0) * 100).toFixed(1)}%`,
        icon: "📦",
        badge: tor.status || "Torrent",
        keywords: [
          "torrent",
          tor.name,
          tor.status || "",
          tor.category || "",
          tor.infoHash || "",
        ],
        action: () => {
          onClose();
          setSelectedTorrentId(tor.id);
          navigate("/torrents");
        },
      });
    }

    return items;
  }, [
    t,
    navigate,
    theme,
    toggleTheme,
    torrents,
    onClose,
    onOpenAddTorrent,
    onOpenIndexerSearch,
    onOpenShortcuts,
    onOpenGettingStarted,
    handlePauseAll,
    handleResumeAll,
    setSelectedTorrentId,
  ]);

  // Filter commands by fuzzy keyword matching
  const filteredCommands = useMemo(() => {
    if (!query.trim()) {
      // Return top priority items when search is empty
      return allCommands.filter((cmd) => cmd.category !== "torrents");
    }

    const q = query.trim().toLowerCase();
    const qTokens = q.split(/\s+/).filter(Boolean);

    return allCommands.filter((cmd) => {
      const targetText = [
        cmd.title,
        cmd.subtitle || "",
        ...(cmd.keywords || []),
      ]
        .join(" ")
        .toLowerCase();

      return qTokens.every((token) => targetText.includes(token));
    });
  }, [allCommands, query]);

  // Handle keyboard navigation inside command palette
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((prev) =>
        prev < filteredCommands.length - 1 ? prev + 1 : 0,
      );
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((prev) =>
        prev > 0 ? prev - 1 : filteredCommands.length - 1,
      );
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (filteredCommands[selectedIndex]) {
        filteredCommands[selectedIndex].action();
      }
    } else if (e.key === "Escape") {
      e.preventDefault();
      onClose();
    }
  };

  // Scroll active item into view
  useEffect(() => {
    const listEl = listRef.current;
    if (!listEl) return;
    const activeEl = listEl.querySelector<HTMLElement>(".command-palette-item.active");
    if (activeEl) {
      activeEl.scrollIntoView({ block: "nearest" });
    }
  }, [selectedIndex]);

  if (!isOpen) return null;

  return (
    <div
      className="modal-overlay"
      onClick={onClose}
      style={{
        position: "fixed",
        inset: 0,
        backgroundColor: "rgba(10, 11, 20, 0.82)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "center",
        zIndex: 9999,
        paddingTop: "12vh",
      }}
    >
      <div
        ref={trapRef}
        className="modal-content command-palette-modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: "640px",
          backgroundColor: "var(--bg-card, #171b35)",
          borderRadius: "12px",
          border: "1px solid rgba(255, 209, 102, 0.35)",
          boxShadow: "0 24px 60px rgba(0, 0, 0, 0.75)",
          overflow: "hidden",
          display: "flex",
          flexDirection: "column",
          maxHeight: "70vh",
        }}
      >
        {/* Search Input Bar */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            padding: "0.85rem 1.15rem",
            borderBottom: "1px solid var(--border-light, #1c203b)",
            gap: "0.75rem",
            backgroundColor: "var(--bg-primary, #10111a)",
          }}
        >
          <span style={{ fontSize: "1.2rem", opacity: 0.7 }}>🔍</span>
          <input
            ref={inputRef}
            type="text"
            className="command-palette-input"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setSelectedIndex(0);
            }}
            onKeyDown={handleKeyDown}
            placeholder="Type a command, page name, setting, or torrent..."
            style={{
              flex: 1,
              background: "transparent",
              border: "none",
              outline: "none",
              color: "var(--text-primary, #f8f4ed)",
              fontSize: "1rem",
              fontWeight: 500,
            }}
          />
          {query ? (
            <button
              type="button"
              onClick={() => setQuery("")}
              style={{
                background: "transparent",
                border: "none",
                color: "var(--text-muted)",
                cursor: "pointer",
                fontSize: "0.9rem",
              }}
            >
              ✕
            </button>
          ) : (
            <kbd
              style={{
                backgroundColor: "rgba(255, 255, 255, 0.08)",
                border: "1px solid rgba(255, 255, 255, 0.16)",
                borderRadius: "4px",
                padding: "0.15rem 0.45rem",
                fontSize: "0.72rem",
                color: "var(--text-muted)",
                fontFamily: "monospace",
              }}
            >
              ESC
            </kbd>
          )}
        </div>

        {/* Results List */}
        <div
          ref={listRef}
          style={{
            flex: 1,
            overflowY: "auto",
            padding: "0.5rem",
            maxHeight: "420px",
          }}
        >
          {filteredCommands.length === 0 ? (
            <div
              style={{
                padding: "2.5rem 1rem",
                textAlign: "center",
                color: "var(--text-muted)",
                fontSize: "0.9rem",
              }}
            >
              <div style={{ fontSize: "1.8rem", marginBottom: "0.5rem" }}>🤔</div>
              No matching commands or torrents found for "{query}".
            </div>
          ) : (
            filteredCommands.map((item, idx) => {
              const isSelected = idx === selectedIndex;
              return (
                <div
                  key={item.id}
                  className={`command-palette-item ${isSelected ? "active" : ""}`}
                  onClick={item.action}
                  onMouseEnter={() => setSelectedIndex(idx)}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.75rem",
                    padding: "0.6rem 0.85rem",
                    borderRadius: "8px",
                    cursor: "pointer",
                    backgroundColor: isSelected
                      ? "rgba(255, 209, 102, 0.15)"
                      : "transparent",
                    borderLeft: isSelected
                      ? "3px solid var(--accent, #ffd166)"
                      : "3px solid transparent",
                    transition: "all 0.1s ease",
                  }}
                >
                  <span style={{ fontSize: "1.2rem", flexShrink: 0 }}>
                    {item.icon}
                  </span>
                  <div style={{ flex: 1, minWidth: 0, overflow: "hidden" }}>
                    <div
                      style={{
                        fontWeight: 600,
                        fontSize: "0.88rem",
                        color: isSelected
                          ? "var(--accent, #ffd166)"
                          : "var(--text-primary, #f8f4ed)",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {item.title}
                    </div>
                    {item.subtitle && (
                      <div
                        style={{
                          fontSize: "0.75rem",
                          color: "var(--text-muted, #8a879e)",
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                          marginTop: "1px",
                        }}
                      >
                        {item.subtitle}
                      </div>
                    )}
                  </div>
                  {item.badge && (
                    <span
                      style={{
                        fontSize: "0.7rem",
                        padding: "0.15rem 0.45rem",
                        borderRadius: "4px",
                        backgroundColor: "rgba(255, 255, 255, 0.08)",
                        color: "var(--text-secondary, #c7c5d3)",
                        flexShrink: 0,
                      }}
                    >
                      {item.badge}
                    </span>
                  )}
                </div>
              );
            })
          )}
        </div>

        {/* Footer Hints */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "0.5rem 1.15rem",
            backgroundColor: "var(--bg-primary, #10111a)",
            borderTop: "1px solid var(--border-light, #1c203b)",
            fontSize: "0.75rem",
            color: "var(--text-muted)",
          }}
        >
          <div style={{ display: "flex", gap: "1rem" }}>
            <span>
              <kbd style={{ fontFamily: "monospace" }}>↑</kbd>{" "}
              <kbd style={{ fontFamily: "monospace" }}>↓</kbd> to navigate
            </span>
            <span>
              <kbd style={{ fontFamily: "monospace" }}>↵</kbd> to select
            </span>
            <span>
              <kbd style={{ fontFamily: "monospace" }}>esc</kbd> to close
            </span>
          </div>
          <span>Leecharr Command Palette</span>
        </div>
      </div>
    </div>
  );
}

export default CommandPalette;
