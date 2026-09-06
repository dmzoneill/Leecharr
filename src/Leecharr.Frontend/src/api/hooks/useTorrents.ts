import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import { useRefetchInterval } from "./useSystem";
import type {
  Torrent,
  TorrentFileInfo,
  Category,
  SeedingStats,
  SpeedSnapshot,
  TorrentSpeedSnapshot,
  Peer,
  TrackerEntry,
  PeerGraphData,
  SpeedScheduleEntry,
  SpeedLimits,
  Tag,
  PeerConnectionLogEntry,
  DownloadHistoryEntry,
  ReleaseInfo,
  DownloadReleaseRequest,
  TrackerBoostTracker,
  TrackerBoostStatusSummary,
  TrackerBoostSettings,
  TrackerCrossMatrixResult,
  SwarmBoostResult,
  TorrentTrackerInspectionResult,
  TrackerMetric,
  TrackerMetricsSummary,
  TrackerMetricSnapshot,
  TorrentEngine,
  ActiveEngineStatus,
  SwitchEngineRequest,
  SwitchEngineResult,
  EngineProbeResult,
  SubsystemOverview,
  SwitchSubsystemRequest,
  SwitchSubsystemResult,
  SubsystemProbeResult,
  TorrentEngineMetrics,
  TorrentResourceMetrics,
} from "../types";

export type AddTorrentInput = {
  files?: File[];
  magnetLink?: string;
  category?: string;
  isPaused?: boolean;
  paused?: boolean;
  savePath?: string;
};

export interface TorrentUploadFailure {
  fileName: string;
  reason: string;
}

export interface AddTorrentResult {
  added: Torrent[];
  failed: TorrentUploadFailure[];
}

export function useTorrents() {
  const interval = useRefetchInterval();
  return useQuery<Torrent[]>({
    queryKey: ["torrents"],
    queryFn: () => apiClient.get("/torrent"),
    refetchInterval: interval,
  });
}

export function useTorrent(id: number) {
  const interval = useRefetchInterval();
  return useQuery<Torrent>({
    queryKey: ["torrents", id],
    queryFn: () => apiClient.get(`/torrent/${id}`),
    enabled: id > 0,
    refetchInterval: interval,
  });
}

export function useTorrentFiles(torrentId: number) {
  return useQuery<TorrentFileInfo[]>({
    queryKey: ["torrents", torrentId, "files"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/files`),
    enabled: torrentId > 0,
  });
}

export function useSetFilePriority() {
  const queryClient = useQueryClient();
  return useMutation<
    void,
    Error,
    { torrentId: number; fileId: number; priority: number }
  >({
    mutationFn: ({ torrentId, fileId, priority }) =>
      apiClient.put(`/torrent/${torrentId}/files/${fileId}/priority`, {
        priority,
      }),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", vars.torrentId, "files"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useSetFilesPriority() {
  const queryClient = useQueryClient();
  return useMutation<
    void,
    Error,
    { torrentId: number; files: Array<{ fileId: number; priority: number }> }
  >({
    mutationFn: async ({ torrentId, files }) => {
      await Promise.all(
        files.map((f) =>
          apiClient.put(`/torrent/${torrentId}/files/${f.fileId}/priority`, {
            priority: f.priority,
          }),
        ),
      );
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", vars.torrentId, "files"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useTorrentTrackers(torrentId: number) {
  const interval = useRefetchInterval();
  return useQuery<TrackerEntry[]>({
    queryKey: ["torrents", torrentId, "trackers"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/trackers`),
    enabled: torrentId > 0,
    refetchInterval: interval,
  });
}

