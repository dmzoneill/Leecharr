import { useTranslation } from "../../i18n";
import { useState } from "react";
import {
  useIndexers,
  useCreateIndexer,
  useUpdateIndexer,
  useDeleteIndexer,
  useTestIndexer,
  useTestDirectIndexer,
  useSyncProwlarr,
  useRssRules,
  useCreateRssRule,
  useUpdateRssRule,
  useDeleteRssRule,
  useSyncRss,
} from "../../api/hooks";
import type {
  IndexerDefinition,
  IndexerTestResult,
  RssRule,
} from "../../api/types";
import {
  TextInput,
  NumberInput,
  SelectInput,
  Toggle,
  SectionCard,
} from "./shared";
import { useToast } from "../../context/ToastContext";
import { useConfirm } from "../../context/ConfirmContext";
import { useEscapeKey } from "../../hooks/useEscapeKey";

export function normalizeIndexerPayload(
  editing: Partial<IndexerDefinition>,
): IndexerDefinition {
  let categories: number[] = [];
  if (Array.isArray(editing.categories)) {
    categories = (editing.categories as (number | string)[])
      .map((c) => Number(c))
      .filter((n) => !isNaN(n));
  } else if (
    typeof editing.categories === "string" &&
    (editing.categories as string).trim()
  ) {
    categories = (editing.categories as string)
      .split(",")
      .map((s) => Number(s.trim()))
      .filter((n) => !isNaN(n));
  } else if (typeof editing.categories === "number") {
    categories = [editing.categories];
  }

  let url = editing.url?.trim() || "";
  if (editing.apiPath && !url.includes(editing.apiPath)) {
    const baseUrl = url.replace(/\/+$/, "");
    const path = editing.apiPath.replace(/^\/+/, "");
    if (path) {
      url = `${baseUrl}/${path}`;
    }
  }

  const name = editing.name?.trim() || editing.indexerType || "Indexer";
  const implementation =
    editing.implementation || `${editing.indexerType || "Prowlarr"}Indexer`;

  return {
    ...editing,
    name,
    url,
    implementation,
    configContract: editing.configContract || "IndexerDefinition",
    categories,
    enable: editing.enable ?? true,
  } as IndexerDefinition;
}

