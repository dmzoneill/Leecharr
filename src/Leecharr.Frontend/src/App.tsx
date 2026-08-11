import React, { useEffect, useState } from "react";
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
import { StatusBar } from "./components/StatusBar";
import { IndexerSearchModal } from "./components/IndexerSearchModal";
import { AddTorrentModal } from "./components/AddTorrentModal";
import {
  GettingStartedModal,
  STORAGE_KEY_HIDE_GUIDE,
} from "./components/GettingStartedModal";
import "./App.css";

const settingsSubItems = [
  { id: "general", label: "General" },
  { id: "webui", label: "Web UI" },
  { id: "notifications", label: "Notifications" },
  { id: "seeding", label: "Seeding & Storage" },
  { id: "bittorrent", label: "BitTorrent Engine" },
  { id: "network", label: "Network & VPN" },
  { id: "peer-protocol", label: "Peer Protocol" },
  { id: "protocols", label: "Protocols" },
  { id: "scheduler", label: "Scheduler" },
  { id: "indexers", label: "Indexers" },
  { id: "connections", label: "Connections" },
  { id: "download-clients", label: "Client Adapters" },
  { id: "advanced", label: "Advanced" },
];

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
  const [activeNav, setActiveNav] = useState<string>("dashboard");
  const [activeSubNav, setActiveSubNav] = useState<string>("history");
  const [torrents, setTorrents] = useState<Torrent[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategory, setSelectedCategory] = useState<string>("all");
  const [connected, setConnected] = useState<boolean>(false);

  const { data: indexersList } = useIndexers();

  // Modals state
  const [showAddModal, setShowAddModal] = useState<boolean>(false);
  const [showSearchModal, setShowSearchModal] = useState<boolean>(false);
  const [showGettingStartedModal, setShowGettingStartedModal] =
    useState<boolean>(() => {
      return localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) !== "true";
    });

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

  useEffect(() => {
    loadData();
    signalRManager.start();
    setConnected(true);

    const unsubscribe = signalRManager.subscribe((msg) => {
      if (msg.name === "torrent" || msg.name === "speedpulse") {
        loadData();
      }
    });

    return () => unsubscribe();
  }, []);

  const handlePause = async (id: number) => {
    await api.pauseTorrent(id);
    loadData();
  };

  const handleResume = async (id: number) => {
    await api.resumeTorrent(id);
    loadData();
  };

  const handleDelete = async (id: number) => {
    if (window.confirm("Are you sure you want to delete this torrent?")) {
      await api.deleteTorrent(id, false);
      loadData();
    }
  };

  return (
    <div className="app">
      {/* Sidebar Navigation */}
      <aside className="sidebar">
        <div className="sidebar-logo">
          <LeecharrLogo size={86} className="brand-logo" />
          <LeecharrText width={120} className="brand-text" />
        </div>

        <nav className="sidebar-nav">
          {/* Dashboard */}
          <div
            className={`sidebar-nav-item ${activeNav === "dashboard" ? "active" : ""}`}
            onClick={() => setActiveNav("dashboard")}
            style={{ cursor: "pointer" }}
          >
            <DashboardIcon size={16} />
            <span>Dashboard</span>
          </div>

          {/* Activity (Torrents Downloads, Add Torrent & Metrics) */}
          <div
            className={`sidebar-nav-item ${activeNav === "activity" ? "active" : ""}`}
            onClick={() => {
              setActiveNav("activity");
              setActiveSubNav("torrents");
            }}
            style={{ cursor: "pointer" }}
          >
            <ActivityIcon size={16} />
            <span>Activity</span>
          </div>
          {activeNav === "activity" && (
            <>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "torrents" ? "active" : ""}`}
                onClick={() => setActiveSubNav("torrents")}
                style={{ cursor: "pointer" }}
              >
                <DashboardIcon size={14} /> <span>Torrents</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "add" ? "active" : ""}`}
                onClick={() => setActiveSubNav("add")}
                style={{ cursor: "pointer" }}
              >
                <span style={{ fontSize: "1.1rem", lineHeight: 1 }}>+</span>{" "}
                <span>Add Torrent</span>
              </div>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "metrics" ? "active" : ""}`}
                onClick={() => setActiveSubNav("metrics")}
                style={{ cursor: "pointer" }}
              >
                <StatsIcon size={14} /> <span>Metrics</span>
              </div>
            </>
          )}

          {/* Torrents (History) */}
          <div
            className={`sidebar-nav-item ${activeNav === "torrents" ? "active" : ""}`}
            onClick={() => {
              setActiveNav("torrents");
              setActiveSubNav("history");
            }}
            style={{ cursor: "pointer" }}
          >
            <TorrentIcon size={16} />
            <span>Torrents</span>
          </div>
          {activeNav === "torrents" && (
            <div
              className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "history" ? "active" : ""}`}
              onClick={() => setActiveSubNav("history")}
              style={{ cursor: "pointer" }}
            >
              <HistoryIcon /> <span>History</span>
            </div>
          )}

          {/* Indexers (All + Individual Indexers + Add Indexer) */}
          <div
            className={`sidebar-nav-item ${activeNav === "indexers" ? "active" : ""}`}
            onClick={() => {
              setActiveNav("indexers");
              setActiveSubNav("all");
            }}
            style={{ cursor: "pointer" }}
          >
            <SearchIcon size={16} />
            <span>Indexers</span>
          </div>
          {activeNav === "indexers" && (
            <>
              <div
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === "all" ? "active" : ""}`}
                onClick={() => setActiveSubNav("all")}
                style={{ cursor: "pointer" }}
              >
                <SearchIcon size={14} /> <span>All Indexers</span>
              </div>
              {(indexersList || []).map((idx) => (
                <div
                  key={idx.id}
                  className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === String(idx.id) ? "active" : ""}`}
                  onClick={() => setActiveSubNav(String(idx.id))}
                  style={{ cursor: "pointer" }}
                  title={`${idx.name} (${idx.indexerType})`}
                >
                  <span
                    style={{
                      width: "6px",
                      height: "6px",
                      borderRadius: "50%",
                      backgroundColor: idx.enable
                        ? "var(--success, #22c55e)"
                        : "var(--text-muted, #7e8092)",
                      flexShrink: 0,
                    }}
                  />
                  <span
                    style={{
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {idx.name}
                  </span>
                </div>
              ))}
              <div
                className="sidebar-nav-item sidebar-nav-sub"
                onClick={() => {
                  setActiveNav("settings");
                  setActiveSubNav("indexers");
                }}
                style={{ cursor: "pointer" }}
              >
                <span style={{ fontSize: "1.1rem", lineHeight: 1 }}>+</span>{" "}
                <span>Add Indexer</span>
              </div>
            </>
          )}

          {/* Peer Map */}
          <div
            className={`sidebar-nav-item ${activeNav === "peermap" ? "active" : ""}`}
            onClick={() => setActiveNav("peermap")}
            style={{ cursor: "pointer" }}
          >
            <PeerMapIcon size={16} />
            <span>Peer Map</span>
          </div>

          {/* Schedule */}
          <div
            className={`sidebar-nav-item ${activeNav === "schedule" ? "active" : ""}`}
            onClick={() => setActiveNav("schedule")}
            style={{ cursor: "pointer" }}
          >
            <ScheduleIcon size={16} />
            <span>Schedule</span>
          </div>

          {/* Statistics */}
          <div
            className={`sidebar-nav-item ${activeNav === "statistics" ? "active" : ""}`}
            onClick={() => setActiveNav("statistics")}
            style={{ cursor: "pointer" }}
          >
            <StatsIcon size={16} />
            <span>Statistics</span>
          </div>

          {/* Settings */}
          <div
            className={`sidebar-nav-item ${activeNav === "settings" ? "active" : ""}`}
            onClick={() => {
              setActiveNav("settings");
              setActiveSubNav("general");
            }}
            style={{ cursor: "pointer" }}
          >
            <SettingsIcon size={16} />
            <span>Settings</span>
          </div>
          {activeNav === "settings" &&
            settingsSubItems.map((item) => (
              <div
                key={item.id}
                className={`sidebar-nav-item sidebar-nav-sub ${activeSubNav === item.id ? "active" : ""}`}
                onClick={() => setActiveSubNav(item.id)}
                style={{ cursor: "pointer" }}
              >
                <span>{item.label}</span>
              </div>
            ))}

          {/* System */}
          <div
            className={`sidebar-nav-item ${activeNav === "system" ? "active" : ""}`}
            onClick={() => {
              setActiveNav("system");
              setActiveSubNav("status");
            }}
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
                onClick={() => setActiveSubNav(item.id)}
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
          </div>
        </header>

        {/* Page Content */}
        <main className="app-main">
          {activeNav === "dashboard" && (
            <div className="content-area">
              <Dashboard
                torrents={torrents}
                onNavigateTorrents={() => {
                  setActiveNav("activity");
                  setActiveSubNav("torrents");
                }}
                onNavigateSettings={(tab) => {
                  setActiveNav("settings");
                  setActiveSubNav(tab);
                }}
              />
            </div>
          )}

          {activeNav === "activity" && (
            <>
              {activeSubNav === "torrents" && (
                <TorrentIndex
                  torrents={torrents}
                  onPause={handlePause}
                  onResume={handleResume}
                  onDelete={handleDelete}
                  onOpenAddModal={() => setShowAddModal(true)}
                  onOpenSearchModal={() => setShowSearchModal(true)}
                  onNavigateTab={(nav, subNav) => {
                    setActiveNav(nav);
                    if (subNav) setActiveSubNav(subNav);
                  }}
                />
              )}
              {activeSubNav === "add" && (
                <AddTorrentPage
                  onSuccess={() => {
                    setActiveNav("activity");
                    setActiveSubNav("torrents");
                    loadData();
                  }}
                />
              )}
              {activeSubNav === "metrics" && <Activity />}
            </>
          )}

          {activeNav === "torrents" && <DownloadHistory />}

          {activeNav === "indexers" && (
            <Indexers
              selectedSubNav={activeSubNav}
              onSelectIndexer={(id) => setActiveSubNav(id)}
              onNavigateSettings={(tab) => {
                setActiveNav("settings");
                setActiveSubNav(tab);
              }}
            />
          )}

          {activeNav === "peermap" && <PeerMap />}
          {activeNav === "schedule" && (
            <div className="content-area">
              <SpeedSchedule />
            </div>
          )}
          {activeNav === "statistics" && <Statistics />}
          {activeNav === "settings" && <Settings section={activeSubNav} />}
          {activeNav === "system" && (
            <>
              {activeSubNav === "status" && <SystemStatus />}
              {activeSubNav === "tasks" && <SystemTasks />}
              {activeSubNav === "backup" && <SystemBackup />}
              {activeSubNav === "updates" && <SystemUpdates />}
              {activeSubNav === "events" && <SystemEvents />}
              {activeSubNav === "logs" && <SystemLogs />}
              {activeSubNav === "network" && <SystemNetwork />}
            </>
          )}
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
        onNavigateSettings={(tab) => {
          setActiveNav("settings");
          setActiveSubNav(tab);
        }}
        onNavigateTorrents={() => {
          setActiveNav("activity");
          setActiveSubNav("torrents");
        }}
        onNavigateIndexers={() => setActiveNav("indexers")}
      />
    </div>
  );
}
export default App;
