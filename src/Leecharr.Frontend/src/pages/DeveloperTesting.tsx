import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import type {
  DeveloperTestItem,
  DeveloperTestResult,
  TestExecutionRequest,
  TestExecutionResponse,
} from "../api/types";

export default function DeveloperTesting() {
  const { t } = useTranslation();
  const [tests, setTests] = useState<DeveloperTestItem[]>([]);
  const [results, setResults] = useState<Record<string, DeveloperTestResult>>({});
  const [history, setHistory] = useState<DeveloperTestResult[]>([]);
  const [selectedCategory, setSelectedCategory] = useState<string>("All");
  const [isLoading, setIsLoading] = useState(true);
  const [isRunningAll, setIsRunningAll] = useState(false);
  const [runningTestId, setRunningTestId] = useState<string | null>(null);
  const [expandedTestId, setExpandedTestId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const fetchTestsAndHistory = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const [testData, historyData] = await Promise.all([
        apiClient.get<DeveloperTestItem[]>("/system/developer/testing/tests"),
        apiClient.get<DeveloperTestResult[]>("/system/developer/testing/history?limit=50"),
      ]);
      setTests(testData || []);
      setHistory(historyData || []);

      const initialResults: Record<string, DeveloperTestResult> = {};
      (historyData || []).forEach((r) => {
        if (!initialResults[r.testId]) {
          initialResults[r.testId] = r;
        }
      });
      setResults(initialResults);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(msg || "Failed to load test runner data.");
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchTestsAndHistory();
  }, [fetchTestsAndHistory]);

  const categories = ["All", ...Array.from(new Set(tests.map((t) => t.category)))];

  const filteredTests = selectedCategory === "All"
    ? tests
    : tests.filter((t) => t.category === selectedCategory);

  const handleRunSingle = async (testId: string) => {
    setRunningTestId(testId);
    try {
      const res = await apiClient.post<DeveloperTestResult>(`/system/developer/testing/run/${testId}`, {});
      if (res) {
        setResults((prev) => ({ ...prev, [testId]: res }));
        setHistory((prev) => [res, ...prev.slice(0, 49)]);
        setExpandedTestId(testId);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(`Failed to run test ${testId}: ${msg}`);
    } finally {
      setRunningTestId(null);
    }
  };

  const handleRunAll = async () => {
    setIsRunningAll(true);
    try {
      const payload: TestExecutionRequest = {
        runAll: selectedCategory === "All",
        category: selectedCategory === "All" ? undefined : selectedCategory,
      };
      const res = await apiClient.post<TestExecutionResponse>("/system/developer/testing/run", payload);
      if (res && res.results) {
        const newResults: Record<string, DeveloperTestResult> = {};
        res.results.forEach((r) => {
          newResults[r.testId] = r;
        });
        setResults((prev) => ({ ...prev, ...newResults }));
        setHistory((prev) => [...res.results, ...prev.slice(0, 50)]);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(`Failed to execute test suite: ${msg}`);
    } finally {
      setIsRunningAll(false);
    }
  };

  const handleClearHistory = async () => {
    try {
      await apiClient.delete("/system/developer/testing/history");
      setHistory([]);
      setResults({});
    } catch {
      // Ignored
    }
  };

  const passedCount = Object.values(results).filter((r) => r.status === "Passed").length;
  const failedCount = Object.values(results).filter((r) => r.status === "Failed").length;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 border-b border-border pb-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <span>🧪</span> {t("developer.testing", "In-App Test Runner")}
          </h1>
          <p className="text-sm text-text-secondary mt-1">
            Execute built-in diagnostic and smoke tests directly within the running application container.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={handleRunAll}
            disabled={isRunningAll || isLoading}
            className="px-4 py-2 bg-accent text-white rounded-md text-sm font-semibold hover:opacity-90 disabled:opacity-50 flex items-center gap-2 shadow"
          >
            {isRunningAll ? "⏳ Running Tests..." : "▶ Run All Tests"}
          </button>
          <button
            onClick={fetchTestsAndHistory}
            className="px-3 py-2 bg-surface text-text border border-border rounded-md text-sm hover:bg-surface-hover"
            title="Refresh tests"
          >
            🔄
          </button>
        </div>
      </div>

      {error && (
        <div className="p-3 bg-red-500/15 border border-red-500/30 text-red-400 rounded-md text-sm">
          {error}
        </div>
      )}

      {/* Summary KPI Cards */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <div className="p-4 bg-surface rounded-lg border border-border">
          <div className="text-xs uppercase tracking-wider text-text-secondary font-medium">Discovered Tests</div>
          <div className="text-2xl font-bold mt-1">{tests.length}</div>
        </div>
        <div className="p-4 bg-surface rounded-lg border border-border">
          <div className="text-xs uppercase tracking-wider text-emerald-400 font-medium">Passed</div>
          <div className="text-2xl font-bold text-emerald-400 mt-1">{passedCount}</div>
        </div>
        <div className="p-4 bg-surface rounded-lg border border-border">
          <div className="text-xs uppercase tracking-wider text-rose-400 font-medium">Failed</div>
          <div className="text-2xl font-bold text-rose-400 mt-1">{failedCount}</div>
        </div>
        <div className="p-4 bg-surface rounded-lg border border-border">
          <div className="text-xs uppercase tracking-wider text-text-secondary font-medium">Active Category</div>
          <div className="text-xl font-bold mt-1 truncate">{selectedCategory}</div>
        </div>
      </div>

      {/* Category Pills Filter */}
      <div className="flex flex-wrap gap-2">
        {categories.map((cat) => (
          <button
            key={cat}
            onClick={() => setSelectedCategory(cat)}
            className={`px-3 py-1.5 rounded-full text-xs font-medium transition-colors ${
              selectedCategory === cat
                ? "bg-accent text-white"
                : "bg-surface border border-border text-text-secondary hover:text-text"
            }`}
          >
            {cat}
          </button>
        ))}
      </div>

      {/* Test Items Grid */}
      <div className="space-y-3">
        {isLoading ? (
          <div className="p-8 text-center text-text-secondary">Loading test fixtures...</div>
        ) : filteredTests.length === 0 ? (
          <div className="p-8 text-center text-text-secondary">No tests discovered in this category.</div>
        ) : (
          filteredTests.map((test) => {
            const result = results[test.id];
            const isRunning = runningTestId === test.id || isRunningAll;
            const isExpanded = expandedTestId === test.id;

            return (
              <div
                key={test.id}
                className="bg-surface rounded-lg border border-border overflow-hidden transition-all shadow-sm"
              >
                <div className="p-4 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
                  <div className="space-y-1 flex-1">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold text-text">{test.name}</span>
                      <span className="px-2 py-0.5 text-[10px] font-mono rounded bg-border text-text-secondary">
                        {test.category}
                      </span>
                      {result && (
                        <span
                          className={`px-2 py-0.5 text-[11px] font-bold rounded ${
                            result.status === "Passed"
                              ? "bg-emerald-500/20 text-emerald-400"
                              : result.status === "Failed"
                              ? "bg-rose-500/20 text-rose-400"
                              : "bg-amber-500/20 text-amber-400"
                          }`}
                        >
                          {result.status} ({result.durationMs}ms)
                        </span>
                      )}
                    </div>
                    <p className="text-xs text-text-secondary">{test.description}</p>
                  </div>

                  <div className="flex items-center gap-2 self-end sm:self-center">
                    {result && (
                      <button
                        onClick={() => setExpandedTestId(isExpanded ? null : test.id)}
                        className="text-xs text-text-secondary hover:text-text px-2 py-1"
                      >
                        {isExpanded ? "Hide Logs ▲" : "View Logs ▼"}
                      </button>
                    )}
                    <button
                      onClick={() => handleRunSingle(test.id)}
                      disabled={isRunning}
                      className="px-3 py-1.5 bg-accent/20 text-accent hover:bg-accent hover:text-white rounded text-xs font-semibold disabled:opacity-50 transition-colors"
                    >
                      {isRunning ? "Running..." : "Run Test"}
                    </button>
                  </div>
                </div>

                {isExpanded && result && (
                  <div className="border-t border-border/60 bg-black/30 p-3 font-mono text-xs text-emerald-300 overflow-x-auto">
                    <div className="text-text-secondary mb-1">
                      Executed at: {new Date(result.executedAtUtc).toLocaleTimeString()} · Target: {test.targetComponent}
                    </div>
                    {result.output && <pre className="whitespace-pre-wrap">{result.output}</pre>}
                    {result.errorMessage && (
                      <div className="text-rose-400 mt-2 whitespace-pre-wrap font-semibold">
                        Error: {result.errorMessage}
                        {result.stackTrace && <div className="text-xs text-rose-300/80">{result.stackTrace}</div>}
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>

      {/* Execution History */}
      {history.length > 0 && (
        <div className="mt-8 border-t border-border pt-6">
          <div className="flex justify-between items-center mb-3">
            <h2 className="text-lg font-semibold flex items-center gap-2">
              <span>📜</span> Recent Execution History
            </h2>
            <button
              onClick={handleClearHistory}
              className="text-xs text-text-secondary hover:text-rose-400"
            >
              Clear History
            </button>
          </div>

          <div className="bg-surface rounded-lg border border-border divide-y divide-border/60 overflow-hidden">
            {history.slice(0, 10).map((item, idx) => (
              <div key={idx} className="p-3 text-xs flex items-center justify-between gap-4">
                <div className="flex items-center gap-2 truncate">
                  <span
                    className={`w-2 h-2 rounded-full ${
                      item.status === "Passed" ? "bg-emerald-400" : "bg-rose-400"
                    }`}
                  />
                  <span className="font-medium text-text truncate">{item.name}</span>
                  <span className="text-text-secondary font-mono">[{item.category}]</span>
                </div>
                <div className="flex items-center gap-3 text-text-secondary shrink-0">
                  <span>{item.durationMs}ms</span>
                  <span>{new Date(item.executedAtUtc).toLocaleTimeString()}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
