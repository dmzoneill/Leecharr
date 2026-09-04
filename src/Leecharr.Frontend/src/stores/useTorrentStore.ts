import { create } from "zustand";
import { Torrent } from "../api/types";

export interface TorrentTelemetry {
  uploadSpeed?: number;
  downloadSpeed?: number;
  progress?: number;
  uploaded?: number;
  downloaded?: number;
  ratio?: number;
  eta?: string | number;
  status?: string;
  seeders?: number;
  leechers?: number;
}

export interface TorrentStoreState {
  // Ephemeral Telemetry per torrent ID (from high-frequency speedPulse SignalR events)
  telemetry: Record<number, TorrentTelemetry>;
  updateTelemetry: (updates: Array<{ id: number; [key: string]: any }>) => void;
  clearTelemetry: () => void;

  // Active Selection State
  selectedTorrentId: number | null;
  selectedIds: Set<number>;
  setSelectedTorrentId: (id: number | null) => void;
  setSelectedIds: (ids: Set<number> | number[]) => void;
  toggleSelectedId: (id: number) => void;
  selectAllIds: (ids: number[]) => void;
  clearSelection: () => void;
}

export const useTorrentStore = create<TorrentStoreState>((set) => ({
  telemetry: {},
  updateTelemetry: (updates) =>
    set((state) => {
      let changed = false;
      const nextTelemetry = { ...state.telemetry };
      for (const u of updates) {
        if (u && typeof u.id === "number") {
          changed = true;
          nextTelemetry[u.id] = {
            ...(nextTelemetry[u.id] || {}),
            uploadSpeed: u.uploadSpeed ?? u.upSpeed ?? nextTelemetry[u.id]?.uploadSpeed,
            downloadSpeed: u.downloadSpeed ?? u.downSpeed ?? nextTelemetry[u.id]?.downloadSpeed,
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
  clearSelection: () => set({ selectedIds: new Set(), selectedTorrentId: null }),
}));

export function applyTelemetry(torrent: Torrent, telemetry?: TorrentTelemetry): Torrent {
  if (!telemetry) return torrent;
  return {
    ...torrent,
    uploadSpeed: telemetry.uploadSpeed ?? torrent.uploadSpeed,
    downloadSpeed: telemetry.downloadSpeed ?? torrent.downloadSpeed,
    progress: telemetry.progress ?? torrent.progress,
    uploaded: telemetry.uploaded ?? torrent.uploaded,
    downloaded: telemetry.downloaded ?? torrent.downloaded,
    ratio: telemetry.ratio ?? torrent.ratio,
    eta: telemetry.eta ?? torrent.eta,
    status: telemetry.status ?? torrent.status,
    seeders: telemetry.seeders ?? torrent.seeders,
    leechers: telemetry.leechers ?? torrent.leechers,
  };
}
