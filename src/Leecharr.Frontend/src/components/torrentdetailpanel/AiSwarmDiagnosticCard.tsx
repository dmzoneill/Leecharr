import React, { useState, useEffect } from "react";
import {
  SparklesIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  AlertIcon,
  CheckCircleIcon,
  RefreshIcon,
} from "../icons/AiIcons";
import type { Torrent, AiDiagnosticReport } from "../../api/types";
import { useAiDiagnoseTorrent, useAnnounceTorrent, useRecheckTorrent } from "../../api/hooks";

interface AiSwarmDiagnosticCardProps {
  torrent: Torrent;
}

export const AiSwarmDiagnosticCard: React.FC<AiSwarmDiagnosticCardProps> = ({ torrent }) => {
  const [isExpanded, setIsExpanded] = useState<boolean>(() => {
    return localStorage.getItem("leecharr_ai_diag_expanded") === "true";
  });
  const [report, setReport] = useState<AiDiagnosticReport | null>(null);

  const diagnoseMutation = useAiDiagnoseTorrent();
  const announceMutation = useAnnounceTorrent();
  const recheckMutation = useRecheckTorrent();

  useEffect(() => {
    localStorage.setItem("leecharr_ai_diag_expanded", isExpanded ? "true" : "false");
  }, [isExpanded]);

  useEffect(() => {
    setReport(null);
  }, [torrent.id]);

  const handleRunDiagnosis = () => {
    if (!torrent.id || diagnoseMutation.isPending) return;
    diagnoseMutation.mutate(torrent.id, {
      onSuccess: (data) => setReport(data),
    });
  };

  const getSeverityBadgeClass = (severity?: string) => {
    switch (severity?.toLowerCase()) {
      case "high":
        return "badge-error";
      case "medium":
        return "badge-warning";
      default:
        return "badge-success";
    }
  };

  const getHealthScoreColor = (score: number) => {
    if (score >= 80) return "#34d399";
    if (score >= 50) return "#fbbf24";
    return "#f87171";
  };

  return (
    <div
      style={{
        borderRadius: "8px",
        border: "1px solid var(--border-color, #23284B)",
        backgroundColor: "var(--bg-secondary, #171B35)",
        overflow: "hidden",
        marginTop: "0.5rem",
      }}
    >
      {/* Collapsible Header */}
      <button
        type="button"
        onClick={() => setIsExpanded(!isExpanded)}
        style={{
          width: "100%",
          padding: "0.5rem 0.75rem",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          backgroundColor: "transparent",
          border: "none",
          cursor: "pointer",
          color: "var(--text-primary, #F8F4ED)",
          fontSize: "0.8rem",
          fontWeight: 600,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <SparklesIcon size={14} style={{ color: "var(--accent-gold, #FFD166)" }} />
          <span>AI Swarm Health & Diagnostic Insights</span>
          {report && (
            <span
              className={`badge ${getSeverityBadgeClass(report.severity)}`}
              style={{
                fontSize: "0.7rem",
                padding: "0.1rem 0.35rem",
                fontFamily: "monospace",
                fontWeight: 700,
              }}
            >
              {report.overallHealth} ({Math.round(report.healthScore)}%)
            </span>
          )}
        </div>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.4rem",
            color: "var(--text-muted, #C7C5D3)",
          }}
        >
          <span style={{ fontSize: "0.75rem", fontWeight: 400 }}>
            {isExpanded ? "Collapse" : "Expand"}
          </span>
          {isExpanded ? <ChevronUpIcon size={14} /> : <ChevronDownIcon size={14} />}
        </div>
      </button>

      {/* Expanded Pane */}
      {isExpanded && (
        <div
          style={{
            padding: "0.75rem",
            borderTop: "1px solid var(--border-color, #23284B)",
            backgroundColor: "var(--bg-primary, #10111A)",
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
          }}
        >
          {!report && !diagnoseMutation.isPending && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                gap: "0.5rem",
              }}
            >
              <p
                style={{
                  margin: 0,
                  fontSize: "0.75rem",
                  color: "var(--text-muted, #C7C5D3)",
                }}
              >
                Run instant AI swarm diagnostics to inspect tracker latency, choking bottlenecks,
                and piece availability.
              </p>
              <button
                type="button"
                onClick={handleRunDiagnosis}
                style={{
                  padding: "0.35rem 0.75rem",
                  backgroundColor: "var(--accent-gold, #FFD166)",
                  color: "#10111A",
                  border: "none",
                  borderRadius: "5px",
                  fontSize: "0.75rem",
                  fontWeight: 700,
                  cursor: "pointer",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.35rem",
                  whiteSpace: "nowrap",
                }}
              >
                <SparklesIcon size={13} />
                <span>Diagnose Swarm</span>
              </button>
            </div>
          )}

          {diagnoseMutation.isPending && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                padding: "0.5rem",
                fontSize: "0.75rem",
                color: "var(--text-muted, #C7C5D3)",
              }}
            >
              <RefreshIcon
                size={14}
                style={{
                  animation: "spin 1s linear infinite",
                  color: "#FFD166",
                }}
              />
              <span>Analyzing swarm peers, tracker responses, and piece availability...</span>
            </div>
          )}

          {report && (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.6rem",
              }}
            >
              {/* Summary Bar */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  padding: "0.5rem 0.6rem",
                  backgroundColor: "var(--bg-secondary, #171B35)",
                  borderRadius: "6px",
                  border: "1px solid var(--border-color, #23284B)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                  }}
                >
                  {report.severity === "High" ? (
                    <AlertIcon size={16} style={{ color: "#f87171" }} />
                  ) : (
                    <CheckCircleIcon size={16} style={{ color: "#34d399" }} />
                  )}
                  <div>
                    <div
                      style={{
                        fontSize: "0.8rem",
                        fontWeight: 700,
                        color: "var(--text-primary, #F8F4ED)",
                      }}
                    >
                      {report.summary}
                    </div>
                    <div
                      style={{
                        fontSize: "0.7rem",
                        color: "var(--text-muted, #C7C5D3)",
                      }}
                    >
                      Swarm: {report.swarmAnalysis} &bull; Tracker: {report.trackerAnalysis}
                    </div>
                  </div>
                </div>

                <div style={{ textAlign: "right" }}>
                  <span
                    style={{
                      fontSize: "0.65rem",
                      textTransform: "uppercase",
                      color: "var(--text-muted, #C7C5D3)",
                    }}
                  >
                    Health Score
                  </span>
                  <div
                    style={{
                      fontSize: "0.9rem",
                      fontWeight: 800,
                      fontFamily: "monospace",
                      color: getHealthScoreColor(report.healthScore),
                    }}
                  >
                    {Math.round(report.healthScore)}%
                  </div>
                </div>
              </div>

              {/* Issues & Bottlenecks */}
              {report.issues?.length > 0 && (
                <div style={{ fontSize: "0.75rem" }}>
                  <span
                    style={{
                      fontWeight: 700,
                      color: "#fca5a5",
                      display: "block",
                      marginBottom: "0.2rem",
                    }}
                  >
                    Detected Issues & Bottlenecks:
                  </span>
                  <ul
                    style={{
                      margin: 0,
                      paddingLeft: "1.2rem",
                      color: "#fecaca",
                    }}
                  >
                    {report.issues.map((issue, idx) => (
                      <li key={idx}>{issue}</li>
                    ))}
                  </ul>
                </div>
              )}

              {/* Actionable Recommendations & 1-Click Fixes */}
              {report.recommendations?.length > 0 && (
                <div style={{ fontSize: "0.75rem" }}>
                  <span
                    style={{
                      fontWeight: 700,
                      color: "var(--accent-gold, #FFD166)",
                      display: "block",
                      marginBottom: "0.2rem",
                    }}
                  >
                    Recommendations & Auto-Remediation:
                  </span>
                  <ul
                    style={{
                      margin: 0,
                      paddingLeft: "1.2rem",
                      color: "var(--text-primary, #F8F4ED)",
                    }}
                  >
                    {report.recommendations.map((rec, idx) => (
                      <li key={idx}>{rec}</li>
                    ))}
                  </ul>
                </div>
              )}

              {/* 1-Click Quick Actions Toolbar */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  flexWrap: "wrap",
                  paddingTop: "0.25rem",
                }}
              >
                <button
                  type="button"
                  onClick={() => announceMutation.mutate(torrent.id)}
                  disabled={announceMutation.isPending}
                  style={{
                    padding: "0.25rem 0.5rem",
                    backgroundColor: "var(--bg-secondary, #171B35)",
                    border: "1px solid var(--border-color, #23284B)",
                    borderRadius: "4px",
                    color: "var(--text-primary, #F8F4ED)",
                    fontSize: "0.7rem",
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    gap: "0.3rem",
                  }}
                  title="Force immediate tracker re-announce"
                >
                  <span>📡 Re-announce Trackers</span>
                </button>

                <button
                  type="button"
                  onClick={() => recheckMutation.mutate(torrent.id)}
                  disabled={recheckMutation.isPending}
                  style={{
                    padding: "0.25rem 0.5rem",
                    backgroundColor: "var(--bg-secondary, #171B35)",
                    border: "1px solid var(--border-color, #23284B)",
                    borderRadius: "4px",
                    color: "var(--text-primary, #F8F4ED)",
                    fontSize: "0.7rem",
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    gap: "0.3rem",
                  }}
                  title="Verify file pieces against hash map"
                >
                  <span>⚡ Force Recheck</span>
                </button>

                <button
                  type="button"
                  onClick={handleRunDiagnosis}
                  disabled={diagnoseMutation.isPending}
                  style={{
                    padding: "0.25rem 0.5rem",
                    backgroundColor: "var(--bg-secondary, #171B35)",
                    border: "1px solid var(--border-color, #23284B)",
                    borderRadius: "4px",
                    color: "var(--text-muted, #C7C5D3)",
                    fontSize: "0.7rem",
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    gap: "0.3rem",
                    marginLeft: "auto",
                  }}
                  title="Re-run diagnostic analysis"
                >
                  <RefreshIcon size={11} />
                  <span>Re-evaluate</span>
                </button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
