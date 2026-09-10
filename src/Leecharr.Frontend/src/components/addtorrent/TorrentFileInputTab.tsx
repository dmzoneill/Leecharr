import React, { useRef, useCallback, useState } from "react";
import { useTranslation } from "../../i18n";
import { formatBytes } from "../../utils/formatters";

export interface TorrentFileInputTabProps {
  files: File[];
  setFiles: React.Dispatch<React.SetStateAction<File[]>>;
  isModal?: boolean;
}

export function TorrentFileInputTab({
  files,
  setFiles,
  isModal = false,
}: TorrentFileInputTabProps) {
  const { t } = useTranslation();
  const [isDragOver, setIsDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const addFiles = useCallback(
    (incoming: FileList | File[]) => {
      const torrentFiles = Array.from(incoming).filter((f) =>
        f.name.endsWith(".torrent"),
      );
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
    },
    [setFiles],
  );

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
    [addFiles],
  );

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      addFiles(e.target.files);
    }
    e.target.value = "";
  };

  return (
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
                ? t("addTorrent.oneFileSelected", {
                    name: files[0].name,
                    defaultValue: `${files[0].name} selected`,
                  })
                : t("addTorrent.multipleFilesSelected", {
                    count: files.length,
                    defaultValue: `${files.length} torrent files selected`,
                  })}
            </span>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted, #7e8092)",
                marginTop: "0.25rem",
              }}
            >
              {t(
                "addTorrent.dragMoreFilesPrompt",
                "Click or drag more files to add",
              )}
            </div>
          </div>
        ) : (
          <div>
            <div style={{ fontWeight: 500, fontSize: "1rem" }}>
              {t(
                "addTorrent.dropFilesPrompt",
                "Drop .torrent files here or click to browse",
              )}
            </div>
            <div
              style={{
                fontSize: "0.82rem",
                color: "var(--text-muted, #7e8092)",
                marginTop: "0.35rem",
              }}
            >
              {t(
                "addTorrent.supportsMultiple",
                "Supports multiple .torrent files simultaneously",
              )}
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
            {t("addTorrent.selectedFilesCount", {
              count: files.length,
              defaultValue: `Selected Files (${files.length})`,
            })}
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
                  title={t("addTorrent.removeFileTooltip", "Remove file")}
                >
                  ✕
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

export default TorrentFileInputTab;
