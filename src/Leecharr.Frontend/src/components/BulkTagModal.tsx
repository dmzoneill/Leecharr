import React, { useState, useEffect, useRef, useMemo } from "react";
import { Tag } from "../api/types";
import { useTranslation } from "../i18n";
import { useModalRegistration } from "./ModalProvider";
import { TagIcon } from "./icons/NavIcons";

export interface BulkTagModalProps {
  isOpen: boolean;
  mode: "add" | "remove";
  selectedCount: number;
  tags: Tag[];
  isPending?: boolean;
  onClose: () => void;
  onConfirm: (tagIds: number[]) => Promise<void> | void;
}

export function BulkTagModal({
  isOpen,
  mode,
  selectedCount,
  tags,
  isPending = false,
  onClose,
  onConfirm,
}: BulkTagModalProps) {
  const { t } = useTranslation();
  const modalRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [selectedTagIds, setSelectedTagIds] = useState<Set<number>>(new Set());
  const [searchTerm, setSearchTerm] = useState("");

  useModalRegistration({
    id: "bulk-tag-modal",
    isOpen,
    onClose: () => {
      if (!isPending) {
        onClose();
      }
    },
    modalRef,
  });

  useEffect(() => {
    if (isOpen) {
      setSelectedTagIds(new Set());
      setSearchTerm("");
      const timer = setTimeout(() => {
        searchInputRef.current?.focus();
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  const filteredTags = useMemo(() => {
    const q = searchTerm.trim().toLowerCase();
    if (!q) return tags;
    return tags.filter((tag) => tag.label.toLowerCase().includes(q));
  }, [tags, searchTerm]);

  if (!isOpen) {
    return null;
  }

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget && !isPending) {
      onClose();
    }
  };

  const handleToggleTag = (id: number) => {
    if (isPending) return;
    setSelectedTagIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const handleSelectAll = () => {
    if (isPending) return;
    setSelectedTagIds(new Set(filteredTags.map((t) => t.id)));
  };

  const handleClearAll = () => {
    if (isPending) return;
    setSelectedTagIds(new Set());
  };

  const handleConfirm = async () => {
    if (isPending || selectedTagIds.size === 0) return;
    await onConfirm(Array.from(selectedTagIds));
  };

  const isAdd = mode === "add";
  const title = isAdd
    ? t("torrents.bulkAddTagsTitle", { defaultValue: "Assign Tags" })
    : t("torrents.bulkRemoveTagsTitle", { defaultValue: "Remove Tags" });

  const subtitle = isAdd
    ? t("torrents.bulkAddTagsDesc", {
        count: selectedCount,
        defaultValue: `Assign selected tags to ${selectedCount} torrent(s)`,
      })
    : t("torrents.bulkRemoveTagsDesc", {
        count: selectedCount,
        defaultValue: `Remove selected tags from ${selectedCount} torrent(s)`,
      });

  return (
    <div
      ref={modalRef}
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="bulk-tag-modal-title"
    >
      <div
        className="modal"
        style={{
          maxWidth: "480px",
          width: "90%",
          maxHeight: "85vh",
          display: "flex",
          flexDirection: "column",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ padding: "1.25rem 1.5rem 0.75rem" }}>
          <h3
            id="bulk-tag-modal-title"
            className="modal-title"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              marginBottom: "0.25rem",
              fontSize: "1.15rem",
            }}
          >
            <TagIcon size={18} />
            <span>{title}</span>
          </h3>
          <p
            style={{
              margin: 0,
              fontSize: "0.85rem",
              color: "var(--text-muted, #888)",
            }}
          >
            {subtitle}
          </p>
        </div>

        <div
          style={{
            padding: "0 1.5rem 1rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
            flex: 1,
            overflow: "hidden",
          }}
        >
          {tags.length === 0 ? (
            <div
              style={{
                padding: "1.5rem",
                textAlign: "center",
                color: "var(--text-muted, #888)",
                fontSize: "0.9rem",
              }}
            >
              {t(
                "tags.noTags",
                { defaultValue: "No tags available. Please create tags first in Settings > Tags." },
              )}
            </div>
          ) : (
            <>
              <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                <input
                  ref={searchInputRef}
                  type="text"
                  className="search-input"
                  placeholder={t("common.filter", { defaultValue: "Filter tags..." })}
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  disabled={isPending}
                  style={{
                    flex: 1,
                    padding: "0.4rem 0.6rem",
                    fontSize: "0.85rem",
                    borderRadius: "4px",
                    border: "1px solid var(--border-color, #333)",
                    background: "var(--bg-input, rgba(255, 255, 255, 0.05))",
                    color: "inherit",
                  }}
                />
                <button
                  type="button"
                  className="btn btn-small btn-outline"
                  onClick={handleSelectAll}
                  disabled={isPending || filteredTags.length === 0}
                  style={{ fontSize: "0.75rem", padding: "0.35rem 0.5rem" }}
                >
                  {t("common.selectAll", { defaultValue: "Select All" })}
                </button>
                <button
                  type="button"
                  className="btn btn-small btn-outline"
                  onClick={handleClearAll}
                  disabled={isPending || selectedTagIds.size === 0}
                  style={{ fontSize: "0.75rem", padding: "0.35rem 0.5rem" }}
                >
                  {t("common.clear", { defaultValue: "Clear" })}
                </button>
              </div>

              <div
                style={{
                  flex: 1,
                  overflowY: "auto",
                  maxHeight: "260px",
                  border: "1px solid var(--border-color, rgba(255, 255, 255, 0.1))",
                  borderRadius: "6px",
                  padding: "0.5rem",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.25rem",
                }}
              >
                {filteredTags.length === 0 ? (
                  <div
                    style={{
                      padding: "1rem",
                      textAlign: "center",
                      color: "var(--text-muted, #888)",
                      fontSize: "0.85rem",
                    }}
                  >
                    {t("common.noResults", { defaultValue: "No matching tags" })}
                  </div>
                ) : (
                  filteredTags.map((tag) => {
                    const isChecked = selectedTagIds.has(tag.id);
                    const tagColor = tag.color || "var(--primary, #3b82f6)";
                    return (
                      <label
                        key={tag.id}
                        htmlFor={`bulk-tag-item-${tag.id}`}
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.6rem",
                          padding: "0.4rem 0.6rem",
                          borderRadius: "4px",
                          cursor: isPending ? "not-allowed" : "pointer",
                          backgroundColor: isChecked
                            ? "var(--bg-selected, rgba(59, 130, 246, 0.15))"
                            : "transparent",
                          transition: "background-color 0.15s ease",
                        }}
                      >
                        <input
                          id={`bulk-tag-item-${tag.id}`}
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => handleToggleTag(tag.id)}
                          disabled={isPending}
                          style={{ cursor: "inherit" }}
                        />
                        <span
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.35rem",
                            fontSize: "0.85rem",
                            padding: "0.15rem 0.5rem",
                            borderRadius: "4px",
                            backgroundColor: tag.color
                              ? `${tag.color}22`
                              : "rgba(59, 130, 246, 0.15)",
                            color: tagColor,
                            border: `1px solid ${tag.color ? `${tag.color}44` : "rgba(59, 130, 246, 0.3)"}`,
                            fontWeight: 500,
                          }}
                        >
                          <span
                            style={{
                              width: "8px",
                              height: "8px",
                              borderRadius: "50%",
                              backgroundColor: tagColor,
                              display: "inline-block",
                            }}
                          />
                          {tag.label}
                        </span>
                      </label>
                    );
                  })
                )}
              </div>
            </>
          )}
        </div>

        <div
          className="modal-actions"
          style={{
            padding: "0.75rem 1.5rem 1.25rem",
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.75rem",
            borderTop: "1px solid var(--border-color, rgba(255, 255, 255, 0.1))",
          }}
        >
          <button
            type="button"
            className="btn btn-outline"
            onClick={onClose}
            disabled={isPending}
          >
            {t("common.cancel", { defaultValue: "Cancel" })}
          </button>
          <button
            type="button"
            className={isAdd ? "btn btn-primary" : "btn btn-danger"}
            onClick={handleConfirm}
            disabled={isPending || selectedTagIds.size === 0}
          >
            {isPending
              ? t("common.saving", { defaultValue: "Saving..." })
              : isAdd
                ? `${t("torrents.assignTags", { defaultValue: "Assign" })} (${selectedTagIds.size})`
                : `${t("torrents.removeTags", { defaultValue: "Remove" })} (${selectedTagIds.size})`}
          </button>
        </div>
      </div>
    </div>
  );
}

export default BulkTagModal;
