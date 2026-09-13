import React, { useState, useMemo } from "react";
import {
  useAutomationScripts,
  useCreateAutomationScript,
  useUpdateAutomationScript,
  useDeleteAutomationScript,
  useRunAutomationScript,
  useTestAutomationScript,
  useAutomationMarketplace,
  useInstallMarketplaceTemplate,
  useTorrents,
  useCategories,
  useTags,
} from "../api/hooks";
import type {
  AutomationScript,
  AutomationTrigger,
  AutomationLanguage,
  AutomationMarketplaceTemplate,
  AutomationExecutionResult,
} from "../api/types";

export function AutomationPage() {
  const { data: scripts, isLoading: loadingScripts } = useAutomationScripts();
  const { data: templates, isLoading: loadingTemplates } = useAutomationMarketplace();
  const { data: torrents } = useTorrents();
  const { data: categories } = useCategories();
  const { data: tags } = useTags();

  const createScript = useCreateAutomationScript();
  const updateScript = useUpdateAutomationScript();
  const deleteScript = useDeleteAutomationScript();
  const runScript = useRunAutomationScript();
  const testScript = useTestAutomationScript();
  const installTemplate = useInstallMarketplaceTemplate();

  const [activeTab, setActiveTab] = useState<"scripts" | "marketplace">("scripts");
  const [selectedCategoryFilter, setSelectedCategoryFilter] = useState<string>("All");
  const [selectedTriggerFilter, setSelectedTriggerFilter] = useState<string>("All");
  const [searchQuery, setSearchQuery] = useState("");

  // Modals state
  const [editorOpen, setEditorOpen] = useState(false);
  const [editingScript, setEditingScript] = useState<Partial<AutomationScript> | null>(null);
  const [logModalOpen, setLogModalOpen] = useState(false);
  const [viewingLog, setViewingLog] = useState<{ name: string; log: string; status: string } | null>(null);
  const [installModalOpen, setInstallModalOpen] = useState(false);
  const [selectedTemplate, setSelectedTemplate] = useState<AutomationMarketplaceTemplate | null>(null);
  const [templateInputs, setTemplateInputs] = useState<Record<string, string>>({});
  const [customInstallName, setCustomInstallName] = useState("");

  // Test Runner state in Editor
  const [testTorrentId, setTestTorrentId] = useState<number | undefined>(undefined);
  const [testResult, setTestResult] = useState<AutomationExecutionResult | null>(null);
  const [isTesting, setIsTesting] = useState(false);
  const [isRunningId, setIsRunningId] = useState<number | null>(null);

  const scriptList = scripts || [];
  const templateList = templates || [];

  const filteredScripts = useMemo(() => {
    return scriptList.filter((s) => {
      if (selectedTriggerFilter !== "All" && s.trigger.toString() !== selectedTriggerFilter) {
        return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase();
        const matchName = s.name.toLowerCase().includes(q);
        const matchDesc = s.description?.toLowerCase().includes(q) || false;
        if (!matchName && !matchDesc) return false;
      }
      return true;
    });
  }, [scriptList, selectedTriggerFilter, searchQuery]);

  const filteredTemplates = useMemo(() => {
    return templateList.filter((t) => {
      if (selectedCategoryFilter !== "All" && t.category !== selectedCategoryFilter) {
        return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase();
        const matchName = t.name.toLowerCase().includes(q);
        const matchDesc = t.description.toLowerCase().includes(q);
        if (!matchName && !matchDesc) return false;
      }
      return true;
    });
  }, [templateList, selectedCategoryFilter, searchQuery]);

  const marketplaceCategories = useMemo(() => {
    const set = new Set<string>();
    templateList.forEach((t) => set.add(t.category));
    return ["All", ...Array.from(set)];
  }, [templateList]);

  // Statistics
  const activeScriptsCount = scriptList.filter((s) => s.isEnabled).length;

  function openNewScript() {
    setEditingScript({
      name: "New Automation Script",
      description: "",
      trigger: "TorrentAdded",
      language: "JavaScript",
      code: `// Contexts available: torrent, system, api, http, html, inputs, secrets, console
console.log('Processing torrent: ' + (torrent ? torrent.name : 'System Event'));

if (torrent) {
  // Example: Tag large torrents
  if (torrent.size > 5000000000) {
    torrent.addTag('Large');
  }
}

// Example: Run internal command or call internal API
// system.runCommand('Backup');
// const health = api.get('health');
`,
      inputsJson: "{}",
      isEnabled: true,
      targetCategories: [],
      targetTagIds: [],
    });
    setTestResult(null);
    setEditorOpen(true);
  }

  function openEditScript(script: AutomationScript) {
    setEditingScript({ ...script });
    setTestResult(null);
    setEditorOpen(true);
  }

  function handleSaveScript() {
    if (!editingScript || !editingScript.name?.trim()) return;

    if (editingScript.id && editingScript.id > 0) {
      updateScript.mutate(editingScript as AutomationScript, {
        onSuccess: () => setEditorOpen(false),
      });
    } else {
      createScript.mutate(editingScript, {
        onSuccess: () => setEditorOpen(false),
      });
    }
  }

  function handleDeleteScript(id: number) {
    if (window.confirm("Are you sure you want to delete this automation script?")) {
      deleteScript.mutate(id);
    }
  }

  function handleToggleEnabled(script: AutomationScript) {
    updateScript.mutate({
      ...script,
      isEnabled: !script.isEnabled,
    });
  }

  function handleRunNow(id: number, torrentId?: number) {
    setIsRunningId(id);
    runScript.mutate(
      { id, torrentId },
      {
        onSuccess: (res) => {
          setIsRunningId(null);
          setViewingLog({
            name: `Manual Run - Script #${id}`,
            log: res.outputLog || (res.success ? "Completed with no output." : (res.error || "Failed")),
            status: res.success ? "Success" : "Failed",
          });
          setLogModalOpen(true);
        },
        onError: (err) => {
          setIsRunningId(null);
          alert(`Execution error: ${err.message}`);
        },
      }
    );
  }

  function handleTestScript() {
    if (!editingScript) return;
    setIsTesting(true);

    let parsedInputs: Record<string, unknown> = {};
    if (editingScript.inputsJson) {
      try {
        parsedInputs = JSON.parse(editingScript.inputsJson);
      } catch (e) {
        alert("Invalid JSON in Inputs schema.");
        setIsTesting(false);
        return;
      }
    }

    testScript.mutate(
      {
        script: editingScript,
        torrentId: testTorrentId,
        customInputs: parsedInputs,
      },
      {
        onSuccess: (res) => {
          setTestResult(res);
          setIsTesting(false);
        },
        onError: (err) => {
          setIsTesting(false);
          alert(`Test failed: ${err.message}`);
        },
      }
    );
  }

  function openInstallModal(template: AutomationMarketplaceTemplate) {
    setSelectedTemplate(template);
    setCustomInstallName(template.name);
    const initialInputs: Record<string, string> = {};
    template.inputFields.forEach((f) => {
      initialInputs[f.key] = f.defaultValue;
    });
    setTemplateInputs(initialInputs);
    setInstallModalOpen(true);
  }

  function handleInstallSubmit() {
    if (!selectedTemplate) return;
    installTemplate.mutate(
      {
        templateId: selectedTemplate.id,
        customName: customInstallName || selectedTemplate.name,
        customInputs: templateInputs,
      },
      {
        onSuccess: () => {
          setInstallModalOpen(false);
          setActiveTab("scripts");
        },
        onError: (err) => {
          alert(`Installation failed: ${err.message}`);
        },
      }
    );
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
          <h1 style={{ fontSize: "1.75rem", fontWeight: 700, margin: 0, display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span>⚡</span> Automation & Community Marketplace
          </h1>
          <p style={{ color: "var(--text-muted, #888)", margin: "0.25rem 0 0 0", fontSize: "0.9rem" }}>
            Event-driven low-code scripting engine (JavaScript & YAML) and community workflow registry.
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem" }}>
          <button
            className={`btn ${activeTab === "scripts" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("scripts")}
          >
            My Scripts ({scriptList.length})
          </button>
          <button
            className={`btn ${activeTab === "marketplace" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("marketplace")}
          >
            🛍️ Marketplace ({templateList.length})
          </button>
          <button className="btn btn-action" onClick={openNewScript}>
            + New Script
          </button>
        </div>
      </div>

      {/* Top Stats Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
          gap: "1rem",
          marginBottom: "1.5rem",
        }}
      >
        <div className="panel" style={{ padding: "1rem" }}>
          <div style={{ fontSize: "0.8rem", color: "var(--text-muted, #888)" }}>Total Installed Scripts</div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700 }}>{scriptList.length}</div>
        </div>
        <div className="panel" style={{ padding: "1rem" }}>
          <div style={{ fontSize: "0.8rem", color: "var(--text-muted, #888)" }}>Active Triggers</div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700, color: "#10b981" }}>{activeScriptsCount} Active</div>
        </div>
        <div className="panel" style={{ padding: "1rem" }}>
          <div style={{ fontSize: "0.8rem", color: "var(--text-muted, #888)" }}>Community Templates</div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700, color: "#6366f1" }}>{templateList.length} Ready to Install</div>
        </div>
        <div className="panel" style={{ padding: "1rem" }}>
          <div style={{ fontSize: "0.8rem", color: "var(--text-muted, #888)" }}>Supported Engines</div>
          <div style={{ fontSize: "1.1rem", fontWeight: 600, marginTop: "0.25rem" }}>JavaScript (Jint) & YAML</div>
        </div>
      </div>

      {/* Search & Filters */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          gap: "1rem",
          flexWrap: "wrap",
        }}
      >
        <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", alignItems: "center" }}>
          <input
            type="text"
            className="input"
            placeholder="Search scripts or templates..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            style={{ width: "240px" }}
          />

          {activeTab === "scripts" ? (
            <select
              className="input"
              value={selectedTriggerFilter}
              onChange={(e) => setSelectedTriggerFilter(e.target.value)}
            >
              <option value="All">All Triggers</option>
              <option value="TorrentAdded">On Torrent Added</option>
              <option value="TorrentCompleted">On Download Complete</option>
              <option value="RatioReached">On Ratio Reached</option>
              <option value="TorrentError">On Torrent Error</option>
              <option value="Manual">Manual Only</option>
              <option value="Scheduled">Scheduled</option>
            </select>
          ) : (
            <div style={{ display: "flex", gap: "0.25rem" }}>
              {marketplaceCategories.map((cat) => (
                <button
                  key={cat}
                  className={`btn btn-sm ${selectedCategoryFilter === cat ? "btn-primary" : "btn-secondary"}`}
                  onClick={() => setSelectedCategoryFilter(cat)}
                >
                  {cat}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* TAB CONTENT: My Scripts */}
      {activeTab === "scripts" && (
        <div>
          {loadingScripts ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center" }}>Loading automation scripts...</div>
          ) : filteredScripts.length === 0 ? (
            <div className="panel" style={{ padding: "3rem", textAlign: "center" }}>
              <h3>No Automation Scripts Found</h3>
              <p style={{ color: "var(--text-muted, #888)" }}>
                Get started by creating your own script or install one from the Community Marketplace!
              </p>
              <div style={{ display: "flex", gap: "0.5rem", justifyContent: "center", marginTop: "1rem" }}>
                <button className="btn btn-primary" onClick={openNewScript}>+ Create Script</button>
                <button className="btn btn-secondary" onClick={() => setActiveTab("marketplace")}>Browse Marketplace</button>
              </div>
            </div>
          ) : (
            <div style={{ display: "grid", gap: "0.75rem" }}>
              {filteredScripts.map((script) => (
                <div
                  key={script.id}
                  className="panel"
                  style={{
                    padding: "1rem 1.25rem",
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    flexWrap: "wrap",
                    gap: "1rem",
                    borderLeft: script.isEnabled ? "4px solid #10b981" : "4px solid #6b7280",
                  }}
                >
                  <div style={{ flex: 1, minWidth: "280px" }}>
                    <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                      <span style={{ fontWeight: 600, fontSize: "1.1rem" }}>{script.name}</span>
                      <span className="badge" style={{ backgroundColor: "#3b82f6", color: "#fff", fontSize: "0.75rem" }}>
                        {script.trigger}
                      </span>
                      <span className="badge" style={{ backgroundColor: "#8b5cf6", color: "#fff", fontSize: "0.75rem" }}>
                        {script.language}
                      </span>
                      {script.lastExecutionStatus && (
                        <button
                          className="badge"
                          style={{
                            backgroundColor: script.lastExecutionStatus === "Success" ? "#10b981" : "#ef4444",
                            color: "#fff",
                            cursor: "pointer",
                            border: "none",
                            fontSize: "0.75rem",
                          }}
                          onClick={() => {
                            setViewingLog({
                              name: script.name,
                              log: script.lastExecutionLog || "No log content",
                              status: script.lastExecutionStatus || "Unknown",
                            });
                            setLogModalOpen(true);
                          }}
                          title="Click to view execution log"
                        >
                          {script.lastExecutionStatus} 📜
                        </button>
                      )}
                    </div>

                    {script.description && (
                      <p style={{ color: "var(--text-muted, #888)", margin: "0.25rem 0", fontSize: "0.85rem" }}>
                        {script.description}
                      </p>
                    )}

                    <div style={{ display: "flex", gap: "0.5rem", fontSize: "0.8rem", color: "var(--text-muted, #888)", marginTop: "0.25rem" }}>
                      {script.lastExecutedAt ? (
                        <span>Last run: {new Date(script.lastExecutedAt).toLocaleString()}</span>
                      ) : (
                        <span>Never executed</span>
                      )}
                      {script.targetCategories && script.targetCategories.length > 0 && (
                        <span>• Categories: {script.targetCategories.join(", ")}</span>
                      )}
                    </div>
                  </div>

                  <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                    <button
                      className={`btn btn-sm ${script.isEnabled ? "btn-secondary" : "btn-outline"}`}
                      onClick={() => handleToggleEnabled(script)}
                      title={script.isEnabled ? "Disable Script" : "Enable Script"}
                    >
                      {script.isEnabled ? "Active" : "Disabled"}
                    </button>

                    <button
                      className="btn btn-sm btn-primary"
                      onClick={() => handleRunNow(script.id)}
                      disabled={isRunningId === script.id}
                    >
                      {isRunningId === script.id ? "Running..." : "▶ Run Now"}
                    </button>

                    <button className="btn btn-sm btn-secondary" onClick={() => openEditScript(script)}>
                      Edit
                    </button>

                    <button className="btn btn-sm btn-danger" onClick={() => handleDeleteScript(script.id)}>
                      Delete
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* TAB CONTENT: Community Marketplace */}
      {activeTab === "marketplace" && (
        <div>
          {loadingTemplates ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center" }}>Loading community templates...</div>
          ) : (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))", gap: "1rem" }}>
              {filteredTemplates.map((template) => (
                <div
                  key={template.id}
                  className="panel"
                  style={{
                    padding: "1.25rem",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                  }}
                >
                  <div>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                      <span className="badge" style={{ backgroundColor: "#4f46e5", color: "#fff", fontSize: "0.75rem" }}>
                        {template.category}
                      </span>
                      <span style={{ fontSize: "0.75rem", color: "var(--text-muted, #888)" }}>v{template.version}</span>
                    </div>

                    <h3 style={{ fontSize: "1.1rem", margin: "0.5rem 0 0.25rem 0" }}>{template.name}</h3>
                    <p style={{ color: "var(--text-muted, #888)", fontSize: "0.85rem", minHeight: "40px" }}>
                      {template.description}
                    </p>

                    <div style={{ display: "flex", gap: "0.5rem", marginBottom: "1rem", flexWrap: "wrap" }}>
                      <span className="badge" style={{ backgroundColor: "#3b82f6", color: "#fff", fontSize: "0.7rem" }}>
                        Trigger: {template.trigger}
                      </span>
                      <span className="badge" style={{ backgroundColor: "#8b5cf6", color: "#fff", fontSize: "0.7rem" }}>
                        {template.language}
                      </span>
                      <span style={{ fontSize: "0.75rem", color: "var(--text-muted, #888)", alignSelf: "center" }}>
                        By {template.author}
                      </span>
                    </div>
                  </div>

                  <button
                    className="btn btn-primary"
                    style={{ width: "100%" }}
                    onClick={() => openInstallModal(template)}
                  >
                    ⬇️ Install Script
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* SCRIPT EDITOR MODAL */}
      {editorOpen && editingScript && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "rgba(0,0,0,0.6)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
            padding: "1rem",
          }}
        >
          <div
            className="panel"
            style={{
              width: "100%",
              maxWidth: "900px",
              maxHeight: "90vh",
              display: "flex",
              flexDirection: "column",
              padding: "1.5rem",
              overflow: "hidden",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
              <h2 style={{ margin: 0, fontSize: "1.25rem" }}>
                {editingScript.id ? "Edit Automation Script" : "Create Automation Script"}
              </h2>
              <button className="btn btn-sm btn-secondary" onClick={() => setEditorOpen(false)}>✕</button>
            </div>

            <div style={{ overflowY: "auto", flex: 1, paddingRight: "0.5rem" }}>
              {/* Form Row 1: Name & Trigger */}
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "0.75rem", marginBottom: "0.75rem" }}>
                <div>
                  <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Script Name</label>
                  <input
                    type="text"
                    className="input"
                    value={editingScript.name || ""}
                    onChange={(e) => setEditingScript({ ...editingScript, name: e.target.value })}
                    placeholder="e.g. Auto-Zap Bonus Points"
                  />
                </div>

                <div>
                  <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Trigger Event</label>
                  <select
                    className="input"
                    value={editingScript.trigger?.toString()}
                    onChange={(e) => setEditingScript({ ...editingScript, trigger: e.target.value as AutomationTrigger })}
                  >
                    <option value="TorrentAdded">📥 On Torrent Added (Grab/Import)</option>
                    <option value="TorrentCompleted">✅ On Download Completed (100%)</option>
                    <option value="RatioReached">🎯 On Ratio / Seed Goal Reached</option>
                    <option value="TorrentStatusChanged">🔄 On Torrent State Changed</option>
                    <option value="TorrentDeleted">🗑️ On Torrent Deleted</option>
                    <option value="TorrentError">⚠️ On Torrent Error / Health Issue</option>
                    <option value="HealthRestored">💚 On Health Restored</option>
                    <option value="MediaEnriched">🎬 On Media Enriched (Metadata/Poster)</option>
                    <option value="ArchiveExtracted">📦 On Archive Extracted (.rar/.zip)</option>
                    <option value="ExtractionFailed">❌ On Archive Extraction Failed</option>
                    <option value="VpnDisconnected">🛡️ On VPN KillSwitch Triggered</option>
                    <option value="VpnRestored">🌐 On VPN Interface Restored</option>
                    <option value="CategoryChanged">📂 On Category Path Updated</option>
                    <option value="ApplicationStarted">🚀 On Application Started</option>
                    <option value="Scheduled">⏱️ Scheduled Interval</option>
                    <option value="Manual">🖐️ Manual Only</option>
                  </select>
                </div>

                <div>
                  <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Language</label>
                  <select
                    className="input"
                    value={editingScript.language?.toString()}
                    onChange={(e) => setEditingScript({ ...editingScript, language: e.target.value as AutomationLanguage })}
                  >
                    <option value="JavaScript">JavaScript (Sandboxed Jint)</option>
                    <option value="Yaml">YAML (Declarative Steps)</option>
                  </select>
                </div>
              </div>

              {/* Description */}
              <div style={{ marginBottom: "0.75rem" }}>
                <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Description</label>
                <input
                  type="text"
                  className="input"
                  value={editingScript.description || ""}
                  onChange={(e) => setEditingScript({ ...editingScript, description: e.target.value })}
                  placeholder="Short note about what this script does..."
                />
              </div>

              {/* Code Editor */}
              <div style={{ marginBottom: "0.75rem" }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.25rem" }}>
                  <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>
                    Script Code ({editingScript.language})
                  </label>
                  <span style={{ fontSize: "0.75rem", color: "var(--text-muted, #888)" }}>
                    Globals: <code>torrent</code>, <code>http</code>, <code>html</code>, <code>inputs</code>, <code>secrets</code>, <code>console</code>
                  </span>
                </div>
                <textarea
                  className="input"
                  rows={12}
                  style={{
                    fontFamily: "monospace",
                    fontSize: "0.85rem",
                    width: "100%",
                    whiteSpace: "pre",
                    backgroundColor: "var(--bg-input, #1e1e2e)",
                    color: "var(--text-input, #f8f8f2)",
                  }}
                  value={editingScript.code || ""}
                  onChange={(e) => setEditingScript({ ...editingScript, code: e.target.value })}
                />
              </div>

              {/* Inputs Schema JSON */}
              <div style={{ marginBottom: "0.75rem" }}>
                <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Inputs / Secrets JSON</label>
                <textarea
                  className="input"
                  rows={3}
                  style={{ fontFamily: "monospace", fontSize: "0.8rem", width: "100%" }}
                  value={editingScript.inputsJson || "{}"}
                  onChange={(e) => setEditingScript({ ...editingScript, inputsJson: e.target.value })}
                  placeholder='{"api_key": "secret", "target_ratio": "3.0"}'
                />
              </div>

              {/* Test Runner Bar */}
              <div className="panel" style={{ padding: "0.75rem", backgroundColor: "var(--bg-card-hover, rgba(255,255,255,0.03))", marginBottom: "0.75rem" }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem" }}>
                  <span style={{ fontWeight: 600, fontSize: "0.85rem" }}>🧪 Live Test & Dry Run Console</span>
                  <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                    <select
                      className="input"
                      style={{ fontSize: "0.8rem", width: "220px" }}
                      value={testTorrentId || ""}
                      onChange={(e) => setTestTorrentId(e.target.value ? Number(e.target.value) : undefined)}
                    >
                      <option value="">Sample Torrent (Empty)</option>
                      {(torrents || []).map((t) => (
                        <option key={t.id} value={t.id}>{t.name.substring(0, 30)}</option>
                      ))}
                    </select>

                    <button
                      className="btn btn-sm btn-primary"
                      onClick={handleTestScript}
                      disabled={isTesting}
                    >
                      {isTesting ? "Executing..." : "Run Test"}
                    </button>
                  </div>
                </div>

                {testResult && (
                  <div style={{ fontSize: "0.8rem" }}>
                    <div style={{ display: "flex", gap: "1rem", marginBottom: "0.25rem" }}>
                      <span>
                        Result: <strong style={{ color: testResult.success ? "#10b981" : "#ef4444" }}>{testResult.success ? "SUCCESS" : "FAILED"}</strong>
                      </span>
                      <span>Time: <strong>{testResult.executionTimeMs}ms</strong></span>
                      {testResult.tagsToAdd?.length > 0 && <span>Tags Added: <strong>{testResult.tagsToAdd.join(", ")}</strong></span>}
                      {testResult.newCategory && <span>New Category: <strong>{testResult.newCategory}</strong></span>}
                    </div>

                    <pre
                      style={{
                        backgroundColor: "#0d1117",
                        color: "#c9d1d9",
                        padding: "0.5rem",
                        borderRadius: "4px",
                        maxHeight: "120px",
                        overflowY: "auto",
                        margin: 0,
                      }}
                    >
                      {testResult.outputLog || testResult.error || "No output logs produced."}
                    </pre>
                  </div>
                )}
              </div>
            </div>

            {/* Modal Footer */}
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.5rem", marginTop: "1rem", paddingTop: "0.5rem", borderTop: "1px solid var(--border-color, #333)" }}>
              <button className="btn btn-secondary" onClick={() => setEditorOpen(false)}>Cancel</button>
              <button className="btn btn-primary" onClick={handleSaveScript}>Save Script</button>
            </div>
          </div>
        </div>
      )}

      {/* INSTALL TEMPLATE MODAL */}
      {installModalOpen && selectedTemplate && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "rgba(0,0,0,0.6)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
            padding: "1rem",
          }}
        >
          <div className="panel" style={{ width: "100%", maxWidth: "550px", padding: "1.5rem" }}>
            <h2 style={{ margin: "0 0 0.5rem 0", fontSize: "1.25rem" }}>Install "{selectedTemplate.name}"</h2>
            <p style={{ color: "var(--text-muted, #888)", fontSize: "0.85rem", marginBottom: "1rem" }}>
              {selectedTemplate.description}
            </p>

            <div style={{ marginBottom: "0.75rem" }}>
              <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Script Name</label>
              <input
                type="text"
                className="input"
                value={customInstallName}
                onChange={(e) => setCustomInstallName(e.target.value)}
              />
            </div>

            {selectedTemplate.inputFields.length > 0 && (
              <div style={{ marginBottom: "1rem" }}>
                <h4 style={{ fontSize: "0.9rem", marginBottom: "0.5rem" }}>Template Parameters</h4>
                <div style={{ display: "grid", gap: "0.5rem" }}>
                  {selectedTemplate.inputFields.map((field) => (
                    <div key={field.key}>
                      <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>{field.label}</label>
                      <input
                        type={field.type === "password" ? "password" : "text"}
                        className="input"
                        value={templateInputs[field.key] ?? ""}
                        onChange={(e) => setTemplateInputs({ ...templateInputs, [field.key]: e.target.value })}
                        placeholder={field.description}
                      />
                      {field.description && (
                        <div style={{ fontSize: "0.75rem", color: "var(--text-muted, #888)" }}>{field.description}</div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.5rem", marginTop: "1rem" }}>
              <button className="btn btn-secondary" onClick={() => setInstallModalOpen(false)}>Cancel</button>
              <button className="btn btn-primary" onClick={handleInstallSubmit}>Confirm & Install</button>
            </div>
          </div>
        </div>
      )}

      {/* LOG MODAL */}
      {logModalOpen && viewingLog && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "rgba(0,0,0,0.6)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
            padding: "1rem",
          }}
        >
          <div className="panel" style={{ width: "100%", maxWidth: "700px", padding: "1.5rem" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
              <h3 style={{ margin: 0, fontSize: "1.1rem" }}>Execution Log: {viewingLog.name}</h3>
              <span
                className="badge"
                style={{
                  backgroundColor: viewingLog.status === "Success" ? "#10b981" : "#ef4444",
                  color: "#fff",
                }}
              >
                {viewingLog.status}
              </span>
            </div>

            <pre
              style={{
                backgroundColor: "#0d1117",
                color: "#58a6ff",
                padding: "1rem",
                borderRadius: "6px",
                maxHeight: "350px",
                overflowY: "auto",
                fontFamily: "monospace",
                fontSize: "0.85rem",
              }}
            >
              {viewingLog.log}
            </pre>

            <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "1rem" }}>
              <button className="btn btn-primary" onClick={() => setLogModalOpen(false)}>Close</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
