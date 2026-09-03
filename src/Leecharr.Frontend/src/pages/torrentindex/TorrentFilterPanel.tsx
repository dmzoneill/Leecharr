import React from "react";
import {
  AllIcon,
  SeedingIcon,
  StoppedIcon,
  QueuedIcon,
  ErrorIcon,
} from "../../components/icons/UIIcons";
import { TrackerFavicon } from "../../components/TrackerFavicon";

const STATE_FILTERS = ["All", "Downloading", "Seeding", "Paused", "Queued", "Error"] as const;

const STATE_FILTER_ICONS: Record<string, React.ReactNode> = {
  All: <AllIcon size={13} />,
  Downloading: <span style={{ color: "var(--accent, #ffd166)", fontSize: "0.85rem" }}>⬇</span>,
  Seeding: <SeedingIcon size={13} />,
  Paused: <StoppedIcon size={13} />,
  Queued: <QueuedIcon size={13} />,
  Error: <ErrorIcon size={13} />,
};

interface TorrentFilterPanelProps {
  selectedState: string;
  onSelectState: (state: string) => void;
  selectedTracker: string;
  onSelectTracker: (tracker: string) => void;
  selectedPrivacy?: string;
  onSelectPrivacy?: (privacy: string) => void;
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
  privacyCounts,
  stateCounts,
  trackerGroups,
  count,
  onCollapse,
}: TorrentFilterPanelProps) {
  return (
    <div className="filter-panel">
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "0.6rem 0.75rem 0.25rem",
        }}
      >
        <span className="filter-panel-section" style={{ padding: 0 }}>
          State
        </span>
        {onCollapse && (
          <button
            type="button"
            onClick={onCollapse}
            title="Hide Filters Sidebar"
            style={{
              background: "rgba(255, 255, 255, 0.05)",
              border: "1px solid var(--border-light, rgba(255, 255, 255, 0.1))",
              borderRadius: "3px",
              color: "var(--text-muted)",
              cursor: "pointer",
              padding: "1px 5px",
              fontSize: "0.75rem",
              lineHeight: 1,
            }}
          >
            «
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
                {STATE_FILTER_ICONS[state]} {state}
              </span>
              <span className="filter-panel-count">{stateCounts[state] ?? 0}</span>
            </button>
          </li>
        ))}
      </ul>
      {onSelectPrivacy && privacyCounts && (
        <>
          <div className="filter-panel-section">Swarm Type</div>
          <ul className="filter-panel-list">
            <li>
              <button
                type="button"
                className={`filter-panel-item${selectedPrivacy === "All" ? " active" : ""}`}
                onClick={() => onSelectPrivacy("All")}
              >
                <span className="filter-panel-label">
                  <AllIcon size={13} /> All Swarms
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
                  <i className="fas fa-lock" style={{ fontSize: "0.7rem" }} /> Private (BEP 27)
                </span>
                <span className="filter-panel-count">{privacyCounts.Private}</span>
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
                  <i className="fas fa-globe" style={{ fontSize: "0.7rem" }} /> Public Swarm
                </span>
                <span className="filter-panel-count">{privacyCounts.Public}</span>
              </button>
            </li>
          </ul>
        </>
      )}
      <div className="filter-panel-section">Tracker</div>
      <ul className="filter-panel-list">
        <li>
          <button
            type="button"
            className={`filter-panel-item${selectedTracker === "All" ? " active" : ""}`}
            onClick={() => onSelectTracker("All")}
          >
            <span className="filter-panel-label">
              <AllIcon size={13} /> All
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
    </div>
  );
}

export default TorrentFilterPanel;
