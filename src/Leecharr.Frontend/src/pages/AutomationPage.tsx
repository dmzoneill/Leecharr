import React, { useState, useMemo, useEffect } from "react";
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

// Visual Pipeline Interfaces
interface VisualAction {
  id: string;
  type: "addTag" | "removeTag" | "setCategory" | "pause" | "resume" | "remove" | "recheck" | "reannounce" | "command";
  value: string;
  deleteData?: boolean;
}

interface VisualHttp {
  method: "GET" | "POST" | "PUT" | "DELETE";
  url: string;
  json: boolean;
  body: string;
  register: string;
}

interface VisualStep {
  id: string;
  name: string;
  conditionEnabled: boolean;
  conditionLeft: string;
  conditionOp: ">" | "<" | ">=" | "<=" | "==" | "!=";
  conditionRight: string;
  hasHttp: boolean;
  http: VisualHttp;
  actions: VisualAction[];
}

const COMMON_COMMANDS = [
  { name: "Backup", desc: "Create full database & config backup" },
  { name: "SyncArr", desc: "Sync connected Sonarr / Radarr instances" },
  { name: "WatchFolderScan", desc: "Scan watch folder for torrents" },
  { name: "TrackerBoostScan", desc: "Scan & optimize candidate trackers" },
  { name: "BlocklistUpdate", desc: "Update peer IP blocklist" },
  { name: "GeoIpUpdate", desc: "Update MaxMind GeoIP database" },
  { name: "RssSync", desc: "Poll RSS indexers for releases" },
];

const TRIGGER_LABELS: Record<string, string> = {
  TorrentAdded: "📥 On Torrent Added",
  TorrentCompleted: "✅ On Download Completed",
  RatioReached: "🎯 On Ratio / Seed Goal Reached",
  TorrentStatusChanged: "🔄 On Torrent State Changed",
  TorrentDeleted: "🗑️ On Torrent Deleted",
  TorrentError: "⚠️ On Torrent Error",
  HealthRestored: "💚 On Health Restored",
  MediaEnriched: "🎬 On Media Enriched",
  ArchiveExtracted: "📦 On Archive Extracted",
  ExtractionFailed: "❌ On Extraction Failed",
  VpnDisconnected: "🛡️ On VPN KillSwitch",
  VpnRestored: "🌐 On VPN Restored",
  CategoryChanged: "📂 On Category Changed",
  ApplicationStarted: "🚀 On App Started",
  Scheduled: "⏱️ Scheduled Interval",
  Manual: "🖐️ Manual Only",
};

// Convert Visual Steps to YAML DSL string
function visualStepsToYaml(pipelineName: string, trigger: string, steps: VisualStep[]): string {
  let yaml = `name: '${pipelineName.replace(/'/g, "''")}'\n`;
  yaml += `trigger: '${trigger}'\n`;
  yaml += `steps:\n`;

  if (steps.length === 0) {
    yaml += `  - name: 'Empty Step'\n    actions: []\n`;
    return yaml;
  }

  for (const step of steps) {
    yaml += `  - name: '${(step.name || "Step").replace(/'/g, "''")}'\n`;
    if (step.conditionEnabled && step.conditionLeft && step.conditionRight) {
      yaml += `    condition: '${step.conditionLeft} ${step.conditionOp} ${step.conditionRight}'\n`;
    }

    if (step.hasHttp && step.http.url.trim()) {
      yaml += `    http:\n`;
      yaml += `      method: '${step.http.method}'\n`;
      yaml += `      url: '${step.http.url.replace(/'/g, "''")}'\n`;
      if (step.http.json) {
        yaml += `      json: true\n`;
      }
      if (step.http.body.trim()) {
        try {
          const parsed = JSON.parse(step.http.body);
          yaml += `      body:\n`;
          for (const [k, v] of Object.entries(parsed)) {
            yaml += `        ${k}: '${String(v).replace(/'/g, "''")}'\n`;
          }
        } catch {
          yaml += `      body: '${step.http.body.replace(/'/g, "''")}'\n`;
        }
      }
      if (step.http.register.trim()) {
        yaml += `    register: '${step.http.register.trim()}'\n`;
      }
    }

    if (step.actions.length > 0) {
      yaml += `    actions:\n`;
      for (const act of step.actions) {
        if (act.type === "addTag") {
          yaml += `      - addTag: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "removeTag") {
          yaml += `      - removeTag: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "setCategory") {
          yaml += `      - setCategory: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "command") {
          yaml += `      - command: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "pause") {
          yaml += `      - pause: true\n`;
        } else if (act.type === "resume") {
          yaml += `      - resume: true\n`;
        } else if (act.type === "recheck") {
          yaml += `      - recheck: true\n`;
        } else if (act.type === "reannounce") {
          yaml += `      - reannounce: true\n`;
        } else if (act.type === "remove") {
          yaml += `      - remove: true\n`;
          if (act.deleteData) {
            yaml += `        deleteData: true\n`;
          }
        }
      }
    }
  }

  return yaml;
}

