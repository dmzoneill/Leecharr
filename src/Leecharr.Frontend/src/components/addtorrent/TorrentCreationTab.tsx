import React, { useState } from "react";
import { useTranslation } from "../../i18n";
import { formatBytes } from "../../utils/formatters";
import { useToast } from "../../context/ToastContext";
import { api } from "../../api/client";
import type { TorrentCreationResult } from "../../api/types";

export interface TorrentCreationTabProps {
  isModal?: boolean;
  onClose?: () => void;
}

export function TorrentCreationTab({
  isModal = false,
  onClose,
}: TorrentCreationTabProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();

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
  const [createResult, setCreateResult] =
    useState<TorrentCreationResult | null>(null);

  const handleCreateTorrent = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createPath.trim()) {
      showToast(
        t(
          "addTorrent.sourcePathRequired",
          "Source path is required to create a torrent",
        ),
        "error",
      );
      return;
    }

    try {
      setIsCreating(true);
      setCreateResult(null);

      const trackersList = createTrackers
        .split("\n")
        .map((tr) => tr.trim())
        .filter((tr) => tr.length > 0);

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
        showToast(
          t(
            "addTorrent.torrentCreatedSuccess",
            "Torrent created successfully!",
          ),
          "success",
        );
      } else {
        showToast(
          res.errorMessage ||
            t("addTorrent.failedToCreateTorrent", "Failed to create torrent"),
          "error",
        );
      }
    } catch (err: any) {
      showToast(
        err?.message ||
          t("addTorrent.failedToCreateTorrent", "Failed to create torrent"),
        "error",
      );
    } finally {
      setIsCreating(false);
    }
  };

  return (
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
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: "1rem",
        }}
      >
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
            {t(
              "addTorrent.sourcePathLabel",
              "Source Path (File or Directory) *",
            )}
          </label>
          <input
            type="text"
            value={createPath}
            onChange={(e) => setCreatePath(e.target.value)}
            placeholder={t(
              "addTorrent.sourcePathPlaceholder",
              "/downloads/complete/MyMovie or /data/file.iso",
            )}
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
            {t("addTorrent.torrentNameLabel", "Torrent Name (Optional)")}
          </label>
          <input
            type="text"
            value={createName}
            onChange={(e) => setCreateName(e.target.value)}
            placeholder={t(
              "addTorrent.torrentNamePlaceholder",
              "Defaults to file / folder name",
            )}
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

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: "1rem",
        }}
      >
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
            {t("addTorrent.pieceSizeLabel", "Piece Size")}
          </label>
          <select
            value={createPieceLength}
            onChange={(e) =>
              setCreatePieceLength(parseInt(e.target.value, 10))
            }
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
            <option value={0}>
              {t(
                "addTorrent.pieceSizeAuto",
                "Auto (Optimal Size based on content)",
              )}
            </option>
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
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            paddingTop: "1.2rem",
          }}
        >
          <input
            type="checkbox"
            id="createPrivateCheck"
            checked={createIsPrivate}
            onChange={(e) => setCreateIsPrivate(e.target.checked)}
          />
          <label
            htmlFor="createPrivateCheck"
            style={{
              fontSize: "0.85rem",
              color: "var(--text-secondary)",
              cursor: "pointer",
            }}
          >
            <strong>
              {t("addTorrent.privateTorrentLabel", "Private Torrent")}
            </strong>{" "}
            ({t("torrents.privateBep27", "BEP 27 - Disables DHT & PEX")})
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
          {t(
            "addTorrent.trackersLabelWithTiers",
            "Tracker URLs (One per line, tiers separated by empty line)",
          )}
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
          {t("addTorrent.webSeedsLabel", "Web Seed URLs (One per line)")}
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

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: "1rem",
        }}
      >
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
            {t("addTorrent.commentLabel", "Comment")}
          </label>
          <input
            type="text"
            value={createComment}
            onChange={(e) => setCreateComment(e.target.value)}
            placeholder={t(
              "addTorrent.commentPlaceholder",
              "Optional description or license info",
            )}
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
            {t("addTorrent.outputPathLabel", "Output .torrent Save Path")}
          </label>
          <input
            type="text"
            value={createOutputPath}
            onChange={(e) => setCreateOutputPath(e.target.value)}
            placeholder={t(
              "addTorrent.outputPathPlaceholder",
              "Optional destination for .torrent file",
            )}
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
              <div
                style={{
                  fontWeight: 700,
                  color: "#10b981",
                  marginBottom: "0.25rem",
                }}
              >
                {t(
                  "addTorrent.torrentCreatedSuccess",
                  "✓ Torrent file created successfully!",
                )}
              </div>
              <div>
                <strong>
                  {t("addTorrent.infoHashLabel", "Info Hash:")}
                </strong>{" "}
                <code style={{ wordBreak: "break-all" }}>
                  {createResult.infoHash}
                </code>
              </div>
              <div>
                <strong>
                  {t("addTorrent.totalSizeLabel", "Total Size:")}
                </strong>{" "}
                {formatBytes(createResult.totalSize)} (
                {createResult.pieceCount} pieces @{" "}
                {formatBytes(createResult.pieceLength)})
              </div>
              {createResult.outputPath && (
                <div style={{ marginTop: "0.25rem" }}>
                  <strong>
                    {t("addTorrent.savedToLabel", "Saved To:")}
                  </strong>{" "}
                  <code>{createResult.outputPath}</code>
                </div>
              )}
            </div>
          ) : (
            <div style={{ color: "#ef4444" }}>
              <strong>
                {t("addTorrent.creationErrorLabel", "Creation Error:")}
              </strong>{" "}
              {createResult.errorMessage}
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
            {t("common.cancel", "Cancel")}
          </button>
        )}
        <button
          type="button"
          className="btn btn-primary"
          onClick={handleCreateTorrent}
          disabled={isCreating || !createPath.trim()}
          style={{ borderRadius: "6px", padding: "0.45rem 1.25rem" }}
        >
          {isCreating
            ? t("addTorrent.creatingTorrent", "Hashing & Creating...")
            : t("addTorrent.createTorrentBtn", "⚡ Create .torrent")}
        </button>
      </div>
    </div>
  );
}

export default TorrentCreationTab;
