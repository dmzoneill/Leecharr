import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import { useRefetchInterval } from "./useSystem";
import type {
  LogFile,
  TorrentEventLogEntry,
  PeerConnectionLogEntry,
  TrackerBoostLogEntry,
} from "../types";

export function useTorrentLogs(
  torrentId: number,
  options?: { polling?: boolean },
) {
  const interval = useRefetchInterval(options?.polling === false ? 0 : 3000);
  const effectiveInterval =
    options?.polling === false ? false : interval;
  return useQuery<TorrentEventLogEntry[]>({
    queryKey: ["torrents", torrentId, "logs"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/logs?count=100`),
    enabled: torrentId > 0,
    refetchInterval: effectiveInterval,
  });
}

export function useLogFiles() {
  return useQuery<LogFile[]>({
    queryKey: ["logfiles"],
    queryFn: () => apiClient.get("/logfile"),
  });
}

export function useClearLogFiles() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.delete("/logfile"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["logfiles"] }),
  });
}

export function usePeerConnectionLog(params?: {
  start?: string;
  end?: string;
  infoHash?: string;
}) {
  const searchParams = new URLSearchParams();
  if (params?.start) searchParams.set("start", params.start);
  if (params?.end) searchParams.set("end", params.end);
  if (params?.infoHash) searchParams.set("infoHash", params.infoHash);
  const query = searchParams.toString();
  return useQuery<PeerConnectionLogEntry[]>({
    queryKey: ["peerlog", params?.start, params?.end, params?.infoHash],
    queryFn: () => apiClient.get(`/peerlog${query ? `?${query}` : ""}`),
  });
}

export function useTrackerBoostLogs(
  limit = 150,
  category?: string,
  level?: string,
  refetchInterval?: number | false,
) {
  const interval = useRefetchInterval(
    refetchInterval === false ? 0 : (refetchInterval ?? 3000),
  );
  const effectiveInterval = refetchInterval === false ? false : interval;
  return useQuery<TrackerBoostLogEntry[]>({
    queryKey: ["trackerboost", "logs", limit, category, level],
    queryFn: () => {
      const params = new URLSearchParams();
      if (limit) params.set("limit", limit.toString());
      if (category && category !== "all") params.set("category", category);
      if (level && level !== "all") params.set("level", level);
      const queryStr = params.toString();
      return apiClient.get(
        `/trackerboost/logs${queryStr ? `?${queryStr}` : ""}`,
      );
    },
    refetchInterval: effectiveInterval,
  });
}

export function useClearTrackerBoostLogs() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error>({
    mutationFn: () => apiClient.delete("/trackerboost/logs"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost", "logs"] });
    },
  });
}
