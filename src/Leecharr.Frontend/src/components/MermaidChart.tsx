import React, { useEffect, useRef, useState, useCallback } from "react";
import mermaid from "mermaid";

interface MermaidChartProps {
  chart: string;
  id?: string;
  title?: string;
}

let chartIndex = 0;

export const MermaidChart: React.FC<MermaidChartProps> = ({ chart, id, title }) => {
  const viewportRef = useRef<HTMLDivElement>(null);
  const [svgContent, setSvgContent] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [zoom, setZoom] = useState<number>(1.0);
  const [pan, setPan] = useState<{ x: number; y: number }>({ x: 0, y: 0 });
  const [isDragging, setIsDragging] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);

  const chartId = useRef(id || `mermaid-chart-${++chartIndex}`);
  const vbWidthRef = useRef<number>(0);
  const vbHeightRef = useRef<number>(0);
  const zoomRef = useRef<number>(1.0);
  const panRef = useRef<{ x: number; y: number }>({ x: 0, y: 0 });
  const isDraggingRef = useRef(false);
  const dragStartRef = useRef<{ x: number; y: number }>({ x: 0, y: 0 });
  const panStartRef = useRef<{ x: number; y: number }>({ x: 0, y: 0 });

  // Keep refs in sync for non-reactive event listeners
  useEffect(() => {
    zoomRef.current = zoom;
  }, [zoom]);

  useEffect(() => {
    panRef.current = pan;
  }, [pan]);

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

  const fitToScreen = useCallback(() => {
    if (!viewportRef.current) return;
    const rect = viewportRef.current.getBoundingClientRect();
    const w = vbWidthRef.current || 800;
    const h = vbHeightRef.current || 600;
    const padding = 32;
    const availW = Math.max(rect.width - padding, 100);
    const availH = Math.max(rect.height - padding, 100);
    const scale = Math.min(availW / w, availH / h, 1.25);
    const newPanX = Math.round((rect.width - w * scale) / 2);
    const newPanY = Math.round((rect.height - h * scale) / 2);
    setZoom(scale);
    setPan({ x: newPanX, y: newPanY });
  }, []);

  const initialFit = useCallback(() => {
    if (!viewportRef.current) return;
    const rect = viewportRef.current.getBoundingClientRect();
    const w = vbWidthRef.current || 800;
    const h = vbHeightRef.current || 600;
    const padding = 32;
    const availW = Math.max(rect.width - padding, 100);
    const availH = Math.max(rect.height - padding, 100);
    const fitScale = Math.min(availW / w, availH / h, 1.25);

    // If fitting would shrink text to unreadable micro-dimensions (< 45%), default to comfortable readable scale
    if (fitScale < 0.45) {
      const readableScale = 0.85;
      const newPanX = 24;
      const newPanY = Math.max(Math.round((rect.height - h * readableScale) / 2), 24);
      setZoom(readableScale);
      setPan({ x: newPanX, y: newPanY });
    } else {
      const newPanX = Math.round((rect.width - w * fitScale) / 2);
      const newPanY = Math.round((rect.height - h * fitScale) / 2);
      setZoom(fitScale);
      setPan({ x: newPanX, y: newPanY });
    }
  }, []);

  const resetActualSize = useCallback(() => {
    if (!viewportRef.current) {
      setZoom(1.0);
      setPan({ x: 20, y: 20 });
      return;
    }
    const rect = viewportRef.current.getBoundingClientRect();
    const w = vbWidthRef.current || 800;
    const h = vbHeightRef.current || 600;
    setZoom(1.0);
    const newPanX = w < rect.width ? Math.round((rect.width - w) / 2) : 24;
    const newPanY = h < rect.height ? Math.round((rect.height - h) / 2) : 24;
    setPan({ x: newPanX, y: newPanY });
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
        if (!isMounted) return;

        // Parse SVG viewBox to obtain intrinsic unconstrained dimensions
        const vbMatch = svg.match(/viewBox=["']([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)["']/i);
        let vbWidth = 0;
        let vbHeight = 0;
        if (vbMatch) {
          vbWidth = parseFloat(vbMatch[3]);
          vbHeight = parseFloat(vbMatch[4]);
        }
        vbWidthRef.current = vbWidth;
        vbHeightRef.current = vbHeight;

        // Normalize SVG by removing forced inline max-width and enforcing intrinsic viewBox dimensions
        let preparedSvg = svg
          .replace(/style="([^"]*)"/i, (_match: string, styleContent: string) => {
            const cleaned = styleContent.replace(/max-width\s*:\s*[^;]+;?/gi, "").trim();
            return `style="${cleaned ? cleaned + "; " : ""}max-width: none !important; display: block;"`;
          });

        if (!preparedSvg.includes('style="')) {
          preparedSvg = preparedSvg.replace(/<svg\b/i, '<svg style="max-width: none !important; display: block;" ');
        }

        if (vbWidth > 0 && vbHeight > 0) {
          preparedSvg = preparedSvg
            .replace(/<svg\b([^>]*)width="[^"]*"/i, "<svg$1")
            .replace(/<svg\b([^>]*)height="[^"]*"/i, "<svg$1")
            .replace(/<svg\b/i, `<svg width="${vbWidth}" height="${vbHeight}" `);
        }

        setSvgContent(preparedSvg);
        setError(null);

        // Auto-fit or readable scale on initial render or chart switch
        setTimeout(() => {
          if (isMounted) {
            initialFit();
          }
        }, 50);
      } catch (err: unknown) {
        if (isMounted) {
          const message = err instanceof Error ? err.message : String(err);
          setError(message || "Failed to render Mermaid diagram.");
        }
      }
    };

    renderChart();
    return () => {
      isMounted = false;
    };
  }, [chart, initialFit]);

  // Non-passive wheel listener for smooth cursor-anchored zoom
  useEffect(() => {
    const el = viewportRef.current;
    if (!el) return;

    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      const rect = el.getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      const mouseY = e.clientY - rect.top;

      const factor = e.deltaY < 0 ? 1.15 : 0.87;
      const currentZoom = zoomRef.current;
      const nextZoom = Math.min(Math.max(currentZoom * factor, 0.1), 5.0);
      if (Math.abs(nextZoom - currentZoom) < 0.001) return;

      const currentPan = panRef.current;
      const nextPanX = mouseX - (mouseX - currentPan.x) * (nextZoom / currentZoom);
      const nextPanY = mouseY - (mouseY - currentPan.y) * (nextZoom / currentZoom);

      zoomRef.current = nextZoom;
      panRef.current = { x: nextPanX, y: nextPanY };
      setZoom(nextZoom);
      setPan({ x: nextPanX, y: nextPanY });
    };

    el.addEventListener("wheel", onWheel, { passive: false });
    return () => el.removeEventListener("wheel", onWheel);
  }, []);

  // Window-level mouse move and mouse up listeners so dragging doesn't get interrupted
  useEffect(() => {
    const onMouseMove = (e: MouseEvent) => {
      if (!isDraggingRef.current) return;
      const dx = e.clientX - dragStartRef.current.x;
      const dy = e.clientY - dragStartRef.current.y;
      const newPan = {
        x: panStartRef.current.x + dx,
        y: panStartRef.current.y + dy,
      };
      panRef.current = newPan;
      setPan(newPan);
    };

    const onMouseUp = () => {
      if (isDraggingRef.current) {
        isDraggingRef.current = false;
        setIsDragging(false);
      }
    };

    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
    return () => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
    };
  }, []);

  // Escape key handler to close fullscreen
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && isFullscreen) {
        setIsFullscreen(false);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isFullscreen]);

  // Lock body scroll when fullscreen is active
  useEffect(() => {
    if (isFullscreen) {
      document.body.style.overflow = "hidden";
      setTimeout(fitToScreen, 60);
    } else {
      document.body.style.overflow = "";
      setTimeout(fitToScreen, 60);
    }
  }, [isFullscreen, fitToScreen]);

  const handleMouseDown = (e: React.MouseEvent) => {
    if (e.button !== 0) return;
    isDraggingRef.current = true;
    setIsDragging(true);
    dragStartRef.current = { x: e.clientX, y: e.clientY };
    panStartRef.current = { ...panRef.current };
  };

  const handleDoubleClick = () => {
    // Toggle between fit and 100% actual size
    if (Math.abs(zoom - 1.0) < 0.05) {
      fitToScreen();
    } else {
      resetActualSize();
    }
  };

  const handleZoomIn = () => {
    if (!viewportRef.current) return;
    const rect = viewportRef.current.getBoundingClientRect();
    const centerX = rect.width / 2;
    const centerY = rect.height / 2;
    const currentZoom = zoomRef.current;
    const nextZoom = Math.min(currentZoom * 1.25, 5.0);
    const currentPan = panRef.current;
    const nextPanX = centerX - (centerX - currentPan.x) * (nextZoom / currentZoom);
    const nextPanY = centerY - (centerY - currentPan.y) * (nextZoom / currentZoom);
    setZoom(nextZoom);
    setPan({ x: nextPanX, y: nextPanY });
  };

  const handleZoomOut = () => {
    if (!viewportRef.current) return;
    const rect = viewportRef.current.getBoundingClientRect();
    const centerX = rect.width / 2;
    const centerY = rect.height / 2;
    const currentZoom = zoomRef.current;
    const nextZoom = Math.max(currentZoom * 0.8, 0.1);
    const currentPan = panRef.current;
    const nextPanX = centerX - (centerX - currentPan.x) * (nextZoom / currentZoom);
    const nextPanY = centerY - (centerY - currentPan.y) * (nextZoom / currentZoom);
    setZoom(nextZoom);
    setPan({ x: nextPanX, y: nextPanY });
  };

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

  const containerStyle: React.CSSProperties = isFullscreen
    ? {
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: 9999,
        background: "rgba(11, 14, 30, 0.98)",
        padding: "20px",
        display: "flex",
        flexDirection: "column",
        gap: "12px",
        boxSizing: "border-box",
      }
    : {
        background: "var(--bg-card, #131733)",
        border: "1px solid var(--border-color, #23284b)",
        borderRadius: "8px",
        padding: "16px",
        marginTop: "16px",
        display: "flex",
        flexDirection: "column",
        gap: "12px",
      };

  return (
    <div className="mermaid-chart-card" style={containerStyle}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "10px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
          <h3 style={{ margin: 0, fontSize: "1.05rem", color: "var(--text-color, #e0e7ff)" }}>
            {title || "Architecture Diagram"}
          </h3>
          {vbWidthRef.current > 0 && (
            <span
              style={{
                fontSize: "0.75rem",
                color: "var(--text-dim, #94a3b8)",
                background: "rgba(0,0,0,0.3)",
                padding: "2px 8px",
                borderRadius: "4px",
                border: "1px solid var(--border-color, #23284b)",
              }}
            >
              {Math.round(vbWidthRef.current)} × {Math.round(vbHeightRef.current)} px
            </span>
          )}
        </div>

        {/* Interactive Controls Toolbar */}
        <div style={{ display: "flex", gap: "6px", alignItems: "center", flexWrap: "wrap" }}>
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              background: "rgba(0,0,0,0.4)",
              borderRadius: "6px",
              border: "1px solid var(--border-color, #23284b)",
              padding: "2px",
              gap: "2px",
            }}
          >
            <button
              type="button"
              className="btn btn-sm btn-secondary"
              onClick={handleZoomOut}
              title="Zoom Out (−)"
              style={{ padding: "3px 9px", minWidth: "28px", fontWeight: "bold" }}
            >
              −
            </button>
            <button
              type="button"
              className="btn btn-sm btn-secondary"
              onClick={resetActualSize}
              title="Click to reset to 100% actual size"
              style={{ padding: "3px 8px", fontSize: "0.75rem", fontFamily: "monospace" }}
            >
              {Math.round(zoom * 100)}%
            </button>
            <button
              type="button"
              className="btn btn-sm btn-secondary"
              onClick={handleZoomIn}
              title="Zoom In (+)"
              style={{ padding: "3px 9px", minWidth: "28px", fontWeight: "bold" }}
            >
              +
            </button>
          </div>

          <button
            type="button"
            className="btn btn-sm btn-secondary"
            onClick={resetActualSize}
            title="Reset to 100% scale (actual readable text size)"
          >
            1:1 Scale
          </button>
          <button
            type="button"
            className="btn btn-sm btn-secondary"
            onClick={fitToScreen}
            title="Fit diagram into viewport"
          >
            Fit View
          </button>
          <button
            type="button"
            className="btn btn-sm btn-secondary"
            onClick={() => setIsFullscreen((prev) => !prev)}
            title={isFullscreen ? "Exit Fullscreen (Esc)" : "Fullscreen Mode"}
          >
            {isFullscreen ? "✕ Exit Fullscreen" : "⛶ Fullscreen"}
          </button>
          <button
            type="button"
            className="btn btn-sm btn-secondary"
            onClick={handleCopy}
            title="Copy Mermaid Code"
          >
            {copied ? "✓ Copied" : "📋 Copy"}
          </button>
          <button
            type="button"
            className="btn btn-sm btn-primary"
            onClick={handleDownloadSvg}
            disabled={!svgContent}
            title="Download SVG"
          >
            💾 SVG
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
          style={{
            position: "relative",
            display: "flex",
            flexDirection: "column",
            flex: isFullscreen ? 1 : "initial",
          }}
        >
          <div
            ref={viewportRef}
            className="mermaid-viewport"
            onMouseDown={handleMouseDown}
            onDoubleClick={handleDoubleClick}
            style={{
              position: "relative",
              overflow: "hidden",
              height: isFullscreen ? "100%" : "640px",
              minHeight: isFullscreen ? "400px" : "480px",
              background: "#080a14",
              backgroundImage: "radial-gradient(circle, rgba(99, 102, 241, 0.15) 1px, transparent 1px)",
              backgroundSize: "24px 24px",
              borderRadius: "6px",
              border: "1px solid var(--border-color, #23284b)",
              cursor: isDragging ? "grabbing" : "grab",
              userSelect: "none",
            }}
          >
            <div
              className="mermaid-canvas"
              style={{
                transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
                transformOrigin: "0 0",
                display: "inline-block",
                position: "absolute",
                top: 0,
                left: 0,
                willChange: "transform",
                pointerEvents: isDragging ? "none" : "auto",
              }}
              dangerouslySetInnerHTML={{ __html: svgContent }}
            />
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginTop: "8px",
              fontSize: "0.75rem",
              color: "var(--text-dim, #94a3b8)",
              padding: "0 4px",
            }}
          >
            <span>💡 <strong>Tip:</strong> Drag to pan • Scroll wheel to zoom • Double-click to toggle Fit / 1:1</span>
            {isFullscreen && <span>Press <kbd style={{ padding: "1px 4px", background: "#1f2552", borderRadius: "3px" }}>Esc</kbd> to exit fullscreen</span>}
          </div>
        </div>
      )}
    </div>
  );
};
