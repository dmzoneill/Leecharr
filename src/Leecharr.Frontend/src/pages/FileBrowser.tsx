import React, { useState, useEffect, useCallback, useMemo } from "react";
import { useNavigate } from "react-router";
import { FileManager } from "@cubone/react-file-manager";
import "@cubone/react-file-manager/dist/style.css";
import {
  useFileListing,
  useCreateDirectory,
  useRenameFileEntry,
  useDeleteFileEntry,
  useBatchDeleteFiles,
  usePasteFiles,
  useFilePreview,
} from "../api/hooks";
import { formatBytes } from "../utils/formatters";
import { useConfirm } from "../context/ConfirmContext";
import { useToast } from "../context/ToastContext";
import { useI18nStore } from "../i18n";
import { languages } from "../i18n";

interface FileManagerFile {
  name: string;
  isDirectory: boolean;
  path: string;
  size?: number;
  updatedAt?: string;
}

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
  const { language } = useI18nStore();
  const activeLang = languages.find((l) => l.code === language);
  const cuboneLanguage = activeLang?.cuboneLanguage || "en-US";
  const navigate = useNavigate();
  const confirm = useConfirm();
  const { showToast } = useToast();

  const queryPath =
    typeof window !== "undefined"
      ? new URLSearchParams(window.location.search).get("path") || undefined
      : undefined;

  const [currentPath, setCurrentPath] = useState<string>(queryPath || "");
  const [previewPath, setPreviewPath] = useState<string | null>(null);

  const {
    data: listing,
    isLoading,
    isError,
    refetch,
  } = useFileListing(currentPath || undefined);

  const mkdirMutation = useCreateDirectory();
  const renameMutation = useRenameFileEntry();
  const deleteMutation = useDeleteFileEntry();
  const batchDeleteMutation = useBatchDeleteFiles();
  const pasteMutation = usePasteFiles();
  const { data: previewData, isLoading: isPreviewLoading } = useFilePreview(
    previewPath || undefined,
    !!previewPath,
  );

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

  const handleCopyPath = () => {
    const textToCopy = currentPath || listing?.path || "";
    navigator.clipboard.writeText(textToCopy);
    showToast("Path copied to clipboard", "info");
  };

  const handleOpenInCli = () => {
    const target = currentPath || listing?.path || "/downloads";
    navigate(`/terminal?path=${encodeURIComponent(target)}`);
  };

  const handleDownloadFile = (filePath: string) => {
    window.open(
      `/api/v1/files/download?path=${encodeURIComponent(filePath)}`,
      "_blank",
    );
  };

  const activePath = useMemo(() => {
    return currentPath || listing?.path || "/downloads";
  }, [currentPath, listing]);

  const files: FileManagerFile[] = useMemo(() => {
    if (!listing) return [];

    const fileMap = new Map<string, FileManagerFile>();

    // 1. Ensure all ancestor folders exist in the file list so react-file-manager's
    // directory tree and path resolution can locate the current directory and its ancestors
    const targetPath = listing.path || currentPath || "";
    if (targetPath && targetPath !== "/") {
      const parts = targetPath.split("/").filter(Boolean);
      let accumulated = "";
      for (const part of parts) {
        accumulated += `/${part}`;
        fileMap.set(accumulated, {
          name: part,
          isDirectory: true,
          path: accumulated,
        });
      }
    }

    // 2. Add all directory entries returned for the current listing
    if (listing.entries) {
      for (const entry of listing.entries) {
        fileMap.set(entry.path, {
          name: entry.name,
          isDirectory: entry.isDirectory,
          path: entry.path,
          size: entry.size,
          updatedAt: entry.modified || undefined,
        });
      }
    }

    return Array.from(fileMap.values());
  }, [listing, currentPath]);

  const dirStats = useMemo(() => {
    let folderCount = 0;
    let fileCount = 0;
    let totalSize = 0;
    if (listing?.entries) {
      for (const e of listing.entries) {
        if (e.isDirectory) folderCount++;
        else {
          fileCount++;
          totalSize += e.size || 0;
        }
      }
    }
    return { folderCount, fileCount, totalSize };
  }, [listing]);

  const handleFolderChange = (newPath: string) => {
    navigateTo(newPath || "/");
  };

  const handleFileOpen = (file: FileManagerFile) => {
    if (file.isDirectory) {
      navigateTo(file.path);
    } else {
      setPreviewPath(file.path);
    }
  };

  const handleCreateFolder = async (
    name: string,
    parentFolder: FileManagerFile,
  ) => {
    const trimmed = name.trim();
    if (!trimmed) return;
    try {
      const parent = parentFolder?.path || currentPath || listing?.path || "/";
      const targetPath =
        parent === "/" || parent.endsWith("/")
          ? `${parent}${trimmed}`
          : `${parent}/${trimmed}`;
      await mkdirMutation.mutateAsync(targetPath);
      showToast(`Created folder "${trimmed}"`, "success");
      refetch();
    } catch (err: any) {
      showToast(err?.message || "Failed to create folder", "error");
    }
  };

  const handleRename = async (file: FileManagerFile, newName: string) => {
    const trimmed = newName.trim();
    if (!trimmed || trimmed === file.name) return;
    try {
      await renameMutation.mutateAsync({
        path: file.path,
        newName: trimmed,
      });
      showToast(`Renamed to "${trimmed}"`, "success");
      refetch();
    } catch (err: any) {
      showToast(err?.message || "Failed to rename", "error");
    }
  };

  const handleDelete = async (selectedFiles: FileManagerFile[]) => {
    if (!selectedFiles || selectedFiles.length === 0) return;
    const count = selectedFiles.length;

    const ok = await confirm({
      title:
        count === 1
          ? selectedFiles[0].isDirectory
            ? "Delete Folder"
            : "Delete File"
          : "Delete Selected Items",
      message: (
        <span>
          Are you sure you want to delete{" "}
          <strong>
            {count === 1 ? selectedFiles[0].name : `${count} items`}
          </strong>
          ?
        </span>
      ),
      danger: true,
      confirmText: count === 1 ? "Delete" : `Delete ${count} Items`,
    });

    if (!ok) return;

    try {
      if (count === 1) {
        await deleteMutation.mutateAsync(selectedFiles[0].path);
        showToast(`Deleted "${selectedFiles[0].name}"`, "info");
      } else {
        await batchDeleteMutation.mutateAsync(selectedFiles.map((f) => f.path));
        showToast(`Deleted ${count} items`, "info");
      }
      refetch();
    } catch (err: any) {
      showToast(err?.message || "Failed to delete item(s)", "error");
    }
  };

  const handleDownload = (selectedFiles: FileManagerFile[]) => {
    if (!selectedFiles || selectedFiles.length === 0) return;
    selectedFiles.forEach((file) => {
      if (!file.isDirectory) {
        handleDownloadFile(file.path);
      }
    });
  };

  const handleCut = (selectedFiles: FileManagerFile[]) => {
    if (!selectedFiles || selectedFiles.length === 0) return;
    showToast(
      `Cut ${selectedFiles.length} item${selectedFiles.length === 1 ? "" : "s"}. Navigate to a destination folder to Paste.`,
      "info",
    );
  };

  const handleCopy = (selectedFiles: FileManagerFile[]) => {
    if (!selectedFiles || selectedFiles.length === 0) return;
    showToast(
      `Copied ${selectedFiles.length} item${selectedFiles.length === 1 ? "" : "s"}. Navigate to a destination folder to Paste.`,
      "info",
    );
  };

  const handlePaste = async (
    copiedFiles: FileManagerFile[],
    destinationFolder: FileManagerFile,
    operationType: "copy" | "move",
  ) => {
    if (!copiedFiles || copiedFiles.length === 0) return;
    try {
      const dest = destinationFolder?.path || activePath || "/downloads";
      await pasteMutation.mutateAsync({
        sources: copiedFiles.map((f) => f.path),
        destination: dest,
        operation: operationType,
      });
      showToast(
        `${operationType === "move" ? "Moved" : "Copied"} ${copiedFiles.length} item${copiedFiles.length === 1 ? "" : "s"} to "${destinationFolder?.name || dest}"`,
        "success",
      );
      refetch();
    } catch (err: any) {
      showToast(err?.message || `Failed to ${operationType} item(s)`, "error");
    }
  };

  const handleFileUploaded = () => {
    showToast("File uploaded successfully", "success");
    refetch();
  };

  const segments = getPathSegments(currentPath || listing?.path || "/");
  const displayPath =
    currentPath || listing?.path || listing?.defaultPath || "/";

  if (isLoading && !listing) {
    return (
      <div
        className="card"
        style={{
          padding: "3rem 1.5rem",
          textAlign: "center",
          color: "var(--text-muted)",
        }}
      >
        <span
          style={{
            fontSize: "2rem",
            display: "block",
            marginBottom: "0.75rem",
          }}
        >
          ⏳
        </span>
        Loading file browser...
      </div>
    );
  }

  if (isError) {
    return (
      <div
        className="card"
        style={{
          padding: "2rem 1.5rem",
          textAlign: "center",
          color: "var(--danger, #ef4444)",
        }}
      >
        <span
          style={{
            fontSize: "2rem",
            display: "block",
            marginBottom: "0.75rem",
          }}
        >
          ⚠️
        </span>
        Failed to load directory listing.
      </div>
    );
  }

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        gap: "0.85rem",
      }}
    >
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
          <span style={{ fontSize: "1.4rem" }}>🗂️</span>
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
              {dirStats.folderCount} folder
              {dirStats.folderCount === 1 ? "" : "s"} &bull;{" "}
              {dirStats.fileCount} file{dirStats.fileCount === 1 ? "" : "s"}{" "}
              &bull; {formatBytes(dirStats.totalSize)} total
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
            style={{ fontSize: "0.8rem", padding: "0.35rem 0.75rem" }}
            onClick={handleNavigateUp}
            disabled={!listing?.parent || listing.parent === listing.path}
            title={t("filebrowser.parentDirectory", "Go to parent directory")}
          >
            ⬆ Up
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.8rem", padding: "0.35rem 0.75rem" }}
            onClick={() => refetch()}
            title={t("common.refresh", "Refresh")}
          >
            🔄 Refresh
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.8rem", padding: "0.35rem 0.75rem" }}
            onClick={handleCopyPath}
            title={t("filebrowser.copyPath", "Copy current path to clipboard")}
          >
            📋 Copy Path
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{
              fontSize: "0.8rem",
              padding: "0.35rem 0.75rem",
              fontFamily: "monospace",
            }}
            onClick={handleOpenInCli}
            title={t("filebrowser.openTerminal", "Open an interactive terminal shell in this directory")}
          >
            &gt;_ CLI
          </button>
        </div>
      </div>

      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "0.35rem",
          padding: "0.55rem 0.85rem",
          backgroundColor: "var(--bg-card, #171b35)",
          borderRadius: "8px",
          border: "1px solid var(--border-light, #1c203b)",
          fontSize: "0.85rem",
          overflowX: "auto",
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
            fontSize: "0.85rem",
          }}
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
                color: "var(--text-primary, #f8f4ed)",
                cursor: "pointer",
                fontSize: "0.85rem",
              }}
            >
              {seg.label}
            </button>
          </React.Fragment>
        ))}
      </div>

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

      <div
        style={{
          flex: 1,
          minHeight: "550px",
          display: "flex",
          flexDirection: "column",
        }}
      >
        <FileManager
          language={cuboneLanguage}
          key={activePath}
          className="leecharr-file-manager"
          files={files}
          initialPath={activePath}
          primaryColor="#ffd166"
          collapsibleNav={true}
          defaultNavExpanded={true}
          enableFilePreview={false}
          height="100%"
          permissions={{
            create: true,
            upload: true,
            move: true,
            copy: true,
            rename: true,
            download: true,
            delete: true,
          }}
          fileUploadConfig={{
            url: `/api/v1/files/upload?path=${encodeURIComponent(activePath)}`,
            method: "POST",
          }}
          onFolderChange={handleFolderChange}
          onFileOpen={handleFileOpen}
          onCreateFolder={handleCreateFolder}
          onRename={handleRename}
          onDelete={handleDelete}
          onDownload={handleDownload}
          onCut={handleCut}
          onCopy={handleCopy}
          onPaste={handlePaste}
          onFileUploaded={handleFileUploaded}
          onRefresh={() => refetch()}
        />
      </div>

      {previewPath && (
        <div
          className="modal-overlay"
          onClick={() => setPreviewPath(null)}
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "rgba(16, 17, 26, 0.85)",
            backdropFilter: "blur(6px)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 10000,
            padding: "1.5rem",
          }}
        >
          <div
            className="modal-content"
            onClick={(e) => e.stopPropagation()}
            style={{
              width: "100%",
              maxWidth: "850px",
              maxHeight: "85vh",
              backgroundColor: "var(--bg-secondary, #171b35)",
              borderRadius: "12px",
              border: "1px solid rgba(255, 209, 102, 0.35)",
              display: "flex",
              flexDirection: "column",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "1rem 1.25rem",
                borderBottom: "1px solid var(--border, #23284B)",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.6rem",
                  overflow: "hidden",
                }}
              >
                <span style={{ fontSize: "1.3rem" }}>📄</span>
                <div>
                  <h3
                    style={{
                      margin: 0,
                      fontSize: "1.05rem",
                      color: "var(--text-primary, #f8f4ed)",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                      maxWidth: "550px",
                    }}
                  >
                    {previewData?.name || previewPath.split("/").pop()}
                  </h3>
                  <span
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted, #8a879e)",
                    }}
                  >
                    {previewData?.size ? formatBytes(previewData.size) : ""}{" "}
                    &bull; <code>{previewPath}</code>
                  </span>
                </div>
              </div>

              <div
                style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
              >
                <button
                  type="button"
                  className="btn btn-outline"
                  style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
                  onClick={() => handleDownloadFile(previewPath)}
                >
                  📥 Download
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
                  onClick={() => setPreviewPath(null)}
                >
                  ✕
                </button>
              </div>
            </div>

            <div
              style={{
                flex: 1,
                overflow: "auto",
                padding: "1.25rem",
                backgroundColor: "var(--bg-primary, #10111A)",
              }}
            >
              {isPreviewLoading ? (
                <div
                  style={{
                    textAlign: "center",
                    padding: "3rem",
                    color: "var(--text-muted)",
                  }}
                >
                  Loading file preview...
                </div>
              ) : previewData?.type === "image" ? (
                <div
                  style={{
                    display: "flex",
                    justifyContent: "center",
                    alignItems: "center",
                    minHeight: "260px",
                  }}
                >
                  <img
                    src={previewData.downloadUrl}
                    alt={previewData.name}
                    style={{
                      maxWidth: "100%",
                      maxHeight: "60vh",
                      objectFit: "contain",
                    }}
                  />
                </div>
              ) : previewData?.type === "text" ? (
                <pre
                  style={{
                    margin: 0,
                    padding: "1rem",
                    backgroundColor: "rgba(0, 0, 0, 0.4)",
                    borderRadius: "6px",
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                    color: "var(--text-primary, #F8F4ED)",
                    whiteSpace: "pre-wrap",
                    maxHeight: "55vh",
                  }}
                >
                  {previewData.content}
                </pre>
              ) : (
                <div
                  style={{
                    textAlign: "center",
                    padding: "3rem 1rem",
                    color: "var(--text-secondary, #C7C5D3)",
                  }}
                >
                  <span
                    style={{
                      fontSize: "3rem",
                      display: "block",
                      marginBottom: "1rem",
                    }}
                  >
                    📄
                  </span>
                  <h4 style={{ margin: "0 0 0.5rem", fontSize: "1.1rem" }}>
                    Binary / Media File
                  </h4>
                  <p
                    style={{
                      margin: "0 0 1.5rem",
                      fontSize: "0.85rem",
                      color: "var(--text-muted, #8a879e)",
                    }}
                  >
                    Direct inline text preview is not available for this file
                    type. You can download the file or open the directory in
                    terminal.
                  </p>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "center",
                      gap: "0.75rem",
                    }}
                  >
                    <button
                      type="button"
                      className="btn btn-primary"
                      onClick={() => handleDownloadFile(previewPath)}
                    >
                      📥 Download File (
                      {previewData?.size ? formatBytes(previewData.size) : ""})
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline"
                      onClick={() => {
                        setPreviewPath(null);
                        handleOpenInCli();
                      }}
                    >
                      💻 Open in Terminal
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default FileBrowser;
