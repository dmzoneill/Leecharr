import { create } from "zustand";
import { Torrent } from "../api/types";
import {
  decodeBase64Bitfield,
  setPieceBit,
  setPieceBits,
  mergeBitfields,
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
      const existing = state.pieceMaps[torrentId]?.bitfield;
      let nextBitfield: Uint8Array = existing
        ? new Uint8Array(existing)
        : new Uint8Array(0);

      if (typeof data?.bitfield === "string" && data.bitfield.length > 0) {
        const decoded = decodeBase64Bitfield(data.bitfield);
        if (decoded) {
          nextBitfield = mergeBitfields(nextBitfield, decoded);
        }
      } else if (data?.bitfield instanceof Uint8Array) {
        nextBitfield = mergeBitfields(nextBitfield, data.bitfield);
      }

      if (Array.isArray(data?.pieceIndices) && data.pieceIndices.length > 0) {
        nextBitfield = setPieceBits(nextBitfield, data.pieceIndices);
      } else if (typeof data?.pieceIndex === "number" && data.pieceIndex >= 0) {
        nextBitfield = setPieceBit(nextBitfield, data.pieceIndex);
      }

      return {
        pieceMaps: {
          ...state.pieceMaps,
          [torrentId]: {
            bitfield: nextBitfield,
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
