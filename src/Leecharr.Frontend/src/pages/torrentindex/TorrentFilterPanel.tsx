import React from "react";
import {
  AllIcon,
  SeedingIcon,
  StoppedIcon,
  QueuedIcon,
  ErrorIcon,
} from "../../components/icons/UIIcons";
import { ChevronsLeftIcon } from "../../components/icons/AppIcons";
import { TrackerFavicon } from "../../components/TrackerFavicon";
import { useTranslation } from "../../i18n";

const STATE_FILTERS = [
  "All",
  "Downloading",
  "Seeding",
  "Paused",
  "Queued",
  "Error",
] as const;

const STATE_FILTER_ICONS: Record<string, React.ReactNode> = {
  All: <AllIcon size={13} />,
  Downloading: (
    <span style={{ color: "var(--accent, #ffd166)", fontSize: "0.85rem" }}>
      ⬇
    </span>
  ),
  Seeding: <SeedingIcon size={13} />,
  Paused: <StoppedIcon size={13} />,
  Queued: <QueuedIcon size={13} />,
  Error: <ErrorIcon size={13} />,
};

export interface TagGroupItem {
  id: number;
  label: string;
  count: number;
  color?: string;
}

export type TagGroupInput = TagGroupItem | [string, number];

interface TorrentFilterPanelProps {
  selectedState: string;
  onSelectState: (state: string) => void;
  selectedTracker: string;
  onSelectTracker: (tracker: string) => void;
  selectedPrivacy?: string;
  onSelectPrivacy?: (privacy: string) => void;
  selectedTag?: string;
  onSelectTag?: (tag: string) => void;
  selectedTagIds?: Set<number>;
  onToggleTag?: (tagId: number) => void;
  tagMatchMode?: "AND" | "OR";
  onTagMatchModeChange?: (mode: "AND" | "OR") => void;
  onClearTags?: () => void;
  untaggedCount?: number;
  tagGroups?: TagGroupInput[];
  privacyCounts?: { All: number; Private: number; Public: number };
  stateCounts: Record<string, number>;
  trackerGroups: [string, number][];
  count: number;
  onCollapse?: () => void;
}

