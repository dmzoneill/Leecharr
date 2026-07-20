import { useState, useRef, useEffect } from "react";
import { useTorrents, useSeedingStats, useSpeedHistory } from "../api/hooks";
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

  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);

  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;

    const recent = serverHistory.slice(-MAX_POINTS);
    const up = recent.map((s) => sanitizeNumber(s.uploadSpeed));
    const down = recent.map((s) => sanitizeNumber(s.downloadSpeed));
    const act = recent.map((s) => sanitizeNumber(s.activeTorrents));
    const peers = recent.map((s) => sanitizeNumber(s.totalPeers));
    const rat = recent.map((s) => sanitizeNumber(s.averageRatio));
    const net = recent.map(
      (s) => sanitizeNumber(s.uploadSpeed) + sanitizeNumber(s.downloadSpeed),
    );

    setHistory({
      uploadSpeed: up,
      downloadSpeed: down,
      activeTorrents: act,
      peerConnections: peers,
      ratio: rat,
      networkActivity: net,
    });

    if (serverHistory.length > 0) {
      const last = serverHistory[serverHistory.length - 1];
      prevRef.current = {
        totalUploaded: sanitizeNumber(last.totalUploaded),
        totalDownloaded: sanitizeNumber(last.totalDownloaded),
        timestamp: new Date(last.timestamp).getTime(),
      };
    }
  }, [serverHistory]);

  useEffect(() => {
    if (!stats) return;

    const now = Date.now();
    const prev = prevRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 0.8) {
        const statsUp = sanitizeNumber(stats.totalUploaded);
        const statsDown = sanitizeNumber(stats.totalDownloaded);

        const upSpeed =
          statsUp >= prev.totalUploaded
            ? Math.max(0, (statsUp - prev.totalUploaded) / timeDelta)
            : 0;
        const downSpeed =
          statsDown >= prev.totalDownloaded
            ? Math.max(0, (statsDown - prev.totalDownloaded) / timeDelta)
            : 0;

        const totalPeers = (torrents ?? []).reduce(
          (sum, t) => sum + (t.seeders || 0) + (t.leechers || 0),
          0,
        );

        const push = (arr: number[], val: number) => {
          const next = [...arr, sanitizeNumber(val)];
          if (next.length > MAX_POINTS) {
            next.splice(0, next.length - MAX_POINTS);
          }
          return next;
        };

        setHistory((curr) => ({
          uploadSpeed: push(curr.uploadSpeed, upSpeed),
          downloadSpeed: push(curr.downloadSpeed, downSpeed),
          activeTorrents: push(
            curr.activeTorrents,
            sanitizeNumber(stats.activeTorrents),
          ),
          peerConnections: push(
            curr.peerConnections,
            sanitizeNumber(totalPeers),
          ),
          ratio: push(curr.ratio, sanitizeNumber(stats.averageRatio)),
          networkActivity: push(curr.networkActivity, upSpeed + downSpeed),
        }));

        prevRef.current = {
          totalUploaded: statsUp,
          totalDownloaded: statsDown,
          timestamp: now,
        };
      }
    } else {
      prevRef.current = {
        totalUploaded: sanitizeNumber(stats.totalUploaded),
        totalDownloaded: sanitizeNumber(stats.totalDownloaded),
        timestamp: now,
      };
    }
  }, [stats, torrents]);

  const currentUpload =
    history.uploadSpeed.length > 0
      ? history.uploadSpeed[history.uploadSpeed.length - 1]
      : 0;
  const currentDownload =
    history.downloadSpeed.length > 0
      ? history.downloadSpeed[history.downloadSpeed.length - 1]
      : 0;
  const currentActive = stats?.activeTorrents ?? 0;
  const currentPeers = (torrents ?? []).reduce(
    (sum, t) => sum + (t.seeders || 0) + (t.leechers || 0),
    0,
  );
  const currentRatio = stats?.averageRatio ?? 0;
  const currentNetwork = currentUpload + currentDownload;

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
              Activity Metrics
            </h1>
            <span
              className="badge badge-success"
              style={{ fontSize: "0.75rem", borderRadius: "4px" }}
            >
              ● Live (1s)
            </span>
          </div>
        </div>
      </div>

      <div className="monitoring-grid">
        <LineChart
          title="Upload Speed"
          value={formatSpeed(currentUpload)}
          data={history.uploadSpeed}
          color="#c8a84e"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Download Speed"
          value={formatSpeed(currentDownload)}
          data={history.downloadSpeed}
          color="#b5443a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Active Torrents"
          value={String(currentActive)}
          data={history.activeTorrents}
          color="#27ae60"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Peer Connections"
          value={String(currentPeers)}
          data={history.peerConnections}
          color="#d4843a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Upload/Download Ratio"
          value={formatRatio(currentRatio)}
          data={history.ratio}
          color="#3498db"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Network Activity"
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
