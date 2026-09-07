import { useState, useMemo } from "react";
import {
  useInspectTorrentTrackers,
  useInspectHashTrackers,
  useBoostTorrent,
  useBoostHash,
  useInjectTrackerToTorrent,
  useBoostAllTorrents,
  useHarvestDownloadTrackers,
  useScanTrackerBoostTrackers,
} from "../../api/hooks";
import { formatBytes, formatRatio } from "../../utils/formatters";
import { useToast } from "../../context/ToastContext";
import TrackerFavicon from "../../components/TrackerFavicon";
import { useTranslation } from "../../i18n";
import type { UnifiedDownloadItem } from "./types";

export interface HarvesterPanelProps {
  unifiedItems: UnifiedDownloadItem[];
  torrentsLoading?: boolean;
  selectedKey: string | null;
  onSelectKey: (key: string) => void;
}

export function HarvesterPanel({
  unifiedItems,
  torrentsLoading,
  selectedKey,
  onSelectKey,
}: HarvesterPanelProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();

  const [downloadFilter, setDownloadFilter] = useState<
    "all" | "public" | "private" | "real" | "leecharr"
  >("all");
  const [downloadSearch, setDownloadSearch] = useState("");

  const scanTrackers = useScanTrackerBoostTrackers();
  const harvestDownloads = useHarvestDownloadTrackers();
  const boostTorrent = useBoostTorrent();
  const boostHash = useBoostHash();
  const injectTracker = useInjectTrackerToTorrent();
  const boostAll = useBoostAllTorrents();

  const filteredDownloads = useMemo(() => {
    return unifiedItems.filter((item) => {
      if (downloadFilter === "public" && item.isPrivate) return false;
      if (downloadFilter === "private" && !item.isPrivate) return false;
      if (downloadFilter === "real" && item.sourceType !== "real_client")
        return false;
      if (downloadFilter === "leecharr" && item.sourceType !== "leecharr")
        return false;
      if (downloadSearch.trim()) {
        const q = downloadSearch.toLowerCase();
        const name = (item.name || "").toLowerCase();
        const hash = (item.infoHash || "").toLowerCase();
        if (!name.includes(q) && !hash.includes(q)) {
          return false;
        }
      }
      return true;
    });
  }, [unifiedItems, downloadFilter, downloadSearch]);

  const activeSelectedKey = selectedKey ?? filteredDownloads[0]?.key ?? "";
  const selectedItem = useMemo(() => {
    return filteredDownloads.find((i) => i.key === activeSelectedKey);
  }, [filteredDownloads, activeSelectedKey]);

  // Inspection hooks with live hash scraping
  const {
    data: torrentInspection,
    isLoading: torrentInspectLoading,
    refetch: refetchTorrentInspect,
  } = useInspectTorrentTrackers(
    selectedItem?.id ?? 0,
    Boolean(selectedItem?.id && selectedItem.id > 0),
  );

  const {
    data: hashInspection,
    isLoading: hashInspectLoading,
    refetch: refetchHashInspect,
  } = useInspectHashTrackers(
    selectedItem?.infoHash ?? "",
    selectedItem?.name ?? "",
    Boolean(!selectedItem?.id && selectedItem?.infoHash),
  );

  const inspection = selectedItem?.id ? torrentInspection : hashInspection;
  const inspectionLoading = selectedItem?.id
    ? torrentInspectLoading
    : hashInspectLoading;

  const handleScanAll = () => {
    scanTrackers.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "trackerBoost.probedEndpointsSuccess",
            "Probed {count} tracker endpoints",
            { count: res.testedCount },
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.probeTrackersFailed",
            "Failed to probe trackers: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleHarvestDownloads = () => {
    harvestDownloads.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "trackerBoost.harvestedTrackersSuccess",
            "Harvested {count} new trackers from active downloads",
            { count: res.harvestedCount },
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.harvestFailed",
            "Failed to harvest from downloads: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const handleBoostItem = (item: UnifiedDownloadItem) => {
    if (item.id && item.id > 0) {
      boostTorrent.mutate(item.id, {
        onSuccess: (res) => {
          showToast(res.message, res.boosted ? "success" : "info");
        },
        onError: (err) => {
          showToast(
            t("trackerBoost.boostFailed", "Failed to boost: {error}", {
              error: err.message,
            }),
            "error",
          );
        },
      });
    } else if (item.infoHash) {
      boostHash.mutate(
        { infoHash: item.infoHash, name: item.name },
        {
          onSuccess: (res) => {
            showToast(res.message, res.boosted ? "success" : "info");
          },
          onError: (err) => {
            showToast(
              t("trackerBoost.boostFailed", "Failed to boost: {error}", {
                error: err.message,
              }),
              "error",
            );
          },
        },
      );
    }
  };

  const handleInjectSingle = (trackerUrl: string) => {
    if (!selectedItem) return;

    injectTracker.mutate(
      {
        torrentId: selectedItem.id,
        infoHash: selectedItem.infoHash,
        trackerUrl,
      },
      {
        onSuccess: (res) => {
          showToast(res.message, "success");
        },
        onError: (err) => {
          showToast(
            t(
              "trackerBoost.injectFailed",
              "Failed to inject tracker: {error}",
              { error: err.message },
            ),
            "error",
          );
        },
      },
    );
  };

  const handleBoostAll = () => {
    boostAll.mutate(undefined, {
      onSuccess: (resList) => {
        const totalAdded = resList.reduce(
          (sum, r) => sum + r.addedTrackersCount,
          0,
        );
        const totalSeeds = resList.reduce(
          (sum, r) => sum + r.totalSeedersFound,
          0,
        );
        showToast(
          t(
            "trackerBoost.boostAllSuccess",
            "Boosted {swarms} swarms: injected {trackers} verified trackers (+{seeds} seeds discovered)",
            {
              swarms: resList.length,
              trackers: totalAdded,
              seeds: totalSeeds,
            },
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.boostDownloadsFailed",
            "Failed to boost downloads: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  return (
    <div
      style={{
        flex: "1 1 auto",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        marginBottom: "0.5rem",
      }}
    >
      {/* Action Toolbar */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.25rem",
          padding: "0.75rem 1rem",
          borderRadius: "8px",
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.6rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <button
            className="btn btn-primary"
            onClick={handleBoostAll}
            disabled={boostAll.isPending || filteredDownloads.length === 0}
            title={t(
              "trackerBoost.scrapeAllTooltip",
              "Scrape candidate trackers and inject only verified positive matches across all active downloads",
            )}
            style={{ padding: "0.45rem 1rem", fontWeight: 600 }}
          >
            {boostAll.isPending
              ? t("trackerBoost.scrapingBoosting", "⚡ Scraping & Boosting...")
              : t(
                  "trackerBoost.boostAllDownloads",
                  "⚡ Boost All Downloads (Verified Only)",
                )}
          </button>

          <button
            className="btn btn-action"
            onClick={handleHarvestDownloads}
            disabled={harvestDownloads.isPending}
            title={t(
              "trackerBoost.harvestTooltip",
              "Extract and discover tracker URLs from active download swarms in Leecharr and download clients",
            )}
          >
            {harvestDownloads.isPending
              ? t("trackerBoost.harvesting", "🔄 Harvesting...")
              : t(
                  "trackerBoost.harvestFromLiveSwarms",
                  "🔄 Harvest from Live Swarms",
                )}
          </button>

          <button
            className="btn btn-action"
            onClick={handleScanAll}
            disabled={scanTrackers.isPending}
            title={t(
              "trackerBoost.probeTooltip",
              "Ping and probe health across all monitored tracker endpoints",
            )}
          >
            {scanTrackers.isPending
              ? t("trackerBoost.probing", "📡 Probing...")
              : t("trackerBoost.probeAllTrackers", "📡 Probe All Trackers")}
          </button>
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            flexWrap: "wrap",
          }}
        >
          <select
            className="form-control"
            style={{
              width: "150px",
              padding: "0.35rem 0.6rem",
              fontSize: "0.82rem",
            }}
            value={downloadFilter}
            onChange={(e) => setDownloadFilter(e.target.value as any)}
          >
            <option value="all">
              {t("trackerBoost.filterAllSwarms", "All Swarms ({count})", {
                count: unifiedItems.length,
              })}
            </option>
            <option value="public">
              {t("trackerBoost.filterPublic", "Public ({count})", {
                count: unifiedItems.filter((i) => !i.isPrivate).length,
              })}
            </option>
            <option value="private">
              {t("trackerBoost.filterPrivate", "Private ({count})", {
                count: unifiedItems.filter((i) => i.isPrivate).length,
              })}
            </option>
          </select>
          <input
            type="text"
            className="form-control"
            style={{
              width: "200px",
              padding: "0.35rem 0.6rem",
              fontSize: "0.82rem",
            }}
            placeholder={t(
              "trackerBoost.searchDownloads",
              "Search downloads...",
            )}
            value={downloadSearch}
            onChange={(e) => setDownloadSearch(e.target.value)}
          />
        </div>
      </div>

      {/* Master-Detail Split: Left = Downloads List, Right = Live Tracker Scraper */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "360px 1fr",
          gap: "1.25rem",
          alignItems: "stretch",
          flex: "1 1 auto",
          minHeight: 0,
        }}
      >
        {/* Left: Downloads List */}
        <div
          className="card"
          style={{
            padding: "0.85rem",
            display: "flex",
            flexDirection: "column",
            height: "100%",
            minHeight: 0,
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "0.75rem",
              paddingBottom: "0.5rem",
              borderBottom: "1px solid var(--border-color)",
              flexShrink: 0,
            }}
          >
            <span style={{ fontWeight: 600, fontSize: "0.9rem" }}>
              {t("trackerBoost.swarmsCount", "Swarms ({count})", {
                count: filteredDownloads.length,
              })}
            </span>
            <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
              {t("trackerBoost.selectToInspect", "Select to inspect swarm")}
            </span>
          </div>

          {torrentsLoading ? (
            <div
              style={{
                padding: "2rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              {t("trackerBoost.loadingDownloads", "Loading downloads...")}
            </div>
          ) : filteredDownloads.length === 0 ? (
            <div
              style={{
                padding: "2rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              {t(
                "trackerBoost.noDownloadsMatch",
                "No downloads found matching filter.",
              )}
            </div>
          ) : (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.5rem",
                flex: "1 1 0",
                minHeight: 0,
                overflowY: "auto",
                paddingRight: "0.25rem",
              }}
            >
              {filteredDownloads.map((item) => {
                const isSelected = item.key === activeSelectedKey;
                return (
                  <div
                    key={item.key}
                    onClick={() => onSelectKey(item.key)}
                    style={{
                      padding: "0.75rem",
                      borderRadius: "6px",
                      cursor: "pointer",
                      backgroundColor: isSelected
                        ? "var(--accent-glow, rgba(56, 189, 248, 0.12))"
                        : "var(--bg-secondary, rgba(255,255,255,0.02))",
                      border: isSelected
                        ? "1px solid var(--accent, #38bdf8)"
                        : "1px solid var(--border-color)",
                      transition: "all 0.15s ease",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "flex-start",
                        gap: "0.5rem",
                        marginBottom: "0.35rem",
                      }}
                    >
                      <span
                        style={{
                          fontWeight: 600,
                          fontSize: "0.85rem",
                          wordBreak: "break-word",
                        }}
                      >
                        {item.name}
                      </span>
                      {item.isPrivate ? (
                        <span
                          className="badge badge-secondary"
                          style={{
                            fontSize: "0.7rem",
                            whiteSpace: "nowrap",
                          }}
                          title={t(
                            "trackerBoost.privateSwarmTooltip",
                            "Private tracker swarm",
                          )}
                        >
                          {t("trackerBoost.privateBadge", "🔒 Private")}
                        </span>
                      ) : (
                        <span
                          className="badge badge-success"
                          style={{
                            fontSize: "0.7rem",
                            whiteSpace: "nowrap",
                          }}
                          title={t(
                            "trackerBoost.publicSwarmTooltip",
                            "Public swarm boost eligible",
                          )}
                        >
                          {t("trackerBoost.publicBadge", "🌐 Public")}
                        </span>
                      )}
                    </div>
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        fontSize: "0.75rem",
                        color: "var(--text-muted)",
                      }}
                    >
                      <span>
                        {formatBytes(item.totalSize)} •{" "}
                        {t("trackerBoost.ratio", "Ratio")}:{" "}
                        {formatRatio(item.ratio)}
                      </span>
                      <span
                        style={{
                          color:
                            item.seeders > 0 ? "var(--success)" : "inherit",
                        }}
                      >
                        {t("trackerBoost.seedsCount", "{count} Seeds", {
                          count: item.seeders,
                        })}
                      </span>
                    </div>
                    <div
                      style={{
                        marginTop: "0.5rem",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      <span
                        className="badge badge-secondary"
                        style={{ fontSize: "0.7rem" }}
                      >
                        {item.clientName}
                      </span>
                      {!item.isPrivate ? (
                        <button
                          className="btn btn-sm btn-primary"
                          style={{
                            padding: "0.2rem 0.5rem",
                            fontSize: "0.75rem",
                          }}
                          onClick={(e) => {
                            e.stopPropagation();
                            handleBoostItem(item);
                          }}
                          title={t(
                            "trackerBoost.scrapeAndInjectTooltip",
                            "Scrape and inject verified trackers",
                          )}
                        >
                          {t("trackerBoost.enrich", "⚡ Enrich")}
                        </button>
                      ) : (
                        <span
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--text-dim)",
                          }}
                        >
                          {t("trackerBoost.protected", "Protected")}
                        </span>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        {/* Right: Live Scrape Inspector Pane */}
        <div
          className="card"
          style={{
            padding: "1.25rem",
            display: "flex",
            flexDirection: "column",
            height: "100%",
            minHeight: 0,
            overflow: "hidden",
          }}
        >
          {selectedItem ? (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                height: "100%",
                minHeight: 0,
                flex: "1 1 auto",
                overflow: "hidden",
              }}
            >
              {/* Selected Item Banner */}
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: "1rem",
                  marginBottom: "1rem",
                  paddingBottom: "0.75rem",
                  borderBottom: "1px solid var(--border-color)",
                  flexShrink: 0,
                }}
              >
                <div>
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                      marginBottom: "0.25rem",
                      flexWrap: "wrap",
                    }}
                  >
                    <h2 style={{ fontSize: "1.1rem", margin: 0 }}>
                      {selectedItem.name}
                    </h2>
                    {selectedItem.isPrivate ? (
                      <span
                        className="badge badge-secondary"
                        style={{ fontSize: "0.75rem" }}
                      >
                        {t(
                          "trackerBoost.privateSwarmBadge",
                          "🔒 Private Swarm",
                        )}
                      </span>
                    ) : (
                      <span
                        className="badge badge-success"
                        style={{ fontSize: "0.75rem" }}
                      >
                        {t("trackerBoost.publicSwarmBadge", "🌐 Public Swarm")}
                      </span>
                    )}
                  </div>
                  <div
                    style={{
                      fontSize: "0.8rem",
                      color: "var(--text-muted)",
                      fontFamily: "monospace",
                    }}
                  >
                    InfoHash: {selectedItem.infoHash}
                  </div>
                </div>
                <div style={{ display: "flex", gap: "0.5rem" }}>
                  <button
                    className="btn btn-action"
                    style={{ fontSize: "0.85rem" }}
                    onClick={() =>
                      selectedItem.id
                        ? refetchTorrentInspect()
                        : refetchHashInspect()
                    }
                    title={t(
                      "trackerBoost.rescrapeTooltip",
                      "Re-scrape candidate trackers for this info_hash",
                    )}
                  >
                    {t("trackerBoost.rescrapeSwarm", "🔄 Re-Scrape Swarm")}
                  </button>
                  {!selectedItem.isPrivate && (
                    <button
                      className="btn btn-primary"
                      style={{ fontSize: "0.85rem", fontWeight: 600 }}
                      onClick={() => handleBoostItem(selectedItem)}
                      title={t(
                        "trackerBoost.injectCandidateTooltip",
                        "Inject verified candidate trackers into this torrent",
                      )}
                    >
                      {t(
                        "trackerBoost.boostTorrentVerified",
                        "⚡ Boost Torrent (Inject Verified)",
                      )}
                    </button>
                  )}
                </div>
              </div>

              {selectedItem.isPrivate && (
                <div
                  style={{
                    padding: "0.75rem 1rem",
                    marginBottom: "1rem",
                    borderRadius: "6px",
                    backgroundColor: "rgba(230, 126, 34, 0.12)",
                    border: "1px solid rgba(230, 126, 34, 0.35)",
                    color: "var(--text-primary)",
                    fontSize: "0.85rem",
                    display: "flex",
                    alignItems: "center",
                    gap: "0.75rem",
                    flexShrink: 0,
                  }}
                >
                  <span style={{ fontSize: "1.25rem" }}>🔒</span>
                  <div>
                    <strong>
                      {t(
                        "trackerBoost.privateSwarmWarningStrong",
                        "Private Tracker Swarm:",
                      )}
                    </strong>{" "}
                    {t(
                      "trackerBoost.privateSwarmWarningText",
                      "Cross-swarm public tracker injection is protected and disabled to comply with BitTorrent private tracker rules (BEP 27). Attached private trackers and health metrics are displayed below.",
                    )}
                  </div>
                </div>
              )}

              {/* Scrape Results Overview */}
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(130px, 1fr))",
                  gap: "0.75rem",
                  marginBottom: "1rem",
                  flexShrink: 0,
                }}
              >
                <div className="stat-card" style={{ padding: "0.75rem" }}>
                  <div className="stat-value" style={{ fontSize: "1.25rem" }}>
                    {inspection?.attachedTrackersCount ?? 0}
                  </div>
                  <div className="stat-label" style={{ fontSize: "0.75rem" }}>
                    {t("trackerBoost.attachedTrackers", "Attached Trackers")}
                  </div>
                </div>
                <div className="stat-card" style={{ padding: "0.75rem" }}>
                  <div
                    className="stat-value"
                    style={{ fontSize: "1.25rem", color: "var(--success)" }}
                  >
                    {inspection?.verifiedTrackersCount ?? 0}
                  </div>
                  <div className="stat-label" style={{ fontSize: "0.75rem" }}>
                    {t(
                      "trackerBoost.verifiedCandidates",
                      "Verified Candidates",
                    )}
                  </div>
                </div>
                <div className="stat-card" style={{ padding: "0.75rem" }}>
                  <div className="stat-value" style={{ fontSize: "1.25rem" }}>
                    {inspection?.totalTrackersChecked ?? 0}
                  </div>
                  <div className="stat-label" style={{ fontSize: "0.75rem" }}>
                    {t("trackerBoost.totalChecked", "Total Checked")}
                  </div>
                </div>
              </div>

              {/* Candidate Trackers Table */}
              {inspectionLoading ? (
                <div
                  style={{
                    padding: "3rem",
                    textAlign: "center",
                    color: "var(--text-muted)",
                  }}
                >
                  {t(
                    "trackerBoost.scrapingCandidateForHash",
                    "Scraping candidate trackers for hash {hash}...",
                    { hash: (selectedItem?.infoHash || "").slice(0, 8) },
                  )}
                </div>
              ) : (
                <div
                  className="torrent-table-wrapper"
                  style={{
                    borderRadius: "6px",
                    border: "1px solid var(--border)",
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
                        <th
                          className="torrent-table-th"
                          style={{ width: "35%" }}
                        >
                          {t("trackerBoost.trackerUrl", "Tracker URL")}
                        </th>
                        <th
                          className="torrent-table-th"
                          style={{ width: "10%" }}
                        >
                          {t("trackerBoost.protocol", "Protocol")}
                        </th>
                        <th
                          className="torrent-table-th"
                          style={{ width: "10%" }}
                        >
                          {t("trackerBoost.latency", "Latency")}
                        </th>
                        <th
                          className="torrent-table-th"
                          style={{ width: "25%" }}
                        >
                          {t(
                            "trackerBoost.statusDetection",
                            "Status / Detection",
                          )}
                        </th>
                        <th
                          className="torrent-table-th"
                          style={{ width: "15%" }}
                        >
                          {t("trackerBoost.peers", "Peers")}
                        </th>
                        <th
                          className="torrent-table-th"
                          style={{ textAlign: "right" }}
                        >
                          {t("trackerBoost.action", "Action")}
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {(inspection?.detections ?? []).map((det) => (
                        <tr
                          key={det.trackerId || det.trackerUrl}
                          className="torrent-table-row"
                          style={{
                            opacity:
                              det.healthStatus === "Offline" ||
                              det.healthStatus === 3
                                ? 0.6
                                : 1,
                          }}
                        >
                          <td
                            style={{
                              maxWidth: "280px",
                              wordBreak: "break-all",
                              fontFamily: "monospace",
                              fontSize: "0.8rem",
                            }}
                          >
                            <div
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.45rem",
                              }}
                            >
                              <TrackerFavicon
                                urlOrHost={det.trackerUrl}
                                size={15}
                              />
                              <span>{det.trackerUrl}</span>
                            </div>
                          </td>
                          <td>
                            <span
                              className="badge badge-secondary"
                              style={{ fontSize: "0.75rem" }}
                            >
                              {det.protocol}
                            </span>
                          </td>
                          <td style={{ fontFamily: "monospace" }}>
                            {det.latencyMs > 0 ? `${det.latencyMs}ms` : "-"}
                          </td>
                          <td>
                            {det.isAttached ? (
                              <span
                                className="badge badge-primary"
                                style={{ fontSize: "0.75rem" }}
                              >
                                {t("trackerBoost.attached", "Attached")}
                              </span>
                            ) : det.isVerified ? (
                              <span
                                className="badge badge-success"
                                style={{ fontSize: "0.75rem" }}
                              >
                                {t(
                                  "trackerBoost.verifiedMatch",
                                  "✓ Verified Match",
                                )}
                              </span>
                            ) : (
                              <span
                                className="badge badge-secondary"
                                style={{ fontSize: "0.75rem" }}
                              >
                                {det.detectionStatus}
                              </span>
                            )}
                          </td>
                          <td>
                            <span
                              style={{
                                color:
                                  det.seeders > 0
                                    ? "var(--success)"
                                    : "inherit",
                                fontWeight: 600,
                              }}
                            >
                              {t(
                                "trackerBoost.seedsCountLower",
                                "{count} seeds",
                                { count: det.seeders },
                              )}
                            </span>{" "}
                            /{" "}
                            <span
                              style={{
                                color:
                                  det.leechers > 0
                                    ? "var(--accent)"
                                    : "inherit",
                              }}
                            >
                              {t(
                                "trackerBoost.leechesCountLower",
                                "{count} leeches",
                                { count: det.leechers },
                              )}
                            </span>
                          </td>
                          <td
                            style={{
                              textAlign: "right",
                              whiteSpace: "nowrap",
                            }}
                          >
                            {det.isAttached ? (
                              <span
                                className="badge badge-primary"
                                style={{
                                  fontSize: "0.72rem",
                                  padding: "0.25rem 0.5rem",
                                }}
                              >
                                {t("trackerBoost.attachedBadge", "✓ Attached")}
                              </span>
                            ) : selectedItem.isPrivate ? (
                              <span
                                className="badge badge-secondary"
                                title={t(
                                  "trackerBoost.privateGuardTitle",
                                  "BEP 27: Public tracker injection is disabled for private torrents",
                                )}
                                style={{
                                  fontSize: "0.72rem",
                                  padding: "0.25rem 0.5rem",
                                  opacity: 0.8,
                                }}
                              >
                                {t(
                                  "trackerBoost.privateGuardBadge",
                                  "🔒 Private Guard",
                                )}
                              </span>
                            ) : det.isVerified ? (
                              <button
                                className="btn btn-sm btn-primary"
                                onClick={() =>
                                  handleInjectSingle(det.trackerUrl)
                                }
                                title={t(
                                  "trackerBoost.injectThisTooltip",
                                  "Inject this verified tracker into the torrent",
                                )}
                              >
                                {t("trackerBoost.inject", "⚡ Inject")}
                              </button>
                            ) : det.healthStatus === "Offline" ||
                              det.healthStatus === 3 ? (
                              <span
                                style={{
                                  color: "var(--text-dim)",
                                  fontSize: "0.75rem",
                                }}
                              >
                                {t("trackerBoost.offline", "Offline")}
                              </span>
                            ) : (
                              <span
                                style={{
                                  color: "var(--text-dim)",
                                  fontSize: "0.75rem",
                                }}
                              >
                                —
                              </span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          ) : (
            <div
              style={{
                padding: "4rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              {t(
                "trackerBoost.selectDownloadHint",
                "Select a download from the left list to inspect live tracker scrape results.",
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default HarvesterPanel;
