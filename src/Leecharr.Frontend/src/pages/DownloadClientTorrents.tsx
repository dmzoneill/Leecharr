import { useState, useMemo, useEffect } from "react";
import { useParams, Link, useNavigate } from "react-router";
import {
  useDownloadClients,
  useDownloadClientItems,
  useImportDownloadClientTorrent,
  useImportDownloadClientTorrents,
  useDownloadHistory,
  useArrConnections,
  useBoostHash,
  usePauseRemoteTorrent,
  useResumeRemoteTorrent,
  useDeleteRemoteTorrent,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { formatBytes } from "../utils/formatters";
import { getMediaDeepLink, getDownloadClientUrl } from "../utils/arrLinks";
import type { BatchImportItemResult } from "../api/types";

export default function DownloadClientTorrents() {
  const { id } = useParams<{ id: string }>();
  const isAll = id === "all";
  const clientId = isAll ? 0 : parseInt(id || "0", 10);
  const navigate = useNavigate();
  const { showToast } = useToast();

  const { data: clients, isLoading: clientsLoading } = useDownloadClients();
  const client = useMemo(
    () => (isAll ? null : clients?.find((c) => c.id === clientId)),
    [clients, clientId, isAll],
  );

  const {
    data: items,
    isLoading: itemsLoading,
    isError,
    error,
    refetch,
    isFetching,
  } = useDownloadClientItems(isAll ? "all" : clientId);

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const importOneMutation = useImportDownloadClientTorrent(clientId);
  const importAllMutation = useImportDownloadClientTorrents(clientId);
  const boostHashMutation = useBoostHash();
  const pauseRemoteMutation = usePauseRemoteTorrent(clientId);
  const resumeRemoteMutation = useResumeRemoteTorrent(clientId);
  const deleteRemoteMutation = useDeleteRemoteTorrent(clientId);

  const [deleteTarget, setDeleteTarget] = useState<{
    clientId: number;
    clientName?: string;
    infoHash: string;
    title: string;
  } | null>(null);
  const [deleteFiles, setDeleteFiles] = useState(false);

  const duplicateHashes = useMemo(() => {
    if (!items) return new Set<string>();
    const counts = new Map<string, number>();
    for (const item of items) {
      if (item.infoHash) {
        const h = item.infoHash.toLowerCase();
        counts.set(h, (counts.get(h) || 0) + 1);
      }
    }
    const dupes = new Set<string>();
    for (const [hash, count] of counts.entries()) {
      if (count > 1) {
        dupes.add(hash);
      }
    }
    return dupes;
  }, [items]);

  const handlePauseTorrent = (
    targetClientId: number,
    hash: string,
    title: string,
  ) => {
    pauseRemoteMutation.mutate(
      { clientId: targetClientId, infoHash: hash },
      {
        onSuccess: () => {
          showToast(`Paused "${title}"`, "success");
        },
        onError: (err) => {
          showToast(`Failed to pause "${title}": ${err.message}`, "error");
        },
      },
    );
  };

  const handleResumeTorrent = (
    targetClientId: number,
    hash: string,
    title: string,
  ) => {
    resumeRemoteMutation.mutate(
      { clientId: targetClientId, infoHash: hash },
      {
        onSuccess: () => {
          showToast(`Resumed "${title}"`, "success");
        },
        onError: (err) => {
          showToast(`Failed to resume "${title}": ${err.message}`, "error");
        },
      },
    );
  };

  const confirmDeleteTorrent = () => {
    if (!deleteTarget) return;
    deleteRemoteMutation.mutate(
      {
        clientId: deleteTarget.clientId,
        infoHash: deleteTarget.infoHash,
        deleteData: deleteFiles,
      },
      {
        onSuccess: () => {
          showToast(
            `Removed "${deleteTarget.title}" from download client`,
            "success",
          );
          setDeleteTarget(null);
        },
        onError: (err) => {
          showToast(
            `Failed to delete "${deleteTarget.title}": ${err.message}`,
            "error",
          );
          setDeleteTarget(null);
        },
      },
    );
  };

  const handleBoostTorrent = (
    hash: string,
    title: string,
    isPrivate?: boolean,
  ) => {
    if (isPrivate) {
      showToast(
        "Tracker boosting is prohibited on private torrents to protect tracker rules and passkeys (BEP 27).",
        "info",
      );
      return;
    }

    boostHashMutation.mutate(
      { infoHash: hash, name: title },
      {
        onSuccess: (res) => {
          showToast(res.message, res.boosted ? "success" : "info");
        },
        onError: (err) => {
          showToast(`Failed to boost swarm: ${err.message}`, "error");
        },
      },
    );
  };

  const [viewMode, setViewMode] = useState<"grid" | "table">("table");
  const [searchTerm, setSearchTerm] = useState("");
  const [filterMode, setFilterMode] = useState<"all" | "missing" | "library">(
    "all",
  );
  const [importingHash, setImportingHash] = useState<string | null>(null);
  const [selectedHashes, setSelectedHashes] = useState<Set<string>>(new Set());
  const [importingSelected, setImportingSelected] = useState(false);
  const [failedImportItems, setFailedImportItems] = useState<
    BatchImportItemResult[] | null
  >(null);

  useEffect(() => {
    setSearchTerm("");
    setFilterMode("all");
    setSelectedHashes(new Set());
    setFailedImportItems(null);
  }, [id]);

  const totalCount = items?.length || 0;
  const inLibraryCount = items?.filter((i) => i.isInLibrary).length || 0;
  const missingCount = totalCount - inLibraryCount;

  const filteredItems = useMemo(() => {
    if (!items) return [];
    return items.filter((item) => {
      if (filterMode === "missing" && item.isInLibrary) return false;
      if (filterMode === "library" && !item.isInLibrary) return false;
      if (!searchTerm.trim()) return true;

      const term = searchTerm.toLowerCase();
      return (
        item.title.toLowerCase().includes(term) ||
        (item.category && item.category.toLowerCase().includes(term)) ||
        item.infoHash.toLowerCase().includes(term) ||
        (item.outputPath && item.outputPath.toLowerCase().includes(term))
      );
    });
  }, [items, filterMode, searchTerm]);

  const visibleMissingItems = useMemo(
    () => filteredItems.filter((i) => !i.isInLibrary && i.infoHash),
    [filteredItems],
  );

  const allMissingSelected = useMemo(
    () =>
      visibleMissingItems.length > 0 &&
      visibleMissingItems.every((i) => selectedHashes.has(i.infoHash)),
    [visibleMissingItems, selectedHashes],
  );

  const someMissingSelected = useMemo(
    () => visibleMissingItems.some((i) => selectedHashes.has(i.infoHash)),
    [visibleMissingItems, selectedHashes],
  );

  const handleToggleSelectAll = () => {
    if (allMissingSelected) {
      setSelectedHashes((prev) => {
        const next = new Set(prev);
        visibleMissingItems.forEach((i) => next.delete(i.infoHash));
        return next;
      });
    } else {
      setSelectedHashes((prev) => {
        const next = new Set(prev);
        visibleMissingItems.forEach((i) => next.add(i.infoHash));
        return next;
      });
    }
  };

  const handleToggleSelect = (hash: string) => {
    setSelectedHashes((prev) => {
      const next = new Set(prev);
      if (next.has(hash)) {
        next.delete(hash);
      } else {
        next.add(hash);
      }
      return next;
    });
  };

  const handleImportOne = (
    hash: string,
    title: string,
    targetClientId?: number,
  ) => {
    setImportingHash(hash);
    importOneMutation.mutate(
      { infoHash: hash, clientId: targetClientId || clientId },
      {
        onSuccess: () => {
          setImportingHash(null);
          setSelectedHashes((prev) => {
            const next = new Set(prev);
            next.delete(hash);
            return next;
          });
          showToast(`Imported "${title}" into Leecharr library`, "success");
        },
        onError: (err) => {
          setImportingHash(null);
          showToast(
            `Failed to import "${title}": ${err.message || "Import operation failed"}`,
            "error",
          );
        },
      },
    );
  };

  const handleImportSelected = () => {
    if (selectedHashes.size === 0) return;
    const missingHashes = Array.from(selectedHashes).filter((hash) => {
      const item = items?.find(
        (i) => i.infoHash?.toLowerCase() === hash.toLowerCase(),
      );
      return !item || !item.isInLibrary;
    });

    if (missingHashes.length === 0) {
      setSelectedHashes(new Set());
      showToast("All selected torrents are already in the library.", "info");
      return;
    }

    setImportingSelected(true);
    importAllMutation.mutate(missingHashes, {
      onSuccess: (res) => {
        const failedCount = res.failed ?? 0;
        showToast(
          `Import Complete: ${res.added} added, ${res.skipped} skipped, ${failedCount} failed.`,
          failedCount > 0 ? "error" : "success",
        );
        if (failedCount > 0 && res.items) {
          const failures = res.items.filter(
            (i: BatchImportItemResult) => !i.success,
          );
          if (failures.length > 0) {
            setFailedImportItems(failures);
          }
        }
      },
      onError: (err) => {
        setImportingSelected(false);
        showToast(
          `Bulk import failed: ${err.message || "Import operation failed"}`,
          "error",
        );
      },
    });
  };

  const handleImportAllMissing = () => {
    if (!items) return;
    const missingHashes = items
      .filter((i) => !i.isInLibrary && i.infoHash)
      .map((i) => i.infoHash);

    if (missingHashes.length === 0) {
      showToast(
        "All torrents from this client are already in the library.",
        "info",
      );
      return;
    }

    importAllMutation.mutate(missingHashes, {
      onSuccess: (res) => {
        setSelectedHashes(new Set());
        const failedCount = res.failed ?? 0;
        showToast(
          `Import Complete: ${res.added} added, ${res.skipped} skipped, ${failedCount} failed.`,
          failedCount > 0 ? "error" : "success",
        );
        if (failedCount > 0 && res.items) {
          const failures = res.items.filter(
            (i: BatchImportItemResult) => !i.success,
          );
          if (failures.length > 0) {
            setFailedImportItems(failures);
          }
        }
      },
      onError: (err) => {
        showToast(
          `Bulk import failed: ${err.message || "Import operation failed"}`,
          "error",
        );
      },
    });
  };

  if (clientsLoading) {
    return (
      <div className="content-area">
        <div className="card" style={{ padding: "3rem", textAlign: "center" }}>
          <div className="loading">Loading download client...</div>
        </div>
      </div>
    );
  }

  if (!isAll && !client) {
    return (
      <div className="content-area">
        <div className="card" style={{ padding: "3rem", textAlign: "center" }}>
          <div className="empty-state">
            <div className="empty-state-title">Download Client Not Found</div>
            <div className="empty-state-text">
              The requested download client does not exist or has been removed.
            </div>
            <Link
              to="/settings/download-clients"
              className="btn btn-primary"
              style={{ marginTop: "1rem", display: "inline-block" }}
            >
              Go to Download Client Settings
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      className="content-area client-torrents-page"
      style={{
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        padding: "1.5rem",
        boxSizing: "border-box",
      }}
    >
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
          flexShrink: 0,
        }}
      >
        <div>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              flexWrap: "wrap",
            }}
          >
            <h1
              style={{
                fontSize: "1.75rem",
                fontWeight: 700,
                margin: 0,
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
              }}
            >
              <span>💽</span> {isAll ? "All Download Clients" : client?.name} (
              {totalCount})
            </h1>
            {isAll ? (
              <span className="badge badge-primary">Aggregated View</span>
            ) : (
              <>
                <span className="badge badge-primary">
                  {client?.clientType}
                </span>
                <span className="badge badge-secondary">
                  {client?.host}:{client?.port}
                </span>
              </>
            )}
          </div>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {isAll
              ? "Aggregated live torrents across all configured download agents"
              : `Live torrent list from ${client?.clientType} download agent`}
          </p>
          {clients && clients.filter((c) => c.enable).length > 1 && (
            <div
              style={{
                display: "flex",
                gap: "0.5rem",
                marginTop: "0.75rem",
                flexWrap: "wrap",
              }}
            >
              <Link
                to="/activity/client/all"
                className={`btn btn-small ${isAll ? "btn-primary" : "btn-outline"}`}
                style={{
                  borderRadius: "16px",
                  padding: "0.2rem 0.75rem",
                  fontSize: "0.8rem",
                }}
              >
                All Clients
              </Link>
              {clients
                .filter((c) => c.enable)
                .map((c) => (
                  <Link
                    key={c.id}
                    to={`/activity/client/${c.id}`}
                    className={`btn btn-small ${!isAll && clientId === c.id ? "btn-primary" : "btn-outline"}`}
                    style={{
                      borderRadius: "16px",
                      padding: "0.2rem 0.75rem",
                      fontSize: "0.8rem",
                    }}
                  >
                    {c.name}
                  </Link>
                ))}
            </div>
          )}
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {/* View Mode Toggle */}
          <div className="view-toggle">
            <button
              className={`view-toggle-btn ${viewMode === "grid" ? "active" : ""}`}
              onClick={() => setViewMode("grid")}
              title="Poster Card Grid View"
            >
              🎬 Posters
            </button>
            <button
              className={`view-toggle-btn ${viewMode === "table" ? "active" : ""}`}
              onClick={() => setViewMode("table")}
              title="Detailed Table View"
            >
              📋 Table
            </button>
          </div>

          {client?.host && (
            <a
              href={getDownloadClientUrl(client)}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-outline"
              style={{ fontSize: "0.85rem", textDecoration: "none" }}
              title={`Open ${client?.name} Web UI`}
            >
              Open Web UI ↗
            </a>
          )}

          <button
            className="btn btn-outline"
            onClick={() => refetch()}
            disabled={isFetching}
          >
            {isFetching ? "Refreshing..." : "↻ Refresh"}
          </button>

          <button
            className="btn btn-primary"
            onClick={handleImportSelected}
            disabled={selectedHashes.size === 0 || importAllMutation.isPending}
            title={
              selectedHashes.size > 0
                ? `Import ${selectedHashes.size} selected torrent(s) into library`
                : "Select missing torrents to import"
            }
          >
            {importAllMutation.isPending && importingSelected
              ? "Importing..."
              : `Import Selected (${selectedHashes.size})`}
          </button>

          <button
            className="btn btn-primary"
            onClick={handleImportAllMissing}
            disabled={missingCount === 0 || importAllMutation.isPending}
            title={
              missingCount > 0
                ? `Import ${missingCount} missing torrents into library`
                : "All torrents are already in library"
            }
          >
            {importAllMutation.isPending && !importingSelected
              ? "Importing..."
              : `Import All Missing (${missingCount})`}
          </button>
        </div>
      </div>

      {/* Stats and filter bar */}
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
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          flexShrink: 0,
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.4rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <button
            className={`btn ${filterMode === "all" ? "btn-primary" : "btn-outline"}`}
            style={{
              fontSize: "0.82rem",
              padding: "0.35rem 0.85rem",
              borderRadius: "6px",
              fontWeight: 500,
            }}
            onClick={() => setFilterMode("all")}
          >
            All ({totalCount})
          </button>
          <button
            className={`btn ${filterMode === "missing" ? "btn-primary" : "btn-outline"}`}
            style={{
              fontSize: "0.82rem",
              padding: "0.35rem 0.85rem",
              borderRadius: "6px",
              fontWeight: 500,
            }}
            onClick={() => setFilterMode("missing")}
          >
            Not in Library ({missingCount})
          </button>
          <button
            className={`btn ${filterMode === "library" ? "btn-primary" : "btn-outline"}`}
            style={{
              fontSize: "0.82rem",
              padding: "0.35rem 0.85rem",
              borderRadius: "6px",
              fontWeight: 500,
            }}
            onClick={() => setFilterMode("library")}
          >
            In Library ({inLibraryCount})
          </button>
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            minWidth: "260px",
            flex: "1",
            maxWidth: "450px",
          }}
        >
          <input
            type="text"
            className="form-control"
            placeholder="Filter client torrents by title, category, hash..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            style={{
              width: "100%",
              padding: "0.4rem 0.75rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary)",
              color: "inherit",
              fontSize: "0.85rem",
            }}
          />
          {searchTerm && (
            <button
              className="btn btn-outline"
              onClick={() => setSearchTerm("")}
              style={{
                fontSize: "0.75rem",
                padding: "0.35rem 0.5rem",
                borderRadius: "6px",
              }}
              title="Clear search filter"
            >
              ✕
            </button>
          )}
        </div>
      </div>

      {/* Main Content Area */}
      {itemsLoading && (
        <div
          className="card"
          style={{ padding: "3rem", textAlign: "center", borderRadius: "8px" }}
        >
          <div className="loading">
            Connecting to {isAll ? "download clients" : client?.name} and
            fetching torrents...
          </div>
        </div>
      )}

      {isError && (
        <div
          className="card"
          style={{
            padding: "2.5rem 1.5rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            style={{
              color: "var(--danger)",
              fontWeight: 600,
              fontSize: "1.1rem",
              marginBottom: "0.5rem",
            }}
          >
            Unable to connect to download client
          </div>
          <div
            style={{
              color: "var(--text-muted)",
              fontSize: "0.9rem",
              marginBottom: "1.25rem",
            }}
          >
            {(error as Error)?.message || "Connection refused or timed out."}
          </div>
          <Link to="/settings/download-clients" className="btn btn-outline">
            Check Client Configuration
          </Link>
        </div>
      )}

      {!itemsLoading && !isError && filteredItems.length === 0 && (
        <div
          className="card empty-state"
          style={{
            padding: "3.5rem 1rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            className="empty-state-title"
            style={{
              fontSize: "1.25rem",
              fontWeight: 600,
              marginBottom: "0.5rem",
            }}
          >
            No Torrents Found
          </div>
          <div
            className="empty-state-text"
            style={{
              color: "var(--text-muted)",
              maxWidth: "500px",
              margin: "0 auto",
            }}
          >
            {searchTerm || filterMode !== "all"
              ? "No torrents match the active search or filter criteria."
              : isAll
                ? "No torrents currently reported across download clients."
                : `No torrents currently reported by ${client?.name}.`}
          </div>
        </div>
      )}

      {/* POSTER GRID VIEW */}
      {!itemsLoading &&
        !isError &&
        filteredItems.length > 0 &&
        viewMode === "grid" && (
          <div
            style={{
              flex: "1 1 0%",
              minHeight: 0,
              height: "100%",
              width: "100%",
              overflowY: "auto",
              overflowX: "hidden",
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
              gridAutoRows: "max-content",
              alignContent: "start",
              gap: "1.25rem",
              paddingRight: "0.25rem",
              paddingBottom: "1rem",
            }}
          >
            {filteredItems.map((item) => {
              const match = history?.find(
                (h) =>
                  (item.infoHash &&
                    h.infoHash?.toLowerCase() ===
                      item.infoHash.toLowerCase()) ||
                  h.title?.toLowerCase() === item.title.toLowerCase(),
              );
              const meta = match?.metadata;
              const displayTitle = meta?.title || item.title;
              const hasPoster = Boolean(meta?.posterUrl);
              const arrLink = match
                ? getMediaDeepLink(match, arrConnections)
                : null;
              const isImporting =
                importingHash === item.infoHash ||
                (importAllMutation.isPending && !item.isInLibrary);

              return (
                <div
                  key={item.infoHash || item.downloadId}
                  className="card"
                  style={{
                    padding: 0,
                    overflow: "hidden",
                    display: "flex",
                    flexDirection: "column",
                    height: "auto",
                    minHeight: "min-content",
                    flexShrink: 0,
                    borderRadius: "8px",
                    border: "1px solid var(--border-light)",
                    backgroundColor: "var(--bg-secondary)",
                    boxShadow:
                      "0 4px 14px rgba(0, 0, 0, 0.35), 0 1px 3px rgba(0, 0, 0, 0.2)",
                    transition: "transform 0.18s ease, box-shadow 0.18s ease",
                  }}
                >
                  {/* Poster Artwork Box */}
                  <div
                    style={{
                      position: "relative",
                      width: "100%",
                      aspectRatio: "2 / 3",
                      backgroundColor: "#141414",
                      overflow: "hidden",
                      flexShrink: 0,
                    }}
                  >
                    {hasPoster ? (
                      <img
                        src={meta?.posterUrl || ""}
                        alt={displayTitle}
                        style={{
                          position: "absolute",
                          top: 0,
                          left: 0,
                          width: "100%",
                          height: "100%",
                          objectFit: "cover",
                        }}
                        loading="lazy"
                      />
                    ) : (
                      <div
                        style={{
                          position: "absolute",
                          top: 0,
                          left: 0,
                          width: "100%",
                          height: "100%",
                          display: "flex",
                          flexDirection: "column",
                          alignItems: "center",
                          justifyContent: "center",
                          color: "var(--text-muted)",
                          padding: "1rem",
                          textAlign: "center",
                        }}
                      >
                        <span
                          style={{ fontSize: "2.8rem", marginBottom: "0.5rem" }}
                        >
                          ⚡
                        </span>
                        <span
                          style={{
                            fontSize: "0.8rem",
                            wordBreak: "break-word",
                          }}
                        >
                          {item.title}
                        </span>
                      </div>
                    )}

                    {/* Top Badges */}
                    <div
                      style={{
                        position: "absolute",
                        top: "8px",
                        left: "8px",
                        right: "8px",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      {arrLink ? (
                        <a
                          href={arrLink.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="badge badge-primary"
                          style={{
                            fontSize: "0.7rem",
                            fontWeight: 600,
                            backgroundColor: "rgba(0, 0, 0, 0.75)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                            textDecoration: "none",
                            color: "inherit",
                          }}
                          title={arrLink.label}
                        >
                          {arrLink.appName} ↗
                        </a>
                      ) : item.category ? (
                        <span
                          className="badge badge-primary"
                          style={{
                            fontSize: "0.7rem",
                            fontWeight: 600,
                            backgroundColor: "rgba(0, 0, 0, 0.75)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                          }}
                        >
                          {item.category}
                        </span>
                      ) : (
                        <span />
                      )}

                      {item.isInLibrary ? (
                        <span
                          className="badge badge-success"
                          style={{
                            fontSize: "0.7rem",
                            backgroundColor: "rgba(39, 174, 96, 0.85)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                          }}
                        >
                          ✓ In Library
                        </span>
                      ) : (
                        <span
                          className="badge badge-warning"
                          style={{
                            fontSize: "0.7rem",
                            backgroundColor: "rgba(230, 126, 34, 0.85)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                          }}
                        >
                          Not In Library
                        </span>
                      )}
                    </div>

                    {/* Bottom Stats Overlay */}
                    <div
                      style={{
                        position: "absolute",
                        bottom: 0,
                        left: 0,
                        right: 0,
                        padding: "0.4rem 0.6rem",
                        backgroundColor: "rgba(0, 0, 0, 0.82)",
                        backdropFilter: "blur(4px)",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        fontSize: "0.75rem",
                        color: "#ccc",
                      }}
                    >
                      <span>{formatBytes(item.totalSize)}</span>
                      <span style={{ fontWeight: 600, color: "var(--accent)" }}>
                        {item.progress.toFixed(1)}%
                      </span>
                    </div>
                  </div>

                  {/* Card Details */}
                  <div
                    style={{
                      padding: "0.85rem",
                      display: "flex",
                      flexDirection: "column",
                      flex: "0 0 auto",
                      gap: "0.4rem",
                      backgroundColor: "var(--bg-secondary)",
                    }}
                  >
                    <div
                      style={{
                        fontWeight: 600,
                        fontSize: "0.9rem",
                        lineHeight: "1.25",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        display: "-webkit-box",
                        WebkitLineClamp: 2,
                        WebkitBoxOrient: "vertical",
                      }}
                      title={displayTitle}
                    >
                      {displayTitle} {meta?.year ? `(${meta.year})` : ""}
                    </div>

                    <div
                      style={{
                        fontSize: "0.72rem",
                        color: "var(--text-muted)",
                        fontFamily: "monospace",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {item.infoHash}
                    </div>

                    {/* Action row */}
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        marginTop: "auto",
                        paddingTop: "0.5rem",
                      }}
                    >
                      <span
                        className={`badge ${
                          item.status?.toLowerCase() === "seeding"
                            ? "badge-success"
                            : item.status?.toLowerCase() === "downloading"
                              ? "badge-primary"
                              : item.status?.toLowerCase() === "paused" ||
                                  item.status?.toLowerCase() === "stopped"
                                ? "badge-warning"
                                : "badge-secondary"
                        }`}
                        style={{ fontSize: "0.72rem" }}
                      >
                        {item.status || "unknown"}
                      </span>
                      {isAll && item.clientName && (
                        <span
                          className="badge badge-secondary"
                          style={{ fontSize: "0.72rem" }}
                        >
                          {item.clientName}
                        </span>
                      )}
                      {duplicateHashes.has(item.infoHash?.toLowerCase()) && (
                        <span
                          className="badge badge-warning"
                          style={{ fontSize: "0.72rem" }}
                          title="This torrent is running on multiple download clients"
                        >
                          Duplicate
                        </span>
                      )}

                      <div
                        style={{
                          display: "flex",
                          gap: "0.35rem",
                          alignItems: "center",
                        }}
                      >
                        {!item.isPrivate ? (
                          <button
                            className="btn btn-primary btn-small"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.2rem 0.55rem",
                              borderRadius: "4px",
                            }}
                            onClick={() =>
                              handleBoostTorrent(
                                item.infoHash,
                                item.title,
                                item.isPrivate,
                              )
                            }
                            disabled={boostHashMutation.isPending}
                            title="Query candidate trackers via BEP 15/48 scrape and inject verified seeders"
                          >
                            ⚡ Boost
                          </button>
                        ) : (
                          <span
                            className="badge badge-secondary"
                            style={{
                              fontSize: "0.72rem",
                              padding: "0.2rem 0.45rem",
                              borderRadius: "4px",
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.2rem",
                              cursor: "help",
                            }}
                            title="Tracker boosting is prohibited on private torrents to protect tracker rules and passkeys (BEP 27)."
                          >
                            🔒 Private Swarm
                          </span>
                        )}

                        {item.isInLibrary ? (
                          <button
                            className="btn btn-outline btn-small"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.2rem 0.55rem",
                              borderRadius: "4px",
                            }}
                            onClick={() => {
                              if (item.libraryTorrentId) {
                                navigate(`/torrents/${item.libraryTorrentId}`);
                              } else {
                                navigate("/torrents");
                              }
                            }}
                          >
                            View ↗
                          </button>
                        ) : (
                          <button
                            className="btn btn-success btn-small"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.2rem 0.55rem",
                              borderRadius: "4px",
                            }}
                            onClick={() =>
                              handleImportOne(
                                item.infoHash,
                                item.title,
                                item.clientId,
                              )
                            }
                            disabled={isImporting}
                          >
                            {isImporting ? "Importing..." : "+ Import"}
                          </button>
                        )}

                        {item.status?.toLowerCase() === "paused" ||
                        item.status?.toLowerCase() === "stopped" ? (
                          <button
                            className="btn btn-outline btn-small"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.2rem 0.55rem",
                              borderRadius: "4px",
                            }}
                            onClick={() =>
                              handleResumeTorrent(
                                item.clientId || clientId,
                                item.infoHash,
                                item.title,
                              )
                            }
                            disabled={resumeRemoteMutation.isPending}
                            title="Resume remote torrent"
                          >
                            ▶ Resume
                          </button>
                        ) : (
                          <button
                            className="btn btn-outline btn-small"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.2rem 0.55rem",
                              borderRadius: "4px",
                            }}
                            onClick={() =>
                              handlePauseTorrent(
                                item.clientId || clientId,
                                item.infoHash,
                                item.title,
                              )
                            }
                            disabled={pauseRemoteMutation.isPending}
                            title="Pause remote torrent"
                          >
                            ⏸ Pause
                          </button>
                        )}

                        <button
                          className="btn btn-danger btn-small"
                          style={{
                            fontSize: "0.78rem",
                            padding: "0.2rem 0.55rem",
                            borderRadius: "4px",
                          }}
                          onClick={() =>
                            setDeleteTarget({
                              clientId: item.clientId || clientId,
                              clientName: item.clientName || client?.name,
                              infoHash: item.infoHash,
                              title: item.title,
                            })
                          }
                          disabled={deleteRemoteMutation.isPending}
                          title="Remove torrent from download client"
                        >
                          🗑 Delete
                        </button>
                      </div>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}

      {/* DETAILED TABLE VIEW */}
      {!itemsLoading &&
        !isError &&
        filteredItems.length > 0 &&
        viewMode === "table" && (
          <div
            className="card"
            style={{
              padding: 0,
              overflow: "hidden",
              borderRadius: "8px",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
              flex: "1 1 auto",
              minHeight: 0,
              display: "flex",
              flexDirection: "column",
            }}
          >
            <div
              style={{
                flex: "1 1 auto",
                minHeight: 0,
                overflowY: "auto",
                overflowX: "auto",
              }}
            >
              <table
                className="table"
                style={{ width: "100%", borderCollapse: "collapse" }}
              >
                <thead
                  style={{
                    position: "sticky",
                    top: 0,
                    zIndex: 2,
                    backgroundColor: "var(--bg-secondary)",
                  }}
                >
                  <tr
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      textAlign: "left",
                      color: "var(--text-muted)",
                      fontSize: "0.8rem",
                    }}
                  >
                    <th
                      style={{
                        padding: "0.75rem 0.5rem 0.75rem 1rem",
                        width: "36px",
                        textAlign: "center",
                      }}
                    >
                      <input
                        type="checkbox"
                        checked={allMissingSelected}
                        ref={(el) => {
                          if (el) {
                            el.indeterminate =
                              !allMissingSelected && someMissingSelected;
                          }
                        }}
                        disabled={visibleMissingItems.length === 0}
                        onChange={handleToggleSelectAll}
                        aria-label="Select all missing torrents"
                        title={
                          visibleMissingItems.length === 0
                            ? "No missing torrents"
                            : allMissingSelected
                              ? "Deselect all missing"
                              : "Select all missing"
                        }
                        style={{
                          cursor:
                            visibleMissingItems.length === 0
                              ? "not-allowed"
                              : "pointer",
                        }}
                      />
                    </th>
                    <th style={{ padding: "0.75rem 1rem" }}>Media & Torrent</th>
                    <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                      Status
                    </th>
                    <th style={{ padding: "0.75rem 1rem", width: "170px" }}>
                      Size & Progress
                    </th>
                    <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                      Category
                    </th>
                    <th style={{ padding: "0.75rem 1rem" }}>Save Path</th>
                    <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                      Library State
                    </th>
                    <th
                      style={{
                        padding: "0.75rem 1rem",
                        width: "120px",
                        textAlign: "right",
                      }}
                    >
                      Action
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {filteredItems.map((item) => {
                    const match = history?.find(
                      (h) =>
                        (item.infoHash &&
                          h.infoHash?.toLowerCase() ===
                            item.infoHash.toLowerCase()) ||
                        h.title?.toLowerCase() === item.title.toLowerCase(),
                    );
                    const meta = match?.metadata;
                    const displayTitle = meta?.title || item.title;
                    const hasPoster = Boolean(meta?.posterUrl);
                    const arrLink = match
                      ? getMediaDeepLink(match, arrConnections)
                      : null;
                    const isImporting =
                      importingHash === item.infoHash ||
                      (importAllMutation.isPending && !item.isInLibrary);

                    return (
                      <tr
                        key={item.infoHash || item.downloadId}
                        style={{
                          borderBottom: "1px solid var(--border-light)",
                          transition: "background-color 0.15s ease",
                        }}
                      >
                        <td
                          style={{
                            padding: "0.75rem 0.5rem 0.75rem 1rem",
                            textAlign: "center",
                            width: "36px",
                          }}
                        >
                          {!item.isInLibrary ? (
                            <input
                              type="checkbox"
                              checked={selectedHashes.has(item.infoHash)}
                              disabled={isImporting}
                              onChange={() => handleToggleSelect(item.infoHash)}
                              aria-label={`Select ${displayTitle}`}
                              style={{
                                cursor: isImporting ? "not-allowed" : "pointer",
                              }}
                            />
                          ) : (
                            <input
                              type="checkbox"
                              disabled
                              checked={false}
                              title="Already in library"
                              style={{ opacity: 0.3, cursor: "not-allowed" }}
                            />
                          )}
                        </td>
                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              display: "flex",
                              alignItems: "center",
                              gap: "0.75rem",
                            }}
                          >
                            {hasPoster ? (
                              <img
                                src={meta?.posterUrl || ""}
                                alt=""
                                style={{
                                  width: "32px",
                                  height: "46px",
                                  objectFit: "cover",
                                  borderRadius: "4px",
                                  border: "1px solid var(--border-light)",
                                  flexShrink: 0,
                                }}
                              />
                            ) : (
                              <span
                                style={{ fontSize: "1.4rem", flexShrink: 0 }}
                              >
                                ⚡
                              </span>
                            )}

                            <div style={{ minWidth: 0, flex: 1 }}>
                              <div
                                style={{ fontWeight: 500, fontSize: "0.88rem" }}
                              >
                                {displayTitle}{" "}
                                {meta?.year ? `(${meta.year})` : ""}
                              </div>
                              <div
                                style={{
                                  fontSize: "0.72rem",
                                  color: "var(--text-muted)",
                                  fontFamily: "monospace",
                                  marginTop: "0.15rem",
                                  overflow: "hidden",
                                  textOverflow: "ellipsis",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {item.infoHash}
                              </div>
                            </div>
                          </div>
                        </td>

                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              display: "flex",
                              flexDirection: "column",
                              gap: "0.25rem",
                              alignItems: "flex-start",
                            }}
                          >
                            <span
                              className={`badge ${
                                item.status?.toLowerCase() === "seeding"
                                  ? "badge-success"
                                  : item.status?.toLowerCase() === "downloading"
                                    ? "badge-primary"
                                    : item.status?.toLowerCase() === "paused" ||
                                        item.status?.toLowerCase() === "stopped"
                                      ? "badge-warning"
                                      : "badge-secondary"
                              }`}
                              style={{ borderRadius: "4px" }}
                            >
                              {item.status || "unknown"}
                            </span>
                            {isAll && item.clientName && (
                              <span
                                className="badge badge-secondary"
                                style={{
                                  fontSize: "0.7rem",
                                  borderRadius: "4px",
                                }}
                              >
                                {item.clientName}
                              </span>
                            )}
                            {duplicateHashes.has(
                              item.infoHash?.toLowerCase(),
                            ) && (
                              <span
                                className="badge badge-warning"
                                style={{
                                  fontSize: "0.7rem",
                                  borderRadius: "4px",
                                }}
                                title="This torrent is running on multiple download clients"
                              >
                                Duplicate
                              </span>
                            )}
                          </div>
                        </td>

                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              fontSize: "0.82rem",
                              marginBottom: "0.3rem",
                              display: "flex",
                              justifyContent: "space-between",
                            }}
                          >
                            <span>{formatBytes(item.totalSize)}</span>
                            <span
                              style={{
                                fontWeight: 600,
                                color: "var(--accent)",
                              }}
                            >
                              {item.progress.toFixed(1)}%
                            </span>
                          </div>
                          <div
                            style={{
                              height: "6px",
                              backgroundColor: "var(--bg-primary)",
                              borderRadius: "3px",
                              overflow: "hidden",
                              width: "100%",
                              border: "1px solid var(--border-light)",
                            }}
                          >
                            <div
                              style={{
                                width: `${Math.min(100, Math.max(0, item.progress))}%`,
                                height: "100%",
                                backgroundColor:
                                  item.progress >= 100
                                    ? "var(--success)"
                                    : "var(--accent)",
                                borderRadius: "3px",
                                transition: "width 0.3s ease",
                              }}
                            />
                          </div>
                        </td>

                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              display: "flex",
                              gap: "0.3rem",
                              alignItems: "center",
                            }}
                          >
                            {item.category && (
                              <span
                                className="badge badge-secondary"
                                style={{ borderRadius: "4px" }}
                              >
                                {item.category}
                              </span>
                            )}
                            {arrLink && (
                              <a
                                href={arrLink.url}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="badge badge-primary"
                                style={{
                                  fontSize: "0.7rem",
                                  padding: "0.15rem 0.4rem",
                                  textDecoration: "none",
                                  borderRadius: "4px",
                                }}
                                title={arrLink.label}
                              >
                                {arrLink.appName} ↗
                              </a>
                            )}
                            {!item.category && !arrLink && (
                              <span
                                style={{
                                  color: "var(--text-muted)",
                                  fontSize: "0.85rem",
                                }}
                              >
                                -
                              </span>
                            )}
                          </div>
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            fontSize: "0.82rem",
                            color: "var(--text-muted)",
                            wordBreak: "break-all",
                          }}
                        >
                          {item.outputPath || "-"}
                        </td>

                        <td style={{ padding: "0.75rem 1rem" }}>
                          {item.isInLibrary ? (
                            <span
                              className="badge badge-success"
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.3rem",
                                borderRadius: "4px",
                              }}
                            >
                              ✓ In Library
                            </span>
                          ) : (
                            <span
                              className="badge badge-warning"
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.3rem",
                                borderRadius: "4px",
                              }}
                            >
                              Not in Library
                            </span>
                          )}
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            textAlign: "right",
                            whiteSpace: "nowrap",
                          }}
                        >
                          <div
                            style={{
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.45rem",
                              whiteSpace: "nowrap",
                            }}
                          >
                            {!item.isPrivate ? (
                              <button
                                className="btn btn-primary"
                                style={{
                                  fontSize: "0.78rem",
                                  padding: "0.3rem 0.65rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  whiteSpace: "nowrap",
                                }}
                                onClick={() =>
                                  handleBoostTorrent(
                                    item.infoHash,
                                    item.title,
                                    item.isPrivate,
                                  )
                                }
                                disabled={boostHashMutation.isPending}
                                title="Query candidate trackers via BEP 15/48 scrape and inject verified seeders"
                              >
                                <span>⚡</span>
                                <span>Boost</span>
                              </button>
                            ) : (
                              <span
                                className="badge badge-secondary"
                                style={{
                                  fontSize: "0.75rem",
                                  padding: "0.3rem 0.55rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.3rem",
                                  whiteSpace: "nowrap",
                                  cursor: "help",
                                }}
                                title="Tracker boosting is prohibited on private torrents to protect tracker rules and passkeys (BEP 27)."
                              >
                                🔒 Private Swarm
                              </span>
                            )}

                            {item.isInLibrary ? (
                              <button
                                className="btn btn-outline"
                                style={{
                                  fontSize: "0.78rem",
                                  padding: "0.3rem 0.65rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  whiteSpace: "nowrap",
                                }}
                                onClick={() => {
                                  if (item.libraryTorrentId) {
                                    navigate(
                                      `/torrents/${item.libraryTorrentId}`,
                                    );
                                  } else {
                                    navigate("/torrents");
                                  }
                                }}
                              >
                                View
                              </button>
                            ) : (
                              <button
                                className="btn btn-success"
                                style={{
                                  fontSize: "0.78rem",
                                  padding: "0.3rem 0.65rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  whiteSpace: "nowrap",
                                }}
                                onClick={() =>
                                  handleImportOne(
                                    item.infoHash,
                                    item.title,
                                    item.clientId,
                                  )
                                }
                                disabled={isImporting}
                              >
                                {isImporting ? (
                                  <span>Adding...</span>
                                ) : (
                                  <>
                                    <span>+</span>
                                    <span>Add</span>
                                  </>
                                )}
                              </button>
                            )}

                            {item.status?.toLowerCase() === "paused" ||
                            item.status?.toLowerCase() === "stopped" ? (
                              <button
                                className="btn btn-outline"
                                style={{
                                  fontSize: "0.78rem",
                                  padding: "0.3rem 0.65rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  whiteSpace: "nowrap",
                                }}
                                onClick={() =>
                                  handleResumeTorrent(
                                    item.clientId || clientId,
                                    item.infoHash,
                                    item.title,
                                  )
                                }
                                disabled={resumeRemoteMutation.isPending}
                                title="Resume remote torrent"
                              >
                                <span>▶</span>
                                <span>Resume</span>
                              </button>
                            ) : (
                              <button
                                className="btn btn-outline"
                                style={{
                                  fontSize: "0.78rem",
                                  padding: "0.3rem 0.65rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  whiteSpace: "nowrap",
                                }}
                                onClick={() =>
                                  handlePauseTorrent(
                                    item.clientId || clientId,
                                    item.infoHash,
                                    item.title,
                                  )
                                }
                                disabled={pauseRemoteMutation.isPending}
                                title="Pause remote torrent"
                              >
                                <span>⏸</span>
                                <span>Pause</span>
                              </button>
                            )}

                            <button
                              className="btn btn-danger"
                              style={{
                                fontSize: "0.78rem",
                                padding: "0.3rem 0.65rem",
                                borderRadius: "4px",
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.35rem",
                                whiteSpace: "nowrap",
                              }}
                              onClick={() =>
                                setDeleteTarget({
                                  clientId: item.clientId || clientId,
                                  clientName: item.clientName || client?.name,
                                  infoHash: item.infoHash,
                                  title: item.title,
                                })
                              }
                              disabled={deleteRemoteMutation.isPending}
                              title="Remove torrent from download client"
                            >
                              <span>🗑</span>
                              <span>Delete</span>
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}

      {/* Delete Remote Torrent Modal */}
      {deleteTarget && (
        <div
          className="modal-backdrop"
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0, 0, 0, 0.6)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
          }}
          onClick={() => setDeleteTarget(null)}
        >
          <div
            className="modal-dialog"
            style={{
              backgroundColor: "var(--bg-secondary, #1e1e24)",
              borderRadius: "8px",
              padding: "1.5rem",
              maxWidth: "480px",
              width: "90%",
              boxShadow: "0 10px 25px rgba(0, 0, 0, 0.5)",
              border: "1px solid var(--border-light, #333)",
            }}
            onClick={(e) => e.stopPropagation()}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                marginBottom: "1rem",
              }}
            >
              <span style={{ fontSize: "1.4rem" }}>🗑</span>
              <h3 style={{ margin: 0, fontSize: "1.2rem", fontWeight: 600 }}>
                Remove Torrent from Download Client
              </h3>
            </div>

            <p
              style={{
                color: "var(--text-primary)",
                fontSize: "0.95rem",
                lineHeight: 1.5,
              }}
            >
              Are you sure you want to remove{" "}
              <strong>&ldquo;{deleteTarget.title}&rdquo;</strong>
              {deleteTarget.clientName
                ? ` from ${deleteTarget.clientName}`
                : ""}
              ?
            </p>

            <label
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.6rem",
                marginTop: "1.25rem",
                cursor: "pointer",
                padding: "0.5rem 0.75rem",
                backgroundColor: "var(--bg-primary, #141418)",
                borderRadius: "6px",
                border: "1px solid var(--border-light, #333)",
              }}
            >
              <input
                type="checkbox"
                checked={deleteFiles}
                onChange={(e) => setDeleteFiles(e.target.checked)}
                style={{ width: "16px", height: "16px", cursor: "pointer" }}
              />
              <span
                style={{
                  fontSize: "0.88rem",
                  color: "var(--text-danger, #e55353)",
                  fontWeight: 500,
                }}
              >
                Also delete downloaded files from disk
              </span>
            </label>

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.75rem",
                marginTop: "1.5rem",
              }}
            >
              <button
                className="btn btn-outline"
                onClick={() => setDeleteTarget(null)}
                disabled={deleteRemoteMutation.isPending}
              >
                Cancel
              </button>
              <button
                className="btn btn-danger"
                onClick={confirmDeleteTorrent}
                disabled={deleteRemoteMutation.isPending}
              >
                {deleteRemoteMutation.isPending
                  ? "Removing..."
                  : "Delete Torrent"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Batch Import Failure Diagnostic Modal */}
      {failedImportItems && failedImportItems.length > 0 && (
        <div
          className="modal-overlay"
          onClick={() => setFailedImportItems(null)}
          style={{ zIndex: 1100 }}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 720,
              width: "90%",
              maxHeight: "85vh",
              display: "flex",
              flexDirection: "column",
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-secondary)",
              padding: "1.5rem",
            }}
          >
            <h3
              className="modal-title"
              style={{
                fontSize: "1.2rem",
                marginBottom: "0.5rem",
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                color: "var(--danger, #e74c3c)",
              }}
            >
              <span>⚠️</span> Batch Import Failures ({failedImportItems.length})
            </h3>
            <p
              style={{
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                marginBottom: "1.25rem",
                lineHeight: 1.5,
              }}
            >
              The following torrents failed to import from{" "}
              {client?.name || "download client"}. Diagnostic details and retry
              actions are listed below:
            </p>

            <div
              style={{
                overflowY: "auto",
                flex: "1 1 auto",
                maxHeight: "50vh",
                marginBottom: "1.25rem",
                border: "1px solid var(--border-light)",
                borderRadius: "6px",
                backgroundColor: "var(--bg-primary)",
              }}
            >
              <table
                style={{
                  width: "100%",
                  borderCollapse: "collapse",
                  fontSize: "0.85rem",
                }}
              >
                <thead>
                  <tr
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      backgroundColor: "var(--bg-secondary)",
                    }}
                  >
                    <th
                      style={{ textAlign: "left", padding: "0.6rem 0.75rem" }}
                    >
                      Torrent
                    </th>
                    <th
                      style={{ textAlign: "left", padding: "0.6rem 0.75rem" }}
                    >
                      Failure Reason
                    </th>
                    <th
                      style={{
                        textAlign: "right",
                        padding: "0.6rem 0.75rem",
                        width: "80px",
                      }}
                    >
                      Action
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {failedImportItems.map((item) => (
                    <tr
                      key={item.infoHash}
                      style={{ borderBottom: "1px solid var(--border-light)" }}
                    >
                      <td style={{ padding: "0.6rem 0.75rem" }}>
                        <div
                          style={{ fontWeight: 600, wordBreak: "break-word" }}
                        >
                          {item.title || item.infoHash}
                        </div>
                        <div
                          style={{
                            fontSize: "0.72rem",
                            color: "var(--text-muted)",
                            fontFamily: "monospace",
                          }}
                        >
                          {item.infoHash}
                        </div>
                      </td>
                      <td
                        style={{
                          padding: "0.6rem 0.75rem",
                          color: "var(--danger, #e74c3c)",
                          wordBreak: "break-word",
                        }}
                      >
                        {item.errorMessage ||
                          "Unknown error occurred during import."}
                      </td>
                      <td
                        style={{
                          padding: "0.6rem 0.75rem",
                          textAlign: "right",
                        }}
                      >
                        <button
                          className="btn btn-outline btn-small"
                          style={{
                            fontSize: "0.75rem",
                            padding: "0.25rem 0.55rem",
                          }}
                          disabled={
                            importOneMutation.isPending &&
                            importingHash === item.infoHash
                          }
                          onClick={() => {
                            handleImportOne(
                              item.infoHash,
                              item.title || item.infoHash,
                            );
                            setFailedImportItems((prev) =>
                              prev
                                ? prev.filter(
                                    (f) => f.infoHash !== item.infoHash,
                                  )
                                : null,
                            );
                          }}
                        >
                          {importOneMutation.isPending &&
                          importingHash === item.infoHash
                            ? "..."
                            : "Retry"}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                className="btn btn-primary"
                onClick={() => setFailedImportItems(null)}
              >
                Dismiss
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
