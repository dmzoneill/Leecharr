import React, {
  useState,
  useCallback,
  useMemo,
  useRef,
  useEffect,
} from "react";
import { useNavigate, useSearchParams } from "react-router";
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
import { useI18nStore, useTranslation, languages } from "../i18n";
import { MediaPlayerModal } from "../components/MediaPlayerModal";
import {
  isPlayableFile,
  buildFileStreamUrl,
  buildFileDownloadUrl,
  buildFilePlaylistUrl,
} from "../utils/mediaPlayer";

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

export type FileCategory =
  | "folder"
  | "video"
  | "audio"
  | "archive"
  | "nfo"
  | "subtitle"
  | "torrent"
  | "document"
  | "executable"
  | "image"
  | "code"
  | "text"
  | "other";

export function getFileCategory(fileName: string): FileCategory {
  if (!fileName || !fileName.includes(".")) return "other";
  const ext = fileName.split(".").pop()?.toLowerCase() || "";

  if (
    [
      "mkv",
      "mp4",
      "avi",
      "mov",
      "m4v",
      "webm",
      "flv",
      "wmv",
      "ts",
      "m2ts",
      "mpg",
      "mpeg",
      "vob",
      "ogv",
      "3gp",
      "divx",
      "rmvb",
      "asf",
    ].includes(ext)
  ) {
    return "video";
  }

  if (
    [
      "mp3",
      "flac",
      "wav",
      "m4a",
      "aac",
      "ogg",
      "opus",
      "wma",
      "alac",
      "ape",
      "mka",
      "mid",
      "midi",
      "ac3",
      "dts",
      "eac3",
      "aiff",
    ].includes(ext)
  ) {
    return "audio";
  }

  if (
    [
      "zip",
      "rar",
      "7z",
      "tar",
      "gz",
      "bz2",
      "xz",
      "zst",
      "tgz",
      "tbz2",
      "cab",
      "iso",
      "img",
      "dmg",
    ].includes(ext)
  ) {
    return "archive";
  }

  if (["nfo", "diz"].includes(ext)) {
    return "nfo";
  }

  if (["srt", "vtt", "ass", "ssa", "sub", "idx"].includes(ext)) {
    return "subtitle";
  }

  if (ext === "torrent") {
    return "torrent";
  }

  if (
    [
      "pdf",
      "doc",
      "docx",
      "epub",
      "mobi",
      "azw",
      "azw3",
      "cbz",
      "cbr",
      "rtf",
      "odt",
      "xls",
      "xlsx",
      "csv",
      "tsv",
      "ppt",
      "pptx",
    ].includes(ext)
  ) {
    return "document";
  }

  if (
    ["exe", "msi", "bin", "apk", "deb", "rpm", "run", "app", "pkg"].includes(
      ext,
    )
  ) {
    return "executable";
  }

  if (
    [
      "png",
      "jpg",
      "jpeg",
      "webp",
      "gif",
      "bmp",
      "ico",
      "tiff",
      "tif",
      "heic",
      "heif",
      "avif",
      "svg",
    ].includes(ext)
  ) {
    return "image";
  }

  if (
    [
      "js",
      "ts",
      "jsx",
      "tsx",
      "py",
      "json",
      "xml",
      "html",
      "css",
      "yaml",
      "yml",
      "toml",
      "ini",
      "conf",
      "config",
      "env",
      "sql",
      "c",
      "cpp",
      "h",
      "cs",
      "java",
      "go",
      "rs",
      "php",
      "rb",
      "sh",
      "bash",
      "zsh",
      "bat",
      "cmd",
      "ps1",
    ].includes(ext)
  ) {
    return "code";
  }

  if (["txt", "log", "readme", "license", "changelog"].includes(ext)) {
    return "text";
  }

  return "other";
}

