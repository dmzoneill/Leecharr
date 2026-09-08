import { useMemo } from "react";
import { create } from "zustand";
import { Torrent } from "../api/types";
import {
  decodeBase64Bitfield,
  setPieceBitInPlace,
  setPieceBitsInPlace,
  setPieceRangesInPlace,
} from "../utils/pieceMapUtils";

export interface TorrentTelemetry {
  uploadSpeed?: number;
  downloadSpeed?: number;
  progress?: number;
  uploaded?: number;
  downloaded?: number;
  ratio?: number;
  eta?: number;
  status?: string;
  seeders?: number;
  leechers?: number;
  lastUpdated?: number;
}

export interface PieceMapData {
  bitfield: Uint8Array;
  version: number;
  lastUpdated: number;
}

export interface TorrentStoreState {
  // Ephemeral Telemetry per torrent ID (from high-frequency speedPulse SignalR events)
  telemetry: Record<number, TorrentTelemetry>;
  updateTelemetry: (updates: Array<{ id: number; [key: string]: any }>) => void;
  clearTelemetry: () => void;
  purgeStaleTelemetry: (maxAgeMs?: number) => void;

  // Real-time piece map updates per torrent ID (from pieceMapUpdated SignalR events)
  pieceMaps: Record<number, PieceMapData>;
  updatePieceMap: (torrentId: number, data: any) => void;
  clearPieceMaps: () => void;

  // Active Selection State
  selectedTorrentId: number | null;
  selectedIds: Set<number>;
  setSelectedTorrentId: (id: number | null) => void;
  setSelectedIds: (ids: Set<number> | number[]) => void;
  toggleSelectedId: (id: number) => void;
  selectAllIds: (ids: number[]) => void;
  clearSelection: () => void;
  removeTorrent: (id: number) => void;
}

