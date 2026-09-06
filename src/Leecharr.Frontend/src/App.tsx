import React, { useEffect, useState, useCallback } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  useLocation,
  useNavigate,
  Routes,
  Route,
  Navigate,
} from "react-router";
import { api } from "./api/client";
import { signalRManager } from "./api/signalr";
import { Torrent, Category } from "./api/types";
import {
  useIndexers,
  useGeneralConfig,
  useRefetchInterval,
  useTorrents,
  useCategories,
} from "./api/hooks";
import { useTorrentStore } from "./stores/useTorrentStore";
import { LeecharrLogo } from "./components/icons/LeecharrLogo";
import { LeecharrText } from "./components/icons/LeecharrText";
import {
  DashboardIcon,
  TorrentIcon,
  SettingsIcon,
  SystemIcon,
} from "./components/icons/NavIcons";
import { ActivityIcon } from "./components/icons/UIIcons";
import {
  ScheduleIcon,
  SearchIcon,
  PeerMapIcon,
  StatsIcon,
  HistoryIcon,
} from "./components/icons/AppIcons";
import { Dashboard } from "./pages/Dashboard";
import { TorrentIndex } from "./pages/TorrentIndex";
import { SpeedSchedule } from "./pages/SpeedSchedule";
import { Indexers } from "./pages/Indexers";
import { Settings } from "./pages/Settings";
import SystemStatus from "./pages/SystemStatus";
import SystemResources from "./pages/SystemResources";
import Activity from "./pages/Activity";
import DownloadHistory from "./pages/DownloadHistory";
import AddTorrentPage from "./pages/AddTorrentPage";
import PeerMap from "./pages/PeerMap";
import Statistics from "./pages/Statistics";
import SystemTasks from "./pages/SystemTasks";
import SystemBackup from "./pages/SystemBackup";
import SystemUpdates from "./pages/SystemUpdates";
import SystemEvents from "./pages/SystemEvents";
import SystemLogs from "./pages/SystemLogs";
import SystemNetwork from "./pages/SystemNetwork";
import { ApiDocsPage } from "./pages/ApiDocsPage";
import TrackerBoost from "./pages/TrackerBoost";
import { TerminalPage } from "./pages/TerminalPage";
import { FileBrowser } from "./pages/FileBrowser";
import { LoginPage } from "./pages/LoginPage";
import { StatusBar } from "./components/StatusBar";
import { IndexerSearchModal } from "./components/IndexerSearchModal";
import { AddTorrentModal } from "./components/AddTorrentModal";
import { AiCopilotDrawer } from "./components/AiCopilotDrawer";
import ToastContainer from "./components/Toast";
import { useToast } from "./context/ToastContext";
import {
  GettingStartedModal,
  STORAGE_KEY_HIDE_GUIDE,
} from "./components/GettingStartedModal";
import {
  SETTINGS_GROUPS,
  LEGACY_SETTINGS_MAP,
} from "./pages/settings/settingsNavData";
import { useSettingsDirty } from "./pages/settings/SettingsDirtyContext";
import { ErrorBoundary } from "./components/ErrorBoundary";
import "./App.css";
import { LanguageSelector } from "./components/LanguageSelector";
import { useTranslation } from "./i18n";

function getSystemSubItems(t: any) {
  return [
    { id: "status", label: t("system.status") },
    { id: "resources", label: t("system.resources") },
    { id: "terminal", label: t("system.terminal") },
    { id: "tasks", label: t("system.tasks") },
    { id: "backup", label: t("system.backup") },
    { id: "updates", label: t("system.updates") },
    { id: "events", label: t("system.events") },
    { id: "logs", label: t("system.logs") },
    { id: "network", label: t("system.network") },
    { id: "api", label: t("system.apiReference") },
  ];
}

