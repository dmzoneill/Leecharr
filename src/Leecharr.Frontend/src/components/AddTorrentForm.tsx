import React, { useState, useRef, useCallback, useEffect, useMemo } from "react";
import {
  useAddTorrent,
  useIndexers,
  useIndexerSearch,
  useDownloadIndexerRelease,
  useCategories,
  AddTorrentResult,
} from "../api/hooks";
import { formatBytes, formatDate } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import { api } from "../api/client";
import type { ReleaseInfo, TorrentCreationResult } from "../api/types";

export interface AddTorrentFormProps {
  initialMode?: "file" | "magnet" | "search" | "create";
  initialQuery?: string;
  isModal?: boolean;
  onClose?: () => void;
  onSuccess?: () => void;
}

export type InputMode = "file" | "magnet" | "search" | "create";

interface MagnetInfo {
  name?: string;
  hash?: string;
  trackerCount: number;
}

function parseMagnetPreview(uri: string): MagnetInfo | null {
  const trimmed = uri.trim();
  if (!trimmed.startsWith("magnet:?")) return null;
  try {
    const rawParams = trimmed.substring(8);
    const params = new URLSearchParams(rawParams);
    const xt = params.get("xt") || "";
    const hash = xt.replace(/^urn:btih:/i, "").substring(0, 40);
    const name = params.get("dn") || undefined;
    const trackers = params.getAll("tr");
    return {
      name: name ? decodeURIComponent(name.replace(/\+/g, " ")) : undefined,
      hash: hash || undefined,
      trackerCount: trackers.length,
    };
  } catch {
    return null;
  }
}