export function TorrentFilterPanel({
  selectedState,
  onSelectState,
  selectedTracker,
  onSelectTracker,
  selectedPrivacy = "All",
  onSelectPrivacy,
  selectedTag = "All",
  onSelectTag,
  selectedTagIds,
  onToggleTag,
  tagMatchMode = "OR",
  onTagMatchModeChange,
  onClearTags,
  untaggedCount,
  tagGroups = [],
  privacyCounts,
  stateCounts,
  trackerGroups,
  count,
  onCollapse,
}: TorrentFilterPanelProps) {
  const normalizedTagGroups: TagGroupItem[] = (tagGroups ?? []).map(
    (tg, idx) => {
      if (Array.isArray(tg)) {
        return { id: idx + 1000, label: tg[0], count: tg[1] };
      }
      return tg;
    },
  );

  const [isTagOpen, setIsTagOpen] = React.useState(true);
  const { t } = useTranslation();

  const getStateLabel = (state: string) => {
    switch (state) {
      case "All":
        return t("common.selectAll");
      case "Downloading":
        return t("torrents.states.downloading");
      case "Seeding":
        return t("torrents.states.seeding");
      case "Paused":
        return t("torrents.states.paused");
      case "Queued":
        return t("torrents.states.queued");
      case "Error":
        return t("torrents.states.error");
      default:
        return state;
    }
  };

  return (
    <div className="filter-panel">
      <div className="filter-panel-header">
        <div className="filter-panel-section">
          {t("torrents.filterState", "State")}
        </div>
        {onCollapse && (
          <button
            type="button"
            className="filter-panel-collapse-btn"
            onClick={onCollapse}
            title={t("torrents.toolbar.hideFilters", "Collapse filter sidebar")}
            aria-label="Collapse filter sidebar"
          >
            <ChevronsLeftIcon size={13} />
          </button>
        )}
      </div>
      <ul className="filter-panel-list">
        {STATE_FILTERS.map((state) => (
          <li key={state}>
            <button
              type="button"
              className={`filter-panel-item${selectedState === state ? " active" : ""}`}
              onClick={() => onSelectState(state)}
            >
              <span className="filter-panel-label">
                {STATE_FILTER_ICONS[state]} {getStateLabel(state)}
              </span>
              <span className="filter-panel-count">
                {stateCounts[state] ?? 0}
              </span>
            </button>
          </li>
        ))}
      </ul>
      {onSelectPrivacy && privacyCounts && (
        <>
          <div className="filter-panel-section">
            {t("torrents.filters.swarmType")}
          </div>
          <ul className="filter-panel-list">
            <li>
              <button
                type="button"
                className={`filter-panel-item${selectedPrivacy === "All" ? " active" : ""}`}
                onClick={() => onSelectPrivacy("All")}
              >
                <span className="filter-panel-label">
                  <AllIcon size={13} /> {t("torrents.filters.allSwarms")}
                </span>
                <span className="filter-panel-count">{privacyCounts.All}</span>
              </button>
            </li>
            <li>
              <button
                type="button"
                className={`filter-panel-item${selectedPrivacy === "Private" ? " active" : ""}`}
                onClick={() => onSelectPrivacy("Private")}
              >
                <span
                  className="filter-panel-label"
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                    color: "#f87171",
                  }}
                >
                  <i className="fas fa-lock" style={{ fontSize: "0.7rem" }} />{" "}
                  {t("torrents.filters.privateBep27")}
                </span>
                <span className="filter-panel-count">
                  {privacyCounts.Private}
                </span>
              </button>
            </li>
            <li>
              <button
                type="button"
                className={`filter-panel-item${selectedPrivacy === "Public" ? " active" : ""}`}
                onClick={() => onSelectPrivacy("Public")}
              >
                <span
                  className="filter-panel-label"
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                    color: "#60a5fa",
                  }}
                >
                  <i className="fas fa-globe" style={{ fontSize: "0.7rem" }} />{" "}
                  {t("torrents.filters.publicSwarm")}
                </span>
                <span className="filter-panel-count">
                  {privacyCounts.Public}
                </span>
              </button>
            </li>
          </ul>
        </>
      )}
      <div className="filter-panel-section">
        {t("torrents.filters.tracker")}
      </div>
      <ul className="filter-panel-list">
        <li>
          <button
            type="button"
            className={`filter-panel-item${selectedTracker === "All" ? " active" : ""}`}
            onClick={() => onSelectTracker("All")}
          >
            <span className="filter-panel-label">
              <AllIcon size={13} /> {t("torrents.filters.all")}
            </span>
            <span className="filter-panel-count">{count}</span>
          </button>
        </li>
        {trackerGroups.map(([domain, groupCount]) => (
          <li key={domain}>
            <button
              type="button"
              className={`filter-panel-item${selectedTracker === domain ? " active" : ""}`}
              onClick={() => onSelectTracker(domain)}
            >
              <span
                className="filter-panel-label"
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.4rem",
                }}
              >
                <TrackerFavicon urlOrHost={domain} size={14} />
                <span
                  style={{
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                  }}
                >
                  {domain}
                </span>
              </span>
              <span className="filter-panel-count">{groupCount}</span>
            </button>
          </li>
        ))}
      </ul>

      {/* Tag / Label Section */}
      <div
        className="filter-panel-header"
        style={{
          cursor: "pointer",
          userSelect: "none",
          marginTop: "0.5rem",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
        onClick={() => setIsTagOpen(!isTagOpen)}
      >
        <div
          className="filter-panel-section"
          style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
        >
          <span style={{ fontSize: "0.7rem" }}>{isTagOpen ? "▼" : "▶"}</span>
          <span>🏷️ Tag / Label</span>
        </div>
        {selectedTagIds && selectedTagIds.size > 0 && onClearTags && (
          <button
            type="button"
            className="filter-panel-clear-tags-btn"
            style={{
              background: "none",
              border: "1px solid var(--border-color, #333)",
              borderRadius: "4px",
              color: "var(--accent, #ffd166)",
              fontSize: "0.68rem",
              padding: "1px 5px",
              cursor: "pointer",
            }}
            onClick={(e) => {
              e.stopPropagation();
              onClearTags();
            }}
            title="Clear selected tags"
          >
            Clear ({selectedTagIds.size})
          </button>
        )}
      </div>

      {isTagOpen && (
        <>
          {/* AND / OR Match Mode Toggle */}
          {onTagMatchModeChange && normalizedTagGroups.length > 0 && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.2rem 0.75rem 0.4rem",
                fontSize: "0.75rem",
              }}
            >
              <span
                style={{
                  color: "var(--text-muted, #888)",
                  fontSize: "0.7rem",
                  fontWeight: 600,
                }}
              >
                MATCH:
              </span>
              <div
                style={{
                  display: "inline-flex",
                  borderRadius: "4px",
                  overflow: "hidden",
                  border: "1px solid var(--border-color, #333)",
                }}
              >
                <button
                  type="button"
                  onClick={() => onTagMatchModeChange("OR")}
                  style={{
                    padding: "2px 8px",
                    fontSize: "0.7rem",
                    fontWeight: 600,
                    background:
                      tagMatchMode === "OR"
                        ? "var(--accent-bg-medium, rgba(255, 209, 102, 0.2))"
                        : "transparent",
                    color:
                      tagMatchMode === "OR"
                        ? "var(--accent, #ffd166)"
                        : "var(--text-muted, #888)",
                    border: "none",
                    cursor: "pointer",
                  }}
                  title="Match torrents with ANY selected tag"
                >
                  ANY (OR)
                </button>
                <button
                  type="button"
                  onClick={() => onTagMatchModeChange("AND")}
                  style={{
                    padding: "2px 8px",
                    fontSize: "0.7rem",
                    fontWeight: 600,
                    background:
                      tagMatchMode === "AND"
                        ? "var(--accent-bg-medium, rgba(255, 209, 102, 0.2))"
                        : "transparent",
                    color:
                      tagMatchMode === "AND"
                        ? "var(--accent, #ffd166)"
                        : "var(--text-muted, #888)",
                    border: "none",
                    borderLeft: "1px solid var(--border-color, #333)",
                    cursor: "pointer",
                  }}
                  title="Match torrents with ALL selected tags"
                >
                  ALL (AND)
                </button>
              </div>
            </div>
          )}

          <ul className="filter-panel-list">
            <li>
              <button
                type="button"
                className={`filter-panel-item${(!selectedTagIds || selectedTagIds.size === 0) && selectedTag === "All" ? " active" : ""}`}
                onClick={() => {
                  onClearTags?.();
                  onSelectTag?.("All");
                }}
              >
                <span className="filter-panel-label">
                  <AllIcon size={13} /> All
                </span>
                <span className="filter-panel-count">{count}</span>
              </button>
            </li>
            {untaggedCount !== undefined && (
              <li>
                <button
                  type="button"
                  className={`filter-panel-item${selectedTag === "Untagged" ? " active" : ""}`}
                  onClick={() => {
                    onClearTags?.();
                    onSelectTag?.(
                      selectedTag === "Untagged" ? "All" : "Untagged",
                    );
                  }}
                >
                  <span
                    className="filter-panel-label"
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                    }}
                  >
                    <span style={{ fontSize: "0.85rem" }}>🏷️</span>
                    <span>Untagged</span>
                  </span>
                  <span className="filter-panel-count">{untaggedCount}</span>
                </button>
              </li>
            )}
            {normalizedTagGroups.map((tag) => {
              const isSelected =
                Boolean(selectedTagIds?.has(tag.id)) ||
                selectedTag === tag.label;
              return (
                <li key={tag.id}>
                  <button
                    type="button"
                    className={`filter-panel-item${isSelected ? " active" : ""}`}
                    onClick={() => {
                      if (onToggleTag) {
                        onToggleTag(tag.id);
                      } else {
                        onSelectTag?.(tag.label);
                      }
                    }}
                  >
                    <span
                      className="filter-panel-label"
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.4rem",
                        minWidth: 0,
                      }}
                    >
                      {onToggleTag && (
                        <input
                          type="checkbox"
                          checked={Boolean(selectedTagIds?.has(tag.id))}
                          onChange={() => {}}
                          style={{
                            cursor: "pointer",
                            accentColor: tag.color || "var(--accent, #ffd166)",
                            margin: 0,
                          }}
                        />
                      )}
                      {tag.color ? (
                        <span
                          style={{
                            width: "8px",
                            height: "8px",
                            borderRadius: "50%",
                            backgroundColor: tag.color,
                            display: "inline-block",
                            flexShrink: 0,
                          }}
                        />
                      ) : (
                        <span style={{ fontSize: "0.85rem" }}>🏷️</span>
                      )}
                      <span
                        style={{
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {tag.label}
                      </span>
                    </span>
                    <span className="filter-panel-count">{tag.count}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </div>
  );
}

export default TorrentFilterPanel;