export const useTorrentStore = create<TorrentStoreState>((set) => ({
  telemetry: {},
  pieceMaps: {},
  updatePieceMap: (torrentId, data) =>
    set((state) => {
      const prevData = state.pieceMaps[torrentId];
      let bitfield = prevData?.bitfield;
      const prevVersion = prevData?.version ?? 0;

      // Determine required max piece index from data
      let maxIdx = -1;
      if (Array.isArray(data?.ranges)) {
        for (let i = 0; i < data.ranges.length; i++) {
          const r = data.ranges[i];
          if (Array.isArray(r) && r.length >= 2 && r[1] > maxIdx) {
            maxIdx = r[1];
          }
        }
      }
      if (Array.isArray(data?.pieceIndices)) {
        for (let i = 0; i < data.pieceIndices.length; i++) {
          const idx = data.pieceIndices[i];
          if (typeof idx === "number" && idx > maxIdx) {
            maxIdx = idx;
          }
        }
      }
      if (typeof data?.pieceIndex === "number" && data.pieceIndex > maxIdx) {
        maxIdx = data.pieceIndex;
      }

      const requiredBytes = maxIdx >= 0 ? (maxIdx >> 3) + 1 : 0;
      if (!bitfield || bitfield.length < requiredBytes) {
        const newCap = Math.max(
          requiredBytes,
          bitfield ? bitfield.length * 2 : 64,
        );
        const newBuf = new Uint8Array(newCap);
        if (bitfield) {
          newBuf.set(bitfield);
        }
        bitfield = newBuf;
      }

      if (typeof data?.bitfield === "string" && data.bitfield.length > 0) {
        const decoded = decodeBase64Bitfield(data.bitfield);
        if (decoded) {
          if (bitfield.length < decoded.length) {
            const newBuf = new Uint8Array(decoded.length);
            newBuf.set(bitfield);
            bitfield = newBuf;
          }
          for (let i = 0; i < decoded.length; i++) {
            bitfield[i] |= decoded[i];
          }
        }
      } else if (data?.bitfield instanceof Uint8Array) {
        if (bitfield.length < data.bitfield.length) {
          const newBuf = new Uint8Array(data.bitfield.length);
          newBuf.set(bitfield);
          bitfield = newBuf;
        }
        for (let i = 0; i < data.bitfield.length; i++) {
          bitfield[i] |= data.bitfield[i];
        }
      }

      if (Array.isArray(data?.ranges) && data.ranges.length > 0) {
        setPieceRangesInPlace(bitfield, data.ranges);
      }
      if (Array.isArray(data?.pieceIndices) && data.pieceIndices.length > 0) {
        setPieceBitsInPlace(bitfield, data.pieceIndices);
      }
      if (typeof data?.pieceIndex === "number" && data.pieceIndex >= 0) {
        setPieceBitInPlace(bitfield, data.pieceIndex);
      }

      return {
        pieceMaps: {
          ...state.pieceMaps,
          [torrentId]: {
            bitfield,
            version: prevVersion + 1,
            lastUpdated: Date.now(),
          },
        },
      };
    }),
  updateTelemetry: (updates) =>
    set((state) => {
      let changed = false;
      const nextTelemetry = { ...state.telemetry };
      const now = Date.now();
      for (const u of updates) {
        if (u && typeof u.id === "number") {
          changed = true;
          nextTelemetry[u.id] = {
            ...(nextTelemetry[u.id] || {}),
            uploadSpeed:
              u.uploadSpeed ?? u.upSpeed ?? nextTelemetry[u.id]?.uploadSpeed,
            downloadSpeed:
              u.downloadSpeed ??
              u.downSpeed ??
              nextTelemetry[u.id]?.downloadSpeed,
            progress: u.progress ?? nextTelemetry[u.id]?.progress,
            uploaded: u.uploaded ?? nextTelemetry[u.id]?.uploaded,
            downloaded: u.downloaded ?? nextTelemetry[u.id]?.downloaded,
            ratio: u.ratio ?? nextTelemetry[u.id]?.ratio,
            eta: u.eta ?? nextTelemetry[u.id]?.eta,
            status: u.status ?? nextTelemetry[u.id]?.status,
            seeders: u.seeders ?? nextTelemetry[u.id]?.seeders,
            leechers: u.leechers ?? nextTelemetry[u.id]?.leechers,
            lastUpdated: now,
          };
        }
      }
      return changed ? { telemetry: nextTelemetry } : state;
    }),
  clearTelemetry: () => set({ telemetry: {} }),
  purgeStaleTelemetry: (maxAgeMs = 5000) =>
    set((state) => {
      const now = Date.now();
      const nextTelemetry: Record<number, TorrentTelemetry> = {};
      let changed = false;
      for (const [id, tel] of Object.entries(state.telemetry)) {
        if (tel.lastUpdated && now - tel.lastUpdated > maxAgeMs) {
          changed = true;
        } else {
          nextTelemetry[Number(id)] = tel;
        }
      }
      return changed ? { telemetry: nextTelemetry } : state;
    }),
  clearPieceMaps: () => set({ pieceMaps: {} }),

  selectedTorrentId: null,
  selectedIds: new Set<number>(),
  setSelectedTorrentId: (id) => set({ selectedTorrentId: id }),
  setSelectedIds: (ids) =>
    set({
      selectedIds: ids instanceof Set ? ids : new Set(ids),
    }),
  toggleSelectedId: (id) =>
    set((state) => {
      const next = new Set(state.selectedIds);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return { selectedIds: next };
    }),
  selectAllIds: (ids) => set({ selectedIds: new Set(ids) }),
  clearSelection: () =>
    set({ selectedIds: new Set(), selectedTorrentId: null }),
  removeTorrent: (id: number) =>
    set((state) => {
      const nextSelected = new Set(state.selectedIds);
      nextSelected.delete(id);
      const nextTelemetry = { ...state.telemetry };
      delete nextTelemetry[id];
      const nextPieceMaps = { ...state.pieceMaps };
      delete nextPieceMaps[id];
      return {
        selectedIds: nextSelected,
        selectedTorrentId:
          state.selectedTorrentId === id ? null : state.selectedTorrentId,
        telemetry: nextTelemetry,
        pieceMaps: nextPieceMaps,
      };
    }),
}));

export function applyTelemetry(
  torrent: Torrent,
  telemetry?: TorrentTelemetry,
): Torrent {
  const isStale = Boolean(
    telemetry?.lastUpdated && Date.now() - telemetry.lastUpdated > 10000,
  );

  const effectiveStatus = (
    !isStale && telemetry?.status ? telemetry.status : torrent.status
  )?.toLowerCase();

  const isInactive =
    effectiveStatus === "paused" ||
    effectiveStatus === "stopped" ||
    effectiveStatus === "error" ||
    effectiveStatus === "queued";

  if (!telemetry || isStale) {
    if (isInactive) {
      return {
        ...torrent,
        downloadSpeed: 0,
        uploadSpeed: 0,
        eta: 0,
        seeders: 0,
        leechers: 0,
      };
    }
    return torrent;
  }

  return {
    ...torrent,
    uploadSpeed: isInactive
      ? 0
      : (telemetry.uploadSpeed ?? torrent.uploadSpeed),
    downloadSpeed: isInactive
      ? 0
      : (telemetry.downloadSpeed ?? torrent.downloadSpeed),
    progress: telemetry.progress ?? torrent.progress,
    uploaded: telemetry.uploaded ?? torrent.uploaded,
    downloaded: telemetry.downloaded ?? torrent.downloaded,
    ratio: telemetry.ratio ?? torrent.ratio,
    eta: isInactive
      ? 0
      : typeof telemetry.eta === "number"
        ? telemetry.eta
        : telemetry.eta
          ? Number(telemetry.eta)
          : torrent.eta,
    status: telemetry.status ?? torrent.status,
    seeders: isInactive ? 0 : (telemetry.seeders ?? torrent.seeders),
    leechers: isInactive ? 0 : (telemetry.leechers ?? torrent.leechers),
  };
}

