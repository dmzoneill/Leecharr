import React, { useEffect, useState } from "react";
import { apiClient } from "../api/client";

interface QualityGateMetric {
  metricKey: string;
  status: string;
  actualValue: string;
  errorThreshold: string;
  comparator: string;
}

interface QualityOverviewMetrics {
  coverage: number;
  newCodeCoverage: number;
  duplicationDensity: number;
  bugs: number;
  vulnerabilities: number;
  codeSmells: number;
  securityHotspotsReviewed: number;
  reliabilityRating: string;
  securityRating: string;
  maintainabilityRating: string;
  technicalDebtFormatted: string;
}

interface QualityPipelineRun {
  name: string;
  status: string;
  conclusion: string;
  runNumber: number;
  htmlUrl: string;
  createdAt: string;
}

interface QualityReportResponse {
  projectName: string;
  repository: string;
  qualityGate: {
    status: string;
    conditions: QualityGateMetric[];
  };
  metrics: QualityOverviewMetrics;
  recentPipelines: QualityPipelineRun[];
  sonarCloudUrl: string;
  codacyUrl: string;
  githubRepoUrl: string;
  message?: string;
}

export const DeveloperQuality: React.FC = () => {
  const [report, setReport] = useState<QualityReportResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;
    setLoading(true);
    setError(null);

    apiClient
      .get<QualityReportResponse>("/system/developer/quality")
      .then((res) => {
        if (isMounted) {
          setReport(res);
          setLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (isMounted) {
          const message = err instanceof Error ? err.message : String(err);
          setError(message || "Failed to load quality metrics.");
          setLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, []);

  return (
    <div className="developer-page developer-quality-page" style={{ padding: "20px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "20px" }}>
        <div>
          <h1 style={{ margin: "0 0 8px 0", fontSize: "1.5rem", color: "var(--text-color, #f5f5f4)" }}>
            🛡️ Code Quality &amp; Security Dashboard
          </h1>
          <p style={{ margin: 0, color: "var(--text-dim, #a8a29e)", fontSize: "0.9rem" }}>
            Live metrics from SonarCloud Quality Gate, Codacy, CodeQL, and GitHub Actions CI/CD pipelines.
          </p>
        </div>

        {report && (
          <div style={{ display: "flex", gap: "8px" }}>
            <a
              href={report.sonarCloudUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-sm btn-secondary"
              style={{ textDecoration: "none" }}
            >
              SonarCloud ↗
            </a>
            <a
              href={report.codacyUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-sm btn-secondary"
              style={{ textDecoration: "none" }}
            >
              Codacy ↗
            </a>
            <a
              href={report.githubRepoUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-sm btn-primary"
              style={{ textDecoration: "none" }}
            >
              GitHub Actions ↗
            </a>
          </div>
        )}
      </div>

      {loading && (
        <div style={{ padding: "40px", textAlign: "center", color: "var(--text-dim, #a8a29e)" }}>
          <div className="spinner" style={{ margin: "0 auto 12px auto" }} />
          Gathering live quality gate and security metrics...
        </div>
      )}

      {error && (
        <div className="alert alert-danger" style={{ marginBottom: "20px" }}>
          {error}
        </div>
      )}

      {!loading && report && (
        <div style={{ display: "flex", flexDirection: "column", gap: "20px" }}>
          {/* Quality Gate Status Banner */}
          <div
            className="card"
            style={{
              background: report.qualityGate.status === "OK" ? "rgba(16, 185, 129, 0.12)" : "rgba(239, 68, 68, 0.12)",
              border: `1px solid ${report.qualityGate.status === "OK" ? "#10b981" : "#ef4444"}`,
              borderRadius: "8px",
              padding: "16px",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <div>
              <h2 style={{ margin: 0, fontSize: "1.2rem", color: report.qualityGate.status === "OK" ? "#10b981" : "#ef4444" }}>
                SonarCloud Quality Gate: {report.qualityGate.status === "OK" ? "PASSED (OK)" : "FAILED (ERROR)"}
              </h2>
              <p style={{ margin: "4px 0 0 0", fontSize: "0.85rem", color: "var(--text-color, #f5f5f4)" }}>
                Strict zero-suppression policy evaluating new code coverage (&ge;80%) and duplication density (&le;3%).
              </p>
            </div>

            <div style={{ textAlign: "right" }}>
              <span style={{ fontSize: "1.5rem", fontWeight: "bold", color: report.qualityGate.status === "OK" ? "#10b981" : "#ef4444" }}>
                {report.metrics.newCodeCoverage.toFixed(1)}%
              </span>
              <div style={{ fontSize: "0.75rem", color: "var(--text-dim, #a8a29e)" }}>New Code Coverage</div>
            </div>
          </div>

          {/* Rating Cards Grid */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "16px" }}>
            <div className="card" style={{ background: "var(--bg-card, #131733)", border: "1px solid var(--border-color, #44403c)", borderRadius: "8px", padding: "16px" }}>
              <div style={{ fontSize: "0.85rem", color: "var(--text-dim, #a8a29e)", marginBottom: "4px" }}>Reliability Rating</div>
              <div style={{ fontSize: "1.8rem", fontWeight: "bold", color: "#10b981" }}>{report.metrics.reliabilityRating}</div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", marginTop: "4px" }}>{report.metrics.bugs} Bugs</div>
            </div>

            <div className="card" style={{ background: "var(--bg-card, #131733)", border: "1px solid var(--border-color, #44403c)", borderRadius: "8px", padding: "16px" }}>
              <div style={{ fontSize: "0.85rem", color: "var(--text-dim, #a8a29e)", marginBottom: "4px" }}>Security Rating</div>
              <div style={{ fontSize: "1.8rem", fontWeight: "bold", color: "#10b981" }}>{report.metrics.securityRating}</div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", marginTop: "4px" }}>{report.metrics.vulnerabilities} Vulnerabilities</div>
            </div>

            <div className="card" style={{ background: "var(--bg-card, #131733)", border: "1px solid var(--border-color, #44403c)", borderRadius: "8px", padding: "16px" }}>
              <div style={{ fontSize: "0.85rem", color: "var(--text-dim, #a8a29e)", marginBottom: "4px" }}>Maintainability Rating</div>
              <div style={{ fontSize: "1.8rem", fontWeight: "bold", color: "#10b981" }}>{report.metrics.maintainabilityRating}</div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", marginTop: "4px" }}>{report.metrics.codeSmells} Code Smells</div>
            </div>

            <div className="card" style={{ background: "var(--bg-card, #131733)", border: "1px solid var(--border-color, #44403c)", borderRadius: "8px", padding: "16px" }}>
              <div style={{ fontSize: "0.85rem", color: "var(--text-dim, #a8a29e)", marginBottom: "4px" }}>Security Hotspots</div>
              <div style={{ fontSize: "1.8rem", fontWeight: "bold", color: "#10b981" }}>{report.metrics.securityHotspotsReviewed.toFixed(0)}%</div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", marginTop: "4px" }}>100% Reviewed</div>
            </div>
          </div>

          {/* Quality Gate Conditions Breakdown */}
          {report.qualityGate.conditions.length > 0 && (
            <div className="card" style={{ background: "var(--bg-card, #131733)", border: "1px solid var(--border-color, #44403c)", borderRadius: "8px", padding: "16px" }}>
              <h3 style={{ margin: "0 0 12px 0", fontSize: "1rem", color: "var(--text-color, #f5f5f4)" }}>
                Quality Gate Criteria
              </h3>
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "12px" }}>
                {report.qualityGate.conditions.map((cond) => {
                  const isOk = cond.status === "OK";
                  return (
                    <div
                      key={cond.metricKey}
                      style={{
                        padding: "10px",
                        background: "var(--bg-lighter, #1c1917)",
                        border: `1px solid ${isOk ? "rgba(16, 185, 129, 0.4)" : "rgba(239, 68, 68, 0.4)"}`,
                        borderRadius: "6px",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      <div>
                        <div style={{ fontWeight: 600, fontSize: "0.85rem", color: "var(--text-color, #f5f5f4)" }}>
                          {cond.metricKey.replace(/_/g, " ")}
                        </div>
                        <div style={{ fontSize: "0.75rem", color: "var(--text-dim, #a8a29e)" }}>
                          Threshold: {cond.comparator} {cond.errorThreshold}
                        </div>
                      </div>
                      <span
                        style={{
                          background: isOk ? "#10b981" : "#ef4444",
                          color: "#fff",
                          fontSize: "0.75rem",
                          fontWeight: "bold",
                          padding: "2px 8px",
                          borderRadius: "12px",
                        }}
                      >
                        {cond.actualValue}%
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          {/* Recent CI/CD Pipeline Runs */}
          {report.recentPipelines.length > 0 && (
            <div className="card" style={{ background: "var(--bg-card, #131733)", border: "1px solid var(--border-color, #44403c)", borderRadius: "8px", padding: "16px" }}>
              <h3 style={{ margin: "0 0 12px 0", fontSize: "1rem", color: "var(--text-color, #f5f5f4)" }}>
                Recent CI/CD &amp; Security Pipeline Runs
              </h3>
              <div style={{ display: "flex", flexDirection: "column", gap: "8px" }}>
                {report.recentPipelines.map((run, i) => {
                  const isSuccess = run.conclusion === "success";
                  return (
                    <div
                      key={i}
                      style={{
                        padding: "10px 14px",
                        background: "var(--bg-lighter, #1c1917)",
                        border: "1px solid var(--border-color, #44403c)",
                        borderRadius: "6px",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                        <span style={{ fontSize: "1.1rem" }}>
                          {isSuccess ? "✅" : run.status === "in_progress" ? "⏳" : "❌"}
                        </span>
                        <div>
                          <span style={{ fontWeight: 600, fontSize: "0.9rem", color: "var(--text-color, #f5f5f4)" }}>
                            {run.name}
                          </span>
                          <span style={{ color: "var(--text-dim, #a8a29e)", fontSize: "0.8rem", marginLeft: "8px" }}>
                            #{run.runNumber} &bull; {new Date(run.createdAt).toLocaleString()}
                          </span>
                        </div>
                      </div>

                      {run.htmlUrl && (
                        <a
                          href={run.htmlUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="btn btn-sm btn-secondary"
                          style={{ textDecoration: "none" }}
                        >
                          View Run ↗
                        </a>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
export default DeveloperQuality;
