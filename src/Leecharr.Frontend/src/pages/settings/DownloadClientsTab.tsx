import { useTranslation } from "../../i18n";
import { useState } from "react";
import {
  useDownloadClients,
  useCreateDownloadClient,
  useUpdateDownloadClient,
  useDeleteDownloadClient,
  useTestDownloadClient,
  useTestDirectDownloadClient,
  useDownloadClientSync,
} from "../../api/hooks";
import { useConfirm } from "../../context/ConfirmContext";
import { useToast } from "../../context/ToastContext";
import { useEscapeKey } from "../../hooks/useEscapeKey";
import type {
  DownloadClientDefinition,
  DownloadClientTestResult,
} from "../../api/types";
import {
  TextInput,
  SelectInput,
  Toggle,
  NumberInput,
  SectionCard,
} from "./shared";

export function DownloadClientsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const confirm = useConfirm();
  const { data: clients, isLoading } = useDownloadClients();
  const createMutation = useCreateDownloadClient();
  const updateMutation = useUpdateDownloadClient();
  const deleteMutation = useDeleteDownloadClient();
  const testMutation = useTestDownloadClient();
  const testDirectMutation = useTestDirectDownloadClient();
  const syncMutation = useDownloadClientSync();
  const [editing, setEditing] =
    useState<Partial<DownloadClientDefinition> | null>(null);
  useEscapeKey(() => setEditing(null), Boolean(editing));
  const [testResults, setTestResults] = useState<
    Record<number, DownloadClientTestResult | null>
  >({});
  const [modalTestResult, setModalTestResult] =
    useState<DownloadClientTestResult | null>(null);

  const defaultClient: Partial<DownloadClientDefinition> = {
    name: "",
    clientType: "QBitTorrent",
    host: t("settingsTabs.downloadClients.hostPlaceholder"),
    port: 8080,
    useSsl: false,
    username: "",
    password: "",
    category: "",
    enable: true,
  };

  const clientDefaults: Record<string, { port: number }> = {
    QBitTorrent: { port: 8080 },
    Transmission: { port: 9091 },
    Deluge: { port: 8112 },
  };

  const handleOpenModal = (client: Partial<DownloadClientDefinition>) => {
    setModalTestResult(null);
    setEditing({ ...client });
  };

  const handleSave = () => {
    if (!editing) return;
    if (editing.id) {
      updateMutation.mutate(editing as DownloadClientDefinition, {
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
        setModalTestResult({
          success: false,
          message: err.message || "Connection test failed",
        }),
    });
  };

  if (isLoading)
    return (
      <div className="loading">
        {t("settingsTabs.downloadClients.loadingClients")}
      </div>
    );

  return (
    <>
      <SectionCard
        title={t("settingsTabs.downloadClients.sectionTitle")}
        description={t("settingsTabs.downloadClients.sectionDescription")}
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
          <button
            className="btn btn-outline btn-small"
            onClick={() => {
              syncMutation.mutate(undefined, {
                onSuccess: (res) =>
                  showToast(
                    `Sync Complete: ${res.syncedCount || 0} torrents synchronized.`,
                    "success",
                  ),
                onError: (err: any) =>
                  showToast(
                    `Sync failed: ${err?.message || t("settingsTabs.notifications.unknownError")}`,
                    "error",
                  ),
              });
            }}
            disabled={syncMutation.isPending}
            title={t("settingsTabs.downloadClients.syncTitle")}
          >
            {syncMutation.isPending
              ? t("settingsTabs.downloadClients.syncing")
              : t("settingsTabs.downloadClients.syncTorrents")}
          </button>
        </div>

        <div className="provider-cards">
          {clients?.map((client) => (
            <div
              key={client.id}
              className="provider-card"
              onClick={() => handleOpenModal(client)}
            >
              <div className="provider-card-actions">
                {client.host && (
                  <a
                    href={`${client.useSsl ? "https" : "http"}://${client.host}${client.port ? `:${client.port}` : ""}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="provider-card-action"
                    title={`Open ${client.name} Web UI`}
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
                    handleTest(client.id);
                  }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title={t("settingsTabs.downloadClients.deleteClient")}
                  onClick={async (e) => {
                    e.stopPropagation();
                    const ok = await confirm({
                      title: t(
                        "settingsTabs.downloadClients.deleteClientTitle",
                      ),
                      message: `Are you sure you want to delete the download client "${client.name}"?`,
                      danger: true,
                      confirmText: t("settingsTabs.categories.deleteConfirm"),
                    });
                    if (!ok) return;

                    deleteMutation.mutate(client.id, {
                      onSuccess: () =>
                        showToast(
                          `Download client "${client.name}" deleted`,
                          "info",
                        ),
                      onError: (err: any) =>
                        showToast(
                          err?.message ||
                            t("settingsTabs.downloadClients.deleteFailed"),
                          "error",
                        ),
                    });
                  }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{client.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">
                  {client.clientType}
                </span>
                {client.enable && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    {t("settingsTabs.indexers.enabled")}
                  </span>
                )}
                {!client.enable && (
                  <span className="provider-card-badge provider-card-badge-gray">
                    {t("settingsTabs.categories.table.disabled")}
                  </span>
                )}
                {client.useSsl && (
                  <span className="provider-card-badge provider-card-badge-amber">
                    {t("settingsTabs.downloadClients.ssl")}
                  </span>
                )}
              </div>
              <div className="provider-card-info">
                {client.host}:{client.port}
              </div>
              {testResults[client.id]?.success === true && (
                <div className="provider-card-test provider-card-test-ok">
                  {t("settingsTabs.indexers.connectionPassed")}
                </div>
              )}
              {testResults[client.id]?.success === false && (
                <div
                  className="provider-card-test provider-card-test-fail"
                  title={testResults[client.id]?.message}
                >
                  {t("settingsTabs.indexers.connectionFailed")}
                </div>
              )}
              {testResults[client.id] === null && (
                <div className="provider-card-test provider-card-test-pending">
                  {t("settingsTabs.notifications.testing")}
                </div>
              )}
            </div>
          ))}
          <div
            className="provider-card-add"
            onClick={() => handleOpenModal(defaultClient)}
            title={t("settingsTabs.downloadClients.addClient")}
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
              {editing.id
                ? t("settingsTabs.downloadClients.editClient")
                : t("settingsTabs.downloadClients.addClient")}
            </div>
            <TextInput
              label={t("settingsTabs.categories.table.name")}
              value={editing.name || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, name: v });
              }}
              placeholder={t("settingsTabs.downloadClients.namePlaceholder")}
            />
            <SelectInput
              label={t("settingsTabs.downloadClients.clientType")}
              value={editing.clientType || "QBitTorrent"}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({
                  ...editing,
                  clientType: v,
                  port: clientDefaults[v]?.port || editing.port || 8080,
                });
              }}
              options={[
                {
                  value: "QBitTorrent",
                  label: t("settingsTabs.downloadClients.typeQBitTorrent"),
                },
                {
                  value: t("settingsTabs.downloadClients.typeTransmission"),
                  label: t("settingsTabs.downloadClients.typeTransmission"),
                },
                {
                  value: t("settingsTabs.downloadClients.typeDeluge"),
                  label: t("settingsTabs.downloadClients.typeDeluge"),
                },
              ]}
            />
            <TextInput
              label={t("settingsTabs.downloadClients.host")}
              value={editing.host || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, host: v });
              }}
              placeholder={t("settingsTabs.downloadClients.hostPlaceholder")}
            />
            <NumberInput
              label={t("settingsTabs.notifications.port")}
              value={editing.port || 8080}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, port: v });
              }}
              min={1}
              max={65535}
            />
            <Toggle
              label={t("settingsTabs.downloadClients.useSsl")}
              checked={editing.useSsl ?? false}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, useSsl: v });
              }}
            />
            <TextInput
              label={t("settingsTabs.downloadClients.username")}
              value={editing.username || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, username: v });
              }}
            />
            <TextInput
              label={t("settingsTabs.downloadClients.password")}
              value={editing.password || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, password: v });
              }}
              type="password"
            />
            <TextInput
              label={t("settingsTabs.downloadClients.category")}
              value={editing.category || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, category: v });
              }}
              hint={t("settingsTabs.downloadClients.categoryHint")}
            />
            <Toggle
              label={t("settingsTabs.indexers.enabled")}
              checked={editing.enable ?? true}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, enable: v });
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
                <span>
                  Testing connection to{" "}
                  {editing.host ||
                    t("settingsTabs.downloadClients.hostPlaceholder")}
                  :{editing.port || 8080}...
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
