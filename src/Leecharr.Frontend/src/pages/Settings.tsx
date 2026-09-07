import React, { useState, useMemo, useRef, useEffect, useContext } from "react";
import { useParams, useNavigate } from "react-router";
import {
  SETTINGS_GROUPS,
  LEGACY_SETTINGS_MAP,
  SettingsGroupId,
  SettingsPageDefinition,
} from "./settings/settingsNavData";
import {
  useSettingsDirty,
  SettingsDirtyContext,
  SettingsDirtyProvider,
  defaultSettingsDirtyContext,
} from "./settings/SettingsDirtyContext";
import { HostSettingsTab } from "./settings/HostSettingsTab";
import { WebUiSettingsTab } from "./settings/WebUiSettingsTab";
import { SecuritySettingsTab } from "./settings/SecuritySettingsTab";
import { WatchFolderSettingsTab } from "./settings/WatchFolderSettingsTab";
import { StorageSettingsTab } from "./settings/StorageSettingsTab";
import { QueueSettingsTab } from "./settings/QueueSettingsTab";
import { CategorySettingsTab } from "./settings/CategorySettingsTab";
import { CustomScriptsTab } from "./settings/CustomScriptsTab";
import { EngineSettingsTab } from "./settings/EngineSettingsTab";
import { ProtocolsSettingsTab } from "./settings/ProtocolsSettingsTab";
import { DhtSettingsTab } from "./settings/DhtSettingsTab";
import { ClientEmulationSettingsTab } from "./settings/ClientEmulationSettingsTab";
import { TrackerServerSettingsTab } from "./settings/TrackerServerSettingsTab";
import { SpeedSettingsTab } from "./settings/SpeedSettingsTab";
import { ScheduleSettingsTab } from "./settings/ScheduleSettingsTab";
import { NetworkSettingsTab } from "./settings/NetworkSettingsTab";
import { ProxySettingsTab } from "./settings/ProxySettingsTab";
import { IndexersTab } from "./settings/IndexersTab";
import { ConnectionsTab } from "./settings/ConnectionsTab";
import { DownloadClientsTab } from "./settings/DownloadClientsTab";
import { NotificationsTab } from "./settings/NotificationsTab";
import { SubsystemsTab } from "./settings/SubsystemsTab";
import { AiTab } from "./settings/AiTab";
import { LoggingTab } from "./settings/LoggingTab";
import { SearchIcon } from "../components/icons/AppIcons";
import { useTranslation } from "../i18n";

export { useSettingsDirty, SettingsDirtyContext, SettingsDirtyProvider };

export function Settings() {
  const { t } = useTranslation();

  const dirtyCtx = useContext(SettingsDirtyContext);
  if (dirtyCtx === defaultSettingsDirtyContext) {
    return (
      <SettingsDirtyProvider>
        <SettingsContent />
      </SettingsDirtyProvider>
    );
  }
  return <SettingsContent />;
}

