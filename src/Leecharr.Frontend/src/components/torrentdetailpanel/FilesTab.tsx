import React, { useState, useEffect, useMemo, useCallback } from "react";
import { useTorrentFiles, useSetFilePriority, useSetFilesPriority } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";
import { useToast } from "../../context/ToastContext";
import { api } from "../../api/client";
import type { Torrent, TorrentFileInfo } from "../../api/types";

export const PRIORITY_OPTIONS = [
  {
    value: 0,
    label: "Skip (Do not download)",
    color: "var(--danger, #ef4444)",
  },
  { value: 1, label: "Low", color: "var(--info, #38bdf8)" },
  { value: 3, label: "Normal", color: "var(--text-primary, #f8f4ed)" },
  { value: 4, label: "High", color: "var(--accent, #ffd166)" },
] as const;

interface TreeNode {
  id: string;
  name: string;
  fullPath: string;
  isFolder: boolean;
  file?: TorrentFileInfo;
  children: TreeNode[];
  size: number;
  bytesCompleted: number;
  progress: number;
  depth: number;
}

function getFileIcon(name: string, isFolder: boolean, isOpen: boolean): string {
  if (isFolder) {
    return isOpen ? "📂" : "📁";
  }
  const ext = name.split(".").pop()?.toLowerCase() || "";
  switch (ext) {
    case "mkv":
    case "mp4":
    case "avi":
    case "mov":
    case "wmv":
    case "m4v":
    case "webm":
      return "🎬";
    case "mp3":
    case "flac":
    case "wav":
    case "aac":
    case "ogg":
    case "m4a":
    case "opus":
      return "🎵";
    case "srt":
    case "sub":
    case "vtt":
    case "ass":
    case "idx":
      return "💬";
    case "zip":
    case "rar":
    case "7z":
    case "tar":
    case "gz":
    case "iso":
      return "📦";
    case "jpg":
    case "jpeg":
    case "png":
    case "webp":
    case "gif":
    case "bmp":
      return "🖼️";
    case "nfo":
    case "txt":
    case "log":
    case "pdf":
    case "epub":
      return "📄";
    default:
      return "📄";
  }
}

function extractFileMediaBadges(
  filename: string,
  torrent?: Torrent
): { label: string; bg: string; fg: string }[] {
  const badges: { label: string; bg: string; fg: string }[] = [];
  const lower = filename.toLowerCase();
  const ext = lower.split(".").pop() || "";

  if (["mkv", "mp4", "avi", "mov", "m4v"].includes(ext)) {
    if (lower.includes("2160p") || lower.includes("4k") || torrent?.resolution === "2160p") {
      badges.push({
        label: "4K UHD",
        bg: "rgba(37, 99, 235, 0.25)",
        fg: "#60a5fa",
      });
    } else if (lower.includes("1080p") || torrent?.resolution === "1080p") {
      badges.push({
        label: "1080p",
        bg: "rgba(37, 99, 235, 0.2)",
        fg: "#93c5fd",
      });
    } else if (lower.includes("720p")) {
      badges.push({
        label: "720p",
        bg: "rgba(37, 99, 235, 0.15)",
        fg: "#bfdbfe",
      });
    }

    if (
      lower.includes("dv") ||
      lower.includes("dovi") ||
      lower.includes("dolby") ||
      torrent?.hdrFormat?.includes("DV")
    ) {
      badges.push({
        label: "DV",
        bg: "rgba(217, 119, 6, 0.25)",
        fg: "#fcd34d",
      });
    }
    if (
      lower.includes("hdr") ||
      (torrent?.hdrFormat && torrent.hdrFormat !== "SDR" && !torrent.hdrFormat.includes("DV"))
    ) {
      badges.push({
        label: "HDR",
        bg: "rgba(217, 119, 6, 0.2)",
        fg: "#fde68a",
      });
    }

    if (
      lower.includes("hevc") ||
      lower.includes("x265") ||
      lower.includes("h.265") ||
      torrent?.videoCodec?.toLowerCase().includes("hevc")
    ) {
      badges.push({
        label: "HEVC",
        bg: "rgba(79, 70, 229, 0.25)",
        fg: "#a5b4fc",
      });
    } else if (lower.includes("avc") || lower.includes("x264") || lower.includes("h.264")) {
      badges.push({
        label: "AVC",
        bg: "rgba(79, 70, 229, 0.2)",
        fg: "#c7d2fe",
      });
    }

    if (lower.includes("atmos") || lower.includes("truehd")) {
      badges.push({
        label: "Atmos",
        bg: "rgba(5, 150, 105, 0.25)",
        fg: "#6ee7b7",
      });
    } else if (lower.includes("dts-hd") || lower.includes("dts")) {
      badges.push({
        label: "DTS-HD",
        bg: "rgba(5, 150, 105, 0.2)",
        fg: "#a7f3d0",
      });
    }
  } else if (["flac", "wav", "alac"].includes(ext)) {
    badges.push({
      label: "Lossless",
      bg: "rgba(16, 185, 129, 0.25)",
      fg: "#34d399",
    });
  } else if (["srt", "sub", "vtt", "ass", "idx"].includes(ext)) {
    const parts = lower.split(".");
    if (parts.length >= 3) {
      const lang = parts[parts.length - 2];
      if (lang && lang.length <= 4) {
        badges.push({
          label: `Sub (${lang.toUpperCase()})`,
          bg: "rgba(168, 85, 247, 0.2)",
          fg: "#d8b4fe",
        });
      }
    } else {
      badges.push({
        label: "Sub",
        bg: "rgba(168, 85, 247, 0.2)",
        fg: "#d8b4fe",
      });
    }
  }

  return badges;
}

