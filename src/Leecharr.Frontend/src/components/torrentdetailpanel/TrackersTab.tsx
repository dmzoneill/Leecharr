import { useState, useMemo } from "react";
import { useTranslation } from "../../i18n";
import {
  useTorrentTrackers,
  useTrackerBoostTrackers,
  useInspectTorrentTrackers,
  useBoostTorrent,
  useAddTorrentTracker,
  useDeleteTorrentTracker,
  useAnnounceTorrentTracker,
} from "../../api/hooks";
import { formatDate } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";
import { useToast } from "../../context/ToastContext";
import TrackerFavicon from "../TrackerFavicon";
import TrackerMultiSelectModal, {
  TrackerPickerItem,
} from "../TrackerMultiSelectModal";

function getAttachedTrackerIndicator(
  status: string,
  det?: { isVerified?: boolean; healthStatus?: string | number },
): {
  icon: string;
  badgeClass: string;
} {
  const isQueued = status === "Queued" || status === "Pending";
  const isWorking =
    status === "Working" ||
    status === "Announcing" ||
    det?.isVerified ||
    det?.healthStatus === "Alive" ||
    det?.healthStatus === 1;
  const isFailed =
    status === "Failed" ||
    status === "Disabled" ||
    det?.healthStatus === "Offline" ||
    det?.healthStatus === 3;
  const isSlow = det?.healthStatus === "Slow" || det?.healthStatus === 2;

  if (isQueued) {
    return {
      icon: "⏳",
      badgeClass: "badge-queued",
    };
  }
  if (isWorking) {
    return {
      icon: "🟢",
      badgeClass:
        status === "Announcing" ? "badge-announcing" : "badge-seeding",
    };
  }
  if (isFailed) {
    return {
      icon: "🔴",
      badgeClass: "badge-error",
    };
  }
  if (isSlow) {
    return {
      icon: "🟡",
      badgeClass: "badge-warning",
    };
  }
  return {
    icon: "🟡",
    badgeClass: "badge-warning",
  };
}