function SettingsContent() {
  const { t } = useTranslation();
  const params = useParams<{ section?: string }>();
  const navigate = useNavigate();
  const rawSection = (params.section || "host").toLowerCase();

  // Resolve active group and page from URL with legacy fallback
  const resolved = useMemo(() => {
    if (LEGACY_SETTINGS_MAP[rawSection]) {
      return LEGACY_SETTINGS_MAP[rawSection];
    }
    for (const group of SETTINGS_GROUPS) {
      if (group.id === rawSection) {
        return { groupId: group.id, pageId: group.pages[0].id };
      }
      const foundPage = group.pages.find((p) => p.id === rawSection);
      if (foundPage) {
        return { groupId: group.id, pageId: foundPage.id };
      }
    }
    return { groupId: "general-security" as SettingsGroupId, pageId: "host" };
  }, [rawSection]);

  const activeGroup = useMemo(
    () =>
      SETTINGS_GROUPS.find((g) => g.id === resolved.groupId) ||
      SETTINGS_GROUPS[0],
    [resolved.groupId],
  );

  const activePage = useMemo(
    () =>
      activeGroup.pages.find((p) => p.id === resolved.pageId) ||
      activeGroup.pages[0],
    [activeGroup, resolved.pageId],
  );

  const [searchQuery, setSearchQuery] = useState("");
  const searchInputRef = useRef<HTMLInputElement>(null);

  // Global hotkey to focus Settings Search (`/` or `Ctrl+F` within settings)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (
        (e.key === "/" || (e.ctrlKey && e.key.toLowerCase() === "f")) &&
        document.activeElement?.tagName !== "INPUT" &&
        document.activeElement?.tagName !== "TEXTAREA"
      ) {
        e.preventDefault();
        searchInputRef.current?.focus();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  // Search filter across all 23 focused pages
  const searchResults = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return null;

    const results: {
      group: (typeof SETTINGS_GROUPS)[0];
      page: SettingsPageDefinition;
      matchedKeywords: string[];
    }[] = [];
    for (const g of SETTINGS_GROUPS) {
      for (const p of g.pages) {
        const titleMatch = p.title.toLowerCase().includes(q);
        const descMatch = p.description.toLowerCase().includes(q);
        const matchedKw = p.keywords.filter((k) => k.toLowerCase().includes(q));
        if (titleMatch || descMatch || matchedKw.length > 0) {
          results.push({ group: g, page: p, matchedKeywords: matchedKw });
        }
      }
    }
    return results;
  }, [searchQuery]);

  const { isDirty, confirmIfDirty } = useSettingsDirty();

  // Guard browser refresh or close when settings are dirty
  useEffect(() => {
    if (!isDirty) return;
    const handleBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => window.removeEventListener("beforeunload", handleBeforeUnload);
  }, [isDirty]);

  const handleSelectPage = (pageId: string) => {
    confirmIfDirty(() => {
      setSearchQuery("");
      navigate(`/settings/${pageId}`);
    });
  };

  const handleSelectGroup = (groupId: SettingsGroupId) => {
    const targetGroup = SETTINGS_GROUPS.find((g) => g.id === groupId);
    if (targetGroup) {
      confirmIfDirty(() => {
        setSearchQuery("");
        navigate(`/settings/${targetGroup.pages[0].id}`);
      });
    }
  };

  // Render the appropriate component for the active focused page
  const renderTabContent = () => {
    switch (activePage.id) {
      case "host":
        return <HostSettingsTab />;
      case "webui":
        return <WebUiSettingsTab />;
      case "security":
        return <SecuritySettingsTab />;
      case "watch-folder":
        return <WatchFolderSettingsTab />;
      case "storage":
        return <StorageSettingsTab />;
      case "queue":
        return <QueueSettingsTab />;
      case "categories":
        return <CategorySettingsTab />;
      case "custom-scripts":
        return <CustomScriptsTab />;
      case "engine":
        return <EngineSettingsTab />;
      case "protocols":
        return <ProtocolsSettingsTab />;
      case "dht":
        return <DhtSettingsTab />;
      case "client-emulation":
        return <ClientEmulationSettingsTab />;
      case "tracker-server":
        return <TrackerServerSettingsTab />;
      case "speed":
        return <SpeedSettingsTab />;
      case "schedule":
        return <ScheduleSettingsTab />;
      case "network":
        return <NetworkSettingsTab />;
      case "proxy":
        return <ProxySettingsTab />;
      case "indexers":
        return <IndexersTab />;
      case "connections":
        return <ConnectionsTab />;
      case "download-clients":
        return <DownloadClientsTab />;
      case "notifications":
        return <NotificationsTab />;
      case "subsystems":
        return <SubsystemsTab />;
      case "ai":
        return <AiTab />;
      case "logging":
        return <LoggingTab />;
      default:
        return <HostSettingsTab />;
    }
  };

  return (
    <div
      className="content-area"
      style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
    >
      {/* 1. Header Banner & Quick Filter Search */}
      <div
        className="card"
        style={{
          padding: "1rem 1.25rem",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          borderRadius: "8px",
          backgroundColor: "var(--bg-secondary)",
          border: "1px solid var(--border-light)",
        }}
      >
        {/* Breadcrumb Heading */}
        <div>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              fontSize: "0.8rem",
              color: "var(--text-muted)",
            }}
          >
            <span>{t("nav.settings")}</span>
            <span>&rsaquo;</span>
            <span style={{ color: "var(--text-secondary)", fontWeight: 600 }}>
              {t(activeGroup.title)}
            </span>
            <span>&rsaquo;</span>
            <span style={{ color: "var(--accent)", fontWeight: 700 }}>
              {t(activePage.shortLabel)}
            </span>
          </div>
          <h1
            className="page-heading"
            style={{
              margin: "0.25rem 0 0 0",
              fontSize: "1.4rem",
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>{activePage.icon}</span>
            <span>{t(activePage.title)}</span>
            {activePage.badge && (
              <span
                className="badge badge-primary"
                style={{ fontSize: "0.7rem", padding: "0.15rem 0.5rem" }}
              >
                {t(activePage.badge)}
              </span>
            )}
          </h1>
          <div
            style={{
              fontSize: "0.82rem",
              color: "var(--text-muted)",
              marginTop: "0.15rem",
            }}
          >
            {t(activePage.description)}
          </div>
        </div>

        {/* Quick Filter Search Bar */}
        <div
          style={{
            position: "relative",
            minWidth: "260px",
            maxWidth: "340px",
            width: "100%",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              backgroundColor: "var(--bg-primary)",
              border: "1px solid var(--border)",
              borderRadius: "6px",
              padding: "0.35rem 0.65rem",
              gap: "0.4rem",
            }}
          >
            <SearchIcon size={14} />
            <input
              ref={searchInputRef}
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder={t("settingsTabs.search.placeholder")}
              style={{
                background: "transparent",
                border: "none",
                outline: "none",
                color: "var(--text-primary)",
                fontSize: "0.85rem",
                width: "100%",
              }}
            />
            {searchQuery && (
              <button
                type="button"
                onClick={() => setSearchQuery("")}
                style={{
                  background: "none",
                  border: "none",
                  color: "var(--text-muted)",
                  cursor: "pointer",
                  fontSize: "0.8rem",
                }}
              >
                ✕
              </button>
            )}
          </div>
        </div>
      </div>

      {/* 4. Live Search Filter Results View */}
      {searchResults && (
        <div
          className="card"
          style={{
            padding: "1.25rem",
            borderRadius: "8px",
            backgroundColor: "var(--bg-secondary)",
            border: "1px solid var(--accent)",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "1rem",
            }}
          >
            <span
              style={{
                fontSize: "0.9rem",
                fontWeight: 600,
                color: "var(--text-primary)",
              }}
            >
              {t("settingsTabs.search.results")} &ldquo;{searchQuery}&rdquo; (
              {searchResults.length}{" "}
              {searchResults.length === 1
                ? t("settingsTabs.search.page")
                : t("settingsTabs.search.pages")}{" "}
              {t("settingsTabs.search.found")})
            </span>
            <button
              type="button"
              onClick={() => setSearchQuery("")}
              className="btn btn-outline btn-small"
              style={{ fontSize: "0.75rem" }}
            >
              {t("settingsTabs.search.clearSearch", "Clear Search")}
            </button>
          </div>

          {searchResults.length === 0 ? (
            <div
              style={{
                textAlign: "center",
                padding: "2rem",
                color: "var(--text-muted)",
              }}
            >
              {t("settingsTabs.search.noResults")}
            </div>
          ) : (
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))",
                gap: "0.85rem",
              }}
            >
              {searchResults.map(({ group, page, matchedKeywords }) => (
                <div
                  key={page.id}
                  onClick={() => handleSelectPage(page.id)}
                  style={{
                    padding: "1rem",
                    borderRadius: "6px",
                    backgroundColor: "var(--bg-primary)",
                    border: "1px solid var(--border)",
                    cursor: "pointer",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    gap: "0.5rem",
                    transition: "border-color 0.15s ease",
                  }}
                  onMouseEnter={(e) =>
                    ((e.currentTarget as HTMLElement).style.borderColor =
                      "var(--accent)")
                  }
                  onMouseLeave={(e) =>
                    ((e.currentTarget as HTMLElement).style.borderColor =
                      "var(--border)")
                  }
                >
                  <div>
                    <div
                      style={{
                        fontSize: "0.75rem",
                        color: "var(--text-muted)",
                        marginBottom: "0.2rem",
                      }}
                    >
                      {t(group.title)}
                    </div>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.4rem",
                        fontWeight: 700,
                        color: "var(--text-primary)",
                      }}
                    >
                      <span>{page.icon}</span>
                      <span>{t(page.title)}</span>
                    </div>
                    <div
                      style={{
                        fontSize: "0.78rem",
                        color: "var(--text-secondary)",
                        marginTop: "0.3rem",
                        lineHeight: 1.35,
                      }}
                    >
                      {t(page.description)}
                    </div>
                  </div>

                  {matchedKeywords.length > 0 && (
                    <div
                      style={{
                        display: "flex",
                        gap: "0.3rem",
                        flexWrap: "wrap",
                        marginTop: "0.4rem",
                      }}
                    >
                      {matchedKeywords.map((kw) => (
                        <span
                          key={kw}
                          style={{
                            fontSize: "0.68rem",
                            backgroundColor: "var(--accent-bg)",
                            color: "var(--accent)",
                            padding: "0.1rem 0.4rem",
                            borderRadius: "3px",
                            fontWeight: 600,
                          }}
                        >
                          ✓ {kw}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* 5. Main Active Content Viewport */}
      {!searchResults && renderTabContent()}
    </div>
  );
}

export default Settings;
