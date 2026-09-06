import { useTranslation } from "../../i18n";
import React, { useState } from "react";
import {
  useCategories,
  useCreateCategory,
  useUpdateCategory,
  useDeleteCategory,
} from "../../api/hooks";
import type { Category } from "../../api/types";
import { useConfirm } from "../../context/ConfirmContext";
import { useToast } from "../../context/ToastContext";
import { useEscapeKey } from "../../hooks/useEscapeKey";
import { SectionCard, TextInput, NumberInput, Toggle } from "./shared";

interface CategorySettingsProps {
  embedded?: boolean;
}

export function CategorySettingsTab({
  embedded = false,
}: CategorySettingsProps) {
  const { t } = useTranslation();

  const { data: categories, isLoading } = useCategories();
  const createMutation = useCreateCategory();
  const updateMutation = useUpdateCategory();
  const deleteMutation = useDeleteCategory();

  const { showToast } = useToast();
  const confirm = useConfirm();

  const [editingCategory, setEditingCategory] =
    useState<Partial<Category> | null>(null);
  const [modalError, setModalError] = useState<string | null>(null);

  useEscapeKey(() => setEditingCategory(null), Boolean(editingCategory));

  const defaultCategoryForm: Partial<Category> = {
    name: "",
    savePath: "",
    defaultDownloadLimit: 0,
    defaultUploadLimit: 0,
    targetRatio: 0,
    targetSeedTimeMinutes: 0,
    autoStop: false,
    isDefault: false,
  };

  const handleOpenAdd = () => {
    setModalError(null);
    setEditingCategory({ ...defaultCategoryForm });
  };

  const handleOpenEdit = (cat: Category) => {
    setModalError(null);
    setEditingCategory({
      id: cat.id,
      name: cat.name,
      savePath: cat.savePath || "",
      defaultDownloadLimit: cat.defaultDownloadLimit || 0,
      defaultUploadLimit: cat.defaultUploadLimit || 0,
      targetRatio: cat.targetRatio || 0,
      targetSeedTimeMinutes: cat.targetSeedTimeMinutes || 0,
      autoStop: Boolean(cat.autoStop),
      isDefault: Boolean(cat.isDefault),
    });
  };

  const handleSave = () => {
    if (!editingCategory) return;

    const trimmedName = editingCategory.name?.trim();
    if (!trimmedName) {
      setModalError(t("settingsTabs.categories.nameRequired"));
      return;
    }

    setModalError(null);

    const payload: Partial<Category> = {
      name: trimmedName,
      savePath: editingCategory.savePath?.trim() || "",
      defaultDownloadLimit: Number(editingCategory.defaultDownloadLimit) || 0,
      defaultUploadLimit: Number(editingCategory.defaultUploadLimit) || 0,
      targetRatio: Number(editingCategory.targetRatio) || 0,
      targetSeedTimeMinutes: Number(editingCategory.targetSeedTimeMinutes) || 0,
      autoStop: Boolean(editingCategory.autoStop),
      isDefault: Boolean(editingCategory.isDefault),
    };

    if (editingCategory.id) {
      updateMutation.mutate(
        { id: editingCategory.id, data: payload },
        {
          onSuccess: (updated) => {
            showToast(
              `Category "${updated.name}" updated successfully`,
              "success",
            );
            setEditingCategory(null);
          },
          onError: (err: any) => {
            setModalError(
              err?.message || t("settingsTabs.categories.updateFailed"),
            );
          },
        },
      );
    } else {
      createMutation.mutate(payload, {
        onSuccess: (created) => {
          showToast(
            `Category "${created.name}" created successfully`,
            "success",
          );
          setEditingCategory(null);
        },
        onError: (err: any) => {
          setModalError(
            err?.message || t("settingsTabs.categories.createFailed"),
          );
        },
      });
    }
  };

  const handleDelete = async (cat: Category) => {
    const ok = await confirm({
      title: t("settingsTabs.categories.deleteTitle"),
      message: `Are you sure you want to delete the category "${cat.name}"? Existing torrent files and active swarms will not be removed.`,
      danger: true,
      confirmText: t("settingsTabs.categories.deleteConfirm"),
    });

    if (!ok) return;

    deleteMutation.mutate(cat.id, {
      onSuccess: () => {
        showToast(`Category "${cat.name}" deleted`, "info");
      },
      onError: (err: any) => {
        showToast(
          err?.message || t("settingsTabs.categories.deleteFailed"),
          "error",
        );
      },
    });
  };

  const isSaving = createMutation.isPending || updateMutation.isPending;

  return (
    <div id="category-settings-section">
      <SectionCard
        title={t("settingsTabs.categories.title")}
        description={t("settingsTabs.categories.description")}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
            {categories
              ? `${categories.length} configured ${categories.length === 1 ? "category" : "categories"}`
              : t("settingsTabs.categories.loading")}
          </div>

          <button
            type="button"
            className="btn btn-primary btn-small"
            onClick={handleOpenAdd}
            style={{ display: "flex", alignItems: "center", gap: "0.35rem" }}
          >
            <span>+</span>
            {t("settingsTabs.categories.addCategory")}
          </button>
        </div>

        {isLoading ? (
          <div className="loading" style={{ padding: "1.5rem 0" }}>
            {t("settingsTabs.categories.loadingCategories")}
          </div>
        ) : !categories || categories.length === 0 ? (
          <div
            style={{
              padding: "2.5rem 1.5rem",
              textAlign: "center",
              backgroundColor: "var(--bg-primary, #10111a)",
              borderRadius: "6px",
              border: "1px dashed var(--border-light, #1c203b)",
            }}
          >
            <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>🏷️</div>
            <h4 style={{ margin: "0 0 0.5rem", color: "var(--text-primary)" }}>
              {t("settingsTabs.categories.noCategories")}
            </h4>
            <p
              style={{
                margin: "0 auto 1.25rem",
                maxWidth: "460px",
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                lineHeight: 1.4,
              }}
            >
              Categories allow you to assign dedicated download directories
              (e.g. <code>/downloads/movies</code>, <code>/downloads/tv</code>),
              set speed limits, and manage torrent goals automatically.
            </p>
            <button
              type="button"
              className="btn btn-primary btn-small"
              onClick={handleOpenAdd}
            >
              + Create First Category
            </button>
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table
              className="table"
              style={{
                width: "100%",
                borderCollapse: "collapse",
                fontSize: "0.85rem",
              }}
            >
              <thead>
                <tr
                  style={{
                    borderBottom: "1px solid var(--border-light, #1c203b)",
                    textAlign: "left",
                    color: "var(--text-muted, #7e8092)",
                    fontSize: "0.8rem",
                  }}
                >
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.categories.table.name")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.categories.table.savePath")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.categories.table.maxDownload")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.categories.table.maxUpload")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.categories.table.targetRatio")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.categories.table.autoStop")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem", textAlign: "right" }}>
                    {t("settingsTabs.categories.table.actions")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {categories.map((cat) => (
                  <tr
                    key={cat.id}
                    style={{
                      borderBottom: "1px solid var(--border-light, #1c203b)",
                    }}
                  >
                    <td style={{ padding: "0.65rem 0.8rem", fontWeight: 600 }}>
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.5rem",
                        }}
                      >
                        <span>{cat.name}</span>
                        {cat.isDefault && (
                          <span
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.15rem 0.45rem",
                              borderRadius: "3px",
                              backgroundColor: "rgba(59, 130, 246, 0.2)",
                              color: "var(--primary, #3b82f6)",
                              border: "1px solid rgba(59, 130, 246, 0.3)",
                            }}
                          >
                            {t("settingsTabs.categories.table.default")}
                          </span>
                        )}
                      </div>
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.8rem",
                        fontFamily: "monospace",
                        color: cat.savePath
                          ? "var(--text-primary)"
                          : "var(--text-muted)",
                      }}
                    >
                      {cat.savePath ||
                        t("settingsTabs.categories.table.defaultStoragePath")}
                    </td>
                    <td style={{ padding: "0.65rem 0.8rem" }}>
                      {cat.defaultDownloadLimit
                        ? `${cat.defaultDownloadLimit} KB/s`
                        : t("settingsTabs.categories.table.unlimited")}
                    </td>
                    <td style={{ padding: "0.65rem 0.8rem" }}>
                      {cat.defaultUploadLimit
                        ? `${cat.defaultUploadLimit} KB/s`
                        : t("settingsTabs.categories.table.unlimited")}
                    </td>
                    <td style={{ padding: "0.65rem 0.8rem" }}>
                      {cat.targetRatio
                        ? `${cat.targetRatio}x`
                        : t("settingsTabs.categories.table.unlimited")}
                    </td>
                    <td style={{ padding: "0.65rem 0.8rem" }}>
                      {cat.autoStop ? (
                        <span
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--success, #28a745)",
                            fontWeight: 600,
                          }}
                        >
                          {t("settingsTabs.categories.table.enabled")}
                        </span>
                      ) : (
                        <span style={{ color: "var(--text-muted)" }}>
                          {t("settingsTabs.categories.table.disabled")}
                        </span>
                      )}
                    </td>
                    <td
                      style={{ padding: "0.65rem 0.8rem", textAlign: "right" }}
                    >
                      <div
                        style={{
                          display: "inline-flex",
                          gap: "0.4rem",
                          justifyContent: "flex-end",
                        }}
                      >
                        <button
                          type="button"
                          className="btn btn-outline btn-small"
                          onClick={() => handleOpenEdit(cat)}
                          title={`Edit ${cat.name}`}
                          style={{
                            padding: "0.2rem 0.5rem",
                            fontSize: "0.75rem",
                          }}
                        >
                          {t("settingsTabs.categories.table.edit")}
                        </button>
                        <button
                          type="button"
                          className="btn btn-danger btn-small"
                          onClick={() => handleDelete(cat)}
                          disabled={deleteMutation.isPending}
                          title={`Delete ${cat.name}`}
                          style={{
                            padding: "0.2rem 0.5rem",
                            fontSize: "0.75rem",
                          }}
                        >
                          {t("settingsTabs.categories.deleteConfirm")}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>

      {/* Add / Edit Category Modal */}
      {editingCategory && (
        <div className="modal-overlay" onClick={() => setEditingCategory(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 520,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid rgba(255, 255, 255, 0.12)",
            }}
          >
            <div
              className="modal-title"
              style={{
                fontSize: "1.2rem",
                marginBottom: "1rem",
                fontWeight: 600,
                color: "var(--text-primary)",
              }}
            >
              {editingCategory.id
                ? `Edit Category: ${editingCategory.name}`
                : t("settingsTabs.categories.modal.addTitle")}
            </div>

            <TextInput
              label={t("settingsTabs.categories.modal.nameLabel")}
              value={editingCategory.name || ""}
              onChange={(v) => {
                setEditingCategory({ ...editingCategory, name: v });
                setModalError(null);
              }}
              placeholder={t("settingsTabs.categories.modal.namePlaceholder")}
              hint={t("settingsTabs.categories.modal.nameHint")}
            />

            <TextInput
              label={t("settingsTabs.categories.modal.savePathLabel")}
              value={editingCategory.savePath || ""}
              onChange={(v) =>
                setEditingCategory({ ...editingCategory, savePath: v })
              }
              placeholder={t(
                "settingsTabs.categories.modal.savePathPlaceholder",
              )}
              hint={t("settingsTabs.categories.modal.savePathHint")}
            />

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: "1rem",
              }}
            >
              <NumberInput
                label={t("settingsTabs.categories.modal.maxDownloadLabel")}
                value={editingCategory.defaultDownloadLimit ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    defaultDownloadLimit: v,
                  })
                }
                min={0}
                suffix="KB/s"
                hint={t("settingsTabs.categories.modal.unlimitedHint")}
              />

              <NumberInput
                label={t("settingsTabs.categories.modal.maxUploadLabel")}
                value={editingCategory.defaultUploadLimit ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    defaultUploadLimit: v,
                  })
                }
                min={0}
                suffix="KB/s"
                hint={t("settingsTabs.categories.modal.unlimitedHint")}
              />
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: "1rem",
              }}
            >
              <NumberInput
                label={t("settingsTabs.categories.modal.targetRatioLabel")}
                value={editingCategory.targetRatio ?? 0}
                onChange={(v) =>
                  setEditingCategory({ ...editingCategory, targetRatio: v })
                }
                min={0}
                step={0.1}
                hint={t("settingsTabs.categories.modal.targetRatioHint")}
              />

              <NumberInput
                label={t("settingsTabs.categories.modal.targetSeedTimeLabel")}
                value={editingCategory.targetSeedTimeMinutes ?? 0}
                onChange={(v) =>
                  setEditingCategory({
                    ...editingCategory,
                    targetSeedTimeMinutes: v,
                  })
                }
                min={0}
                suffix={t("settingsTabs.schedule.minuteSuffix")}
                hint={t("settingsTabs.categories.modal.unlimitedHint")}
              />
            </div>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
                marginTop: "0.5rem",
              }}
            >
              <Toggle
                label={t("settingsTabs.categories.modal.autoStopLabel")}
                checked={editingCategory.autoStop ?? false}
                onChange={(v) =>
                  setEditingCategory({ ...editingCategory, autoStop: v })
                }
                hint={t("settingsTabs.categories.modal.autoStopHint")}
              />

              <Toggle
                label={t("settingsTabs.categories.modal.defaultCategoryLabel")}
                checked={editingCategory.isDefault ?? false}
                onChange={(v) =>
                  setEditingCategory({ ...editingCategory, isDefault: v })
                }
                hint={t("settingsTabs.categories.modal.defaultCategoryHint")}
              />
            </div>

            {modalError && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.6rem 0.8rem",
                  borderRadius: "6px",
                  fontSize: "0.85rem",
                  lineHeight: "1.4",
                  backgroundColor: "rgba(220, 53, 69, 0.15)",
                  color: "var(--danger, #dc3545)",
                  border: "1px solid rgba(220, 53, 69, 0.35)",
                }}
              >
                ✕ {modalError}
              </div>
            )}

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setEditingCategory(null)}
                disabled={isSaving}
              >
                {t("settingsTabs.categories.modal.cancel")}
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSave}
                disabled={isSaving}
              >
                {isSaving
                  ? t("settingsTabs.categories.modal.saving")
                  : editingCategory.id
                    ? t("settingsTabs.categories.modal.saveChanges")
                    : t("settingsTabs.categories.modal.createCategory")}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export const CategorySettings = CategorySettingsTab;
export default CategorySettingsTab;