export function TrackersTab({
  torrent,
  torrentId,
}: {
  torrent?: { id: number; isPrivate?: boolean };
  torrentId?: number;
}) {
  const { t } = useTranslation();
  const effectiveId = torrentId ?? torrent?.id ?? 0;
  const {
    data: trackers,
    isLoading,
    isError,
    refetch,
  } = useTorrentTrackers(effectiveId);
  const { data: availableTrackers } = useTrackerBoostTrackers();
  const { data: inspection } = useInspectTorrentTrackers(
    effectiveId,
    effectiveId > 0,
  );
  const addTracker = useAddTorrentTracker();
  const deleteTracker = useDeleteTorrentTracker();
  const announceTracker = useAnnounceTorrentTracker();
  const boostTorrent = useBoostTorrent();
  const { showToast } = useToast();
  const [showPickerModal, setShowPickerModal] = useState(false);
  const [selectedUrls, setSelectedUrls] = useState<Set<string>>(new Set());
  const [isAddingBatch, setIsAddingBatch] = useState(false);
  const isPrivate = Boolean(
    torrent?.isPrivate || (inspection as any)?.isPrivate,
  );

  const handleBoostSwarm = () => {
    if (!effectiveId || isPrivate) return;
    boostTorrent.mutate(effectiveId, {
      onSuccess: (res) => {
        showToast(res.message, res.boosted ? "success" : "info");
        refetch();
      },
      onError: (err) => {
        showToast(
          t(
            "torrents.detail.failedToBoostSwarm",
            "Failed to boost swarm: {error}",
            { error: err.message },
          ),
          "error",
        );
      },
    });
  };

  const attachedUrls = useMemo(() => {
    return new Set(
      (trackers ?? []).map((t) => (t.url ?? "").trim().toLowerCase()),
    );
  }, [trackers]);

  const detectionMap = useMemo(() => {
    const map = new Map<
      string,
      NonNullable<typeof inspection>["detections"][number]
    >();
    (inspection?.detections ?? []).forEach((d) => {
      if (d.trackerUrl) {
        map.set(d.trackerUrl.trim().toLowerCase(), d);
      }
    });
    return map;
  }, [inspection]);

  const pickerTrackers = useMemo<TrackerPickerItem[]>(() => {
    return (availableTrackers ?? []).map((tr) => {
      const cleanUrl = (tr.url ?? "").trim().toLowerCase();
      const det = detectionMap.get(cleanUrl);
      const isAttached = attachedUrls.has(cleanUrl) || det?.isAttached || false;
      const isVerified = det?.isVerified || false;
      const isAlive =
        tr.status === "Alive" ||
        tr.status === 1 ||
        det?.healthStatus === "Alive" ||
        det?.healthStatus === 1 ||
        false;
      const isSlow =
        tr.status === "Slow" ||
        tr.status === 2 ||
        det?.healthStatus === "Slow" ||
        det?.healthStatus === 2 ||
        false;
      const isOffline =
        tr.status === "Offline" ||
        tr.status === 3 ||
        det?.healthStatus === "Offline" ||
        det?.healthStatus === 3 ||
        false;

      let statusLabel = t("trackerStatus.untested", "Untested");
      if (isAttached) {
        statusLabel = t("trackerStatus.attached", "Attached");
      } else if (isVerified) {
        statusLabel = `✓ Found in Swarm (${det?.seeders ?? 0}s / ${det?.leechers ?? 0}l)`;
      } else if (isAlive) {
        statusLabel = t("trackerStatus.online", "Online (0 Peers)");
      } else if (isSlow) {
        statusLabel = `${t("trackerStatus.slow", "Slow")} (${tr.latencyMs > 0 ? tr.latencyMs + "ms" : "High Latency"})`;
      } else if (isOffline) {
        statusLabel = t("trackerStatus.offline", "Offline");
      }

      return {
        url: tr.url,
        host: tr.host,
        protocol: String(tr.protocol ?? ""),
        isAttached,
        isVerified,
        isAlive,
        isSlow,
        isOffline,
        latencyMs: tr.latencyMs,
        seeders: det?.seeders,
        leechers: det?.leechers,
        statusLabel,
      };
    });
  }, [availableTrackers, detectionMap, attachedUrls]);

  const handleToggleUrl = (url: string) => {
    setSelectedUrls((prev) => {
      const next = new Set(prev);
      if (next.has(url)) next.delete(url);
      else next.add(url);
      return next;
    });
  };

  const handleSelectBatch = (urls: string[]) => {
    setSelectedUrls((prev) => {
      const next = new Set(prev);
      urls.forEach((u) => next.add(u));
      return next;
    });
  };

  const handleClearSelection = () => {
    setSelectedUrls(new Set());
  };

  const handleAddAndAnnounceSelected = async () => {
    if (!effectiveId || isPrivate || selectedUrls.size === 0) return;

    setIsAddingBatch(true);
    let addedCount = 0;
    const errors: string[] = [];
    for (const url of Array.from(selectedUrls)) {
      try {
        await addTracker.mutateAsync({ torrentId: effectiveId, url });
        addedCount++;
      } catch (err: any) {
        const rawMsg =
          err?.response?.data?.message ||
          (typeof err?.response?.data === "string" && err.response.data.trim()
            ? err.response.data.trim()
            : null) ||
          err?.message ||
          t("torrents.detail.failedToAddTracker", "Failed to add tracker");
        errors.push(selectedUrls.size > 1 ? `${rawMsg} (${url})` : rawMsg);
      }
    }
    setIsAddingBatch(false);
    setSelectedUrls(new Set());
    if (addedCount > 0) {
      showToast(
        t(
          "torrents.detail.addedTrackersQueued",
          "Added {count} tracker(s) to torrent and queued announce",
          { count: addedCount },
        ),
        "success",
      );
    }
    if (errors.length > 0) {
      errors.forEach((e) => showToast(e, "error"));
    }
    refetch();
  };

  const handleDeleteTracker = (trackerId: number) => {
    if (!effectiveId) return;
    deleteTracker.mutate(
      { torrentId: effectiveId, trackerId },
      {
        onSuccess: () => {
          showToast(
            t(
              "torrents.detail.trackerRemovedSuccess",
              "Tracker removed and reannounced",
            ),
            "success",
          );
          refetch();
        },
        onError: (err) => {
          showToast(
            t(
              "torrents.detail.failedToRemoveTracker",
              "Failed to remove tracker: {error}",
              { error: err.message },
            ),
            "error",
          );
        },
      },
    );
  };

  if (isLoading)
    return <PanelLoading>{t("torrents.detail.loadingTrackers")}</PanelLoading>;
  if (isError)
    return <PanelEmpty>{t("torrents.detail.failedToLoadTrackers")}</PanelEmpty>;

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "calc(100% + 1rem)",
        margin: "-0.5rem -0.75rem",
        overflow: "hidden",
      }}
    >
      <div
        className="detail-panel-table-wrap"
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: "auto",
          padding: "0.5rem 0.75rem",
        }}
      >
        {(torrent?.isPrivate || (inspection as any)?.isPrivate) && (
          <div
            style={{
              padding: "0.45rem 0.65rem",
              marginBottom: "0.5rem",
              borderRadius: "4px",
              fontSize: "0.72rem",
              backgroundColor: "rgba(239, 68, 68, 0.12)",
              border: "1px solid rgba(239, 68, 68, 0.3)",
              color: "#fca5a5",
              display: "flex",
              alignItems: "center",
              gap: "6px",
            }}
          >
            <i className="fas fa-lock" />
            <span>{t("torrents.detail.trackersPrivateBanner")}</span>
          </div>
        )}
        {!trackers || trackers.length === 0 ? (
          <PanelEmpty>{t("torrents.detail.noTrackers")}</PanelEmpty>
        ) : (
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th">
                  {t("torrents.detail.colUrl")}
                </th>
                <th className="torrent-table-th">
                  {t("torrents.detail.colTier")}
                </th>
                <th className="torrent-table-th">
                  {t("torrents.detail.status")}
                </th>
                <th className="torrent-table-th">
                  {t("torrents.detail.connectedSeeders")}
                </th>
                <th className="torrent-table-th">
                  {t("torrents.detail.connectedLeechers")}
                </th>
                <th className="torrent-table-th">
                  {t("torrents.detail.colInterval")}
                </th>
                <th className="torrent-table-th">
                  {t("torrents.detail.colLastAnnounce")}
                </th>
                <th className="torrent-table-th">
                  {t("torrents.detail.colNextAnnounce")}
                </th>
                <th
                  className="torrent-table-th"
                  style={{ textAlign: "right", width: "90px" }}
                >
                  {t("torrents.detail.colAction")}
                </th>
              </tr>
            </thead>
            <tbody>
              {trackers.map((tItem) => {
                const det = detectionMap.get(
                  (tItem.url ?? "").trim().toLowerCase(),
                );
                const ind = getAttachedTrackerIndicator(tItem.status, det);
                return (
                  <tr key={tItem.id} className="torrent-table-row">
                    <td className="mono" style={{ wordBreak: "break-all" }}>
                      <div
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        <TrackerFavicon urlOrHost={tItem.url} size={15} />
                        <span>{tItem.url}</span>
                      </div>
                    </td>
                    <td>{tItem.tier}</td>
                    <td>
                      <span
                        className={`badge ${ind.badgeClass}`}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.35rem",
                        }}
                      >
                        <span style={{ fontSize: "0.85em" }}>{ind.icon}</span>
                        <span>
                          {t(
                            "trackerStatus." +
                              (tItem.status || "queued").toLowerCase(),
                            tItem.status,
                          )}
                        </span>
                      </span>
                    </td>
                    <td>{tItem.seeders}</td>
                    <td>{tItem.leechers}</td>
                    <td>
                      {tItem.announceInterval
                        ? `${tItem.announceInterval}s`
                        : "1800s"}
                    </td>
                    <td>
                      {tItem.lastAnnounce
                        ? formatDate(tItem.lastAnnounce)
                        : t("common.never", "Never")}
                    </td>
                    <td>
                      {tItem.nextAnnounce
                        ? formatDate(tItem.nextAnnounce)
                        : t("torrents.states.queued", "Queued")}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <div
                        style={{
                          display: "inline-flex",
                          gap: "0.3rem",
                          justifyContent: "flex-end",
                        }}
                      >
                        <button
                          className="btn btn-sm btn-primary"
                          style={{
                            padding: "0.2rem 0.45rem",
                            fontSize: "0.72rem",
                          }}
                          onClick={() => {
                            if (!effectiveId) return;
                            announceTracker.mutate(
                              { torrentId: effectiveId, trackerId: tItem.id },
                              {
                                onSuccess: (data) => {
                                  showToast(
                                    data.message || "Announce queued",
                                    "success",
                                  );
                                  refetch();
                                },
                                onError: (err) => {
                                  showToast(
                                    `Announce failed: ${err.message}`,
                                    "error",
                                  );
                                },
                              },
                            );
                          }}
                          disabled={announceTracker.isPending}
                          title={t(
                            "torrentDetail.triggerAnnounce",
                            "Trigger immediate tracker announce",
                          )}
                        >
                          {announceTracker.isPending
                            ? "..."
                            : t("torrents.detail.announceBtn")}
                        </button>
                        <button
                          className="btn btn-sm btn-danger"
                          style={{
                            padding: "0.2rem 0.45rem",
                            fontSize: "0.72rem",
                          }}
                          onClick={() => handleDeleteTracker(tItem.id)}
                          disabled={deleteTracker.isPending}
                          title={t(
                            "torrentDetail.removeTracker",
                            "Remove tracker from torrent and reannounce",
                          )}
                        >
                          {t("torrents.detail.removeBtn")}
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Action bar pinned flush to bottom, left and right */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "0.5rem",
          padding: "0.5rem 0.75rem",
          borderTop: "1px solid var(--border-light)",
          backgroundColor: "var(--bg-secondary)",
          flexShrink: 0,
          flexWrap: "wrap",
        }}
      >
        <label
          style={{
            fontSize: "0.82rem",
            fontWeight: 500,
            color: "var(--text-secondary)",
            whiteSpace: "nowrap",
          }}
        >
          {t("torrents.detail.addTrackerLabel")}
        </label>

        <button
          type="button"
          className="form-control btn-action"
          style={{
            flex: "1 1 280px",
            maxWidth: "520px",
            padding: "0.35rem 0.75rem",
            fontSize: "0.82rem",
            textAlign: "left",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            cursor: isPrivate ? "not-allowed" : "pointer",
            opacity: isPrivate ? 0.6 : 1,
          }}
          onClick={() => {
            if (!isPrivate) setShowPickerModal(true);
          }}
          disabled={isPrivate}
          title={
            isPrivate
              ? t(
                  "torrents.detail.privateTrackerDisabledTitle",
                  "Adding public trackers is disabled on private swarms (BEP 27)",
                )
              : t(
                  "torrents.detail.openTrackerPickerTitle",
                  "Open tracker picker to select, search, and filter trackers",
                )
          }
        >
          <span
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            <span>🎯</span>
            {selectedUrls.size === 0 ? (
              <span style={{ color: "var(--text-muted)" }}>
                {t("torrents.detail.chooseTrackers")}
              </span>
            ) : (
              <span style={{ color: "var(--text-primary)", fontWeight: 600 }}>
                {t("torrents.detail.selectedTrackers", {
                  count: selectedUrls.size,
                  plural: selectedUrls.size === 1 ? "" : "s",
                })}
              </span>
            )}
          </span>

          <span
            className={`badge ${selectedUrls.size > 0 ? "badge-success" : "badge-secondary"}`}
            style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }}
          >
            {t("torrents.detail.selectedBadge", { count: selectedUrls.size })}
          </span>
        </button>

        <button
          className="btn btn-sm btn-primary"
          style={{
            fontSize: "0.82rem",
            padding: "0.35rem 0.85rem",
            whiteSpace: "nowrap",
          }}
          onClick={handleAddAndAnnounceSelected}
          disabled={
            isPrivate ||
            isAddingBatch ||
            selectedUrls.size === 0 ||
            !availableTrackers ||
            availableTrackers.length === 0
          }
          title={
            isPrivate
              ? "Adding public trackers is disabled on private swarms (BEP 27)"
              : "Add selected tracker(s) to this torrent and trigger announce"
          }
        >
          {isAddingBatch
            ? t("torrents.detail.adding")
            : selectedUrls.size > 0
              ? t("torrents.detail.addAndAnnounceCount", {
                  count: selectedUrls.size,
                })
              : t("torrents.detail.addAndAnnounce")}
        </button>

        <button
          className="btn btn-sm"
          style={{
            fontSize: "0.82rem",
            padding: "0.35rem 0.85rem",
            whiteSpace: "nowrap",
            backgroundColor: "rgba(255, 209, 102, 0.15)",
            color: "var(--accent, #ffd166)",
            border: "1px solid rgba(255, 209, 102, 0.35)",
            fontWeight: 600,
          }}
          onClick={handleBoostSwarm}
          disabled={isPrivate || boostTorrent.isPending || !effectiveId}
          title={
            isPrivate
              ? "Swarm boost is disabled for private torrents (BEP 27)"
              : "Automatically detect and inject verified public trackers into this torrent swarm"
          }
        >
          {boostTorrent.isPending
            ? t("torrents.detail.boosting")
            : t("torrents.detail.boostSwarm")}
        </button>
      </div>

      <TrackerMultiSelectModal
        isOpen={showPickerModal}
        onClose={() => setShowPickerModal(false)}
        trackers={pickerTrackers}
        selectedUrls={selectedUrls}
        onToggleUrl={handleToggleUrl}
        onSelectBatch={handleSelectBatch}
        onClearSelection={handleClearSelection}
        onAddAndAnnounce={handleAddAndAnnounceSelected}
        isAdding={isAddingBatch}
      />
    </div>
  );
}
