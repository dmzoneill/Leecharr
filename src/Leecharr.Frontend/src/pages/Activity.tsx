import { useTranslation } from "../i18n";
import { useState, useRef, useEffect, useMemo } from "react";
import { Link } from "react-router";
import { useTorrents, useSeedingStats, useSpeedHistory } from "../api/hooks";
import { useTorrentStore } from "../stores/useTorrentStore";
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
  const telemetry = useTorrentStore((state) => state.telemetry);

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
  const liveStats = useMemo(() => {
    let dl = 0;
    let ul = 0;
    let active = 0;
    let peers = 0;
    let ratioSum = 0;
    const list = torrents ?? [];

    for (const t of list) {
      const tel = telemetry[t.id];
      const effectiveDl = tel?.downloadSpeed ?? t.downloadSpeed ?? 0;
      const effectiveUl = tel?.uploadSpeed ?? t.uploadSpeed ?? 0;
      const effectiveStatus = (tel?.status ?? t.status ?? "").toLowerCase();
      const effectiveRatio = tel?.ratio ?? t.ratio ?? 0;
      const effectiveSeeders = tel?.seeders ?? t.seeders ?? 0;
      const effectiveLeechers = tel?.leechers ?? t.leechers ?? 0;

      dl += effectiveDl;
      ul += effectiveUl;
      ratioSum += effectiveRatio;
      peers += effectiveSeeders + effectiveLeechers;

      if (
        effectiveStatus === "downloading" ||
        effectiveStatus === "seeding" ||
        effectiveStatus === "checking" ||
        effectiveStatus === "allocating" ||
        effectiveStatus === "metadata" ||
        effectiveStatus === "active"
      ) {
        active++;
      }
    }

    const calculatedAvgRatio =
      list.length > 0
        ? ratioSum / list.length
        : sanitizeNumber(stats?.averageRatio ?? stats?.globalRatio ?? 0);

    const resolvedUl = ul > 0 || !stats?.uploadSpeed ? ul : stats.uploadSpeed;
    const resolvedDl = dl > 0 || !stats?.downloadSpeed ? dl : stats.downloadSpeed;
    const resolvedActive =
      active > 0 || stats?.activeTorrents === undefined
        ? active
        : stats.activeTorrents;

    return {
      uploadSpeed: resolvedUl,
      downloadSpeed: resolvedDl,
      activeTorrents: resolvedActive,
      peerConnections: peers,
      ratio: calculatedAvgRatio,
      networkActivity: resolvedUl + resolvedDl,
    };
  }, [torrents, telemetry, stats]);

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
    <div className="content-area">
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {t("activity.title")}
            </h1>
            <span
              className="badge badge-success"
              style={{ fontSize: "0.75rem", borderRadius: "4px" }}
            >
              {t("common.live")}
            </span>
          </div>
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
