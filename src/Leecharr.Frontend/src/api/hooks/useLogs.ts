import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import { useRefetchInterval } from "./useSystem";
import type {
  LogFile,
  TorrentEventLogEntry,
  PeerConnectionLogEntry,
  TrackerBoostLogEntry,
} from "../types";

export type LogLevel = "Trace" | "Debug" | "Info" | "Warn" | "Error";

export interface EventLogEntry {
  id: number;
  timestamp: string;
  level: LogLevel;
  component: string;
  message: string;
}

export function useEventLogs(levelParam?: LogLevel | null) {
  const interval = useRefetchInterval();
  return useQuery<EventLogEntry[]>({
    queryKey: ["system", "events", levelParam],
    queryFn: async () => {
      const query = levelParam
        ? `?level=${encodeURIComponent(levelParam.toLowerCase())}`
        : "";
      const data = await apiClient.get<
        Array<{
          id: number;
          time: string;
          level: string;
          logger: string;
          message: string;
          exception: string | null;
        }>
      >(`/log${query}`);
      return data.map((entry) => {
        const normLevel =
          entry.level.charAt(0).toUpperCase() +
          entry.level.slice(1).toLowerCase();
        const validLevel: LogLevel =
          normLevel === "Trace" ||
          normLevel === "Debug" ||
          normLevel === "Warn" ||
          normLevel === "Error"
            ? (normLevel as LogLevel)
            : "Info";
        return {
          id: entry.id,
          timestamp: entry.time,
          level: validLevel,
          component: entry.logger,
          message: entry.exception
            ? `${entry.message}\n${entry.exception}`
            : entry.message,
        };
      });
    },
    refetchInterval: interval,
  });
}

export function useTorrentLogs(
  torrentId: number,
  options?: { polling?: boolean },
) {
  const interval = useRefetchInterval(options?.polling === false ? 0 : 3000);
  const effectiveInterval = options?.polling === false ? false : interval;
  return useQuery<TorrentEventLogEntry[]>({
    queryKey: ["torrents", torrentId, "logs"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/logs?count=100`),
    enabled: torrentId > 0,
    refetchInterval: effectiveInterval,
  });
}

export function useLogFiles() {
  const interval = useRefetchInterval();
  return useQuery<LogFile[]>({
    queryKey: ["logfiles"],
    queryFn: () => apiClient.get("/logfile"),
    refetchInterval: interval,
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
  const interval = useRefetchInterval();
  const searchParams = new URLSearchParams();
  if (params?.start) searchParams.set("start", params.start);
  if (params?.end) searchParams.set("end", params.end);
  if (params?.infoHash) searchParams.set("infoHash", params.infoHash);
  const query = searchParams.toString();
  return useQuery<PeerConnectionLogEntry[]>({
    queryKey: ["peerlog", params?.start, params?.end, params?.infoHash],
    queryFn: () => apiClient.get(`/peerlog${query ? `?${query}` : ""}`),
    refetchInterval: interval,
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