export function AddTorrentForm({
  initialMode = "file",
  initialQuery = "",
  isModal = false,
  onClose,
  onSuccess,
}: AddTorrentFormProps) {
  const [mode, setMode] = useState<InputMode>(initialMode);
  const [files, setFiles] = useState<File[]>([]);
  const [magnetLink, setMagnetLink] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("");
  const [isPaused, setIsPaused] = useState(false);
  const [isDragOver, setIsDragOver] = useState(false);
  const [resultMessage, setResultMessage] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const addTorrent = useAddTorrent();
  const { showToast } = useToast();

  const { data: categories } = useCategories();

  // Indexer Search State
  const [searchQuery, setSearchQuery] = useState(initialQuery);
  const [activeSearchTerm, setActiveSearchTerm] = useState(initialQuery);
  const [selectedIndexerId, setSelectedIndexerId] = useState<number | undefined>(undefined);
  const [downloadingGuid, setDownloadingGuid] = useState<string | null>(null);

  // Torrent Creator State
  const [createPath, setCreatePath] = useState("");
  const [createName, setCreateName] = useState("");
  const [createComment, setCreateComment] = useState("");
  const [createCreatedBy, setCreateCreatedBy] = useState("Leecharr");
  const [createPieceLength, setCreatePieceLength] = useState(0);
  const [createIsPrivate, setCreateIsPrivate] = useState(false);
  const [createTrackers, setCreateTrackers] = useState("");
  const [createWebSeeds, setCreateWebSeeds] = useState("");
  const [createOutputPath, setCreateOutputPath] = useState("");
  const [isCreating, setIsCreating] = useState(false);
  const [createResult, setCreateResult] = useState<TorrentCreationResult | null>(null);

  const { data: indexers } = useIndexers();
  const enabledIndexers = indexers?.filter((i) => i.enable) || [];

  const searchResults = useIndexerSearch(
    {
      query: activeSearchTerm,
      indexerId: selectedIndexerId,
    },
    mode === "search" && Boolean(activeSearchTerm.trim())
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

  // Pre-select default category if none chosen
  useEffect(() => {
    if (!selectedCategory && categories && categories.length > 0) {
      const defaultCat = categories.find((c) => c.isDefault);
      if (defaultCat) {
        setSelectedCategory(defaultCat.name);
      }
    }
  }, [categories, selectedCategory]);

  const addFiles = useCallback((incoming: FileList | File[]) => {
    const torrentFiles = Array.from(incoming).filter((f) => f.name.endsWith(".torrent"));
    if (torrentFiles.length === 0) return;
    setFiles((prev) => {
      const existing = new Set(prev.map((f) => f.name));
      const merged = [...prev];
      for (const f of torrentFiles) {
        if (!existing.has(f.name)) {
          merged.push(f);
          existing.add(f.name);
        }
      }
      return merged;
    });
  }, []);

  const removeFile = (name: string) => {
    setFiles((prev) => prev.filter((f) => f.name !== name));
  };

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);
      addFiles(e.dataTransfer.files);
    },
    [addFiles]
  );

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      addFiles(e.target.files);
    }
    e.target.value = "";
  };

  const handleSubmit = () => {
    if (mode === "file" && files.length > 0) {
      setResultMessage(null);
      addTorrent.mutate(
        { files, category: selectedCategory, isPaused },
        {
          onSuccess: (result: AddTorrentResult) => {
            if (result && result.failed && result.failed.length === 0) {
              showToast(`Added ${result.added.length} torrent(s) successfully`, "success");
              if (onSuccess) onSuccess();
              if (onClose) onClose();
              return;
            }
            if (result && result.failed && result.failed.length > 0) {
              const failedNames = new Set(result.failed.map((f) => f.fileName));
              setFiles((prev) => prev.filter((f) => failedNames.has(f.name)));
              setResultMessage(
                `${result.added.length} added, ${result.failed.length} skipped: ${result.failed
                  .map((f) => `${f.fileName} (${f.reason})`)
                  .join("; ")}`
              );
            } else {
              showToast("Torrent(s) added successfully", "success");
              if (onSuccess) onSuccess();
              if (onClose) onClose();
            }
          },
          onError: (err) => {
            showToast(`Failed to upload torrents: ${err.message}`, "error");
          },
        }
      );
    } else if (mode === "magnet" && magnetLink.trim()) {
      addTorrent.mutate(
        { magnetLink: magnetLink.trim(), category: selectedCategory, isPaused },
        {
          onSuccess: () => {
            showToast("Magnet link added successfully", "success");
            setMagnetLink("");
            if (onSuccess) onSuccess();
            if (onClose) onClose();
          },
          onError: (err) => {
            showToast(`Failed to add magnet: ${err.message}`, "error");
          },
        }
      );
    }
  };

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
          showToast(`Added "${release.title}" to download queue`, "success");
        },
        onError: (err) => {
          setDownloadingGuid(null);
          showToast(`Failed to add release: ${err.message || "Unknown error"}`, "error");
        },
      }
    );
  };

  const handleCreateTorrent = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createPath.trim()) {
      showToast("Source path is required to create a torrent", "error");
      return;
    }

    try {
      setIsCreating(true);
      setCreateResult(null);

      const trackersList = createTrackers
        .split("\n")
        .map((t) => t.trim())
        .filter((t) => t.length > 0);

      const webSeedsList = createWebSeeds
        .split("\n")
        .map((w) => w.trim())
        .filter((w) => w.length > 0);

      const res = await api.createTorrent({
        path: createPath.trim(),
        name: createName.trim() || undefined,
        comment: createComment.trim() || undefined,
        createdBy: createCreatedBy.trim() || undefined,
        isPrivate: createIsPrivate,
        pieceLength: createPieceLength > 0 ? createPieceLength : undefined,
        trackers: trackersList.length > 0 ? trackersList : undefined,
        webSeeds: webSeedsList.length > 0 ? webSeedsList : undefined,
        outputPath: createOutputPath.trim() || undefined,
      });

      setCreateResult(res);
      if (res.success) {
        showToast("Torrent created successfully!", "success");
      } else {
        showToast(res.errorMessage || "Failed to create torrent", "error");
      }
    } catch (err: any) {
      showToast(err?.message || "Failed to create torrent", "error");
    } finally {
      setIsCreating(false);
    }
  };

  const isMagnetValid = magnetLink.trim().startsWith("magnet:?");
  const magnetPreview = useMemo(() => parseMagnetPreview(magnetLink), [magnetLink]);
  const canSubmit = (mode === "file" && files.length > 0) || (mode === "magnet" && isMagnetValid);

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        flex: "1 1 auto",
        minHeight: 0,
        height: "100%",
        overflow: "hidden",
      }}
    >
      {/* Mode Switcher Tabs */}
      <div
        className="tab-nav"
        style={{
          display: "flex",
          gap: "0.5rem",
          marginBottom: "1.25rem",
          borderBottom: "1px solid var(--border-light, #1c203b)",
          paddingBottom: "0.5rem",
          flexShrink: 0,
        }}
      >
        <button
          type="button"
          className={`tab-btn ${mode === "file" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("file")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
            backgroundColor: mode === "file" ? "var(--accent, #ffd166)" : "transparent",
            color: mode === "file" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          📁 Torrent File
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "magnet" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("magnet")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
            backgroundColor: mode === "magnet" ? "var(--accent, #ffd166)" : "transparent",
            color: mode === "magnet" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          🧲 Magnet Link
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "search" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("search")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
            backgroundColor: mode === "search" ? "var(--accent, #ffd166)" : "transparent",
            color: mode === "search" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          🔍 Indexer Search
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "create" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("create")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
            backgroundColor: mode === "create" ? "var(--accent, #ffd166)" : "transparent",
            color: mode === "create" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          ⚡ Create Torrent
        </button>
      </div>

      {/* Mode 1: File Upload */}
      {mode === "file" && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            flex: "1 1 auto",
            minHeight: 0,
            justifyContent: files.length > 0 ? "flex-start" : "center",
            alignItems: "center",
            width: "100%",
            padding: "1rem 0",
          }}
        >
          <div
            className={`drop-zone ${isDragOver ? "drop-zone-active" : ""} ${files.length > 0 ? "drop-zone-has-file" : ""}`}
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
            style={{
              border: isDragOver
                ? "2px dashed var(--accent, #ffd166)"
                : "2px dashed rgba(255, 255, 255, 0.15)",
              borderRadius: "8px",
              padding: isModal ? "2.5rem 1.5rem" : "3.5rem 2rem",
              textAlign: "center",
              cursor: "pointer",
              backgroundColor: isDragOver
                ? "rgba(255, 209, 102, 0.08)"
                : "var(--bg-primary, #10111a)",
              transition: "all 0.2s ease",
              width: "100%",
              maxWidth: isModal ? "100%" : "640px",
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              justifyContent: "center",
              margin: files.length > 0 ? "0 auto" : "auto",
            }}
          >
            <div style={{ fontSize: "2.5rem", marginBottom: "0.6rem" }}>📤</div>
            {files.length > 0 ? (
              <div>
                <span style={{ fontWeight: 600, color: "var(--accent, #ffd166)" }}>
                  {files.length === 1
                    ? `${files[0].name} selected`
                    : `${files.length} torrent files selected`}
                </span>
                <div
                  style={{
                    fontSize: "0.8rem",
                    color: "var(--text-muted, #7e8092)",
                    marginTop: "0.25rem",
                  }}
                >
                  Click or drag more files to add
                </div>
              </div>
            ) : (
              <div>
                <div style={{ fontWeight: 500, fontSize: "1rem" }}>
                  Drop .torrent files here or click to browse
                </div>
                <div
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--text-muted, #7e8092)",
                    marginTop: "0.35rem",
                  }}
                >
                  Supports multiple .torrent files simultaneously
                </div>
              </div>
            )}
          </div>

          <input
            ref={fileInputRef}
            type="file"
            accept=".torrent"
            multiple
            style={{ display: "none" }}
            onChange={handleFileChange}
          />

          {files.length > 0 && (
            <div
              style={{
                marginTop: "1rem",
                width: "100%",
                maxWidth: isModal ? "100%" : "640px",
              }}
            >
              <div
                style={{
                  fontSize: "0.8rem",
                  fontWeight: 600,
                  textTransform: "uppercase",
                  color: "var(--text-muted, #7e8092)",
                  marginBottom: "0.4rem",
                }}
              >
                Selected Files ({files.length})
              </div>
              <ul
                style={{
                  listStyle: "none",
                  padding: 0,
                  margin: 0,
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.4rem",
                  maxHeight: "180px",
                  overflowY: "auto",
                }}
              >
                {files.map((f) => (
                  <li
                    key={f.name}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      padding: "0.4rem 0.75rem",
                      backgroundColor: "var(--bg-secondary, #171b35)",
                      borderRadius: "6px",
                      border: "1px solid var(--border-light, #1c203b)",
                      fontSize: "0.85rem",
                    }}
                  >
                    <span
                      style={{
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                        marginRight: "0.5rem",
                      }}
                    >
                      📄 {f.name} ({formatBytes(f.size)})
                    </span>
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        removeFile(f.name);
                      }}
                      style={{
                        background: "none",
                        border: "none",
                        color: "var(--danger, #ef4444)",
                        cursor: "pointer",
                        fontSize: "0.85rem",
                        padding: "0.1rem 0.3rem",
                      }}
                      title="Remove file"
                    >
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* Mode 2: Magnet Link */}
      {mode === "magnet" && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            flex: "1 1 auto",
            minHeight: 0,
            justifyContent: "center",
            alignItems: "center",
            width: "100%",
            padding: "1rem 0",
          }}
        >
          <div
            style={{
              width: "100%",
              maxWidth: isModal ? "100%" : "640px",
              display: "flex",
              flexDirection: "column",
              gap: "0.85rem",
              margin: "auto",
            }}
          >
            {/* Header with Title & Quick Action Buttons */}
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <label
                style={{
                  fontSize: "0.95rem",
                  fontWeight: 600,
                  color: "var(--text-primary, #f8f4ed)",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  margin: 0,
                }}
              >
                <span>🧲</span> Magnet URI / Link
              </label>

              <div style={{ display: "flex", gap: "0.4rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={async () => {
                    try {
                      const text = await navigator.clipboard.readText();
                      if (text) setMagnetLink(text.trim());
                    } catch {
                      // clipboard access rejected or unsupported
                    }
                  }}
                  style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                  title="Paste link from clipboard"
                >
                  📋 Paste Clipboard
                </button>
                {magnetLink && (
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    onClick={() => setMagnetLink("")}
                    style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                    title="Clear input"
                  >
                    ✕ Clear
                  </button>
                )}
              </div>
            </div>

            {/* Textarea container */}
            <div
              style={{
                borderRadius: "8px",
                border: magnetLink.trim()
                  ? isMagnetValid
                    ? "1px solid rgba(34, 197, 94, 0.6)"
                    : "1px solid rgba(239, 68, 68, 0.6)"
                  : "1px solid var(--border-light, rgba(255, 255, 255, 0.15))",
                backgroundColor: "var(--bg-primary, #10111a)",
                boxShadow:
                  magnetLink.trim() && isMagnetValid ? "0 0 0 1px rgba(34, 197, 94, 0.2)" : "none",
                transition: "all 0.2s ease",
              }}
            >
              <textarea
                className="form-control"
                placeholder="magnet:?xt=urn:btih:..."
                value={magnetLink}
                onChange={(e) => setMagnetLink(e.target.value)}
                rows={isModal ? 4 : 5}
                style={{
                  width: "100%",
                  minHeight: isModal ? "100px" : "130px",
                  maxHeight: "220px",
                  padding: "0.85rem",
                  borderRadius: "8px",
                  backgroundColor: "transparent",
                  border: "none",
                  outline: "none",
                  boxShadow: "none",
                  color: "inherit",
                  fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace",
                  fontSize: "0.85rem",
                  lineHeight: "1.45",
                  resize: isModal ? "vertical" : "none",
                }}
                autoFocus
              />
            </div>

            {/* Status & Validation Message */}
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                fontSize: "0.8rem",
              }}
            >
              <span style={{ color: "var(--text-muted, #7e8092)" }}>
                Paste any valid BitTorrent v1 or v2 magnet link.
              </span>
              {magnetLink.trim() && (
                <span
                  style={{
                    color: isMagnetValid ? "var(--success, #22c55e)" : "var(--danger, #ef4444)",
                    fontWeight: 600,
                  }}
                >
                  {isMagnetValid ? "✓ Valid Magnet Format" : "✗ Must start with magnet:?"}
                </span>
              )}
            </div>

            {/* Extracted Magnet Details Preview */}
            {isMagnetValid && (
              <div
                style={{
                  marginTop: "0.25rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  backgroundColor: "rgba(34, 197, 94, 0.08)",
                  border: "1px solid rgba(34, 197, 94, 0.2)",
                  fontSize: "0.82rem",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.35rem",
                }}
              >
                {magnetPreview?.name && (
                  <div style={{ display: "flex", gap: "0.5rem" }}>
                    <span
                      style={{
                        color: "var(--text-muted, #7e8092)",
                        minWidth: "75px",
                      }}
                    >
                      Name:
                    </span>
                    <span
                      style={{
                        fontWeight: 600,
                        color: "var(--text-primary, #f8f4ed)",
                        wordBreak: "break-all",
                      }}
                    >
                      {magnetPreview.name}
                    </span>
                  </div>
                )}
                {magnetPreview?.hash && (
                  <div style={{ display: "flex", gap: "0.5rem" }}>
                    <span
                      style={{
                        color: "var(--text-muted, #7e8092)",
                        minWidth: "75px",
                      }}
                    >
                      Info Hash:
                    </span>
                    <span style={{ fontFamily: "monospace", color: "#60a5fa" }}>
                      {magnetPreview.hash}
                    </span>
                  </div>
                )}
                {magnetPreview?.trackerCount !== undefined && magnetPreview.trackerCount > 0 && (
                  <div style={{ display: "flex", gap: "0.5rem" }}>
                    <span
                      style={{
                        color: "var(--text-muted, #7e8092)",
                        minWidth: "75px",
                      }}
                    >
                      Trackers:
                    </span>
                    <span style={{ color: "#4ade80" }}>
                      {magnetPreview.trackerCount} bundled tracker(s)
                    </span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      )}

      {/* Mode 3: Indexer Search */}
      {mode === "search" && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            flex: "1 1 auto",
            minHeight: 0,
            overflow: "hidden",
          }}
        >
          {enabledIndexers.length === 0 ? (
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
                No Enabled Indexers Configured
              </div>
              <p
                style={{
                  color: "var(--text-muted, #7e8092)",
                  fontSize: "0.85rem",
                  maxWidth: "420px",
                  margin: "0 auto 1.25rem",
                }}
              >
                Connect Jackett, Prowlarr, Torznab, or Newznab indexers in Settings to search
                releases directly.
              </p>
            </div>
          ) : (
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
                  placeholder="Search releases (e.g. Ubuntu, Debian, 1080p, 4k)..."
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
                      setSelectedIndexerId(e.target.value ? Number(e.target.value) : undefined)
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
                    <option value="">All Indexers ({enabledIndexers.length})</option>
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
                  {searchResults.isFetching ? "Searching..." : "Search"}
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
                    <div className="loading">Searching configured indexers...</div>
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
                    Search failed:{" "}
                    {(searchResults.error as Error)?.message || "Check indexer connection"}
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
                      No releases found for "{activeSearchTerm}". Try different keywords or indexer.
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
                    Type a keyword above to search across configured indexers (
                    {enabledIndexers.map((i) => i.name).join(", ")}).
                  </div>
                )}

                {!searchResults.isFetching && (searchResults.data?.length ?? 0) > 0 && (
                  <table className="table" style={{ width: "100%", borderCollapse: "collapse" }}>
                    <thead>
                      <tr
                        style={{
                          borderBottom: "1px solid var(--border-light, #1c203b)",
                          textAlign: "left",
                          fontSize: "0.8rem",
                          color: "var(--text-muted, #7e8092)",
                          position: "sticky",
                          top: 0,
                          backgroundColor: "var(--bg-secondary, #171b35)",
                          zIndex: 2,
                        }}
                      >
                        <th style={{ padding: "0.65rem 0.85rem" }}>Title</th>
                        <th
                          style={{
                            padding: "0.65rem 0.85rem",
                            width: "130px",
                          }}
                        >
                          Indexer
                        </th>
                        <th
                          style={{
                            padding: "0.65rem 0.85rem",
                            width: "100px",
                          }}
                        >
                          Size
                        </th>
                        <th
                          style={{
                            padding: "0.65rem 0.85rem",
                            width: "95px",
                          }}
                        >
                          Peers
                        </th>
                        <th
                          style={{
                            padding: "0.65rem 0.85rem",
                            width: "100px",
                          }}
                        >
                          Date
                        </th>
                        <th
                          style={{
                            padding: "0.65rem 0.85rem",
                            width: "90px",
                            textAlign: "right",
                          }}
                        >
                          Action
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
                          (rel.category || "").toLowerCase().includes("freeleech") ||
                          (rel.categories || []).some((c) =>
                            c.toLowerCase().includes("freeleech")
                          ) ||
                          (rel.downloadUrl || "").toLowerCase().includes("freeleech") ||
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
                                        backgroundColor: "rgba(255, 255, 255, 0.08)",
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
                                  backgroundColor: "rgba(255, 209, 102, 0.15)",
                                  color: "var(--accent, #ffd166)",
                                }}
                              >
                                {rel.indexerName || rel.indexer || "Indexer"}
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
                              {rel.publishDate ? formatDate(rel.publishDate) : "-"}
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
                                {isDownloading ? "Adding..." : "+ Add"}
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
          )}
        </div>
      )}

      {/* Mode 4: Torrent Creator */}
      {mode === "create" && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            flex: "1 1 auto",
            minHeight: 0,
            overflowY: "auto",
            paddingRight: "0.5rem",
            gap: "1rem",
          }}
        >
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1rem" }}>
            <div>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  marginBottom: "0.3rem",
                  color: "var(--text-secondary)",
                }}
              >
                Source Path (File or Directory) *
              </label>
              <input
                type="text"
                value={createPath}
                onChange={(e) => setCreatePath(e.target.value)}
                placeholder="/downloads/complete/MyMovie or /data/file.iso"
                className="form-input"
                style={{
                  width: "100%",
                  padding: "0.5rem 0.75rem",
                  fontSize: "0.85rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light, #1c203b)",
                  backgroundColor: "var(--bg-primary, #10111a)",
                  color: "inherit",
                }}
              />
            </div>

            <div>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  marginBottom: "0.3rem",
                  color: "var(--text-secondary)",
                }}
              >
                Torrent Name (Optional)
              </label>
              <input
                type="text"
                value={createName}
                onChange={(e) => setCreateName(e.target.value)}
                placeholder="Defaults to file / folder name"
                className="form-input"
                style={{
                  width: "100%",
                  padding: "0.5rem 0.75rem",
                  fontSize: "0.85rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light, #1c203b)",
                  backgroundColor: "var(--bg-primary, #10111a)",
                  color: "inherit",
                }}
              />
            </div>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1rem" }}>
            <div>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  marginBottom: "0.3rem",
                  color: "var(--text-secondary)",
                }}
              >
                Piece Size
              </label>
              <select
                value={createPieceLength}
                onChange={(e) => setCreatePieceLength(parseInt(e.target.value, 10))}
                className="form-input"
                style={{
                  width: "100%",
                  padding: "0.5rem 0.75rem",
                  fontSize: "0.85rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light, #1c203b)",
                  backgroundColor: "var(--bg-primary, #10111a)",
                  color: "inherit",
                }}
              >
                <option value={0}>Auto (Optimal Size based on content)</option>
                <option value={16384}>16 KiB</option>
                <option value={32768}>32 KiB</option>
                <option value={65536}>64 KiB</option>
                <option value={131072}>128 KiB</option>
                <option value={262144}>256 KiB</option>
                <option value={524288}>512 KiB</option>
                <option value={1048576}>1 MiB</option>
                <option value={2097152}>2 MiB</option>
                <option value={4194304}>4 MiB</option>
                <option value={8388608}>8 MiB</option>
                <option value={16777216}>16 MiB</option>
                <option value={33554432}>32 MiB</option>
              </select>
            </div>

            <div
              style={{ display: "flex", alignItems: "center", gap: "0.5rem", paddingTop: "1.2rem" }}
            >
              <input
                type="checkbox"
                id="createPrivateCheck"
                checked={createIsPrivate}
                onChange={(e) => setCreateIsPrivate(e.target.checked)}
              />
              <label
                htmlFor="createPrivateCheck"
                style={{ fontSize: "0.85rem", color: "var(--text-secondary)", cursor: "pointer" }}
              >
                <strong>Private Torrent</strong> (BEP 27 - Disables DHT & PEX)
              </label>
            </div>
          </div>

          <div>
            <label
              style={{
                display: "block",
                fontSize: "0.85rem",
                fontWeight: 600,
                marginBottom: "0.3rem",
                color: "var(--text-secondary)",
              }}
            >
              Tracker URLs (One per line, tiers separated by empty line)
            </label>
            <textarea
              rows={3}
              value={createTrackers}
              onChange={(e) => setCreateTrackers(e.target.value)}
              placeholder="http://tracker.example.com:80/announce&#10;udp://tracker.opentrackr.org:1337/announce"
              className="form-input"
              style={{
                width: "100%",
                padding: "0.5rem 0.75rem",
                fontSize: "0.85rem",
                borderRadius: "6px",
                border: "1px solid var(--border-light, #1c203b)",
                backgroundColor: "var(--bg-primary, #10111a)",
                color: "inherit",
                fontFamily: "monospace",
              }}
            />
          </div>

          <div>
            <label
              style={{
                display: "block",
                fontSize: "0.85rem",
                fontWeight: 600,
                marginBottom: "0.3rem",
                color: "var(--text-secondary)",
              }}
            >
              Web Seed URLs (One per line)
            </label>
            <textarea
              rows={2}
              value={createWebSeeds}
              onChange={(e) => setCreateWebSeeds(e.target.value)}
              placeholder="https://cdn.example.com/downloads/MyMovie.mkv"
              className="form-input"
              style={{
                width: "100%",
                padding: "0.5rem 0.75rem",
                fontSize: "0.85rem",
                borderRadius: "6px",
                border: "1px solid var(--border-light, #1c203b)",
                backgroundColor: "var(--bg-primary, #10111a)",
                color: "inherit",
                fontFamily: "monospace",
              }}
            />
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1rem" }}>
            <div>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  marginBottom: "0.3rem",
                  color: "var(--text-secondary)",
                }}
              >
                Comment
              </label>
              <input
                type="text"
                value={createComment}
                onChange={(e) => setCreateComment(e.target.value)}
                placeholder="Optional description or license info"
                className="form-input"
                style={{
                  width: "100%",
                  padding: "0.5rem 0.75rem",
                  fontSize: "0.85rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light, #1c203b)",
                  backgroundColor: "var(--bg-primary, #10111a)",
                  color: "inherit",
                }}
              />
            </div>

            <div>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  marginBottom: "0.3rem",
                  color: "var(--text-secondary)",
                }}
              >
                Output .torrent Save Path
              </label>
              <input
                type="text"
                value={createOutputPath}
                onChange={(e) => setCreateOutputPath(e.target.value)}
                placeholder="Optional destination for .torrent file"
                className="form-input"
                style={{
                  width: "100%",
                  padding: "0.5rem 0.75rem",
                  fontSize: "0.85rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-light, #1c203b)",
                  backgroundColor: "var(--bg-primary, #10111a)",
                  color: "inherit",
                }}
              />
            </div>
          </div>

          {createResult && (
            <div
              style={{
                padding: "0.75rem 1rem",
                borderRadius: "6px",
                backgroundColor: createResult.success
                  ? "rgba(16, 185, 129, 0.15)"
                  : "rgba(239, 68, 68, 0.15)",
                border: `1px solid ${createResult.success ? "rgba(16, 185, 129, 0.4)" : "rgba(239, 68, 68, 0.4)"}`,
                fontSize: "0.85rem",
              }}
            >
              {createResult.success ? (
                <div>
                  <div style={{ fontWeight: 700, color: "#10b981", marginBottom: "0.25rem" }}>
                    ✓ Torrent file created successfully!
                  </div>
                  <div>
                    <strong>Info Hash:</strong>{" "}
                    <code style={{ wordBreak: "break-all" }}>{createResult.infoHash}</code>
                  </div>
                  <div>
                    <strong>Total Size:</strong> {formatBytes(createResult.totalSize)} (
                    {createResult.pieceCount} pieces @ {formatBytes(createResult.pieceLength)})
                  </div>
                  {createResult.outputPath && (
                    <div style={{ marginTop: "0.25rem" }}>
                      <strong>Saved To:</strong> <code>{createResult.outputPath}</code>
                    </div>
                  )}
                </div>
              ) : (
                <div style={{ color: "#ef4444" }}>
                  <strong>Creation Error:</strong> {createResult.errorMessage}
                </div>
              )}
            </div>
          )}

          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: "0.5rem",
              marginTop: "0.5rem",
            }}
          >
            {isModal && onClose && (
              <button
                type="button"
                className="btn btn-outline"
                onClick={onClose}
                disabled={isCreating}
                style={{ borderRadius: "6px" }}
              >
                Cancel
              </button>
            )}
            <button
              type="button"
              className="btn btn-primary"
              onClick={handleCreateTorrent}
              disabled={isCreating || !createPath.trim()}
              style={{ borderRadius: "6px", padding: "0.45rem 1.25rem" }}
            >
              {isCreating ? "Hashing & Creating..." : "⚡ Create .torrent"}
            </button>
          </div>
        </div>
      )}

      {/* Category & Download Options in File and Magnet modes */}
      {mode !== "search" && mode !== "create" && (
        <div
          style={{
            display: "flex",
            flexWrap: "wrap",
            gap: "1rem",
            alignItems: "center",
            marginTop: "1rem",
            paddingTop: "1rem",
            borderTop: "1px solid var(--border-light, #1c203b)",
            flexShrink: 0,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <label
              htmlFor="torrentCategorySelect"
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary, #c7c5d3)",
              }}
            >
              Category:
            </label>
            <select
              id="torrentCategorySelect"
              value={selectedCategory}
              onChange={(e) => setSelectedCategory(e.target.value)}
              className="form-input"
              style={{
                padding: "0.3rem 0.6rem",
                fontSize: "0.85rem",
                borderRadius: "4px",
                backgroundColor: "var(--bg-primary, #10111a)",
                color: "inherit",
                border: "1px solid var(--border-light, #1c203b)",
              }}
            >
              <option value="">
                {categories && categories.length > 0 ? "(None)" : "(No categories configured)"}
              </option>
              {categories?.map((c) => (
                <option key={c.id} value={c.name}>
                  {c.name}
                  {c.savePath ? ` (${c.savePath})` : ""}
                  {c.isDefault ? " [Default]" : ""}
                </option>
              ))}
            </select>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
            <input
              type="checkbox"
              id="pausedCheckbox"
              checked={isPaused}
              onChange={(e) => setIsPaused(e.target.checked)}
            />
            <label
              htmlFor="pausedCheckbox"
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary, #c7c5d3)",
                cursor: "pointer",
              }}
            >
              Start in paused state
            </label>
          </div>
        </div>
      )}

      {(addTorrent.isError || resultMessage) && (
        <div
          className="modal-error"
          style={{
            marginTop: "1rem",
            padding: "0.6rem 0.9rem",
            borderRadius: "6px",
            backgroundColor: "rgba(239, 68, 68, 0.12)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            color: "var(--danger, #ef4444)",
            fontSize: "0.85rem",
            flexShrink: 0,
          }}
        >
          {addTorrent.isError
            ? addTorrent.error instanceof Error
              ? addTorrent.error.message
              : "Failed to add torrent"
            : resultMessage}
        </div>
      )}

      {mode !== "search" && mode !== "create" && (
        <div
          className="modal-actions"
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.5rem",
            marginTop: "1.25rem",
            flexShrink: 0,
          }}
        >
          {isModal && onClose && (
            <button
              type="button"
              className="btn btn-outline"
              onClick={onClose}
              disabled={addTorrent.isPending}
              style={{ borderRadius: "6px" }}
            >
              Cancel
            </button>
          )}
          <button
            type="button"
            className="btn btn-success"
            onClick={handleSubmit}
            disabled={!canSubmit || addTorrent.isPending}
            style={{ borderRadius: "6px", padding: "0.45rem 1.25rem" }}
          >
            {addTorrent.isPending ? "Adding..." : "Add Torrent"}
          </button>
        </div>
      )}
    </div>
  );
}

export default AddTorrentForm;
