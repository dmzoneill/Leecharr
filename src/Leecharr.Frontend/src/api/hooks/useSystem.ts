import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import type {
  SystemStatus,
  HealthCheckResult,
  DiskSpaceInfo,
  NetworkStatus,
  NetworkDiagnostics,
  Backup,
  RestoreBackupRequest,
  UpdateEntry,
  AiStatus,
  AiParsedRelease,
  AiDiagnosticReport,
  AiSearchParameters,
  AiMalwareRiskAssessment,
  AiChatRequest,
  AiChatResponse,
  AiConfig,
  SystemResourceTelemetrySnapshot,
  HostProcessResourceMetrics,
} from "../types";

export const DEFAULT_REFETCH_MS = 5000;

export function useRefetchInterval(): number {
  const { data } = useQuery<{ uiRefreshRateSec: number }>({
    queryKey: ["config", "advanced"],
    queryFn: () => apiClient.get("/config/advanced"),
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
  return data?.uiRefreshRateSec
    ? data.uiRefreshRateSec * 1000
    : DEFAULT_REFETCH_MS;
}

export function useSystemStatus() {
  return useQuery<SystemStatus>({
    queryKey: ["system", "status"],
    queryFn: () => apiClient.get("/system/status"),
  });
}

export function useHealthChecks() {
  return useQuery<HealthCheckResult[]>({
    queryKey: ["health"],
    queryFn: () => apiClient.get("/health"),
    refetchInterval: 30000,
  });
}

export function useDiskSpace() {
  return useQuery<DiskSpaceInfo[]>({
    queryKey: ["diskspace"],
    queryFn: () => apiClient.get("/diskspace"),
  });
}

export function useNetworkStatus() {
  return useQuery<NetworkStatus>({
    queryKey: ["network", "status"],
    queryFn: () => apiClient.get("/network/status"),
  });
}

export function useNetworkDiagnostics() {
  const interval = useRefetchInterval();
  return useQuery<NetworkDiagnostics>({
    queryKey: ["network", "diagnostics"],
    queryFn: () => apiClient.get("/network/diagnostics"),
    refetchInterval: interval,
  });
}

export function useBackups() {
  return useQuery<Backup[]>({
    queryKey: ["backups"],
    queryFn: () => apiClient.get("/backup"),
  });
}

export function useCreateBackup() {
  const queryClient = useQueryClient();
  return useMutation<Backup, Error, void>({
    mutationFn: () => apiClient.post("/backup"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["backups"] }),
  });
}

export function useDeleteBackup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/backup/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["backups"] }),
  });
}

export function useRestoreBackup() {
  return useMutation({
    mutationFn: (request: RestoreBackupRequest) =>
      apiClient.post("/backup/restore", request),
  });
}

export function useUpdates() {
  return useQuery<UpdateEntry[]>({
    queryKey: ["updates"],
    queryFn: () => apiClient.get("/update"),
    staleTime: 60_000,
  });
}

export function useAiStatus() {
  return useQuery<AiStatus>({
    queryKey: ["ai", "status"],
    queryFn: () => apiClient.get("/ai/status"),
    refetchInterval: 30_000,
  });
}

export function useAiParseRelease() {
  return useMutation<AiParsedRelease, Error, { releaseName: string }>({
    mutationFn: (vars) => apiClient.post("/ai/parse-release", vars),
  });
}

export function useAiNaturalSearch() {
  return useMutation<AiSearchParameters, Error, { query: string }>({
    mutationFn: (vars) => apiClient.post("/ai/natural-search", vars),
  });
}

export function useAiDiagnoseTorrent() {
  return useMutation<AiDiagnosticReport, Error, number>({
    mutationFn: (torrentId: number) =>
      apiClient.post(`/ai/diagnose/${torrentId}`, {}),
  });
}

export function useAiMalwareCheck() {
  return useMutation<
    AiMalwareRiskAssessment,
    Error,
    { torrentId?: number; torrentName?: string; fileNames?: string[] }
  >({
    mutationFn: (vars) => apiClient.post("/ai/malware-check", vars),
  });
}

export function useAiChat() {
  return useMutation<AiChatResponse, Error, AiChatRequest>({
    mutationFn: (vars) => apiClient.post("/ai/chat", vars),
  });
}

export function useAiConfig() {
  return useQuery<AiConfig>({
    queryKey: ["config", "ai"],
    queryFn: () => apiClient.get("/config/ai"),
  });
}

export function useSaveAiConfig() {
  const queryClient = useQueryClient();
  return useMutation<AiConfig, Error, AiConfig>({
    mutationFn: (config) => apiClient.put("/config/ai/1", config),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["config", "ai"] }),
  });
}

export function useSystemResources(refetchInterval: number | false = 2000) {
  return useQuery<SystemResourceTelemetrySnapshot>({
    queryKey: ["system", "resources"],
    queryFn: () => apiClient.get("/system/resources"),
    refetchInterval,
  });
}

export function useHostResources(refetchInterval: number | false = 2000) {
  return useQuery<HostProcessResourceMetrics>({
    queryKey: ["system", "resources", "host"],
    queryFn: () => apiClient.get("/system/resources/host"),
    refetchInterval,
  });
}
