import { useRef, useEffect, useState, useMemo, useId } from "react";
import { useSpeedHistory, useSeedingStats, useTorrents } from "../api/hooks";
import {
  useTorrentStore,
  useAggregatedTorrentMetrics,
} from "../stores/useTorrentStore";
import { formatSpeed } from "../utils/formatters";
import { useTranslation } from "../i18n";

export type TimeframeOption = "60s" | "5m" | "15m" | "1h" | "24h";

interface RawSpeedPoint {
  uploadSpeed: number;
  downloadSpeed: number;
  time: number;
}

interface SpeedGraphProps {
  maxPoints?: number;
  defaultTimeframe?: TimeframeOption;
}

const DEFAULT_SVG_WIDTH = 1000;
const SVG_HEIGHT = 180;
const PADDING = { top: 12, right: 24, bottom: 26, left: 75 };

interface TimeframeConfig {
  key: TimeframeOption;
  label: string;
  durationMs: number;
  pointCount: number;
  startLabel: string;
  midLabel: string;
}

const TIMEFRAMES: TimeframeConfig[] = [
  {
    key: "60s",
    label: "60s",
    durationMs: 60 * 1000,
    pointCount: 60,
    startLabel: "60s ago",
    midLabel: "30s ago",
  },
  {
    key: "5m",
    label: "5m",
    durationMs: 5 * 60 * 1000,
    pointCount: 60,
    startLabel: "5m ago",
    midLabel: "2.5m ago",
  },
  {
    key: "15m",
    label: "15m",
    durationMs: 15 * 60 * 1000,
    pointCount: 60,
    startLabel: "15m ago",
    midLabel: "7.5m ago",
  },
  {
    key: "1h",
    label: "1h",
    durationMs: 60 * 60 * 1000,
    pointCount: 60,
    startLabel: "1h ago",
    midLabel: "30m ago",
  },
  {
    key: "24h",
    label: "24h",
    durationMs: 24 * 60 * 60 * 1000,
    pointCount: 72,
    startLabel: "24h ago",
    midLabel: "12h ago",
  },
];

function getNiceMax(value: number): number {
  if (value <= 0) return 1024;
  const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
  const normalized = value / magnitude;
  let nice: number;
  if (normalized <= 1) nice = 1;
  else if (normalized <= 2) nice = 2;
  else if (normalized <= 5) nice = 5;
  else nice = 10;
  return Math.max(1024, nice * magnitude);
}