// Convert YAML DSL to Visual Steps
function yamlToVisualSteps(code: string): VisualStep[] {
  if (!code || !code.includes("steps:")) {
    return [
      {
        id: "step-1",
        name: "Check and Tag",
        conditionEnabled: true,
        conditionLeft: "${torrent.size}",
        conditionOp: ">",
        conditionRight: "1000000000",
        hasHttp: false,
        http: { method: "POST", url: "", json: true, body: "{}", register: "" },
        actions: [
          { id: "act-1", type: "addTag", value: "Large-Download" },
        ],
      },
    ];
  }

  const steps: VisualStep[] = [];
  const lines = code.split("\n");
  let currentStep: VisualStep | null = null;
  let inActions = false;
  let inHttp = false;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();

    if (trimmed.startsWith("- name:")) {
      if (currentStep) {
        steps.push(currentStep);
      }
      const nameMatch = trimmed.match(/- name:\s*['"]?([^'"]+)['"]?/);
      currentStep = {
        id: `step-${steps.length + 1}-${Date.now()}`,
        name: nameMatch ? nameMatch[1] : "Step",
        conditionEnabled: false,
        conditionLeft: "${torrent.size}",
        conditionOp: ">",
        conditionRight: "1000000000",
        hasHttp: false,
        http: { method: "POST", url: "", json: true, body: "{}", register: "" },
        actions: [],
      };
      inActions = false;
      inHttp = false;
    } else if (currentStep && trimmed.startsWith("condition:")) {
      const condMatch = trimmed.match(/condition:\s*['"]?([^'"]+)['"]?/);
      if (condMatch) {
        currentStep.conditionEnabled = true;
        const expr = condMatch[1].trim();
        const opMatch = expr.match(/(>=|<=|==|!=|>|<)/);
        if (opMatch) {
          const op = opMatch[1] as any;
          const parts = expr.split(op);
          currentStep.conditionLeft = parts[0]?.trim() || "${torrent.size}";
          currentStep.conditionOp = op;
          currentStep.conditionRight = parts[1]?.trim() || "0";
        }
      }
    } else if (currentStep && trimmed.startsWith("http:")) {
      currentStep.hasHttp = true;
      inHttp = true;
      inActions = false;
    } else if (currentStep && inHttp && trimmed.startsWith("method:")) {
      const m = trimmed.match(/method:\s*['"]?([^'"]+)['"]?/);
      if (m && currentStep) currentStep.http.method = m[1] as any;
    } else if (currentStep && inHttp && trimmed.startsWith("url:")) {
      const u = trimmed.match(/url:\s*['"]?([^'"]+)['"]?/);
      if (u && currentStep) currentStep.http.url = u[1];
    } else if (currentStep && inHttp && trimmed.startsWith("register:")) {
      const r = trimmed.match(/register:\s*['"]?([^'"]+)['"]?/);
      if (r && currentStep) currentStep.http.register = r[1];
    } else if (currentStep && trimmed.startsWith("actions:")) {
      inActions = true;
      inHttp = false;
    } else if (currentStep && inActions && trimmed.startsWith("- addTag:")) {
      const v = trimmed.match(/- addTag:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "addTag", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- removeTag:")) {
      const v = trimmed.match(/- removeTag:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "removeTag", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setCategory:")) {
      const v = trimmed.match(/- setCategory:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setCategory", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- command:")) {
      const v = trimmed.match(/- command:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "command", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- pause:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "pause", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- resume:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "resume", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- recheck:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "recheck", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- reannounce:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "reannounce", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- remove:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "remove", value: "", deleteData: false });
    }
  }

  if (currentStep) {
    steps.push(currentStep);
  }

  return steps.length > 0
    ? steps
    : [
        {
          id: "step-default",
          name: "Action Step",
          conditionEnabled: false,
          conditionLeft: "${torrent.size}",
          conditionOp: ">",
          conditionRight: "0",
          hasHttp: false,
          http: { method: "POST", url: "", json: true, body: "{}", register: "" },
          actions: [{ id: "act-1", type: "addTag", value: "Processed" }],
        },
      ];
}

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

  const [activeTab, setActiveTab] = useState<"scripts" | "marketplace" | "history">("scripts");
  const [selectedCategoryFilter, setSelectedCategoryFilter] = useState<string>("All");
  const [selectedTriggerFilter, setSelectedTriggerFilter] = useState<string>("All");
  const [searchQuery, setSearchQuery] = useState("");

  // Modals state
  const [editorOpen, setEditorOpen] = useState(false);
  const [editingScript, setEditingScript] = useState<Partial<AutomationScript> | null>(null);
  const [editorMode, setEditorMode] = useState<"visual" | "code">("visual");
  const [visualSteps, setVisualSteps] = useState<VisualStep[]>([]);

  const [logModalOpen, setLogModalOpen] = useState(false);
  const [viewingLog, setViewingLog] = useState<{
    name: string;
    trigger?: string;
    time?: string;
    log: string;
    status: string;
    durationMs?: number;
  } | null>(null);

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

  // Sync visual steps when editing script opens
  useEffect(() => {
    if (editingScript) {
      if (editingScript.language === "Yaml" || editingScript.language === 1) {
        setVisualSteps(yamlToVisualSteps(editingScript.code || ""));
      } else {
        setEditorMode("code");
      }
    }
  }, [editingScript?.id, editingScript?.language]);

  // Sync visual steps back to YAML code
  function updateVisualSteps(newSteps: VisualStep[]) {
    setVisualSteps(newSteps);
    if (editingScript) {
      const generatedYaml = visualStepsToYaml(
        editingScript.name || "Automation Pipeline",
        editingScript.trigger?.toString() || "TorrentCompleted",
        newSteps
      );
      setEditingScript({ ...editingScript, code: generatedYaml });
    }
  }

  function openNewScript(language: "Yaml" | "JavaScript" = "Yaml") {
    const initialSteps: VisualStep[] = [
      {
        id: "step-1",
        name: "Filter Large Torrents",
        conditionEnabled: true,
        conditionLeft: "${torrent.size}",
        conditionOp: ">",
        conditionRight: "5000000000",
        hasHttp: false,
        http: { method: "POST", url: "", json: true, body: "{}", register: "" },
        actions: [
          { id: "act-1", type: "addTag", value: "Large-Release" },
          { id: "act-2", type: "command", value: "Backup" },
        ],
      },
    ];

    const initialCode = language === "Yaml"
      ? visualStepsToYaml("New Automation Pipeline", "TorrentCompleted", initialSteps)
      : `// JavaScript Contexts: torrent, system, api, http, html, inputs, secrets, console
console.log('Processing torrent: ' + (torrent ? torrent.name : 'System Event'));

if (torrent) {
  if (torrent.size > 5000000000) {
    torrent.addTag('Large-Release');
  }
}

// Execute system command
// system.runCommand('Backup');
`;

    setEditingScript({
      name: language === "Yaml" ? "New Automation Pipeline" : "New Custom Script",
      description: "",
      trigger: "TorrentCompleted",
      language: language,
      code: initialCode,
      inputsJson: "{}",
      isEnabled: true,
      targetCategories: [],
      targetTagIds: [],
    });
    setVisualSteps(initialSteps);
    setEditorMode(language === "Yaml" ? "visual" : "code");
    setTestResult(null);
    setEditorOpen(true);
  }

  function openEditScript(script: AutomationScript) {
    setEditingScript({ ...script });
    const isYaml = script.language === "Yaml" || script.language === 1;
    if (isYaml) {
      setVisualSteps(yamlToVisualSteps(script.code || ""));
      setEditorMode("visual");
    } else {
      setEditorMode("code");
    }
    setTestResult(null);
    setEditorOpen(true);
  }

  function handleSaveScript() {
    if (!editingScript || !editingScript.name?.trim()) return;

    let finalScript = { ...editingScript };
    if ((finalScript.language === "Yaml" || finalScript.language === 1) && editorMode === "visual") {
      finalScript.code = visualStepsToYaml(
        finalScript.name || "Pipeline",
        finalScript.trigger?.toString() || "TorrentCompleted",
        visualSteps
      );
    }

    if (finalScript.id && finalScript.id > 0) {
      updateScript.mutate(finalScript as AutomationScript, {
        onSuccess: () => setEditorOpen(false),
      });
    } else {
      createScript.mutate(finalScript, {
        onSuccess: () => setEditorOpen(false),
      });
    }
  }

  function handleDeleteScript(id: number) {
    if (window.confirm("Are you sure you want to delete this automation pipeline?")) {
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
    const targetScript = scriptList.find((s) => s.id === id);
    runScript.mutate(
      { id, torrentId },
      {
        onSuccess: (res) => {
          setIsRunningId(null);
          setViewingLog({
            name: targetScript?.name || `Script #${id}`,
            trigger: targetScript?.trigger?.toString() || "Manual",
            time: new Date().toLocaleTimeString(),
            log: res.outputLog || (res.success ? "Completed with no output." : (res.error || "Failed")),
            status: res.success ? "Success" : "Failed",
            durationMs: res.executionTimeMs,
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

  function handleDryRun() {
    if (!editingScript) return;
    setIsTesting(true);
    setTestResult(null);

    let scriptPayload = { ...editingScript };
    if ((scriptPayload.language === "Yaml" || scriptPayload.language === 1) && editorMode === "visual") {
      scriptPayload.code = visualStepsToYaml(
        scriptPayload.name || "Pipeline",
        scriptPayload.trigger?.toString() || "TorrentCompleted",
        visualSteps
      );
    }

    testScript.mutate(
      {
        script: scriptPayload,
        torrentId: testTorrentId || (torrents && torrents[0]?.id),
      },
      {
        onSuccess: (res) => {
          setTestResult(res);
          setIsTesting(false);
        },
        onError: (err) => {
          setIsTesting(false);
          setTestResult({
            success: false,
            error: err.message,
            executionTimeMs: 0,
            tagsToAdd: [],
            tagsToRemove: [],
            shouldPause: false,
            shouldResume: false,
            shouldRemove: false,
            deleteDataOnRemove: false,
          });
        },
      }
    );
  }

  function handleInstallTemplate() {
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
            <span>⚡</span> Automation Pipelines & Marketplace
          </h1>
          <p style={{ color: "var(--text-muted, #888)", margin: "0.25rem 0 0 0", fontSize: "0.9rem" }}>
            Visual drag-and-drop workflow builder, low-code scripting engine, and community pipeline registry.
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem" }}>
          <button
            className={`btn ${activeTab === "scripts" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("scripts")}
          >
            📋 My Pipelines ({scriptList.length})
          </button>
          <button
            className={`btn ${activeTab === "history" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("history")}
          >
            📊 Run History
          </button>
          <button
            className={`btn ${activeTab === "marketplace" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("marketplace")}
          >
            🛍️ Marketplace ({templateList.length})
          </button>
        </div>
      </div>

      {/* TAB 1: MY SCRIPTS / PIPELINES */}
      {activeTab === "scripts" && (
        <div>
          {/* Action Bar */}
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
            <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
              <input
                type="text"
                placeholder="Search pipelines..."
                className="input"
                style={{ width: "240px" }}
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />
              <select
                className="input"
                style={{ width: "190px" }}
                value={selectedTriggerFilter}
                onChange={(e) => setSelectedTriggerFilter(e.target.value)}
              >
                <option value="All">All Triggers</option>
                {Object.entries(TRIGGER_LABELS).map(([k, label]) => (
                  <option key={k} value={k}>{label}</option>
                ))}
              </select>
            </div>

            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button className="btn btn-primary" onClick={() => openNewScript("Yaml")}>
                ✨ + Visual Pipeline Builder
              </button>
              <button className="btn btn-secondary" onClick={() => openNewScript("JavaScript")}>
                💻 + JavaScript Script
              </button>
            </div>
          </div>

          {loadingScripts ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
              Loading automation pipelines...
            </div>
          ) : filteredScripts.length === 0 ? (
            <div className="panel" style={{ padding: "3rem", textAlign: "center" }}>
              <div style={{ fontSize: "2.5rem", marginBottom: "0.5rem" }}>⚙️</div>
              <h3 style={{ margin: "0 0 0.5rem 0" }}>No Automation Pipelines Found</h3>
              <p style={{ color: "var(--text-muted)", maxWidth: "480px", margin: "0 auto 1.5rem auto" }}>
                Create automated actions for when torrents are added, completed, hit ratio goals, or browse the Community Marketplace.
              </p>
              <div style={{ display: "flex", gap: "0.5rem", justifyContent: "center" }}>
                <button className="btn btn-primary" onClick={() => openNewScript("Yaml")}>
                  ✨ Build Visual Pipeline
                </button>
                <button className="btn btn-secondary" onClick={() => setActiveTab("marketplace")}>
                  🛍️ Browse Marketplace
                </button>
              </div>
            </div>
          ) : (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(360px, 1fr))", gap: "1rem" }}>
              {filteredScripts.map((script) => (
                <div
                  key={script.id}
                  className="panel"
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    borderLeft: `4px solid ${script.isEnabled ? "var(--accent, #3b82f6)" : "var(--border, #444)"}`,
                    padding: "1.25rem",
                  }}
                >
                  <div>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "0.5rem" }}>
                      <div>
                        <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 600 }}>{script.name}</h3>
                        <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                          {script.language === 1 || script.language === "Yaml" ? "📦 Visual Pipeline (YAML)" : "💻 JavaScript"}
                        </span>
                      </div>
                      <label style={{ display: "flex", alignItems: "center", cursor: "pointer", gap: "0.35rem" }}>
                        <input
                          type="checkbox"
                          checked={script.isEnabled}
                          onChange={() => handleToggleEnabled(script)}
                        />
                        <span style={{ fontSize: "0.8rem", fontWeight: 600 }}>
                          {script.isEnabled ? "Active" : "Disabled"}
                        </span>
                      </label>
                    </div>

                    <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 0.75rem 0", minHeight: "2.4rem" }}>
                      {script.description || "No description provided."}
                    </p>

                    <div style={{ display: "flex", flexWrap: "wrap", gap: "0.35rem", marginBottom: "1rem" }}>
                      <span className="badge" style={{ backgroundColor: "rgba(59, 130, 246, 0.15)", color: "#60a5fa" }}>
                        {TRIGGER_LABELS[script.trigger.toString()] || script.trigger.toString()}
                      </span>
                      {script.targetCategories && script.targetCategories.length > 0 && (
                        <span className="badge" style={{ backgroundColor: "rgba(168, 85, 247, 0.15)", color: "#c084fc" }}>
                          📁 {script.targetCategories.join(", ")}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Footer metadata & buttons */}
                  <div>
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        fontSize: "0.75rem",
                        color: "var(--text-muted)",
                        borderTop: "1px solid var(--border, #333)",
                        paddingTop: "0.75rem",
                        marginBottom: "0.75rem",
                      }}
                    >
                      <span>
                        Last Run:{" "}
                        {script.lastExecutedAt
                          ? new Date(script.lastExecutedAt).toLocaleTimeString()
                          : "Never"}
                      </span>
                      {script.lastExecutionStatus && (
                        <span
                          style={{
                            fontWeight: 600,
                            color: script.lastExecutionStatus === "Success" ? "#22c55e" : "#ef4444",
                          }}
                        >
                          {script.lastExecutionStatus === "Success" ? "● Succeeded" : "● Failed"}
                        </span>
                      )}
                    </div>

                    <div style={{ display: "flex", gap: "0.4rem", justifyContent: "flex-end" }}>
                      {script.lastExecutionLog && (
                        <button
                          className="btn btn-sm btn-secondary"
                          onClick={() => {
                            setViewingLog({
                              name: script.name,
                              trigger: script.trigger.toString(),
                              time: script.lastExecutedAt ? new Date(script.lastExecutedAt).toLocaleString() : undefined,
                              log: script.lastExecutionLog || "",
                              status: script.lastExecutionStatus || "Unknown",
                            });
                            setLogModalOpen(true);
                          }}
                          title="View Execution Log"
                        >
                          📜 Logs
                        </button>
                      )}
                      <button
                        className="btn btn-sm btn-secondary"
                        onClick={() => handleRunNow(script.id)}
                        disabled={isRunningId === script.id}
                        title="Trigger run immediately"
                      >
                        {isRunningId === script.id ? "⏳ Running..." : "▶️ Run"}
                      </button>
                      <button className="btn btn-sm btn-secondary" onClick={() => openEditScript(script)}>
                        ✏️ Edit
                      </button>
                      <button
                        className="btn btn-sm btn-danger"
                        onClick={() => handleDeleteScript(script.id)}
                        title="Delete Script"
                      >
                        🗑️
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* TAB 2: PIPELINE RUN HISTORY & VIEWER */}
      {activeTab === "history" && (
        <div>
          <div className="panel" style={{ padding: "1.5rem" }}>
            <h2 style={{ fontSize: "1.2rem", margin: "0 0 1rem 0" }}>📊 Pipeline Execution History</h2>
            <p style={{ color: "var(--text-muted)", fontSize: "0.9rem", marginBottom: "1.5rem" }}>
              Detailed execution traces and step-by-step performance metrics for recent pipeline runs.
            </p>

            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.9rem" }}>
              <thead>
                <tr style={{ borderBottom: "1px solid var(--border)", textAlign: "left" }}>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Status</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Pipeline Name</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Trigger</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Executed At</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {scriptList.filter((s) => s.lastExecutedAt).length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ textAlign: "center", padding: "2rem", color: "var(--text-muted)" }}>
                      No pipeline execution runs recorded yet. Trigger a run or wait for an event.
                    </td>
                  </tr>
                ) : (
                  scriptList
                    .filter((s) => s.lastExecutedAt)
                    .sort((a, b) => (new Date(b.lastExecutedAt!).getTime() - new Date(a.lastExecutedAt!).getTime()))
                    .map((s) => (
                      <tr key={s.id} style={{ borderBottom: "1px solid var(--border)" }}>
                        <td style={{ padding: "0.75rem 0.5rem" }}>
                          <span
                            className="badge"
                            style={{
                              backgroundColor: s.lastExecutionStatus === "Success" ? "rgba(34, 197, 94, 0.15)" : "rgba(239, 68, 68, 0.15)",
                              color: s.lastExecutionStatus === "Success" ? "#22c55e" : "#ef4444",
                            }}
                          >
                            {s.lastExecutionStatus === "Success" ? "✅ Success" : "❌ Failed"}
                          </span>
                        </td>
                        <td style={{ padding: "0.75rem 0.5rem", fontWeight: 600 }}>{s.name}</td>
                        <td style={{ padding: "0.75rem 0.5rem" }}>
                          <span className="badge">{TRIGGER_LABELS[s.trigger.toString()] || s.trigger.toString()}</span>
                        </td>
                        <td style={{ padding: "0.75rem 0.5rem", color: "var(--text-muted)" }}>
                          {s.lastExecutedAt ? new Date(s.lastExecutedAt).toLocaleString() : "Unknown"}
                        </td>
                        <td style={{ padding: "0.75rem 0.5rem" }}>
                          <button
                            className="btn btn-sm btn-secondary"
                            onClick={() => {
                              setViewingLog({
                                name: s.name,
                                trigger: s.trigger.toString(),
                                time: s.lastExecutedAt ? new Date(s.lastExecutedAt).toLocaleString() : undefined,
                                log: s.lastExecutionLog || "",
                                status: s.lastExecutionStatus || "Unknown",
                              });
                              setLogModalOpen(true);
                            }}
                          >
                            🔍 Trace Inspector
                          </button>
                        </td>
                      </tr>
                    ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 3: MARKETPLACE */}
      {activeTab === "marketplace" && (
        <div>
          {/* Marketplace Category Filters */}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem", flexWrap: "wrap", gap: "0.5rem" }}>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
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

            <input
              type="text"
              placeholder="Search community templates..."
              className="input"
              style={{ width: "240px" }}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          {loadingTemplates ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
              Loading marketplace catalog...
            </div>
          ) : (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(360px, 1fr))", gap: "1rem" }}>
              {filteredTemplates.map((template) => (
                <div
                  key={template.id}
                  className="panel"
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    padding: "1.25rem",
                  }}
                >
                  <div>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "0.5rem" }}>
                      <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 600 }}>{template.name}</h3>
                      <span className="badge" style={{ backgroundColor: "rgba(59, 130, 246, 0.15)", color: "#60a5fa" }}>
                        v{template.version}
                      </span>
                    </div>

                    <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginBottom: "0.5rem" }}>
                      by <span style={{ fontWeight: 600, color: "var(--text-primary)" }}>{template.author}</span> •{" "}
                      <span className="badge" style={{ fontSize: "0.7rem" }}>{template.category}</span>
                    </div>

                    <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 1rem 0" }}>
                      {template.description}
                    </p>
                  </div>

                  <button
                    className="btn btn-primary"
                    style={{ width: "100%" }}
                    onClick={() => {
                      setSelectedTemplate(template);
                      setTemplateInputs({ ...template.defaultInputs });
                      setCustomInstallName(template.name);
                      setInstallModalOpen(true);
                    }}
                  >
                    ⬇️ Install Pipeline
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* SCRIPT & PIPELINE EDITOR MODAL */}
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
              maxWidth: "1000px",
              maxHeight: "92vh",
              display: "flex",
              flexDirection: "column",
              padding: "1.5rem",
              overflow: "hidden",
            }}
          >
            {/* Modal Header */}
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
              <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
                <h2 style={{ margin: 0, fontSize: "1.25rem" }}>
                  {editingScript.id ? "Edit Automation Pipeline" : "Create Automation Pipeline"}
                </h2>
                {/* Visual vs Code Mode Toggle */}
                <div style={{ display: "flex", backgroundColor: "var(--surface, #222)", borderRadius: "6px", padding: "2px" }}>
                  <button
                    type="button"
                    className={`btn btn-sm ${editorMode === "visual" ? "btn-primary" : "btn-secondary"}`}
                    style={{ padding: "0.25rem 0.75rem", fontSize: "0.8rem" }}
                    onClick={() => {
                      if (editorMode !== "visual") {
                        setVisualSteps(yamlToVisualSteps(editingScript.code || ""));
                        setEditorMode("visual");
                      }
                    }}
                  >
                    ✨ Visual Pipeline Builder
                  </button>
                  <button
                    type="button"
                    className={`btn btn-sm ${editorMode === "code" ? "btn-primary" : "btn-secondary"}`}
                    style={{ padding: "0.25rem 0.75rem", fontSize: "0.8rem" }}
                    onClick={() => {
                      if (editorMode !== "code") {
                        const generatedYaml = visualStepsToYaml(
                          editingScript.name || "Pipeline",
                          editingScript.trigger?.toString() || "TorrentCompleted",
                          visualSteps
                        );
                        setEditingScript({ ...editingScript, code: generatedYaml });
                        setEditorMode("code");
                      }
                    }}
                  >
                    💻 Code / YAML View
                  </button>
                </div>
              </div>
              <button className="btn btn-sm btn-secondary" onClick={() => setEditorOpen(false)}>✕</button>
            </div>

            <div style={{ overflowY: "auto", flex: 1, paddingRight: "0.5rem" }}>
              {/* Form Row 1: Name, Trigger, Language */}
              <div style={{ display: "grid", gridTemplateColumns: "1.5fr 1fr 1fr", gap: "0.75rem", marginBottom: "0.75rem" }}>
                <div>
                  <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Pipeline Name</label>
                  <input
                    type="text"
                    className="input"
                    value={editingScript.name || ""}
                    onChange={(e) => setEditingScript({ ...editingScript, name: e.target.value })}
                    placeholder="e.g. 4K Movie Auto-Zap & Backup"
                  />
                </div>

                <div>
                  <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Trigger Event</label>
                  <select
                    className="input"
                    value={editingScript.trigger?.toString()}
                    onChange={(e) => setEditingScript({ ...editingScript, trigger: e.target.value as AutomationTrigger })}
                  >
                    {Object.entries(TRIGGER_LABELS).map(([k, label]) => (
                      <option key={k} value={k}>{label}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Engine / Format</label>
                  <select
                    className="input"
                    value={editingScript.language?.toString()}
                    onChange={(e) => {
                      const newLang = e.target.value as AutomationLanguage;
                      setEditingScript({ ...editingScript, language: newLang });
                      if (newLang === "JavaScript") {
                        setEditorMode("code");
                      }
                    }}
                  >
                    <option value="Yaml">YAML (Visual Pipeline DSL)</option>
                    <option value="JavaScript">JavaScript (Sandboxed Jint)</option>
                  </select>
                </div>
              </div>

              {/* Form Row 2: Description */}
              <div style={{ marginBottom: "0.75rem" }}>
                <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Description</label>
                <input
                  type="text"
                  className="input"
                  value={editingScript.description || ""}
                  onChange={(e) => setEditingScript({ ...editingScript, description: e.target.value })}
                  placeholder="Summary of what this automation pipeline performs..."
                />
              </div>

              {/* EDITOR MODE 1: VISUAL PIPELINE BUILDER */}
              {editorMode === "visual" && (
                <div style={{ marginBottom: "1rem" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem" }}>
                    <label style={{ fontSize: "0.85rem", fontWeight: 700, color: "var(--accent, #3b82f6)" }}>
                      ✨ Pipeline Steps ({visualSteps.length})
                    </label>
                    <button
                      type="button"
                      className="btn btn-sm btn-secondary"
                      onClick={() => {
                        const newStep: VisualStep = {
                          id: `step-${visualSteps.length + 1}-${Date.now()}`,
                          name: `Step ${visualSteps.length + 1}`,
                          conditionEnabled: false,
                          conditionLeft: "${torrent.size}",
                          conditionOp: ">",
                          conditionRight: "1000000000",
                          hasHttp: false,
                          http: { method: "POST", url: "", json: true, body: "{}", register: "" },
                          actions: [{ id: `act-${Date.now()}`, type: "addTag", value: "Auto-Tagged" }],
                        };
                        updateVisualSteps([...visualSteps, newStep]);
                      }}
                    >
                      ➕ Add Step
                    </button>
                  </div>

                  {visualSteps.map((step, stepIdx) => (
                    <div
                      key={step.id}
                      className="panel"
                      style={{
                        marginBottom: "1rem",
                        backgroundColor: "var(--surface, #1e1e24)",
                        border: "1px solid var(--border, #333)",
                        padding: "1rem",
                        borderRadius: "8px",
                      }}
                    >
                      {/* Step Header */}
                      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.75rem" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                          <span
                            style={{
                              backgroundColor: "var(--accent, #3b82f6)",
                              color: "#fff",
                              borderRadius: "50%",
                              width: "22px",
                              height: "22px",
                              display: "inline-flex",
                              alignItems: "center",
                              justifyContent: "center",
                              fontSize: "0.75rem",
                              fontWeight: 700,
                            }}
                          >
                            {stepIdx + 1}
                          </span>
                          <input
                            type="text"
                            className="input"
                            style={{ fontWeight: 600, width: "260px", padding: "0.2rem 0.5rem" }}
                            value={step.name}
                            onChange={(e) => {
                              const copy = [...visualSteps];
                              copy[stepIdx].name = e.target.value;
                              updateVisualSteps(copy);
                            }}
                            placeholder="Step Name"
                          />
                        </div>

                        <div style={{ display: "flex", gap: "0.25rem" }}>
                          <button
                            type="button"
                            className="btn btn-sm btn-secondary"
                            disabled={stepIdx === 0}
                            onClick={() => {
                              const copy = [...visualSteps];
                              const temp = copy[stepIdx];
                              copy[stepIdx] = copy[stepIdx - 1];
                              copy[stepIdx - 1] = temp;
                              updateVisualSteps(copy);
                            }}
                          >
                            ⬆️
                          </button>
                          <button
                            type="button"
                            className="btn btn-sm btn-secondary"
                            disabled={stepIdx === visualSteps.length - 1}
                            onClick={() => {
                              const copy = [...visualSteps];
                              const temp = copy[stepIdx];
                              copy[stepIdx] = copy[stepIdx + 1];
                              copy[stepIdx + 1] = temp;
                              updateVisualSteps(copy);
                            }}
                          >
                            ⬇️
                          </button>
                          <button
                            type="button"
                            className="btn btn-sm btn-danger"
                            onClick={() => {
                              const copy = visualSteps.filter((_, idx) => idx !== stepIdx);
                              updateVisualSteps(copy);
                            }}
                          >
                            🗑️
                          </button>
                        </div>
                      </div>

                      {/* Condition Builder */}
                      <div
                        style={{
                          backgroundColor: "rgba(0,0,0,0.2)",
                          padding: "0.6rem",
                          borderRadius: "6px",
                          marginBottom: "0.75rem",
                        }}
                      >
                        <label style={{ display: "flex", alignItems: "center", gap: "0.5rem", fontSize: "0.8rem", fontWeight: 600, cursor: "pointer" }}>
                          <input
                            type="checkbox"
                            checked={step.conditionEnabled}
                            onChange={(e) => {
                              const copy = [...visualSteps];
                              copy[stepIdx].conditionEnabled = e.target.checked;
                              updateVisualSteps(copy);
                            }}
                          />
                          Only run this step if condition matches (IF condition)
                        </label>

                        {step.conditionEnabled && (
                          <div style={{ display: "flex", gap: "0.5rem", marginTop: "0.5rem", alignItems: "center" }}>
                            <select
                              className="input"
                              style={{ width: "200px" }}
                              value={step.conditionLeft}
                              onChange={(e) => {
                                const copy = [...visualSteps];
                                copy[stepIdx].conditionLeft = e.target.value;
                                updateVisualSteps(copy);
                              }}
                            >
                              <option value="${torrent.size}">Torrent Size (bytes)</option>
                              <option value="${torrent.ratio}">Torrent Ratio</option>
                              <option value="${torrent.category}">Category Name</option>
                              <option value="${torrent.tracker}">Tracker URL</option>
                              <option value="${torrent.isPrivate}">Is Private Tracker</option>
                              <option value="${torrent.status}">Status</option>
                            </select>

                            <select
                              className="input"
                              style={{ width: "80px" }}
                              value={step.conditionOp}
                              onChange={(e) => {
                                const copy = [...visualSteps];
                                copy[stepIdx].conditionOp = e.target.value as any;
                                updateVisualSteps(copy);
                              }}
                            >
                              <option value=">">&gt;</option>
                              <option value="<">&lt;</option>
                              <option value=">=">&gt;=</option>
                              <option value="<=">&lt;=</option>
                              <option value="==">==</option>
                              <option value="!=">!=</option>
                            </select>

                            <input
                              type="text"
                              className="input"
                              style={{ flex: 1 }}
                              value={step.conditionRight}
                              onChange={(e) => {
                                const copy = [...visualSteps];
                                copy[stepIdx].conditionRight = e.target.value;
                                updateVisualSteps(copy);
                              }}
                              placeholder="e.g. 5000000000 or 'Movies'"
                            />
                          </div>
                        )}
                      </div>

                      {/* Actions List */}
                      <div>
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem" }}>
                          <span style={{ fontSize: "0.8rem", fontWeight: 600 }}>⚡ Step Actions</span>
                          <div style={{ display: "flex", gap: "0.3rem" }}>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "addTag", value: "New-Tag" });
                                updateVisualSteps(copy);
                              }}
                            >
                              🏷️ + Tag
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "setCategory", value: categories?.[0]?.name || "Movies" });
                                updateVisualSteps(copy);
                              }}
                            >
                              📁 + Category
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "command", value: "Backup" });
                                updateVisualSteps(copy);
                              }}
                            >
                              ⚙️ + System Task
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "recheck", value: "" });
                                updateVisualSteps(copy);
                              }}
                            >
                              🔍 + Recheck
                            </button>
                          </div>
                        </div>

                        {step.actions.length === 0 ? (
                          <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", fontStyle: "italic", padding: "0.4rem 0" }}>
                            No actions added. Click the buttons above to attach tags, categories, tasks, or torrent commands.
                          </div>
                        ) : (
                          step.actions.map((act, actIdx) => (
                            <div
                              key={act.id}
                              style={{
                                display: "flex",
                                alignItems: "center",
                                gap: "0.5rem",
                                marginBottom: "0.4rem",
                                backgroundColor: "rgba(255,255,255,0.03)",
                                padding: "0.4rem",
                                borderRadius: "4px",
                              }}
                            >
                              <select
                                className="input"
                                style={{ width: "160px", padding: "0.2rem" }}
                                value={act.type}
                                onChange={(e) => {
                                  const copy = [...visualSteps];
                                  copy[stepIdx].actions[actIdx].type = e.target.value as any;
                                  updateVisualSteps(copy);
                                }}
                              >
                                <option value="addTag">🏷️ Add Tag</option>
                                <option value="removeTag">🏷️ Remove Tag</option>
                                <option value="setCategory">📁 Set Category</option>
                                <option value="command">⚙️ Run System Task</option>
                                <option value="recheck">🔍 Force Recheck</option>
                                <option value="reannounce">📡 Force Reannounce</option>
                                <option value="pause">⏸️ Pause Torrent</option>
                                <option value="resume">▶️ Resume Torrent</option>
                                <option value="remove">🗑️ Remove Torrent</option>
                              </select>

                              {act.type === "command" ? (
                                <select
                                  className="input"
                                  style={{ flex: 1, padding: "0.2rem" }}
                                  value={act.value}
                                  onChange={(e) => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].actions[actIdx].value = e.target.value;
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  {COMMON_COMMANDS.map((cmd) => (
                                    <option key={cmd.name} value={cmd.name}>
                                      {cmd.name} — {cmd.desc}
                                    </option>
                                  ))}
                                </select>
                              ) : act.type === "setCategory" ? (
                                <select
                                  className="input"
                                  style={{ flex: 1, padding: "0.2rem" }}
                                  value={act.value}
                                  onChange={(e) => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].actions[actIdx].value = e.target.value;
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  {(categories || []).map((cat) => (
                                    <option key={cat.id} value={cat.name}>{cat.name}</option>
                                  ))}
                                </select>
                              ) : act.type === "addTag" || act.type === "removeTag" ? (
                                <input
                                  type="text"
                                  className="input"
                                  style={{ flex: 1, padding: "0.2rem 0.5rem" }}
                                  value={act.value}
                                  onChange={(e) => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].actions[actIdx].value = e.target.value;
                                    updateVisualSteps(copy);
                                  }}
                                  placeholder="Tag name (e.g. 4K, Radarr, Archive)"
                                />
                              ) : (
                                <span style={{ flex: 1, fontSize: "0.8rem", color: "var(--text-muted)" }}>
                                  Action applies automatically to active torrent
                                </span>
                              )}

                              <button
                                type="button"
                                className="btn btn-sm btn-secondary"
                                onClick={() => {
                                  const copy = [...visualSteps];
                                  copy[stepIdx].actions = copy[stepIdx].actions.filter((_, idx) => idx !== actIdx);
                                  updateVisualSteps(copy);
                                }}
                              >
                                ✕
                              </button>
                            </div>
                          ))
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {/* EDITOR MODE 2: CODE / YAML / JS VIEW */}
              {editorMode === "code" && (
                <div style={{ marginBottom: "1rem" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.25rem" }}>
                    <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>
                      {editingScript.language === "Yaml" || editingScript.language === 1 ? "YAML Pipeline DSL" : "JavaScript Code"}
                    </label>
                    <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                      Helpers: <code>system.runCommand()</code>, <code>api.get()</code>, <code>torrent.addTag()</code>
                    </span>
                  </div>
                  <textarea
                    className="input"
                    rows={12}
                    style={{
                      fontFamily: "monospace",
                      fontSize: "0.85rem",
                      backgroundColor: "#111",
                      color: "#e2e8f0",
                      lineHeight: "1.4",
                    }}
                    value={editingScript.code || ""}
                    onChange={(e) => setEditingScript({ ...editingScript, code: e.target.value })}
                  />
                </div>
              )}

              {/* LIVE DRY RUN & TEST RUNNER */}
              <div
                style={{
                  borderTop: "1px solid var(--border, #333)",
                  paddingTop: "1rem",
                  marginTop: "1rem",
                  backgroundColor: "rgba(0,0,0,0.15)",
                  padding: "1rem",
                  borderRadius: "6px",
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.75rem" }}>
                  <div>
                    <h4 style={{ margin: 0, fontSize: "0.95rem" }}>⚡ Live Dry Run Inspector</h4>
                    <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                      Test pipeline execution logic safely against active torrents without writing mutations.
                    </span>
                  </div>

                  <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                    <select
                      className="input"
                      style={{ width: "240px", fontSize: "0.8rem" }}
                      value={testTorrentId}
                      onChange={(e) => setTestTorrentId(e.target.value ? Number(e.target.value) : undefined)}
                    >
                      <option value="">Sample Torrent (Big Buck Bunny 1080p)</option>
                      {(torrents || []).map((t) => (
                        <option key={t.id} value={t.id}>{t.name} ({(t.totalSize / (1024 * 1024 * 1024)).toFixed(2)} GB)</option>
                      ))}
                    </select>

                    <button
                      type="button"
                      className="btn btn-sm btn-primary"
                      onClick={handleDryRun}
                      disabled={isTesting}
                    >
                      {isTesting ? "⏳ Evaluating..." : "▶️ Dry Run"}
                    </button>
                  </div>
                </div>

                {testResult && (
                  <div
                    style={{
                      backgroundColor: testResult.success ? "rgba(34, 197, 94, 0.08)" : "rgba(239, 68, 68, 0.08)",
                      border: `1px solid ${testResult.success ? "#22c55e" : "#ef4444"}`,
                      borderRadius: "6px",
                      padding: "0.75rem",
                      fontSize: "0.85rem",
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", fontWeight: 600, marginBottom: "0.5rem" }}>
                      <span style={{ color: testResult.success ? "#22c55e" : "#ef4444" }}>
                        {testResult.success ? "✅ Pipeline Dry Run Succeeded" : "❌ Pipeline Dry Run Failed"}
                      </span>
                      <span>Duration: {testResult.executionTimeMs}ms</span>
                    </div>

                    {testResult.tagsToAdd.length > 0 && (
                      <div><strong>Tags Added:</strong> {testResult.tagsToAdd.join(", ")}</div>
                    )}
                    {testResult.newCategory && (
                      <div><strong>New Category:</strong> {testResult.newCategory}</div>
                    )}
                    {testResult.shouldRecheck && (
                      <div><strong>Torrent Action:</strong> 🔍 Force Hash Recheck</div>
                    )}

                    {testResult.outputLog && (
                      <pre
                        style={{
                          marginTop: "0.5rem",
                          marginBottom: 0,
                          backgroundColor: "#000",
                          color: "#a3e635",
                          padding: "0.5rem",
                          borderRadius: "4px",
                          fontSize: "0.75rem",
                          maxHeight: "150px",
                          overflowY: "auto",
                        }}
                      >
                        {testResult.outputLog}
                      </pre>
                    )}
                  </div>
                )}
              </div>
            </div>

            {/* Modal Footer */}
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.5rem", marginTop: "1rem", borderTop: "1px solid var(--border)", paddingTop: "1rem" }}>
              <button type="button" className="btn btn-secondary" onClick={() => setEditorOpen(false)}>
                Cancel
              </button>
              <button type="button" className="btn btn-primary" onClick={handleSaveScript}>
                💾 Save Pipeline
              </button>
            </div>
          </div>
        </div>
      )}

      {/* PIPELINE RUN TRACE VIEWER MODAL */}
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
          <div
            className="panel"
            style={{
              width: "100%",
              maxWidth: "800px",
              maxHeight: "85vh",
              display: "flex",
              flexDirection: "column",
              padding: "1.5rem",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
              <div>
                <h3 style={{ margin: 0, fontSize: "1.2rem" }}>🔍 Pipeline Trace: {viewingLog.name}</h3>
                <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                  Trigger: {viewingLog.trigger} • Executed: {viewingLog.time || "Recently"}
                </span>
              </div>
              <button className="btn btn-sm btn-secondary" onClick={() => setLogModalOpen(false)}>✕</button>
            </div>

            <div style={{ marginBottom: "0.5rem", display: "flex", gap: "0.5rem" }}>
              <span
                className="badge"
                style={{
                  backgroundColor: viewingLog.status === "Success" ? "rgba(34, 197, 94, 0.15)" : "rgba(239, 68, 68, 0.15)",
                  color: viewingLog.status === "Success" ? "#22c55e" : "#ef4444",
                }}
              >
                Status: {viewingLog.status}
              </span>
            </div>

            <pre
              style={{
                flex: 1,
                overflowY: "auto",
                backgroundColor: "#0d1117",
                color: "#c9d1d9",
                padding: "1rem",
                borderRadius: "6px",
                fontFamily: "monospace",
                fontSize: "0.8rem",
                lineHeight: "1.5",
                whiteSpace: "pre-wrap",
                border: "1px solid #30363d",
              }}
            >
              {viewingLog.log}
            </pre>

            <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "1rem" }}>
              <button className="btn btn-secondary" onClick={() => setLogModalOpen(false)}>Close</button>
            </div>
          </div>
        </div>
      )}

      {/* TEMPLATE INSTALL MODAL */}
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
          <div className="panel" style={{ width: "100%", maxWidth: "600px", padding: "1.5rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>Install Community Pipeline</h3>
            <p style={{ color: "var(--text-muted)", fontSize: "0.85rem", marginBottom: "1rem" }}>
              {selectedTemplate.description}
            </p>

            <div style={{ marginBottom: "0.75rem" }}>
              <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>Pipeline Custom Name</label>
              <input
                type="text"
                className="input"
                value={customInstallName}
                onChange={(e) => setCustomInstallName(e.target.value)}
              />
            </div>

            {selectedTemplate.inputFields && selectedTemplate.inputFields.length > 0 && (
              <div style={{ borderTop: "1px solid var(--border)", paddingTop: "0.75rem", marginTop: "0.75rem" }}>
                <h4 style={{ fontSize: "0.9rem", margin: "0 0 0.5rem 0" }}>Pipeline Configuration Parameters</h4>
                {selectedTemplate.inputFields.map((field) => (
                  <div key={field.key} style={{ marginBottom: "0.75rem" }}>
                    <label style={{ fontSize: "0.8rem", fontWeight: 600 }}>{field.label}</label>
                    <input
                      type={field.type === "password" ? "password" : "text"}
                      className="input"
                      value={templateInputs[field.key] || ""}
                      onChange={(e) =>
                        setTemplateInputs({
                          ...templateInputs,
                          [field.key]: e.target.value,
                        })
                      }
                      placeholder={field.description}
                    />
                    <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>{field.description}</span>
                  </div>
                ))}
              </div>
            )}

            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.5rem", marginTop: "1.5rem" }}>
              <button className="btn btn-secondary" onClick={() => setInstallModalOpen(false)}>Cancel</button>
              <button className="btn btn-primary" onClick={handleInstallTemplate}>Install Pipeline</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
