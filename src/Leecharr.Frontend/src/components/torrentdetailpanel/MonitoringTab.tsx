import React, { useState, useEffect, useRef } from "react";
import { useTorrentSpeedHistory } from "../../api/hooks";
import { formatBytes, formatSpeed } from "../../utils/formatters";
import type { Torrent } from "../../api/types";

const CHART_W = 460;
const CHART_H = 140;
const CHART_PAD = { top: 12, right: 14, bottom: 24, left: 65 };
const MAX_PTS = 60;

function MiniChart({
  title,
  value,
  data,
  color,
  unit = "speed",
}: {
  title: string;
  value: string;
  data: number[];
  color: string;
  unit?: "speed" | "count";
}) {
  const cw = CHART_W - CHART_PAD.left - CHART_PAD.right;
  const ch = CHART_H - CHART_PAD.top - CHART_PAD.bottom;

  let maxVal = 0;
  let sumVal = 0;
  let countVal = 0;
  for (const v of data) {
    if (typeof v === "number" && Number.isFinite(v)) {
      if (v > maxVal) maxVal = v;
      sumVal += v;
      countVal++;
    }
  }
  const avgVal = countVal > 0 ? sumVal / countVal : 0;
  const niceMax =
    maxVal > 0 ? maxVal * 1.15 : unit === "count" ? 10 : 1024 * 100;

  const pts = data
    .map((v, i) => {
      const rawVal = typeof v === "number" && Number.isFinite(v) ? v : 0;
      const clampedVal = Math.max(0, Math.min(rawVal, niceMax));
      const x = CHART_PAD.left + (i / Math.max(1, MAX_PTS - 1)) * cw;
      const y = CHART_PAD.top + ch - (clampedVal / niceMax) * ch;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");

  const areaPts = pts
    ? `${CHART_PAD.left},${CHART_PAD.top + ch} ${pts} ${CHART_PAD.left + cw},${CHART_PAD.top + ch}`
    : "";

  const clipId = `mini_clip_${title.toLowerCase().replace(/[^a-z0-9]/g, "_")}`;
  const gradId = `mini_grad_${title.toLowerCase().replace(/[^a-z0-9]/g, "_")}`;

  const formatTick = (val: number) => {
    return unit === "count" ? `${Math.round(val)}` : formatSpeed(val);
  };

  return (
    <div
      style={{
        backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
        borderRadius: "6px",
        border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
        padding: "0.6rem 0.8rem",
        display: "flex",
        flexDirection: "column",
        gap: "0.4rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          fontSize: "0.78rem",
        }}
      >
        <span style={{ fontWeight: 600, color: "var(--text-primary)" }}>
          {title}
        </span>
        <div style={{ display: "flex", gap: "0.6rem", alignItems: "center" }}>
          <span style={{ fontSize: "0.7rem", color: "var(--text-muted)" }}>
            Peak:{" "}
            <strong style={{ color: "var(--text-secondary)" }}>
              {formatTick(maxVal)}
            </strong>
          </span>
          <span style={{ fontSize: "0.7rem", color: "var(--text-muted)" }}>
            Avg:{" "}
            <strong style={{ color: "var(--text-secondary)" }}>
              {formatTick(avgVal)}
            </strong>
          </span>
          <span style={{ fontWeight: 700, color, fontSize: "0.85rem" }}>
            {value}
          </span>
        </div>
      </div>

      <svg
        width="100%"
        viewBox={`0 0 ${CHART_W} ${CHART_H}`}
        preserveAspectRatio="xMidYMid meet"
        style={{ overflow: "visible" }}
      >
        <defs>
          <clipPath id={clipId}>
            <rect x={CHART_PAD.left} y={CHART_PAD.top} width={cw} height={ch} />
          </clipPath>
          <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={color} stopOpacity={0.3} />
            <stop offset="100%" stopColor={color} stopOpacity={0.02} />
          </linearGradient>
        </defs>

        {/* Y Axis Grid lines & text */}
        <line
          x1={CHART_PAD.left}
          y1={CHART_PAD.top}
          x2={CHART_PAD.left + cw}
          y2={CHART_PAD.top}
          stroke="rgba(255, 255, 255, 0.08)"
          strokeDasharray="2 2"
        />
        <text
          x={CHART_PAD.left - 6}
          y={CHART_PAD.top + 4}
          fill="var(--text-muted)"
          fontSize="9"
          textAnchor="end"
        >
          {formatTick(niceMax)}
        </text>

        <line
          x1={CHART_PAD.left}
          y1={CHART_PAD.top + ch / 2}
          x2={CHART_PAD.left + cw}
          y2={CHART_PAD.top + ch / 2}
          stroke="rgba(255, 255, 255, 0.05)"
          strokeDasharray="2 2"
        />
        <text
          x={CHART_PAD.left - 6}
          y={CHART_PAD.top + ch / 2 + 3}
          fill="var(--text-muted)"
          fontSize="9"
          textAnchor="end"
        >
          {formatTick(niceMax / 2)}
        </text>

        <line
          x1={CHART_PAD.left}
          y1={CHART_PAD.top + ch}
          x2={CHART_PAD.left + cw}
          y2={CHART_PAD.top + ch}
          stroke="rgba(255, 255, 255, 0.15)"
        />
        <text
          x={CHART_PAD.left - 6}
          y={CHART_PAD.top + ch + 3}
          fill="var(--text-muted)"
          fontSize="9"
          textAnchor="end"
        >
          {unit === "count" ? "0" : "0 B/s"}
        </text>

        {/* X Axis Time Marks */}
        <text
          x={CHART_PAD.left}
          y={CHART_PAD.top + ch + 15}
          fill="var(--text-muted)"
          fontSize="9"
          textAnchor="start"
        >
          -60s
        </text>
        <text
          x={CHART_PAD.left + cw / 2}
          y={CHART_PAD.top + ch + 15}
          fill="var(--text-muted)"
          fontSize="9"
          textAnchor="middle"
        >
          -30s
        </text>
        <text
          x={CHART_PAD.left + cw}
          y={CHART_PAD.top + ch + 15}
          fill="var(--text-muted)"
          fontSize="9"
          textAnchor="end"
        >
          Now
        </text>

        {/* Filled polygon */}
        {areaPts && (
          <polygon
            points={areaPts}
            fill={`url(#${gradId})`}
            clipPath={`url(#${clipId})`}
          />
        )}

        {/* Line stroke */}
        {pts && (
          <polyline
            points={pts}
            fill="none"
            stroke={color}
            strokeWidth={1.8}
            strokeLinejoin="round"
            clipPath={`url(#${clipId})`}
          />
        )}
      </svg>
    </div>
  );
}

export function MonitoringTab({
  torrent,
  torrentId,
}: {
  torrent?: Torrent;
  torrentId?: number;
}) {
  const effectiveId = torrentId ?? torrent?.id ?? 0;
  const { data: history } = useTorrentSpeedHistory(effectiveId);
  const histRef = useRef<{ up: number[]; down: number[]; peers: number[] }>({
    up: Array(MAX_PTS).fill(0),
    down: Array(MAX_PTS).fill(0),
    peers: Array(MAX_PTS).fill(0),
  });
  const seededRef = useRef(false);
  const prevRef = useRef<{
    uploaded: number;
    downloaded: number;
    ts: number;
  } | null>(null);
  const prevIdRef = useRef<number | null>(null);
  const [, setTick] = useState(0);

  useEffect(() => {
    seededRef.current = false;
    histRef.current = {
      up: Array(MAX_PTS).fill(0),
      down: Array(MAX_PTS).fill(0),
      peers: Array(MAX_PTS).fill(0),
    };
    prevRef.current = null;
    prevIdRef.current = effectiveId;
    setTick((t) => t + 1);
  }, [effectiveId]);

  useEffect(() => {
    if (!history || history.length === 0 || seededRef.current) return;
    seededRef.current = true;
    const upArr = history.map((s) => s.uploadSpeed);
    const downArr = history.map((s) => s.downloadSpeed);
    histRef.current.up = [...histRef.current.up, ...upArr].slice(-MAX_PTS);
    histRef.current.down = [...histRef.current.down, ...downArr].slice(
      -MAX_PTS,
    );
    setTick((t) => t + 1);
  }, [history]);

  useEffect(() => {
    if (!torrent) return;
    const now = Date.now();
    const prev = prevRef.current;
    const idChanged = prevIdRef.current !== torrent.id;
    prevIdRef.current = torrent.id;

    if (!prev || idChanged) {
      prevRef.current = {
        uploaded: torrent.uploaded,
        downloaded: torrent.downloaded,
        ts: now,
      };
      return;
    }

    const dt = (now - prev.ts) / 1000;
    if (dt >= 1) {
      const upSpeed =
        torrent.uploadSpeed !== undefined
          ? torrent.uploadSpeed
          : Math.max(0, (torrent.uploaded - prev.uploaded) / dt);
      const downSpeed =
        torrent.downloadSpeed !== undefined
          ? torrent.downloadSpeed
          : Math.max(0, (torrent.downloaded - prev.downloaded) / dt);
      const peers = (torrent.seeders || 0) + (torrent.leechers || 0);

      histRef.current.up = [...histRef.current.up.slice(1), upSpeed];
      histRef.current.down = [...histRef.current.down.slice(1), downSpeed];
      histRef.current.peers = [...histRef.current.peers.slice(1), peers];

      prevRef.current = {
        uploaded: torrent.uploaded,
        downloaded: torrent.downloaded,
        ts: now,
      };

      setTick((t) => t + 1);
    }
  }, [torrent]);

  const h = histRef.current;
  const curUp =
    torrent?.uploadSpeed !== undefined
      ? torrent.uploadSpeed
      : h.up.length > 0
        ? h.up[h.up.length - 1]
        : 0;
  const curDown =
    torrent?.downloadSpeed !== undefined
      ? torrent.downloadSpeed
      : h.down.length > 0
        ? h.down[h.down.length - 1]
        : 0;
  const totalPeers = (torrent?.seeders || 0) + (torrent?.leechers || 0);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      {/* 2-Column Responsive Charts */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(360px, 1fr))",
          gap: "0.75rem",
        }}
      >
        <MiniChart
          title="Download Throughput"
          value={formatSpeed(curDown)}
          data={h.down}
          color="#06d6a0"
        />
        <MiniChart
          title="Upload Throughput"
          value={formatSpeed(curUp)}
          data={h.up}
          color="#ffd166"
        />
      </div>

      {/* Swarm & Bandwidth Summary Bar */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
          gap: "0.5rem",
          padding: "0.5rem 0.8rem",
          backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
          borderRadius: "6px",
          border: "1px solid var(--border-light, rgba(255, 255, 255, 0.08))",
          fontSize: "0.75rem",
        }}
      >
        <div>
          <span style={{ color: "var(--text-muted)" }}>
            Active Swarm Peers:{" "}
          </span>
          <strong style={{ color: "#60a5fa" }}>{totalPeers}</strong> (
          {torrent?.seeders || 0} seeds, {torrent?.leechers || 0} leechers)
        </div>
        <div>
          <span style={{ color: "var(--text-muted)" }}>
            Session Downloaded:{" "}
          </span>
          <strong style={{ color: "var(--text-primary)" }}>
            {formatBytes(torrent?.downloaded || 0)}
          </strong>
        </div>
        <div>
          <span style={{ color: "var(--text-muted)" }}>Session Uploaded: </span>
          <strong style={{ color: "var(--text-primary)" }}>
            {formatBytes(torrent?.uploaded || 0)}
          </strong>
        </div>
      </div>
    </div>
  );
}
