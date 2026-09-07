import React, { useState, useMemo } from "react";
import {
  useTrackerBoostTrackers,
  useAddTrackerBoostTracker,
  useDeleteTrackerBoostTracker,
} from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import TrackerFavicon from "../../components/TrackerFavicon";
import { useTranslation } from "../../i18n";

export interface RadarViewProps {
  onOpenBulkImport?: () => void;
}

export function RadarView({ onOpenBulkImport }: RadarViewProps) {
  const { t } = useTranslation();
  const { data: trackers } = useTrackerBoostTrackers();
  const addTracker = useAddTrackerBoostTracker();
  const deleteTracker = useDeleteTrackerBoostTracker();
  const { showToast } = useToast();

  const [trackerSearch, setTrackerSearch] = useState("");
  const [sourceFilter, setSourceFilter] = useState<string>("all");
  const [healthFilter, setHealthFilter] = useState<string>("all");
  const [newTrackerUrl, setNewTrackerUrl] = useState("");
  const [isAddingTracker, setIsAddingTracker] = useState(false);

  const handleCopyAllTrackers = () => {
    if (!trackers || trackers.length === 0) return;
    const uniqueUrls = Array.from(new Set(trackers.map((t) => t.url))).join(
      "\n",
    );
    navigator.clipboard.writeText(uniqueUrls);
    showToast(
      t(
        "trackerBoost.radar.copiedUrlsToast",
        "Copied {count} tracker URLs to clipboard!",
        { count: trackers.length },
      ),
      "info",
    );
  };

  const handleExportTrackers = () => {
    if (!trackers || trackers.length === 0) {
      showToast(
        t(
          "trackerBoost.radar.noTrackersExport",
          "No trackers available to export",
        ),
        "info",
      );
      return;
    }
    const uniqueUrls = Array.from(new Set(trackers.map((t) => t.url))).join(
      "\n",
    );
    const blob = new Blob([uniqueUrls], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `leecharr-trackers-${new Date().toISOString().slice(0, 10)}.txt`;
    link.click();
    URL.revokeObjectURL(url);
    showToast(
      t(
        "trackerBoost.radar.exportedTrackersToast",
        "Exported {count} tracker endpoints to .txt!",
        { count: trackers.length },
      ),
      "success",
    );
  };

  const handleAddCustomTracker = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTrackerUrl.trim()) return;
    addTracker.mutate(
      { url: newTrackerUrl.trim() },
      {
        onSuccess: () => {
          showToast(
            t(
              "trackerBoost.radar.trackerAddedSuccess",
              "Custom tracker added successfully",
            ),
            "success",
          );
          setNewTrackerUrl("");
          setIsAddingTracker(false);
        },
        onError: (err) => {
          showToast(
            t(
              "trackerBoost.radar.trackerAddFailed",
              "Failed to add tracker: {error}",
              { error: err.message },
            ),
            "error",
          );
        },
      },
    );
  };

  const filteredTrackers = useMemo(() => {
    return (trackers ?? []).filter((t) => {
      if (trackerSearch.trim()) {
        const q = trackerSearch.toLowerCase();
        const url = (t.url || "").toLowerCase();
        const host = (t.host || "").toLowerCase();
        const sourceName = (t.sourceName || "").toLowerCase();
        if (!url.includes(q) && !host.includes(q) && !sourceName.includes(q)) {
          return false;
        }
      }
      if (sourceFilter !== "all") {
        if (
          sourceFilter === "active" &&
          t.source !== "ActiveTorrent" &&
          t.source !== 4
        )
          return false;
        if (
          sourceFilter === "prowlarr" &&
          t.source !== "Prowlarr" &&
          t.source !== 1
        )
          return false;
        if (
          sourceFilter === "feeds" &&
          t.source !== "PublicList" &&
          t.source !== 0
        )
          return false;
        if (
          sourceFilter === "manual" &&
          t.source !== "Manual" &&
          t.source !== 3
        )
          return false;
      }
      if (healthFilter !== "all") {
        if (healthFilter === "alive" && t.status !== "Alive" && t.status !== 1)
          return false;
        if (healthFilter === "slow" && t.status !== "Slow" && t.status !== 2)
          return false;
        if (
          healthFilter === "offline" &&
          t.status !== "Offline" &&
          t.status !== 3
        )
          return false;
        if (
          healthFilter === "untested" &&
          t.status !== "Untested" &&
          t.status !== 0
        )
          return false;
      }
      return true;
    });
  }, [trackers, trackerSearch, sourceFilter, healthFilter]);

  return (
    <div
      className="card"
      style={{
        padding: "1.25rem",
        flex: "1 1 auto",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        marginBottom: "0.5rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1rem",
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <input
            type="text"
            className="form-control"
            style={{
              width: "240px",
              padding: "0.4rem 0.75rem",
              fontSize: "0.85rem",
            }}
            placeholder={t(
              "trackerBoost.searchTrackerHosts",
              "Search tracker hosts...",
            )}
            value={trackerSearch}
            onChange={(e) => setTrackerSearch(e.target.value)}
          />
          <select
            className="form-control"
            style={{
              width: "160px",
              padding: "0.4rem 0.75rem",
              fontSize: "0.85rem",
            }}
            value={sourceFilter}
            onChange={(e) => setSourceFilter(e.target.value)}
          >
            <option value="all">
              {t("trackerBoost.allSources", "All Sources")}
            </option>
            <option value="active">
              {t("trackerBoost.activeSwarmHarvest", "Active Swarm Harvest")}
            </option>
            <option value="prowlarr">Prowlarr</option>
            <option value="feeds">
              {t("trackerBoost.publicFeeds", "Public Feeds")}
            </option>
            <option value="manual">
              {t("trackerBoost.manualEntry", "Manual Entry")}
            </option>
          </select>
        </div>
        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <button
            className="btn btn-action"
            onClick={handleCopyAllTrackers}
            title={t(
              "trackerBoost.copyAllUrls",
              "Copy all tracker URLs to clipboard",
            )}
          >
            {t("trackerBoost.radar.copyAll", "📋 Copy All")}
          </button>
          <button
            className="btn btn-action"
            onClick={handleExportTrackers}
            title={t(
              "trackerBoost.downloadTxt",
              "Download verified and active trackers as a .txt file",
            )}
          >
            {t("trackerBoost.radar.exportTxt", "📤 Export (.txt)")}
          </button>
          {onOpenBulkImport && (
            <button
              className="btn btn-action"
              onClick={onOpenBulkImport}
              title={t(
                "trackerBoost.pasteMultiple",
                "Paste multiple tracker URLs at once",
              )}
            >
              {t("trackerBoost.radar.bulkImport", "📥 Bulk Import")}
            </button>
          )}
          <button
            className="btn btn-primary"
            onClick={() => setIsAddingTracker(true)}
          >
            {t("trackerBoost.radar.addSingle", "+ Add Single")}
          </button>
        </div>
      </div>

      {isAddingTracker && (
        <form
          onSubmit={handleAddCustomTracker}
          style={{ display: "flex", gap: "0.5rem", marginBottom: "1rem" }}
        >
          <input
            type="text"
            className="form-control"
            placeholder="udp://tracker.example.com:1337/announce"
            value={newTrackerUrl}
            onChange={(e) => setNewTrackerUrl(e.target.value)}
            style={{ flex: 1 }}
          />
          <button
            type="submit"
            className="btn btn-primary"
            disabled={addTracker.isPending}
          >
            {t("common.save", "Save")}
          </button>
          <button
            type="button"
            className="btn btn-outline"
            onClick={() => setIsAddingTracker(false)}
          >
            {t("common.cancel", "Cancel")}
          </button>
        </form>
      )}

      <div
        className="torrent-table-wrapper"
        style={{
          borderRadius: "6px",
          border: "1px solid var(--border)",
          marginTop: "0.5rem",
          flex: "1 1 auto",
          minHeight: 0,
          overflowY: "auto",
          backgroundColor: "var(--bg-secondary, rgba(0,0,0,0.2))",
        }}
      >
        <table className="torrent-table" style={{ width: "100%" }}>
          <thead
            style={{
              position: "sticky",
              top: 0,
              zIndex: 2,
              backgroundColor: "var(--bg-secondary)",
            }}
          >
            <tr>
              <th className="torrent-table-th" style={{ width: "38%" }}>
                {t("trackerBoost.matrix.trackerEndpoint", "Tracker Endpoint")}
              </th>
              <th className="torrent-table-th" style={{ width: "10%" }}>
                {t("trackerBoost.protocol", "Protocol")}
              </th>
              <th className="torrent-table-th" style={{ width: "16%" }}>
                {t("trackerBoost.radar.source", "Source")}
              </th>
              <th className="torrent-table-th" style={{ width: "12%" }}>
                {t("common.status", "Status")}
              </th>
              <th className="torrent-table-th" style={{ width: "10%" }}>
                {t("trackerBoost.latency", "Latency")}
              </th>
              <th className="torrent-table-th" style={{ width: "14%" }}>
                {t("trackerBoost.radar.verifiedSwarms", "Verified Swarms")}
              </th>
              <th
                className="torrent-table-th"
                style={{ width: "10%", textAlign: "right" }}
              >
                {t("trackerBoost.matrix.actions", "Actions")}
              </th>
            </tr>
          </thead>
          <tbody>
            {filteredTrackers.map((tr) => (
              <tr key={tr.id} className="torrent-table-row">
                <td
                  style={{
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                    wordBreak: "break-all",
                  }}
                >
                  <div
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.45rem",
                    }}
                  >
                    <TrackerFavicon urlOrHost={tr.url} size={15} />
                    <span>{tr.url}</span>
                  </div>
                </td>
                <td>
                  <span
                    className="badge badge-secondary"
                    style={{ fontSize: "0.75rem" }}
                  >
                    {tr.protocol}
                  </span>
                </td>
                <td>
                  <span
                    className="badge badge-outline"
                    style={{ fontSize: "0.75rem" }}
                  >
                    {tr.sourceName}
                  </span>
                </td>
                <td>
                  <span
                    className={`badge ${tr.status === "Alive" || tr.status === 1 ? "badge-success" : tr.status === "Offline" || tr.status === 3 ? "badge-danger" : "badge-secondary"}`}
                    style={{ fontSize: "0.75rem" }}
                  >
                    {tr.status}
                  </span>
                </td>
                <td style={{ fontFamily: "monospace" }}>
                  {tr.latencyMs > 0 ? `${tr.latencyMs}ms` : "-"}
                </td>
                <td>
                  {t("trackerBoost.radar.swarmsCountLower", "{count} swarms", {
                    count: tr.totalVerifiedTorrents ?? tr.totalSwarmsFound,
                  })}
                </td>
                <td style={{ textAlign: "right" }}>
                  <button
                    className="btn btn-sm btn-danger"
                    style={{
                      padding: "0.25rem 0.6rem",
                      fontSize: "0.75rem",
                    }}
                    onClick={() => deleteTracker.mutate(tr.id)}
                  >
                    {t("common.delete", "Delete")}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default RadarView;
