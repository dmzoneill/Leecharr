import React from "react";
import {
  AllIcon,
  SeedingIcon,
  StoppedIcon,
  QueuedIcon,
  ErrorIcon,
} from "../../components/icons/UIIcons";
import { TrackerFavicon } from "../../components/TrackerFavicon";

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

interface TorrentFilterPanelProps {
  selectedState: string;
  onSelectState: (state: string) => void;
  selectedTracker: string;
  onSelectTracker: (tracker: string) => void;
  stateCounts: Record<string, number>;
  trackerGroups: [string, number][];
  count: number;
}

export function TorrentFilterPanel({
  selectedState,
  onSelectState,
  selectedTracker,
  onSelectTracker,
  stateCounts,
  trackerGroups,
  count,
}: TorrentFilterPanelProps) {
  return (
    <div className="filter-panel">
      <div className="filter-panel-section">State</div>
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
              <span className="filter-panel-count">
                {stateCounts[state] ?? 0}
              </span>
            </button>
          </li>
        ))}
      </ul>
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
