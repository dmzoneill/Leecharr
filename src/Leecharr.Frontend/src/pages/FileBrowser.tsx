import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router";
import {
  useFileListing,
  useCreateDirectory,
  useRenameFileEntry,
  useDeleteFileEntry,
} from "../api/hooks";
import { formatBytes, formatDate } from "../utils/formatters";
import { useConfirm } from "../context/ConfirmContext";
import { useToast } from "../context/ToastContext";
import { PromptModal } from "../components/PromptModal";

function getPathSegments(path: string): { label: string; fullPath: string }[] {
  const segments = path.split("/").filter(Boolean);
  const result: { label: string; fullPath: string }[] = [];

  for (let i = 0; i < segments.length; i++) {
    result.push({
      label: segments[i],
      fullPath: "/" + segments.slice(0, i + 1).join("/"),
    });
  }

  return result;
}

export function FileBrowser() {
  const navigate = useNavigate();
  const confirm = useConfirm();
  const { showToast } = useToast();

  const queryPath =
    typeof window !== "undefined"
      ? new URLSearchParams(window.location.search).get("path") || undefined
      : undefined;

  const [currentPath, setCurrentPath] = useState<string>(queryPath || "");
  const [isNewFolderOpen, setIsNewFolderOpen] = useState(false);
  const [renameTarget, setRenameTarget] = useState<{
    path: string;
    name: string;
  } | null>(null);
  const {
    data: listing,
    isLoading,
    isError,
    refetch,
  } = useFileListing(currentPath || undefined);
  const mkdirMutation = useCreateDirectory();
  const renameMutation = useRenameFileEntry();
  const deleteMutation = useDeleteFileEntry();

  useEffect(() => {
    if (listing?.path && !currentPath) {
      setCurrentPath(listing.path);
    }
  }, [listing, currentPath]);

  const navigateTo = useCallback((path: string) => {
    setCurrentPath(path);
    const url = new URL(window.location.href);
    url.searchParams.set("path", path);
    window.history.replaceState({}, "", url.toString());
  }, []);

  const handleNavigateUp = () => {
    if (listing?.parent && listing.parent !== listing.path) {
      navigateTo(listing.parent);
    }
  };

  const handleEntryClick = (entry: { isDirectory: boolean; path: string }) => {
    if (entry.isDirectory) {
      navigateTo(entry.path);
    }
  };

  const handleCopyPath = () => {
    navigator.clipboard.writeText(currentPath || listing?.path || "");
    showToast("Path copied to clipboard", "info");
  };

  const handleNewFolder = () => {
    setIsNewFolderOpen(true);
  };

  const handleConfirmNewFolder = async (name: string) => {
    setIsNewFolderOpen(false);
    const trimmed = (name || "").trim();
    if (!trimmed) return;

    try {
      const targetPath = currentPath
        ? `${currentPath}/${trimmed}`
        : `${listing?.defaultPath || "/"}/${trimmed}`;
      await mkdirMutation.mutateAsync(targetPath);
      showToast(`Created folder "${trimmed}"`, "success");
    } catch (err: any) {
      showToast(err?.message || "Failed to create folder", "error");
    }
  };

  const handleRename = (entryPath: string, currentName: string) => {
    setRenameTarget({ path: entryPath, name: currentName });
  };

  const handleConfirmRename = async (newName: string) => {
    if (!renameTarget) return;
    const { path: entryPath, name: currentName } = renameTarget;
    setRenameTarget(null);

    const trimmed = (newName || "").trim();
    if (!trimmed || trimmed === currentName) return;

    try {
      await renameMutation.mutateAsync({ path: entryPath, newName: trimmed });
      showToast(`Renamed to "${trimmed}"`, "success");
    } catch (err: any) {
      showToast(err?.message || "Failed to rename", "error");
    }
  };

  const handleDelete = async (
    entryPath: string,
    entryName: string,
    isDir: boolean,
  ) => {
    const ok = await confirm({
      title: isDir ? "Delete Folder" : "Delete File",
      message: (
        <span>
          Delete <strong>{entryName}</strong>?{" "}
          {isDir && (
            <span style={{ color: "var(--danger, #ef4444)" }}>
              This will delete all contents recursively.
            </span>
          )}
        </span>
      ),
      danger: true,
      confirmText: "Delete",
    });

    if (!ok) return;

    try {
      await deleteMutation.mutateAsync(entryPath);
      showToast(`Deleted "${entryName}"`, "info");
    } catch (err: any) {
      showToast(err?.message || "Failed to delete", "error");
    }
  };

  const handleOpenInCli = () => {
    navigate(
      `/terminal?path=${encodeURIComponent(currentPath || listing?.path || "/downloads")}`,
    );
  };

  const segments = getPathSegments(currentPath || listing?.path || "/");

  if (isLoading && !listing) {
    return (
      <div
        className="card"
        style={{
          padding: "1.5rem",
          textAlign: "center",
          color: "var(--text-muted)",
        }}
      >
        Loading file browser...
      </div>
    );
  }

  if (isError) {
    return (
      <div
        className="card"
        style={{
          padding: "1.5rem",
          textAlign: "center",
          color: "var(--danger, #ef4444)",
        }}
      >
        Failed to load directory listing.
      </div>
    );
  }

  const entries = listing?.entries || [];
  const displayPath =
    currentPath || listing?.path || listing?.defaultPath || "/";

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        gap: "0.85rem",
      }}
    >
      {/* Header Card */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.85rem",
          padding: "0.85rem 1.25rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <span style={{ fontSize: "1.4rem" }}>📂</span>
          <div>
            <h2 style={{ margin: 0, fontSize: "1.15rem", fontWeight: 600 }}>
              File Browser
            </h2>
            <p
              style={{
                margin: 0,
                fontSize: "0.8rem",
                color: "var(--text-muted)",
              }}
            >
              Browse, create, rename, and delete files and folders on the
              server.
            </p>
          </div>
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            flexWrap: "wrap",
          }}
        >
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
            onClick={handleNavigateUp}
            disabled={!listing?.parent || listing.parent === listing.path}
            title="Go to parent directory"
          >
            ⬆ Up
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
            onClick={() => refetch()}
            title="Refresh"
          >
            🔄 Refresh
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
            onClick={handleCopyPath}
            title="Copy current path to clipboard"
          >
            📋 Copy Path
          </button>
          <button
            type="button"
            className="btn btn-primary"
            style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
            onClick={handleNewFolder}
            disabled={mkdirMutation.isPending}
            title="Create new folder in current directory"
          >
            + New Folder
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{
              fontSize: "0.8rem",
              padding: "0.3rem 0.65rem",
              fontFamily: "monospace",
            }}
            onClick={handleOpenInCli}
            title="Open an interactive terminal shell in this directory"
          >
            &gt;_ CLI
          </button>
        </div>
      </div>

      {/* PWD Breadcrumb Bar */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "0.35rem",
          padding: "0.55rem 0.85rem",
          backgroundColor: "var(--bg-card, #131627)",
          borderRadius: "8px",
          border: "1px solid var(--border-light, #1c203b)",
          fontSize: "0.85rem",
          overflowX: "auto",
          whiteSpace: "nowrap",
          flexWrap: "nowrap",
        }}
      >
        <span
          style={{
            color: "var(--text-muted, #8a879e)",
            flexShrink: 0,
            fontWeight: 600,
          }}
        >
          📁 PWD:
        </span>
        <button
          type="button"
          onClick={() => navigateTo("/")}
          style={{
            background: "none",
            border: "none",
            color: "var(--accent, #ffd166)",
            cursor: "pointer",
            fontWeight: segments.length === 0 ? 700 : 400,
            fontSize: "0.85rem",
            padding: "0 0.2rem",
            fontFamily: "monospace",
          }}
          title="Root"
        >
          /
        </button>
        {segments.map((seg, idx) => (
          <React.Fragment key={seg.fullPath}>
            <span
              style={{ color: "var(--text-muted, #8a879e)", flexShrink: 0 }}
            >
              /
            </span>
            <button
              type="button"
              onClick={() => navigateTo(seg.fullPath)}
              style={{
                background: "none",
                border: "none",
                color:
                  idx === segments.length - 1
                    ? "var(--text-primary, #f8f4ed)"
                    : "var(--accent, #ffd166)",
                cursor: "pointer",
                fontWeight: idx === segments.length - 1 ? 700 : 400,
                fontSize: "0.85rem",
                padding: "0 0.2rem",
                fontFamily: "monospace",
                whiteSpace: "nowrap",
              }}
              title={seg.fullPath}
            >
              {seg.label}
            </button>
          </React.Fragment>
        ))}
      </div>

      {/* Error banner for missing path */}
      {listing && !listing.exists && (
        <div
          style={{
            padding: "0.75rem 1rem",
            backgroundColor: "rgba(239, 68, 68, 0.1)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            borderRadius: "6px",
            fontSize: "0.85rem",
            color: "var(--danger, #ef4444)",
          }}
        >
          Directory does not exist:{" "}
          <code style={{ wordBreak: "break-all" }}>{displayPath}</code>
        </div>
      )}

      {/* File Table */}
      <div
        className="card"
        style={{
          flex: 1,
          minHeight: 0,
          overflow: "auto",
          padding: 0,
        }}
      >
        <table
          className="torrent-table"
          style={{ width: "100%", borderCollapse: "collapse" }}
        >
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
              <th
                className="torrent-table-th"
                style={{ textAlign: "left", padding: "0.6rem 0.85rem" }}
              >
                Name
              </th>
              <th
                className="torrent-table-th"
                style={{
                  width: 100,
                  textAlign: "right",
                  padding: "0.6rem 0.85rem",
                }}
              >
                Size
              </th>
              <th
                className="torrent-table-th"
                style={{
                  width: 150,
                  textAlign: "left",
                  padding: "0.6rem 0.85rem",
                }}
              >
                Modified
              </th>
              <th
                className="torrent-table-th"
                style={{
                  width: 130,
                  textAlign: "right",
                  padding: "0.6rem 0.85rem",
                }}
              >
                Actions
              </th>
            </tr>
          </thead>
          <tbody>
            {entries.length === 0 && (
              <tr>
                <td
                  colSpan={4}
                  style={{
                    padding: "2rem",
                    textAlign: "center",
                    color: "var(--text-muted, #8a879e)",
                    fontSize: "0.9rem",
                  }}
                >
                  {listing?.exists ? "Empty directory" : "Directory not found"}
                </td>
              </tr>
            )}
            {entries.map((entry) => (
              <tr
                key={entry.path}
                className="torrent-table-row"
                style={{
                  backgroundColor: entry.isDirectory
                    ? "rgba(23, 27, 53, 0.4)"
                    : "transparent",
                  borderBottom: "1px solid rgba(35, 40, 75, 0.5)",
                  fontSize: "0.85rem",
                  cursor: entry.isDirectory ? "pointer" : "default",
                }}
                onClick={() => handleEntryClick(entry)}
              >
                <td style={{ padding: "0.5rem 0.85rem" }}>
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.6rem",
                    }}
                  >
                    <span style={{ fontSize: "1rem", flexShrink: 0 }}>
                      {entry.isDirectory ? "📁" : getFileIcon(entry.extension)}
                    </span>
                    <span
                      style={{
                        fontWeight: entry.isDirectory ? 600 : 400,
                        color: entry.isDirectory
                          ? "var(--text-primary, #F8F4ED)"
                          : "var(--text-secondary, #C7C5D3)",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                      title={entry.path}
                    >
                      {entry.name}
                    </span>
                  </div>
                </td>

                <td
                  style={{
                    textAlign: "right",
                    padding: "0.5rem 0.85rem",
                    color: "var(--text-secondary, #C7C5D3)",
                    whiteSpace: "nowrap",
                  }}
                >
                  {entry.isDirectory ? "\u2014" : formatBytes(entry.size)}
                </td>

                <td
                  style={{
                    padding: "0.5rem 0.85rem",
                    color: "var(--text-muted, #8a879e)",
                    fontSize: "0.8rem",
                    whiteSpace: "nowrap",
                  }}
                >
                  {entry.modified ? formatDate(entry.modified) : "\u2014"}
                </td>

                <td
                  style={{
                    textAlign: "right",
                    padding: "0.5rem 0.85rem",
                  }}
                >
                  <div
                    style={{
                      display: "inline-flex",
                      gap: "0.4rem",
                    }}
                  >
                    <button
                      type="button"
                      className="btn btn-small btn-outline"
                      style={{
                        fontSize: "0.72rem",
                        padding: "0.2rem 0.45rem",
                      }}
                      onClick={(e) => {
                        e.stopPropagation();
                        handleRename(entry.path, entry.name);
                      }}
                      title="Rename"
                    >
                      ✏️ Rename
                    </button>
                    <button
                      type="button"
                      className="btn btn-small btn-outline"
                      style={{
                        fontSize: "0.72rem",
                        padding: "0.2rem 0.45rem",
                        color: "var(--danger, #ef4444)",
                        borderColor: "rgba(239, 68, 68, 0.35)",
                      }}
                      onClick={(e) => {
                        e.stopPropagation();
                        handleDelete(entry.path, entry.name, entry.isDirectory);
                      }}
                      disabled={deleteMutation.isPending}
                      title="Delete"
                    >
                      🗑 Delete
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <PromptModal
        isOpen={isNewFolderOpen}
        title="New Folder"
        message={`Creating in: ${currentPath || listing?.path || "/"}`}
        placeholder="Folder name"
        confirmText="Create"
        cancelText="Cancel"
        onConfirm={handleConfirmNewFolder}
        onCancel={() => setIsNewFolderOpen(false)}
      />

      <PromptModal
        isOpen={!!renameTarget}
        title="Rename"
        message={renameTarget ? `Rename: ${renameTarget.path}` : ""}
        defaultValue={renameTarget?.name || ""}
        placeholder="New name"
        confirmText="Rename"
        cancelText="Cancel"
        onConfirm={handleConfirmRename}
        onCancel={() => setRenameTarget(null)}
      />
    </div>
  );
}

function getFileIcon(extension: string | null): string {
  const ext = (extension || "").toLowerCase();
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
    case "torrent":
      return "🌊";
    case "torrent":
      return "🌊";
    default:
      return "📄";
  }
}

export default FileBrowser;
