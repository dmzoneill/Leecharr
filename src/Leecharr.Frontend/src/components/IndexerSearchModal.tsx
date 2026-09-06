import React, { useState, useEffect } from "react";
import {
  SparklesIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  CheckCircleIcon,
  CloseIcon,
} from "./icons/AiIcons";
import { ReleaseInfo, AiSearchParameters } from "../api/types";
import {
  useAiNaturalSearch,
  useAiConfig,
  useIndexerSearch,
  useDownloadIndexerRelease,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { useEscapeKey } from "../hooks/useEscapeKey";
import { useTranslation } from "../i18n";

interface IndexerSearchModalProps {
  onClose: () => void;
  onTorrentAdded: () => void;
  initialQuery?: string;
  isOpen?: boolean;
}

export const IndexerSearchModal: React.FC<IndexerSearchModalProps> = ({
  onClose,
  onTorrentAdded,
  initialQuery,
  isOpen,
}) => {
  const { t } = useTranslation();
  useEscapeKey(onClose, isOpen ?? true);

  const [query, setQuery] = useState<string>(initialQuery || "");
  const [activeSearchTerm, setActiveSearchTerm] = useState<string>(
    initialQuery || "",
  );
  const [freeleechOnly, setFreeleechOnly] = useState<boolean>(false);
  const [minSeedersFilter, setMinSeedersFilter] = useState<number>(0);
  const [downloadingKey, setDownloadingKey] = useState<string | null>(null);

  useEffect(() => {
    if (initialQuery) {
      setQuery(initialQuery);
      setActiveSearchTerm(initialQuery);
    }
  }, [initialQuery]);

  // All hooks must be called before any conditional return (Rules of Hooks).
  const { showToast } = useToast();
  const { data: aiConfig } = useAiConfig();
  const isAiSearchEnabled = aiConfig?.enableNaturalSearch !== false;

  const searchResultsQuery = useIndexerSearch(
    { query: activeSearchTerm },
    Boolean(activeSearchTerm.trim()),
  );
  const downloadReleaseMutation = useDownloadIndexerRelease();

  // Collapsible AI Search Bar State
  const [isAiExpanded, setIsAiExpanded] = useState<boolean>(() => {
    return localStorage.getItem("leecharr_ai_search_expanded") === "true";
  });
  const [naturalQuery, setNaturalQuery] = useState<string>("");
  const [aiParams, setAiParams] = useState<AiSearchParameters | null>(null);

  const naturalSearchMutation = useAiNaturalSearch();

  useEffect(() => {
    localStorage.setItem(
      "leecharr_ai_search_expanded",
      isAiExpanded ? "true" : "false",
    );
  }, [isAiExpanded]);

  // Safe to return early now — all hooks have been called above.
  if (isOpen !== undefined && !isOpen) return null;

  const handleAiNaturalParse = (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!naturalQuery.trim() || naturalSearchMutation.isPending) return;

    naturalSearchMutation.mutate(
      { query: naturalQuery.trim() },
      {
        onSuccess: (params) => {
          setAiParams(params);
        },
      },
    );
  };

  const handleApplyAiParams = () => {
    if (!aiParams) return;
    const cleanSearch =
      aiParams.cleanTitle || aiParams.cleanQuery || naturalQuery;
    setQuery(cleanSearch);
    if (aiParams.freeleechOnly) {
      setFreeleechOnly(true);
    }
    if (aiParams.minSeeders > 0) {
      setMinSeedersFilter(aiParams.minSeeders);
    }
    setActiveSearchTerm(cleanSearch.trim());
  };

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    if (query.trim()) {
      setActiveSearchTerm(query.trim());
    }
  };

  const handleGrab = (result: ReleaseInfo) => {
    const itemKey = result.guid || result.infoHash || result.title;
    setDownloadingKey(itemKey);

    downloadReleaseMutation.mutate(
      {
        title: result.title,
        downloadUrl: result.downloadUrl || undefined,
        magnetUrl: result.magnetUrl || undefined,
        infoHash: result.infoHash || undefined,
        indexerId: result.indexerId,
        indexerName: result.indexerName || result.indexer || "",
      },
      {
        onSuccess: () => {
          setDownloadingKey(null);
          showToast(`Added "${result.title}" to download queue`, "success");
          onTorrentAdded();
        },
        onError: (err) => {
          setDownloadingKey(null);
          showToast(
            `Failed to grab release: ${err.message || "Unknown error"}`,
            "error",
          );
        },
      },
    );
  };

  const formatBytes = (bytes: number) => {
    if (!bytes) return "0 B";
    const k = 1024;
    const sizes = ["B", "KB", "MB", "GB", "TB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
  };

  const rawResults = searchResultsQuery.data || [];
  const filteredResults = rawResults.filter((r) => {
    if (minSeedersFilter > 0 && (r.seeders ?? 0) < minSeedersFilter)
      return false;
    if (freeleechOnly) {
      const isFl =
        Boolean(r.isFreeleech) ||
        r.downloadVolumeFactor === 0 ||
        (r.category || "").toLowerCase().includes("freeleech") ||
        (r.categories || []).some((c) =>
          c.toLowerCase().includes("freeleech"),
        ) ||
        (r.downloadUrl || "").toLowerCase().includes("freeleech") ||
        (r.magnetUrl || "").toLowerCase().includes("freeleech");
      return isFl;
    }
    return true;
  });

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-content indexer-search-modal"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: "800px", width: "100%" }}
      >
        <div className="modal-header">
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <SparklesIcon
              size={18}
              style={{ color: "var(--accent-gold, #FFD166)" }}
            />
            <h3>{t("indexers.indexerDiscoverySearch")}</h3>
          </div>
          <button
            type="button"
            className="btn-close"
            onClick={onClose}
            aria-label="Close"
          >
            <CloseIcon size={18} />
          </button>
        </div>

        <div
          className="modal-body"
          style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
        >
          {/* Collapsible Discrete AI Smart Search Accordion */}
          {isAiSearchEnabled && (
            <div
              style={{
                borderRadius: "8px",
                border: "1px solid var(--border-color, #23284B)",
                backgroundColor: "var(--bg-secondary, #171B35)",
                overflow: "hidden",
              }}
            >
              <button
                type="button"
                onClick={() => setIsAiExpanded(!isAiExpanded)}
                style={{
                  width: "100%",
                  padding: "0.5rem 0.75rem",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  backgroundColor: "transparent",
                  border: "none",
                  cursor: "pointer",
                  color: "var(--text-primary, #F8F4ED)",
                  fontSize: "0.75rem",
                  fontWeight: 600,
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <SparklesIcon
                    size={14}
                    style={{ color: "var(--accent-gold, #FFD166)" }}
                  />
                  <span>{t("indexers.aiSmartSearch")}</span>
                  <span
                    style={{
                      fontSize: "0.65rem",
                      padding: "0.1rem 0.35rem",
                      borderRadius: "4px",
                      backgroundColor: "#23284B",
                      color: "#FFD166",
                      fontFamily: "monospace",
                    }}
                  >
                    {t("indexers.ai")}
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.3rem",
                    color: "var(--text-muted, #C7C5D3)",
                  }}
                >
                  <span style={{ fontSize: "0.7rem", fontWeight: 400 }}>
                    {isAiExpanded
                      ? t("indexers.collapse")
                      : t("indexers.expand")}
                  </span>
                  {isAiExpanded ? (
                    <ChevronUpIcon size={14} />
                  ) : (
                    <ChevronDownIcon size={14} />
                  )}
                </div>
              </button>

              {isAiExpanded && (
                <div
                  style={{
                    padding: "0.75rem",
                    borderTop: "1px solid var(--border-color, #23284B)",
                    backgroundColor: "var(--bg-primary, #10111A)",
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.6rem",
                  }}
                >
                  <p
                    style={{
                      margin: 0,
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #C7C5D3)",
                    }}
                  >
                    {t("indexers.describeNaturalQuery")}{" "}
                    <em style={{ color: "var(--accent-gold, #FFD166)" }}>
                      {'"'}
                      {t("indexers.naturalQueryExample")}
                      {'"'}
                    </em>
                    {t("indexers.aiExtractsHint")}
                  </p>

                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.4rem",
                    }}
                  >
                    <input
                      type="text"
                      value={naturalQuery}
                      onChange={(e) => setNaturalQuery(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") handleAiNaturalParse(e);
                      }}
                      placeholder={t("indexers.typeNaturalQuery")}
                      style={{
                        flex: 1,
                        backgroundColor: "var(--bg-secondary, #171B35)",
                        border: "1px solid var(--border-color, #23284B)",
                        borderRadius: "6px",
                        padding: "0.4rem 0.6rem",
                        fontSize: "0.75rem",
                        color: "var(--text-primary, #F8F4ED)",
                        outline: "none",
                      }}
                    />
                    <button
                      type="button"
                      onClick={() => handleAiNaturalParse()}
                      disabled={
                        !naturalQuery.trim() || naturalSearchMutation.isPending
                      }
                      style={{
                        padding: "0.4rem 0.75rem",
                        backgroundColor: "#23284B",
                        color: "#FFD166",
                        border: "1px solid rgba(255, 209, 102, 0.3)",
                        borderRadius: "6px",
                        fontSize: "0.75rem",
                        fontWeight: 600,
                        cursor: "pointer",
                        display: "flex",
                        alignItems: "center",
                        gap: "0.3rem",
                        opacity:
                          !naturalQuery.trim() ||
                          naturalSearchMutation.isPending
                            ? 0.5
                            : 1,
                      }}
                    >
                      <SparklesIcon size={13} />
                      <span>
                        {naturalSearchMutation.isPending
                          ? t("indexers.parsing")
                          : t("indexers.extractIntent")}
                      </span>
                    </button>
                  </div>

                  {/* Parsed Parameter Chips */}
                  {aiParams && (
                    <div
                      style={{
                        backgroundColor: "var(--bg-secondary, #171B35)",
                        border: "1px solid var(--border-color, #23284B)",
                        borderRadius: "6px",
                        padding: "0.6rem",
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.5rem",
                      }}
                    >
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "space-between",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "0.75rem",
                            fontWeight: 700,
                            color: "var(--text-primary, #F8F4ED)",
                            display: "flex",
                            alignItems: "center",
                            gap: "0.3rem",
                          }}
                        >
                          <CheckCircleIcon
                            size={14}
                            style={{ color: "#34d399" }}
                          />
                          {t("indexers.extractedFiltersConfidence")}{" "}
                          {Math.round(aiParams.confidenceScore * 100)}%):
                        </span>
                        <button
                          type="button"
                          onClick={handleApplyAiParams}
                          style={{
                            padding: "0.25rem 0.6rem",
                            backgroundColor: "var(--accent-gold, #FFD166)",
                            color: "#10111A",
                            border: "none",
                            borderRadius: "4px",
                            fontSize: "0.75rem",
                            fontWeight: 700,
                            cursor: "pointer",
                          }}
                        >
                          {t("indexers.applySearch")}
                        </button>
                      </div>

                      <div
                        style={{
                          display: "flex",
                          flexWrap: "wrap",
                          gap: "0.3rem",
                        }}
                      >
                        {aiParams.cleanTitle && (
                          <span
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.15rem 0.4rem",
                              borderRadius: "4px",
                              backgroundColor: "#23284B",
                              color: "#F8F4ED",
                              fontFamily: "monospace",
                            }}
                          >
                            {t("common.title")}:{" "}
                            <strong>{aiParams.cleanTitle}</strong>
                          </span>
                        )}
                        {aiParams.category && (
                          <span
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.15rem 0.4rem",
                              borderRadius: "4px",
                              backgroundColor: "#23284B",
                              color: "#FFD166",
                              fontFamily: "monospace",
                            }}
                          >
                            {t("common.category")}:{" "}
                            <strong>{aiParams.category}</strong>
                          </span>
                        )}
                        {aiParams.resolution && (
                          <span
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.15rem 0.4rem",
                              borderRadius: "4px",
                              backgroundColor: "#23284B",
                              color: "#7dd3fc",
                              fontFamily: "monospace",
                            }}
                          >
                            {t("indexers.resolution")}:{" "}
                            <strong>{aiParams.resolution}</strong>
                          </span>
                        )}
                        {aiParams.quality && (
                          <span
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.15rem 0.4rem",
                              borderRadius: "4px",
                              backgroundColor: "#23284B",
                              color: "#d8b4fe",
                              fontFamily: "monospace",
                            }}
                          >
                            {t("indexers.quality")}:{" "}
                            <strong>{aiParams.quality}</strong>
                          </span>
                        )}
                        {aiParams.minSeeders > 0 && (
                          <span
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.15rem 0.4rem",
                              borderRadius: "4px",
                              backgroundColor: "rgba(52, 211, 153, 0.2)",
                              color: "#6ee7b7",
                              fontFamily: "monospace",
                            }}
                          >
                            {t("indexers.seedsGe")}{" "}
                            <strong>{aiParams.minSeeders}</strong>
                          </span>
                        )}
                        {aiParams.freeleechOnly && (
                          <span
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.15rem 0.4rem",
                              borderRadius: "4px",
                              backgroundColor: "rgba(245, 158, 11, 0.2)",
                              color: "#fcd34d",
                              fontFamily: "monospace",
                              fontWeight: 700,
                            }}
                          >
                            {t("indexers.freeleechOnly")}
                          </span>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* Standard Search Bar */}
          <form onSubmit={handleSearch} className="search-form">
            <input
              type="text"
              placeholder={t("indexers.searchPlaceholder")}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="search-input"
              autoFocus
            />
            <button
              type="submit"
              className="btn btn-primary"
              disabled={searchResultsQuery.isLoading}
            >
              {searchResultsQuery.isLoading
                ? t("indexers.searchingBtn")
                : t("indexers.searchBtn")}
            </button>
          </form>

          <div className="filter-bar">
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={freeleechOnly}
                onChange={(e) => setFreeleechOnly(e.target.checked)}
              />
              {t("indexers.freeleechOnly100")}
            </label>
          </div>

          <div className="results-list">
            {searchResultsQuery.isLoading ? (
              <p className="text-muted text-center py-4">
                {t("indexers.searchingIndexers")}
              </p>
            ) : filteredResults.length === 0 ? (
              <p className="text-muted text-center py-4">
                {activeSearchTerm
                  ? t("indexers.noResults")
                  : t("indexers.searchPrompt")}
              </p>
            ) : (
              filteredResults.map((r, i) => {
                const itemKey = r.guid || r.infoHash || r.title;
                const isGrabbing = downloadingKey === itemKey;
                const isFl =
                  Boolean(r.isFreeleech) ||
                  r.downloadVolumeFactor === 0 ||
                  (r.category || "").toLowerCase().includes("freeleech") ||
                  (r.categories || []).some((c) =>
                    c.toLowerCase().includes("freeleech"),
                  ) ||
                  (r.downloadUrl || "").toLowerCase().includes("freeleech") ||
                  (r.magnetUrl || "").toLowerCase().includes("freeleech");

                return (
                  <div key={r.guid || r.infoHash || i} className="result-card">
                    <div className="result-details">
                      <div className="result-title">
                        <strong>{r.title}</strong>
                        {isFl && (
                          <span className="freeleech-badge">
                            {t("indexers.freeleech")}
                          </span>
                        )}
                      </div>
                      <div className="result-meta">
                        <span className="meta-item">
                          <strong>{t("indexers.indexer")}:</strong>{" "}
                          {r.indexerName || r.indexer || "Torznab"}
                        </span>
                        <span className="meta-item">
                          <strong>{t("common.category")}:</strong>{" "}
                          {(r.categories && r.categories.length > 0
                            ? r.categories.join(", ")
                            : r.category) || "General"}
                        </span>
                        <span className="meta-item">
                          <strong>{t("common.size")}:</strong>{" "}
                          {formatBytes(r.size)}
                        </span>
                        <span className="meta-item">
                          <strong>{t("indexers.seeders")}:</strong>{" "}
                          {r.seeders ?? 0}
                        </span>
                        <span className="meta-item">
                          <strong>{t("indexers.leechers")}:</strong>{" "}
                          {r.leechers ?? 0}
                        </span>
                      </div>
                    </div>
                    <button
                      className="btn btn-grab"
                      onClick={() => handleGrab(r)}
                      disabled={isGrabbing}
                    >
                      {isGrabbing ? t("indexers.grabbing") : t("indexers.grab")}
                    </button>
                  </div>
                );
              })
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
