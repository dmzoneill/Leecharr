import React, { useState, useEffect, useRef, useMemo } from "react";
import { Tag } from "../api/types";
import { useTranslation } from "../i18n";
import { useModalRegistration } from "./ModalProvider";
import { useFocusTrap } from "../hooks/useFocusTrap";
import { useCreateTag } from "../api/hooks";
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
  const createTagMutation = useCreateTag();
  const modalRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [selectedTagIds, setSelectedTagIds] = useState<Set<number>>(new Set());
  const [searchTerm, setSearchTerm] = useState("");
  const [isCreatingTag, setIsCreatingTag] = useState(false);

  const handleClose = () => {
    if (!isPending && !isCreatingTag) {
      onClose();
    }
  };

  useModalRegistration({
    id: "bulk-tag-modal",
    isOpen,
    onClose: handleClose,
    modalRef,
  });

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onClose: handleClose,
  });

  const setContainerRef = (el: HTMLDivElement | null) => {
    (modalRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
    (trapRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
  };

  useEffect(() => {
    if (isOpen) {
      setSelectedTagIds(new Set());
      setSearchTerm("");
      setIsCreatingTag(false);
      const timer = setTimeout(() => {
        searchInputRef.current?.focus();
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  const trimmedSearch = searchTerm.trim();

  const filteredTags = useMemo(() => {
    const q = trimmedSearch.toLowerCase();
    if (!q) return tags;
    return tags.filter((tag) => tag.label.toLowerCase().includes(q));
  }, [tags, trimmedSearch]);

  const hasExactMatch = useMemo(() => {
    const q = trimmedSearch.toLowerCase();
    if (!q) return false;
    return tags.some((tag) => tag.label.toLowerCase() === q);
  }, [tags, trimmedSearch]);

  if (!isOpen) {
    return null;
  }

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget && !isPending && !isCreatingTag) {
      onClose();
    }
  };

  const handleToggleTag = (id: number) => {
    if (isPending || isCreatingTag) return;
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
    if (isPending || isCreatingTag) return;
    setSelectedTagIds(new Set(filteredTags.map((t) => t.id)));
  };

  const handleClearAll = () => {
    if (isPending || isCreatingTag) return;
    setSelectedTagIds(new Set());
  };

  const handleQuickCreateTag = async () => {
    if (!trimmedSearch || hasExactMatch || isCreatingTag || isPending) return;
    setIsCreatingTag(true);
    try {
      const newTag = await createTagMutation.mutateAsync({
        label: trimmedSearch,
      });
      if (newTag?.id) {
        setSelectedTagIds((prev) => new Set(prev).add(newTag.id));
      }
      setSearchTerm("");
    } catch (err) {
      console.error("Failed to create tag shortcut:", err);
    } finally {
      setIsCreatingTag(false);
    }
  };

  const handleSearchKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (
      e.key === "Enter" &&
      mode === "add" &&
      trimmedSearch &&
      !hasExactMatch
    ) {
      e.preventDefault();
      handleQuickCreateTag();
    }
  };

  const handleConfirm = async () => {
    if (isPending || isCreatingTag || selectedTagIds.size === 0) return;
    await onConfirm(Array.from(selectedTagIds));
  };

  const isAdd = mode === "add";
  const title = isAdd
    ? t("torrents.bulkAddTagsTitle", undefined, "Assign Tags")
    : t("torrents.bulkRemoveTagsTitle", undefined, "Remove Tags");

  const subtitle = isAdd
    ? t(
        "torrents.bulkAddTagsDesc",
        { count: selectedCount },
        `Assign selected tags to ${selectedCount} torrent(s)`,
      )
    : t(
        "torrents.bulkRemoveTagsDesc",
        { count: selectedCount },
        `Remove selected tags from ${selectedCount} torrent(s)`,
      );

  return (
    <div
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="bulk-tag-modal-title"
    >
      <div
        ref={setContainerRef}
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
        {/* Header with Title and Close Button */}
        <div
          style={{
            padding: "1.25rem 1.5rem 0.75rem",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "flex-start",
          }}
        >
          <div>
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
          <button
            type="button"
            className="btn btn-outline btn-small"
            onClick={handleClose}
            disabled={isPending || isCreatingTag}
            aria-label={t("common.close", undefined, "Close")}
            title={t("common.close", undefined, "Close")}
            style={{
              padding: "0.2rem 0.5rem",
              fontSize: "0.8rem",
              marginLeft: "1rem",
            }}
          >
            ✕
          </button>
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
          {tags.length === 0 && !trimmedSearch ? (
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
                undefined,
                "No tags available. Please create tags first in Settings > Tags.",
              )}
            </div>
          ) : (
            <>
              <div
                style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}
              >
                <input
                  ref={searchInputRef}
                  type="text"
                  className="search-input"
                  placeholder={t("common.filter", undefined, "Filter tags...")}
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  onKeyDown={handleSearchKeyDown}
                  disabled={isPending || isCreatingTag}
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
                  disabled={
                    isPending || isCreatingTag || filteredTags.length === 0
                  }
                  style={{ fontSize: "0.75rem", padding: "0.35rem 0.5rem" }}
                >
                  {t("common.selectAll", undefined, "Select All")}
                </button>
                <button
                  type="button"
                  className="btn btn-small btn-outline"
                  onClick={handleClearAll}
                  disabled={
                    isPending || isCreatingTag || selectedTagIds.size === 0
                  }
                  style={{ fontSize: "0.75rem", padding: "0.35rem 0.5rem" }}
                >
                  {t("common.clear", undefined, "Clear")}
                </button>
              </div>

              {/* Tag Creation Shortcut when in Add Mode */}
              {isAdd && trimmedSearch && !hasExactMatch && (
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "0.4rem 0.65rem",
                    backgroundColor: "rgba(59, 130, 246, 0.1)",
                    border: "1px dashed var(--primary, #3b82f6)",
                    borderRadius: "6px",
                    gap: "0.5rem",
                  }}
                >
                  <div
                    style={{
                      fontSize: "0.82rem",
                      color: "var(--text-primary, #fff)",
                      display: "flex",
                      alignItems: "center",
                      gap: "0.35rem",
                    }}
                  >
                    <span>➕</span>
                    <span>
                      {t("tags.createTag", undefined, "Create tag")}:{" "}
                      <strong>"{trimmedSearch}"</strong>
                    </span>
                  </div>
                  <button
                    type="button"
                    className="btn btn-small btn-primary"
                    onClick={handleQuickCreateTag}
                    disabled={isCreatingTag || isPending}
                    style={{ fontSize: "0.75rem", padding: "0.25rem 0.55rem" }}
                  >
                    {isCreatingTag
                      ? t("common.saving", undefined, "Creating...")
                      : t("common.add", undefined, "Create & Select")}
                  </button>
                </div>
              )}

              <div
                style={{
                  flex: 1,
                  overflowY: "auto",
                  maxHeight: "260px",
                  border:
                    "1px solid var(--border-color, rgba(255, 255, 255, 0.1))",
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
                    {t("common.noResults", undefined, "No matching tags")}
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
                          cursor:
                            isPending || isCreatingTag
                              ? "not-allowed"
                              : "pointer",
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
                          disabled={isPending || isCreatingTag}
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
                            border: `1px solid ${
                              tag.color
                                ? `${tag.color}44`
                                : "rgba(59, 130, 246, 0.3)"
                            }`,
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

                        {/* Tag Count Badge */}
                        {typeof tag.torrentCount === "number" && (
                          <span
                            style={{
                              marginLeft: "auto",
                              fontSize: "0.75rem",
                              padding: "0.1rem 0.45rem",
                              borderRadius: "10px",
                              backgroundColor: "rgba(255, 255, 255, 0.08)",
                              color: "var(--text-muted, #aaa)",
                              fontWeight: 400,
                            }}
                            title={`${tag.torrentCount} torrent(s)`}
                          >
                            {tag.torrentCount}
                          </span>
                        )}
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
            borderTop:
              "1px solid var(--border-color, rgba(255, 255, 255, 0.1))",
          }}
        >
          <button
            type="button"
            className="btn btn-outline"
            onClick={handleClose}
            disabled={isPending || isCreatingTag}
          >
            {t("common.cancel", undefined, "Cancel")}
          </button>
          <button
            type="button"
            className={isAdd ? "btn btn-primary" : "btn btn-danger"}
            onClick={handleConfirm}
            disabled={isPending || isCreatingTag || selectedTagIds.size === 0}
          >
            {isPending
              ? t("common.saving", undefined, "Saving...")
              : isAdd
                ? `${t("torrents.assignTags", undefined, "Assign")} (${selectedTagIds.size})`
                : `${t("torrents.removeTags", undefined, "Remove")} (${selectedTagIds.size})`}
          </button>
        </div>
      </div>
    </div>
  );
}

export default BulkTagModal;