export function useDeleteTorrentTracker() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, { torrentId: number; trackerId: number }>({
    mutationFn: ({ torrentId, trackerId }) =>
      apiClient.delete(`/torrent/${torrentId}/trackers/${trackerId}`),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", vars.torrentId, "trackers"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useCategories() {
  return useQuery<Category[]>({
    queryKey: ["categories"],
    queryFn: () => apiClient.get("/categories"),
  });
}

export function useCreateCategory() {
  const queryClient = useQueryClient();
  return useMutation<Category, Error, Partial<Category>>({
    mutationFn: (data: Partial<Category>) =>
      apiClient.post("/categories", data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function useUpdateCategory() {
  const queryClient = useQueryClient();
  return useMutation<Category, Error, { id: number; data: Partial<Category> }>({
    mutationFn: ({ id, data }) => apiClient.put(`/categories/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function useDeleteCategory() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/categories/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function useAddTorrentTracker() {
  const queryClient = useQueryClient();
  return useMutation<
    TrackerEntry,
    Error,
    { torrentId: number; url: string; tier?: number }
  >({
    mutationFn: ({ torrentId, url, tier }) =>
      apiClient.post(`/torrent/${torrentId}/trackers`, {
        url,
        tier: tier ?? 1,
      }),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", vars.torrentId, "trackers"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useAnnounceTorrentTracker() {
  const queryClient = useQueryClient();
  return useMutation<
    { success: boolean; message: string },
    Error,
    { torrentId: number; trackerId: number }
  >({
    mutationFn: ({ torrentId, trackerId }) =>
      apiClient.post(
        `/torrent/${torrentId}/trackers/${trackerId}/announce`,
        {},
      ),
    onSuccess: (_, { torrentId }) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", torrentId, "trackers"],
      });
      queryClient.invalidateQueries({
        queryKey: ["torrents", torrentId, "logs"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents", torrentId] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useAddTorrent() {
  const queryClient = useQueryClient();
  return useMutation<AddTorrentResult, Error, AddTorrentInput>({
    mutationFn: async (input) => {
      const isPaused = input.isPaused ?? input.paused ?? false;
      if (input.files && input.files.length > 0) {
        const formData = new FormData();
        input.files.forEach((file) => formData.append("file", file));
        if (input.category) formData.append("category", input.category);
        if (isPaused) formData.append("paused", "true");
        if (input.savePath) formData.append("savePath", input.savePath);
        return apiClient.postForm<AddTorrentResult>(
          "/torrents/upload",
          formData,
        );
      }
      return apiClient.post("/torrents", {
        magnetLink: input.magnetLink,
        category: input.category,
        paused: isPaused,
        savePath: input.savePath,
      });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["torrents"] }),
  });
}

export function useUpdateTorrent() {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, Torrent>({
    mutationFn: (torrent) => apiClient.put(`/torrent/${torrent.id}`, torrent),
    onSuccess: (_, torrent) => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["torrents", torrent.id] });
    },
  });
}

export function useDeleteTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      deleteFiles = false,
    }: {
      id: number;
      deleteFiles?: boolean;
    }) =>
      apiClient.delete(
        `/torrent/${id}${deleteFiles ? "?deleteFiles=true" : ""}`,
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["torrents"] }),
  });
}

export function useAnnounceTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/torrent/${id}/announce`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useRecheckTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/torrent/${id}/recheck`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useMoveTorrentQueue() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, position }: { id: number; position: string }) =>
      apiClient.put(`/torrent/${id}/queue`, { position }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useStartSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/seeding/start/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
    },
  });
}

export function useStopSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/seeding/stop/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
    },
  });
}

export function useStartAllSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post("/seeding/start-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
    },
  });
}

export function useStopAllSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post("/seeding/stop-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
    },
  });
}

export function useSeedingStats() {
  const interval = useRefetchInterval();
  return useQuery<SeedingStats>({
    queryKey: ["seeding", "stats"],
    queryFn: () => apiClient.get("/seeding/stats"),
    refetchInterval: interval,
  });
}

export function useSpeedHistory() {
  return useQuery<SpeedSnapshot[]>({
    queryKey: ["seeding", "history"],
    queryFn: () => apiClient.get("/seeding/history"),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

export function useTorrentSpeedHistory(torrentId: number) {
  return useQuery<TorrentSpeedSnapshot[]>({
    queryKey: ["seeding", "history", torrentId],
    queryFn: () => apiClient.get(`/seeding/history/${torrentId}`),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
    enabled: torrentId > 0,
  });
}

export function usePeers(torrentId: number) {
  const interval = useRefetchInterval();
  return useQuery<Peer[]>({
    queryKey: ["torrents", torrentId, "peers"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/peers`),
    enabled: torrentId > 0,
    refetchInterval: interval,
  });
}

export function usePeerGraph(start?: string, end?: string) {
  const interval = useRefetchInterval();
  const params = new URLSearchParams();
  if (start) params.set("start", start);
  if (end) params.set("end", end);
  const query = params.toString();
  return useQuery<PeerGraphData>({
    queryKey: ["peerlog", "graph", start, end],
    queryFn: () => apiClient.get(`/peerlog/graph${query ? `?${query}` : ""}`),
    refetchInterval: interval,
  });
}

export function useSpeedSchedules() {
  return useQuery<SpeedScheduleEntry[]>({
    queryKey: ["speedschedule"],
    queryFn: () => apiClient.get("/speedschedule"),
  });
}

export function useActiveSpeedLimits() {
  const interval = useRefetchInterval();
  return useQuery<SpeedLimits>({
    queryKey: ["speedschedule", "active"],
    queryFn: () => apiClient.get("/speedschedule/active"),
    refetchInterval: interval,
  });
}

export function useCreateSpeedSchedule() {
  const queryClient = useQueryClient();
  return useMutation<SpeedScheduleEntry, Error, Partial<SpeedScheduleEntry>>({
    mutationFn: (schedule) => apiClient.post("/speedschedule", schedule),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["speedschedule"] }),
  });
}

export function useUpdateSpeedSchedule() {
  const queryClient = useQueryClient();
  return useMutation<SpeedScheduleEntry, Error, SpeedScheduleEntry>({
    mutationFn: (schedule) =>
      apiClient.put(`/speedschedule/${schedule.id}`, schedule),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["speedschedule"] }),
  });
}

export function useDeleteSpeedSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/speedschedule/${id}`),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["speedschedule"] }),
  });
}

export function useTags() {
  return useQuery<Tag[]>({
    queryKey: ["tags"],
    queryFn: () => apiClient.get("/tag"),
  });
}

export function useCreateTag() {
  const queryClient = useQueryClient();
  return useMutation<Tag, Error, Partial<Tag>>({
    mutationFn: (tag) => apiClient.post("/tag", tag),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["tags"] }),
  });
}

export function useUpdateTag() {
  const queryClient = useQueryClient();
  return useMutation<Tag, Error, Tag>({
    mutationFn: (tag) => apiClient.put(`/tag/${tag.id}`, tag),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["tags"] }),
  });
}

export function useDeleteTag() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/tag/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["tags"] }),
  });
}

export function useActivePeers() {
  const interval = useRefetchInterval();
  return useQuery<PeerConnectionLogEntry[]>({
    queryKey: ["peerlog", "active"],
    queryFn: () => apiClient.get("/peerlog/active"),
    refetchInterval: interval,
  });
}

export function useDownloadHistory(params?: {
  query?: string;
  status?: string;
  limit?: number;
}) {
  const interval = useRefetchInterval();
  const searchParams = new URLSearchParams();
  if (params?.query) searchParams.set("query", params.query);
  if (params?.status) searchParams.set("status", params.status);
  if (params?.limit) searchParams.set("limit", String(params.limit));
  const queryString = searchParams.toString();

  return useQuery<DownloadHistoryEntry[]>({
    queryKey: ["downloadhistory", params?.query, params?.status, params?.limit],
    queryFn: () =>
      apiClient.get(`/downloadhistory${queryString ? `?${queryString}` : ""}`),
    refetchInterval: interval,
  });
}

export function useReAddHistoryTorrent() {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, number>({
    mutationFn: (id: number) => apiClient.post(`/downloadhistory/${id}/readd`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useDeleteHistoryTorrent() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/downloadhistory/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useClearDownloadHistory() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; clearedCount: number }, Error, void>({
    mutationFn: () => apiClient.delete("/downloadhistory"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useEnrichHistoryTorrent() {
  const queryClient = useQueryClient();
  return useMutation<DownloadHistoryEntry, Error, number>({
    mutationFn: (id: number) => apiClient.post(`/downloadhistory/${id}/enrich`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useEnrichAllHistory() {
  const queryClient = useQueryClient();
  return useMutation<{ message: string }, Error, void>({
    mutationFn: () => apiClient.post("/downloadhistory/enrich-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useReconcileDownloadHistory() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; processedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/downloadhistory/reconcile"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
      },
    },
  );
}

export function useIndexerSearch(
  params: { query: string; category?: string; indexerId?: number },
  enabled = true,
) {
  const searchParams = new URLSearchParams();
  searchParams.set("query", params.query);
  if (params.category) searchParams.set("category", params.category);
  if (params.indexerId) searchParams.set("indexerId", String(params.indexerId));
  const queryString = searchParams.toString();

  return useQuery<ReleaseInfo[]>({
    queryKey: [
      "indexers",
      "search",
      params.query,
      params.category,
      params.indexerId,
    ],
    queryFn: () => apiClient.get(`/indexers/search?${queryString}`),
    enabled: enabled && Boolean(params.query?.trim()),
    staleTime: 30_000,
  });
}

export function useDownloadIndexerRelease() {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, DownloadReleaseRequest>({
    mutationFn: (req) => apiClient.post("/indexers/download", req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useTrackerBoostStatus() {
  return useQuery<TrackerBoostStatusSummary>({
    queryKey: ["trackerboost", "status"],
    queryFn: () => apiClient.get("/trackerboost/status"),
    refetchInterval: 10_000,
  });
}

export function useTrackerBoostTrackers() {
  return useQuery<TrackerBoostTracker[]>({
    queryKey: ["trackerboost", "trackers"],
    queryFn: () => apiClient.get("/trackerboost/trackers"),
  });
}

export function useInspectTorrentTrackers(torrentId: number, enabled = true) {
  return useQuery<TorrentTrackerInspectionResult>({
    queryKey: ["trackerboost", "check", torrentId],
    queryFn: () => apiClient.get(`/trackerboost/check/${torrentId}`),
    enabled: enabled && torrentId > 0,
  });
}

export function useInspectHashTrackers(
  infoHash: string,
  name = "",
  enabled = true,
) {
  return useQuery<TorrentTrackerInspectionResult>({
    queryKey: ["trackerboost", "check-hash", infoHash],
    queryFn: () => {
      const search = name ? `?name=${encodeURIComponent(name)}` : "";
      return apiClient.get(`/trackerboost/check-hash/${infoHash}${search}`);
    },
    enabled: enabled && Boolean(infoHash),
  });
}

export function useTrackerBoostSettings() {
  return useQuery<TrackerBoostSettings>({
    queryKey: ["trackerboost", "settings"],
    queryFn: () => apiClient.get("/trackerboost/settings"),
  });
}

export function useUpdateTrackerBoostSettings() {
  const queryClient = useQueryClient();
  return useMutation<TrackerBoostSettings, Error, TrackerBoostSettings>({
    mutationFn: (settings) => apiClient.put("/trackerboost/settings", settings),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useTrackerBoostMatrix() {
  return useQuery<TrackerCrossMatrixResult>({
    queryKey: ["trackerboost", "matrix"],
    queryFn: () => apiClient.get("/trackerboost/matrix"),
    refetchInterval: 15_000,
  });
}

export function useScanTrackerBoostTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; testedCount: number }, Error, void>({
    mutationFn: () => apiClient.post("/trackerboost/scan"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useHarvestDownloadTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; harvestedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/trackerboost/harvest/downloads"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      },
    },
  );
}

export function useHarvestProwlarrTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; harvestedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/trackerboost/harvest/prowlarr"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      },
    },
  );
}

export function useHarvestFeedTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; harvestedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/trackerboost/harvest/feeds"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      },
    },
  );
}

export function useBoostTorrent() {
  const queryClient = useQueryClient();
  return useMutation<SwarmBoostResult, Error, number>({
    mutationFn: (torrentId) =>
      apiClient.post(`/trackerboost/boost/${torrentId}`),
    onSuccess: (_, torrentId) => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      queryClient.invalidateQueries({
        queryKey: ["trackerboost", "check", torrentId],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["trackers", torrentId] });
    },
  });
}

export function useBoostHash() {
  const queryClient = useQueryClient();
  return useMutation<
    SwarmBoostResult,
    Error,
    { infoHash: string; name?: string }
  >({
    mutationFn: (vars) => {
      const search = vars.name ? `?name=${encodeURIComponent(vars.name)}` : "";
      return apiClient.post(
        `/trackerboost/boost-hash/${vars.infoHash}${search}`,
      );
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      queryClient.invalidateQueries({
        queryKey: ["trackerboost", "check-hash", vars.infoHash],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useInjectTrackerToTorrent() {
  const queryClient = useQueryClient();
  return useMutation<
    SwarmBoostResult,
    Error,
    { torrentId?: number; infoHash?: string; trackerUrl: string }
  >({
    mutationFn: (payload) => apiClient.post("/trackerboost/inject", payload),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      if (vars.torrentId) {
        queryClient.invalidateQueries({
          queryKey: ["trackerboost", "check", vars.torrentId],
        });
      }
      if (vars.infoHash) {
        queryClient.invalidateQueries({
          queryKey: ["trackerboost", "check-hash", vars.infoHash],
        });
      }
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useBoostAllTorrents() {
  const queryClient = useQueryClient();
  return useMutation<SwarmBoostResult[], Error, void>({
    mutationFn: () => apiClient.post("/trackerboost/boost-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useAddTrackerBoostTracker() {
  const queryClient = useQueryClient();
  return useMutation<TrackerBoostTracker, Error, { url: string }>({
    mutationFn: (payload) => apiClient.post("/trackerboost/trackers", payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useDeleteTrackerBoostTracker() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error, number>({
    mutationFn: (id) => apiClient.delete(`/trackerboost/trackers/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useBulkImportTrackerBoostTrackers() {
  const queryClient = useQueryClient();
  return useMutation<
    { success: boolean; importedCount: number },
    Error,
    { trackersText: string }
  >({
    mutationFn: (payload) =>
      apiClient.post("/trackerboost/trackers/bulk", payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

// Aliases for backward compatibility
export const useDownloadPlusPlusStatus = useTrackerBoostStatus;
export const useDownloadPlusPlusTrackers = useTrackerBoostTrackers;
export const useScanDownloadPlusPlusTrackers = useScanTrackerBoostTrackers;
export const useAddDownloadPlusPlusTracker = useAddTrackerBoostTracker;
export const useDeleteDownloadPlusPlusTracker = useDeleteTrackerBoostTracker;
export const useBulkImportDownloadPlusPlusTrackers =
  useBulkImportTrackerBoostTrackers;

export function useTrackerMetrics(refetchInterval: number | false = 4000) {
  return useQuery<TrackerMetric[]>({
    queryKey: ["trackermetrics"],
    queryFn: () => apiClient.get("/trackermetrics"),
    refetchInterval,
  });
}

export function useTrackerMetricsSummary(
  refetchInterval: number | false = 4000,
) {
  return useQuery<TrackerMetricsSummary>({
    queryKey: ["trackermetrics", "summary"],
    queryFn: () => apiClient.get("/trackermetrics/summary"),
    refetchInterval,
  });
}

export function useTrackerMetric(id: number) {
  return useQuery<TrackerMetric>({
    queryKey: ["trackermetrics", id],
    queryFn: () => apiClient.get(`/trackermetrics/${id}`),
    enabled: id > 0,
  });
}

export function useTrackerMetricHistory(id: number, hours = 24) {
  return useQuery<TrackerMetricSnapshot[]>({
    queryKey: ["trackermetrics", id, "history", hours],
    queryFn: () =>
      apiClient.get(`/trackermetrics/${id}/history?hours=${hours}`),
    enabled: id > 0,
  });
}

export function useResetTrackerMetric() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; message: string }, Error, number>({
    mutationFn: (id: number) =>
      apiClient.post(`/trackermetrics/${id}/reset`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackermetrics"] });
    },
  });
}

export function useDeleteTrackerMetric() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; message: string }, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/trackermetrics/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackermetrics"] });
    },
  });
}