export function App() {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();

  const { data: torrents = [] } = useTorrents();
  const { data: categories = [] } = useCategories();
  const [connected, setConnected] = useState<boolean>(false);
  const [isReconnecting, setIsReconnecting] = useState<boolean>(false);
  const [currentUser, setCurrentUser] = useState<
    import("./api/types").CurrentUser | null
  >(null);

  const queryClient = useQueryClient();

  const { data: indexersList } = useIndexers();
  const { data: generalConfig } = useGeneralConfig();

  useEffect(() => {
    const applyTheme = () => {
      let theme = generalConfig?.themeStyle || "dark";
      if (theme === "system") {
        theme = window.matchMedia("(prefers-color-scheme: light)").matches
          ? "light"
          : "dark";
      }
      const accent = generalConfig?.colorScheme || "auto";
      document.documentElement.setAttribute("data-theme", theme);
      document.documentElement.setAttribute("data-accent", accent);
    };

    applyTheme();

    if (generalConfig?.themeStyle === "system") {
      const mediaQuery = window.matchMedia("(prefers-color-scheme: light)");
      const handler = () => applyTheme();
      mediaQuery.addEventListener("change", handler);
      return () => mediaQuery.removeEventListener("change", handler);
    }
  }, [generalConfig?.themeStyle, generalConfig?.colorScheme]);

  const loadUser = async () => {
    try {
      const user = await api.getCurrentUser();
      setCurrentUser(user);
    } catch {
      // Auth might not be enabled or user not logged in
    }
  };

  useEffect(() => {
    loadUser();
  }, []);

  const handleLogout = async () => {
    try {
      await api.logout();
      setCurrentUser(null);
      navigate("/login");
    } catch (err) {
      console.error("Logout failed", err);
    }
  };

  // Modals state
  const [showAddModal, setShowAddModal] = useState<boolean>(false);
  const [showSearchModal, setShowSearchModal] = useState<boolean>(false);
  const [showGettingStartedModal, setShowGettingStartedModal] =
    useState<boolean>(() => {
      return localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) !== "true";
    });
  const [openSettingsGroups, setOpenSettingsGroups] = useState<
    Record<string, boolean>
  >({});
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState<boolean>(() => {
    return localStorage.getItem("leecharr_sidebar_collapsed") === "true";
  });

  const toggleSidebar = () => {
    setIsSidebarCollapsed((prev) => {
      const next = !prev;
      localStorage.setItem("leecharr_sidebar_collapsed", String(next));
      return next;
    });
  };

  const pathname = location.pathname;

  // Determine active top-level nav & sub-nav from URL path
  let activeNav = "dashboard";
  let activeSubNav = "";

  if (pathname === "/" || pathname === "/dashboard") {
    activeNav = "dashboard";
  } else if (pathname.startsWith("/torrents")) {
    activeNav = "torrents";
    if (pathname.includes("/add")) activeSubNav = "add";
    else activeSubNav = "all";
  } else if (pathname.startsWith("/activity")) {
    activeNav = "activity";
    if (pathname.includes("/history")) activeSubNav = "history";
    else if (pathname.includes("/metrics")) activeSubNav = "metrics";
    else activeSubNav = "history";
  } else if (pathname.startsWith("/peermap")) {
    activeNav = "peermap";
  } else if (pathname.startsWith("/schedule")) {
    activeNav = "schedule";
  } else if (pathname.startsWith("/statistics")) {
    activeNav = "statistics";
  } else if (
    pathname.startsWith("/indexers") ||
    pathname.startsWith("/search")
  ) {
    activeNav = "indexers";
  } else if (
    pathname.startsWith("/trackerboost") ||
    pathname.startsWith("/boost") ||
    pathname.startsWith("/downloadplusplus")
  ) {
    activeNav = "trackerboost";
  } else if (pathname.startsWith("/settings")) {
    activeNav = "settings";
    const section = (pathname.split("/")[2] || "host").toLowerCase();
    const legacy = LEGACY_SETTINGS_MAP[section];
    if (legacy) {
      activeSubNav = legacy.pageId;
    } else {
      let foundPageId = "host";
      for (const g of SETTINGS_GROUPS) {
        if (g.id === section) {
          foundPageId = g.pages[0].id;
          break;
        }
        const p = g.pages.find((page) => page.id === section);
        if (p) {
          foundPageId = p.id;
          break;
        }
      }
      activeSubNav = foundPageId;
    }
  } else if (pathname.startsWith("/terminal")) {
    activeNav = "terminal";
  } else if (pathname.startsWith("/files")) {
    activeNav = "files";
  } else if (pathname.startsWith("/system")) {
    activeNav = "system";
    const parts = pathname.split("/");
    activeSubNav = parts[2] || "status";
  }

  const refreshServerData = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ["torrents"] });
    queryClient.invalidateQueries({ queryKey: ["categories"] });
  }, [queryClient]);

  const { showToast } = useToast();
  const { confirmIfDirty } = useSettingsDirty();

  const guardedNavigate = useCallback(
    (to: string) => {
      confirmIfDirty(() => navigate(to));
    },
    [confirmIfDirty, navigate],
  );

  useEffect(() => {
    const unsubReconnecting = signalRManager.onReconnecting(() => {
      setConnected(false);
      setIsReconnecting(true);
    });

    const unsubReconnected = signalRManager.onReconnected(() => {
      setConnected(true);
      setIsReconnecting(false);
      refreshServerData();
    });

    const unsubClose = signalRManager.onClose(() => {
      setConnected(false);
      setIsReconnecting(true);
    });

    signalRManager
      .start()
      .then(() => {
        if (signalRManager.isConnected()) {
          setConnected(true);
          setIsReconnecting(false);
        }
      })
      .catch((err) => {
        console.warn("SignalR start error:", err);
        setConnected(false);
        setIsReconnecting(true);
      });

    const unsubscribe = signalRManager.subscribe((msg) => {
      if (msg.name === "speedPulse") {
        if (msg.body) {
          const updates: any[] = Array.isArray(msg.body)
            ? msg.body
            : Array.isArray((msg.body as any).torrents)
              ? (msg.body as any).torrents
              : typeof (msg.body as any).id === "number"
                ? [msg.body]
                : typeof msg.body === "object"
                  ? Object.entries(msg.body).map(
                      ([id, data]: [string, any]) => ({
                        id: Number(id) || data?.id,
                        ...(typeof data === "object" ? data : {}),
                      }),
                    )
                  : [];

          if (updates.length > 0) {
            useTorrentStore.getState().updateTelemetry(updates);
          }
        }
        return;
      }

      if (msg.name === "pieceMapUpdated") {
        if (msg.body) {
          const body = msg.body as any;
          const tid = Number(body.torrentId || body.id);
          if (tid) {
            useTorrentStore.getState().updatePieceMap(tid, body);
          }
        }
        return;
      }

      if (
        msg.name === "torrent" ||
        msg.name === "torrentAdded" ||
        msg.name === "torrentUpdated" ||
        msg.name === "torrentDeleted" ||
        msg.name === "category" ||
        msg.name === "categoryAdded" ||
        msg.name === "categoryUpdated" ||
        msg.name === "categoryDeleted" ||
        msg.name === "subsystemSwitched"
      ) {
        if (
          msg.name === "torrentDeleted" ||
          (msg.name === "torrent" && (msg.action as unknown) === "Deleted")
        ) {
          const body = msg.body as any;
          if (Array.isArray(body)) {
            for (const item of body) {
              const tid = Number(
                typeof item === "object" && item !== null
                  ? (item.id ?? item.torrentId)
                  : item,
              );
              if (!Number.isNaN(tid) && tid > 0) {
                useTorrentStore.getState().removeTorrent(tid);
              }
            }
          } else if (body !== undefined && body !== null) {
            const tid = Number(
              typeof body === "object" ? (body.id ?? body.torrentId) : body,
            );
            if (!Number.isNaN(tid) && tid > 0) {
              useTorrentStore.getState().removeTorrent(tid);
            }
          }
        }
        refreshServerData();
      }
    });

    return () => {
      unsubscribe();
      unsubReconnecting();
      unsubReconnected();
      unsubClose();
    };
  }, [refreshServerData]);

  const handlePause = async (id: number) => {
    try {
      await api.pauseTorrent(id);
      showToast("Torrent paused", "info");
      refreshServerData();
    } catch (err: any) {
      showToast(err?.message || "Failed to pause torrent", "error");
    }
  };

  const handleResume = async (id: number) => {
    try {
      await api.resumeTorrent(id);
      showToast("Torrent resumed", "success");
      refreshServerData();
    } catch (err: any) {
      showToast(err?.message || "Failed to resume torrent", "error");
    }
  };

  const handleDelete = async (
    payload: { id: number; deleteFiles?: boolean } | number,
  ) => {
    const id = typeof payload === "number" ? payload : payload.id;
    const deleteFiles =
      typeof payload === "number" ? false : Boolean(payload.deleteFiles);

    try {
      await api.deleteTorrent(id, deleteFiles);
      useTorrentStore.getState().removeTorrent(id);
      showToast(
        deleteFiles ? "Torrent and files deleted" : "Torrent removed",
        "info",
      );
      refreshServerData();
    } catch (err: any) {
      showToast(err?.message || "Failed to delete torrent", "error");
    }
  };

  if (pathname === "/login") {
    return (
      <ErrorBoundary title={t("errors.login")}>
        <LoginPage
          onLoginSuccess={() => {
            loadUser();
            navigate("/");
          }}
        />
      </ErrorBoundary>
    );
  }

  return (
    <div
      className={`app nav-${activeNav} ${isSidebarCollapsed ? "sidebar-collapsed" : ""}`}
    >
      {/* Sidebar Navigation */}
      <aside className={`sidebar sidebar-${activeNav}`}>
        <div
          className="sidebar-logo"
          onClick={() => guardedNavigate("/")}
          style={{ cursor: "pointer", position: "relative" }}
        >
          <button
            type="button"
            className="sidebar-collapse-btn"
            onClick={(e) => {
              e.stopPropagation();
              toggleSidebar();
            }}
            title={t(
              isSidebarCollapsed ? "nav.expandMenu" : "nav.collapseMenu",
            )}
          >
            «
          </button>
          <LeecharrLogo size={86} className="brand-logo" />
          <LeecharrText width={120} className="brand-text" />
        </div>

        <nav className="sidebar-nav">
          {/* Dashboard */}
          <div
            className={`sidebar-nav-item ${activeNav === "dashboard" ? "active" : ""}`}
            onClick={() => guardedNavigate("/")}
            style={{ cursor: "pointer" }}
          >
            <DashboardIcon size={16} />
            <span>{t("nav.dashboard")}</span>
          </div>

          {/* Torrents (Primary Client / Transfers) */}
          <div
            className={`sidebar-nav-item ${activeNav === "torrents" ? "active" : ""}`}
            onClick={() => guardedNavigate("/torrents")}
            style={{ cursor: "pointer" }}
          >
            <TorrentIcon size={16} />
            <span>{t("nav.torrents")}</span>
          </div>
          {activeNav === "torrents" && (
            <>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "all" ? "active" : ""}`}
                onClick={() => guardedNavigate("/torrents")}
                style={{ cursor: "pointer" }}
              >
                <DashboardIcon size={14} /> <span>{t("nav.torrents")}</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "add" ? "active" : ""}`}
                onClick={() => guardedNavigate("/torrents/add")}
                style={{ cursor: "pointer" }}
              >
                <span style={{ fontSize: "1.1rem", lineHeight: 1 }}>+</span>{" "}
                <span>{t("modals.addTorrent")}</span>
              </div>
            </>
          )}

          {/* Activity (History & Real-time Metrics) */}
          <div
            className={`sidebar-nav-item ${activeNav === "activity" ? "active" : ""}`}
            onClick={() => guardedNavigate("/activity/history")}
            style={{ cursor: "pointer" }}
          >
            <ActivityIcon size={16} />
            <span>{t("nav.activity")}</span>
          </div>
          {activeNav === "activity" && (
            <>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "history" ? "active" : ""}`}
                onClick={() => guardedNavigate("/activity/history")}
                style={{ cursor: "pointer" }}
              >
                <HistoryIcon /> <span>{t("nav.history")}</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "metrics" ? "active" : ""}`}
                onClick={() => guardedNavigate("/activity/metrics")}
                style={{ cursor: "pointer" }}
              >
                <StatsIcon size={14} /> <span>{t("nav.stats")}</span>
              </div>
            </>
          )}

          {/* Indexer Search & Discovery */}
          <div
            className={`sidebar-nav-item ${activeNav === "indexers" ? "active" : ""}`}
            onClick={() => guardedNavigate("/indexers")}
            style={{ cursor: "pointer" }}
          >
            <SearchIcon size={16} />
            <span>{t("nav.indexers")}</span>
          </div>

          {/* Peer Map */}
          <div
            className={`sidebar-nav-item ${activeNav === "peermap" ? "active" : ""}`}
            onClick={() => guardedNavigate("/peermap")}
            style={{ cursor: "pointer" }}
          >
            <PeerMapIcon size={16} />
            <span>{t("nav.peerMap")}</span>
          </div>

          {/* Schedule */}
          <div
            className={`sidebar-nav-item ${activeNav === "schedule" ? "active" : ""}`}
            onClick={() => guardedNavigate("/schedule")}
            style={{ cursor: "pointer" }}
          >
            <ScheduleIcon size={16} />
            <span>{t("settings.schedule")}</span>
          </div>

          {/* Statistics */}
          <div
            className={`sidebar-nav-item ${activeNav === "statistics" ? "active" : ""}`}
            onClick={() => guardedNavigate("/statistics")}
            style={{ cursor: "pointer" }}
          >
            <StatsIcon size={16} />
            <span>{t("activity.statistics")}</span>
          </div>

          {/* Tracker Boost */}
          <div
            className={`sidebar-nav-item ${activeNav === "trackerboost" ? "active" : ""}`}
            onClick={() => guardedNavigate("/trackerboost")}
            style={{ cursor: "pointer" }}
            title="Tracker Boost Swarm Optimization & Discovery"
          >
            <span
              style={{
                fontSize: "1.05rem",
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                width: "16px",
              }}
            >
              ⚡
            </span>
            <span>{t("nav.trackerBoost")}</span>
          </div>

          {/* Terminal CLI */}
          <div
            className={`sidebar-nav-item ${activeNav === "terminal" ? "active" : ""}`}
            onClick={() => guardedNavigate("/terminal")}
            style={{ cursor: "pointer" }}
            title="Interactive Download Shell & File Inspector"
          >
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                width: "16px",
                fontFamily: "monospace",
                fontSize: "0.85rem",
                fontWeight: "bold",
              }}
            >
              &gt;_
            </span>
            <span>{t("nav.terminal")}</span>
          </div>

          {/* File Browser */}
          <div
            className={`sidebar-nav-item ${activeNav === "files" ? "active" : ""}`}
            onClick={() => guardedNavigate("/files")}
            style={{ cursor: "pointer" }}
            title={t("nav.browseFiles")}
          >
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                width: "16px",
                fontSize: "1rem",
              }}
            >
              📁
            </span>
            <span>{t("nav.fileBrowser")}</span>
          </div>

          {/* Settings */}
          <div
            className={`sidebar-nav-item ${activeNav === "settings" ? "active-parent" : ""}`}
            onClick={() => guardedNavigate("/settings/host")}
            style={{ cursor: "pointer" }}
          >
            <SettingsIcon size={16} />
            <span>{t("nav.settings")}</span>
          </div>
          {activeNav === "settings" && (
            <div className="sidebar-settings-tree">
              {SETTINGS_GROUPS.map((group) => {
                const isGroupActive = group.pages.some(
                  (p) => p.id === activeSubNav,
                );
                const isOpen = openSettingsGroups[group.id] ?? isGroupActive;
                return (
                  <div key={group.id} className="sidebar-group-container">
                    <div
                      className="sidebar-group-header"
                      onClick={(e) => {
                        e.stopPropagation();
                        setOpenSettingsGroups((prev) => ({
                          ...prev,
                          [group.id]: !isOpen,
                        }));
                      }}
                      title={`Toggle ${t(group.title)}`}
                    >
                      <span
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        <span>{group.icon}</span>
                        <span>{t(group.shortLabel)}</span>
                      </span>
                      <span
                        className={`sidebar-group-chevron ${isOpen ? "open" : ""}`}
                      >
                        ▶
                      </span>
                    </div>
                    {isOpen &&
                      group.pages.map((page) => {
                        const isPageActive = activeSubNav === page.id;
                        return (
                          <div
                            key={page.id}
                            className={`sidebar-settings-subitem ${isPageActive ? "active" : ""}`}
                            onClick={() =>
                              guardedNavigate(`/settings/${page.id}`)
                            }
                            title={t(page.description)}
                          >
                            <span
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.45rem",
                                overflow: "hidden",
                                textOverflow: "ellipsis",
                                whiteSpace: "nowrap",
                              }}
                            >
                              <span
                                style={{
                                  fontSize: "0.85rem",
                                  flexShrink: 0,
                                }}
                              >
                                {page.icon}
                              </span>
                              <span
                                style={{
                                  overflow: "hidden",
                                  textOverflow: "ellipsis",
                                }}
                              >
                                {t(page.shortLabel)}
                              </span>
                            </span>
                            {page.badge && (
                              <span
                                className="sidebar-badge"
                                style={{
                                  backgroundColor: isPageActive
                                    ? "var(--accent)"
                                    : "rgba(255,255,255,0.06)",
                                  color: isPageActive
                                    ? "#10111a"
                                    : "var(--text-muted)",
                                }}
                              >
                                {t(page.badge)}
                              </span>
                            )}
                          </div>
                        );
                      })}
                  </div>
                );
              })}
            </div>
          )}

          {/* System */}
          <div
            className={`sidebar-nav-item ${activeNav === "system" ? "active" : ""}`}
            onClick={() => guardedNavigate("/system/status")}
            style={{ cursor: "pointer" }}
          >
            <SystemIcon size={16} />
            <span>{t("nav.system")}</span>
          </div>
          {activeNav === "system" &&
            getSystemSubItems(t).map((item) => (
              <div
                key={item.id}
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === item.id ? "active" : ""}`}
                onClick={() => guardedNavigate(`/system/${item.id}`)}
                style={{ cursor: "pointer" }}
              >
                <span>{item.label}</span>
              </div>
            ))}
        </nav>
      </aside>

      {/* Main Content Area */}
      <div className="main-wrapper">
        {/* Topbar Header */}
        <header className="topbar">
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <button
              type="button"
              className="topbar-btn sidebar-toggle-btn"
              onClick={toggleSidebar}
              title={
                isSidebarCollapsed
                  ? "Show Main Menu (Alt+M)"
                  : "Hide Main Menu (Alt+M)"
              }
              style={{
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                width: "28px",
                height: "28px",
                border:
                  "1px solid var(--border-light, rgba(255, 255, 255, 0.12))",
                borderRadius: "4px",
                background: isSidebarCollapsed
                  ? "var(--accent, #ffd166)"
                  : "transparent",
                color: isSidebarCollapsed ? "#10111a" : "var(--text-secondary)",
                cursor: "pointer",
                fontSize: "0.95rem",
                padding: 0,
              }}
            >
              {isSidebarCollapsed ? "☰" : "⮜"}
            </button>
            <div
              className="topbar-search"
              onClick={() => setShowSearchModal(true)}
              style={{ cursor: "pointer" }}
              title={t("nav.searchPlaceholder")}
            >
              <SearchIcon size={14} />
              <input
                type="text"
                placeholder={`${t("common.search")}...`}
                className="topbar-search-input"
                readOnly
                style={{ cursor: "pointer" }}
              />
              <kbd
                style={{
                  backgroundColor: "rgba(255, 255, 255, 0.08)",
                  border: "1px solid rgba(255, 255, 255, 0.16)",
                  borderRadius: "3px",
                  padding: "0.1rem 0.4rem",
                  fontSize: "0.7rem",
                  color: "var(--text-muted)",
                  fontFamily: "monospace",
                }}
              >
                Ctrl+K
              </kbd>
            </div>
          </div>

          <div
            className="topbar-actions"
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <LanguageSelector />
            <button
              className="btn btn-small"
              onClick={() => setShowGettingStartedModal(true)}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                backgroundColor: "rgba(255, 209, 102, 0.12)",
                color: "var(--accent, #ffd166)",
                border: "1px solid rgba(255, 209, 102, 0.3)",
                fontWeight: 600,
              }}
              title={t("nav.gettingStarted")}
            >
              🚀 {t("modals.gettingStarted")}
            </button>
            <button
              className="btn btn-small"
              onClick={() => guardedNavigate("/terminal")}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.35rem",
                backgroundColor: "rgba(56, 189, 248, 0.12)",
                color: "#38bdf8",
                border: "1px solid rgba(56, 189, 248, 0.3)",
                fontWeight: 600,
                fontFamily: "monospace",
              }}
              title={t("nav.openTerminal")}
            >
              {t("nav.cli")}
            </button>
            <button
              className="btn btn-small btn-success"
              onClick={() => setShowAddModal(true)}
            >
              + {t("modals.addTorrent")}
            </button>

            {currentUser?.isAuthenticated && (
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  marginLeft: "0.5rem",
                  borderLeft: "1px solid var(--border)",
                  paddingLeft: "0.75rem",
                }}
              >
                <div
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                    width: "28px",
                    height: "28px",
                    borderRadius: "50%",
                    backgroundColor: "#23284B",
                    color: "#FFD166",
                    fontSize: "12px",
                    fontWeight: 600,
                    border: "1px solid rgba(255, 209, 102, 0.3)",
                  }}
                >
                  {currentUser.displayName
                    ? currentUser.displayName.charAt(0).toUpperCase()
                    : currentUser.username.charAt(0).toUpperCase()}
                </div>
                <span
                  style={{
                    fontSize: "0.85rem",
                    color: "var(--text-primary)",
                    fontWeight: 500,
                  }}
                >
                  {currentUser.displayName || currentUser.username}
                </span>
                <button
                  className="btn btn-small btn-outline"
                  onClick={handleLogout}
                  style={{ fontSize: "0.75rem", padding: "3px 8px" }}
                  title={t("nav.signOut")}
                >
                  {t("nav.signOut")}
                </button>
              </div>
            )}
          </div>
        </header>

        {/* Reconnecting State Notification Banner */}
        {isReconnecting && (
          <div
            className="reconnecting-banner"
            role="status"
            aria-live="polite"
            style={{
              backgroundColor: "rgba(224, 168, 46, 0.15)",
              borderBottom: "1px solid rgba(224, 168, 46, 0.35)",
              color: "#ffd166",
              padding: "0.45rem 1rem",
              fontSize: "0.85rem",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              gap: "0.6rem",
              fontWeight: 500,
            }}
          >
            <span
              style={{
                display: "inline-block",
                width: "8px",
                height: "8px",
                borderRadius: "50%",
                backgroundColor: "#ffd166",
                boxShadow: "0 0 6px #ffd166",
              }}
            />
            <span>{t("alerts.connectionLost")}</span>
          </div>
        )}

        {/* Declarative React Router Viewport */}
        <main className="app-main">
          <ErrorBoundary title={t("errors.view")}>
            <Routes>
              {/* Dashboard */}
              <Route
                path="/"
                element={
                  <ErrorBoundary title={t("errors.dashboard")}>
                    <div className="content-area">
                      <Dashboard
                        torrents={torrents}
                        onNavigateTorrents={() => guardedNavigate("/torrents")}
                        onNavigateSettings={(tab) =>
                          guardedNavigate(`/settings/${tab}`)
                        }
                      />
                    </div>
                  </ErrorBoundary>
                }
              />
              <Route path="/dashboard" element={<Navigate to="/" replace />} />

              {/* Torrents (Primary Client) */}
              <Route
                path="/torrents"
                element={
                  <ErrorBoundary title={t("errors.torrents")}>
                    <TorrentIndex
                      torrents={torrents}
                      onPause={handlePause}
                      onResume={handleResume}
                      onDelete={handleDelete}
                      onOpenAddModal={() => setShowAddModal(true)}
                      onOpenSearchModal={() => setShowSearchModal(true)}
                      onNavigateTab={(nav, subNav) => {
                        if (nav === "settings")
                          guardedNavigate(`/settings/${subNav || "general"}`);
                        else if (nav === "system")
                          guardedNavigate(`/system/${subNav || "status"}`);
                        else if (subNav) guardedNavigate(`/${nav}/${subNav}`);
                        else guardedNavigate(`/${nav}`);
                      }}
                    />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/torrents/add"
                element={
                  <ErrorBoundary title={t("errors.addTorrent")}>
                    <AddTorrentPage
                      onSuccess={() => {
                        navigate("/torrents");
                        refreshServerData();
                      }}
                    />
                  </ErrorBoundary>
                }
              />

              {/* Activity Hub */}
              <Route
                path="/activity"
                element={<Navigate to="/activity/history" replace />}
              />
              <Route
                path="/activity/torrents"
                element={<Navigate to="/torrents" replace />}
              />
              <Route
                path="/activity/add"
                element={<Navigate to="/torrents/add" replace />}
              />
              <Route
                path="/activity/history"
                element={
                  <ErrorBoundary title={t("errors.downloadHistory")}>
                    <DownloadHistory />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/history"
                element={<Navigate to="/activity/history" replace />}
              />
              <Route
                path="/activity/metrics"
                element={
                  <ErrorBoundary title={t("errors.activity")}>
                    <Activity />
                  </ErrorBoundary>
                }
              />

              {/* Indexers */}
              <Route
                path="/indexers"
                element={
                  <ErrorBoundary title={t("errors.indexers")}>
                    <Indexers />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/search"
                element={<Navigate to="/indexers" replace />}
              />

              {/* Operational Visualizations */}
              <Route
                path="/peermap"
                element={
                  <ErrorBoundary title={t("errors.peerMap")}>
                    <PeerMap />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/schedule"
                element={
                  <ErrorBoundary title={t("errors.speedSchedule")}>
                    <SpeedSchedule />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/statistics"
                element={
                  <ErrorBoundary title={t("errors.statistics")}>
                    <Statistics />
                  </ErrorBoundary>
                }
              />

              {/* Tracker Boost */}
              <Route
                path="/trackerboost"
                element={
                  <ErrorBoundary title={t("errors.trackerBoost")}>
                    <TrackerBoost />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/boost"
                element={<Navigate to="/trackerboost" replace />}
              />
              <Route
                path="/downloadplusplus"
                element={<Navigate to="/trackerboost" replace />}
              />

              {/* Settings */}
              <Route
                path="/settings"
                element={<Navigate to="/settings/general" replace />}
              />
              <Route
                path="/settings/:section"
                element={
                  <ErrorBoundary title={t("errors.settings")}>
                    <Settings />
                  </ErrorBoundary>
                }
              />

              {/* System Diagnostics & Maintenance */}
              <Route
                path="/system"
                element={<Navigate to="/system/status" replace />}
              />
              <Route
                path="/system/status"
                element={
                  <ErrorBoundary title={t("errors.systemStatus")}>
                    <SystemStatus />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/resources"
                element={
                  <ErrorBoundary title={t("errors.systemResources")}>
                    <SystemResources />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/telemetry"
                element={<Navigate to="/system/resources" replace />}
              />
              <Route
                path="/system/tasks"
                element={
                  <ErrorBoundary title={t("errors.systemTasks")}>
                    <SystemTasks />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/backup"
                element={
                  <ErrorBoundary title={t("errors.systemBackup")}>
                    <SystemBackup />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/updates"
                element={
                  <ErrorBoundary title={t("errors.systemUpdates")}>
                    <SystemUpdates />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/events"
                element={
                  <ErrorBoundary title={t("errors.systemEvents")}>
                    <SystemEvents />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/logs"
                element={
                  <ErrorBoundary title={t("errors.systemLogs")}>
                    <SystemLogs />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/network"
                element={
                  <ErrorBoundary title={t("errors.systemNetwork")}>
                    <SystemNetwork />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/api"
                element={
                  <ErrorBoundary title={t("errors.apiReference")}>
                    <ApiDocsPage />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/api-docs"
                element={<Navigate to="/system/api" replace />}
              />
              <Route
                path="/system/swagger"
                element={<Navigate to="/system/api" replace />}
              />
              <Route
                path="/api-docs"
                element={<Navigate to="/system/api" replace />}
              />

              {/* Terminal CLI */}
              <Route
                path="/terminal"
                element={
                  <ErrorBoundary title={t("errors.terminal")}>
                    <TerminalPage />
                  </ErrorBoundary>
                }
              />
              <Route
                path="/system/terminal"
                element={
                  <ErrorBoundary title={t("errors.terminal")}>
                    <TerminalPage />
                  </ErrorBoundary>
                }
              />

              {/* File Browser */}
              <Route
                path="/files"
                element={
                  <ErrorBoundary title={t("errors.fileBrowser")}>
                    <FileBrowser />
                  </ErrorBoundary>
                }
              />

              {/* Fallback */}
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </ErrorBoundary>
        </main>

        {/* Bottom Status Bar */}
        <StatusBar connected={connected} isReconnecting={isReconnecting} />
      </div>

      {/* Add Torrent Modal */}
      {showAddModal && (
        <ErrorBoundary title={t("errors.addTorrentModal")}>
          <AddTorrentModal
            isOpen={showAddModal}
            onClose={() => setShowAddModal(false)}
            onSuccess={() => {
              setShowAddModal(false);
              refreshServerData();
            }}
          />
        </ErrorBoundary>
      )}

      {/* Indexer Search Modal */}
      {showSearchModal && (
        <ErrorBoundary title={t("errors.searchModal")}>
          <IndexerSearchModal
            onClose={() => setShowSearchModal(false)}
            onTorrentAdded={refreshServerData}
          />
        </ErrorBoundary>
      )}

      {/* Getting Started & Setup Guide Modal */}
      <ErrorBoundary title={t("errors.setupGuide")}>
        <GettingStartedModal
          isOpen={showGettingStartedModal}
          onClose={() => setShowGettingStartedModal(false)}
          onNavigateSettings={(tab) => guardedNavigate(`/settings/${tab}`)}
          onNavigateTorrents={() => guardedNavigate("/torrents")}
          onNavigateIndexers={() => guardedNavigate("/indexers")}
        />
      </ErrorBoundary>

      {/* Discrete Collapsible AI Copilot Drawer */}
      <ErrorBoundary title={t("errors.copilotDrawer")}>
        <AiCopilotDrawer />
      </ErrorBoundary>

      {/* Global Floating Toast Notifications */}
      <ToastContainer />
    </div>
  );
}

export default App;
