import { useState, useRef, useCallback } from "react";
import { useModalRegistration } from "./ModalProvider";
import { usePermissions } from "../hooks/usePermissions";
import { useToast } from "../context/ToastContext";
import { formatBytes, formatDate } from "../utils/formatters";
import { useTranslation } from "../i18n";
import { UploadIcon } from "./icons/UIIcons";
import type { PackageImportResult } from "../api/types";

export interface ImportPackageModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: () => void;
}

export function ImportPackageModal({
  isOpen,
  onClose,
  onSuccess,
}: ImportPackageModalProps) {
  const { t } = useTranslation();
  const { showToast } = useToast();
  const { canAddTorrent, canMutateTorrents } = usePermissions();
  const modalRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const [destinationPath, setDestinationPath] = useState("");
  const [sourcePrefix, setSourcePrefix] = useState("");
  const [destinationPrefix, setDestinationPrefix] = useState("");
  const [showAdvancedMapping, setShowAdvancedMapping] = useState(false);

  const [isUploading, setIsUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [importResult, setImportResult] = useState<PackageImportResult | null>(
    null,
  );

  useModalRegistration({
    id: "import-package-modal",
    isOpen,
    onClose: () => {
      if (!isUploading) onClose();
    },
    modalRef,
  });

  const resetForm = useCallback(() => {
    setSelectedFile(null);
    setUploadProgress(0);
    setStatusMessage(null);
    setErrorMessage(null);
    setImportResult(null);
    setIsUploading(false);
  }, []);

  const handleFileSelect = (file: File) => {
    const lowerName = file.name.toLowerCase();
    const isPackage =
      lowerName.endsWith(".tar.gz") ||
      lowerName.endsWith(".leecharr") ||
      lowerName.endsWith(".seedarr") ||
      lowerName.endsWith(".tar") ||
      lowerName.endsWith(".tgz") ||
      lowerName.endsWith(".gz");

    if (!isPackage) {
      showToast(
        "Please select a valid package archive (.leecharr or .tar.gz)",
        "info",
      );
    }

    setSelectedFile(file);
    setErrorMessage(null);
    setImportResult(null);
  };

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      handleFileSelect(e.dataTransfer.files[0]);
    }
  }, []);

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      handleFileSelect(e.target.files[0]);
    }
    e.target.value = "";
  };

  const handleImport = () => {
    if (!selectedFile) return;

    setIsUploading(true);
    setUploadProgress(0);
    setStatusMessage("Uploading package archive...");
    setErrorMessage(null);
    setImportResult(null);

    const formData = new FormData();
    formData.append("file", selectedFile);
    if (destinationPath.trim()) {
      formData.append("destinationPath", destinationPath.trim());
    }
    if (sourcePrefix.trim()) {
      formData.append("sourcePrefix", sourcePrefix.trim());
    }
    if (destinationPrefix.trim()) {
      formData.append("destinationPrefix", destinationPrefix.trim());
    }

    const xhr = new XMLHttpRequest();

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        const percent = Math.round((event.loaded / event.total) * 100);
        setUploadProgress(percent);
        if (percent === 100) {
          setStatusMessage(
            "Extracting package contents & verifying torrent payloads...",
          );
        }
      }
    };

    xhr.onload = () => {
      setIsUploading(false);
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          const res: PackageImportResult = JSON.parse(xhr.responseText);
          setImportResult(res);
          setStatusMessage("Package imported successfully!");
          showToast(
            res.message ||
              `Successfully imported ${res.importedTorrentsCount} torrent(s)`,
            "success",
          );
          if (onSuccess) onSuccess();
        } catch {
          setErrorMessage("Failed to parse import response from server.");
        }
      } else {
        try {
          const err = JSON.parse(xhr.responseText);
          setErrorMessage(
            err.message || `Import failed with HTTP ${xhr.status}`,
          );
        } catch {
          setErrorMessage(
            xhr.responseText || `Import failed with HTTP ${xhr.status}`,
          );
        }
      }
    };

    xhr.onerror = () => {
      setIsUploading(false);
      setErrorMessage(
        "Network error occurred while uploading package archive.",
      );
    };

    const queryParams = new URLSearchParams();
    if (destinationPath.trim())
      queryParams.append("destinationPath", destinationPath.trim());
    if (sourcePrefix.trim())
      queryParams.append("sourcePrefix", sourcePrefix.trim());
    if (destinationPrefix.trim())
      queryParams.append("destinationPrefix", destinationPrefix.trim());

    const queryString = queryParams.toString();
    xhr.open(
      "POST",
      `/api/v1/packages/import${queryString ? `?${queryString}` : ""}`,
    );
    xhr.send(formData);
  };

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget && !isUploading) {
      onClose();
    }
  };

  if (!isOpen) return null;

  const canImport = canAddTorrent || canMutateTorrents;

  return (
    <div
      ref={modalRef}
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="import-package-modal-title"
    >
      <div
        className="modal"
        style={{
          maxWidth: "680px",
          width: "92%",
          borderRadius: "8px",
          padding: "1.5rem",
          boxShadow:
            "0 12px 40px rgba(0, 0, 0, 0.6), 0 2px 8px rgba(0, 0, 0, 0.3)",
          border: "1px solid var(--border-light, rgba(255, 255, 255, 0.15))",
          maxHeight: "90vh",
          display: "flex",
          flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1.25rem",
            flexShrink: 0,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
            <span style={{ fontSize: "1.3rem" }}>📦</span>
            <h2
              id="import-package-modal-title"
              className="modal-title"
              style={{ margin: 0, fontSize: "1.2rem" }}
            >
              {t("torrents.importPackage", undefined, "Import Package")}
            </h2>
          </div>
          <button
            type="button"
            className="btn btn-outline"
            style={{
              padding: "0.2rem 0.5rem",
              fontSize: "0.85rem",
              borderRadius: "4px",
            }}
            onClick={onClose}
            disabled={isUploading}
            title="Close dialog"
            aria-label="Close dialog"
          >
            ✕
          </button>
        </div>

        {!canImport ? (
          <div style={{ padding: "1rem 0" }}>
            <div
              role="alert"
              style={{
                padding: "0.75rem 1rem",
                backgroundColor: "rgba(239, 68, 68, 0.1)",
                border: "1px solid rgba(239, 68, 68, 0.3)",
                borderRadius: "6px",
                color: "#fca5a5",
                fontSize: "0.875rem",
              }}
            >
              🔒 You have ReadOnly permissions. Importing packages is not
              permitted.
            </div>
            <div
              style={{
                marginTop: "1rem",
                display: "flex",
                justifyContent: "flex-end",
              }}
            >
              <button className="btn btn-outline" onClick={onClose}>
                Close
              </button>
            </div>
          </div>
        ) : importResult ? (
          /* Results View */
          <div
            style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
          >
            <div
              style={{
                padding: "0.75rem 1rem",
                backgroundColor: "rgba(34, 197, 94, 0.1)",
                border: "1px solid rgba(34, 197, 94, 0.3)",
                borderRadius: "6px",
                color: "#86efac",
                fontSize: "0.875rem",
              }}
            >
              ✓ {importResult.message || `Successfully processed package.`}
            </div>

            <div
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted, #94a3b8)",
              }}
            >
              Extracted files: {importResult.extractedFiles.length} (
              {formatBytes(importResult.totalBytesExtracted)})
            </div>

            {importResult.torrents.length > 0 && (
              <div
                style={{
                  border:
                    "1px solid var(--border-light, rgba(255, 255, 255, 0.1))",
                  borderRadius: "6px",
                  overflow: "hidden",
                  maxHeight: "220px",
                  overflowY: "auto",
                }}
              >
                <table
                  style={{
                    width: "100%",
                    borderCollapse: "collapse",
                    fontSize: "0.82rem",
                  }}
                >
                  <thead>
                    <tr
                      style={{
                        backgroundColor: "rgba(255, 255, 255, 0.05)",
                        borderBottom:
                          "1px solid var(--border-light, rgba(255, 255, 255, 0.1))",
                        textAlign: "left",
                      }}
                    >
                      <th style={{ padding: "0.4rem 0.6rem" }}>Torrent</th>
                      <th style={{ padding: "0.4rem 0.6rem" }}>Size</th>
                      <th style={{ padding: "0.4rem 0.6rem" }}>Category</th>
                      <th style={{ padding: "0.4rem 0.6rem" }}>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {importResult.torrents.map((t) => (
                      <tr
                        key={t.infoHash}
                        style={{
                          borderBottom:
                            "1px solid var(--border-light, rgba(255, 255, 255, 0.05))",
                        }}
                      >
                        <td
                          style={{ padding: "0.4rem 0.6rem", fontWeight: 500 }}
                        >
                          {t.name}
                          {t.isDuplicate && (
                            <span
                              style={{
                                marginLeft: "6px",
                                fontSize: "0.7rem",
                                padding: "2px 5px",
                                borderRadius: "4px",
                                backgroundColor: "rgba(234, 179, 8, 0.2)",
                                color: "#fde047",
                              }}
                            >
                              Duplicate
                            </span>
                          )}
                        </td>
                        <td
                          style={{
                            padding: "0.4rem 0.6rem",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {formatBytes(t.totalSize)}
                        </td>
                        <td style={{ padding: "0.4rem 0.6rem" }}>
                          {t.category || "—"}
                        </td>
                        <td
                          style={{
                            padding: "0.4rem 0.6rem",
                            whiteSpace: "nowrap",
                          }}
                        >
                          <span
                            style={{
                              padding: "2px 6px",
                              borderRadius: "4px",
                              fontSize: "0.75rem",
                              backgroundColor:
                                t.status === "Seeding"
                                  ? "rgba(34, 197, 94, 0.2)"
                                  : "rgba(56, 189, 248, 0.2)",
                              color:
                                t.status === "Seeding" ? "#86efac" : "#7dd3fc",
                            }}
                          >
                            {t.status === "Seeding"
                              ? "Seeding (Verified)"
                              : t.status || "Imported"}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
                marginTop: "1rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline"
                onClick={resetForm}
              >
                Import Another Package
              </button>
              <button
                type="button"
                className="btn btn-success"
                onClick={onClose}
              >
                Done
              </button>
            </div>
          </div>
        ) : (
          /* Upload & Configuration View */
          <div
            style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
          >
            {/* Drag & Drop Area */}
            <div
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
              style={{
                border: isDragOver
                  ? "2px dashed var(--accent, #38bdf8)"
                  : "2px dashed var(--border-light, rgba(255, 255, 255, 0.2))",
                borderRadius: "8px",
                padding: "1.5rem",
                textAlign: "center",
                cursor: isUploading ? "not-allowed" : "pointer",
                backgroundColor: isDragOver
                  ? "rgba(56, 189, 248, 0.08)"
                  : "rgba(255, 255, 255, 0.02)",
                transition: "all 0.2s ease-in-out",
              }}
            >
              <input
                ref={fileInputRef}
                type="file"
                accept=".tar.gz,.leecharr,.seedarr,.tar,.gz,.tgz,application/gzip,application/x-tar,application/x-gzip"
                style={{ display: "none" }}
                onChange={handleInputChange}
                disabled={isUploading}
              />
              <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>📦</div>
              <p
                style={{
                  margin: "0 0 0.25rem 0",
                  fontWeight: 500,
                  fontSize: "0.95rem",
                }}
              >
                {selectedFile
                  ? selectedFile.name
                  : "Drag and drop package archive here (.leecharr, .tar.gz)"}
              </p>
              <p
                style={{
                  margin: 0,
                  fontSize: "0.8rem",
                  color: "var(--text-muted, #94a3b8)",
                }}
              >
                {selectedFile
                  ? `${formatBytes(selectedFile.size)} • Modified ${formatDate(new Date(selectedFile.lastModified).toISOString())}`
                  : "or click to select file from your computer"}
              </p>
            </div>

            {/* Path Destination & Remapping Inputs */}
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
                backgroundColor: "rgba(255, 255, 255, 0.02)",
                padding: "0.85rem 1rem",
                borderRadius: "6px",
                border:
                  "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
              }}
            >
              <div>
                <label
                  htmlFor="import-package-destination-path"
                  style={{
                    display: "block",
                    fontSize: "0.82rem",
                    fontWeight: 600,
                    marginBottom: "0.3rem",
                  }}
                >
                  Destination Directory (Root)
                </label>
                <input
                  id="import-package-destination-path"
                  type="text"
                  className="search-input"
                  style={{ width: "100%", boxSizing: "border-box" }}
                  placeholder="e.g. /downloads or leave blank for default library root"
                  value={destinationPath}
                  onChange={(e) => setDestinationPath(e.target.value)}
                  disabled={isUploading}
                />
                <span
                  style={{
                    fontSize: "0.74rem",
                    color: "var(--text-muted, #94a3b8)",
                    display: "block",
                    marginTop: "2px",
                  }}
                >
                  Payload files and torrent save path will be placed in this
                  folder.
                </span>
              </div>

              {/* Collapsible Advanced Path Remapping */}
              <div>
                <button
                  type="button"
                  onClick={() => setShowAdvancedMapping(!showAdvancedMapping)}
                  style={{
                    background: "none",
                    border: "none",
                    color: "var(--accent, #38bdf8)",
                    fontSize: "0.8rem",
                    padding: 0,
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    gap: "4px",
                  }}
                >
                  <span>{showAdvancedMapping ? "▼" : "▶"}</span>
                  <span>Cross-Platform Path Remapping (Windows ↔ Linux)</span>
                </button>

                {showAdvancedMapping && (
                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns: "1fr 1fr",
                      gap: "0.6rem",
                      marginTop: "0.5rem",
                    }}
                  >
                    <div>
                      <label
                        htmlFor="import-package-source-prefix"
                        style={{
                          display: "block",
                          fontSize: "0.78rem",
                          fontWeight: 500,
                          marginBottom: "0.2rem",
                        }}
                      >
                        Source Prefix
                      </label>
                      <input
                        id="import-package-source-prefix"
                        type="text"
                        className="search-input"
                        style={{
                          width: "100%",
                          boxSizing: "border-box",
                          fontSize: "0.8rem",
                        }}
                        placeholder="e.g. C:\Torrents"
                        value={sourcePrefix}
                        onChange={(e) => setSourcePrefix(e.target.value)}
                        disabled={isUploading}
                      />
                    </div>
                    <div>
                      <label
                        htmlFor="import-package-destination-prefix"
                        style={{
                          display: "block",
                          fontSize: "0.78rem",
                          fontWeight: 500,
                          marginBottom: "0.2rem",
                        }}
                      >
                        Destination Prefix
                      </label>
                      <input
                        id="import-package-destination-prefix"
                        type="text"
                        className="search-input"
                        style={{
                          width: "100%",
                          boxSizing: "border-box",
                          fontSize: "0.8rem",
                        }}
                        placeholder="e.g. /downloads/complete"
                        value={destinationPrefix}
                        onChange={(e) => setDestinationPrefix(e.target.value)}
                        disabled={isUploading}
                      />
                    </div>
                  </div>
                )}
              </div>
            </div>

            {/* Error Message Alert */}
            {errorMessage && (
              <div
                role="alert"
                style={{
                  padding: "0.65rem 0.9rem",
                  backgroundColor: "rgba(239, 68, 68, 0.12)",
                  border: "1px solid rgba(239, 68, 68, 0.35)",
                  borderRadius: "6px",
                  color: "#fca5a5",
                  fontSize: "0.82rem",
                  display: "flex",
                  alignItems: "center",
                  gap: "6px",
                }}
              >
                <span>⚠️</span>
                <span>{errorMessage}</span>
              </div>
            )}

            {/* Upload Progress & Extraction Status Indicator */}
            {isUploading && (
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.4rem",
                  marginTop: "0.25rem",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontSize: "0.8rem",
                    color: "var(--text-muted, #94a3b8)",
                  }}
                >
                  <span>{statusMessage || "Uploading package..."}</span>
                  <span>{uploadProgress}%</span>
                </div>
                <div
                  style={{
                    height: "8px",
                    width: "100%",
                    backgroundColor: "rgba(255, 255, 255, 0.1)",
                    borderRadius: "4px",
                    overflow: "hidden",
                  }}
                >
                  <div
                    style={{
                      height: "100%",
                      width: `${uploadProgress}%`,
                      backgroundColor: "var(--accent, #38bdf8)",
                      transition: "width 0.2s ease-in-out",
                    }}
                  />
                </div>
              </div>
            )}

            {/* Actions */}
            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
                marginTop: "0.75rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline"
                onClick={onClose}
                disabled={isUploading}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-success"
                onClick={handleImport}
                disabled={!selectedFile || isUploading}
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                }}
              >
                <UploadIcon size={14} />
                <span>{isUploading ? "Importing..." : "Import Package"}</span>
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default ImportPackageModal;