export function SpeedGraph({
  maxPoints = 60,
  defaultTimeframe = "60s",
}: SpeedGraphProps) {
  const { t } = useTranslation();
  const gradientId = useId().replace(/:/g, "_");
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] =
    useState<number>(DEFAULT_SVG_WIDTH);
  const [timeframe, setTimeframe] = useState<TimeframeOption>(defaultTimeframe);
  const [rawHistory, setRawHistory] = useState<RawSpeedPoint[]>([]);
  const seededRef = useRef(false);

  const { data: serverHistory } = useSpeedHistory();
  const { data: stats } = useSeedingStats();
  const { data: torrents } = useTorrents();
  const metrics = useAggregatedTorrentMetrics(torrents, stats);
  const liveSpeeds = useMemo(
    () => ({
      downloadSpeed: metrics.downloadSpeed,
      uploadSpeed: metrics.uploadSpeed,
    }),
    [metrics.downloadSpeed, metrics.uploadSpeed],
  );

  const liveSpeedsRef = useRef(liveSpeeds);
  liveSpeedsRef.current = liveSpeeds;

  const currentTfConfig = useMemo(
    () => TIMEFRAMES.find((tf) => tf.key === timeframe) || TIMEFRAMES[0],
    [timeframe],
  );

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

  // Seed initial history from server
  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;

    const now = Date.now();
    const points: RawSpeedPoint[] = serverHistory.map((s, idx) => {
      const parsedTime = s.timestamp ? new Date(s.timestamp).getTime() : 0;
      const pointTime =
        parsedTime > 0 ? parsedTime : now - (serverHistory.length - idx) * 1000;
      return {
        uploadSpeed: Number(s.uploadSpeed) || 0,
        downloadSpeed: Number(s.downloadSpeed) || 0,
        time: pointTime,
      };
    });
    setRawHistory(points);
  }, [serverHistory]);

  // Append real-time speeds at regular intervals and on telemetry updates
  useEffect(() => {
    const interval = setInterval(() => {
      const { uploadSpeed, downloadSpeed } = liveSpeedsRef.current;
      const now = Date.now();

      setRawHistory((prev) => {
        const next = [...prev, { uploadSpeed, downloadSpeed, time: now }];
        // Keep up to 24h of history
        const cutoff = now - 24 * 60 * 60 * 1000;
        return next.filter((p) => p.time >= cutoff);
      });
    }, 1500);

    return () => clearInterval(interval);
  }, []);

  // Resample points according to active timeframe
  const displayPoints = useMemo(() => {
    const { durationMs, pointCount } = currentTfConfig;
    const now = Date.now();
    const startTime = now - durationMs;
    const intervalMs = durationMs / Math.max(1, pointCount - 1);

    const relevant = rawHistory.filter((p) => p.time >= startTime - intervalMs);

    if (relevant.length === 0) {
      const currentUp = liveSpeeds.uploadSpeed;
      const currentDown = liveSpeeds.downloadSpeed;
      if (currentUp === 0 && currentDown === 0) {
        return [];
      }
      return [
        {
          uploadSpeed: currentUp,
          downloadSpeed: currentDown,
        },
      ];
    }

    const minTime = relevant[0].time;
    const sampled: Array<{ uploadSpeed: number; downloadSpeed: number }> = [];
    for (let i = 0; i < pointCount; i++) {
      const targetTime = startTime + i * intervalMs;
      // Skip points before the earliest recorded history to anchor sparse data to present
      if (targetTime < minTime - intervalMs * 0.75) {
        continue;
      }
      // Find nearest point within window
      let closest = relevant[0];
      let minDiff = Math.abs(relevant[0].time - targetTime);

      for (let j = 1; j < relevant.length; j++) {
        const diff = Math.abs(relevant[j].time - targetTime);
        if (diff < minDiff) {
          minDiff = diff;
          closest = relevant[j];
        }
      }
      sampled.push({
        uploadSpeed: closest.uploadSpeed,
        downloadSpeed: closest.downloadSpeed,
      });
    }

    if (sampled.length === 0 && relevant.length > 0) {
      sampled.push({
        uploadSpeed: relevant[relevant.length - 1].uploadSpeed,
        downloadSpeed: relevant[relevant.length - 1].downloadSpeed,
      });
    }

    return sampled;
  }, [rawHistory, currentTfConfig, liveSpeeds]);

  const svgWidth = Math.max(300, containerWidth);
  const chartWidth = Math.max(100, svgWidth - PADDING.left - PADDING.right);
  const chartHeight = SVG_HEIGHT - PADDING.top - PADDING.bottom;

  let maxSpeed = 0;
  for (const point of displayPoints) {
    const up = Number(point.uploadSpeed) || 0;
    const down = Number(point.downloadSpeed) || 0;
    maxSpeed = Math.max(maxSpeed, up, down);
  }
  const niceMax = getNiceMax(maxSpeed > 0 ? maxSpeed * 1.15 : 1024);

  const gridLineCount = 3;
  const gridLines = Array.from({ length: gridLineCount + 1 }, (_, i) => {
    const value = (niceMax / gridLineCount) * i;
    const y = PADDING.top + chartHeight - (value / niceMax) * chartHeight;
    return { value, y };
  });

  const effectiveMaxPoints = currentTfConfig?.pointCount ?? maxPoints;

  const toPoints = (
    data: Array<{ uploadSpeed: number; downloadSpeed: number }>,
    key: "uploadSpeed" | "downloadSpeed",
  ): string => {
    if (data.length === 0) return "";
    const offset = Math.max(0, effectiveMaxPoints - data.length);
    return data
      .map((point, i) => {
        const x =
          PADDING.left +
          ((offset + i) / Math.max(1, effectiveMaxPoints - 1)) * chartWidth;
        const val = Number(point[key]) || 0;
        const y = PADDING.top + chartHeight - (val / niceMax) * chartHeight;
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(" ");
  };

  const toAreaPath = (
    data: Array<{ uploadSpeed: number; downloadSpeed: number }>,
    key: "uploadSpeed" | "downloadSpeed",
  ): string => {
    if (data.length < 2) return "";
    const offset = Math.max(0, effectiveMaxPoints - data.length);
    const bottom = PADDING.top + chartHeight;
    const firstX =
      PADDING.left +
      (offset / Math.max(1, effectiveMaxPoints - 1)) * chartWidth;
    const lastX =
      PADDING.left +
      ((offset + data.length - 1) / Math.max(1, effectiveMaxPoints - 1)) *
        chartWidth;

    const linePoints = data
      .map((point, i) => {
        const x =
          PADDING.left +
          ((offset + i) / Math.max(1, effectiveMaxPoints - 1)) * chartWidth;
        const val = Number(point[key]) || 0;
        const y = PADDING.top + chartHeight - (val / niceMax) * chartHeight;
        return `L ${x.toFixed(1)} ${y.toFixed(1)}`;
      })
      .join(" ");

    return `M ${firstX.toFixed(1)} ${bottom.toFixed(1)} ${linePoints} L ${lastX.toFixed(1)} ${bottom.toFixed(1)} Z`;
  };

  const uploadPoints = toPoints(displayPoints, "uploadSpeed");
  const downloadPoints = toPoints(displayPoints, "downloadSpeed");
  const uploadArea = toAreaPath(displayPoints, "uploadSpeed");
  const downloadArea = toAreaPath(displayPoints, "downloadSpeed");

  const currentUpload =
    displayPoints.length > 0
      ? Number(displayPoints[displayPoints.length - 1].uploadSpeed) || 0
      : Number(stats?.uploadSpeed) || 0;
  const currentDownload =
    displayPoints.length > 0
      ? Number(displayPoints[displayPoints.length - 1].downloadSpeed) || 0
      : Number(stats?.downloadSpeed) || 0;

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
          flexWrap: "wrap",
          gap: "0.75rem",
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
            {t("components.transferSpeed", "Transfer Speed")}
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
            {t("components.live1s", "Live (1s)")}
          </span>
        </div>

        {/* Timeframe selector toggles */}
        <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
          <div className="view-toggle" style={{ margin: 0 }}>
            {TIMEFRAMES.map((tf) => (
              <button
                key={tf.key}
                type="button"
                className={`view-toggle-btn ${timeframe === tf.key ? "active" : ""}`}
                onClick={() => setTimeframe(tf.key)}
                style={{ padding: "0.15rem 0.45rem", fontSize: "0.72rem" }}
              >
                {tf.label}
              </button>
            ))}
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
              {t("components.upload", "Upload")}:{" "}
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
              {t("components.download", "Download")}:{" "}
              <strong style={{ color: "#e74c3c" }}>
                {formatSpeed(currentDownload)}
              </strong>
            </span>
          </div>
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
            <linearGradient
              id={`speedUploadGrad_${gradientId}`}
              x1="0"
              y1="0"
              x2="0"
              y2="1"
            >
              <stop offset="0%" stopColor="#c8a84e" stopOpacity="0.3" />
              <stop offset="100%" stopColor="#c8a84e" stopOpacity="0.0" />
            </linearGradient>
            <linearGradient
              id={`speedDownloadGrad_${gradientId}`}
              x1="0"
              y1="0"
              x2="0"
              y2="1"
            >
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
          {uploadArea && (
            <path d={uploadArea} fill={`url(#speedUploadGrad_${gradientId})`} />
          )}
          {downloadArea && (
            <path
              d={downloadArea}
              fill={`url(#speedDownloadGrad_${gradientId})`}
            />
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
            {currentTfConfig.startLabel}
          </text>
          <text
            x={PADDING.left + chartWidth / 2}
            y={SVG_HEIGHT - 6}
            fill="var(--text-muted)"
            fontSize={9.5}
            textAnchor="middle"
          >
            {currentTfConfig.midLabel}
          </text>
          <text
            x={svgWidth - PADDING.right}
            y={SVG_HEIGHT - 6}
            fill="var(--text-muted)"
            fontSize={9.5}
            textAnchor="end"
          >
            {t("components.now", "now")}
          </text>
        </svg>
      </div>
    </div>
  );
}

export default SpeedGraph;
