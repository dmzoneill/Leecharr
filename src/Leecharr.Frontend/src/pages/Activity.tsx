import { useTranslation } from "../i18n";
import { useState, useRef, useEffect } from "react";
import { Link } from "react-router";
import { useTorrents, useSeedingStats, useSpeedHistory } from "../api/hooks";
import { useAggregatedTorrentMetrics } from "../stores/useTorrentStore";
import { formatSpeed, formatRatio } from "../utils/formatters";
import LineChart from "../components/LineChart";

const MAX_POINTS = 60;

function sanitizeNumber(val: unknown): number {
  const num = typeof val === "number" ? val : parseFloat(String(val));
  return Number.isFinite(num) && num > 0 ? num : 0;
}

interface HistoryState {
  uploadSpeed: number[];
  downloadSpeed: number[];
  activeTorrents: number[];
  peerConnections: number[];
  ratio: number[];
  networkActivity: number[];
}

function Activity() {
  const { t } = useTranslation();

  const { data: torrents } = useTorrents();
  const { data: stats } = useSeedingStats();
  const { data: serverHistory } = useSpeedHistory();

  const [history, setHistory] = useState<HistoryState>({
    uploadSpeed: [],
    downloadSpeed: [],
    activeTorrents: [],
    peerConnections: [],
    ratio: [],
    networkActivity: [],
  });

  const seededRef = useRef(false);

  // Compute instantaneous real-time metrics combining server stats and live SignalR telemetry
  const liveStats = useAggregatedTorrentMetrics(torrents, stats);

  const liveStatsRef = useRef(liveStats);
  liveStatsRef.current = liveStats;

  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;

    const recent = serverHistory.slice(-MAX_POINTS);
    const up = recent.map((s) => sanitizeNumber(s.uploadSpeed));
    const down = recent.map((s) => sanitizeNumber(s.downloadSpeed));
    const act = recent.map((s) => sanitizeNumber(s.activeTorrents));
    const peerCounts = recent.map((s) => sanitizeNumber(s.totalPeers));
    const rat = recent.map((s) => sanitizeNumber(s.averageRatio));
    const net = recent.map(
      (s) => sanitizeNumber(s.uploadSpeed) + sanitizeNumber(s.downloadSpeed),
    );

    setHistory({
      uploadSpeed: up,
      downloadSpeed: down,
      activeTorrents: act,
      peerConnections: peerCounts,
      ratio: rat,
      networkActivity: net,
    });
  }, [serverHistory]);

  // Push new real-time data point every 1.5s interval to keep live charts streaming
  useEffect(() => {
    const push = (arr: number[], val: number) => {
      const next = [...arr, sanitizeNumber(val)];
      if (next.length > MAX_POINTS) {
        next.splice(0, next.length - MAX_POINTS);
      }
      return next;
    };

    const interval = setInterval(() => {
      const cur = liveStatsRef.current;
      setHistory((curr) => ({
        uploadSpeed: push(curr.uploadSpeed, cur.uploadSpeed),
        downloadSpeed: push(curr.downloadSpeed, cur.downloadSpeed),
        activeTorrents: push(curr.activeTorrents, cur.activeTorrents),
        peerConnections: push(curr.peerConnections, cur.peerConnections),
        ratio: push(curr.ratio, cur.ratio),
        networkActivity: push(curr.networkActivity, cur.networkActivity),
      }));
    }, 1500);

    return () => clearInterval(interval);
  }, []);

  const currentUpload = liveStats.uploadSpeed;
  const currentDownload = liveStats.downloadSpeed;
  const currentActive = liveStats.activeTorrents;
  const currentPeers = liveStats.peerConnections;
  const currentRatio = liveStats.ratio;
  const currentNetwork = liveStats.networkActivity;

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>⚡</span> {t("activity.title")}
            <span
              className="badge badge-success"
              style={{
                fontSize: "0.75rem",
                borderRadius: "4px",
                marginLeft: "0.25rem",
              }}
            >
              {t("common.live")}
            </span>
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {t("activity.metricsTitle", "Real-time Transfer Metrics")}
          </p>
        </div>
        <Link
          to="/system/resources"
          className="btn btn-small"
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: "0.35rem",
            backgroundColor: "rgba(255, 209, 102, 0.15)",
            color: "var(--accent, #ffd166)",
            border: "1px solid rgba(255, 209, 102, 0.3)",
          }}
        >
          <span>📊</span> {t("activity.liveTelemetry")}
        </Link>
      </div>

      <div className="monitoring-grid">
        <LineChart
          title={t("activity.uploadSpeed")}
          value={formatSpeed(currentUpload)}
          data={history.uploadSpeed}
          color="#c8a84e"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title={t("activity.downloadSpeed")}
          value={formatSpeed(currentDownload)}
          data={history.downloadSpeed}
          color="#b5443a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title={t("activity.activeTorrents")}
          value={String(currentActive)}
          data={history.activeTorrents}
          color="#27ae60"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title={t("activity.peerConnections")}
          value={String(currentPeers)}
          data={history.peerConnections}
          color="#d4843a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title={t("activity.uploadDownloadRatio")}
          value={formatRatio(currentRatio)}
          data={history.ratio}
          color="#3498db"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title={t("activity.networkActivity")}
          value={formatSpeed(currentNetwork)}
          data={history.networkActivity}
          color="#9b59b6"
          maxPoints={MAX_POINTS}
        />
      </div>
    </div>
  );
}

export default Activity;
