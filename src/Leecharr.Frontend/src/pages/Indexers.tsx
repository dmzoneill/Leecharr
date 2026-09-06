import React, { useState, useEffect } from "react";
import {
  useIndexers,
  useIndexerSearch,
  useDownloadIndexerRelease,
  useTestIndexer,
  useUpdateIndexer,
} from "../api/hooks";
import { formatBytes, formatDate } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import type { ReleaseInfo, IndexerDefinition } from "../api/types";

interface IndexersProps {
  selectedSubNav?: string;
  onSelectIndexer?: (id: string) => void;
  onNavigateSettings?: (section: string) => void;
}

export const Indexers: React.FC<IndexersProps> = ({
  selectedSubNav = "all",
  onSelectIndexer,
  onNavigateSettings,
}) => {
  const { data: indexers, isLoading: isIndexersLoading } = useIndexers();
  const testIndexerMutation = useTestIndexer();
  const updateIndexerMutation = useUpdateIndexer();
  const downloadReleaseMutation = useDownloadIndexerRelease();
  const { showToast } = useToast();

  const [query, setQuery] = useState("");
  const [activeSearchTerm, setActiveSearchTerm] = useState("");
  const [freeleechOnly, setFreeleechOnly] = useState(false);
  const [downloadingGuid, setDownloadingGuid] = useState<string | null>(null);
  const [testingId, setTestingId] = useState<number | null>(null);

  // Determine active indexer
  const enabledIndexers = (indexers || []).filter((i) => i.enable);
  const isAll = selectedSubNav === "all" || !selectedSubNav;
  const currentIndexerId = !isAll ? Number(selectedSubNav) : undefined;
  const currentIndexer = indexers?.find((i) => i.id === currentIndexerId);

  // Live Indexer Search Hook
  const searchResults = useIndexerSearch(
    {
      query: activeSearchTerm,
      indexerId: currentIndexerId,
    },
    Boolean(activeSearchTerm.trim()),
  );

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    if (query.trim()) {
      setActiveSearchTerm(query.trim());
    }
  };

  const handleGrab = (release: ReleaseInfo) => {
    const itemKey = release.guid || release.infoHash || release.title;
    setDownloadingGuid(itemKey);

    downloadReleaseMutation.mutate(
      {
        title: release.title,
        downloadUrl: release.downloadUrl || undefined,
        magnetUrl: release.magnetUrl || undefined,
        infoHash: release.infoHash || undefined,
        indexerId: release.indexerId,
        indexerName: release.indexerName || release.indexer || "",
      },
      {
        onSuccess: () => {
          setDownloadingGuid(null);
          showToast(`Grabbed "${release.title}" successfully`, "success");
        },
        onError: (err) => {
          setDownloadingGuid(null);
          showToast(
            `Failed to grab: ${err.message || "Unknown error"}`,
            "error",
          );
        },
      },
    );
  };

  const handleTest = (id: number) => {
    setTestingId(id);
    testIndexerMutation.mutate(id, {
      onSuccess: (res) => {
        setTestingId(null);
        if (res.success) {
          showToast(
            `Connection to indexer verified (${res.responseTimeMs || 0}ms)`,
            "success",
          );
        } else {
          showToast(`Indexer test failed: ${res.message}`, "error");
        }
      },
      onError: (err) => {
        setTestingId(null);
        showToast(`Test error: ${err.message}`, "error");
      },
    });
  };

  const handleToggleEnable = (indexer: IndexerDefinition) => {
    updateIndexerMutation.mutate(
      {
        ...indexer,
        enable: !indexer.enable,
      },
      {
        onSuccess: () => {
          showToast(
            `Indexer "${indexer.name}" ${!indexer.enable ? "enabled" : "disabled"}`,
            "success",
          );
        },
      },
    );
  };

  const rawResults = searchResults.data || [];
  const filtered = freeleechOnly
    ? rawResults.filter(
        (r) =>
          Boolean(r.isFreeleech) ||
          r.downloadVolumeFactor === 0 ||
          (r.category || "").toLowerCase().includes("freeleech") ||
          (r.categories || []).some((c) =>
            c.toLowerCase().includes("freeleech"),
          ) ||
          (r.downloadUrl || "").toLowerCase().includes("freeleech") ||
          (r.magnetUrl || "").toLowerCase().includes("freeleech"),
      )
    : rawResults;

  if (isIndexersLoading) {
    return (
      <div
        className="content-area"
        style={{ padding: "2rem", textAlign: "center" }}
      >
        <div className="loading">Loading configured indexers...</div>
      </div>
    );
  }

  return (
    <div
      className="indexers-page content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      {/* Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexShrink: 0,
        }}
      >
        <div>
          <h1
            className="page-heading"
            style={{ margin: 0, fontSize: "1.4rem" }}
          >
            {currentIndexer
              ? `Indexer: ${currentIndexer.name}`
              : "Multi-Indexer Search & Discovery"}
          </h1>
          <p
            className="text-muted"
            style={{ margin: "0.25rem 0 0", fontSize: "0.85rem" }}
          >
            {currentIndexer
              ? `Direct query endpoint for ${currentIndexer.indexerType} (${currentIndexer.url})`
              : "Unified Torznab & Newznab search across all linked indexers with Freeleech detection."}
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.5rem" }}>
          {currentIndexer && (
            <>
              <button
                type="button"
                className="btn btn-small btn-outline"
                onClick={() => handleTest(currentIndexer.id)}
                disabled={testingId === currentIndexer.id}
              >
                {testingId === currentIndexer.id
                  ? "Testing..."
                  : "⚡ Test Connection"}
              </button>
              <button
                type="button"
                className="btn btn-small btn-outline"
                onClick={() => handleToggleEnable(currentIndexer)}
              >
                {currentIndexer.enable ? "⏸ Disable" : "▶ Enable"}
              </button>
            </>
          )}
          <button
            type="button"
            className="btn btn-small"
            onClick={() => onNavigateSettings && onNavigateSettings("indexers")}
            style={{
              backgroundColor: "rgba(255, 209, 102, 0.1)",
              color: "var(--accent, #ffd166)",
              border: "1px solid var(--accent, #ffd166)",
              fontWeight: 600,
            }}
          >
            ⚙ Manage Indexers
          </button>
        </div>
      </div>

      {/* No Indexers Warning Banner */}
      {!isIndexersLoading && (!indexers || indexers.length === 0) && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "0.85rem 1.25rem",
            backgroundColor: "rgba(255, 209, 102, 0.12)",
            border: "1px solid var(--accent, #ffd166)",
            borderRadius: "6px",
            marginBottom: "1rem",
            color: "var(--text-primary, #f8f4ed)",
            fontSize: "0.9rem",
            flexShrink: 0,
          }}
        >
          <div>
            <strong>⚠️ No indexers configured.</strong> You need to add a
            Torznab indexer or sync with Prowlarr in Settings &gt; Indexers to
            search for releases.
          </div>
          <button
            type="button"
            className="btn btn-small"
            onClick={() => onNavigateSettings && onNavigateSettings("indexers")}
            style={{
              backgroundColor: "var(--accent, #ffd166)",
              color: "#10111a",
              fontWeight: 600,
              whiteSpace: "nowrap",
              marginLeft: "1rem",
            }}
          >
            Configure Indexers
          </button>
        </div>
      )}

      {/* Indexer Filter Pills / Chips when in All View */}
      {isAll && enabledIndexers.length > 0 && (
        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            marginBottom: "1rem",
            overflowX: "auto",
            paddingBottom: "0.25rem",
            flexShrink: 0,
          }}
        >
          <button
            type="button"
            className="badge"
            style={{
              padding: "0.35rem 0.75rem",
              borderRadius: "20px",
              backgroundColor: "var(--accent, #ffd166)",
              color: "#000000",
              fontWeight: 600,
              border: "none",
              cursor: "pointer",
            }}
          >
            All Indexers ({enabledIndexers.length})
          </button>
          {enabledIndexers.map((idx) => (
            <button
              key={idx.id}
              type="button"
              className="badge"
              onClick={() => onSelectIndexer && onSelectIndexer(String(idx.id))}
              style={{
                padding: "0.35rem 0.75rem",
                borderRadius: "20px",
                backgroundColor: "var(--bg-secondary, #171b35)",
                color: "var(--text-secondary, #c7c5d3)",
                border: "1px solid var(--border-light, #1c203b)",
                cursor: "pointer",
              }}
            >
              {idx.name} ({idx.indexerType})
            </button>
          ))}
        </div>
      )}

      {/* Search Input Box Card */}
      <div
        className="card"
        style={{
          padding: "1.25rem",
          borderRadius: "8px",
          backgroundColor: "var(--bg-secondary, #171b35)",
          border: "1px solid var(--border-light, #1c203b)",
          marginBottom: "1rem",
          flexShrink: 0,
        }}
      >
        <form
          onSubmit={handleSearch}
          style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap" }}
        >
          <input
            type="text"
            placeholder={
              currentIndexer
                ? `Search ${currentIndexer.name} (e.g. 1080p, 2160p, Linux, Debian)...`
                : "Search all indexers (e.g. 1080p, 2160p, Linux, Debian)..."
            }
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="form-input"
            style={{
              flex: 1,
              minWidth: "260px",
              padding: "0.6rem 1rem",
              fontSize: "0.95rem",
              borderRadius: "6px",
              backgroundColor: "var(--bg-primary, #10111a)",
              border: "1px solid var(--border-light, #1c203b)",
              color: "inherit",
            }}
            autoFocus
          />
          <button
            type="submit"
            className="btn btn-primary"
            disabled={searchResults.isFetching}
            style={{
              padding: "0.6rem 1.5rem",
              fontWeight: 600,
              borderRadius: "6px",
            }}
          >
            {searchResults.isFetching
              ? "Searching swarms..."
              : "Search Releases"}
          </button>
        </form>

        <div
          style={{
            display: "flex",
            gap: "1.5rem",
            alignItems: "center",
            marginTop: "0.75rem",
            fontSize: "0.85rem",
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              cursor: "pointer",
              color: "var(--text-secondary, #c7c5d3)",
            }}
          >
            <input
              type="checkbox"
              checked={freeleechOnly}
              onChange={(e) => setFreeleechOnly(e.target.checked)}
            />
            <span>Freeleech Only (100% Free / Zero Ratio Cost)</span>
          </label>
        </div>
      </div>

      {/* Results Table Card */}
      <div
        className="card"
        style={{
          flex: "1 1 auto",
          minHeight: 0,
          display: "flex",
          flexDirection: "column",
          borderRadius: "8px",
          backgroundColor: "var(--bg-secondary, #171b35)",
          border: "1px solid var(--border-light, #1c203b)",
          overflow: "hidden",
        }}
      >
        <div style={{ flex: "1 1 auto", minHeight: 0, overflowY: "auto" }}>
          {searchResults.isFetching && (
            <div style={{ padding: "4rem", textAlign: "center" }}>
              <div
                className="loading"
                style={{ fontSize: "1rem", color: "var(--accent, #ffd166)" }}
              >
                Querying{" "}
                {currentIndexer
                  ? currentIndexer.name
                  : `${enabledIndexers.length} active indexers`}
                ...
              </div>
            </div>
          )}

          {searchResults.isError && (
            <div
              style={{
                padding: "3rem",
                textAlign: "center",
                color: "var(--danger, #ef4444)",
              }}
            >
              Search query failed:{" "}
              {(searchResults.error as Error)?.message ||
                "Check indexer connection"}
            </div>
          )}

          {!searchResults.isFetching &&
            !searchResults.isError &&
            !activeSearchTerm && (
              <div
                style={{
                  padding: "4rem 2rem",
                  textAlign: "center",
                  color: "var(--text-muted, #7e8092)",
                }}
              >
                <div style={{ fontSize: "2.5rem", marginBottom: "0.75rem" }}>
                  🔍
                </div>
                <div
                  style={{
                    fontWeight: 600,
                    fontSize: "1.05rem",
                    color: "var(--text-primary, #f8f4ed)",
                    marginBottom: "0.25rem",
                  }}
                >
                  {currentIndexer
                    ? `Ready to search ${currentIndexer.name}`
                    : "Ready to search all indexers"}
                </div>
                <p
                  style={{
                    maxWidth: "480px",
                    margin: "0 auto",
                    fontSize: "0.85rem",
                  }}
                >
                  Enter keywords above to search releases, compare seeds/peers,
                  and one-click grab directly into your download queue.
                </p>
              </div>
            )}

          {!searchResults.isFetching &&
            !searchResults.isError &&
            activeSearchTerm &&
            filtered.length === 0 && (
              <div
                style={{
                  padding: "4rem 2rem",
                  textAlign: "center",
                  color: "var(--text-muted, #7e8092)",
                }}
              >
                <div style={{ fontSize: "2.5rem", marginBottom: "0.75rem" }}>
                  📭
                </div>
                <div
                  style={{
                    fontWeight: 600,
                    fontSize: "1.05rem",
                    color: "var(--text-primary, #f8f4ed)",
                    marginBottom: "0.25rem",
                  }}
                >
                  No releases found for {'"'}
                  {activeSearchTerm}
                  {'"'}
                </div>
                <p
                  style={{
                    maxWidth: "440px",
                    margin: "0 auto",
                    fontSize: "0.85rem",
                  }}
                >
                  {!indexers || indexers.length === 0
                    ? "No indexers are currently configured. Add a Torznab indexer or sync with Prowlarr in Settings > Indexers to enable search."
                    : "Try adjusting your search query, unchecking Freeleech filter, or adding more indexers in Settings."}
                </p>
              </div>
            )}

          {!searchResults.isFetching &&
            !searchResults.isError &&
            filtered.length > 0 && (
              <table
                className="table"
                style={{ width: "100%", borderCollapse: "collapse" }}
              >
                <thead>
                  <tr
                    style={{
                      borderBottom: "1px solid var(--border-light, #1c203b)",
                      textAlign: "left",
                      fontSize: "0.8rem",
                      color: "var(--text-muted, #7e8092)",
                      position: "sticky",
                      top: 0,
                      backgroundColor: "var(--bg-primary, #10111a)",
                      zIndex: 2,
                    }}
                  >
                    <th style={{ padding: "0.75rem 1rem" }}>
                      Title & Categories
                    </th>
                    <th style={{ padding: "0.75rem 1rem", width: "140px" }}>
                      Indexer
                    </th>
                    <th style={{ padding: "0.75rem 1rem", width: "110px" }}>
                      Size
                    </th>
                    <th style={{ padding: "0.75rem 1rem", width: "110px" }}>
                      Seeds / Leech
                    </th>
                    <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                      Published
                    </th>
                    <th
                      style={{
                        padding: "0.75rem 1rem",
                        width: "100px",
                        textAlign: "right",
                      }}
                    >
                      Action
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((rel) => {
                    const itemKey = rel.guid || rel.infoHash || rel.title;
                    const isDownloading = downloadingGuid === itemKey;
                    const isFree =
                      Boolean(rel.isFreeleech) ||
                      rel.downloadVolumeFactor === 0 ||
                      (rel.category || "")
                        .toLowerCase()
                        .includes("freeleech") ||
                      (rel.categories || []).some((c) =>
                        c.toLowerCase().includes("freeleech"),
                      ) ||
                      (rel.downloadUrl || "")
                        .toLowerCase()
                        .includes("freeleech") ||
                      (rel.magnetUrl || "").toLowerCase().includes("freeleech");
                    const catList =
                      rel.categories && rel.categories.length > 0
                        ? rel.categories
                        : rel.category
                          ? rel.category
                              .split(",")
                              .map((c) => c.trim())
                              .filter(Boolean)
                          : [];

                    return (
                      <tr
                        key={itemKey}
                        style={{
                          borderBottom: "1px solid rgba(255, 255, 255, 0.05)",
                          fontSize: "0.85rem",
                        }}
                      >
                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              fontWeight: 500,
                              color: "var(--text-primary, #f8f4ed)",
                              wordBreak: "break-word",
                            }}
                          >
                            {rel.title}
                            {isFree && (
                              <span
                                className="badge"
                                style={{
                                  marginLeft: "0.5rem",
                                  fontSize: "0.65rem",
                                  padding: "0.1rem 0.4rem",
                                  borderRadius: "3px",
                                  backgroundColor: "rgba(34, 197, 94, 0.15)",
                                  color: "var(--success, #22c55e)",
                                  fontWeight: 700,
                                }}
                              >
                                FREELEECH
                              </span>
                            )}
                          </div>
                          {catList.length > 0 && (
                            <div
                              style={{
                                display: "flex",
                                gap: "0.3rem",
                                marginTop: "0.3rem",
                              }}
                            >
                              {catList.slice(0, 3).map((c, i) => (
                                <span
                                  key={i}
                                  className="badge badge-secondary"
                                  style={{
                                    fontSize: "0.65rem",
                                    padding: "0.1rem 0.35rem",
                                    borderRadius: "3px",
                                    backgroundColor:
                                      "rgba(255, 255, 255, 0.06)",
                                  }}
                                >
                                  {c}
                                </span>
                              ))}
                            </div>
                          )}
                        </td>

                        <td style={{ padding: "0.75rem 1rem" }}>
                          <span
                            className="badge"
                            style={{
                              fontSize: "0.75rem",
                              padding: "0.15rem 0.5rem",
                              borderRadius: "4px",
                              backgroundColor: "rgba(255, 209, 102, 0.12)",
                              color: "var(--accent, #ffd166)",
                              fontWeight: 600,
                            }}
                          >
                            {rel.indexerName || rel.indexer || "Indexer"}
                          </span>
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {formatBytes(rel.size)}
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            whiteSpace: "nowrap",
                          }}
                        >
                          <span
                            style={{
                              color: "var(--success, #22c55e)",
                              fontWeight: 600,
                            }}
                          >
                            ▲ {rel.seeders ?? 0}
                          </span>{" "}
                          <span
                            style={{
                              color: "var(--text-muted, #7e8092)",
                              marginLeft: "0.25rem",
                            }}
                          >
                            ▼ {rel.leechers ?? 0}
                          </span>
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            fontSize: "0.8rem",
                            color: "var(--text-muted, #7e8092)",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {rel.publishDate ? formatDate(rel.publishDate) : "-"}
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            textAlign: "right",
                          }}
                        >
                          <button
                            type="button"
                            className="btn btn-success"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.35rem 0.75rem",
                              borderRadius: "4px",
                              fontWeight: 600,
                            }}
                            onClick={() => handleGrab(rel)}
                            disabled={isDownloading}
                          >
                            {isDownloading ? "Grabbing..." : "+ Grab"}
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
        </div>
      </div>
    </div>
  );
};

export default Indexers;