export function useTorrentEngines() {
  return useQuery<TorrentEngine[]>({
    queryKey: ["torrentengine", "list"],
    queryFn: () => apiClient.get("/torrentengine"),
    refetchInterval: 10_000,
  });
}

export function useActiveTorrentEngine() {
  return useQuery<ActiveEngineStatus>({
    queryKey: ["torrentengine", "active"],
    queryFn: () => apiClient.get("/torrentengine/active"),
    refetchInterval: 3_000,
  });
}

export function useSwitchTorrentEngine() {
  const queryClient = useQueryClient();
  return useMutation<SwitchEngineResult, Error, SwitchEngineRequest>({
    mutationFn: (req: SwitchEngineRequest) =>
      apiClient.post("/torrentengine/switch", req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrentengine"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["config"] });
    },
  });
}

export function useProbeTorrentEngine() {
  return useMutation<EngineProbeResult, Error, string>({
    mutationFn: (engineId: string) =>
      apiClient.post(`/torrentengine/${engineId}/probe`, {}),
  });
}

export function useSubsystems() {
  return useQuery<SubsystemOverview[]>({
    queryKey: ["subsystems"],
    queryFn: () => apiClient.get("/subsystems"),
    refetchInterval: 10_000,
  });
}

export function useSubsystemDetails(subsystemId: string) {
  return useQuery<SubsystemOverview>({
    queryKey: ["subsystems", subsystemId],
    queryFn: () => apiClient.get(`/subsystems/${subsystemId}`),
    enabled: !!subsystemId,
  });
}

