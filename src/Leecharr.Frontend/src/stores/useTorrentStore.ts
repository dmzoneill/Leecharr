import { create } from "zustand";
import { Torrent } from "../api/types";

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
}

export interface PieceMapData {
  verifiedIndices: Set<number>;
  lastUpdated: number;
}

export interface TorrentStoreState {
  // Ephemeral Telemetry per torrent ID (from high-frequency speedPulse SignalR events)
  telemetry: Record<number, TorrentTelemetry>;
  updateTelemetry: (updates: Array<{ id: number; [key: string]: any }>) => void;
  clearTelemetry: () => void;

  // Real-time piece map updates per torrent ID (from pieceMapUpdated SignalR events)
  pieceMaps: Record<number, PieceMapData>;
  updatePieceMap: (torrentId: number, data: any) => void;

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
      const existing = state.pieceMaps[torrentId]?.verifiedIndices;
      const verified = existing ? new Set(existing) : new Set<number>();
      if (Array.isArray(data?.pieceIndices)) {
        for (const idx of data.pieceIndices) {
          verified.add(idx);
        }
      } else if (typeof data?.pieceIndex === "number") {
        verified.add(data.pieceIndex);
      }
      if (typeof data?.bitfield === "string" && data.bitfield.length > 0) {
        try {
          const binary = atob(data.bitfield);
          for (let i = 0; i < binary.length; i++) {
            const byte = binary.charCodeAt(i);
            for (let bit = 0; bit < 8; bit++) {
              if ((byte & (1 << (7 - bit))) !== 0) {
                verified.add(i * 8 + bit);
              }
            }
          }
        } catch {
          // ignore invalid bitfield
        }
      }
      return {
        pieceMaps: {
          ...state.pieceMaps,
          [torrentId]: {
            verifiedIndices: verified,
            lastUpdated: Date.now(),
          },
        },
      };
    }),
  updateTelemetry: (updates) =>
    set((state) => {
      let changed = false;
      const nextTelemetry = { ...state.telemetry };
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
          };
        }
      }
      return changed ? { telemetry: nextTelemetry } : state;
    }),
  clearTelemetry: () => set({ telemetry: {} }),

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
  if (!telemetry) return torrent;
  return {
    ...torrent,
    uploadSpeed: telemetry.uploadSpeed ?? torrent.uploadSpeed,
    downloadSpeed: telemetry.downloadSpeed ?? torrent.downloadSpeed,
    progress: telemetry.progress ?? torrent.progress,
    uploaded: telemetry.uploaded ?? torrent.uploaded,
    downloaded: telemetry.downloaded ?? torrent.downloaded,
    ratio: telemetry.ratio ?? torrent.ratio,
    eta:
      typeof telemetry.eta === "number"
        ? telemetry.eta
        : telemetry.eta
          ? Number(telemetry.eta)
          : torrent.eta,
    status: telemetry.status ?? torrent.status,
    seeders: telemetry.seeders ?? torrent.seeders,
    leechers: telemetry.leechers ?? torrent.leechers,
  };
}
