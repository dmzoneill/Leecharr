import React, { useEffect, useState } from "react";
import { apiClient } from "../api/client";
import { MermaidChart } from "../components/MermaidChart";

interface UmlDiagramResponse {
  diagramType: string;
  subsystem: string;
  title: string;
  mermaidCode: string;
  nodeCount: number;
  edgeCount: number;
  generatedAtUtc: string;
  availableSubsystems: string[];
  availableDiagramTypes: string[];
}

export const DeveloperUml: React.FC = () => {
  const [diagramType, setDiagramType] = useState<string>("class");
  const [subsystem, setSubsystem] = useState<string>("torrents");
  const [includeInterfaces, setIncludeInterfaces] = useState(true);
  const [includeMethods, setIncludeMethods] = useState(true);
  const [data, setData] = useState<UmlDiagramResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [availableSubsystems, setAvailableSubsystems] = useState<string[]>([]);

  useEffect(() => {
    apiClient
      .get<string[]>("/system/developer/uml/subsystems")
      .then((subs) => setAvailableSubsystems(subs))
      .catch(() => setAvailableSubsystems(["all", "torrents", "trackers", "indexers", "authentication", "automation", "notifications", "network", "storage"]));
  }, []);

  useEffect(() => {
    let isMounted = true;
    setLoading(true);
    setError(null);

    const query = new URLSearchParams({
      diagramType,
      subsystem,
      includeInterfaces: includeInterfaces.toString(),
      includeMethods: includeMethods.toString(),
    });

    apiClient
      .get<UmlDiagramResponse>(`/system/developer/uml?${query.toString()}`)
      .then((res) => {
        if (isMounted) {
          setData(res);
          setLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (isMounted) {
          const message = err instanceof Error ? err.message : String(err);
          setError(message || "Failed to generate UML diagram.");
          setLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [diagramType, subsystem, includeInterfaces, includeMethods]);

  return (
    <div className="developer-page developer-uml-page" style={{ padding: "20px" }}>
      <div style={{ marginBottom: "20px" }}>
        <h1 style={{ margin: "0 0 8px 0", fontSize: "1.5rem", color: "var(--text-color, #f5f5f4)" }}>
          📐 Architecture &amp; UML Diagrams
        </h1>
        <p style={{ margin: 0, color: "var(--text-dim, #a8a29e)", fontSize: "0.9rem" }}>
          Dynamic C# reflection and frontend architecture scanner generating interactive Mermaid diagrams.
        </p>
      </div>

      <div
        className="card"
        style={{
          background: "var(--bg-card, #131733)",
          border: "1px solid var(--border-color, #44403c)",
          borderRadius: "8px",
          padding: "16px",
          marginBottom: "20px",
          display: "flex",
          flexWrap: "wrap",
          gap: "16px",
          alignItems: "center",
          justifyContent: "space-between",
        }}
      >
        <div style={{ display: "flex", flexWrap: "wrap", gap: "16px", alignItems: "center" }}>
          <div>
            <label style={{ display: "block", fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", marginBottom: "4px" }}>
              Diagram Type
            </label>
            <select
              className="form-control"
              value={diagramType}
              onChange={(e) => setDiagramType(e.target.value)}
              style={{
                background: "var(--bg-lighter, #1c1917)",
                color: "var(--text-color, #f5f5f4)",
                border: "1px solid var(--border-color, #44403c)",
                borderRadius: "4px",
                padding: "6px 12px",
              }}
            >
              <option value="class">Class Diagram (UML)</option>
              <option value="di">Dependency Injection Topology</option>
              <option value="state">Lifecycle State Machine</option>
              <option value="api">API Controller Route Map</option>
              <option value="frontend">Frontend Component Tree</option>
            </select>
          </div>

          <div>
            <label style={{ display: "block", fontSize: "0.8rem", color: "var(--text-dim, #a8a29e)", marginBottom: "4px" }}>
              Subsystem
            </label>
            <select
              className="form-control"
              value={subsystem}
              onChange={(e) => setSubsystem(e.target.value)}
              disabled={diagramType === "api" || diagramType === "frontend"}
              style={{
                background: "var(--bg-lighter, #1c1917)",
                color: "var(--text-color, #f5f5f4)",
                border: "1px solid var(--border-color, #44403c)",
                borderRadius: "4px",
                padding: "6px 12px",
              }}
            >
              {availableSubsystems.map((sub) => (
                <option key={sub} value={sub}>
                  {sub.toUpperCase()}
                </option>
              ))}
            </select>
          </div>

          {diagramType === "class" && (
            <div style={{ display: "flex", gap: "16px", marginTop: "16px" }}>
              <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "0.85rem", color: "var(--text-color, #f5f5f4)" }}>
                <input
                  type="checkbox"
                  checked={includeInterfaces}
                  onChange={(e) => setIncludeInterfaces(e.target.checked)}
                />
                Interfaces
              </label>
              <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "0.85rem", color: "var(--text-color, #f5f5f4)" }}>
                <input
                  type="checkbox"
                  checked={includeMethods}
                  onChange={(e) => setIncludeMethods(e.target.checked)}
                />
                Methods
              </label>
            </div>
          )}
        </div>

        {data && (
          <div style={{ display: "flex", gap: "12px", fontSize: "0.85rem", color: "var(--text-dim, #a8a29e)" }}>
            <span>Nodes: <strong style={{ color: "var(--accent-color, #f59e0b)" }}>{data.nodeCount}</strong></span>
            <span>Edges: <strong style={{ color: "var(--accent-color, #f59e0b)" }}>{data.edgeCount}</strong></span>
          </div>
        )}
      </div>

      {loading && (
        <div style={{ padding: "40px", textAlign: "center", color: "var(--text-dim, #a8a29e)" }}>
          <div className="spinner" style={{ margin: "0 auto 12px auto" }} />
          Scanning code architecture and rendering diagram...
        </div>
      )}

      {error && (
        <div className="alert alert-danger" style={{ marginBottom: "20px" }}>
          {error}
        </div>
      )}

      {!loading && data && (
        <MermaidChart
          chart={data.mermaidCode}
          title={data.title}
          id={`uml-${diagramType}-${subsystem}`}
        />
      )}
    </div>
  );
};
export default DeveloperUml;
