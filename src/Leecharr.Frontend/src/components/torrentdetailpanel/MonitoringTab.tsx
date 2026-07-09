import { useState, useEffect, useRef } from "react";
import { useTorrentSpeedHistory } from "../../api/hooks";
import { formatSpeed } from "../../utils/formatters";
import type { Torrent } from "../../api/types";

const CHART_W = 400;
const CHART_H = 120;
const CHART_PAD = { top: 6, right: 10, bottom: 16, left: 50 };
const MAX_PTS = 60;

function MiniChart({
  title,
  value,
  data,
  color,
}: {
  title: string;
  value: string;
  data: number[];
  color: string;
}) {
  const cw = CHART_W - CHART_PAD.left - CHART_PAD.right;
  const ch = CHART_H - CHART_PAD.top - CHART_PAD.bottom;
  let maxVal = 0;
  for (const v of data) {
    if (typeof v === "number" && Number.isFinite(v) && v > maxVal) {
      maxVal = v;
    }
  }
  const niceMax = maxVal > 0 ? maxVal * 1.1 : 1;

  const pts = data
    .map((v, i) => {
      const rawVal = typeof v === "number" && Number.isFinite(v) ? v : 0;
      const clampedVal = Math.max(0, Math.min(rawVal, niceMax));
      const x = CHART_PAD.left + (i / Math.max(1, MAX_PTS - 1)) * cw;
      const y = CHART_PAD.top + ch - (clampedVal / niceMax) * ch;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");

  const clipId = `mini_clip_${title.toLowerCase().replace(/[^a-z0-9]/g, "_")}`;

  return (
    <div className="detail-panel-chart">
      <div className="detail-panel-chart-header">
        <span>{title}</span>
        <span style={{ color }}>{value}</span>
      </div>
      <svg
        width="100%"
        viewBox={`0 0 ${CHART_W} ${CHART_H}`}
        preserveAspectRatio="xMidYMid meet"
      >
        <defs>
          <clipPath id={clipId}>
            <rect x={CHART_PAD.left} y={CHART_PAD.top} width={cw} height={ch} />
          </clipPath>
        </defs>
        <rect
          x={CHART_PAD.left}
          y={CHART_PAD.top}
          width={cw}
          height={ch}
          fill="none"
          stroke="var(--border-light)"
          strokeWidth={0.5}
        />
        {pts && (
          <g clipPath={`url(#${clipId})`}>
            <polyline
              points={pts}
              fill="none"
              stroke={color}
              strokeWidth={1.5}
              strokeLinejoin="round"
            />
          </g>
        )}
      </svg>
    </div>
  );
}

export function MonitoringTab({ torrent }: { torrent: Torrent }) {
  const { data: history } = useTorrentSpeedHistory(torrent.id);
  const histRef = useRef<{ up: number[]; down: number[] }>({
    up: [],
    down: [],
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
    if (!history || history.length === 0 || seededRef.current) return;
    seededRef.current = true;
    histRef.current.up = history.map((s) => s.uploadSpeed);
    histRef.current.down = history.map((s) => s.downloadSpeed);
    setTick((t) => t + 1);
  }, [history]);

  useEffect(() => {
    const now = Date.now();
    const prev = prevRef.current;
    const idChanged = prevIdRef.current !== torrent.id;
    prevIdRef.current = torrent.id;
    if (prev && !idChanged) {
      const dt = (now - prev.ts) / 1000;
      if (dt >= 1) {
        const push = (arr: number[], val: number) => {
          const next = [...arr, val];
          return next.length > MAX_PTS
            ? next.slice(next.length - MAX_PTS)
            : next;
        };
        histRef.current.up = push(
          histRef.current.up,
          Math.max(0, (torrent.uploaded - prev.uploaded) / dt),
        );
        histRef.current.down = push(
          histRef.current.down,
          Math.max(0, (torrent.downloaded - prev.downloaded) / dt),
        );
        setTick((t) => t + 1);
      }
    }
    prevRef.current = {
      uploaded: torrent.uploaded,
      downloaded: torrent.downloaded,
      ts: now,
    };
  }, [torrent.id, torrent.uploaded, torrent.downloaded]);

  const h = histRef.current;
  const curUp = h.up.length > 0 ? h.up[h.up.length - 1] : 0;
  const curDown = h.down.length > 0 ? h.down[h.down.length - 1] : 0;

  return (
    <div className="detail-panel-monitoring">
      <MiniChart
        title="Upload"
        value={formatSpeed(curUp)}
        data={h.up}
        color="#c8a84e"
      />
      <MiniChart
        title="Download"
        value={formatSpeed(curDown)}
        data={h.down}
        color="#b5443a"
      />
    </div>
  );
}