function normalizePriority(val?: number): number {
  if (val === undefined || val === null) return 3; // Default Normal
  if (val === 0) return 0;
  if (val <= 2) return 1;
  if (val === 3) return 3;
  return 4;
}

function buildTree(files: TorrentFileInfo[]): TreeNode[] {
  interface IntermediateNode {
    name: string;
    fullPath: string;
    isFolder: boolean;
    file?: TorrentFileInfo;
    children: Map<string, IntermediateNode>;
    size: number;
    bytesCompleted: number;
  }

  const rootMap = new Map<string, IntermediateNode>();

  for (const f of files) {
    const rawPath = (f.path || "unnamed").replace(/\\/g, "/");
    const parts = rawPath.split("/").filter(Boolean);

    let currentMap = rootMap;
    let accumulatedPath = "";

    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      accumulatedPath = accumulatedPath ? `${accumulatedPath}/${part}` : part;
      const isLast = i === parts.length - 1;

      if (isLast) {
        currentMap.set(part, {
          name: part,
          fullPath: accumulatedPath,
          isFolder: false,
          file: f,
          children: new Map(),
          size: f.size,
          bytesCompleted: f.bytesCompleted ?? f.size * (f.progress ?? 0),
        });
      } else {
        if (!currentMap.has(part)) {
          currentMap.set(part, {
            name: part,
            fullPath: accumulatedPath,
            isFolder: true,
            children: new Map(),
            size: 0,
            bytesCompleted: 0,
          });
        }
        currentMap = currentMap.get(part)!.children;
      }
    }
  }

  function convert(node: IntermediateNode, depth: number): TreeNode {
    const childrenArr: TreeNode[] = [];
    let totalSize = node.isFolder ? 0 : node.size;
    let totalCompleted = node.isFolder ? 0 : node.bytesCompleted;

    for (const child of node.children.values()) {
      const convertedChild = convert(child, depth + 1);
      childrenArr.push(convertedChild);
      if (node.isFolder) {
        totalSize += convertedChild.size;
        totalCompleted += convertedChild.bytesCompleted;
      }
    }

    // Sort: Folders first, then alphabetically
    childrenArr.sort((a, b) => {
      if (a.isFolder && !b.isFolder) return -1;
      if (!a.isFolder && b.isFolder) return 1;
      return a.name.localeCompare(b.name, undefined, {
        numeric: true,
        sensitivity: "base",
      });
    });

    const progress =
      totalSize > 0
        ? Math.min(1, Math.max(0, totalCompleted / totalSize))
        : (node.file?.progress ?? 0);

    return {
      id: node.fullPath,
      name: node.name,
      fullPath: node.fullPath,
      isFolder: node.isFolder,
      file: node.file,
      children: childrenArr,
      size: totalSize,
      bytesCompleted: totalCompleted,
      progress,
      depth,
    };
  }

  const result: TreeNode[] = [];
  for (const node of rootMap.values()) {
    result.push(convert(node, 0));
  }

  result.sort((a, b) => {
    if (a.isFolder && !b.isFolder) return -1;
    if (!a.isFolder && b.isFolder) return 1;
    return a.name.localeCompare(b.name, undefined, {
      numeric: true,
      sensitivity: "base",
    });
  });

  return result;
}

