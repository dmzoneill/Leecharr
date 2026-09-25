import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useRefetchInterval } from "./hooks/useSystem";

export interface BlocklistResource {
  enabled: boolean;
  url: string;
  autoUpdateEnabled: boolean;
  autoUpdateIntervalDays: number;
  ipv4RuleCount: number;
  ipv6RuleCount: number;
  totalRuleCount: number;
  ruleCount: number;
  lastUpdatedUtc: string | null;
  lastSyncStatus: string;
  nextScheduledSyncUtc: string | null;
  nextAllowedSyncUtc: string | null;
}

export interface BlocklistConfigRequest {
  enabled?: boolean;
  url?: string;
  autoUpdateEnabled?: boolean;
  autoUpdateIntervalDays?: number;
}

export interface BlocklistSyncResponse {
  success: boolean;
  status: string;
  message: string;
  ruleCount: number;
  totalRuleCount: number;
  ipv4RuleCount: number;
  ipv6RuleCount: number;
  lastUpdatedUtc: string | null;
  nextAllowedSyncUtc: string | null;
}

export interface BlocklistTestRequest {
  ip: string;
}

export interface BlocklistTestResponse {
  isBlocked: boolean;
  rule: string | null;
}

export function getBlocklistStatus(): Promise<BlocklistResource> {
  return apiClient.get<BlocklistResource>("/blocklist");
}

export function updateBlocklistConfig(
  config: BlocklistConfigRequest,
): Promise<BlocklistResource> {
  return apiClient.put<BlocklistResource>("/blocklist", config);
}

export function syncBlocklist(): Promise<BlocklistSyncResponse> {
  return apiClient.post<BlocklistSyncResponse>("/blocklist/sync");
}

export function testBlocklistIp(
  ip: string,
): Promise<BlocklistTestResponse> {
  return apiClient.post<BlocklistTestResponse>("/blocklist/test", { ip });
}

export function useBlocklistStatus() {
  const interval = useRefetchInterval();
  return useQuery<BlocklistResource>({
    queryKey: ["blocklist"],
    queryFn: getBlocklistStatus,
    refetchInterval: interval,
  });
}

export function useUpdateBlocklistConfig() {
  const queryClient = useQueryClient();
  return useMutation<BlocklistResource, Error, BlocklistConfigRequest>({
    mutationFn: updateBlocklistConfig,
    onSuccess: (data) => {
      queryClient.setQueryData(["blocklist"], data);
      queryClient.invalidateQueries({ queryKey: ["blocklist"] });
    },
  });
}

export function useSyncBlocklist() {
  const queryClient = useQueryClient();
  return useMutation<BlocklistSyncResponse, Error, void>({
    mutationFn: syncBlocklist,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["blocklist"] });
    },
  });
}