export function IndexersTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const { data: indexers, isLoading: isIndexersLoading } = useIndexers();
  const { data: rssRules, isLoading: isRssRulesLoading } = useRssRules();

  const createMutation = useCreateIndexer();
  const updateMutation = useUpdateIndexer();
  const deleteMutation = useDeleteIndexer();
  const testMutation = useTestIndexer();
  const testDirectMutation = useTestDirectIndexer();
  const syncMutation = useSyncProwlarr();

  const createRuleMutation = useCreateRssRule();
  const updateRuleMutation = useUpdateRssRule();
  const deleteRuleMutation = useDeleteRssRule();
  const syncRssMutation = useSyncRss();

  const [editing, setEditing] = useState<Partial<IndexerDefinition> | null>(
    null,
  );
  const [editingRule, setEditingRule] = useState<Partial<RssRule> | null>(null);
  const confirm = useConfirm();

  useEscapeKey(() => setEditing(null), Boolean(editing));
  useEscapeKey(() => setEditingRule(null), Boolean(editingRule));

  const [testResults, setTestResults] = useState<
    Record<number, boolean | null>
  >({});
  const [modalTestResult, setModalTestResult] =
    useState<IndexerTestResult | null>(null);

  const defaultIndexer: Partial<IndexerDefinition> = {
    name: t("settingsTabs.indexers.prowlarr"),
    indexerType: t("settingsTabs.indexers.prowlarr"),
    url: "http://prowlarr:9696",
    apiKey: "",
    apiPath: "/api",
    enableRss: true,
    enableSearch: true,
    categories: "",
    downloadClientId: 0,
    enable: true,
  };

  const defaultRssRule: Partial<RssRule> = {
    name: "New RSS Rule",
    isEnabled: true,
    mustContain: "",
    mustNotContain: "",
    minSeeders: 1,
    minSizeBytes: 0,
    maxSizeBytes: 0,
    freeleechOnly: false,
    categoryId: 0,
    indexerIds: [],
  };

  const handleSave = () => {
    if (!editing) return;
    if (
      editing.indexerType === t("settingsTabs.indexers.prowlarr") &&
      !editing.id
    ) {
      syncMutation.mutate(
        {
          url: editing.url || "http://localhost:9696",
          apiKey: editing.apiKey || "",
        },
        {
          onSuccess: (data) => {
            showToast(
              `Synced ${data.syncedCount} indexers from Prowlarr`,
              "success",
            );
            setEditing(null);
            setModalTestResult(null);
          },
          onError: (err) => {
            showToast(
              err?.message || t("settingsTabs.indexers.syncProwlarrFailed"),
              "error",
            );
          },
        },
      );
      return;
    }
    const payload = normalizeIndexerPayload(editing);
    if (editing.id) {
      updateMutation.mutate(payload, {
        onSuccess: () => {
          setEditing(null);
          setModalTestResult(null);
        },
      });
    } else {
      createMutation.mutate(payload, {
        onSuccess: () => {
          setEditing(null);
          setModalTestResult(null);
        },
      });
    }
  };

  const handleSaveRule = () => {
    if (!editingRule) return;
    const name = editingRule.name?.trim() || "RSS Rule";
    const payload: RssRule = {
      id: editingRule.id || 0,
      name,
      isEnabled: editingRule.isEnabled ?? true,
      mustContain: editingRule.mustContain || "",
      mustNotContain: editingRule.mustNotContain || "",
      minSeeders: Number(editingRule.minSeeders) || 0,
      minSizeBytes: Number(editingRule.minSizeBytes) || 0,
      maxSizeBytes: Number(editingRule.maxSizeBytes) || 0,
      freeleechOnly: Boolean(editingRule.freeleechOnly),
      categoryId: Number(editingRule.categoryId) || 0,
      indexerIds: Array.isArray(editingRule.indexerIds)
        ? editingRule.indexerIds
        : [],
    };

    if (editingRule.id) {
      updateRuleMutation.mutate(payload, {
        onSuccess: () => {
          showToast(`RSS Rule "${name}" updated`, "success");
          setEditingRule(null);
        },
        onError: (err: any) => {
          showToast(
            err?.message || t("settingsTabs.indexers.rssRuleUpdateFailed"),
            "error",
          );
        },
      });
    } else {
      createRuleMutation.mutate(payload, {
        onSuccess: () => {
          showToast(`RSS Rule "${name}" created`, "success");
          setEditingRule(null);
        },
        onError: (err: any) => {
          showToast(
            err?.message || t("settingsTabs.indexers.rssRuleCreateFailed"),
            "error",
          );
        },
      });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) =>
        setTestResults((prev) => ({ ...prev, [id]: data.success })),
      onError: () => setTestResults((prev) => ({ ...prev, [id]: false })),
    });
  };

  const handleModalTest = () => {
    if (!editing) return;
    setModalTestResult(null);
    const payload = normalizeIndexerPayload(editing);
    testDirectMutation.mutate(payload, {
      onSuccess: (res) => {
        setModalTestResult(res);
      },
      onError: (err) => {
        setModalTestResult({
          success: false,
          message: err.message || "Failed to test indexer connection.",
        });
      },
    });
  };

  const handleSyncRssNow = () => {
    syncRssMutation.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          `RSS Sync complete: grabbed ${res.grabbedCount} release(s)`,
          "success",
        );
      },
      onError: (err: any) => {
        showToast(
          err?.message || t("settingsTabs.indexers.rssSyncFailed"),
          "error",
        );
      },
    });
  };

  if (isIndexersLoading || isRssRulesLoading)
    return <div className="loading">{t("settingsTabs.indexers.loading")}</div>;

  return (
    <>
      <SectionCard
        title={t("settingsTabs.indexers.torznabTitle")}
        description={t("settingsTabs.indexers.torznabDesc")}
      >
        <div className="provider-cards">
          {indexers?.map((idx) => (
            <div
              key={idx.id}
              className="provider-card"
              onClick={() => {
                setEditing({ ...idx });
                setModalTestResult(null);
              }}
            >
              <div className="provider-card-actions">
                {idx.url && (
                  <a
                    href={idx.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="provider-card-action"
                    title={`Open ${idx.name} Web UI (${idx.url})`}
                    onClick={(e) => e.stopPropagation()}
                    style={{ textDecoration: "none", color: "inherit" }}
                  >
                    ↗
                  </a>
                )}
                <button
                  className="provider-card-action"
                  title={t("settingsTabs.indexers.testConnection")}
                  onClick={(e) => {
                    e.stopPropagation();
                    handleTest(idx.id);
                  }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title={t("settingsTabs.indexers.deleteIndexer")}
                  onClick={async (e) => {
                    e.stopPropagation();
                    const ok = await confirm({
                      title: t("settingsTabs.indexers.deleteIndexer"),
                      message: `Are you sure you want to delete the indexer "${idx.name}"?`,
                      danger: true,
                      confirmText: t("settingsTabs.categories.deleteConfirm"),
                    });
                    if (!ok) return;

                    deleteMutation.mutate(idx.id, {
                      onSuccess: () =>
                        showToast(`Indexer "${idx.name}" deleted`, "info"),
                      onError: (err: any) =>
                        showToast(
                          err?.message ||
                            t("settingsTabs.indexers.deleteIndexerFailed"),
                          "error",
                        ),
                    });
                  }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{idx.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">
                  {idx.indexerType}
                </span>
                {idx.enableRss && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    {t("settingsTabs.indexers.badgeRss")}
                  </span>
                )}
                {idx.enableSearch && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    {t("settingsTabs.indexers.badgeSearch")}
                  </span>
                )}
              </div>
              <div className="provider-card-info">{idx.url}</div>
              {testResults[idx.id] === true && (
                <div className="provider-card-test provider-card-test-ok">
                  {t("settingsTabs.indexers.connectionPassed")}
                </div>
              )}
              {testResults[idx.id] === false && (
                <div className="provider-card-test provider-card-test-fail">
                  {t("settingsTabs.indexers.connectionFailed")}
                </div>
              )}
              {testResults[idx.id] === null && (
                <div className="provider-card-test provider-card-test-pending">
                  {t("settingsTabs.notifications.testing")}
                </div>
              )}
            </div>
          ))}
          <div
            className="provider-card-add"
            onClick={() => {
              setEditing({ ...defaultIndexer });
              setModalTestResult(null);
            }}
            title={t("settingsTabs.indexers.addIndexer")}
          >
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.indexers.rssRulesTitle")}
        description={t("settingsTabs.indexers.rssRulesDesc")}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            marginBottom: "1rem",
          }}
        >
          <button
            type="button"
            className="btn btn-outline btn-small"
            onClick={handleSyncRssNow}
            disabled={syncRssMutation.isPending}
          >
            {syncRssMutation.isPending
              ? t("settingsTabs.indexers.syncingRss")
              : t("settingsTabs.indexers.syncRssNow")}
          </button>
        </div>

        <div className="provider-cards">
          {rssRules?.map((rule) => (
            <div
              key={rule.id}
              className="provider-card"
              onClick={() => setEditingRule({ ...rule })}
            >
              <div className="provider-card-actions">
                <button
                  className="provider-card-action provider-card-action-danger"
                  title={t("settingsTabs.indexers.deleteRssRule")}
                  onClick={async (e) => {
                    e.stopPropagation();
                    const ok = await confirm({
                      title: t("settingsTabs.indexers.deleteRssRule"),
                      message: `Are you sure you want to delete the RSS rule "${rule.name}"?`,
                      danger: true,
                      confirmText: t("settingsTabs.categories.deleteConfirm"),
                    });
                    if (!ok) return;

                    deleteRuleMutation.mutate(rule.id, {
                      onSuccess: () =>
                        showToast(`RSS Rule "${rule.name}" deleted`, "info"),
                      onError: (err: any) =>
                        showToast(
                          err?.message ||
                            t("settingsTabs.indexers.deleteRssRuleFailed"),
                          "error",
                        ),
                    });
                  }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{rule.name}</div>
              <div className="provider-card-badges">
                <span
                  className={`provider-card-badge ${
                    rule.isEnabled
                      ? "provider-card-badge-green"
                      : "provider-card-badge-gray"
                  }`}
                >
                  {rule.isEnabled
                    ? t("settingsTabs.indexers.enabled")
                    : t("settingsTabs.categories.table.disabled")}
                </span>
                {rule.minSeeders > 0 && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    ≥ {rule.minSeeders} seeds
                  </span>
                )}
                {rule.freeleechOnly && (
                  <span className="provider-card-badge provider-card-badge-gold">
                    {t("settingsTabs.indexers.freeleech")}
                  </span>
                )}
                {rule.categoryId > 0 && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    Cat: {rule.categoryId}
                  </span>
                )}
                <span className="provider-card-badge provider-card-badge-blue">
                  {rule.indexerIds && rule.indexerIds.length > 0
                    ? `${rule.indexerIds.length} Indexers`
                    : t("settingsTabs.indexers.allIndexers")}
                </span>
              </div>
              <div className="provider-card-info">
                {rule.mustContain && (
                  <div style={{ wordBreak: "break-all" }}>
                    <strong>{t("settingsTabs.indexers.mustContain")}</strong>{" "}
                    <code>{rule.mustContain}</code>
                  </div>
                )}
                {rule.mustNotContain && (
                  <div style={{ wordBreak: "break-all" }}>
                    <strong>{t("settingsTabs.indexers.mustNotContain")}</strong>{" "}
                    <code>{rule.mustNotContain}</code>
                  </div>
                )}
                {!rule.mustContain && !rule.mustNotContain && (
                  <div>{t("settingsTabs.indexers.catchAllRule")}</div>
                )}
              </div>
            </div>
          ))}
          <div
            className="provider-card-add"
            onClick={() => setEditingRule({ ...defaultRssRule })}
            title={t("settingsTabs.indexers.addRssRule")}
          >
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </SectionCard>

      {editing && (
        <div
          className="modal-overlay"
          onClick={() => {
            setEditing(null);
            setModalTestResult(null);
          }}
        >
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
              style={{ fontSize: "1.2rem", marginBottom: "1rem" }}
            >
              {editing.id
                ? t("settingsTabs.indexers.editIndexerTitle")
                : t("settingsTabs.indexers.addIndexer")}
            </div>
            <TextInput
              label={t("settingsTabs.categories.table.name")}
              value={editing.name || ""}
              onChange={(v) => {
                setEditing({ ...editing, name: v });
                setModalTestResult(null);
              }}
              placeholder={t("settingsTabs.indexers.namePlaceholder")}
            />
            <SelectInput
              label={t("settingsTabs.indexers.typeLabel")}
              value={editing.indexerType || t("settingsTabs.indexers.prowlarr")}
              onChange={(v) => {
                const defaults: Record<string, string> = {
                  Prowlarr: "http://localhost:9696",
                  Torznab: "http://localhost:9117",
                  Newznab: "http://localhost:5076",
                };
                setEditing({
                  ...editing,
                  indexerType: v,
                  url: defaults[v] || editing.url || "",
                });
                setModalTestResult(null);
              }}
              options={[
                {
                  value: t("settingsTabs.indexers.prowlarr"),
                  label: t("settingsTabs.indexers.prowlarr"),
                },
                {
                  value: t("settingsTabs.indexers.torznab"),
                  label: t("settingsTabs.indexers.torznab"),
                },
                {
                  value: t("settingsTabs.indexers.newznab"),
                  label: t("settingsTabs.indexers.newznab"),
                },
              ]}
            />
            <TextInput
              label={t("settingsTabs.indexers.urlLabel")}
              value={editing.url || ""}
              onChange={(v) => {
                setEditing({ ...editing, url: v });
                setModalTestResult(null);
              }}
              placeholder="http://localhost:9696"
            />
            <TextInput
              label={t("settingsTabs.indexers.apiKeyLabel")}
              value={editing.apiKey || ""}
              onChange={(v) => {
                setEditing({ ...editing, apiKey: v });
                setModalTestResult(null);
              }}
              type="password"
            />
            <TextInput
              label={t("settingsTabs.indexers.apiPathLabel")}
              value={editing.apiPath || "/api"}
              onChange={(v) => {
                setEditing({ ...editing, apiPath: v });
                setModalTestResult(null);
              }}
              placeholder="api"
            />
            <TextInput
              label={t("settingsTabs.indexers.categoriesLabel")}
              value={
                Array.isArray(editing.categories)
                  ? editing.categories.join(",")
                  : editing.categories || ""
              }
              onChange={(v) => {
                setEditing({ ...editing, categories: v });
                setModalTestResult(null);
              }}
              placeholder="2000,5000"
            />
            <Toggle
              label={t("settingsTabs.indexers.enableLabel")}
              checked={editing.enable ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enable: v });
                setModalTestResult(null);
              }}
            />
            <Toggle
              label={t("settingsTabs.indexers.badgeRss")}
              checked={editing.enableRss ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enableRss: v });
                setModalTestResult(null);
              }}
            />
            <Toggle
              label={t("settingsTabs.indexers.badgeSearch")}
              checked={editing.enableSearch ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enableSearch: v });
                setModalTestResult(null);
              }}
            />

            {testDirectMutation.isPending && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  fontSize: "0.875rem",
                  backgroundColor: "rgba(200, 168, 78, 0.12)",
                  color: "var(--accent, #c8a84e)",
                  border: "1px solid rgba(200, 168, 78, 0.35)",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                }}
              >
                <span>Testing connection to {editing.url || "indexer"}...</span>
              </div>
            )}

            {modalTestResult && !testDirectMutation.isPending && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  fontSize: "0.875rem",
                  lineHeight: "1.4",
                  display: "flex",
                  alignItems: "flex-start",
                  gap: "0.65rem",
                  backgroundColor: modalTestResult.success
                    ? "rgba(40, 167, 69, 0.15)"
                    : "rgba(220, 53, 69, 0.15)",
                  color: modalTestResult.success
                    ? "var(--success, #28a745)"
                    : "var(--danger, #dc3545)",
                  border: `1px solid ${
                    modalTestResult.success
                      ? "rgba(40, 167, 69, 0.35)"
                      : "rgba(220, 53, 69, 0.35)"
                  }`,
                }}
              >
                <span
                  style={{
                    fontWeight: "bold",
                    fontSize: "1.1rem",
                    lineHeight: "1",
                  }}
                >
                  {modalTestResult.success ? "✓" : "✕"}
                </span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600 }}>
                    {modalTestResult.success
                      ? t("settingsTabs.indexers.connectionSuccessful")
                      : t("settingsTabs.indexers.connectionFailedModal")}
                  </div>
                  {modalTestResult.message && (
                    <div
                      style={{
                        marginTop: "0.25rem",
                        opacity: 0.95,
                        wordBreak: "break-word",
                      }}
                    >
                      {modalTestResult.message}
                    </div>
                  )}
                </div>
              </div>
            )}

            {(createMutation.isError || updateMutation.isError) && (
              <div className="modal-error">
                {(createMutation.error || updateMutation.error)?.message}
              </div>
            )}
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={handleModalTest}
                disabled={testDirectMutation.isPending}
              >
                {testDirectMutation.isPending
                  ? t("settingsTabs.notifications.testing")
                  : t("settingsTabs.indexers.testConnection")}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  className="btn btn-outline btn-small"
                  onClick={() => {
                    setEditing(null);
                    setModalTestResult(null);
                  }}
                >
                  {t("settingsTabs.categories.modal.cancel")}
                </button>
                <button
                  className="btn btn-primary btn-small"
                  onClick={handleSave}
                  disabled={
                    createMutation.isPending ||
                    updateMutation.isPending ||
                    syncMutation.isPending
                  }
                >
                  {createMutation.isPending ||
                  updateMutation.isPending ||
                  syncMutation.isPending
                    ? t("settingsTabs.categories.modal.saving")
                    : t("settingsTabs.notifications.save")}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {editingRule && (
        <div className="modal-overlay" onClick={() => setEditingRule(null)}>
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
              style={{ fontSize: "1.2rem", marginBottom: "1rem" }}
            >
              {editingRule.id
                ? t("settingsTabs.indexers.editRssRuleTitle")
                : t("settingsTabs.indexers.addRssRule")}
            </div>
            <TextInput
              label={t("settingsTabs.indexers.ruleNameLabel")}
              value={editingRule.name || ""}
              onChange={(v) => setEditingRule({ ...editingRule, name: v })}
              placeholder={t("settingsTabs.indexers.ruleNamePlaceholder")}
            />
            <Toggle
              label={t("settingsTabs.indexers.enableRuleLabel")}
              checked={editingRule.isEnabled ?? true}
              onChange={(v) => setEditingRule({ ...editingRule, isEnabled: v })}
            />
            <TextInput
              label={t("settingsTabs.indexers.mustContainLabel")}
              value={editingRule.mustContain || ""}
              onChange={(v) =>
                setEditingRule({ ...editingRule, mustContain: v })
              }
              placeholder={t("settingsTabs.indexers.mustContainPlaceholder")}
              hint={t("settingsTabs.indexers.mustContainHint")}
            />
            <TextInput
              label={t("settingsTabs.indexers.mustNotContainLabel")}
              value={editingRule.mustNotContain || ""}
              onChange={(v) =>
                setEditingRule({ ...editingRule, mustNotContain: v })
              }
              placeholder={t("settingsTabs.indexers.mustNotContainPlaceholder")}
              hint={t("settingsTabs.indexers.mustNotContainHint")}
            />
            <NumberInput
              label={t("settingsTabs.indexers.minSeedersLabel")}
              value={editingRule.minSeeders ?? 1}
              onChange={(v) =>
                setEditingRule({ ...editingRule, minSeeders: v })
              }
              min={0}
            />
            <NumberInput
              label={t("settingsTabs.indexers.minSizeLabel")}
              value={editingRule.minSizeBytes ?? 0}
              onChange={(v) =>
                setEditingRule({ ...editingRule, minSizeBytes: v })
              }
              min={0}
              hint="0 = no minimum size constraint"
            />
            <NumberInput
              label={t("settings.maximumSizeBytes")}
              value={editingRule.maxSizeBytes ?? 0}
              onChange={(v) =>
                setEditingRule({ ...editingRule, maxSizeBytes: v })
              }
              min={0}
              hint="0 = no maximum size constraint"
            />
            <NumberInput
              label={t("settings.categoryID")}
              value={editingRule.categoryId ?? 0}
              onChange={(v) =>
                setEditingRule({ ...editingRule, categoryId: v })
              }
              min={0}
              hint="0 = any category (e.g. 5040 for TV/HD, 2040 for Movies/HD)"
            />
            <TextInput
              label={t("settings.assignedIndexersIDs")}
              value={
                Array.isArray(editingRule.indexerIds)
                  ? editingRule.indexerIds.join(",")
                  : ""
              }
              onChange={(v) => {
                const ids = v
                  .split(",")
                  .map((s) => Number(s.trim()))
                  .filter((n) => !isNaN(n) && n > 0);
                setEditingRule({ ...editingRule, indexerIds: ids });
              }}
              placeholder="Leave empty for all indexers"
              hint="Comma-separated Indexer IDs (leave blank for all indexers)"
            />
            <Toggle
              label={t("settings.freeleechOnly")}
              checked={editingRule.freeleechOnly ?? false}
              onChange={(v) =>
                setEditingRule({ ...editingRule, freeleechOnly: v })
              }
            />

            {(createRuleMutation.isError || updateRuleMutation.isError) && (
              <div className="modal-error">
                {
                  (createRuleMutation.error || updateRuleMutation.error)
                    ?.message
                }
              </div>
            )}
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
                marginTop: "1.5rem",
              }}
            >
              <button
                className="btn btn-outline btn-small"
                onClick={() => setEditingRule(null)}
              >
                {t("settingsTabs.categories.modal.cancel")}
              </button>
              <button
                className="btn btn-primary btn-small"
                onClick={handleSaveRule}
                disabled={
                  createRuleMutation.isPending || updateRuleMutation.isPending
                }
              >
                {createRuleMutation.isPending || updateRuleMutation.isPending
                  ? t("settingsTabs.categories.modal.saving")
                  : t("settingsTabs.notifications.save")}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
