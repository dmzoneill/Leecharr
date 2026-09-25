import { useState, useEffect } from "react";
import { useFileListing, useCreateDirectory } from "../api/hooks";
import { useFocusTrap } from "../hooks/useFocusTrap";
import { useModalRegistration } from "./ModalProvider";
import { useToast } from "../context/ToastContext";
import { useTranslation } from "../i18n";

export interface FolderBrowserModalProps {
  isOpen: boolean;
  initialPath?: string;
  title?: string;
  onSelect: (selectedPath: string) => void;
  onClose: () => void;
}

export function FolderBrowserModal({
  isOpen,
  initialPath = "/downloads",
  title = "Select Folder",
  onSelect,
  onClose,
}: FolderBrowserModalProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();
  const [currentPath, setCurrentPath] = useState<string>(
    initialPath || "/downloads",
  );
  const [newFolderName, setNewFolderName] = useState("");
  const [showNewFolderInput, setShowNewFolderInput] = useState(false);

  useEffect(() => {
    if (isOpen) {
      setCurrentPath(initialPath || "/downloads");
      setShowNewFolderInput(false);
      setNewFolderName("");
    }
  }, [isOpen, initialPath]);

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onClose,
  });

  useModalRegistration({
    id: "folder-browser-modal",
    isOpen,
    onClose,
    modalRef: trapRef,
  });

  const {
    data: listing,
    isLoading,
    isError,
    refetch,
  } = useFileListing(currentPath || undefined);
  const mkdirMutation = useCreateDirectory();

  const handleNavigateUp = () => {
    if (listing?.parent && listing.parent !== listing.path) {
      setCurrentPath(listing.parent);
    } else {
      const parts = currentPath.split("/").filter(Boolean);
      if (parts.length > 1) {
        parts.pop();
        setCurrentPath("/" + parts.join("/"));
      } else {
        setCurrentPath("/");
      }
    }
  };

  const handleNavigateInto = (path: string) => {
    setCurrentPath(path);
  };

  const handleCreateFolder = async () => {
    if (!newFolderName.trim()) return;
    const folderName = newFolderName.trim();
    const target =
      currentPath === "/" || currentPath.endsWith("/")
        ? `${currentPath}${folderName}`
        : `${currentPath}/${folderName}`;
    try {
      await mkdirMutation.mutateAsync(target);
      showToast(`Created folder "${folderName}"`, "success");
      setNewFolderName("");
      setShowNewFolderInput(false);
      refetch();
    } catch (err: unknown) {
      showToast((err as Error)?.message || "Failed to create folder", "error");
    }
  };

  const handleConfirmSelect = () => {
    onSelect(currentPath);
    onClose();
  };

  if (!isOpen) return null;

  const directories = (listing?.entries || []).filter((e) => e.isDirectory);

  return (
    <div
      className="modal-overlay"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      style={{
        position: "fixed",
        inset: 0,
        backgroundColor: "rgba(10, 11, 20, 0.82)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 10000,
        padding: "1.5rem",
      }}
    >
      <div
        ref={trapRef}
        className="modal-content"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: "580px",
          backgroundColor: "var(--bg-card, #171b35)",
          borderRadius: "12px",
          border: "1px solid rgba(255, 209, 102, 0.35)",
          boxShadow: "0 24px 60px rgba(0, 0, 0, 0.75)",
          overflow: "hidden",
          display: "flex",
          flexDirection: "column",
          maxHeight: "80vh",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "0.85rem 1.25rem",
            backgroundColor: "var(--bg-primary, #10111a)",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span style={{ fontSize: "1.2rem" }}>📁</span>
            <h3
              style={{
                margin: 0,
                fontSize: "1.05rem",
                fontWeight: 600,
                color: "var(--text-primary, #f8f4ed)",
              }}
            >
              {title}
            </h3>
          </div>

          <button
            type="button"
            className="btn btn-outline btn-small"
            onClick={onClose}
            aria-label={t(
              "folderBrowser.close",
              undefined,
              "Close folder browser",
            )}
            title={t("folderBrowser.close", undefined, "Close folder browser")}
            style={{ padding: "0.2rem 0.5rem", fontSize: "0.8rem" }}
          >
            ✕
          </button>
        </div>

        {/* Path bar and action controls */}
        <div
          style={{
            padding: "0.75rem 1.25rem",
            backgroundColor: "var(--bg-secondary, #171b35)",
            borderBottom: "1px solid var(--border-light)",
            display: "flex",
            flexDirection: "column",
            gap: "0.5rem",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={handleNavigateUp}
              disabled={!currentPath || currentPath === "/"}
              title={t("folderBrowser.goToParent", "Go to parent directory")}
              style={{
                padding: "0.3rem 0.6rem",
                fontSize: "0.8rem",
                flexShrink: 0,
              }}
            >
              ⬆ {t("folderBrowser.up", "Up")}
            </button>

            <div
              style={{
                flex: 1,
                fontFamily: "monospace",
                fontSize: "0.85rem",
                padding: "0.35rem 0.6rem",
                backgroundColor: "var(--bg-primary, #10111a)",
                borderRadius: "6px",
                border: "1px solid var(--border-light)",
                color: "var(--accent, #ffd166)",
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
              }}
            >
              {currentPath || "/"}
            </div>

            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={() => setShowNewFolderInput((prev) => !prev)}
              title={t("folderBrowser.createNewFolder", "Create new folder")}
              style={{
                padding: "0.3rem 0.6rem",
                fontSize: "0.8rem",
                flexShrink: 0,
              }}
            >
              + {t("folderBrowser.newFolder", "New Folder")}
            </button>
          </div>

          {showNewFolderInput && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                marginTop: "0.25rem",
              }}
            >
              <input
                type="text"
                className="form-input"
                value={newFolderName}
                onChange={(e) => setNewFolderName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") handleCreateFolder();
                }}
                placeholder={t(
                  "folderBrowser.folderNamePlaceholder",
                  "Folder name...",
                )}
                style={{
                  flex: 1,
                  padding: "0.3rem 0.6rem",
                  fontSize: "0.85rem",
                }}
                autoFocus
              />
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleCreateFolder}
                disabled={!newFolderName.trim() || mkdirMutation.isPending}
                style={{ padding: "0.3rem 0.6rem", fontSize: "0.8rem" }}
              >
                {t("folderBrowser.create", "Create")}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => {
                  setShowNewFolderInput(false);
                  setNewFolderName("");
                }}
                style={{ padding: "0.3rem 0.6rem", fontSize: "0.8rem" }}
              >
                {t("folderBrowser.cancel", "Cancel")}
              </button>
            </div>
          )}
        </div>

        {/* Directory List View */}
        <div
          style={{
            flex: 1,
            overflowY: "auto",
            padding: "0.5rem 1.25rem",
            minHeight: "220px",
            maxHeight: "360px",
          }}
        >
          {isLoading ? (
            <div
              style={{
                padding: "2.5rem 1rem",
                textAlign: "center",
                color: "var(--text-muted)",
                fontSize: "0.85rem",
              }}
            >
              {t("folderBrowser.loading", "Loading directory listing...")}
            </div>
          ) : isError ? (
            <div
              style={{
                padding: "2rem 1rem",
                textAlign: "center",
                color: "var(--danger, #ef4444)",
                fontSize: "0.85rem",
              }}
            >
              {t("folderBrowser.failedToLoad", "Failed to load directory.")}
            </div>
          ) : directories.length === 0 ? (
            <div
              style={{
                padding: "2.5rem 1rem",
                textAlign: "center",
                color: "var(--text-muted)",
                fontSize: "0.85rem",
              }}
            >
              <div style={{ fontSize: "1.6rem", marginBottom: "0.35rem" }}>
                📂
              </div>
              {t(
                "folderBrowser.noSubfolders",
                "No subfolders in this directory.",
              )}
            </div>
          ) : (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.25rem",
              }}
            >
              {directories.map((dir) => (
                <div
                  key={dir.path}
                  onClick={() => handleNavigateInto(dir.path)}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "0.5rem 0.75rem",
                    borderRadius: "6px",
                    cursor: "pointer",
                    backgroundColor: "rgba(255, 255, 255, 0.03)",
                    border: "1px solid transparent",
                    transition: "all 0.15s ease",
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.backgroundColor =
                      "rgba(255, 209, 102, 0.12)";
                    e.currentTarget.style.borderColor =
                      "rgba(255, 209, 102, 0.3)";
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.backgroundColor =
                      "rgba(255, 255, 255, 0.03)";
                    e.currentTarget.style.borderColor = "transparent";
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                      overflow: "hidden",
                    }}
                  >
                    <span>📁</span>
                    <span
                      style={{
                        fontSize: "0.85rem",
                        fontWeight: 500,
                        color: "var(--text-primary, #f8f4ed)",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {dir.name}
                    </span>
                  </div>

                  <span
                    style={{
                      fontSize: "0.75rem",
                      color: "var(--text-muted)",
                      fontFamily: "monospace",
                    }}
                  >
                    ▶
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Footer */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "0.75rem 1.25rem",
            backgroundColor: "var(--bg-primary, #10111a)",
            borderTop: "1px solid var(--border-light)",
          }}
        >
          <div
            style={{
              fontSize: "0.78rem",
              color: "var(--text-muted)",
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap",
              maxWidth: "280px",
            }}
          >
            {t("folderBrowser.selected", "Selected:")}{" "}
            <code style={{ color: "var(--accent)" }}>{currentPath}</code>
          </div>

          <div style={{ display: "flex", gap: "0.5rem" }}>
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={onClose}
            >
              {t("common.cancel", "Cancel")}
            </button>
            <button
              type="button"
              className="btn btn-primary btn-small"
              onClick={handleConfirmSelect}
            >
              {t("folderBrowser.selectThisFolder", "Select This Folder")}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export default FolderBrowserModal;