export function FileBrowser() {
  const { language } = useI18nStore();
  const { t } = useTranslation();
  const activeLang = languages.find((l) => l.code === language);
  const cuboneLanguage = activeLang?.cuboneLanguage || "en-US";
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const confirm = useConfirm();
  const { showToast } = useToast();

  const currentPath = searchParams.get("path") || "";
  const [previewPath, setPreviewPath] = useState<string | null>(null);
  const [playingMediaFile, setPlayingMediaFile] =
    useState<FileManagerFile | null>(null);
  const [selectedFiles, setSelectedFiles] = useState<FileManagerFile[]>([]);
  const lastContextMenuFileRef = useRef<FileManagerFile | null>(null);
  const fileManagerContainerRef = useRef<HTMLDivElement>(null);

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

  const navigateTo = useCallback(
    (path: string) => {
      setSearchParams(path ? { path } : {});
    },
    [setSearchParams],
  );

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

  const handlePlayFile = useCallback((file: FileManagerFile) => {
    setPlayingMediaFile(file);
  }, []);

  const handlePreviewFile = useCallback((file: FileManagerFile) => {
    setPreviewPath(file.path);
  }, []);

  const handleDownloadM3u = useCallback((file: FileManagerFile) => {
    window.open(buildFilePlaylistUrl(file.path), "_blank");
  }, []);

  const handleFileOpen = (file: FileManagerFile) => {
    if (file.isDirectory) {
      navigateTo(file.path);
    } else if (isPlayableFile(file.name)) {
      handlePlayFile(file);
    } else {
      handlePreviewFile(file);
    }
  };

  // Enhance context menu & toolbar & bypass internal delete modal
  useEffect(() => {
    const container = fileManagerContainerRef.current;
    if (!container) return;

    const getTargetFile = (): FileManagerFile | null => {
      if (
        lastContextMenuFileRef.current &&
        !lastContextMenuFileRef.current.isDirectory
      ) {
        return lastContextMenuFileRef.current;
      }
      if (selectedFiles.length === 1 && !selectedFiles[0].isDirectory) {
        return selectedFiles[0];
      }
      // Check DOM for selected element title
      const selectedEl = container.querySelector<HTMLElement>(
        ".file-item-container.file-selected",
      );
      const title =
        selectedEl?.getAttribute("title") ||
        selectedEl
          ?.querySelector<HTMLElement>(".file-name")
          ?.textContent?.trim();
      if (title) {
        const found = files.find((f) => f.name === title && !f.isDirectory);
        if (found) return found;
      }
      return null;
    };

    const enhanceContextMenu = () => {
      const contextMenuUl = container.querySelector<HTMLUListElement>(
        ".fm-context-menu .file-context-menu-list ul",
      );
      if (!contextMenuUl) return;

      const targetFile = getTargetFile();
      if (!targetFile || targetFile.isDirectory) return;

      const existingGroup = contextMenuUl.querySelector<HTMLElement>(
        ".leecharr-ctx-group",
      );
      if (existingGroup) {
        if (existingGroup.dataset.filePath === targetFile.path) {
          return;
        }
        existingGroup.remove();
      }

      const isPlayable = isPlayableFile(targetFile.name);
      const closeMenu = () => {
        const contextMenuEl =
          container.querySelector<HTMLElement>(".fm-context-menu");
        if (contextMenuEl) {
          contextMenuEl.classList.remove("visible");
          contextMenuEl.classList.add("hidden");
        }
      };

      const groupDiv = document.createElement("div");
      groupDiv.className = "leecharr-ctx-group";
      groupDiv.dataset.filePath = targetFile.path;

      if (isPlayable) {
        // Play
        const playLi = document.createElement("li");
        playLi.className = "leecharr-ctx-item leecharr-ctx-play";
        playLi.style.cursor = "pointer";
        playLi.style.fontWeight = "600";
        playLi.style.color = "var(--accent, #ffd166)";
        playLi.innerHTML = `<span style="font-size: 15px; width: 18px; display: inline-flex; align-items: center; justify-content: center;">▶</span> <span>${t("common.play", "Play / Stream")}</span>`;
        playLi.addEventListener("click", (ev) => {
          ev.stopPropagation();
          closeMenu();
          handlePlayFile(targetFile);
        });
        groupDiv.appendChild(playLi);

        // M3U
        const m3uLi = document.createElement("li");
        m3uLi.className = "leecharr-ctx-item leecharr-ctx-m3u";
        m3uLi.style.cursor = "pointer";
        m3uLi.innerHTML = `<span style="font-size: 15px; width: 18px; display: inline-flex; align-items: center; justify-content: center;">📥</span> <span>${t("filebrowser.downloadM3u", "Download M3U Playlist")}</span>`;
        m3uLi.addEventListener("click", (ev) => {
          ev.stopPropagation();
          closeMenu();
          handleDownloadM3u(targetFile);
        });
        groupDiv.appendChild(m3uLi);

        // Quick Preview
        const previewLi = document.createElement("li");
        previewLi.className = "leecharr-ctx-item leecharr-ctx-preview";
        previewLi.style.cursor = "pointer";
        previewLi.innerHTML = `<span style="font-size: 15px; width: 18px; display: inline-flex; align-items: center; justify-content: center;">👁</span> <span>${t("common.preview", "Quick Preview")}</span>`;
        previewLi.addEventListener("click", (ev) => {
          ev.stopPropagation();
          closeMenu();
          handlePreviewFile(targetFile);
        });
        groupDiv.appendChild(previewLi);
      } else {
        // Preview File
        const previewLi = document.createElement("li");
        previewLi.className = "leecharr-ctx-item leecharr-ctx-preview";
        previewLi.style.cursor = "pointer";
        previewLi.style.fontWeight = "600";
        previewLi.style.color = "var(--accent, #ffd166)";
        previewLi.innerHTML = `<span style="font-size: 15px; width: 18px; display: inline-flex; align-items: center; justify-content: center;">👁</span> <span>${t("common.preview", "Preview File")}</span>`;
        previewLi.addEventListener("click", (ev) => {
          ev.stopPropagation();
          closeMenu();
          handlePreviewFile(targetFile);
        });
        groupDiv.appendChild(previewLi);
      }

      const divider = document.createElement("div");
      divider.className = "divider";
      groupDiv.appendChild(divider);

      contextMenuUl.insertBefore(groupDiv, contextMenuUl.firstChild);
    };

    const enhanceToolbar = () => {
      const tbActions = container.querySelector<HTMLElement>(
        ".toolbar.file-selected .file-action-container > div",
      );
      if (!tbActions) return;

      const targetFile =
        (selectedFiles.length === 1 && !selectedFiles[0].isDirectory
          ? selectedFiles[0]
          : null) ||
        files.find(
          (f) =>
            !f.isDirectory &&
            f.name ===
              container
                .querySelector(".file-item-container.file-selected")
                ?.getAttribute("title"),
        );

      if (!targetFile || targetFile.isDirectory) {
        tbActions
          .querySelectorAll(".leecharr-tb-btn")
          .forEach((el) => el.remove());
        delete tbActions.dataset.leecharrPath;
        return;
      }

      if (tbActions.dataset.leecharrPath === targetFile.path) {
        return; // already enhanced
      }

      tbActions
        .querySelectorAll(".leecharr-tb-btn")
        .forEach((el) => el.remove());
      tbActions.dataset.leecharrPath = targetFile.path;

      const isPlayable = isPlayableFile(targetFile.name);

      if (isPlayable) {
        const playBtn = document.createElement("button");
        playBtn.type = "button";
        playBtn.className =
          "item-action file-action leecharr-tb-btn leecharr-tb-play";
        playBtn.style.color = "var(--accent, #ffd166)";
        playBtn.style.fontWeight = "700";
        playBtn.innerHTML = `<span style="font-size: 16px; margin-right: 4px;">▶</span><span>${t("common.play", "Play")}</span>`;
        playBtn.addEventListener("click", (ev) => {
          ev.stopPropagation();
          handlePlayFile(targetFile);
        });

        const m3uBtn = document.createElement("button");
        m3uBtn.type = "button";
        m3uBtn.className =
          "item-action file-action leecharr-tb-btn leecharr-tb-m3u";
        m3uBtn.innerHTML = `<span style="font-size: 16px; margin-right: 4px;">📥</span><span>M3U</span>`;
        m3uBtn.addEventListener("click", (ev) => {
          ev.stopPropagation();
          handleDownloadM3u(targetFile);
        });

        const prevBtn = document.createElement("button");
        prevBtn.type = "button";
        prevBtn.className =
          "item-action file-action leecharr-tb-btn leecharr-tb-preview";
        prevBtn.innerHTML = `<span style="font-size: 16px; margin-right: 4px;">👁</span><span>${t("common.preview", "Preview")}</span>`;
        prevBtn.addEventListener("click", (ev) => {
          ev.stopPropagation();
          handlePreviewFile(targetFile);
        });

        tbActions.insertBefore(prevBtn, tbActions.firstChild);
        tbActions.insertBefore(m3uBtn, tbActions.firstChild);
        tbActions.insertBefore(playBtn, tbActions.firstChild);
      } else {
        const prevBtn = document.createElement("button");
        prevBtn.type = "button";
        prevBtn.className =
          "item-action file-action leecharr-tb-btn leecharr-tb-preview";
        prevBtn.style.color = "var(--accent, #ffd166)";
        prevBtn.style.fontWeight = "700";
        prevBtn.innerHTML = `<span style="font-size: 16px; margin-right: 4px;">👁</span><span>${t("common.preview", "Preview")}</span>`;
        prevBtn.addEventListener("click", (ev) => {
          ev.stopPropagation();
          handlePreviewFile(targetFile);
        });

        tbActions.insertBefore(prevBtn, tbActions.firstChild);
      }
    };

    const onContextMenuCapture = (e: MouseEvent) => {
      const target = e.target as HTMLElement | null;
      const itemEl = target?.closest<HTMLElement>(
        ".file-item-container, [title]",
      );
      const title =
        itemEl?.getAttribute("title") ||
        itemEl?.querySelector<HTMLElement>(".file-name")?.textContent?.trim();
      if (title) {
        const found = files.find((f) => f.name === title && !f.isDirectory);
        if (found) {
          lastContextMenuFileRef.current = found;
        }
      }
      if (
        !lastContextMenuFileRef.current &&
        selectedFiles.length === 1 &&
        !selectedFiles[0].isDirectory
      ) {
        lastContextMenuFileRef.current = selectedFiles[0];
      }

      [0, 15, 40, 80, 150].forEach((delay) => {
        setTimeout(enhanceContextMenu, delay);
      });
    };

    container.addEventListener("contextmenu", onContextMenuCapture, true);

    const observer = new MutationObserver(() => {
      // 1. Bypass Cubone delete modal
      const deleteDangerBtn = container.querySelector<HTMLButtonElement>(
        ".file-delete-confirm-actions .fm-button-danger",
      );
      if (deleteDangerBtn) {
        deleteDangerBtn.click();
      }

      enhanceContextMenu();
      enhanceToolbar();
      enhanceFileItems();
    });

    const enhanceFileItems = () => {
      const items = container.querySelectorAll<HTMLElement>(
        ".file-item-container",
      );
      items.forEach((itemEl) => {
        const title =
          itemEl.getAttribute("title") ||
          itemEl
            .querySelector<HTMLElement>(".file-name")
            ?.textContent?.trim() ||
          "";

        const isDir =
          itemEl.querySelector(".folder-item") !== null ||
          files.find((f) => f.name === title)?.isDirectory;

        if (isDir) {
          itemEl.dataset.fileCategory = "folder";
          return;
        }

        const category = getFileCategory(title);
        const ext = title.includes(".")
          ? title.split(".").pop()?.toLowerCase() || ""
          : "";

        if (itemEl.dataset.fileCategory !== category) {
          itemEl.dataset.fileCategory = category;
        }
        if (ext && itemEl.dataset.fileExt !== ext) {
          itemEl.dataset.fileExt = ext;
        }

        // Add badge if not present and not editing
        if (!itemEl.querySelector(".rename-file-container")) {
          const nameEl = itemEl.querySelector<HTMLElement>(".file-name");
          if (nameEl && !itemEl.querySelector(".leecharr-file-badge")) {
            const badge = document.createElement("span");
            badge.className = `leecharr-file-badge leecharr-badge-${category}`;
            badge.textContent = (ext || category).toUpperCase();
            nameEl.insertAdjacentElement("beforebegin", badge);
          }
        }
      });
    };

    observer.observe(container, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["class", "style"],
    });

    enhanceToolbar();
    enhanceFileItems();

    return () => {
      container.removeEventListener("contextmenu", onContextMenuCapture, true);
      observer.disconnect();
    };
  }, [
    files,
    selectedFiles,
    handlePlayFile,
    handlePreviewFile,
    handleDownloadM3u,
    t,
  ]);

  const handleCreateFolder = async (
    nameOrParent?: any,
    parentFolder?: FileManagerFile,
  ) => {
    let name = typeof nameOrParent === "string" ? nameOrParent : "";
    const parent =
      (typeof nameOrParent === "object" ? nameOrParent?.path : null) ||
      parentFolder?.path ||
      activePath ||
      "/downloads";
    if (!name) {
      name =
        window.prompt(
          t("filebrowser.enterFolderName", "Enter new folder name:"),
        ) || "";
    }
    if (!name || !name.trim()) return;
    const trimmed = name.trim();
    try {
      const targetPath =
        parent === "/" || parent.endsWith("/")
          ? `${parent}${trimmed}`
          : `${parent}/${trimmed}`;
      await mkdirMutation.mutateAsync(targetPath);
      showToast(
        t("filebrowser.createdFolder", 'Created folder "{name}"', {
          name: trimmed,
        }),
        "success",
      );
      refetch();
    } catch (err: any) {
      showToast(
        err?.message ||
          t("filebrowser.failedToCreateFolder", "Failed to create folder"),
        "error",
      );
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
      showToast(
        t("filebrowser.renamedTo", 'Renamed to "{name}"', {
          name: trimmed,
        }),
        "success",
      );
      refetch();
    } catch (err: any) {
      showToast(
        err?.message || t("filebrowser.failedToRename", "Failed to rename"),
        "error",
      );
    }
  };

  const handleDelete = async (selectedFiles: FileManagerFile[]) => {
    if (!selectedFiles || selectedFiles.length === 0) return;
    const count = selectedFiles.length;

    const ok = await confirm({
      title:
        count === 1
          ? selectedFiles[0].isDirectory
            ? t("filebrowser.deleteFolderTitle", "Delete Folder")
            : t("filebrowser.deleteFileTitle", "Delete File")
          : t("filebrowser.deleteSelectedTitle", "Delete Selected Items"),
      message: (
        <span>
          {t("filebrowser.deleteConfirm", "Are you sure you want to delete")}{" "}
          <strong>
            {count === 1
              ? selectedFiles[0].name
              : t("filebrowser.itemCount", "{count} items", { count })}
          </strong>
          ?
        </span>
      ),
      danger: true,
      confirmText:
        count === 1
          ? t("common.delete", "Delete")
          : t("filebrowser.deleteCountItems", "Delete {count} Items", {
              count,
            }),
    });

    if (!ok) return;

    try {
      if (count === 1) {
        await deleteMutation.mutateAsync(selectedFiles[0].path);
        showToast(
          t("filebrowser.deletedItem", 'Deleted "{name}"', {
            name: selectedFiles[0].name,
          }),
          "info",
        );
      } else {
        await batchDeleteMutation.mutateAsync(selectedFiles.map((f) => f.path));
        showToast(
          t("filebrowser.deletedMultiple", "Deleted {count} items", { count }),
          "info",
        );
      }
      refetch();
    } catch (err: any) {
      showToast(
        err?.message ||
          t("filebrowser.failedToDelete", "Failed to delete item(s)"),
        "error",
      );
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
      t(
        "filebrowser.cutItemsNavigate",
        "Cut {count} item(s). Navigate to a destination folder to Paste.",
        { count: selectedFiles.length },
      ),
      "info",
    );
  };

  const handleCopy = (selectedFiles: FileManagerFile[]) => {
    if (!selectedFiles || selectedFiles.length === 0) return;
    showToast(
      t(
        "filebrowser.copiedItemsNavigate",
        "Copied {count} item(s). Navigate to a destination folder to Paste.",
        { count: selectedFiles.length },
      ),
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
        t("filebrowser.pasteSuccess", '{op} {count} item(s) to "{dest}"', {
          op:
            operationType === "move"
              ? t("filebrowser.moved", "Moved")
              : t("filebrowser.copied", "Copied"),
          count: copiedFiles.length,
          dest: destinationFolder?.name || dest,
        }),
        "success",
      );
      refetch();
    } catch (err: any) {
      showToast(
        err?.message ||
          t("filebrowser.failedToPaste", "Failed to paste item(s)"),
        "error",
      );
    }
  };

  const handleFileUploaded = () => {
    showToast(
      t("filebrowser.fileUploadedSuccess", "File uploaded successfully"),
      "success",
    );
    refetch();
  };

  const segments = getPathSegments(currentPath || listing?.path || "/");
  const displayPath =
    currentPath || listing?.path || listing?.defaultPath || "/";

  if (isLoading && !listing) {
    return (
      <div
        className="content-area"
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          height: "100%",
          padding: "1.5rem",
          boxSizing: "border-box",
        }}
      >
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
          {t("filebrowser.loadingBrowser", "Loading file browser...")}
        </div>
      </div>
    );
  }

  if (isError) {
    return (
      <div
        className="content-area"
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          height: "100%",
          padding: "1.5rem",
          boxSizing: "border-box",
        }}
      >
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
          {t(
            "filebrowser.failedToLoadDirectory",
            "Failed to load directory listing.",
          )}
        </div>
      </div>
    );
  }

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        gap: "1rem",
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
          marginBottom: "1rem",
          flexWrap: "wrap",
          gap: "1rem",
          flexShrink: 0,
        }}
      >
        <div>
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
            <span>📁</span> {t("filebrowser.title", "File Browser")}
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {dirStats.folderCount} {t("filebrowser.folderCount", "folder(s)")}{" "}
            &bull; {dirStats.fileCount} {t("filebrowser.fileCount", "file(s)")}{" "}
            &bull; {formatBytes(dirStats.totalSize)}{" "}
            {t("common.total", "total")}
          </p>
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            flexWrap: "wrap",
          }}
        >
          {selectedFiles.length === 1 && !selectedFiles[0].isDirectory && (
            <>
              {isPlayableFile(selectedFiles[0].name) ? (
                <>
                  <button
                    type="button"
                    className="btn btn-primary"
                    style={{
                      fontSize: "0.85rem",
                      padding: "0.35rem 0.75rem",
                      display: "flex",
                      alignItems: "center",
                      gap: "0.35rem",
                    }}
                    onClick={() => handlePlayFile(selectedFiles[0])}
                    title={t("common.play", "Play / Stream")}
                  >
                    <span>▶</span> {t("common.play", "Play")}
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline"
                    style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
                    onClick={() => handleDownloadM3u(selectedFiles[0])}
                    title={t(
                      "filebrowser.downloadM3u",
                      "Download M3U Playlist",
                    )}
                  >
                    📥 M3U
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline"
                    style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
                    onClick={() => handlePreviewFile(selectedFiles[0])}
                    title={t("common.preview", "Quick Preview")}
                  >
                    👁 {t("common.preview", "Preview")}
                  </button>
                </>
              ) : (
                <button
                  type="button"
                  className="btn btn-primary"
                  style={{
                    fontSize: "0.85rem",
                    padding: "0.35rem 0.75rem",
                    display: "flex",
                    alignItems: "center",
                    gap: "0.35rem",
                  }}
                  onClick={() => handlePreviewFile(selectedFiles[0])}
                  title={t("common.preview", "Preview File")}
                >
                  <span>👁</span> {t("common.preview", "Preview")}
                </button>
              )}
            </>
          )}
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={handleNavigateUp}
            disabled={!listing?.parent || listing.parent === listing.path}
            title={t("filebrowser.parentDirectory", "Go to parent directory")}
          >
            ⬆ {t("filebrowser.up", "Up")}
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={() => refetch()}
            title={t("common.refresh", "Refresh")}
          >
            🔄 {t("common.refresh", "Refresh")}
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={handleCopyPath}
            title={t("filebrowser.copyPath", "Copy current path to clipboard")}
          >
            📋 {t("filebrowser.copyPath", "Copy Path")}
          </button>
          <button
            type="button"
            className="btn btn-outline"
            style={{
              fontSize: "0.85rem",
              padding: "0.35rem 0.75rem",
              fontFamily: "monospace",
            }}
            onClick={handleOpenInCli}
            title={t(
              "filebrowser.openTerminal",
              "Open an interactive terminal shell in this directory",
            )}
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
          border: "1px solid var(--border-light)",
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
            padding: "0.1rem 0.35rem",
            borderRadius: "3px",
            fontFamily: "monospace",
          }}
        >
          /
        </button>
        {segments.map((seg, idx) => (
          <React.Fragment key={seg.fullPath}>
            <span style={{ color: "var(--text-muted, #8a879e)" }}>/</span>
            <button
              type="button"
              onClick={() => navigateTo(seg.fullPath)}
              style={{
                background:
                  idx === segments.length - 1
                    ? "rgba(255, 209, 102, 0.15)"
                    : "none",
                border: "none",
                color:
                  idx === segments.length - 1
                    ? "var(--accent, #ffd166)"
                    : "var(--text-primary, #f8f4ed)",
                fontWeight: idx === segments.length - 1 ? 600 : 400,
                cursor: "pointer",
                padding: "0.15rem 0.4rem",
                borderRadius: "4px",
                fontFamily: "monospace",
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
          {t("filebrowser.directoryDoesNotExist", "Directory does not exist:")}{" "}
          <code style={{ wordBreak: "break-all" }}>{displayPath}</code>
        </div>
      )}

      <div
        ref={fileManagerContainerRef}
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
          onSelectionChange={(selected) => setSelectedFiles(selected || [])}
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
                borderBottom: "1px solid var(--border)",
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
                {(previewData?.type === "video" ||
                  previewData?.type === "audio") && (
                  <>
                    <button
                      type="button"
                      className="btn btn-primary"
                      style={{
                        fontSize: "0.8rem",
                        padding: "0.3rem 0.65rem",
                        display: "flex",
                        alignItems: "center",
                        gap: "0.35rem",
                      }}
                      onClick={() => {
                        setPlayingMediaFile({
                          name: previewData.name,
                          path: previewData.path,
                          size: previewData.size,
                          isDirectory: false,
                        });
                        setPreviewPath(null);
                      }}
                      title={t("player.openInPlayer", "Open in Media Player")}
                    >
                      <span>▶</span>{" "}
                      {t("player.openInPlayer", "Open in Player")}
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline"
                      style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
                      onClick={() =>
                        window.open(
                          previewData.playlistUrl ||
                            buildFilePlaylistUrl(previewPath),
                          "_blank",
                        )
                      }
                      title={t(
                        "filebrowser.downloadM3u",
                        "Download M3U Playlist",
                      )}
                    >
                      📥 M3U
                    </button>
                  </>
                )}
                {previewData?.type === "text" && previewData.content && (
                  <button
                    type="button"
                    className="btn btn-outline"
                    style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
                    onClick={() => {
                      navigator.clipboard.writeText(previewData.content || "");
                      showToast(
                        t("common.copiedToClipboard", "Copied to clipboard"),
                        "info",
                      );
                    }}
                    title={t("common.copy", "Copy content")}
                  >
                    📋 {t("common.copy", "Copy")}
                  </button>
                )}
                <button
                  type="button"
                  className="btn btn-outline"
                  style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
                  onClick={() => handleDownloadFile(previewPath)}
                >
                  📥 {t("common.download", "Download")}
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
                  {t("filebrowser.loadingPreview", "Loading file preview...")}
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
              ) : previewData?.type === "video" ? (
                <div
                  style={{
                    display: "flex",
                    justifyContent: "center",
                    alignItems: "center",
                    flexDirection: "column",
                    gap: "1rem",
                    minHeight: "260px",
                  }}
                >
                  <video
                    src={previewData.downloadUrl}
                    controls
                    autoPlay
                    style={{
                      maxWidth: "100%",
                      maxHeight: "60vh",
                      borderRadius: "6px",
                      backgroundColor: "#000",
                    }}
                  >
                    Your browser does not support the video tag.
                  </video>
                </div>
              ) : previewData?.type === "audio" ? (
                <div
                  style={{
                    display: "flex",
                    justifyContent: "center",
                    alignItems: "center",
                    flexDirection: "column",
                    padding: "2rem",
                    gap: "1.5rem",
                    minHeight: "200px",
                  }}
                >
                  <span style={{ fontSize: "3.5rem" }}>🎵</span>
                  <audio
                    src={previewData.downloadUrl}
                    controls
                    autoPlay
                    style={{ width: "100%", maxWidth: "500px" }}
                  >
                    Your browser does not support the audio element.
                  </audio>
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
                    {t("filebrowser.binaryMediaFile", "Binary / Media File")}
                  </h4>
                  <p
                    style={{
                      margin: "0 0 1.5rem",
                      fontSize: "0.85rem",
                      color: "var(--text-muted, #8a879e)",
                    }}
                  >
                    {t(
                      "filebrowser.binaryPreviewNotice",
                      "Direct inline text preview is not available for this file type. You can download the file or open the directory in terminal.",
                    )}
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
                      📥 {t("filebrowser.downloadFile", "Download File")} (
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
                      💻 {t("filebrowser.openInTerminal", "Open in Terminal")}
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {playingMediaFile && (
        <MediaPlayerModal
          isOpen={true}
          onClose={() => setPlayingMediaFile(null)}
          file={{
            path: playingMediaFile.path,
            name: playingMediaFile.name,
            size: playingMediaFile.size,
          }}
          streamUrl={buildFileStreamUrl(playingMediaFile.path)}
          downloadUrl={buildFileDownloadUrl(playingMediaFile.path)}
          playlistUrl={buildFilePlaylistUrl(playingMediaFile.path)}
        />
      )}
    </div>
  );
}

export default FileBrowser;
