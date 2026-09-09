import React, { useState, useEffect } from "react";
import { useTranslation } from "../../i18n";
import {
  useIndexers,
  useIndexerSearch,
  useDownloadIndexerRelease,
} from "../../api/hooks";
import { formatBytes, formatDate } from "../../utils/formatters";
import { useToast } from "../../context/ToastContext";
import type { ReleaseInfo } from "../../api/types";

export interface IndexerSearchTabProps {
  initialQuery?: string;
  selectedCategory?: string;
  isModal?: boolean;
}

export function IndexerSearchTab({
  initialQuery = "",
  selectedCategory = "",
  isModal = false,
}: IndexerSearchTabProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();

  const [searchQuery, setSearchQuery] = useState(initialQuery);
  const [activeSearchTerm, setActiveSearchTerm] = useState(initialQuery);
  const [selectedIndexerId, setSelectedIndexerId] = useState<
    number | undefined
  >(undefined);
  const [downloadingGuid, setDownloadingGuid] = useState<string | null>(null);

  const { data: indexers } = useIndexers();
  const enabledIndexers = indexers?.filter((i) => i.enable) || [];

  const searchResults = useIndexerSearch(
    {
      query: activeSearchTerm,
      indexerId: selectedIndexerId,
    },
    Boolean(activeSearchTerm.trim()),
  );

  const downloadReleaseMutation = useDownloadIndexerRelease();

  useEffect(() => {
    if (initialQuery) {
      setSearchQuery(initialQuery);
      setActiveSearchTerm(initialQuery);
    }
  }, [initialQuery]);

  // Debounced auto-search as user types
  useEffect(() => {
    const trimmed = searchQuery.trim();
    if (trimmed !== activeSearchTerm) {
      const timer = setTimeout(() => {
        setActiveSearchTerm(trimmed);
      }, 350);
      return () => clearTimeout(timer);
    }
  }, [searchQuery, activeSearchTerm]);

  const handleSearchSubmit = (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (searchQuery.trim()) {
      setActiveSearchTerm(searchQuery.trim());
    }
  };

  const handleAddRelease = (release: ReleaseInfo) => {
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
        category: selectedCategory,
      },
      {
        onSuccess: () => {
          setDownloadingGuid(null);
          showToast(
            t("addTorrent.addedToDownloadQueue", {
              title: release.title,
              defaultValue: `Added "${release.title}" to download queue`,
            }),
            "success",
          );
        },
        onError: (err) => {
          setDownloadingGuid(null);
          showToast(
            t("addTorrent.failedToAddRelease", {
              message: err.message || "Unknown error",
              defaultValue: `Failed to add release: ${err.message || "Unknown error"}`,
            }),
            "error",
          );
        },
      },
    );
  };

  if (enabledIndexers.length === 0) {
    return (
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          flex: "1 1 auto",
          minHeight: 0,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            padding: "2.5rem 1rem",
            textAlign: "center",
            backgroundColor: "var(--bg-primary, #10111a)",
            borderRadius: "8px",
            border: "1px solid var(--border-light, #1c203b)",
          }}
        >
          <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>🔌</div>
          <div style={{ fontWeight: 600, marginBottom: "0.4rem" }}>
            {t(
              "addTorrent.noEnabledIndexers",
              "No Enabled Indexers Configured",
            )}
          </div>
          <p
            style={{
              color: "var(--text-muted, #7e8092)",
              fontSize: "0.85rem",
              maxWidth: "420px",
              margin: "0 auto 1.25rem",
            }}
          >
            {t(
              "addTorrent.connectIndexersDesc",
              "Connect Jackett, Prowlarr, Torznab, or Newznab indexers in Settings to search releases directly.",
            )}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        flex: "1 1 auto",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      <form
        onSubmit={handleSearchSubmit}
        style={{
          display: "flex",
          gap: "0.5rem",
          flexWrap: "wrap",
          marginBottom: "1rem",
          flexShrink: 0,
        }}
      >
        <input
          type="text"
          className="form-control"
          placeholder={t(
            "addTorrent.searchReleasesPlaceholder",
            "Search releases (e.g. Ubuntu, Debian, 1080p, 4k)...",
          )}
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          style={{
            flex: 1,
            minWidth: "240px",
            padding: "0.5rem 0.85rem",
            borderRadius: "6px",
            backgroundColor: "var(--bg-primary, #10111a)",
            border: "1px solid var(--border-light, #1c203b)",
            color: "inherit",
            fontSize: "0.9rem",
          }}
          autoFocus
        />
        {enabledIndexers.length > 1 && (
          <select
            className="form-control"
            value={selectedIndexerId ?? ""}
            onChange={(e) =>
              setSelectedIndexerId(
                e.target.value ? Number(e.target.value) : undefined,
              )
            }
            style={{
              backgroundColor: "var(--bg-primary, #10111a)",
              color: "inherit",
              border: "1px solid var(--border-light, #1c203b)",
              borderRadius: "6px",
              padding: "0.5rem 0.85rem",
              fontSize: "0.85rem",
            }}
          >
            <option value="">
              {t("addTorrent.allIndexersCount", {
                count: enabledIndexers.length,
                defaultValue: `All Indexers (${enabledIndexers.length})`,
              })}
            </option>
            {enabledIndexers.map((idx) => (
              <option key={idx.id} value={idx.id}>
                {idx.name} ({idx.indexerType})
              </option>
            ))}
          </select>
        )}
        <button
          type="submit"
          className="btn btn-primary"
          disabled={searchResults.isFetching}
          style={{ borderRadius: "6px", padding: "0.5rem 1.25rem" }}
        >
          {searchResults.isFetching
            ? t("addTorrent.searching", "Searching...")
            : t("common.search", "Search")}
        </button>
      </form>

      {/* Results Container */}
      <div
        style={{
          flex: isModal ? undefined : "1 1 auto",
          maxHeight: isModal ? "480px" : undefined,
          minHeight: 0,
          overflowY: "auto",
          border: "1px solid var(--border-light, #1c203b)",
          borderRadius: "8px",
          backgroundColor: "var(--bg-primary, #10111a)",
          boxShadow: "inset 0 2px 6px rgba(0, 0, 0, 0.2)",
        }}
      >
        {searchResults.isFetching && (
          <div style={{ padding: "3rem", textAlign: "center" }}>
            <div className="loading">
              {t(
                "addTorrent.searchingIndexers",
                "Searching configured indexers...",
              )}
            </div>
          </div>
        )}

        {searchResults.isError && (
          <div
            style={{
              padding: "2rem",
              color: "var(--danger, #ef4444)",
              textAlign: "center",
            }}
          >
            {t("addTorrent.searchFailed", "Search failed:")}{" "}
            {(searchResults.error as Error)?.message ||
              t(
                "addTorrent.checkIndexerConnection",
                "Check indexer connection",
              )}
          </div>
        )}

        {!searchResults.isFetching &&
          !searchResults.isError &&
          activeSearchTerm &&
          (searchResults.data?.length ?? 0) === 0 && (
            <div
              style={{
                padding: "3rem",
                textAlign: "center",
                color: "var(--text-muted, #7e8092)",
              }}
            >
              {t("addTorrent.noReleasesFound", {
                query: activeSearchTerm,
                defaultValue: `No releases found for "${activeSearchTerm}". Try different keywords or indexer.`,
              })}
            </div>
          )}

        {!searchResults.isFetching && !activeSearchTerm && (
          <div
            style={{
              padding: "3rem",
              textAlign: "center",
              color: "var(--text-muted, #7e8092)",
            }}
          >
            {t("addTorrent.typeKeywordPrompt", {
              indexers: enabledIndexers.map((i) => i.name).join(", "),
              defaultValue: `Type a keyword above to search across configured indexers (${enabledIndexers.map((i) => i.name).join(", ")}).`,
            })}
          </div>
        )}

        {!searchResults.isFetching &&
          (searchResults.data?.length ?? 0) > 0 && (
            <table
              className="table"
              style={{ width: "100%", borderCollapse: "collapse" }}
            >
              <thead>
                <tr
                  style={{
                    borderBottom:
                      "1px solid var(--border-light, #1c203b)",
                    textAlign: "left",
                    fontSize: "0.8rem",
                    color: "var(--text-muted, #7e8092)",
                    position: "sticky",
                    top: 0,
                    backgroundColor: "var(--bg-secondary, #171b35)",
                    zIndex: 2,
                  }}
                >
                  <th style={{ padding: "0.65rem 0.85rem" }}>
                    {t("addTorrent.colTitle", "Title")}
                  </th>
                  <th
                    style={{
                      padding: "0.65rem 0.85rem",
                      width: "130px",
                    }}
                  >
                    {t("addTorrent.colIndexer", "Indexer")}
                  </th>
                  <th
                    style={{
                      padding: "0.65rem 0.85rem",
                      width: "100px",
                    }}
                  >
                    {t("addTorrent.colSize", "Size")}
                  </th>
                  <th
                    style={{
                      padding: "0.65rem 0.85rem",
                      width: "95px",
                    }}
                  >
                    {t("addTorrent.colPeers", "Peers")}
                  </th>
                  <th
                    style={{
                      padding: "0.65rem 0.85rem",
                      width: "100px",
                    }}
                  >
                    {t("addTorrent.colDate", "Date")}
                  </th>
                  <th
                    style={{
                      padding: "0.65rem 0.85rem",
                      width: "90px",
                      textAlign: "right",
                    }}
                  >
                    {t("addTorrent.colAction", "Action")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {searchResults.data?.map((rel) => {
                  const itemKey = rel.guid || rel.infoHash || rel.title;
                  const isDownloading = downloadingGuid === itemKey;
                  const isFl =
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
                    (rel.magnetUrl || "")
                      .toLowerCase()
                      .includes("freeleech");
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
                        borderBottom:
                          "1px solid rgba(255, 255, 255, 0.05)",
                        fontSize: "0.85rem",
                      }}
                    >
                      <td style={{ padding: "0.65rem 0.85rem" }}>
                        <div
                          style={{
                            fontWeight: 500,
                            wordBreak: "break-word",
                          }}
                        >
                          {rel.title}
                          {isFl && (
                            <span
                              className="badge"
                              style={{
                                marginLeft: "0.5rem",
                                fontSize: "0.65rem",
                                padding: "0.1rem 0.4rem",
                                borderRadius: "3px",
                                backgroundColor:
                                  "rgba(34, 197, 94, 0.15)",
                                color: "var(--success, #22c55e)",
                                fontWeight: 700,
                              }}
                            >
                              {t("addTorrent.freeleech", "FREELEECH")}
                            </span>
                          )}
                        </div>
                        {catList.length > 0 && (
                          <div
                            style={{
                              display: "flex",
                              gap: "0.3rem",
                              marginTop: "0.25rem",
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
                                    "rgba(255, 255, 255, 0.08)",
                                }}
                              >
                                {c}
                              </span>
                            ))}
                          </div>
                        )}
                      </td>

                      <td style={{ padding: "0.65rem 0.85rem" }}>
                        <span
                          className="badge badge-primary"
                          style={{
                            fontSize: "0.75rem",
                            borderRadius: "4px",
                            backgroundColor:
                              "rgba(255, 209, 102, 0.15)",
                            color: "var(--accent, #ffd166)",
                          }}
                        >
                          {rel.indexerName ||
                            rel.indexer ||
                            t("components.indexer", "Indexer")}
                        </span>
                      </td>

                      <td
                        style={{
                          padding: "0.65rem 0.85rem",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {formatBytes(rel.size)}
                      </td>

                      <td
                        style={{
                          padding: "0.65rem 0.85rem",
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
                            marginLeft: "0.2rem",
                          }}
                        >
                          ▼ {rel.leechers ?? 0}
                        </span>
                      </td>

                      <td
                        style={{
                          padding: "0.65rem 0.85rem",
                          fontSize: "0.8rem",
                          color: "var(--text-muted, #7e8092)",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {rel.publishDate
                          ? formatDate(rel.publishDate)
                          : "-"}
                      </td>

                      <td
                        style={{
                          padding: "0.65rem 0.85rem",
                          textAlign: "right",
                        }}
                      >
                        <button
                          type="button"
                          className="btn btn-success"
                          style={{
                            fontSize: "0.78rem",
                            padding: "0.3rem 0.65rem",
                            borderRadius: "4px",
                          }}
                          onClick={() => handleAddRelease(rel)}
                          disabled={isDownloading}
                        >
                          {isDownloading
                            ? t("addTorrent.addingRelease", "Adding...")
                            : t("addTorrent.addReleaseBtn", "+ Add")}
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
  );
}

export default IndexerSearchTab;
