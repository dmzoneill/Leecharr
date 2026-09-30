import { useState, useEffect } from "react";
import { useTranslation } from "../i18n";
import { useAddTorrent, useCategories, useTags, AddTorrentResult } from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { trackTorrentAdd } from "../utils/analytics";
import {
  TorrentFileInputTab,
  MagnetInputTab,
  IndexerSearchTab,
  TorrentCreationTab,
} from "./addtorrent";

export { parseMagnetPreview, type MagnetInfo } from "./addtorrent";

export interface AddTorrentFormProps {
  initialMode?: "file" | "magnet" | "search" | "create";
  initialQuery?: string;
  isModal?: boolean;
  onClose?: () => void;
  onSuccess?: () => void;
}

export type InputMode = "file" | "magnet" | "search" | "create";

export function AddTorrentForm({
  initialMode = "file",
  initialQuery = "",
  isModal = false,
  onClose,
  onSuccess,
}: AddTorrentFormProps) {
  const { t } = useTranslation();
  const [mode, setMode] = useState<InputMode>(initialMode);
  const [files, setFiles] = useState<File[]>([]);
  const [magnetLink, setMagnetLink] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("");
  const [savePath, setSavePath] = useState("");
  const [isPaused, setIsPaused] = useState(false);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>([]);
  const [sequentialDownload, setSequentialDownload] = useState(false);
  const [firstLastPiecePriority, setFirstLastPiecePriority] = useState(false);
  const [downloadLimit, setDownloadLimit] = useState<number | "">("");
  const [uploadLimit, setUploadLimit] = useState<number | "">("");
  const [resultMessage, setResultMessage] = useState<string | null>(null);

  const addTorrent = useAddTorrent();
  const { showToast } = useToast();
  const { data: categories } = useCategories();
  const { data: tags = [] } = useTags();

  // Preselect default category if none chosen
  useEffect(() => {
    if (!selectedCategory && categories && categories.length > 0) {
      const defaultCat = categories.find((c) => c.isDefault);
      if (defaultCat) {
        setSelectedCategory(defaultCat.name);
      }
    }
  }, [categories, selectedCategory]);

  const handleSubmit = () => {
    const dlLimitNum =
      typeof downloadLimit === "number" && downloadLimit > 0
        ? downloadLimit
        : undefined;
    const ulLimitNum =
      typeof uploadLimit === "number" && uploadLimit > 0
        ? uploadLimit
        : undefined;
    const commonPayload = {
      category: selectedCategory,
      savePath: savePath.trim() || undefined,
      isPaused,
      sequentialDownload: sequentialDownload || undefined,
      firstLastPiecePriority: firstLastPiecePriority || undefined,
      downloadLimit: dlLimitNum,
      uploadLimit: ulLimitNum,
      tags: selectedTagIds.length > 0 ? selectedTagIds : undefined,
    };

    if (mode === "file" && files.length > 0) {
      setResultMessage(null);
      addTorrent.mutate(
        {
          files,
          ...commonPayload,
        },
        {
          onSuccess: (result: AddTorrentResult) => {
            trackTorrentAdd(
              "file",
              result?.added?.length || files.length,
              selectedCategory,
              {
                start_paused: isPaused,
              },
            );
            if (result && result.failed && result.failed.length === 0) {
              showToast(
                t("addTorrent.addedTorrentsSuccess", {
                  count: result.added.length,
                  defaultValue: `Added ${result.added.length} torrent(s) successfully`,
                }),
                "success",
              );
              if (onSuccess) onSuccess();
              if (onClose) onClose();
              return;
            }
            if (result && result.failed && result.failed.length > 0) {
              const failedNames = new Set(result.failed.map((f) => f.fileName));
              setFiles((prev) => prev.filter((f) => failedNames.has(f.name)));
              const details = result.failed
                .map((f) => `${f.fileName} (${f.reason})`)
                .join("; ");
              const summaryMsg = `${result.added?.length ?? 0} added, ${result.failed.length} skipped: ${details}`;
              setResultMessage(summaryMsg);
              showToast(summaryMsg, "error");
            } else {
              showToast(
                t(
                  "addTorrent.torrentAddedSuccess",
                  "Torrent(s) added successfully",
                ),
                "success",
              );
              if (onSuccess) onSuccess();
              if (onClose) onClose();
            }
          },
          onError: (err) => {
            showToast(
              t("addTorrent.failedToUploadTorrents", {
                message: err.message,
                defaultValue: `Failed to upload torrents: ${err.message}`,
              }),
              "error",
            );
          },
        },
      );
    } else if (mode === "magnet" && magnetLink.trim()) {
      addTorrent.mutate(
        {
          magnetLink: magnetLink.trim(),
          ...commonPayload,
        },
        {
          onSuccess: () => {
            trackTorrentAdd("magnet", 1, selectedCategory, {
              start_paused: isPaused,
            });
            showToast(
              t(
                "addTorrent.magnetAddedSuccess",
                "Magnet link added successfully",
              ),
              "success",
            );
            setMagnetLink("");
            if (onSuccess) onSuccess();
            if (onClose) onClose();
          },
          onError: (err) => {
            showToast(
              t("addTorrent.failedToAddMagnet", {
                message: err.message,
                defaultValue: `Failed to add magnet: ${err.message}`,
              }),
              "error",
            );
          },
        },
      );
    }
  };

  const isMagnetValid = magnetLink.trim().toLowerCase().startsWith("magnet:?");
  const canSubmit =
    (mode === "file" && files.length > 0) ||
    (mode === "magnet" && isMagnetValid);

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
          borderBottom: "1px solid var(--border-light)",
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
            backgroundColor:
              mode === "file" ? "var(--accent, #ffd166)" : "transparent",
            color:
              mode === "file" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          {t("addTorrent.torrentFileTab", "📁 Torrent File")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "magnet" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("magnet")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
            backgroundColor:
              mode === "magnet" ? "var(--accent, #ffd166)" : "transparent",
            color:
              mode === "magnet" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          {t("addTorrent.magnetLinkTab", "🧲 Magnet Link")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "search" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("search")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
            backgroundColor:
              mode === "search" ? "var(--accent, #ffd166)" : "transparent",
            color:
              mode === "search" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          {t("addTorrent.indexerSearchTab", "🔍 Indexer Search")}
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "create" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("create")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
            backgroundColor:
              mode === "create" ? "var(--accent, #ffd166)" : "transparent",
            color:
              mode === "create" ? "#000000" : "var(--text-secondary, #c7c5d3)",
            border: "none",
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          {t("addTorrent.createTorrentTab", "⚡ Create Torrent")}
        </button>
      </div>

      {/* Mode 1: File Upload */}
      {mode === "file" && (
        <TorrentFileInputTab
          files={files}
          setFiles={setFiles}
          isModal={isModal}
        />
      )}

      {/* Mode 2: Magnet Link */}
      {mode === "magnet" && (
        <MagnetInputTab
          magnetLink={magnetLink}
          setMagnetLink={setMagnetLink}
          isModal={isModal}
        />
      )}

      {/* Mode 3: Indexer Search */}
      {mode === "search" && (
        <IndexerSearchTab
          initialQuery={initialQuery}
          selectedCategory={selectedCategory}
          savePath={savePath}
          isPaused={isPaused}
          isModal={isModal}
        />
      )}

      {/* Mode 4: Torrent Creator */}
      {mode === "create" && (
        <TorrentCreationTab isModal={isModal} onClose={onClose} />
      )}

      {/* Category & Download Options in File, Magnet and Search modes */}
      {mode !== "create" && (
        <div
          style={{
            display: "flex",
            flexWrap: "wrap",
            gap: "1rem",
            alignItems: "center",
            marginTop: "1rem",
            paddingTop: "1rem",
            borderTop: "1px solid var(--border-light)",
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
              {t("addTorrent.categoryLabel", "Category:")}
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
                border: "1px solid var(--border-light)",
              }}
            >
              <option value="">
                {categories && categories.length > 0
                  ? t("addTorrent.noCategory", "(None)")
                  : t(
                      "addTorrent.noCategoriesConfigured",
                      "(No categories configured)",
                    )}
              </option>
              {categories?.map((c) => (
                <option key={c.id} value={c.name}>
                  {c.name}
                  {c.savePath ? ` (${c.savePath})` : ""}
                  {c.isDefault
                    ? ` [${t("addTorrent.defaultBadge", "Default")}]`
                    : ""}
                </option>
              ))}
            </select>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <label
              htmlFor="torrentSavePathInput"
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary, #c7c5d3)",
              }}
            >
              Save Path:
            </label>
            <input
              id="torrentSavePathInput"
              type="text"
              value={savePath}
              onChange={(e) => setSavePath(e.target.value)}
              placeholder="Default download directory"
              className="form-input"
              style={{
                padding: "0.3rem 0.6rem",
                fontSize: "0.85rem",
                borderRadius: "4px",
                backgroundColor: "var(--bg-primary, #10111a)",
                color: "inherit",
                border: "1px solid var(--border-light, #1c203b)",
                minWidth: "200px",
              }}
            />
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <label
              htmlFor="torrentDlLimitInput"
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary, #c7c5d3)",
              }}
            >
              {t("tags.downloadLimit", "Download Limit (KB/s)")}:
            </label>
            <input
              id="torrentDlLimitInput"
              type="number"
              min={0}
              placeholder="0 (unlimited)"
              value={downloadLimit}
              onChange={(e) =>
                setDownloadLimit(
                  e.target.value === ""
                    ? ""
                    : Math.max(0, parseInt(e.target.value, 10) || 0),
                )
              }
              className="form-input"
              style={{
                padding: "0.3rem 0.6rem",
                fontSize: "0.85rem",
                borderRadius: "4px",
                backgroundColor: "var(--bg-primary, #10111a)",
                color: "inherit",
                border: "1px solid var(--border-light, #1c203b)",
                width: "110px",
              }}
            />
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <label
              htmlFor="torrentUpLimitInput"
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary, #c7c5d3)",
              }}
            >
              {t("tags.uploadLimit", "Upload Limit (KB/s)")}:
            </label>
            <input
              id="torrentUpLimitInput"
              type="number"
              min={0}
              placeholder="0 (unlimited)"
              value={uploadLimit}
              onChange={(e) =>
                setUploadLimit(
                  e.target.value === ""
                    ? ""
                    : Math.max(0, parseInt(e.target.value, 10) || 0),
                )
              }
              className="form-input"
              style={{
                padding: "0.3rem 0.6rem",
                fontSize: "0.85rem",
                borderRadius: "4px",
                backgroundColor: "var(--bg-primary, #10111a)",
                color: "inherit",
                border: "1px solid var(--border-light, #1c203b)",
                width: "110px",
              }}
            />
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
              {t("addTorrent.startPaused", "Start in paused state")}
            </label>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
            <input
              type="checkbox"
              id="sequentialDownloadCheckbox"
              checked={sequentialDownload}
              onChange={(e) => setSequentialDownload(e.target.checked)}
            />
            <label
              htmlFor="sequentialDownloadCheckbox"
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary, #c7c5d3)",
                cursor: "pointer",
              }}
            >
              {t("torrents.detail.sequentialDownload", "Sequential Download")}
            </label>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
            <input
              type="checkbox"
              id="firstLastPiecePriorityCheckbox"
              checked={firstLastPiecePriority}
              onChange={(e) => setFirstLastPiecePriority(e.target.checked)}
            />
            <label
              htmlFor="firstLastPiecePriorityCheckbox"
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary, #c7c5d3)",
                cursor: "pointer",
              }}
            >
              {t(
                "quickSettings.sequentialPiecePickingTitle",
                "Prioritize first and last pieces",
              )}
            </label>
          </div>

          {tags && tags.length > 0 && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                width: "100%",
                flexWrap: "wrap",
                paddingTop: "0.25rem",
              }}
            >
              <span
                style={{
                  fontSize: "0.85rem",
                  color: "var(--text-secondary, #c7c5d3)",
                }}
              >
                {t("tags.title", "Tags")}:
              </span>
              <div style={{ display: "flex", flexWrap: "wrap", gap: "0.4rem" }}>
                {tags.map((tag) => {
                  const isSelected = selectedTagIds.includes(tag.id);
                  const tagColor = tag.color || "var(--accent, #ffd166)";
                  return (
                    <button
                      key={tag.id}
                      type="button"
                      onClick={() =>
                        setSelectedTagIds((prev) =>
                          isSelected
                            ? prev.filter((id) => id !== tag.id)
                            : [...prev, tag.id],
                        )
                      }
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.35rem",
                        padding: "0.2rem 0.55rem",
                        borderRadius: "4px",
                        fontSize: "0.8rem",
                        cursor: "pointer",
                        border: isSelected
                          ? `1px solid ${tagColor}`
                          : "1px solid var(--border-light, #1c203b)",
                        backgroundColor: isSelected
                          ? tag.color
                            ? `${tag.color}33`
                            : "rgba(255, 209, 102, 0.2)"
                          : "rgba(255, 255, 255, 0.04)",
                        color: isSelected
                          ? tagColor
                          : "var(--text-secondary, #c7c5d3)",
                      }}
                    >
                      <span
                        style={{
                          width: "7px",
                          height: "7px",
                          borderRadius: "50%",
                          backgroundColor: tagColor,
                          display: "inline-block",
                        }}
                      />
                      <span>{tag.label}</span>
                      {isSelected && (
                        <span
                          style={{ fontSize: "0.75rem", fontWeight: "bold" }}
                        >
                          ✓
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
            </div>
          )}
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
              : t("addTorrent.failedToAddTorrent", "Failed to add torrent")
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
              {t("common.cancel", "Cancel")}
            </button>
          )}
          <button
            type="button"
            className="btn btn-success"
            onClick={handleSubmit}
            disabled={!canSubmit || addTorrent.isPending}
            style={{ borderRadius: "6px", padding: "0.45rem 1.25rem" }}
          >
            {addTorrent.isPending
              ? t("addTorrent.addingTorrent", "Adding...")
              : t("addTorrent.addTorrentBtn", "Add Torrent")}
          </button>
        </div>
      )}
    </div>
  );
}

export default AddTorrentForm;
