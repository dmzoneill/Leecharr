import { useTranslation } from "../../i18n";
import { useState, useRef } from "react";
import {
  useArrConnections,
  useCreateArrConnection,
  useUpdateArrConnection,
  useDeleteArrConnection,
  useTestArrConnection,
  useTestDirectArrConnection,
  useArrSync,
} from "../../api/hooks";
import type {
  ArrConnection,
  ArrTestResult,
  RemotePathMapping,
} from "../../api/types";
import { TextInput, SelectInput, Toggle, SectionCard } from "./shared";
import { useToast } from "../../context/ToastContext";
import { useConfirm } from "../../context/ConfirmContext";
import { useFocusTrap } from "../../hooks/useFocusTrap";

const STORAGE_KEY_PATH_MAPPINGS = "leecharr_remote_path_mappings";

function getStoredPathMappings(): RemotePathMapping[] {
  if (typeof window === "undefined" || !window.localStorage) return [];
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY_PATH_MAPPINGS);
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function setStoredPathMappings(mappings: RemotePathMapping[]): void {
  if (typeof window === "undefined" || !window.localStorage) return;
  try {
    window.localStorage.setItem(
      STORAGE_KEY_PATH_MAPPINGS,
      JSON.stringify(mappings),
    );
  } catch {
    // ignore storage write errors
  }
}

