import React, { useEffect, useState } from "react";
import { apiClient } from "../api/client";

interface PullRequestItem {
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
  mergedAt?: string;
  draft: boolean;
  commentsCount: number;
  labels: string[];
}

interface PullRequestsResponse {
  repository: string;
  stateFilter: string;
  totalCount: number;
  items: PullRequestItem[];
  isRateLimited: boolean;
  message: string;
}

export const DeveloperPullRequests: React.FC = () => {
  const [stateFilter, setStateFilter] = useState<string>("all");
  const [data, setData] = useState<PullRequestsResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;
    setLoading(true);
    setError(null);

    apiClient
      .get<PullRequestsResponse>(`/system/developer/github/pulls?state=${stateFilter}`)
      .then((res) => {
        if (isMounted) {
          setData(res);
          setLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (isMounted) {
          const message = err instanceof Error ? err.message : String(err);
          setError(message || "Failed to load pull requests.");
          setLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [stateFilter]);

  return (
    <div className="developer-page developer-prs-page" style={{ padding: "20px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "20px" }}>
        <div>
          <h1 style={{ margin: "0 0 8px 0", fontSize: "1.5rem", color: "var(--text-color, #f5f5f4)" }}>
            🐙 Pull Requests
          </h1>
          <p style={{ margin: 0, color: "var(--text-dim, #a8a29e)", fontSize: "0.9rem" }}>
            Repository pull requests with live build status, reviews, and branch tracking.
          </p>
        </div>

        {data && (
          <a
            href={`https://github.com/${data.repository}/pulls`}
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
          Loading pull requests...
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
          No pull requests found matching state &apos;{stateFilter}&apos;.
        </div>
      )}

      {!loading && data && data.items.length > 0 && (
        <div style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
          {data.items.map((pr) => {
            const isMerged = !!pr.mergedAt;
            const badgeColor = isMerged ? "#8b5cf6" : pr.state === "open" ? "#10b981" : "#ef4444";
            const badgeText = isMerged ? "Merged" : pr.draft ? "Draft" : pr.state.toUpperCase();

            return (
              <div
                key={pr.id}
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
                  {pr.authorAvatarUrl ? (
                    <img
                      src={pr.authorAvatarUrl}
                      alt={pr.author}
                      style={{ width: "36px", height: "36px", borderRadius: "50%" }}
                    />
                  ) : (
                    <div style={{ width: "36px", height: "36px", borderRadius: "50%", background: "#44403c" }} />
                  )}

                  <div style={{ display: "flex", flexDirection: "column", gap: "4px" }}>
                    <div style={{ display: "flex", alignItems: "center", gap: "8px", flexWrap: "wrap" }}>
                      <span style={{ color: "var(--text-dim, #a8a29e)", fontWeight: "bold" }}>
                        #{pr.number}
                      </span>
                      <a
                        href={pr.htmlUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        style={{
                          color: "var(--text-color, #f5f5f4)",
                          fontWeight: 600,
                          textDecoration: "none",
                        }}
                      >
                        {pr.title}
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
                        {badgeText}
                      </span>
                    </div>

                    <div style={{ fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", display: "flex", gap: "12px" }}>
                      <span>Opened by <strong>{pr.author}</strong></span>
                      <span>{new Date(pr.createdAt).toLocaleDateString()}</span>
                      {pr.labels.map((l) => (
                        <span key={l} style={{ background: "rgba(245, 158, 11, 0.15)", color: "#f59e0b", padding: "1px 6px", borderRadius: "4px" }}>
                          {l}
                        </span>
                      ))}
                    </div>
                  </div>
                </div>

                <a
                  href={pr.htmlUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="btn btn-sm btn-secondary"
                  style={{ textDecoration: "none", whiteSpace: "nowrap" }}
                >
                  View PR ↗
                </a>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
export default DeveloperPullRequests;
