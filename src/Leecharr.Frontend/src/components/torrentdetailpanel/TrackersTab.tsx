import { useState, useMemo } from "react";
import {
  useTorrentTrackers,
  useTrackerBoostTrackers,
  useInspectTorrentTrackers,
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

export function TrackersTab({ torrentId }: { torrentId: number }) {
  const {
    data: trackers,
    isLoading,
    isError,
    refetch,
  } = useTorrentTrackers(torrentId);
  const { data: availableTrackers } = useTrackerBoostTrackers();
  const { data: inspection } = useInspectTorrentTrackers(
    torrentId,
    torrentId > 0,
  );
  const addTracker = useAddTorrentTracker();
  const deleteTracker = useDeleteTorrentTracker();
  const announceTracker = useAnnounceTorrentTracker();
  const { showToast } = useToast();
  const [showPickerModal, setShowPickerModal] = useState(false);
  const [selectedUrls, setSelectedUrls] = useState<Set<string>>(new Set());
  const [isAddingBatch, setIsAddingBatch] = useState(false);

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

      let statusLabel = "Untested";
      if (isAttached) {
        statusLabel = "Attached";
      } else if (isVerified) {
        statusLabel = `✓ Found in Swarm (${det?.seeders ?? 0}s / ${det?.leechers ?? 0}l)`;
      } else if (isAlive) {
        statusLabel = "Online (0 Peers)";
      } else if (isSlow) {
        statusLabel = `Slow (${tr.latencyMs > 0 ? tr.latencyMs + "ms" : "High Latency"})`;
      } else if (isOffline) {
        statusLabel = "Offline";
      }

      return {
        url: tr.url,
        host: tr.host,
        protocol: tr.protocol,
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
    if (!torrentId || selectedUrls.size === 0) return;

    setIsAddingBatch(true);
    let addedCount = 0;
    for (const url of Array.from(selectedUrls)) {
      try {
        await addTracker.mutateAsync({ torrentId, url });
        addedCount++;
      } catch {
        // continue
      }
    }
    setIsAddingBatch(false);
    setSelectedUrls(new Set());
    showToast(
      `Added ${addedCount} tracker(s) to torrent and triggered announce`,
      "success",
    );
    refetch();
  };

  const handleDeleteTracker = (trackerId: number) => {
    deleteTracker.mutate(
      { torrentId, trackerId },
      {
        onSuccess: () => {
          showToast("Tracker removed and reannounced", "success");
          refetch();
        },
        onError: (err) => {
          showToast(`Failed to remove tracker: ${err.message}`, "error");
        },
      },
    );
  };

  const handleAddTracker = async () => {
    if (!torrentId) return;

    if (selectedTracker === "all_verified") {
      const candidates = trackerOptions.filter(
        (o) => o.isVerified && !o.isAttached,
      );
      if (candidates.length === 0) {
        showToast("No verified candidate trackers found in swarm", "info");
        return;
      }
      let added = 0;
      for (const tr of candidates) {
        try {
          await addTracker.mutateAsync({ torrentId, url: tr.url });
          added++;
        } catch {
          // continue
        }
      }
      showToast(
        `Added ${added} verified tracker(s) to torrent and announced`,
        "success",
      );
      refetch();
    } else if (selectedTracker === "all_online") {
      const candidates = trackerOptions.filter(
        (o) => o.isAlive && !o.isAttached,
      );
      if (candidates.length === 0) {
        showToast("No unattached online trackers available", "info");
        return;
      }
      let added = 0;
      for (const tr of candidates) {
        try {
          await addTracker.mutateAsync({ torrentId, url: tr.url });
          added++;
        } catch {
          // continue
        }
      }
      showToast(
        `Added ${added} online tracker(s) to torrent and announced`,
        "success",
      );
      refetch();
    } else if (selectedTracker === "all") {
      const candidates = trackerOptions.filter((o) => !o.isAttached);
      if (candidates.length === 0) {
        showToast("All trackers are already attached", "info");
        return;
      }
      let added = 0;
      for (const tr of candidates) {
        try {
          await addTracker.mutateAsync({ torrentId, url: tr.url });
          added++;
        } catch {
          // continue
        }
      }
      showToast(
        `Added ${added} tracker(s) to torrent and announced`,
        "success",
      );
      refetch();
    } else if (selectedTracker) {
      addTracker.mutate(
        { torrentId, url: selectedTracker },
        {
          onSuccess: () => {
            showToast("Added tracker and announced successfully", "success");
            refetch();
          },
          onError: (err) => {
            showToast(`Failed to add tracker: ${err.message}`, "error");
          },
        },
      );
    }
  };

  if (isLoading) return <PanelLoading>Loading trackers...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load trackers.</PanelEmpty>;

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
        {!trackers || trackers.length === 0 ? (
          <PanelEmpty>No trackers attached to this torrent.</PanelEmpty>
        ) : (
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th">URL</th>
                <th className="torrent-table-th">Tier</th>
                <th className="torrent-table-th">Status</th>
                <th className="torrent-table-th">Seeders</th>
                <th className="torrent-table-th">Leechers</th>
                <th className="torrent-table-th">Announces</th>
                <th className="torrent-table-th">Last Announce</th>
                <th
                  className="torrent-table-th"
                  style={{ textAlign: "right", width: "90px" }}
                >
                  Action
                </th>
              </tr>
            </thead>
            <tbody>
              {trackers.map((t) => {
                const det = detectionMap.get(
                  (t.url ?? "").trim().toLowerCase(),
                );
                const ind = getAttachedTrackerIndicator(t.status, det);
                return (
                  <tr key={t.id} className="torrent-table-row">
                    <td className="mono" style={{ wordBreak: "break-all" }}>
                      <div
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        <TrackerFavicon urlOrHost={t.url} size={15} />
                        <span>{t.url}</span>
                      </div>
                    </td>
                    <td>{t.tier}</td>
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
                        <span>{t.status}</span>
                      </span>
                    </td>
                    <td>{t.seeders}</td>
                    <td>{t.leechers}</td>
                    <td>
                      {t.successfulAnnounces}/{t.totalAnnounces}
                    </td>
                    <td>{formatDate(t.lastAnnounce)}</td>
                    <td style={{ textAlign: "right" }}>
                      <div style={{ display: "inline-flex", gap: "0.3rem", justifyContent: "flex-end" }}>
                        <button
                          className="btn btn-sm btn-primary"
                          style={{
                            padding: "0.2rem 0.45rem",
                            fontSize: "0.72rem",
                          }}
                          onClick={() => {
                            announceTracker.mutate(
                              { torrentId, trackerId: t.id },
                              {
                                onSuccess: (data) => {
                                  showToast(data.message || "Announce queued", "success");
                                },
                                onError: (err) => {
                                  showToast(`Announce failed: ${err.message}`, "error");
                                },
                              },
                            );
                          }}
                          disabled={announceTracker.isPending}
                          title="Trigger immediate tracker announce"
                        >
                          {announceTracker.isPending ? "..." : "Announce"}
                        </button>
                        <button
                          className="btn btn-sm btn-danger"
                          style={{
                            padding: "0.2rem 0.45rem",
                            fontSize: "0.72rem",
                          }}
                          onClick={() => handleDeleteTracker(t.id)}
                          disabled={deleteTracker.isPending}
                          title="Remove tracker from torrent and reannounce"
                        >
                          Remove
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
          Add Tracker:
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
            cursor: "pointer",
          }}
          onClick={() => setShowPickerModal(true)}
          title="Open tracker picker to select, search, and filter trackers"
        >
          <span
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            <span>🎯</span>
            {selectedUrls.size === 0 ? (
              <span style={{ color: "var(--text-muted)" }}>
                Choose Trackers to Add... (0 Selected)
              </span>
            ) : (
              <span style={{ color: "var(--text-primary)", fontWeight: 600 }}>
                {selectedUrls.size} Tracker{selectedUrls.size === 1 ? "" : "s"}{" "}
                Selected (Click to change)
              </span>
            )}
          </span>

          <span
            className={`badge ${selectedUrls.size > 0 ? "badge-success" : "badge-secondary"}`}
            style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }}
          >
            {selectedUrls.size} Selected
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
            isAddingBatch ||
            selectedUrls.size === 0 ||
            !availableTrackers ||
            availableTrackers.length === 0
          }
          title="Add selected tracker(s) to this torrent and trigger announce"
        >
          {isAddingBatch
            ? "Adding..."
            : selectedUrls.size > 0
              ? `+ Add & Announce (${selectedUrls.size})`
              : "+ Add & Announce"}
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
