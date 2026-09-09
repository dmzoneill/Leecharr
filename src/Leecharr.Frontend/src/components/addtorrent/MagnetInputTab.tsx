import React, { useMemo } from "react";
import { useTranslation } from "../../i18n";

export interface MagnetInfo {
  name?: string;
  hash?: string;
  trackerCount: number;
}

export function parseMagnetPreview(uri: string): MagnetInfo | null {
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

export interface MagnetInputTabProps {
  magnetLink: string;
  setMagnetLink: (link: string) => void;
  isModal?: boolean;
}

export function MagnetInputTab({
  magnetLink,
  setMagnetLink,
  isModal = false,
}: MagnetInputTabProps) {
  const { t } = useTranslation();

  const isMagnetValid = magnetLink.trim().startsWith("magnet:?");
  const magnetPreview = useMemo(
    () => parseMagnetPreview(magnetLink),
    [magnetLink],
  );

  return (
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
            <span>🧲</span>{" "}
            {t("addTorrent.magnetUriLabel", "Magnet URI / Link")}
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
              title={t(
                "addTorrent.pasteClipboardTooltip",
                "Paste link from clipboard",
              )}
            >
              {t("addTorrent.pasteClipboard", "📋 Paste Clipboard")}
            </button>
            {magnetLink && (
              <button
                type="button"
                className="btn btn-outline btn-xs"
                onClick={() => setMagnetLink("")}
                style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                title={t("addTorrent.clearInputTooltip", "Clear input")}
              >
                {t("addTorrent.clearInput", "✕ Clear")}
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
              magnetLink.trim() && isMagnetValid
                ? "0 0 0 1px rgba(34, 197, 94, 0.2)"
                : "none",
            transition: "all 0.2s ease",
          }}
        >
          <textarea
            className="form-control"
            placeholder={t(
              "addTorrent.magnetPlaceholder",
              "magnet:?xt=urn:btih:...",
            )}
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
              fontFamily:
                "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace",
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
            {t(
              "addTorrent.validMagnetDescription",
              "Paste any valid BitTorrent v1 or v2 magnet link.",
            )}
          </span>
          {magnetLink.trim() && (
            <span
              style={{
                color: isMagnetValid
                  ? "var(--success, #22c55e)"
                  : "var(--danger, #ef4444)",
                fontWeight: 600,
              }}
            >
              {isMagnetValid
                ? t("addTorrent.validMagnetFormat", "✓ Valid Magnet Format")
                : t(
                    "addTorrent.invalidMagnetFormat",
                    "✗ Must start with magnet:?",
                  )}
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
                  {t("addTorrent.nameLabel", "Name:")}
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
                  {t("addTorrent.infoHashLabel", "Info Hash:")}
                </span>
                <span style={{ fontFamily: "monospace", color: "#60a5fa" }}>
                  {magnetPreview.hash}
                </span>
              </div>
            )}
            {magnetPreview?.trackerCount !== undefined &&
              magnetPreview.trackerCount > 0 && (
                <div style={{ display: "flex", gap: "0.5rem" }}>
                  <span
                    style={{
                      color: "var(--text-muted, #7e8092)",
                      minWidth: "75px",
                    }}
                  >
                    {t("addTorrent.trackersLabel", "Trackers:")}
                  </span>
                  <span style={{ color: "#4ade80" }}>
                    {t("addTorrent.bundledTrackers", {
                      count: magnetPreview.trackerCount,
                      defaultValue: `${magnetPreview.trackerCount} bundled tracker(s)`,
                    })}
                  </span>
                </div>
              )}
          </div>
        )}
      </div>
    </div>
  );
}

export default MagnetInputTab;