export function ConnectionsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const confirm = useConfirm();
  const { data: connections, isLoading } = useArrConnections();
  const createMutation = useCreateArrConnection();
  const updateMutation = useUpdateArrConnection();
  const deleteMutation = useDeleteArrConnection();
  const testMutation = useTestArrConnection();
  const testDirectMutation = useTestDirectArrConnection();
  const syncMutation = useArrSync();
  const [editing, setEditing] = useState<Partial<ArrConnection> | null>(null);
  const initialConnRef = useRef<string>("");

  const handleCloseModal = async () => {
    const isDirty = Boolean(
      editing &&
      initialConnRef.current &&
      JSON.stringify(editing) !== initialConnRef.current,
    );
    if (isDirty) {
      const ok = await confirm({
        title: t("settingsTabs.shared.unsavedChangesTitle"),
        message: t("settingsTabs.shared.unsavedChangesDesc"),
        confirmText: t("settingsTabs.shared.discardAndLeave"),
        cancelText: t("settingsTabs.shared.stayOnPage"),
        danger: true,
      });
      if (!ok) return;
    }
    setEditing(null);
    setModalTestResult(null);
  };

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen: Boolean(editing),
    onClose: handleCloseModal,
  });

  const [testResults, setTestResults] = useState<
    Record<number, ArrTestResult | null>
  >({});
  const [modalTestResult, setModalTestResult] = useState<ArrTestResult | null>(
    null,
  );

  const defaultConnection: Partial<ArrConnection> = {
    name: "Sonarr",
    arrType: "Sonarr",
    url: "http://localhost:8989",
    externalUrl: "",
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    syncCategories: true,
    category: "",
    savePath: "",
    implementation: "SonarrConnection",
    configContract: "ArrConnectionDefinition",
  };

  const [pathMappings, setPathMappings] = useState<RemotePathMapping[]>(() =>
    getStoredPathMappings(),
  );
  const [editingMapping, setEditingMapping] =
    useState<Partial<RemotePathMapping> | null>(null);
  const initialMappingRef = useRef<string>("");

  const handleCloseMappingModal = async () => {
    const isDirty = Boolean(
      editingMapping &&
      initialMappingRef.current &&
      JSON.stringify(editingMapping) !== initialMappingRef.current,
    );
    if (isDirty) {
      const ok = await confirm({
        title: t("settingsTabs.shared.unsavedChangesTitle"),
        message: t("settingsTabs.shared.unsavedChangesDesc"),
        confirmText: t("settingsTabs.shared.discardAndLeave"),
        cancelText: t("settingsTabs.shared.stayOnPage"),
        danger: true,
      });
      if (!ok) return;
    }
    setEditingMapping(null);
  };

  const mappingTrapRef = useFocusTrap<HTMLDivElement>({
    isOpen: Boolean(editingMapping),
    onClose: handleCloseMappingModal,
  });

  const handleOpenAddMapping = () => {
    const initial: Partial<RemotePathMapping> = {
      host: "*",
      remotePath: "",
      localPath: "",
    };
    initialMappingRef.current = JSON.stringify(initial);
    setEditingMapping(initial);
  };

  const handleOpenEditMapping = (mapping: RemotePathMapping) => {
    const initial = { ...mapping };
    initialMappingRef.current = JSON.stringify(initial);
    setEditingMapping(initial);
  };

  const handleSaveMapping = () => {
    if (!editingMapping) return;
    if (!editingMapping.host?.trim()) {
      showToast(t("settingsTabs.connections.hostRequired"), "error");
      return;
    }
    if (!editingMapping.remotePath?.trim()) {
      showToast(t("settingsTabs.connections.remotePathRequired"), "error");
      return;
    }
    if (!editingMapping.localPath?.trim()) {
      showToast(t("settingsTabs.connections.localPathRequired"), "error");
      return;
    }

    if (editingMapping.id) {
      const updated = pathMappings.map((m) =>
        m.id === editingMapping.id ? (editingMapping as RemotePathMapping) : m,
      );
      setPathMappings(updated);
      setStoredPathMappings(updated);
      showToast(t("settingsTabs.connections.pathMappingUpdated"), "success");
    } else {
      const newMapping: RemotePathMapping = {
        id: Date.now(),
        host: editingMapping.host.trim(),
        remotePath: editingMapping.remotePath.trim(),
        localPath: editingMapping.localPath.trim(),
      };
      const updated = [...pathMappings, newMapping];
      setPathMappings(updated);
      setStoredPathMappings(updated);
      showToast(t("settingsTabs.connections.pathMappingCreated"), "success");
    }
    setEditingMapping(null);
  };

  const handleDeleteMapping = async (mapping: RemotePathMapping) => {
    const ok = await confirm({
      title: t("settingsTabs.connections.deletePathMappingTitle"),
      message: t("settingsTabs.connections.deletePathMappingMessage", {
        host: mapping.host,
      }),
      danger: true,
      confirmText: t("settingsTabs.categories.deleteConfirm"),
    });
    if (!ok) return;

    const updated = pathMappings.filter((m) => m.id !== mapping.id);
    setPathMappings(updated);
    setStoredPathMappings(updated);
    showToast(t("settingsTabs.connections.pathMappingDeleted"), "info");
  };

  const handleOpenModal = (conn: Partial<ArrConnection>) => {
    setModalTestResult(null);
    const initial = { ...conn };
    initialConnRef.current = JSON.stringify(initial);
    setEditing(initial);
  };

  const handleSave = () => {
    if (!editing) return;
    if (!editing.name?.trim()) {
      showToast(
        t("settingsTabs.connections.nameRequired", "Name is required"),
        "error",
      );
      return;
    }
    if (!editing.url?.trim()) {
      showToast(
        t("settingsTabs.connections.urlRequired", "URL is required"),
        "error",
      );
      return;
    }
    if (editing.id) {
      updateMutation.mutate(editing as ArrConnection, {
        onSuccess: () => {
          showToast(
            t("settingsTabs.connections.connectionUpdated", {
              name: editing.name,
              defaultValue: `Connection "${editing.name}" updated`,
            }),
            "success",
          );
          setEditing(null);
        },
        onError: (err: unknown) => {
          showToast(
            (err as Error)?.message ||
              t(
                "settingsTabs.connections.updateFailed",
                "Failed to update connection",
              ),
            "error",
          );
        },
      });
    } else {
      createMutation.mutate(editing, {
        onSuccess: () => {
          showToast(
            t("settingsTabs.connections.connectionCreated", {
              name: editing.name,
              defaultValue: `Connection "${editing.name}" created`,
            }),
            "success",
          );
          setEditing(null);
        },
        onError: (err: unknown) => {
          showToast(
            (err as Error)?.message ||
              t(
                "settingsTabs.connections.createFailed",
                "Failed to create connection",
              ),
            "error",
          );
        },
      });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) => setTestResults((prev) => ({ ...prev, [id]: data })),
      onError: (err) =>
        setTestResults((prev) => ({
          ...prev,
          [id]: { success: false, message: err.message },
        })),
    });
  };

  const handleModalTest = () => {
    if (!editing) return;
    setModalTestResult(null);
    testDirectMutation.mutate(editing, {
      onSuccess: (data) => setModalTestResult(data),
      onError: (err) =>
        setModalTestResult({ success: false, message: err.message }),
    });
  };

  if (isLoading)
    return (
      <div className="loading">{t("settingsTabs.connections.loading")}</div>
    );

  return (
    <>
      <SectionCard
        title={t("settings.arrMediaManagementConnectio")}
        description={t("settings.integrateWithSonarrRadarr")}
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
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              flexWrap: "wrap",
            }}
          >
            <button
              className="btn btn-outline btn-small"
              onClick={() => syncMutation.mutate()}
              disabled={syncMutation.isPending}
            >
              {syncMutation.isPending
                ? t("settingsTabs.downloadClients.syncing")
                : t("settingsTabs.connections.syncNow")}
            </button>
            {syncMutation.isError && (
              <span style={{ color: "var(--danger)", fontSize: "0.85rem" }}>
                {t("settingsTabs.connections.syncFailed", {
                  error: syncMutation.error?.message,
                })}
              </span>
            )}
            {syncMutation.isSuccess && syncMutation.data && (
              <span style={{ color: "var(--success)", fontSize: "0.85rem" }}>
                {syncMutation.data.syncedCount !== undefined ? (
                  <span>
                    ✓{" "}
                    {syncMutation.data.message ||
                      t("settingsTabs.connections.syncCompleteFraction", {
                        syncedCount: syncMutation.data.syncedCount,
                        totalCount:
                          syncMutation.data.totalCount ??
                          syncMutation.data.syncedCount,
                      })}
                  </span>
                ) : (
                  <span>
                    ✓{" "}
                    {t("settingsTabs.connections.syncCompleteDetails", {
                      added: syncMutation.data.added ?? 0,
                      skipped: syncMutation.data.skipped ?? 0,
                    })}
                    {(syncMutation.data.failed ?? 0) > 0 && (
                      <span
                        style={{
                          color: "var(--danger)",
                          marginLeft: "0.35rem",
                        }}
                      >
                        {t("settingsTabs.connections.syncFailedCount", {
                          failed: syncMutation.data.failed,
                        })}
                      </span>
                    )}
                  </span>
                )}
              </span>
            )}
          </div>
        </div>

        <div className="provider-cards">
          {connections?.map((conn) => (
            <div
              key={conn.id}
              className="provider-card"
              onClick={() => handleOpenModal(conn)}
            >
              <div className="provider-card-actions">
                {(conn.externalUrl || conn.url) && (
                  <a
                    href={conn.externalUrl || conn.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="provider-card-action"
                    title={t("settingsTabs.connections.openWebUI", {
                      name: conn.name,
                      url: conn.externalUrl || conn.url,
                    })}
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
                    handleTest(conn.id);
                  }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title={t("settings.deleteConnection")}
                  onClick={async (e) => {
                    e.stopPropagation();
                    const ok = await confirm({
                      title: t("settingsTabs.connections.deleteTitle"),
                      message: t("settingsTabs.connections.deleteMessage", {
                        name: conn.name,
                      }),
                      danger: true,
                      confirmText: t("settingsTabs.categories.deleteConfirm"),
                    });
                    if (!ok) return;

                    deleteMutation.mutate(conn.id, {
                      onSuccess: () =>
                        showToast(
                          t("settingsTabs.connections.deleted", {
                            name: conn.name,
                          }),
                          "info",
                        ),
                      onError: (err: unknown) =>
                        showToast(
                          (err as Error)?.message ||
                            t("settingsTabs.connections.deleteFailed"),
                          "error",
                        ),
                    });
                  }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{conn.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">
                  {conn.arrType}
                </span>
                {conn.enable === false && (
                  <span className="provider-card-badge provider-card-badge-gray">
                    {t("settingsTabs.categories.table.disabled")}
                  </span>
                )}
                {conn.syncEnabled && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    {t("settingsTabs.connections.badgeSync")}
                  </span>
                )}
                {conn.syncCategories !== false && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    {t("settingsTabs.connections.badgeSyncCategories")}
                  </span>
                )}
                {conn.enableAutomaticAdd && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    {t("settingsTabs.connections.badgeAutoAdd")}
                  </span>
                )}
                {conn.webhookEnabled && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    {t("settingsTabs.connections.badgeWebhook")}
                  </span>
                )}
              </div>
              <div className="provider-card-info">
                {conn.url}
                {conn.externalUrl && conn.externalUrl !== conn.url && (
                  <div
                    style={{
                      fontSize: "0.75rem",
                      opacity: 0.8,
                      marginTop: "2px",
                    }}
                  >
                    ↳ {conn.externalUrl}
                  </div>
                )}
                {conn.category && (
                  <div
                    style={{
                      fontSize: "0.75rem",
                      opacity: 0.8,
                      marginTop: "2px",
                    }}
                  >
                    {t("settingsTabs.connections.category")}: {conn.category}
                  </div>
                )}
                {conn.savePath && (
                  <div
                    style={{
                      fontSize: "0.75rem",
                      opacity: 0.8,
                      marginTop: "2px",
                    }}
                  >
                    {t("settingsTabs.connections.savePath")}: {conn.savePath}
                  </div>
                )}
              </div>
              {testResults[conn.id]?.success === true && (
                <div className="provider-card-test provider-card-test-ok">
                  {t("settingsTabs.indexers.connectionPassed")}
                </div>
              )}
              {testResults[conn.id]?.success === false && (
                <div
                  className="provider-card-test provider-card-test-fail"
                  title={testResults[conn.id]?.message}
                >
                  {t("settingsTabs.indexers.connectionFailed")}
                </div>
              )}
              {testResults[conn.id] === null && (
                <div className="provider-card-test provider-card-test-pending">
                  {t("settingsTabs.notifications.testing")}
                </div>
              )}
            </div>
          ))}
          <div
            className="provider-card-add"
            onClick={() => handleOpenModal(defaultConnection)}
            title={t("settings.addArrConnection")}
          >
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.connections.remotePathMappingsTitle")}
        description={t("settingsTabs.connections.remotePathMappingsDesc")}
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
            className="btn btn-primary btn-small"
            onClick={handleOpenAddMapping}
          >
            + {t("settingsTabs.connections.addPathMapping")}
          </button>
        </div>

        {pathMappings.length === 0 ? (
          <div
            style={{
              padding: "2rem",
              textAlign: "center",
              color: "var(--text-muted, #7e8092)",
              backgroundColor: "var(--bg-subtle, rgba(255, 255, 255, 0.02))",
              borderRadius: "6px",
              border: "1px dashed var(--border, #333)",
            }}
          >
            <p style={{ margin: "0 0 1rem" }}>
              {t("settingsTabs.connections.noPathMappings")}
            </p>
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={handleOpenAddMapping}
            >
              + {t("settingsTabs.connections.addPathMapping")}
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
                    borderBottom: "1px solid var(--border-light, #333)",
                    textAlign: "left",
                    color: "var(--text-muted, #7e8092)",
                    fontSize: "0.8rem",
                  }}
                >
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.connections.host")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.connections.remotePath")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>
                    {t("settingsTabs.connections.localPath")}
                  </th>
                  <th style={{ padding: "0.6rem 0.8rem", textAlign: "right" }}>
                    {t("settingsTabs.categories.table.actions")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {pathMappings.map((m) => (
                  <tr
                    key={m.id}
                    style={{
                      borderBottom: "1px solid var(--border-light, #222)",
                    }}
                  >
                    <td style={{ padding: "0.65rem 0.8rem", fontWeight: 600 }}>
                      <span className="provider-card-badge provider-card-badge-blue">
                        {m.host}
                      </span>
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.8rem",
                        fontFamily: "monospace",
                      }}
                    >
                      {m.remotePath}
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.8rem",
                        fontFamily: "monospace",
                      }}
                    >
                      {m.localPath}
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.8rem",
                        textAlign: "right",
                        whiteSpace: "nowrap",
                      }}
                    >
                      <button
                        type="button"
                        className="btn btn-outline btn-small"
                        style={{ marginRight: "0.5rem" }}
                        onClick={() => handleOpenEditMapping(m)}
                      >
                        {t("settingsTabs.categories.table.edit")}
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-small btn-danger"
                        onClick={() => handleDeleteMapping(m)}
                      >
                        {t("settingsTabs.categories.table.delete")}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>

      {editing && (
        <div
          className="modal-overlay"
          onClick={handleCloseModal}
          role="dialog"
          aria-modal="true"
        >
          <div
            ref={trapRef}
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 520,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border)",
            }}
          >
            <div
              className="modal-title"
              style={{ fontSize: "1.2rem", marginBottom: "1rem" }}
            >
              {editing.id
                ? t("settingsTabs.connections.editTitle")
                : t("settingsTabs.connections.addTitle")}
            </div>
            <TextInput
              label={t("settingsTabs.categories.table.name")}
              value={editing.name || ""}
              onChange={(v) => setEditing({ ...editing, name: v })}
              placeholder={t("settings.sonarr")}
            />
            <SelectInput
              label={t("settingsTabs.indexers.typeLabel")}
              value={editing.arrType || "Sonarr"}
              onChange={(v) => {
                const defaults: Record<string, string> = {
                  Sonarr: "http://localhost:8989",
                  Radarr: "http://localhost:7878",
                  Lidarr: "http://localhost:8686",
                };
                setEditing({
                  ...editing,
                  arrType: v,
                  name:
                    editing.name && editing.name !== editing.arrType
                      ? editing.name
                      : v,
                  url: defaults[v] || editing.url || "",
                  implementation: `${v}Connection`,
                });
              }}
              options={[
                { value: "Sonarr", label: "Sonarr" },
                { value: "Radarr", label: "Radarr" },
                { value: "Lidarr", label: "Lidarr" },
              ]}
            />
            <TextInput
              label={t("settingsTabs.indexers.urlLabel")}
              value={editing.url || ""}
              onChange={(v) => setEditing({ ...editing, url: v })}
              placeholder="http://localhost:8989"
            />
            <TextInput
              label={t(
                "settings.externalUrl",
                "Public / External URL (Optional)",
              )}
              value={editing.externalUrl || ""}
              onChange={(v) =>
                setEditing({ ...editing, externalUrl: v, publicUrl: v })
              }
              placeholder="http://my-domain.com:8989"
              hint={t(
                "settings.externalUrlHint",
                "Optional public URL for browser deep links (e.g. when accessing Leecharr remotely while using internal container addresses).",
              )}
            />
            <TextInput
              label={t("settingsTabs.indexers.apiKeyLabel")}
              value={editing.apiKey || ""}
              onChange={(v) => setEditing({ ...editing, apiKey: v })}
              type="password"
            />
            <TextInput
              label={t("settingsTabs.connections.category")}
              value={editing.category || ""}
              onChange={(v) => setEditing({ ...editing, category: v })}
              placeholder="tv"
              hint={t("settingsTabs.connections.categoryHint")}
            />
            <TextInput
              label={t("settingsTabs.connections.savePath")}
              value={editing.savePath || ""}
              onChange={(v) => setEditing({ ...editing, savePath: v })}
              placeholder="/downloads/tv"
              hint={t("settingsTabs.connections.savePathHint")}
            />
            <Toggle
              label={t("settingsTabs.notifications.enableConnection")}
              checked={editing.enable ?? true}
              onChange={(v) => setEditing({ ...editing, enable: v })}
            />
            <Toggle
              label={t("settings.syncEnabled")}
              checked={editing.syncEnabled ?? true}
              onChange={(v) => setEditing({ ...editing, syncEnabled: v })}
            />
            <Toggle
              label={t("settingsTabs.connections.syncCategories")}
              checked={editing.syncCategories ?? true}
              onChange={(v) => setEditing({ ...editing, syncCategories: v })}
            />
            <Toggle
              label={t("settings.autoAdd")}
              checked={editing.enableAutomaticAdd ?? true}
              onChange={(v) =>
                setEditing({ ...editing, enableAutomaticAdd: v })
              }
            />
            <Toggle
              label={t("settings.webhook")}
              checked={editing.webhookEnabled ?? true}
              onChange={(v) => setEditing({ ...editing, webhookEnabled: v })}
            />
            {editing.webhookEnabled !== false && (
              <TextInput
                label={t("settings.webhookHost")}
                value={editing.webhookHost || ""}
                onChange={(v) => setEditing({ ...editing, webhookHost: v })}
                placeholder="Leecharr"
                hint={t("settingsTabs.connections.webhookHostHint")}
              />
            )}

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
                <span>
                  {t("settingsTabs.connections.testingConnection", {
                    url: editing.url || "server",
                  })}
                </span>
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
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={handleCloseModal}
                >
                  {t("settingsTabs.categories.modal.cancel")}
                </button>
                <button
                  className="btn btn-primary btn-small"
                  onClick={handleSave}
                  disabled={
                    createMutation.isPending || updateMutation.isPending
                  }
                >
                  {createMutation.isPending || updateMutation.isPending
                    ? t("settingsTabs.categories.modal.saving")
                    : t("settingsTabs.notifications.save")}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {editingMapping && (
        <div
          className="modal-overlay"
          onClick={handleCloseMappingModal}
          role="dialog"
          aria-modal="true"
        >
          <div
            ref={mappingTrapRef}
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 520,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border)",
            }}
          >
            <div
              className="modal-title"
              style={{ fontSize: "1.2rem", marginBottom: "1rem" }}
            >
              {editingMapping.id
                ? t("settingsTabs.connections.editPathMapping")
                : t("settingsTabs.connections.addPathMapping")}
            </div>
            <TextInput
              label={t("settingsTabs.connections.host")}
              value={editingMapping.host || ""}
              onChange={(v) =>
                setEditingMapping({ ...editingMapping, host: v })
              }
              placeholder="*"
              hint={t("settingsTabs.connections.hostHint")}
            />
            <TextInput
              label={t("settingsTabs.connections.remotePath")}
              value={editingMapping.remotePath || ""}
              onChange={(v) =>
                setEditingMapping({ ...editingMapping, remotePath: v })
              }
              placeholder="/downloads/"
              hint={t("settingsTabs.connections.remotePathHint")}
            />
            <TextInput
              label={t("settingsTabs.connections.localPath")}
              value={editingMapping.localPath || ""}
              onChange={(v) =>
                setEditingMapping({ ...editingMapping, localPath: v })
              }
              placeholder="/data/downloads/"
              hint={t("settingsTabs.connections.localPathHint")}
            />
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
                type="button"
                className="btn btn-outline btn-small"
                onClick={handleCloseMappingModal}
              >
                {t("settingsTabs.categories.modal.cancel")}
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSaveMapping}
              >
                {t("settingsTabs.notifications.save")}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
