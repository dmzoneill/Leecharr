import { useTranslation } from "../../i18n";
import { useState } from "react";
import {
  useArrConnections,
  useCreateArrConnection,
  useUpdateArrConnection,
  useDeleteArrConnection,
  useTestArrConnection,
  useTestDirectArrConnection,
  useArrSync,
} from "../../api/hooks";
import type { ArrConnection, ArrTestResult } from "../../api/types";
import { TextInput, SelectInput, Toggle, SectionCard } from "./shared";
import { useToast } from "../../context/ToastContext";
import { useConfirm } from "../../context/ConfirmContext";
import { useEscapeKey } from "../../hooks/useEscapeKey";

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

  useEscapeKey(() => setEditing(null), Boolean(editing));
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
    apiKey: "",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    implementation: "SonarrConnection",
    configContract: "ArrConnectionDefinition",
  };

  const handleOpenModal = (conn: Partial<ArrConnection>) => {
    setModalTestResult(null);
    setEditing({ ...conn });
  };

  const handleSave = () => {
    if (!editing) return;
    if (editing.id) {
      updateMutation.mutate(editing as ArrConnection, {
        onSuccess: () => setEditing(null),
      });
    } else {
      createMutation.mutate(editing, { onSuccess: () => setEditing(null) });
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
      <div className="loading">
        {t("settings.loadingConnections", "Loading connections...")}
      </div>
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
                : "🔄 Sync Now"}
            </button>
            {syncMutation.isError && (
              <span style={{ color: "var(--danger)", fontSize: "0.85rem" }}>
                Sync failed: {syncMutation.error?.message}
              </span>
            )}
            {syncMutation.isSuccess && syncMutation.data && (
              <span style={{ color: "var(--success)", fontSize: "0.85rem" }}>
                {syncMutation.data.syncedCount !== undefined ? (
                  <span>
                    ✓{" "}
                    {syncMutation.data.message ||
                      `Sync complete: ${syncMutation.data.syncedCount}/${syncMutation.data.totalCount ?? syncMutation.data.syncedCount} connected`}
                  </span>
                ) : (
                  <span>
                    ✓ Sync complete: {syncMutation.data.added ?? 0} added,{" "}
                    {syncMutation.data.skipped ?? 0} skipped
                    {(syncMutation.data.failed ?? 0) > 0 && (
                      <span
                        style={{
                          color: "var(--danger)",
                          marginLeft: "0.35rem",
                        }}
                      >
                        ({syncMutation.data.failed} failed)
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
                {conn.url && (
                  <a
                    href={conn.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="provider-card-action"
                    title={`Open ${conn.name} Web UI (${conn.url})`}
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
                      title: "Delete Connection",
                      message: `Are you sure you want to delete the connection "${conn.name}"?`,
                      danger: true,
                      confirmText: t("settingsTabs.categories.deleteConfirm"),
                    });
                    if (!ok) return;

                    deleteMutation.mutate(conn.id, {
                      onSuccess: () =>
                        showToast(`Connection "${conn.name}" deleted`, "info"),
                      onError: (err: any) =>
                        showToast(
                          err?.message || "Failed to delete connection",
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
                    Sync
                  </span>
                )}
                {conn.enableAutomaticAdd && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    Auto Add
                  </span>
                )}
                {conn.webhookEnabled && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    Webhook
                  </span>
                )}
              </div>
              <div className="provider-card-info">{conn.url}</div>
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

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
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
              {editing.id ? "Edit Connection" : "Add Connection"}
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
              label={t("settingsTabs.indexers.apiKeyLabel")}
              value={editing.apiKey || ""}
              onChange={(v) => setEditing({ ...editing, apiKey: v })}
              type="password"
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
                hint="Hostname or IP for *arr to reach Leecharr (leave empty to use default)"
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
                <span>Testing connection to {editing.url || "server"}...</span>
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
                  onClick={() => setEditing(null)}
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
    </>
  );
}
