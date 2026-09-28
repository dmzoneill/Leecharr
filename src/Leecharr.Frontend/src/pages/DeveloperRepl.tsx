import React, { useState, useEffect, useCallback, useRef } from "react";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import type {
  ReplExecutionRequest,
  ReplExecutionResponse,
  ReplHistoryEntry,
} from "../api/types";

const PRESET_SCRIPTS = [
  {
    name: "System Status & Memory",
    code: `const mem = 100 * 1024 * 1024;\nconsole.log("Evaluating buffer:", mem);\n({ status: "Online", uptimeSeconds: 3600 });`,
  },
  {
    name: "Math & Bit Manipulation",
    code: `const pieceSize = 1048576;\nconst totalBytes = 4294967296;\nconst pieces = Math.ceil(totalBytes / pieceSize);\nconsole.log("Total Pieces:", pieces);\n({ totalBytes, pieceSize, pieces });`,
  },
  {
    name: "Custom Diagnostics Probe",
    code: `console.log("Checking host services...");\nconst res = { ping: "pong", timestamp: new Date().toISOString() };\nres;`,
  },
];

export default function DeveloperRepl() {
  const { t } = useTranslation();
  const [code, setCode] = useState(PRESET_SCRIPTS[0].code);
  const [response, setResponse] = useState<ReplExecutionResponse | null>(null);
  const [history, setHistory] = useState<ReplHistoryEntry[]>([]);
  const [isExecuting, setIsExecuting] = useState(false);
  const [activeTab, setActiveTab] = useState<"result" | "logs">("result");
  const [error, setError] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const fetchHistory = useCallback(async () => {
    try {
      const data = await apiClient.get<ReplHistoryEntry[]>("/system/developer/repl/history?limit=30");
      setHistory(data || []);
    } catch {
      // Ignored
    }
  }, []);

  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  const handleExecute = async () => {
    if (!code.trim()) return;
    setIsExecuting(true);
    setError(null);

    try {
      const payload: ReplExecutionRequest = {
        code,
        language: "javascript",
        timeoutSeconds: 15,
      };

      const res = await apiClient.post<ReplExecutionResponse>("/system/developer/repl/eval", payload);
      setResponse(res);
      if (res.output && (!res.resultJson || res.resultJson === "undefined")) {
        setActiveTab("logs");
      } else {
        setActiveTab("result");
      }
      fetchHistory();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(msg || "REPL execution failed.");
    } finally {
      setIsExecuting(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
      e.preventDefault();
      handleExecute();
    }
  };

  const handleClearHistory = async () => {
    try {
      await apiClient.delete("/system/developer/repl/history");
      setHistory([]);
    } catch {
      // Ignored
    }
  };

  const handleResetSession = async () => {
    try {
      await apiClient.delete("/system/developer/repl/session");
      setResponse(null);
      setHistory([]);
      setCode(PRESET_SCRIPTS[0].code);
    } catch {
      // Ignored
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 border-b border-border pb-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <span>⚡</span> {t("developer.repl", "Interactive Sandbox REPL")}
          </h1>
          <p className="text-sm text-text-secondary mt-1">
            Execute sandboxed JavaScript scripts and diagnostic probes directly against runtime host objects.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={handleResetSession}
            className="px-3 py-1.5 bg-surface text-rose-400 border border-border rounded-md text-xs hover:bg-rose-500/10 font-medium"
          >
            Reset Session
          </button>
        </div>
      </div>

      {error && (
        <div className="p-3 bg-red-500/15 border border-red-500/30 text-red-400 rounded-md text-sm">
          {error}
        </div>
      )}

      {/* Preset Pickers */}
      <div className="flex items-center gap-2 overflow-x-auto pb-1 text-xs">
        <span className="text-text-secondary font-medium shrink-0">Sample Templates:</span>
        {PRESET_SCRIPTS.map((preset) => (
          <button
            key={preset.name}
            onClick={() => setCode(preset.code)}
            className="px-2.5 py-1 bg-surface border border-border hover:border-accent text-text rounded text-xs transition-colors shrink-0"
          >
            {preset.name}
          </button>
        ))}
      </div>

      {/* Main REPL Workbench */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Editor Column */}
        <div className="lg:col-span-7 flex flex-col space-y-2">
          <div className="flex justify-between items-center text-xs text-text-secondary">
            <span>Script Editor (Ctrl+Enter to evaluate)</span>
            <span>Timeout: 15s</span>
          </div>

          <div className="relative rounded-lg border border-border bg-black/40 overflow-hidden focus-within:border-accent">
            <textarea
              ref={textareaRef}
              value={code}
              onChange={(e) => setCode(e.target.value)}
              onKeyDown={handleKeyDown}
              rows={14}
              className="w-full p-4 bg-transparent text-emerald-400 font-mono text-sm resize-none focus:outline-none leading-relaxed"
              placeholder="// Write JavaScript expression or script here..."
              spellCheck={false}
            />
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button
              onClick={handleExecute}
              disabled={isExecuting || !code.trim()}
              className="px-5 py-2 bg-accent text-white font-semibold text-sm rounded-md shadow hover:opacity-90 disabled:opacity-50 flex items-center gap-2"
            >
              {isExecuting ? "⏳ Evaluating..." : "▶ Evaluate (Ctrl+Enter)"}
            </button>
          </div>
        </div>

        {/* Output Column */}
        <div className="lg:col-span-5 flex flex-col space-y-2">
          <div className="flex justify-between items-center text-xs">
            <div className="flex gap-2">
              <button
                onClick={() => setActiveTab("result")}
                className={`px-3 py-1 rounded font-medium ${
                  activeTab === "result"
                    ? "bg-accent text-white"
                    : "text-text-secondary hover:text-text bg-surface"
                }`}
              >
                Result JSON
              </button>
              <button
                onClick={() => setActiveTab("logs")}
                className={`px-3 py-1 rounded font-medium ${
                  activeTab === "logs"
                    ? "bg-accent text-white"
                    : "text-text-secondary hover:text-text bg-surface"
                }`}
              >
                Console Logs
              </button>
            </div>
            {response && (
              <span className="text-text-secondary font-mono text-[11px]">
                {response.durationMs}ms · {response.resultType}
              </span>
            )}
          </div>

          <div className="flex-1 min-h-[300px] rounded-lg border border-border bg-black/50 p-4 font-mono text-xs overflow-auto">
            {!response ? (
              <div className="h-full flex items-center justify-center text-text-secondary">
                No expression evaluated yet.
              </div>
            ) : response.errorMessage ? (
              <div className="text-rose-400 whitespace-pre-wrap font-semibold">
                Error: {response.errorMessage}
              </div>
            ) : activeTab === "result" ? (
              <pre className="text-cyan-300 whitespace-pre-wrap">{response.resultJson || "undefined"}</pre>
            ) : (
              <pre className="text-emerald-300 whitespace-pre-wrap">
                {response.output || "[No console.log output produced]"}
              </pre>
            )}
          </div>
        </div>
      </div>

      {/* REPL History Drawer */}
      {history.length > 0 && (
        <div className="mt-8 border-t border-border pt-6">
          <div className="flex justify-between items-center mb-3">
            <h2 className="text-lg font-semibold flex items-center gap-2">
              <span>📜</span> Command History
            </h2>
            <button
              onClick={handleClearHistory}
              className="text-xs text-text-secondary hover:text-rose-400"
            >
              Clear History
            </button>
          </div>

          <div className="bg-surface rounded-lg border border-border divide-y divide-border/60 overflow-hidden">
            {history.slice(0, 10).map((h) => (
              <div
                key={h.id}
                onClick={() => setCode(h.code)}
                className="p-3 text-xs flex items-center justify-between gap-4 cursor-pointer hover:bg-surface-hover transition-colors"
                title="Click to reload this code into editor"
              >
                <div className="flex items-center gap-2 truncate font-mono text-emerald-400/90">
                  <span
                    className={`w-2 h-2 rounded-full ${h.success ? "bg-emerald-400" : "bg-rose-400"}`}
                  />
                  <span className="truncate">{h.code.replace(/\n/g, " ")}</span>
                </div>
                <div className="flex items-center gap-3 text-text-secondary shrink-0 font-mono text-[11px]">
                  <span>{h.durationMs}ms</span>
                  <span>{new Date(h.executedAtUtc).toLocaleTimeString()}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
