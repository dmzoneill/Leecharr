import React, { useEffect, useState } from "react";
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
import { useIndexers } from "./api/hooks";
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
import "./App.css";

const systemSubItems = [
  { id: "status", label: "Status" },
  { id: "tasks", label: "Tasks" },
  { id: "backup", label: "Backup" },
  { id: "updates", label: "Updates" },
  { id: "events", label: "Events" },
  { id: "logs", label: "Log Files" },
  { id: "network", label: "Network" },
];

export function App() {
  const location = useLocation();
  const navigate = useNavigate();

  const [torrents, setTorrents] = useState<Torrent[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [connected, setConnected] = useState<boolean>(false);
  const [currentUser, setCurrentUser] = useState<
    import("./api/types").CurrentUser | null
  >(null);

  const { data: indexersList } = useIndexers();

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
  } else if (pathname.startsWith("/system")) {
    activeNav = "system";
    const parts = pathname.split("/");
    activeSubNav = parts[2] || "status";
  }

  const loadData = async () => {
    try {
      const [tList, cList] = await Promise.all([
        api.getTorrents(),
        api.getCategories(),
      ]);
      setTorrents(tList);
      setCategories(cList);
    } catch (err) {
      console.error("Failed to load initial data:", err);
    }
  };

  const { showToast } = useToast();

  useEffect(() => {
    loadData();
    signalRManager.start();
    setConnected(true);

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
                  ? Object.entries(msg.body).map(([id, data]: [string, any]) => ({
                      id: Number(id) || data?.id,
                      ...(typeof data === "object" ? data : {}),
                    }))
                  : [];

          if (updates.length > 0) {
            const updateMap = new Map<number, any>();
            for (const u of updates) {
              if (u && typeof u.id === "number") {
                updateMap.set(u.id, u);
              }
            }

            if (updateMap.size > 0) {
              setTorrents((prevTorrents) =>
                prevTorrents.map((t) => {
                  const u = updateMap.get(t.id);
                  if (!u) return t;
                  return {
                    ...t,
                    uploadSpeed: u.uploadSpeed ?? u.upSpeed ?? t.uploadSpeed,
                    downloadSpeed:
                      u.downloadSpeed ?? u.downSpeed ?? t.downloadSpeed,
                    progress: u.progress ?? t.progress,
                    uploaded: u.uploaded ?? t.uploaded,
                    downloaded: u.downloaded ?? t.downloaded,
                    ratio: u.ratio ?? t.ratio,
                    eta: u.eta ?? t.eta,
                    status: u.status ?? t.status,
                    seeders: u.seeders ?? t.seeders,
                    leechers: u.leechers ?? t.leechers,
                  };
                }),
              );
            }
          }
        }
        return;
      }

      if (msg.name === "pieceMapUpdated") {
        // High-frequency piece map events are handled by dedicated components; do not reload full data
        return;
      }

      if (
        msg.name === "torrent" ||
        msg.name === "torrentAdded" ||
        msg.name === "torrentUpdated" ||
        msg.name === "torrentDeleted" ||
        msg.name === "category" ||
        msg.name === "subsystemSwitched"
      ) {
        loadData();
      }
    });

    return () => unsubscribe();
  }, []);

  const handlePause = async (id: number) => {
    try {
      await api.pauseTorrent(id);
      showToast("Torrent paused", "info");
      loadData();
    } catch (err: any) {
      showToast(err?.message || "Failed to pause torrent", "error");
    }
  };

  const handleResume = async (id: number) => {
    try {
      await api.resumeTorrent(id);
      showToast("Torrent resumed", "success");
      loadData();
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
      showToast(
        deleteFiles ? "Torrent and files deleted" : "Torrent removed",
        "info",
      );
      loadData();
    } catch (err: any) {
      showToast(err?.message || "Failed to delete torrent", "error");
    }
  };

  if (pathname === "/login") {
    return (
      <LoginPage
        onLoginSuccess={() => {
          loadUser();
          navigate("/");
        }}
      />
    );
  }

  return (
    <div className={`app nav-${activeNav}`}>
      {/* Sidebar Navigation */}
      <aside className={`sidebar sidebar-${activeNav}`}>
        <div
          className="sidebar-logo"
          onClick={() => navigate("/")}
          style={{ cursor: "pointer" }}
        >
          <LeecharrLogo size={86} className="brand-logo" />
          <LeecharrText width={120} className="brand-text" />
        </div>

        <nav className="sidebar-nav">
          {/* Dashboard */}
          <div
            className={`sidebar-nav-item ${activeNav === "dashboard" ? "active" : ""}`}
            onClick={() => navigate("/")}
            style={{ cursor: "pointer" }}
          >
            <DashboardIcon size={16} />
            <span>Dashboard</span>
          </div>

          {/* Torrents (Primary Client / Transfers) */}
          <div
            className={`sidebar-nav-item ${activeNav === "torrents" ? "active" : ""}`}
            onClick={() => navigate("/torrents")}
            style={{ cursor: "pointer" }}
          >
            <TorrentIcon size={16} />
            <span>Torrents</span>
          </div>
          {activeNav === "torrents" && (
            <>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "all" ? "active" : ""}`}
                onClick={() => navigate("/torrents")}
                style={{ cursor: "pointer" }}
              >
                <DashboardIcon size={14} /> <span>All Transfers</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "add" ? "active" : ""}`}
                onClick={() => navigate("/torrents/add")}
                style={{ cursor: "pointer" }}
              >
                <span style={{ fontSize: "1.1rem", lineHeight: 1 }}>+</span>{" "}
                <span>Add Torrent</span>
              </div>
            </>
          )}

          {/* Activity (History & Real-time Metrics) */}
          <div
            className={`sidebar-nav-item ${activeNav === "activity" ? "active" : ""}`}
            onClick={() => navigate("/activity/history")}
            style={{ cursor: "pointer" }}
          >
            <ActivityIcon size={16} />
            <span>Activity</span>
          </div>
          {activeNav === "activity" && (
            <>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "history" ? "active" : ""}`}
                onClick={() => navigate("/activity/history")}
                style={{ cursor: "pointer" }}
              >
                <HistoryIcon /> <span>History</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "metrics" ? "active" : ""}`}
                onClick={() => navigate("/activity/metrics")}
                style={{ cursor: "pointer" }}
              >
                <StatsIcon size={14} /> <span>Metrics</span>
              </div>
            </>
          )}

          {/* Indexer Search & Discovery */}
          <div
            className={`sidebar-nav-item ${activeNav === "indexers" ? "active" : ""}`}
            onClick={() => navigate("/indexers")}
            style={{ cursor: "pointer" }}
          >
            <SearchIcon size={16} />
            <span>Indexers</span>
          </div>

          {/* Peer Map */}
          <div
            className={`sidebar-nav-item ${activeNav === "peermap" ? "active" : ""}`}
            onClick={() => navigate("/peermap")}
            style={{ cursor: "pointer" }}
          >
            <PeerMapIcon size={16} />
            <span>Peer Map</span>
          </div>

          {/* Schedule */}
          <div
            className={`sidebar-nav-item ${activeNav === "schedule" ? "active" : ""}`}
            onClick={() => navigate("/schedule")}
            style={{ cursor: "pointer" }}
          >
            <ScheduleIcon size={16} />
            <span>Schedule</span>
          </div>

          {/* Statistics */}
          <div
            className={`sidebar-nav-item ${activeNav === "statistics" ? "active" : ""}`}
            onClick={() => navigate("/statistics")}
            style={{ cursor: "pointer" }}
          >
            <StatsIcon size={16} />
            <span>Statistics</span>
          </div>

          {/* Settings */}
          <div
            className={`sidebar-nav-item ${activeNav === "settings" ? "active-parent" : ""}`}
            onClick={() => navigate("/settings/host")}
            style={{ cursor: "pointer" }}
          >
            <SettingsIcon size={16} />
            <span>Settings</span>
          </div>
          {activeNav === "settings" && (
            <div style={{ display: "flex", flexDirection: "column" }}>
              {SETTINGS_GROUPS.map((group) => (
                <div key={group.id} style={{ marginTop: "0.4rem" }}>
                  <div className="sidebar-group-header">
                    <span>{group.icon}</span>
                    <span>{group.shortLabel}</span>
                  </div>
                  {group.pages.map((page) => {
                    const isPageActive = activeSubNav === page.id;
                    return (
                      <div
                        key={page.id}
                        className={`sidebar-nav-item sidebar-nav-sub ${isPageActive ? "active" : ""}`}
                        onClick={() => navigate(`/settings/${page.id}`)}
                        style={{
                          cursor: "pointer",
                          paddingLeft: "2.2rem",
                          paddingTop: "0.35rem",
                          paddingBottom: "0.35rem",
                          fontSize: "0.82rem",
                          display: "flex",
                          justifyContent: "space-between",
                          alignItems: "center",
                        }}
                        title={page.description}
                      >
                        <span
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.4rem",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          <span style={{ fontSize: "0.85rem", flexShrink: 0 }}>
                            {page.icon}
                          </span>
                          <span style={{ overflow: "hidden", textOverflow: "ellipsis" }}>
                            {page.shortLabel}
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
                            {page.badge}
                          </span>
                        )}
                      </div>
                    );
                  })}
                </div>
              ))}
            </div>
          )}

          {/* System */}
          <div
            className={`sidebar-nav-item ${activeNav === "system" ? "active" : ""}`}
            onClick={() => navigate("/system/status")}
            style={{ cursor: "pointer" }}
          >
            <SystemIcon size={16} />
            <span>System</span>
          </div>
          {activeNav === "system" &&
            systemSubItems.map((item) => (
              <div
                key={item.id}
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === item.id ? "active" : ""}`}
                onClick={() => navigate(`/system/${item.id}`)}
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
          <div
            className="topbar-search"
            onClick={() => setShowSearchModal(true)}
            style={{ cursor: "pointer" }}
            title="Quick Jump / Search... (Ctrl+K or /)"
          >
            <SearchIcon size={14} />
            <input
              type="text"
              placeholder="Quick Jump / Search... (Ctrl+K or /)"
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
              ⌘K
            </kbd>
          </div>

          <div
            className="topbar-actions"
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
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
              title="Getting Started & Setup Guide (Prowlarr, Sonarr, Radarr, Lidarr)"
            >
              🚀 Setup Guide
            </button>
            <button
              className="btn btn-small btn-success"
              onClick={() => setShowAddModal(true)}
            >
              + Add Torrent
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
                  title="Sign Out"
                >
                  Sign Out
                </button>
              </div>
            )}
          </div>
        </header>

        {/* Declarative React Router Viewport */}
        <main className="app-main">
          <Routes>
            {/* Dashboard */}
            <Route
              path="/"
              element={
                <div className="content-area">
                  <Dashboard
                    torrents={torrents}
                    onNavigateTorrents={() => navigate("/torrents")}
                    onNavigateSettings={(tab) => navigate(`/settings/${tab}`)}
                  />
                </div>
              }
            />
            <Route path="/dashboard" element={<Navigate to="/" replace />} />

            {/* Torrents (Primary Client) */}
            <Route
              path="/torrents"
              element={
                <TorrentIndex
                  torrents={torrents}
                  onPause={handlePause}
                  onResume={handleResume}
                  onDelete={handleDelete}
                  onOpenAddModal={() => setShowAddModal(true)}
                  onOpenSearchModal={() => setShowSearchModal(true)}
                  onNavigateTab={(nav, subNav) => {
                    if (nav === "settings")
                      navigate(`/settings/${subNav || "general"}`);
                    else if (nav === "system")
                      navigate(`/system/${subNav || "status"}`);
                    else if (subNav) navigate(`/${nav}/${subNav}`);
                    else navigate(`/${nav}`);
                  }}
                />
              }
            />
            <Route
              path="/torrents/add"
              element={
                <AddTorrentPage
                  onSuccess={() => {
                    navigate("/torrents");
                    loadData();
                  }}
                />
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
            <Route path="/activity/history" element={<DownloadHistory />} />
            <Route
              path="/history"
              element={<Navigate to="/activity/history" replace />}
            />
            <Route path="/activity/metrics" element={<Activity />} />

            {/* Indexers */}
            <Route path="/indexers" element={<Indexers />} />
            <Route
              path="/search"
              element={<Navigate to="/indexers" replace />}
            />

            {/* Operational Visualizations */}
            <Route path="/peermap" element={<PeerMap />} />
            <Route path="/schedule" element={<SpeedSchedule />} />
            <Route path="/statistics" element={<Statistics />} />

            {/* Settings */}
            <Route
              path="/settings"
              element={<Navigate to="/settings/general" replace />}
            />
            <Route path="/settings/:section" element={<Settings />} />

            {/* System Diagnostics & Maintenance */}
            <Route
              path="/system"
              element={<Navigate to="/system/status" replace />}
            />
            <Route path="/system/status" element={<SystemStatus />} />
            <Route path="/system/tasks" element={<SystemTasks />} />
            <Route path="/system/backup" element={<SystemBackup />} />
            <Route path="/system/updates" element={<SystemUpdates />} />
            <Route path="/system/events" element={<SystemEvents />} />
            <Route path="/system/logs" element={<SystemLogs />} />
            <Route path="/system/network" element={<SystemNetwork />} />

            {/* Fallback */}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>

        {/* Bottom Status Bar */}
        <StatusBar />
      </div>

      {/* Add Torrent Modal */}
      {showAddModal && (
        <AddTorrentModal
          isOpen={showAddModal}
          onClose={() => setShowAddModal(false)}
          onSuccess={() => {
            setShowAddModal(false);
            loadData();
          }}
        />
      )}

      {/* Indexer Search Modal */}
      {showSearchModal && (
        <IndexerSearchModal
          onClose={() => setShowSearchModal(false)}
          onTorrentAdded={loadData}
        />
      )}

      {/* Getting Started & Setup Guide Modal */}
      <GettingStartedModal
        isOpen={showGettingStartedModal}
        onClose={() => setShowGettingStartedModal(false)}
        onNavigateSettings={(tab) => navigate(`/settings/${tab}`)}
        onNavigateTorrents={() => navigate("/torrents")}
        onNavigateIndexers={() => navigate("/indexers")}
      />

      {/* Discrete Collapsible AI Copilot Drawer */}
      <AiCopilotDrawer />

      {/* Global Floating Toast Notifications */}
      <ToastContainer />
    </div>
  );
}

export default App;
