import { useState, useMemo } from "react";
import {
  useTags,
  useCreateTag,
  useUpdateTag,
  useDeleteTag,
  useTorrents,
  useCategories,
} from "../api/hooks";
import type { Tag, Torrent } from "../api/types";

const COLOR_PRESETS = [
  "#3b82f6", // Blue
  "#10b981", // Emerald
  "#f59e0b", // Amber
  "#ef4444", // Red
  "#8b5cf6", // Purple
  "#ec4899", // Pink
  "#06b6d4", // Cyan
  "#6366f1", // Indigo
];

function formatTime(seconds?: number): string {
  if (!seconds || seconds <= 0) return "";
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`;
  if (seconds < 86400) return `${(seconds / 3600).toFixed(1)}h`;
  return `${(seconds / 86400).toFixed(1)}d`;
}

export function calculateTagUsageCounts(
  torrents?: Torrent[] | null,
  selectedCategory: string = "All",
  tags?: Tag[] | null,
): Record<number, number> {
  const counts: Record<number, number> = {};
  if (torrents) {
    for (const t of torrents) {
      if (selectedCategory !== "All") {
        const cat = t.category?.trim() || "Uncategorized";
        if (cat !== selectedCategory) continue;
      }
      const ids = t.tagIds ?? (t as any).tags ?? [];
      for (const id of ids) {
        counts[id] = (counts[id] ?? 0) + 1;
      }
      // Also support legacy t.label match if tag.label matches, for backward compatibility
      if (t.label && tags) {
        const labels = t.label.split(",").map((s) => s.trim().toLowerCase());
        for (const tag of tags) {
          if (
            labels.includes(tag.label.toLowerCase()) &&
            !ids.includes(tag.id)
          ) {
            counts[tag.id] = (counts[tag.id] ?? 0) + 1;
          }
        }
      }
    }
  }
  return counts;
}

function Tags() {
  const { data: tags, isLoading, isError } = useTags();
  const { data: torrents } = useTorrents();
  const { data: categories } = useCategories();
  const createTag = useCreateTag();
  const updateTag = useUpdateTag();
  const deleteTag = useDeleteTag();

  const [modalTag, setModalTag] = useState<Partial<Tag> | null>(null);
  const [deletingTag, setDeletingTag] = useState<Tag | null>(null);
  const [selectedCategory, setSelectedCategory] = useState<string>("All");

  const tagList = tags ?? [];

  const tagUsageCounts = useMemo(() => {
    return calculateTagUsageCounts(torrents, selectedCategory, tags);
  }, [torrents, selectedCategory, tags]);

  const getTagUsageCount = (tag: Tag) => {
    if (
      selectedCategory === "All" &&
      !torrents &&
      tag.torrentCount !== undefined
    ) {
      return tag.torrentCount;
    }
    return tagUsageCounts[tag.id] ?? 0;
  };

  function handleDelete(id: number) {
    const target = tagList.find((t) => t.id === id);
    if (target) {
      setDeletingTag(target);
    }
  }

  function handleSaveModal() {
    if (!modalTag || !modalTag.label?.trim()) return;

    const trimmedLabel = modalTag.label.trim();
    const payload: Partial<Tag> = {
      ...modalTag,
      label: trimmedLabel,
      color: modalTag.color?.trim() || undefined,
      uploadLimitKbps: modalTag.uploadLimitKbps
        ? Number(modalTag.uploadLimitKbps)
        : undefined,
      downloadLimitKbps: modalTag.downloadLimitKbps
        ? Number(modalTag.downloadLimitKbps)
        : undefined,
      minSeedRatio: modalTag.minSeedRatio
        ? Number(modalTag.minSeedRatio)
        : undefined,
      minSeedTimeSeconds: modalTag.minSeedTimeSeconds
        ? Number(modalTag.minSeedTimeSeconds)
        : undefined,
    };

    if (payload.id) {
      updateTag.mutate(payload as Tag, {
        onSuccess: () => setModalTag(null),
      });
    } else {
      createTag.mutate(payload, {
        onSuccess: () => setModalTag(null),
      });
    }
  }

  function confirmDeleteTag() {
    if (!deletingTag) return;
    const deletedId = deletingTag.id;
    const deletedLabel = deletingTag.label;
    deleteTag.mutate(deletedId, {
      onSuccess: () => {
        if (typeof window !== "undefined") {
          window.dispatchEvent(
            new CustomEvent("leecharr:tag-deleted", {
              detail: { id: deletedId, label: deletedLabel },
            }),
          );
          window.dispatchEvent(
            new CustomEvent("seedarr:tag-deleted", {
              detail: { id: deletedId, label: deletedLabel },
            }),
          );
        }
        setDeletingTag(null);
      },
    });
  }

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
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
            <span>🏷️</span> Tags ({tagList.length})
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Organize and filter torrent swarms by custom labels, colors, and
            seeding policies
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          {categories && categories.length > 0 && (
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
            >
              <label
                htmlFor="category-filter"
                style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}
              >
                Category:
              </label>
              <select
                id="category-filter"
                className="form-input"
                style={{
                  padding: "0.35rem 0.6rem",
                  fontSize: "0.85rem",
                  height: "auto",
                }}
                value={selectedCategory}
                onChange={(e) => setSelectedCategory(e.target.value)}
              >
                <option value="All">All Categories</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.name}>
                    {c.name}
                  </option>
                ))}
                <option value="Uncategorized">Uncategorized</option>
              </select>
            </div>
          )}
          <button
            className="btn btn-primary"
            onClick={() => setModalTag({ label: "", color: "#3b82f6" })}
          >
            + Add Tag
          </button>
        </div>
      </div>

      <div
        className="card"
        style={{
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: "1px solid var(--border-light)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        {isLoading ? (
          <p className="loading" style={{ padding: "1.5rem" }}>
            Loading tags...
          </p>
        ) : isError ? (
          <p className="error" style={{ padding: "1.5rem" }}>
            Failed to load tags.
          </p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Tag Label</th>
                  <th className="torrent-table-th">Seeding Policies</th>
                  <th className="torrent-table-th">Assigned Torrents</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {tagList.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="torrent-table-empty">
                      No tags defined yet. Click &quot;+ Add Tag&quot; to create
                      one.
                    </td>
                  </tr>
                ) : (
                  tagList.map((tag) => (
                    <tr key={tag.id} className="torrent-table-row">
                      <td>
                        <span
                          className={`badge ${!tag.color ? "badge-primary" : ""}`}
                          style={{
                            fontSize: "0.82rem",
                            padding: "0.25rem 0.65rem",
                            backgroundColor: tag.color || undefined,
                            borderColor: tag.color || undefined,
                            color: tag.color ? "#fff" : undefined,
                            fontWeight: 600,
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.35rem",
                          }}
                        >
                          🏷️ {tag.label}
                        </span>
                      </td>
                      <td>
                        <div
                          style={{
                            display: "flex",
                            gap: "0.4rem",
                            flexWrap: "wrap",
                            fontSize: "0.8rem",
                          }}
                        >
                          {tag.uploadLimitKbps ? (
                            <span
                              className="badge badge-secondary"
                              title="Upload Limit"
                            >
                              ↑ {tag.uploadLimitKbps} KB/s
                            </span>
                          ) : null}
                          {tag.downloadLimitKbps ? (
                            <span
                              className="badge badge-secondary"
                              title="Download Limit"
                            >
                              ↓ {tag.downloadLimitKbps} KB/s
                            </span>
                          ) : null}
                          {tag.minSeedRatio ? (
                            <span
                              className="badge badge-secondary"
                              title="Min Seed Ratio"
                            >
                              Ratio: {tag.minSeedRatio}
                            </span>
                          ) : null}
                          {tag.minSeedTimeSeconds ? (
                            <span
                              className="badge badge-secondary"
                              title="Min Seed Time"
                            >
                              Time: {formatTime(tag.minSeedTimeSeconds)}
                            </span>
                          ) : null}
                          {!tag.uploadLimitKbps &&
                            !tag.downloadLimitKbps &&
                            !tag.minSeedRatio &&
                            !tag.minSeedTimeSeconds && (
                              <span
                                style={{
                                  color: "var(--text-muted)",
                                  fontStyle: "italic",
                                }}
                              >
                                Default
                              </span>
                            )}
                        </div>
                      </td>
                      <td>
                        <span style={{ fontWeight: 600 }}>
                          {getTagUsageCount(tag)}
                        </span>{" "}
                        <span
                          style={{
                            color: "var(--text-muted)",
                            fontSize: "0.8rem",
                          }}
                        >
                          {selectedCategory !== "All"
                            ? `torrents (${selectedCategory})`
                            : "torrents"}
                        </span>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <div style={{ display: "inline-flex", gap: "0.5rem" }}>
                          <button
                            className="btn btn-outline btn-small"
                            onClick={() => setModalTag({ ...tag })}
                          >
                            Edit
                          </button>
                          <button
                            className="btn btn-danger btn-small"
                            onClick={() => handleDelete(tag.id)}
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Add / Edit Tag Modal */}
      {modalTag && (
        <div className="modal-overlay" onClick={() => setModalTag(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 520,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <h3
              className="modal-title"
              style={{ fontSize: "1.15rem", marginBottom: "1rem" }}
            >
              {modalTag.id ? `Edit Tag: "${modalTag.label}"` : "Add Tag"}
            </h3>

            {/* Label Input */}
            <div style={{ marginBottom: "1rem" }}>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  marginBottom: "0.35rem",
                }}
              >
                Tag Label *
              </label>
              <input
                type="text"
                className="form-input"
                placeholder="e.g. 4k-hdr, seedbox, ptp"
                value={modalTag.label || ""}
                onChange={(e) =>
                  setModalTag({ ...modalTag, label: e.target.value })
                }
                autoFocus
              />
            </div>

            {/* Color Customization */}
            <div style={{ marginBottom: "1.25rem" }}>
              <label
                style={{
                  display: "block",
                  fontSize: "0.85rem",
                  fontWeight: 600,
                  marginBottom: "0.35rem",
                }}
              >
                Color Customization
              </label>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  marginBottom: "0.5rem",
                }}
              >
                <input
                  type="color"
                  value={modalTag.color || "#3b82f6"}
                  onChange={(e) =>
                    setModalTag({ ...modalTag, color: e.target.value })
                  }
                  style={{
                    width: "36px",
                    height: "36px",
                    padding: "0",
                    border: "1px solid var(--border-light)",
                    borderRadius: "4px",
                    cursor: "pointer",
                    backgroundColor: "transparent",
                  }}
                />
                <input
                  type="text"
                  className="form-input"
                  placeholder="#3b82f6"
                  value={modalTag.color || ""}
                  onChange={(e) =>
                    setModalTag({ ...modalTag, color: e.target.value })
                  }
                  style={{ width: "110px" }}
                />
                {modalTag.color && (
                  <button
                    type="button"
                    className="btn btn-outline btn-small"
                    onClick={() =>
                      setModalTag({ ...modalTag, color: undefined })
                    }
                    title="Clear color"
                  >
                    Clear
                  </button>
                )}
                {/* Live chip preview */}
                <span
                  className={`badge ${!modalTag.color ? "badge-primary" : ""}`}
                  style={{
                    marginLeft: "auto",
                    backgroundColor: modalTag.color || undefined,
                    borderColor: modalTag.color || undefined,
                    color: modalTag.color ? "#fff" : undefined,
                    fontSize: "0.82rem",
                    padding: "0.25rem 0.65rem",
                  }}
                >
                  🏷️ {modalTag.label?.trim() || "Preview"}
                </span>
              </div>
              <div
                style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}
              >
                <span
                  style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}
                >
                  Presets:
                </span>
                {COLOR_PRESETS.map((preset) => (
                  <button
                    key={preset}
                    type="button"
                    onClick={() => setModalTag({ ...modalTag, color: preset })}
                    style={{
                      width: "20px",
                      height: "20px",
                      borderRadius: "50%",
                      backgroundColor: preset,
                      border:
                        modalTag.color === preset
                          ? "2px solid #fff"
                          : "1px solid transparent",
                      cursor: "pointer",
                      padding: 0,
                    }}
                    title={preset}
                  />
                ))}
              </div>
            </div>

            {/* Bandwidth Limits */}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr",
                gap: "0.75rem",
                marginBottom: "1rem",
              }}
            >
              <div>
                <label
                  style={{
                    display: "block",
                    fontSize: "0.85rem",
                    fontWeight: 600,
                    marginBottom: "0.35rem",
                  }}
                >
                  Upload Limit (KB/s)
                </label>
                <input
                  type="number"
                  className="form-input"
                  placeholder="Unlimited"
                  min="0"
                  value={modalTag.uploadLimitKbps ?? ""}
                  onChange={(e) =>
                    setModalTag({
                      ...modalTag,
                      uploadLimitKbps: e.target.value
                        ? Number(e.target.value)
                        : undefined,
                    })
                  }
                />
              </div>
              <div>
                <label
                  style={{
                    display: "block",
                    fontSize: "0.85rem",
                    fontWeight: 600,
                    marginBottom: "0.35rem",
                  }}
                >
                  Download Limit (KB/s)
                </label>
                <input
                  type="number"
                  className="form-input"
                  placeholder="Unlimited"
                  min="0"
                  value={modalTag.downloadLimitKbps ?? ""}
                  onChange={(e) =>
                    setModalTag({
                      ...modalTag,
                      downloadLimitKbps: e.target.value
                        ? Number(e.target.value)
                        : undefined,
                    })
                  }
                />
              </div>
            </div>

            {/* Seeding Policy Goals */}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr",
                gap: "0.75rem",
                marginBottom: "1.5rem",
              }}
            >
              <div>
                <label
                  style={{
                    display: "block",
                    fontSize: "0.85rem",
                    fontWeight: 600,
                    marginBottom: "0.35rem",
                  }}
                >
                  Min Seed Ratio
                </label>
                <input
                  type="number"
                  className="form-input"
                  placeholder="e.g. 2.0"
                  step="0.1"
                  min="0"
                  value={modalTag.minSeedRatio ?? ""}
                  onChange={(e) =>
                    setModalTag({
                      ...modalTag,
                      minSeedRatio: e.target.value
                        ? Number(e.target.value)
                        : undefined,
                    })
                  }
                />
              </div>
              <div>
                <label
                  style={{
                    display: "block",
                    fontSize: "0.85rem",
                    fontWeight: 600,
                    marginBottom: "0.35rem",
                  }}
                >
                  Min Seed Time (Seconds)
                </label>
                <input
                  type="number"
                  className="form-input"
                  placeholder="e.g. 86400"
                  min="0"
                  value={modalTag.minSeedTimeSeconds ?? ""}
                  onChange={(e) =>
                    setModalTag({
                      ...modalTag,
                      minSeedTimeSeconds: e.target.value
                        ? Number(e.target.value)
                        : undefined,
                    })
                  }
                />
              </div>
            </div>

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setModalTag(null)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSaveModal}
                disabled={
                  !modalTag.label?.trim() ||
                  createTag.isPending ||
                  updateTag.isPending
                }
              >
                {createTag.isPending || updateTag.isPending
                  ? "Saving..."
                  : "Save Tag"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {deletingTag && (
        <div className="modal-overlay" onClick={() => setDeletingTag(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 480,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <h3
              className="modal-title"
              style={{ fontSize: "1.15rem", marginBottom: "0.75rem" }}
            >
              Delete Tag: &quot;{deletingTag.label}&quot;
            </h3>

            <div
              style={{
                padding: "0.75rem 1rem",
                borderRadius: "6px",
                backgroundColor: "rgba(220, 53, 69, 0.15)",
                border: "1px solid rgba(220, 53, 69, 0.35)",
                color: "var(--danger, #dc3545)",
                fontSize: "0.85rem",
                marginBottom: "1rem",
                lineHeight: 1.4,
              }}
            >
              ⚠️ Warning: Deleting this tag will remove it from all assigned
              torrents, indexers, and automated rules. This action cannot be
              undone.
            </div>

            <p
              style={{
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                marginBottom: "1.25rem",
                lineHeight: 1.5,
              }}
            >
              Are you sure you want to delete tag{" "}
              <strong>{deletingTag.label}</strong>?
              {getTagUsageCount(deletingTag) ? (
                <>
                  <br />
                  Currently assigned to{" "}
                  <strong>{getTagUsageCount(deletingTag)}</strong>{" "}
                  {getTagUsageCount(deletingTag) === 1 ? "torrent" : "torrents"}
                  .
                </>
              ) : (
                <>
                  <br />
                  This tag is not currently assigned to any torrents.
                </>
              )}
            </p>

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                className="btn btn-outline btn-small"
                onClick={() => setDeletingTag(null)}
                disabled={deleteTag.isPending}
              >
                Cancel
              </button>
              <button
                className="btn btn-danger btn-small"
                onClick={confirmDeleteTag}
                disabled={deleteTag.isPending}
              >
                {deleteTag.isPending ? "Deleting..." : "Delete Tag"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default Tags;
