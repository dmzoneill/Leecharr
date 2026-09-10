import React, { useEffect, useState, useCallback, useRef } from "react";
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
  FolderIcon,
  TrackerBoostIcon,
  TerminalIcon,
} from "./components/icons/NavIcons";
import { ActivityIcon } from "./components/icons/UIIcons";
import {
  ScheduleIcon,
  SearchIcon,
  PeerMapIcon,
  StatsIcon,
  HistoryIcon,
  MenuIcon,
  ChevronsLeftIcon,
  ChevronsRightIcon,
  SunIcon,
  MoonIcon,
  HeartIcon,
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
import { useTheme } from "./context/ThemeContext";
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
import { getErrorMessage } from "./utils/errorUtils";

function getSystemSubItems(t: (key: string) => string) {
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
    } catch (_err: unknown) {
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
    } catch (err: unknown) {
      console.error("Logout failed", getErrorMessage(err));
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
  const [showProfileMenu, setShowProfileMenu] = useState<boolean>(false);
  const profileMenuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (
        profileMenuRef.current &&
        !profileMenuRef.current.contains(event.target as Node)
      ) {
        setShowProfileMenu(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

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
    queryClient.invalidateQueries({ queryKey: ["speedschedule"] });
    queryClient.invalidateQueries({ queryKey: ["subsystems"] });
    queryClient.invalidateQueries({ queryKey: ["config"] });
    queryClient.invalidateQueries({ queryKey: ["torrentengine"] });
    queryClient.invalidateQueries({ queryKey: ["health"] });
    queryClient.invalidateQueries({ queryKey: ["system", "status"] });
    queryClient.invalidateQueries({ queryKey: ["diskspace"] });
  }, [queryClient]);

  const { showToast } = useToast();
  const { theme, toggleTheme } = useTheme();
  const { confirmIfDirty } = useSettingsDirty();
  const [showTopApiKey, setShowTopApiKey] = useState(false);
  const [unmaskedTopApiKey, setUnmaskedTopApiKey] = useState<string | null>(null);

  const fetchUnmaskedTopKey = useCallback(async () => {
    if (unmaskedTopApiKey) return unmaskedTopApiKey;
    try {
      const res = await api.getApiKey();
      if (res?.apiKey) {
        setUnmaskedTopApiKey(res.apiKey);
        return res.apiKey;
      }
    } catch {
      // Fallback
    }
    return generalConfig?.apiKey || "";
  }, [generalConfig?.apiKey, unmaskedTopApiKey]);

  const handleCopyApiKey = useCallback(async () => {
    try {
      let keyToCopy = unmaskedTopApiKey;
      if (!keyToCopy || keyToCopy.includes("*")) {
        keyToCopy = await fetchUnmaskedTopKey();
      }
      if (keyToCopy && !keyToCopy.includes("*")) {
        await navigator.clipboard.writeText(keyToCopy);
        showToast(
          t("settings.apiKeyCopied", "API key copied to clipboard"),
          "success",
        );
      } else {
        showToast(
          t("settings.failedToCopyApiKey", "Failed to copy API key to clipboard"),
          "error",
        );
      }
    } catch {
      showToast(
        t("settings.failedToCopyApiKey", "Failed to copy API key to clipboard"),
        "error",
      );
    }
  }, [fetchUnmaskedTopKey, showToast, t, unmaskedTopApiKey]);

  const handleMouseEnterTopKey = useCallback(async () => {
    setShowTopApiKey(true);
    if (!unmaskedTopApiKey) {
      await fetchUnmaskedTopKey();
    }
  }, [fetchUnmaskedTopKey, unmaskedTopApiKey]);

  const handleMouseLeaveTopKey = useCallback(() => {
    setShowTopApiKey(false);
  }, []);

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
      useTorrentStore.getState().clearTelemetry();
    });

    const unsubReconnected = signalRManager.onReconnected(() => {
      setConnected(true);
      setIsReconnecting(false);
      useTorrentStore.getState().clearTelemetry();
      refreshServerData();
    });

    const unsubClose = signalRManager.onClose(() => {
      setConnected(false);
      setIsReconnecting(true);
      useTorrentStore.getState().clearTelemetry();
    });

    const staleInterval = setInterval(() => {
      useTorrentStore.getState().purgeStaleTelemetry(8000);
    }, 4000);

    signalRManager
      .start()
      .then(() => {
        if (signalRManager.isConnected()) {
          setConnected(true);
          setIsReconnecting(false);
        }
      })
      .catch((err: unknown) => {
        console.warn("SignalR start error:", getErrorMessage(err));
        setConnected(false);
        setIsReconnecting(true);
      });

    const unsubscribe = signalRManager.subscribe((msg) => {
      if (msg.name === "speedPulse") {
        if (msg.body) {
          const body = msg.body as
            | Array<{ id: number; [key: string]: unknown }>
            | {
                torrents?: Array<{ id: number; [key: string]: unknown }>;
                id?: number;
              }
            | Record<string, { id?: number; [key: string]: unknown }>;
          const updates: Array<{ id: number; [key: string]: unknown }> =
            Array.isArray(body)
              ? (body as Array<{ id: number; [key: string]: unknown }>)
              : Array.isArray(
                    (
                      body as {
                        torrents?: Array<{
                          id: number;
                          [key: string]: unknown;
                        }>;
                      }
                    ).torrents,
                  )
                ? (
                    body as {
                      torrents: Array<{ id: number; [key: string]: unknown }>;
                    }
                  ).torrents
                : typeof (body as { id?: number }).id === "number"
                  ? [body as { id: number; [key: string]: unknown }]
                  : typeof body === "object"
                    ? Object.entries(
                        body as Record<
                          string,
                          { id?: number; [key: string]: unknown }
                        >,
                      ).map(
                        ([id, data]: [
                          string,
                          { id?: number; [key: string]: unknown },
                        ]) => ({
                          id: Number(id) || data?.id || 0,
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
          const body = msg.body as { torrentId?: number; id?: number };
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
        msg.name === "speedschedule" ||
        msg.name === "speedscheduleAdded" ||
        msg.name === "speedscheduleUpdated" ||
        msg.name === "speedscheduleDeleted" ||
        msg.name === "subsystemSwitched"
      ) {
        if (
          msg.name === "torrentDeleted" ||
          (msg.name === "torrent" && (msg.action as unknown) === "Deleted")
        ) {
          const body = msg.body as unknown;
          if (Array.isArray(body)) {
            for (const item of body) {
              const tid = Number(
                typeof item === "object" && item !== null
                  ? ((item as { id?: number; torrentId?: number }).id ??
                      (item as { id?: number; torrentId?: number }).torrentId)
                  : item,
              );
              if (!Number.isNaN(tid) && tid > 0) {
                useTorrentStore.getState().removeTorrent(tid);
              }
            }
          } else if (body !== undefined && body !== null) {
            const tid = Number(
              typeof body === "object"
                ? ((body as { id?: number; torrentId?: number }).id ??
                    (body as { id?: number; torrentId?: number }).torrentId)
                : body,
            );
            if (!Number.isNaN(tid) && tid > 0) {
              useTorrentStore.getState().removeTorrent(tid);
            }
          }
        }
        if (msg.name === "subsystemSwitched") {
          const body = msg.body as { subsystemId?: string; id?: string } | null;
          const subsystemId =
            typeof body === "object" && body !== null
              ? (body.subsystemId ?? body.id)
              : undefined;
          if (subsystemId) {
            queryClient.invalidateQueries({
              queryKey: ["subsystems", subsystemId],
            });
          }
          queryClient.invalidateQueries({ queryKey: ["torrentengine"] });
        }
        refreshServerData();
      }
    });

    return () => {
      clearInterval(staleInterval);
      unsubscribe();
      unsubReconnecting();
      unsubReconnected();
      unsubClose();
    };
  }, [queryClient, refreshServerData]);

  const handlePause = async (id: number) => {
    try {
      await api.pauseTorrent(id);
      showToast("Torrent paused", "info");
      refreshServerData();
    } catch (err: unknown) {
      showToast(getErrorMessage(err, "Failed to pause torrent"), "error");
    }
  };

  const handleResume = async (id: number) => {
    try {
      await api.resumeTorrent(id);
      showToast("Torrent resumed", "success");
      refreshServerData();
    } catch (err: unknown) {
      showToast(getErrorMessage(err, "Failed to resume torrent"), "error");
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
    } catch (err: unknown) {
      showToast(getErrorMessage(err, "Failed to delete torrent"), "error");
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
            aria-label={
              isSidebarCollapsed ? "Expand sidebar" : "Collapse sidebar"
            }
          >
            {isSidebarCollapsed ? (
              <ChevronsRightIcon size={14} />
            ) : (
              <ChevronsLeftIcon size={14} />
            )}
          </button>
          <LeecharrLogo
            size={isSidebarCollapsed ? 36 : 86}
            className="brand-logo"
          />
          {!isSidebarCollapsed && (
            <LeecharrText width={120} className="brand-text" />
          )}
        </div>

        <nav className="sidebar-nav">
          {/* Dashboard */}
          <div
            className={`sidebar-nav-item ${activeNav === "dashboard" ? "active" : ""}`}
            onClick={() => guardedNavigate("/")}
            style={{ cursor: "pointer" }}
            title={t("nav.dashboard")}
          >
            <DashboardIcon size={16} />
            <span>{t("nav.dashboard")}</span>
          </div>

          {/* Torrents (Primary Client / Transfers) */}
          <div
            className={`sidebar-nav-item ${activeNav === "torrents" ? "active" : ""}`}
            onClick={() => guardedNavigate("/torrents")}
            style={{ cursor: "pointer" }}
            title={t("nav.torrents")}
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
                title={t("nav.torrents")}
              >
                <DashboardIcon size={14} /> <span>{t("nav.torrents")}</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "add" ? "active" : ""}`}
                onClick={() => guardedNavigate("/torrents/add")}
                style={{ cursor: "pointer" }}
                title={t("modals.addTorrent")}
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
            title={t("nav.activity")}
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
                title={t("nav.history")}
              >
                <HistoryIcon /> <span>{t("nav.history")}</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "metrics" ? "active" : ""}`}
                onClick={() => guardedNavigate("/activity/metrics")}
                style={{ cursor: "pointer" }}
                title={t("nav.statistics")}
              >
                <StatsIcon size={14} /> <span>{t("nav.statistics")}</span>
              </div>
            </>
          )}

          {/* Indexer Search & Discovery */}
          <div
            className={`sidebar-nav-item ${activeNav === "indexers" ? "active" : ""}`}
            onClick={() => guardedNavigate("/indexers")}
            style={{ cursor: "pointer" }}
            title={t("nav.indexers")}
          >
            <SearchIcon size={16} />
            <span>{t("nav.indexers")}</span>
          </div>

          {/* Peer Map */}
          <div
            className={`sidebar-nav-item ${activeNav === "peermap" ? "active" : ""}`}
            onClick={() => guardedNavigate("/peermap")}
            style={{ cursor: "pointer" }}
            title={t("nav.peerMap")}
          >
            <PeerMapIcon size={16} />
            <span>{t("nav.peerMap")}</span>
          </div>

          {/* Schedule */}
          <div
            className={`sidebar-nav-item ${activeNav === "schedule" ? "active" : ""}`}
            onClick={() => guardedNavigate("/schedule")}
            style={{ cursor: "pointer" }}
            title={t("nav.speedSchedule")}
          >
            <ScheduleIcon size={16} />
            <span>{t("nav.speedSchedule")}</span>
          </div>

          {/* Statistics */}
          <div
            className={`sidebar-nav-item ${activeNav === "statistics" ? "active" : ""}`}
            onClick={() => guardedNavigate("/statistics")}
            style={{ cursor: "pointer" }}
            title={t("nav.statistics")}
          >
            <StatsIcon size={16} />
            <span>{t("nav.statistics")}</span>
          </div>

          {/* Tracker Boost */}
          <div
            className={`sidebar-nav-item ${activeNav === "trackerboost" ? "active" : ""}`}
            onClick={() => guardedNavigate("/trackerboost")}
            style={{ cursor: "pointer" }}
            title="Tracker Boost Swarm Optimization & Discovery"
          >
            <TrackerBoostIcon size={16} />
            <span>{t("nav.trackerBoost")}</span>
          </div>

          {/* Terminal CLI */}
          <div
            className={`sidebar-nav-item ${activeNav === "terminal" ? "active" : ""}`}
            onClick={() => guardedNavigate("/terminal")}
            style={{ cursor: "pointer" }}
            title="Interactive Download Shell & File Inspector"
          >
            <TerminalIcon size={16} />
            <span>{t("nav.terminalCli")}</span>
          </div>

          {/* File Browser */}
          <div
            className={`sidebar-nav-item ${activeNav === "files" ? "active" : ""}`}
            onClick={() => guardedNavigate("/files")}
            style={{ cursor: "pointer" }}
            title={t("nav.browseFiles")}
          >
            <FolderIcon size={16} />
            <span>{t("nav.fileBrowser")}</span>
          </div>

          {/* Settings */}
          <div
            className={`sidebar-nav-item ${activeNav === "settings" ? "active-parent" : ""}`}
            onClick={() => guardedNavigate("/settings/host")}
            style={{ cursor: "pointer" }}
            title={t("nav.settings")}
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
            title={t("nav.system")}
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
                title={item.label}
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
                  ? "Expand sidebar (Alt+M)"
                  : "Collapse sidebar (Alt+M)"
              }
              aria-label="Toggle navigation sidebar"
              style={{
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                width: "28px",
                height: "28px",
                border: "1px solid var(--border-light, #162031)",
                borderRadius: "4px",
                background: "transparent",
                color: "var(--text-secondary)",
                cursor: "pointer",
                fontSize: "0.95rem",
                padding: 0,
              }}
            >
              <MenuIcon size={16} />
            </button>
            <div
              className="topbar-search"
              onClick={() => setShowSearchModal(true)}
              style={{ cursor: "pointer" }}
              title={t(
                "topbar.searchPlaceholder",
                "Quick Jump / Search... (Ctrl+K)",
              )}
            >
              <SearchIcon size={14} />
              <input
                type="text"
                placeholder={t(
                  "topbar.searchPlaceholder",
                  "Quick Jump / Search... (Ctrl+K)",
                )}
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
            style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}
          >
            <LanguageSelector />
            <button
              className="btn btn-small"
              onClick={() => setShowGettingStartedModal(true)}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                backgroundColor:
                  "var(--accent-bg-medium, rgba(91, 141, 239, 0.12))",
                color: "var(--accent, #5b8def)",
                border:
                  "1px solid var(--accent-border-alert, rgba(91, 141, 239, 0.3))",
                fontWeight: 600,
              }}
              title={t("nav.gettingStarted")}
            >
              🚀 {t("nav.gettingStarted")}
            </button>

            <button
              type="button"
              className="topbar-apikey-btn"
              onClick={handleCopyApiKey}
              onMouseEnter={handleMouseEnterTopKey}
              onMouseLeave={handleMouseLeaveTopKey}
              title={t(
                "apiDocs.copyApiKeyTooltip",
                "Click to copy API Key to clipboard",
              )}
            >
              <span style={{ fontSize: "0.85rem" }}>⚿</span>
              <span
                style={{
                  letterSpacing: showTopApiKey ? "0.5px" : "1px",
                  opacity: 0.85,
                  fontFamily: "monospace",
                  fontSize: "0.8rem",
                }}
              >
                {showTopApiKey
                  ? (unmaskedTopApiKey || (generalConfig?.apiKey && !generalConfig.apiKey.includes("*") ? generalConfig.apiKey : "••••••••••••••••••••••••••••••••"))
                  : "••••••••••••••••••••••••••••••••"}
              </span>
            </button>

            <button
              type="button"
              className="topbar-btn"
              onClick={toggleTheme}
              title={
                theme === "dark"
                  ? t("nav.themeLight", "Switch to Light Mode")
                  : t("nav.themeDark", "Switch to Dark Mode")
              }
              aria-label="Toggle theme"
            >
              {theme === "dark" ? <SunIcon /> : <MoonIcon />}
            </button>

            <a
              href="https://github.com/dmzoneill/Leecharr"
              target="_blank"
              rel="noreferrer"
              className="topbar-btn topbar-heart"
              title={t("nav.support", "Support & Donate")}
              aria-label="Support and Donate"
            >
              <HeartIcon size={15} />
            </a>

            {currentUser?.isAuthenticated && (
              <div
                className="topbar-user-profile"
                ref={profileMenuRef}
                style={{
                  position: "relative",
                  display: "flex",
                  alignItems: "center",
                }}
              >
                <button
                  type="button"
                  className="topbar-btn"
                  onClick={() => setShowProfileMenu(!showProfileMenu)}
                  title={currentUser.displayName || currentUser.username}
                  aria-expanded={showProfileMenu}
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                    padding: "2px",
                  }}
                >
                  <div
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      justifyContent: "center",
                      width: "24px",
                      height: "24px",
                      borderRadius: "50%",
                      backgroundColor: "var(--bg-hover-elevated, #23324c)",
                      color: "var(--accent, #5b8def)",
                      fontSize: "11px",
                      fontWeight: 700,
                      border: "1px solid var(--border, #1f2c42)",
                      overflow: "hidden",
                    }}
                  >
                    {currentUser.avatarUrl ? (
                      <img
                        src={currentUser.avatarUrl}
                        alt={currentUser.displayName || currentUser.username}
                        style={{
                          width: "100%",
                          height: "100%",
                          objectFit: "cover",
                        }}
                      />
                    ) : (
                      (currentUser.displayName || currentUser.username)
                        .charAt(0)
                        .toUpperCase()
                    )}
                  </div>
                </button>

                {showProfileMenu && (
                  <div
                    className="topbar-dropdown"
                    style={{ minWidth: "210px" }}
                    onClick={() => setShowProfileMenu(false)}
                  >
                    <div
                      style={{
                        padding: "8px 14px",
                        borderBottom: "1px solid var(--border, #1f2c42)",
                      }}
                    >
                      <div
                        style={{
                          fontWeight: 600,
                          fontSize: "0.85rem",
                          color: "var(--text-primary)",
                        }}
                      >
                        {currentUser.displayName || currentUser.username}
                      </div>
                      {currentUser.email && (
                        <div
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--text-muted)",
                            marginTop: "2px",
                          }}
                        >
                          {currentUser.email}
                        </div>
                      )}
                    </div>

                    <button
                      type="button"
                      className="topbar-dropdown-item"
                      onClick={() => guardedNavigate("/system/status")}
                    >
                      🖥️ {t("nav.systemStatus", "System Status")}
                    </button>
                    <button
                      type="button"
                      className="topbar-dropdown-item"
                      onClick={() => guardedNavigate("/settings/host")}
                    >
                      ⚙️ {t("nav.settings", "Settings")}
                    </button>
                    <button
                      type="button"
                      className="topbar-dropdown-item"
                      onClick={() => setShowSearchModal(true)}
                    >
                      🔍 {t("nav.commandPalette", "Command Palette (Ctrl+K)")}
                    </button>
                    <button
                      type="button"
                      className="topbar-dropdown-item"
                      onClick={() => setShowGettingStartedModal(true)}
                    >
                      🚀 {t("nav.gettingStarted", "Getting Started Guide")}
                    </button>

                    <div className="topbar-dropdown-separator" />

                    <button
                      type="button"
                      className="topbar-dropdown-item topbar-dropdown-danger"
                      onClick={handleLogout}
                    >
                      🚪 {t("nav.signOut", "Sign Out")}
                    </button>
                  </div>
                )}
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
              backgroundColor: "rgba(251, 191, 36, 0.15)",
              borderBottom: "1px solid rgba(251, 191, 36, 0.35)",
              color: "var(--warning, #fbbf24)",
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
                backgroundColor: "var(--warning, #fbbf24)",
                boxShadow: "0 0 6px var(--warning, #fbbf24)",
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
