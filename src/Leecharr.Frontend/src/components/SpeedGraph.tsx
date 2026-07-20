import { useRef, useEffect, useState } from "react";
import { useSpeedHistory, useSeedingStats } from "../api/hooks";
import { formatSpeed } from "../utils/formatters";

interface SpeedDataPoint {
  uploadSpeed: number;
  downloadSpeed: number;
}

interface SpeedGraphProps {
  maxPoints?: number;
}

const DEFAULT_SVG_WIDTH = 1000;
const SVG_HEIGHT = 180;
const PADDING = { top: 12, right: 24, bottom: 26, left: 75 };

function getNiceMax(value: number): number {
  if (value <= 0) return 1024;
  const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
  const normalized = value / magnitude;
  let nice: number;
  if (normalized <= 1) nice = 1;
  else if (normalized <= 2) nice = 2;
  else if (normalized <= 5) nice = 5;
  else nice = 10;
  return nice * magnitude;
}

function SpeedGraph({ maxPoints = 60 }: SpeedGraphProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] =
    useState<number>(DEFAULT_SVG_WIDTH);
  const historyRef = useRef<SpeedDataPoint[]>([]);
  const seededRef = useRef(false);
  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);

  const { data: serverHistory } = useSpeedHistory();
  const { data: stats } = useSeedingStats();

  useEffect(() => {
    if (!containerRef.current) return;
    const el = containerRef.current;
    if (el.clientWidth > 0) {
      setContainerWidth(el.clientWidth);
    }

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        if (entry.contentRect.width > 0) {
          setContainerWidth(entry.contentRect.width);
        }
      }
    });

    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;

    const points: SpeedDataPoint[] = serverHistory
      .slice(-maxPoints)
      .map((s) => ({
        uploadSpeed: s.uploadSpeed,
        downloadSpeed: s.downloadSpeed,
      }));
    historyRef.current = points;

    if (serverHistory.length > 0) {
      const last = serverHistory[serverHistory.length - 1];
      prevRef.current = {
        totalUploaded: last.totalUploaded,
        totalDownloaded: last.totalDownloaded,
        timestamp: new Date(last.timestamp).getTime(),
      };
    }
  }, [serverHistory, maxPoints]);

  useEffect(() => {
    if (!stats) return;

    const now = Date.now();
    const prev = prevRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 0.5) {
        const uploadSpeed = Math.max(
          0,
          (stats.totalUploaded - prev.totalUploaded) / timeDelta,
        );
        const downloadSpeed = Math.max(
          0,
          (stats.totalDownloaded - prev.totalDownloaded) / timeDelta,
        );

        const next = [...historyRef.current, { uploadSpeed, downloadSpeed }];
        if (next.length > maxPoints) {
          next.splice(0, next.length - maxPoints);
        }
        historyRef.current = next;
      }
    }

    prevRef.current = {
      totalUploaded: stats.totalUploaded,
      totalDownloaded: stats.totalDownloaded,
      timestamp: now,
    };
  }, [stats, maxPoints]);

  const history = historyRef.current;
  const svgWidth = Math.max(300, containerWidth);
  const chartWidth = Math.max(100, svgWidth - PADDING.left - PADDING.right);
  const chartHeight = SVG_HEIGHT - PADDING.top - PADDING.bottom;

  let maxSpeed = 0;
  for (const point of history) {
    maxSpeed = Math.max(maxSpeed, point.uploadSpeed, point.downloadSpeed);
  }
  const niceMax = getNiceMax(maxSpeed > 0 ? maxSpeed * 1.15 : 1024);

  const gridLineCount = 3;
  const gridLines = Array.from({ length: gridLineCount + 1 }, (_, i) => {
    const value = (niceMax / gridLineCount) * i;
    const y = PADDING.top + chartHeight - (value / niceMax) * chartHeight;
    return { value, y };
  });

  const toPoints = (
    data: SpeedDataPoint[],
    key: "uploadSpeed" | "downloadSpeed",
  ): string => {
    if (data.length === 0) return "";
    return data
      .map((point, i) => {
        const x = PADDING.left + (i / Math.max(1, maxPoints - 1)) * chartWidth;
        const y =
          PADDING.top + chartHeight - (point[key] / niceMax) * chartHeight;
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(" ");
  };

  const toAreaPath = (
    data: SpeedDataPoint[],
    key: "uploadSpeed" | "downloadSpeed",
  ): string => {
    if (data.length < 2) return "";
    const bottom = PADDING.top + chartHeight;
    const firstX = PADDING.left;
    const lastX =
      PADDING.left +
      ((data.length - 1) / Math.max(1, maxPoints - 1)) * chartWidth;

    const linePoints = data
      .map((point, i) => {
        const x = PADDING.left + (i / Math.max(1, maxPoints - 1)) * chartWidth;
        const y =
          PADDING.top + chartHeight - (point[key] / niceMax) * chartHeight;
        return `L ${x.toFixed(1)} ${y.toFixed(1)}`;
      })
      .join(" ");

    return `M ${firstX.toFixed(1)} ${bottom.toFixed(1)} ${linePoints} L ${lastX.toFixed(1)} ${bottom.toFixed(1)} Z`;
  };

  const uploadPoints = toPoints(history, "uploadSpeed");
  const downloadPoints = toPoints(history, "downloadSpeed");
  const uploadArea = toAreaPath(history, "uploadSpeed");
  const downloadArea = toAreaPath(history, "downloadSpeed");

  const currentUpload =
    history.length > 0 ? history[history.length - 1].uploadSpeed : 0;
  const currentDownload =
    history.length > 0 ? history[history.length - 1].downloadSpeed : 0;

  return (
    <div
      className="card"
      style={{
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        border: "1px solid rgba(255, 255, 255, 0.08)",
        marginBottom: "1.25rem",
        padding: "1rem 1.25rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "0.75rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <h3
            style={{
              margin: 0,
              border: "none",
              padding: 0,
              fontSize: "1.05rem",
            }}
          >
            Transfer Speed
          </h3>
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.3rem",
              fontSize: "0.72rem",
              color: "var(--accent, #c8a84e)",
              background: "rgba(200, 168, 78, 0.12)",
              padding: "0.15rem 0.5rem",
              borderRadius: "4px",
              fontWeight: 600,
            }}
          >
            <span
              style={{
                width: 6,
                height: 6,
                borderRadius: "50%",
                backgroundColor: "var(--accent, #c8a84e)",
                display: "inline-block",
                animation: "pulse 2s infinite",
              }}
            />
            Live (1s)
          </span>
        </div>

        <div
          className="speed-graph-legend"
          style={{ margin: 0, display: "flex", gap: "1rem" }}
        >
          <span
            className="speed-graph-legend-item"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              fontSize: "0.82rem",
            }}
          >
            <span
              className="speed-graph-indicator"
              style={{
                width: 10,
                height: 10,
                borderRadius: "50%",
                backgroundColor: "var(--accent, #c8a84e)",
                display: "inline-block",
              }}
            />
            Upload:{" "}
            <strong style={{ color: "var(--accent, #c8a84e)" }}>
              {formatSpeed(currentUpload)}
            </strong>
          </span>
          <span
            className="speed-graph-legend-item"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              fontSize: "0.82rem",
            }}
          >
            <span
              className="speed-graph-indicator"
              style={{
                width: 10,
                height: 10,
                borderRadius: "50%",
                backgroundColor: "#e74c3c",
                display: "inline-block",
              }}
            />
            Download:{" "}
            <strong style={{ color: "#e74c3c" }}>
              {formatSpeed(currentDownload)}
            </strong>
          </span>
        </div>
      </div>

      <div
        className="speed-graph"
        ref={containerRef}
        style={{ width: "100%", height: "180px", overflow: "hidden" }}
      >
        <svg
          width="100%"
          height="100%"
          viewBox={`0 0 ${svgWidth} ${SVG_HEIGHT}`}
          preserveAspectRatio="none"
          style={{ overflow: "visible", display: "block" }}
        >
          <defs>
            <linearGradient id="speedUploadGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#c8a84e" stopOpacity="0.3" />
              <stop offset="100%" stopColor="#c8a84e" stopOpacity="0.0" />
            </linearGradient>
            <linearGradient id="speedDownloadGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#e74c3c" stopOpacity="0.25" />
              <stop offset="100%" stopColor="#e74c3c" stopOpacity="0.0" />
            </linearGradient>
          </defs>

          {/* Background grid box */}
          <rect
            x={PADDING.left}
            y={PADDING.top}
            width={chartWidth}
            height={chartHeight}
            fill="rgba(255, 255, 255, 0.015)"
            stroke="rgba(255, 255, 255, 0.06)"
            strokeWidth={1}
            rx={4}
          />

          {/* Grid lines & values */}
          {gridLines.map(({ value, y }, i) => (
            <g key={i}>
              <line
                x1={PADDING.left}
                y1={y}
                x2={svgWidth - PADDING.right}
                y2={y}
                stroke="rgba(255, 255, 255, 0.06)"
                strokeWidth={1}
                strokeDasharray={i === 0 ? "none" : "3 3"}
              />
              <text
                x={PADDING.left - 8}
                y={y + 3.5}
                textAnchor="end"
                fill="var(--text-muted)"
                fontSize={10}
                fontFamily="inherit"
              >
                {formatSpeed(value)}
              </text>
            </g>
          ))}

          {/* Area Fills */}
          {uploadArea && <path d={uploadArea} fill="url(#speedUploadGrad)" />}
          {downloadArea && (
            <path d={downloadArea} fill="url(#speedDownloadGrad)" />
          )}

          {/* Polylines */}
          {uploadPoints && (
            <polyline
              points={uploadPoints}
              fill="none"
              stroke="#c8a84e"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          )}

          {downloadPoints && (
            <polyline
              points={downloadPoints}
              fill="none"
              stroke="#e74c3c"
              strokeWidth={1.8}
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          )}

          {/* Time axis labels */}
          <text
            x={PADDING.left}
            y={SVG_HEIGHT - 6}
            fill="var(--text-muted)"
            fontSize={9.5}
            textAnchor="start"
          >
            {maxPoints}s ago
          </text>
          <text
            x={PADDING.left + chartWidth / 2}
            y={SVG_HEIGHT - 6}
            fill="var(--text-muted)"
            fontSize={9.5}
            textAnchor="middle"
          >
            {Math.floor(maxPoints / 2)}s ago
          </text>
          <text
            x={svgWidth - PADDING.right}
            y={SVG_HEIGHT - 6}
            fill="var(--text-muted)"
            fontSize={9.5}
            textAnchor="end"
          >
            now
          </text>
        </svg>
      </div>
    </div>
  );
}

export default SpeedGraph;
