import React, { useEffect, useRef, useState } from "react";
import mermaid from "mermaid";

interface MermaidChartProps {
  chart: string;
  id?: string;
  title?: string;
}

let chartIndex = 0;

export const MermaidChart: React.FC<MermaidChartProps> = ({ chart, id, title }) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const [svgContent, setSvgContent] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const chartId = useRef(id || `mermaid-chart-${++chartIndex}`);

  useEffect(() => {
    mermaid.initialize({
      startOnLoad: false,
      theme: "dark",
      themeVariables: {
        darkMode: true,
        background: "#0b0e1e",
        primaryColor: "#131733",
        primaryTextColor: "#e0e7ff",
        primaryBorderColor: "#6366f1",
        lineColor: "#818cf8",
        secondaryColor: "#1f2552",
        tertiaryColor: "#0b0e1e",
      },
      securityLevel: "loose",
    });
  }, []);

  useEffect(() => {
    let isMounted = true;
    const renderChart = async () => {
      if (!chart || !chart.trim()) {
        setSvgContent("");
        setError(null);
        return;
      }

      try {
        const uniqueId = `${chartId.current}-${Date.now()}`;
        const { svg } = await mermaid.render(uniqueId, chart);
        if (isMounted) {
          setSvgContent(svg);
          setError(null);
        }
      } catch (err: any) {
        if (isMounted) {
          setError(err?.message || "Failed to render Mermaid diagram.");
        }
      }
    };

    renderChart();
    return () => {
      isMounted = false;
    };
  }, [chart]);

  const handleCopy = () => {
    navigator.clipboard.writeText(chart);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleDownloadSvg = () => {
    if (!svgContent) return;
    const blob = new Blob([svgContent], { type: "image/svg+xml;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${title || "diagram"}.svg`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  return (
    <div className="mermaid-chart-card" style={{
      background: "var(--bg-card, #131733)",
      border: "1px solid var(--border-color, #23284b)",
      borderRadius: "8px",
      padding: "16px",
      marginTop: "16px",
      display: "flex",
      flexDirection: "column",
      gap: "12px"
    }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--text-color, #e0e7ff)" }}>
          {title || "Architecture Diagram"}
        </h3>
        <div style={{ display: "flex", gap: "8px" }}>
          <button
            type="button"
            className="btn btn-sm btn-secondary"
            onClick={handleCopy}
            title="Copy Mermaid Code"
          >
            {copied ? "✓ Copied" : "📋 Copy Mermaid"}
          </button>
          <button
            type="button"
            className="btn btn-sm btn-primary"
            onClick={handleDownloadSvg}
            disabled={!svgContent}
            title="Download SVG"
          >
            💾 Download SVG
          </button>
        </div>
      </div>

      {error ? (
        <div className="alert alert-warning" style={{ margin: 0 }}>
          <strong>Rendering Warning:</strong> {error}
          <pre style={{ marginTop: "8px", fontSize: "0.8rem", whiteSpace: "pre-wrap" }}>{chart}</pre>
        </div>
      ) : (
        <div
          ref={containerRef}
          className="mermaid-viewport"
          style={{
            overflowX: "auto",
            overflowY: "auto",
            maxHeight: "650px",
            padding: "16px",
            background: "rgba(0,0,0,0.2)",
            borderRadius: "6px",
            display: "flex",
            justifyContent: "center"
          }}
          dangerouslySetInnerHTML={{ __html: svgContent }}
        />
      )}
    </div>
  );
};
