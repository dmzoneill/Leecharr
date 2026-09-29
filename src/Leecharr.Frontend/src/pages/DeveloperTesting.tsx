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
  const [selectedTest, setSelectedTest] = useState<DeveloperTestItem | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isRunningAll, setIsRunningAll] = useState(false);
  const [runningTestId, setRunningTestId] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const fetchTestsAndHistory = useCallback(async () => {
    setIsLoading(true);
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
      if (testData && testData.length > 0 && !selectedTest) {
        setSelectedTest(testData[0]);
      }
    } catch {
      setActionMessage({ text: "Failed to load test runner data.", type: "error" });
    } finally {
      setIsLoading(false);
    }
  }, [selectedTest]);

  useEffect(() => {
    fetchTestsAndHistory();
  }, [fetchTestsAndHistory]);

  const categories = ["All", ...Array.from(new Set(tests.map((t) => t.category)))];

  const filteredTests = selectedCategory === "All"
    ? tests
    : tests.filter((t) => t.category === selectedCategory);

  const handleRunSingle = async (testId: string) => {
    setRunningTestId(testId);
    setActionMessage(null);
    try {
      const res = await apiClient.post<DeveloperTestResult>(`/system/developer/testing/run/${testId}`, {});
      if (res) {
        setResults((prev) => ({ ...prev, [testId]: res }));
        setHistory((prev) => [res, ...prev.slice(0, 49)]);
        const item = tests.find((t) => t.id === testId);
        if (item) setSelectedTest(item);
        setActionMessage({
          text: `Test '${res.name}' completed with status: ${res.status} (${res.durationMs}ms)`,
          type: res.status === "Passed" ? "success" : "error",
        });
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setActionMessage({ text: `Failed to run test: ${msg}`, type: "error" });
    } finally {
      setRunningTestId(null);
    }
  };

  const handleRunAll = async () => {
    setIsRunningAll(true);
    setActionMessage(null);
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
        setActionMessage({
          text: `Batch test execution completed: ${res.passed}/${res.totalTests} passed (${res.totalDurationMs}ms)`,
          type: res.failed === 0 ? "success" : "error",
        });
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setActionMessage({ text: `Batch execution failed: ${msg}`, type: "error" });
    } finally {
      setIsRunningAll(false);
    }
  };

  const handleClearHistory = async () => {
    try {
      await apiClient.delete("/system/developer/testing/history");
      setHistory([]);
      setResults({});
      setActionMessage({ text: "Test execution history cleared.", type: "success" });
    } catch {
      setActionMessage({ text: "Failed to clear history.", type: "error" });
    }
  };

  const passedCount = Object.values(results).filter((r) => r.status === "Passed").length;
  const failedCount = Object.values(results).filter((r) => r.status === "Failed").length;
  const activeResult = selectedTest ? results[selectedTest.id] : null;

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Top Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "12px",
          marginBottom: "16px",
        }}
      >
        <div>
          <h2 style={{ margin: "0 0 4px 0", fontSize: "1.4rem", fontWeight: 700 }}>
            🧪 In-App Test Runner & Smoke Diagnostics
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Execute live subsystem diagnostic and smoke tests directly inside the running container.
          </p>
        </div>

        <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" }}>
          <button
            onClick={handleRunAll}
            disabled={isRunningAll || isLoading}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "6px",
              padding: "7px 14px",
              borderRadius: "6px",
              border: "none",
              backgroundColor: "var(--accent, #3b82f6)",
              color: "#fff",
              cursor: isRunningAll ? "not-allowed" : "pointer",
              fontSize: "0.83rem",
              fontWeight: 600,
              opacity: isRunningAll ? 0.6 : 1,
            }}
          >
            {isRunningAll ? "⏳ Running Suite..." : "▶ Run All Tests"}
          </button>

          <button
            onClick={fetchTestsAndHistory}
            disabled={isLoading}
            style={{
              padding: "7px 12px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.83rem",
            }}
          >
            🔄 Refresh
          </button>
        </div>
      </div>

      {actionMessage && (
        <div
          style={{
            padding: "10px 14px",
            borderRadius: "6px",
            marginBottom: "16px",
            fontSize: "0.85rem",
            backgroundColor: actionMessage.type === "success" ? "rgba(16, 185, 129, 0.15)" : "rgba(239, 68, 68, 0.15)",
            border: `1px solid ${actionMessage.type === "success" ? "#10b981" : "#ef4444"}`,
            color: actionMessage.type === "success" ? "#34d399" : "#f87171",
          }}
        >
          {actionMessage.text}
        </div>
      )}

      {/* KPI Summary Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
          gap: "12px",
          marginBottom: "16px",
        }}
      >
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600 }}>
            Available Tests
          </div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700, marginTop: "4px" }}>
            {tests.length}
          </div>
        </div>

        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "#34d399", fontWeight: 600 }}>
            Passed
          </div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700, marginTop: "4px", color: "#34d399" }}>
            {passedCount}
          </div>
        </div>

        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "#f87171", fontWeight: 600 }}>
            Failed
          </div>
          <div style={{ fontSize: "1.5rem", fontWeight: 700, marginTop: "4px", color: "#f87171" }}>
            {failedCount}
          </div>
        </div>

        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "14px",
          }}
        >
          <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600 }}>
            Category Scope
          </div>
          <div style={{ fontSize: "1.1rem", fontWeight: 700, marginTop: "6px", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
            {selectedCategory}
          </div>
        </div>
      </div>

      {/* Category Pills Filter */}
      <div style={{ display: "flex", gap: "8px", flexWrap: "wrap", marginBottom: "16px" }}>
        {categories.map((cat) => (
          <button
            key={cat}
            onClick={() => setSelectedCategory(cat)}
            style={{
              padding: "5px 12px",
              borderRadius: "20px",
              fontSize: "0.78rem",
              fontWeight: 600,
              cursor: "pointer",
              border: selectedCategory === cat ? "none" : "1px solid var(--border, #334155)",
              backgroundColor: selectedCategory === cat ? "var(--accent, #3b82f6)" : "var(--bg-surface, #1e293b)",
              color: selectedCategory === cat ? "#fff" : "var(--text-secondary, #94a3b8)",
            }}
          >
            {cat}
          </button>
        ))}
      </div>

      {/* Main Split View: Test Catalog on Left, Live Output / Inspector on Right */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 480px", gap: "16px" }}>
        {/* Left: Test List */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            overflow: "hidden",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 360px)",
            minHeight: "450px",
          }}
        >
          <div style={{ overflowY: "auto", flex: 1 }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.83rem", textAlign: "left" }}>
              <thead>
                <tr style={{ backgroundColor: "rgba(0,0,0,0.2)", borderBottom: "1px solid var(--border, #334155)", color: "var(--text-secondary, #94a3b8)" }}>
                  <th style={{ padding: "10px 14px" }}>Test Name</th>
                  <th style={{ padding: "10px 14px", width: "110px" }}>Category</th>
                  <th style={{ padding: "10px 14px", width: "100px" }}>Status</th>
                  <th style={{ padding: "10px 14px", textAlign: "right", width: "100px" }}>Action</th>
                </tr>
              </thead>
              <tbody>
                {isLoading && tests.length === 0 ? (
                  <tr>
                    <td colSpan={4} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>
                      Discovering diagnostic smoke tests...
                    </td>
                  </tr>
                ) : filteredTests.length === 0 ? (
                  <tr>
                    <td colSpan={4} style={{ padding: "30px", textAlign: "center", color: "var(--text-secondary)" }}>
                      No tests found in this category.
                    </td>
                  </tr>
                ) : (
                  filteredTests.map((test) => {
                    const isSelected = selectedTest?.id === test.id;
                    const res = results[test.id];
                    const isRunning = runningTestId === test.id || isRunningAll;

                    return (
                      <tr
                        key={test.id}
                        onClick={() => setSelectedTest(test)}
                        style={{
                          borderBottom: "1px solid var(--border, #334155)",
                          backgroundColor: isSelected ? "rgba(59, 130, 246, 0.15)" : "transparent",
                          cursor: "pointer",
                        }}
                      >
                        <td style={{ padding: "10px 14px" }}>
                          <div style={{ fontWeight: 600, color: "#fff" }}>{test.name}</div>
                          <div style={{ fontSize: "0.75rem", color: "var(--text-secondary, #94a3b8)", marginTop: "2px" }}>
                            {test.description}
                          </div>
                        </td>
                        <td style={{ padding: "10px 14px" }}>
                          <span
                            style={{
                              padding: "2px 8px",
                              borderRadius: "4px",
                              fontSize: "0.72rem",
                              fontFamily: "monospace",
                              backgroundColor: "var(--bg-primary, #0f172a)",
                              border: "1px solid var(--border, #334155)",
                              color: "var(--text-secondary, #94a3b8)",
                            }}
                          >
                            {test.category}
                          </span>
                        </td>
                        <td style={{ padding: "10px 14px", whiteSpace: "nowrap" }}>
                          {res ? (
                            <span
                              style={{
                                padding: "2px 8px",
                                borderRadius: "4px",
                                fontSize: "0.75rem",
                                fontWeight: 600,
                                backgroundColor: res.status === "Passed" ? "rgba(16, 185, 129, 0.2)" : "rgba(239, 68, 68, 0.2)",
                                color: res.status === "Passed" ? "#34d399" : "#f87171",
                              }}
                            >
                              {res.status} ({res.durationMs}ms)
                            </span>
                          ) : (
                            <span style={{ fontSize: "0.75rem", color: "var(--text-secondary, #94a3b8)" }}>
                              Not Run
                            </span>
                          )}
                        </td>
                        <td style={{ padding: "10px 14px", textAlign: "right" }}>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              handleRunSingle(test.id);
                            }}
                            disabled={isRunning}
                            style={{
                              padding: "4px 10px",
                              borderRadius: "4px",
                              border: "1px solid var(--border, #334155)",
                              backgroundColor: "var(--bg-primary, #0f172a)",
                              color: "var(--accent, #3b82f6)",
                              cursor: isRunning ? "not-allowed" : "pointer",
                              fontSize: "0.75rem",
                              fontWeight: 600,
                            }}
                          >
                            {isRunning ? "..." : "Run"}
                          </button>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Right: Selected Test Output Inspector */}
        <div
          style={{
            backgroundColor: "var(--bg-surface, #1e293b)",
            border: "1px solid var(--border, #334155)",
            borderRadius: "8px",
            padding: "16px",
            display: "flex",
            flexDirection: "column",
            height: "calc(100vh - 360px)",
            minHeight: "450px",
            overflow: "hidden",
          }}
        >
          {selectedTest ? (
            <div style={{ display: "flex", flexDirection: "column", height: "100%", minHeight: 0 }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "12px", borderBottom: "1px solid var(--border, #334155)", paddingBottom: "10px" }}>
                <div>
                  <h3 style={{ margin: "0 0 4px 0", fontSize: "1.05rem", fontWeight: 700 }}>
                    {selectedTest.name}
                  </h3>
                  <div style={{ fontSize: "0.78rem", color: "var(--text-secondary, #94a3b8)" }}>
                    Target Component: <code>{selectedTest.targetComponent}</code> · ID: <code>{selectedTest.id}</code>
                  </div>
                </div>

                <button
                  onClick={() => handleRunSingle(selectedTest.id)}
                  disabled={runningTestId === selectedTest.id || isRunningAll}
                  style={{
                    padding: "6px 14px",
                    borderRadius: "6px",
                    border: "none",
                    backgroundColor: "var(--accent, #3b82f6)",
                    color: "#fff",
                    cursor: "pointer",
                    fontSize: "0.8rem",
                    fontWeight: 600,
                  }}
                >
                  {runningTestId === selectedTest.id ? "Running..." : "Run Single Test"}
                </button>
              </div>

              <div style={{ flex: 1, minHeight: 0, overflowY: "auto", display: "flex", flexDirection: "column", gap: "12px" }}>
                <div>
                  <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600, marginBottom: "4px" }}>
                    Description
                  </div>
                  <div style={{ fontSize: "0.83rem", color: "#e2e8f0" }}>
                    {selectedTest.description}
                  </div>
                </div>

                <div>
                  <div style={{ fontSize: "0.75rem", textTransform: "uppercase", color: "var(--text-secondary, #94a3b8)", fontWeight: 600, marginBottom: "4px" }}>
                    Execution Result
                  </div>

                  {activeResult ? (
                    <div style={{ display: "flex", flexDirection: "column", gap: "8px" }}>
                      <div style={{ display: "flex", gap: "10px", fontSize: "0.8rem" }}>
                        <span
                          style={{
                            padding: "2px 8px",
                            borderRadius: "4px",
                            fontWeight: 700,
                            backgroundColor: activeResult.status === "Passed" ? "rgba(16, 185, 129, 0.2)" : "rgba(239, 68, 68, 0.2)",
                            color: activeResult.status === "Passed" ? "#34d399" : "#f87171",
                          }}
                        >
                          {activeResult.status}
                        </span>
                        <span style={{ color: "var(--text-secondary, #94a3b8)" }}>
                          Duration: <strong>{activeResult.durationMs}ms</strong>
                        </span>
                        <span style={{ color: "var(--text-secondary, #94a3b8)" }}>
                          Time: {new Date(activeResult.executedAtUtc).toLocaleTimeString()}
                        </span>
                      </div>

                      {activeResult.output && (
                        <pre
                          style={{
                            margin: 0,
                            padding: "12px",
                            borderRadius: "6px",
                            backgroundColor: "var(--bg-primary, #0f172a)",
                            border: "1px solid var(--border, #334155)",
                            fontFamily: "monospace",
                            fontSize: "0.78rem",
                            color: "#34d399",
                            whiteSpace: "pre-wrap",
                            overflowX: "auto",
                          }}
                        >
                          {activeResult.output}
                        </pre>
                      )}

                      {activeResult.errorMessage && (
                        <div
                          style={{
                            padding: "10px",
                            borderRadius: "6px",
                            backgroundColor: "rgba(239, 68, 68, 0.1)",
                            border: "1px solid rgba(239, 68, 68, 0.3)",
                            color: "#f87171",
                            fontSize: "0.8rem",
                            fontFamily: "monospace",
                          }}
                        >
                          <div style={{ fontWeight: 700 }}>Error: {activeResult.errorMessage}</div>
                          {activeResult.stackTrace && (
                            <pre style={{ margin: "6px 0 0 0", fontSize: "0.72rem", color: "#fca5a5", whiteSpace: "pre-wrap" }}>
                              {activeResult.stackTrace}
                            </pre>
                          )}
                        </div>
                      )}
                    </div>
                  ) : (
                    <div style={{ padding: "16px", backgroundColor: "var(--bg-primary, #0f172a)", border: "1px solid var(--border, #334155)", borderRadius: "6px", color: "var(--text-secondary, #94a3b8)", fontSize: "0.8rem", textAlign: "center" }}>
                      This test has not executed in this session yet. Click &quot;Run Single Test&quot; to run now.
                    </div>
                  )}
                </div>
              </div>
            </div>
          ) : (
            <div style={{ height: "100%", display: "flex", alignItems: "center", justifyContent: "center", color: "var(--text-secondary, #94a3b8)", fontSize: "0.85rem" }}>
              Select a test from the left to view details and execute.
            </div>
          )}
        </div>
      </div>

      {/* Execution History */}
      {history.length > 0 && (
        <div style={{ marginTop: "24px", paddingTop: "16px", borderTop: "1px solid var(--border, #334155)" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
            <h3 style={{ margin: 0, fontSize: "1.05rem", fontWeight: 700, display: "flex", alignItems: "center", gap: "6px" }}>
              <span>📜</span> Execution History
            </h3>
            <button
              onClick={handleClearHistory}
              style={{
                background: "transparent",
                border: "none",
                color: "#f87171",
                cursor: "pointer",
                fontSize: "0.78rem",
              }}
            >
              Clear History
            </button>
          </div>

          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              overflow: "hidden",
            }}
          >
            {history.slice(0, 8).map((item, idx) => (
              <div
                key={idx}
                style={{
                  padding: "8px 14px",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  fontSize: "0.78rem",
                  borderBottom: idx === Math.min(history.length, 8) - 1 ? "none" : "1px solid var(--border, #334155)",
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <span
                    style={{
                      width: "8px",
                      height: "8px",
                      borderRadius: "50%",
                      backgroundColor: item.status === "Passed" ? "#34d399" : "#f87171",
                    }}
                  />
                  <span style={{ fontWeight: 600, color: "#fff" }}>{item.name}</span>
                  <span style={{ color: "var(--text-secondary, #94a3b8)", fontFamily: "monospace" }}>[{item.category}]</span>
                </div>
                <div style={{ display: "flex", gap: "14px", color: "var(--text-secondary, #94a3b8)", fontFamily: "monospace" }}>
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