function getDescendantFiles(node: TreeNode): TorrentFileInfo[] {
  if (!node.isFolder && node.file) {
    return [node.file];
  }
  const files: TorrentFileInfo[] = [];
  for (const child of node.children) {
    files.push(...getDescendantFiles(child));
  }
  return files;
}

export function FilesTab({ torrent, torrentId }: { torrent?: Torrent; torrentId?: number }) {
  const effectiveId = torrentId ?? torrent?.id ?? 0;
  const { showToast } = useToast();
  const { data: files, isLoading, isError, refetch: refetchFiles } = useTorrentFiles(effectiveId);
  const setFilePriority = useSetFilePriority();
  const setFilesPriority = useSetFilesPriority();

  const [renamingNode, setRenamingNode] = useState<{
    fullPath: string;
    isFolder: boolean;
    name: string;
  } | null>(null);
  const [renameInput, setRenameInput] = useState("");
  const [isRenaming, setIsRenaming] = useState(false);

  const [expandedPaths, setExpandedPaths] = useState<Set<string>>(() => new Set());
  const [filterQuery, setFilterQuery] = useState("");
  const [initializedTree, setInitializedTree] = useState(false);

  useEffect(() => {
    setInitializedTree(false);
    setExpandedPaths(new Set());
    setFilterQuery("");
    setRenamingNode(null);
    setRenameInput("");
    setIsRenaming(false);
  }, [effectiveId]);

  const handleStartRename = (node: TreeNode, e: React.MouseEvent) => {
    e.stopPropagation();
    setRenamingNode({ fullPath: node.fullPath, isFolder: node.isFolder, name: node.name });
    setRenameInput(node.name);
  };

  const handleConfirmRename = async () => {
    if (!renamingNode || !renameInput.trim() || renameInput === renamingNode.name) {
      setRenamingNode(null);
      return;
    }

    const infoHash = torrent?.infoHash;
    if (!infoHash) {
      showToast("Cannot rename: Missing torrent info hash", "error");
      return;
    }

    try {
      setIsRenaming(true);
      const segments = renamingNode.fullPath.split("/");
      segments[segments.length - 1] = renameInput.trim();
      const newFullPath = segments.join("/");

      if (renamingNode.isFolder) {
        await api.renameTorrentFolder(infoHash, renamingNode.fullPath, newFullPath);
      } else {
        await api.renameTorrentFile(infoHash, renamingNode.fullPath, newFullPath);
      }

      showToast(`Successfully renamed to '${renameInput.trim()}'`, "success");
      setRenamingNode(null);
      await refetchFiles();
    } catch (err: any) {
      showToast(err?.message || "Failed to rename item", "error");
    } finally {
      setIsRenaming(false);
    }
  };

  const tree = useMemo(() => {
    if (!files || files.length === 0) return [];
    return buildTree(files);
  }, [files]);

  // Expand top-level folders initially
  React.useEffect(() => {
    if (tree.length > 0 && !initializedTree) {
      const initialExpanded = new Set<string>();
      const addFolders = (nodes: TreeNode[]) => {
        for (const n of nodes) {
          if (n.isFolder) {
            initialExpanded.add(n.fullPath);
            if (n.depth < 2) {
              addFolders(n.children);
            }
          }
        }
      };
      addFolders(tree);
      setExpandedPaths(initialExpanded);
      setInitializedTree(true);
    }
  }, [tree, initializedTree]);

  const toggleFolder = useCallback((path: string) => {
    setExpandedPaths((prev) => {
      const next = new Set(prev);
      if (next.has(path)) {
        next.delete(path);
      } else {
        next.add(path);
      }
      return next;
    });
  }, []);

  const expandAll = useCallback(() => {
    const all = new Set<string>();
    const collect = (nodes: TreeNode[]) => {
      for (const n of nodes) {
        if (n.isFolder) {
          all.add(n.fullPath);
          collect(n.children);
        }
      }
    };
    collect(tree);
    setExpandedPaths(all);
  }, [tree]);

  const collapseAll = useCallback(() => {
    setExpandedPaths(new Set());
  }, []);

  const handleSetPriority = useCallback(
    (fileId: number, priority: number) => {
      if (effectiveId <= 0) return;
      setFilePriority.mutate({ torrentId: effectiveId, fileId, priority });
    },
    [effectiveId, setFilePriority]
  );

  const handleBatchSetPriority = useCallback(
    (targetFiles: TorrentFileInfo[], priority: number) => {
      if (effectiveId <= 0 || targetFiles.length === 0) return;
      setFilesPriority.mutate({
        torrentId: effectiveId,
        files: targetFiles.map((f) => ({ fileId: f.id, priority })),
      });
    },
    [effectiveId, setFilesPriority]
  );

  const handleToggleNodeCheckbox = useCallback(
    (node: TreeNode) => {
      const targetFiles = getDescendantFiles(node);
      if (targetFiles.length === 0) return;

      const anyActive = targetFiles.some((f) => normalizePriority(f.priority) !== 0);
      const newPriority = anyActive ? 0 : 3; // Toggle between Skip (0) and Normal (3)
      handleBatchSetPriority(targetFiles, newPriority);
    },
    [handleBatchSetPriority]
  );

  if (isLoading) return <PanelLoading>Loading files...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load files.</PanelEmpty>;
  if (!files || files.length === 0) return <PanelEmpty>No files</PanelEmpty>;

  const totalFilesCount = files.length;
  const totalBytes = files.reduce((acc, f) => acc + (f.size || 0), 0);
  const totalCompletedBytes = files.reduce(
    (acc, f) => acc + (f.bytesCompleted ?? f.size * (f.progress ?? 0)),
    0
  );
  const overallProgress = totalBytes > 0 ? (totalCompletedBytes / totalBytes) * 100 : 0;

  // Flatten visible tree rows based on expanded state and filter query
  const flatRows: TreeNode[] = [];
  const q = filterQuery.trim().toLowerCase();

  function flatten(nodes: TreeNode[]) {
    for (const node of nodes) {
      const matchesFilter =
        !q ||
        node.name.toLowerCase().includes(q) ||
        node.fullPath.toLowerCase().includes(q) ||
        (node.isFolder && getDescendantFiles(node).some((f) => f.path.toLowerCase().includes(q)));

      if (!matchesFilter) continue;

      flatRows.push(node);

      if (node.isFolder && (expandedPaths.has(node.fullPath) || q.length > 0)) {
        flatten(node.children);
      }
    }
  }

  flatten(tree);

  return (
    <div
      className="files-tab-container"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        gap: "0.5rem",
      }}
    >
      {/* Media Container Inspector Specifications Banner */}
      {(torrent?.resolution ||
        torrent?.videoCodec ||
        torrent?.audioCodec ||
        torrent?.hdrFormat) && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "0.5rem",
            padding: "0.4rem 0.75rem",
            backgroundColor: "rgba(23, 27, 53, 0.7)",
            borderRadius: "6px",
            border: "1px solid rgba(255, 209, 102, 0.25)",
            fontSize: "0.78rem",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              flexWrap: "wrap",
            }}
          >
            <span
              style={{
                fontWeight: 700,
                color: "var(--accent-gold, #FFD166)",
                fontSize: "0.75rem",
              }}
            >
              🎬 Pure C# Media Inspector:
            </span>
            {torrent.resolution && (
              <span
                className="badge"
                style={{
                  backgroundColor: "#2563eb",
                  color: "#fff",
                  fontSize: "0.68rem",
                  fontWeight: 700,
                }}
              >
                {torrent.resolution}
              </span>
            )}
            {torrent.videoCodec && (
              <span
                className="badge"
                style={{
                  backgroundColor: "#4f46e5",
                  color: "#fff",
                  fontSize: "0.68rem",
                  fontWeight: 700,
                }}
              >
                {torrent.videoCodec}
              </span>
            )}
            {torrent.hdrFormat && torrent.hdrFormat !== "SDR" && (
              <span
                className="badge"
                style={{
                  backgroundColor: "#d97706",
                  color: "#fff",
                  fontSize: "0.68rem",
                  fontWeight: 700,
                }}
              >
                {torrent.hdrFormat}
              </span>
            )}
            {torrent.audioCodec && (
              <span
                className="badge"
                style={{
                  backgroundColor: "#059669",
                  color: "#fff",
                  fontSize: "0.68rem",
                  fontWeight: 700,
                }}
              >
                {torrent.audioCodec} {torrent.audioChannels ? `(${torrent.audioChannels})` : ""}
              </span>
            )}
          </div>
          <span style={{ fontSize: "0.7rem", color: "var(--text-muted, #8a879e)" }}>
            TagLib# & Pure EBML Stream Parser
          </span>
        </div>
      )}

      {/* File Controls Toolbar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          padding: "0.4rem 0.6rem",
          backgroundColor: "var(--bg-secondary, #171B35)",
          borderRadius: "6px",
          border: "1px solid var(--border, #23284B)",
          flexWrap: "wrap",
          gap: "0.5rem",
          fontSize: "0.8rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <span style={{ color: "var(--text-secondary, #C7C5D3)" }}>
            <strong style={{ color: "var(--text-primary, #F8F4ED)" }}>{totalFilesCount}</strong>{" "}
            files ({formatBytes(totalBytes)} total)
          </span>
          <span
            style={{
              padding: "0.1rem 0.4rem",
              borderRadius: "4px",
              backgroundColor: "rgba(255, 209, 102, 0.12)",
              color: "var(--accent-gold, #FFD166)",
              fontWeight: 600,
              fontSize: "0.75rem",
            }}
          >
            {overallProgress.toFixed(1)}% verified
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <input
            type="text"
            placeholder="Filter files..."
            value={filterQuery}
            onChange={(e) => setFilterQuery(e.target.value)}
            style={{
              backgroundColor: "var(--bg-primary, #10111A)",
              border: "1px solid var(--border, #23284B)",
              color: "var(--text-primary, #F8F4ED)",
              borderRadius: "4px",
              padding: "0.2rem 0.5rem",
              fontSize: "0.75rem",
              outline: "none",
              width: "140px",
            }}
          />
          <button
            type="button"
            className="btn btn-small btn-outline"
            onClick={expandAll}
            style={{ fontSize: "0.72rem", padding: "0.2rem 0.5rem" }}
          >
            Expand All
          </button>
          <button
            type="button"
            className="btn btn-small btn-outline"
            onClick={collapseAll}
            style={{ fontSize: "0.72rem", padding: "0.2rem 0.5rem" }}
          >
            Collapse All
          </button>
        </div>
      </div>

      {/* Hierarchical File Tree Table */}
      <div className="detail-panel-table-wrap" style={{ flex: 1, overflow: "auto", minHeight: 0 }}>
        <table className="torrent-table" style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr
              style={{
                position: "sticky",
                top: 0,
                backgroundColor: "var(--bg-primary, #10111A)",
                zIndex: 2,
                borderBottom: "1px solid var(--border, #23284B)",
              }}
            >
              <th className="torrent-table-th" style={{ width: 36, textAlign: "center" }}>
                <input
                  type="checkbox"
                  checked={files.every((f) => normalizePriority(f.priority) !== 0)}
                  ref={(input) => {
                    if (input) {
                      const someActive = files.some((f) => normalizePriority(f.priority) !== 0);
                      const allActive = files.every((f) => normalizePriority(f.priority) !== 0);
                      input.indeterminate = someActive && !allActive;
                    }
                  }}
                  onChange={() => {
                    const allActive = files.every((f) => normalizePriority(f.priority) !== 0);
                    handleBatchSetPriority(files, allActive ? 0 : 3);
                  }}
                  title="Toggle all files selective download"
                />
              </th>
              <th className="torrent-table-th" style={{ textAlign: "left" }}>
                File / Folder Name
              </th>
              <th className="torrent-table-th" style={{ width: 90, textAlign: "right" }}>
                Size
              </th>
              <th className="torrent-table-th" style={{ width: 140, textAlign: "left" }}>
                Progress
              </th>
              <th className="torrent-table-th" style={{ width: 130, textAlign: "center" }}>
                Priority
              </th>
            </tr>
          </thead>
          <tbody>
            {flatRows.map((node) => {
              const descendantFiles = getDescendantFiles(node);
              const isFolder = node.isFolder;
              const isExpanded = expandedPaths.has(node.fullPath);

              // Checkbox state
              const allChecked = descendantFiles.every((f) => normalizePriority(f.priority) !== 0);
              const someChecked = descendantFiles.some((f) => normalizePriority(f.priority) !== 0);
              const isIndeterminate = someChecked && !allChecked;

              // Priority for node
              const currentPriority = isFolder
                ? descendantFiles.every(
                    (f) =>
                      normalizePriority(f.priority) ===
                      normalizePriority(descendantFiles[0]?.priority)
                  )
                  ? normalizePriority(descendantFiles[0]?.priority)
                  : -1
                : normalizePriority(node.file?.priority);

              const pct = Math.floor(node.progress * 100);

              return (
                <tr
                  key={node.id}
                  className="torrent-table-row"
                  style={{
                    backgroundColor: isFolder ? "rgba(23, 27, 53, 0.4)" : "transparent",
                    borderBottom: "1px solid rgba(35, 40, 75, 0.5)",
                    fontSize: "0.8rem",
                  }}
                >
                  {/* Selective Download Checkbox */}
                  <td style={{ textAlign: "center", padding: "0.4rem 0.2rem" }}>
                    <input
                      type="checkbox"
                      checked={allChecked}
                      ref={(input) => {
                        if (input) input.indeterminate = isIndeterminate;
                      }}
                      onChange={() => handleToggleNodeCheckbox(node)}
                      title={
                        isFolder
                          ? `Toggle ${descendantFiles.length} files in folder`
                          : "Toggle selective download"
                      }
                    />
                  </td>

                  {/* Name with indentation and folder expander */}
                  <td style={{ padding: "0.4rem 0.5rem" }}>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "6px",
                        paddingLeft: `${node.depth * 18}px`,
                      }}
                    >
                      {isFolder ? (
                        <button
                          type="button"
                          onClick={() => toggleFolder(node.fullPath)}
                          style={{
                            background: "none",
                            border: "none",
                            color: "var(--text-secondary, #C7C5D3)",
                            cursor: "pointer",
                            padding: "0 2px",
                            fontSize: "0.75rem",
                            display: "inline-flex",
                            alignItems: "center",
                          }}
                        >
                          {isExpanded ? "▼" : "▶"}
                        </button>
                      ) : (
                        <span style={{ width: "12px", display: "inline-block" }} />
                      )}

                      <span style={{ fontSize: "0.95rem" }}>
                        {getFileIcon(node.name, isFolder, isExpanded)}
                      </span>

                      <span
                        style={{
                          fontWeight: isFolder ? 600 : 400,
                          color:
                            currentPriority === 0
                              ? "var(--text-muted, #8a879e)"
                              : isFolder
                                ? "var(--text-primary, #F8F4ED)"
                                : "var(--text-secondary, #C7C5D3)",
                          textDecoration: currentPriority === 0 ? "line-through" : "none",
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                        title={node.fullPath}
                      >
                        {node.name}
                        {isFolder && (
                          <span
                            style={{
                              fontSize: "0.72rem",
                              color: "var(--text-muted, #8a879e)",
                              marginLeft: "6px",
                              fontWeight: 400,
                            }}
                          >
                            ({descendantFiles.length} items)
                          </span>
                        )}
                      </span>

                      <button
                        type="button"
                        onClick={(e) => handleStartRename(node, e)}
                        title={node.isFolder ? "Rename Folder" : "Rename File"}
                        style={{
                          background: "none",
                          border: "none",
                          cursor: "pointer",
                          padding: "1px 4px",
                          fontSize: "0.75rem",
                          color: "var(--text-muted, #8a879e)",
                          borderRadius: "3px",
                          marginLeft: "6px",
                          opacity: 0.6,
                        }}
                        onMouseEnter={(e) => (e.currentTarget.style.opacity = "1")}
                        onMouseLeave={(e) => (e.currentTarget.style.opacity = "0.6")}
                      >
                        ✏️
                      </button>

                      {!isFolder && (
                        <div
                          style={{
                            display: "inline-flex",
                            gap: "4px",
                            alignItems: "center",
                            marginLeft: "6px",
                            flexShrink: 0,
                          }}
                        >
                          {extractFileMediaBadges(node.name, torrent).map((b, idx) => (
                            <span
                              key={idx}
                              style={{
                                fontSize: "0.62rem",
                                fontWeight: 700,
                                padding: "0.05rem 0.3rem",
                                borderRadius: "3px",
                                backgroundColor: b.bg,
                                color: b.fg,
                                border: `1px solid ${b.fg}40`,
                                textTransform: "uppercase",
                              }}
                            >
                              {b.label}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                  </td>

                  {/* Size */}
                  <td
                    style={{
                      textAlign: "right",
                      padding: "0.4rem 0.75rem",
                      color: "var(--text-secondary, #C7C5D3)",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {formatBytes(node.size)}
                  </td>

                  {/* Progress Bar & Pct */}
                  <td style={{ padding: "0.4rem 0.75rem" }}>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "6px",
                      }}
                    >
                      <div
                        style={{
                          flex: 1,
                          height: 5,
                          backgroundColor: "rgba(255, 255, 255, 0.08)",
                          borderRadius: 3,
                          overflow: "hidden",
                        }}
                      >
                        <div
                          style={{
                            width: `${pct}%`,
                            height: "100%",
                            backgroundColor:
                              pct >= 100
                                ? "var(--success, #10b981)"
                                : "var(--accent-gold, #FFD166)",
                            transition: "width 0.2s",
                          }}
                        />
                      </div>
                      <span
                        style={{
                          fontSize: "0.72rem",
                          width: "35px",
                          textAlign: "right",
                          color: "var(--text-secondary, #C7C5D3)",
                        }}
                      >
                        {pct}%
                      </span>
                    </div>
                  </td>

                  {/* Priority Select */}
                  <td style={{ textAlign: "center", padding: "0.3rem 0.5rem" }}>
                    <select
                      value={currentPriority === -1 ? "" : currentPriority}
                      onChange={(e) => {
                        const val = parseInt(e.target.value, 10);
                        if (isNaN(val)) return;
                        if (isFolder) {
                          handleBatchSetPriority(descendantFiles, val);
                        } else if (node.file) {
                          handleSetPriority(node.file.id, val);
                        }
                      }}
                      style={{
                        backgroundColor: "var(--bg-secondary, #171B35)",
                        border: "1px solid var(--border, #23284B)",
                        color:
                          currentPriority === 0
                            ? "var(--danger, #ef4444)"
                            : currentPriority === 4
                              ? "var(--accent-gold, #FFD166)"
                              : "var(--text-primary, #F8F4ED)",
                        borderRadius: "4px",
                        padding: "0.15rem 0.35rem",
                        fontSize: "0.72rem",
                        fontWeight: 500,
                        cursor: "pointer",
                        outline: "none",
                      }}
                    >
                      {currentPriority === -1 && (
                        <option value="" disabled>
                          Mixed
                        </option>
                      )}
                      {PRIORITY_OPTIONS.map((opt) => (
                        <option key={opt.value} value={opt.value}>
                          {opt.label}
                        </option>
                      ))}
                    </select>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {renamingNode && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "rgba(0, 0, 0, 0.7)",
            zIndex: 9999,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            backdropFilter: "blur(4px)",
          }}
          onClick={() => !isRenaming && setRenamingNode(null)}
        >
          <div
            style={{
              backgroundColor: "var(--bg-card, #171b35)",
              border: "1px solid var(--border, #23284b)",
              borderRadius: "8px",
              padding: "1.5rem",
              width: "100%",
              maxWidth: "480px",
              boxShadow: "0 10px 25px rgba(0, 0, 0, 0.5)",
            }}
            onClick={(e) => e.stopPropagation()}
          >
            <h3 style={{ margin: "0 0 1rem", fontSize: "1.1rem", color: "var(--text-primary)" }}>
              Rename {renamingNode.isFolder ? "Folder" : "File"}
            </h3>
            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary)",
                marginBottom: "0.75rem",
              }}
            >
              Original Path: <code style={{ wordBreak: "break-all" }}>{renamingNode.fullPath}</code>
            </p>
            <input
              type="text"
              value={renameInput}
              onChange={(e) => setRenameInput(e.target.value)}
              className="input-text"
              style={{
                width: "100%",
                padding: "0.5rem 0.75rem",
                fontSize: "0.9rem",
                borderRadius: "6px",
                border: "1px solid var(--border)",
                backgroundColor: "var(--bg-primary)",
                color: "var(--text-primary)",
                marginBottom: "1.25rem",
              }}
              autoFocus
              onKeyDown={(e) => {
                if (e.key === "Enter") handleConfirmRename();
                if (e.key === "Escape") setRenamingNode(null);
              }}
            />
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.75rem" }}>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => setRenamingNode(null)}
                disabled={isRenaming}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleConfirmRename}
                disabled={isRenaming || !renameInput.trim() || renameInput === renamingNode.name}
              >
                {isRenaming ? "Renaming..." : "Save Name"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default FilesTab;