export interface AggregatedTorrentMetrics {
  totalDlSpeed: number;
  totalUlSpeed: number;
  downloadSpeed: number;
  uploadSpeed: number;
  activeCount: number;
  activeTorrents: number;
  downloadingCount: number;
  seedingCount: number;
  pausedCount: number;
  totalSeeders: number;
  totalLeechers: number;
  peerConnections: number;
  totalUploaded: number;
  totalDownloaded: number;
  averageRatio: number;
  avgRatio: number;
  ratio: number;
  networkActivity: number;
}

export interface TorrentMetricsStats {
  totalUploaded?: number;
  totalDownloaded?: number;
  averageRatio?: number;
  globalRatio?: number;
  activeTorrents?: number;
  downloadSpeed?: number | string;
  uploadSpeed?: number | string;
}

export function useAggregatedTorrentMetrics(
  torrents?: Torrent[],
  stats?: TorrentMetricsStats | null,
): AggregatedTorrentMetrics {
  const telemetry = useTorrentStore((state) => state.telemetry);

  return useMemo(() => {
    let dl = 0;
    let ul = 0;
    let downloading = 0;
    let seeding = 0;
    let paused = 0;
    let active = 0;
    let seeders = 0;
    let leechers = 0;
    let uploadedSum = 0;
    let downloadedSum = 0;
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
      const effectiveUploaded = tel?.uploaded ?? t.uploaded ?? 0;
      const effectiveDownloaded = tel?.downloaded ?? t.downloaded ?? 0;

      dl += effectiveDl;
      ul += effectiveUl;
      seeders += effectiveSeeders;
      leechers += effectiveLeechers;
      uploadedSum += effectiveUploaded;
      downloadedSum += effectiveDownloaded;
      ratioSum += effectiveRatio;

      if (effectiveStatus === "downloading") {
        downloading++;
        active++;
      } else if (
        effectiveStatus === "seeding" ||
        effectiveStatus === "completed"
      ) {
        seeding++;
        if (effectiveStatus === "seeding") {
          active++;
        }
      } else if (
        effectiveStatus === "paused" ||
        effectiveStatus === "stopped" ||
        effectiveStatus === "idle"
      ) {
        paused++;
      } else if (
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
        : (stats?.averageRatio ?? stats?.globalRatio ?? 0);

    const statUl =
      stats?.uploadSpeed !== undefined
        ? Number(stats.uploadSpeed) || 0
        : undefined;
    const statDl =
      stats?.downloadSpeed !== undefined
        ? Number(stats.downloadSpeed) || 0
        : undefined;

    const resolvedUl = ul > 0 || statUl === undefined ? ul : statUl;
    const resolvedDl = dl > 0 || statDl === undefined ? dl : statDl;
    const resolvedActive =
      list.length > 0 ? active : (stats?.activeTorrents ?? 0);

    const totalUploaded = Math.max(stats?.totalUploaded ?? 0, uploadedSum);
    const totalDownloaded = Math.max(stats?.totalDownloaded ?? 0, downloadedSum);
    const averageRatio = stats?.averageRatio ?? calculatedAvgRatio;

    return {
      totalDlSpeed: dl,
      totalUlSpeed: ul,
      downloadSpeed: resolvedDl,
      uploadSpeed: resolvedUl,
      activeCount: resolvedActive,
      activeTorrents: resolvedActive,
      downloadingCount: downloading,
      seedingCount: seeding,
      pausedCount: paused,
      totalSeeders: seeders,
      totalLeechers: leechers,
      peerConnections: seeders + leechers,
      totalUploaded,
      totalDownloaded,
      averageRatio,
      avgRatio: calculatedAvgRatio,
      ratio: calculatedAvgRatio,
      networkActivity: resolvedUl + resolvedDl,
    };
  }, [torrents, telemetry, stats]);
}