export function useSwitchSubsystem() {
  const queryClient = useQueryClient();
  return useMutation<SwitchSubsystemResult, Error, SwitchSubsystemRequest>({
    mutationFn: (req: SwitchSubsystemRequest) =>
      apiClient.post(`/subsystems/${req.subsystemId}/switch`, {
        providerId: req.providerId,
      }),
    onSuccess: (_, req) => {
      queryClient.invalidateQueries({ queryKey: ["subsystems"] });
      queryClient.invalidateQueries({
        queryKey: ["subsystems", req.subsystemId],
      });
      queryClient.invalidateQueries({ queryKey: ["config"] });
      if (req.subsystemId === "bittorrent") {
        queryClient.invalidateQueries({ queryKey: ["torrentengine"] });
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
      }
    },
  });
}

export function useProbeSubsystemProvider() {
  return useMutation<
    SubsystemProbeResult,
    Error,
    { subsystemId: string; providerId: string }
  >({
    mutationFn: (vars) =>
      apiClient.post(
        `/subsystems/${vars.subsystemId}/probe/${vars.providerId}`,
        {},
      ),
  });
}

export function useTorrentEngineMetrics(
  refetchInterval: number | false = 2000,
) {
  return useQuery<TorrentEngineMetrics>({
    queryKey: ["system", "resources", "engine"],
    queryFn: () => apiClient.get("/system/resources/engine"),
    refetchInterval,
  });
}

export function usePerTorrentMetrics(refetchInterval: number | false = 2000) {
  return useQuery<TorrentResourceMetrics[]>({
    queryKey: ["system", "resources", "torrents"],
    queryFn: () => apiClient.get("/system/resources/torrents"),
    refetchInterval,
  });
}

export function useTorrentResourceMetrics(
  id: number,
  refetchInterval: number | false = 2000,
) {
  return useQuery<TorrentResourceMetrics>({
    queryKey: ["system", "resources", "torrents", id],
    queryFn: () => apiClient.get(`/system/resources/torrents/${id}`),
    refetchInterval,
    enabled: id > 0,
  });
}
