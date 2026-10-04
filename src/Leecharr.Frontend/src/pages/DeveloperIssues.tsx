import React, { useEffect, useState } from "react";
import { apiClient } from "../api/client";

interface IssueItem {
  id: number;
  number: number;
  title: string;
  state: string;
  author: string;
  authorAvatarUrl: string;
  htmlUrl: string;
  createdAt: string;
  updatedAt?: string;
  closedAt?: string;
  commentsCount: number;
  labels: string[];
}

interface IssuesResponse {
  repository: string;
  stateFilter: string;
  totalCount: number;
  items: IssueItem[];
  isRateLimited: boolean;
  message: string;
}

export const DeveloperIssues: React.FC = () => {
  const [stateFilter, setStateFilter] = useState<string>("all");
  const [data, setData] = useState<IssuesResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;
    setLoading(true);
    setError(null);

    apiClient
      .get<IssuesResponse>(`/system/developer/github/issues?state=${stateFilter}`)
      .then((res) => {
        if (isMounted) {
          setData(res);
          setLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (isMounted) {
          const message = err instanceof Error ? err.message : String(err);
          setError(message || "Failed to load issues.");
          setLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [stateFilter]);

  return (
    <div className="developer-page developer-issues-page" style={{ padding: "20px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "20px" }}>
        <div>
          <h1 style={{ margin: "0 0 8px 0", fontSize: "1.5rem", color: "var(--text-color, #f5f5f4)" }}>
            📋 GitHub Issues &amp; Roadmap
          </h1>
          <p style={{ margin: 0, color: "var(--text-dim, #a8a29e)", fontSize: "0.9rem" }}>
            Track active defects, enhancements, and roadmap tickets directly from GitHub.
          </p>
        </div>

        {data && (
          <a
            href={`https://github.com/${data.repository}/issues`}
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-sm btn-secondary"
            style={{ textDecoration: "none" }}
          >
            ↗ Open in GitHub
          </a>
        )}
      </div>

      <div style={{ display: "flex", gap: "8px", marginBottom: "16px" }}>
        {["all", "open", "closed"].map((s) => (
          <button
            key={s}
            type="button"
            className={`btn btn-sm ${stateFilter === s ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setStateFilter(s)}
            style={{ textTransform: "capitalize" }}
          >
            {s}
          </button>
        ))}
      </div>

      {loading && (
        <div style={{ padding: "40px", textAlign: "center", color: "var(--text-dim, #a8a29e)" }}>
          <div className="spinner" style={{ margin: "0 auto 12px auto" }} />
          Loading issues...
        </div>
      )}

      {error && (
        <div className="alert alert-danger" style={{ marginBottom: "20px" }}>
          {error}
        </div>
      )}

      {data?.isRateLimited && (
        <div className="alert alert-warning" style={{ marginBottom: "16px" }}>
          {data.message}
        </div>
      )}

      {!loading && data && data.items.length === 0 && (
        <div className="card" style={{ padding: "40px", textAlign: "center", color: "var(--text-dim, #a8a29e)" }}>
          No issues found matching state &apos;{stateFilter}&apos;.
        </div>
      )}

      {!loading && data && data.items.length > 0 && (
        <div style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
          {data.items.map((issue) => {
            const badgeColor = issue.state === "open" ? "#10b981" : "#8b5cf6";

            return (
              <div
                key={issue.id}
                className="card"
                style={{
                  background: "var(--bg-card, #131733)",
                  border: "1px solid var(--border-color, #44403c)",
                  borderRadius: "8px",
                  padding: "16px",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: "16px",
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: "12px", flex: 1 }}>
                  {issue.authorAvatarUrl ? (
                    <img
                      src={issue.authorAvatarUrl}
                      alt={issue.author}
                      style={{ width: "36px", height: "36px", borderRadius: "50%" }}
                    />
                  ) : (
                    <div style={{ width: "36px", height: "36px", borderRadius: "50%", background: "#44403c" }} />
                  )}

                  <div style={{ display: "flex", flexDirection: "column", gap: "4px" }}>
                    <div style={{ display: "flex", alignItems: "center", gap: "8px", flexWrap: "wrap" }}>
                      <span style={{ color: "var(--text-dim, #a8a29e)", fontWeight: "bold" }}>
                        #{issue.number}
                      </span>
                      <a
                        href={issue.htmlUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        style={{
                          color: "var(--text-color, #f5f5f4)",
                          fontWeight: 600,
                          textDecoration: "none",
                        }}
                      >
                        {issue.title}
                      </a>
                      <span
                        style={{
                          background: badgeColor,
                          color: "#fff",
                          fontSize: "0.75rem",
                          fontWeight: "bold",
                          padding: "2px 8px",
                          borderRadius: "12px",
                        }}
                      >
                        {issue.state.toUpperCase()}
                      </span>
                    </div>

                    <div style={{ fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", display: "flex", gap: "12px", alignItems: "center" }}>
                      <span>Reported by <strong>{issue.author}</strong></span>
                      <span>{new Date(issue.createdAt).toLocaleDateString()}</span>
                      {issue.commentsCount > 0 && (
                        <span>💬 {issue.commentsCount} comments</span>
                      )}
                      {issue.labels.map((l) => (
                        <span key={l} style={{ background: "rgba(245, 158, 11, 0.15)", color: "#f59e0b", padding: "1px 6px", borderRadius: "4px" }}>
                          {l}
                        </span>
                      ))}
                    </div>
                  </div>
                </div>

                <a
                  href={issue.htmlUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="btn btn-sm btn-secondary"
                  style={{ textDecoration: "none", whiteSpace: "nowrap" }}
                >
                  View Issue ↗
                </a>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
export default DeveloperIssues;
