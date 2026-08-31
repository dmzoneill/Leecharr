import { useState } from "react";
import {
  useIndexers,
  useCreateIndexer,
  useUpdateIndexer,
  useDeleteIndexer,
  useTestIndexer,
  useTestDirectIndexer,
} from "../../api/hooks";
import type { IndexerDefinition, IndexerTestResult } from "../../api/types";
import { TextInput, SelectInput, Toggle, SectionCard } from "./shared";

export function IndexersTab() {
  const { data: indexers, isLoading } = useIndexers();
  const createMutation = useCreateIndexer();
  const updateMutation = useUpdateIndexer();
  const deleteMutation = useDeleteIndexer();
  const testMutation = useTestIndexer();
  const testDirectMutation = useTestDirectIndexer();
  const [editing, setEditing] = useState<Partial<IndexerDefinition> | null>(
    null,
  );
  const [testResults, setTestResults] = useState<
    Record<number, boolean | null>
  >({});
  const [modalTestResult, setModalTestResult] =
    useState<IndexerTestResult | null>(null);

  const defaultIndexer: Partial<IndexerDefinition> = {
    name: "Prowlarr",
    indexerType: "Prowlarr",
    url: "http://prowlarr:9696",
    apiKey: "",
    apiPath: "/api",
    enableRss: true,
    enableSearch: true,
    categories: "",
    downloadClientId: 0,
    enable: true,
  };

  const handleSave = () => {
    if (!editing) return;
    const name = editing.name?.trim() || editing.indexerType || "Indexer";
    const payload = {
      ...editing,
      name,
      enable: editing.enable ?? true,
      implementation: `${editing.indexerType || "Prowlarr"}Indexer`,
      configContract: "IndexerDefinition",
    };
    if (editing.id) {
      updateMutation.mutate(payload as IndexerDefinition, {
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
    testDirectMutation.mutate(editing, {
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

  if (isLoading) return <div className="loading">Loading indexers...</div>;

  return (
    <>
      <SectionCard
        title="Torznab & Newznab Indexers"
        description="Configure Prowlarr, Jackett, or standalone Torznab/Newznab indexers for automated releases"
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
                  title="Test Connection"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleTest(idx.id);
                  }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete Indexer"
                  onClick={(e) => {
                    e.stopPropagation();
                    deleteMutation.mutate(idx.id);
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
                    RSS
                  </span>
                )}
                {idx.enableSearch && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    Search
                  </span>
                )}
              </div>
              <div className="provider-card-info">{idx.url}</div>
              {testResults[idx.id] === true && (
                <div className="provider-card-test provider-card-test-ok">
                  ✓ Connection passed
                </div>
              )}
              {testResults[idx.id] === false && (
                <div className="provider-card-test provider-card-test-fail">
                  ✕ Connection failed
                </div>
              )}
              {testResults[idx.id] === null && (
                <div className="provider-card-test provider-card-test-pending">
                  Testing...
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
            title="Add Indexer"
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
              {editing.id ? "Edit Indexer" : "Add Indexer"}
            </div>
            <TextInput
              label="Name"
              value={editing.name || ""}
              onChange={(v) => {
                setEditing({ ...editing, name: v });
                setModalTestResult(null);
              }}
              placeholder="My Prowlarr"
            />
            <SelectInput
              label="Type"
              value={editing.indexerType || "Prowlarr"}
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
                { value: "Prowlarr", label: "Prowlarr" },
                { value: "Torznab", label: "Torznab" },
                { value: "Newznab", label: "Newznab" },
              ]}
            />
            <TextInput
              label="URL"
              value={editing.url || ""}
              onChange={(v) => {
                setEditing({ ...editing, url: v });
                setModalTestResult(null);
              }}
              placeholder="http://localhost:9696"
            />
            <TextInput
              label="API Key"
              value={editing.apiKey || ""}
              onChange={(v) => {
                setEditing({ ...editing, apiKey: v });
                setModalTestResult(null);
              }}
              type="password"
            />
            <TextInput
              label="API Path"
              value={editing.apiPath || "/api"}
              onChange={(v) => {
                setEditing({ ...editing, apiPath: v });
                setModalTestResult(null);
              }}
              placeholder="/api"
            />
            <TextInput
              label="Categories"
              value={editing.categories || ""}
              onChange={(v) => {
                setEditing({ ...editing, categories: v });
                setModalTestResult(null);
              }}
              placeholder="2000,5000"
            />
            <Toggle
              label="Enable"
              checked={editing.enable ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enable: v });
                setModalTestResult(null);
              }}
            />
            <Toggle
              label="RSS"
              checked={editing.enableRss ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enableRss: v });
                setModalTestResult(null);
              }}
            />
            <Toggle
              label="Search"
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
                      ? "Connection Successful"
                      : "Connection Failed"}
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
                  ? "Testing..."
                  : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  className="btn btn-outline btn-small"
                  onClick={() => {
                    setEditing(null);
                    setModalTestResult(null);
                  }}
                >
                  Cancel
                </button>
                <button
                  className="btn btn-primary btn-small"
                  onClick={handleSave}
                  disabled={
                    createMutation.isPending || updateMutation.isPending
                  }
                >
                  {createMutation.isPending || updateMutation.isPending
                    ? "Saving..."
                    : "Save"}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
