import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import { useRefetchInterval } from "./useSystem";
import type {
  GeneralConfig,
  SeedingConfig,
  NetworkConfig,
  BitTorrentConfig,
  PeerProtocolConfig,
  ProtocolsConfig,
  SimulationConfig,
  TrackerServerConfig,
  TrackerServerStats,
  TrackerServerTorrent,
  SchedulerConfig,
  AdvancedConfig,
  ArrConnection,
  ArrTestResult,
  DownloadClientDefinition,
  DownloadClientTestResult,
  DownloadClientRemoteItem,
  SyncResult,
  IndexerDefinition,
  IndexerTestResult,
  RssRule,
  Torrent,
} from "../types";

function useConfigQuery<T>(section: string) {
  return useQuery<T>({
    queryKey: ["config", section],
    queryFn: () => apiClient.get(`/config/${section}`),
  });
}

function useConfigMutation<T>(section: string) {
  const queryClient = useQueryClient();
  return useMutation<T, Error, T>({
    mutationFn: (config) => apiClient.put(`/config/${section}/1`, config),
    onMutate: async (newConfig) => {
      await queryClient.cancelQueries({ queryKey: ["config", section] });
      const previous = queryClient.getQueryData<T>(["config", section]);
      queryClient.setQueryData<T>(["config", section], newConfig);
      return { previous };
    },
    onError: (_err, _newConfig, context: any) => {
      if (context?.previous) {
        queryClient.setQueryData<T>(["config", section], context.previous);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["config", section] });
    },
  });
}

export function useGeneralConfig() {
  return useConfigQuery<GeneralConfig>("general");
}

export function useSaveGeneralConfig() {
  return useConfigMutation<GeneralConfig>("general");
}

export function useSeedingConfig() {
  return useConfigQuery<SeedingConfig>("seeding");
}

export function useSaveSeedingConfig() {
  return useConfigMutation<SeedingConfig>("seeding");
}

export function useNetworkConfig() {
  return useConfigQuery<NetworkConfig>("network");
}

export function useSaveNetworkConfig() {
  return useConfigMutation<NetworkConfig>("network");
}

export function useBitTorrentConfig() {
  return useConfigQuery<BitTorrentConfig>("bittorrent");
}

export function useSaveBitTorrentConfig() {
  return useConfigMutation<BitTorrentConfig>("bittorrent");
}

export function usePeerProtocolConfig() {
  return useConfigQuery<PeerProtocolConfig>("peerprotocol");
}

export function useSavePeerProtocolConfig() {
  return useConfigMutation<PeerProtocolConfig>("peerprotocol");
}

export function useProtocolsConfig() {
  return useConfigQuery<ProtocolsConfig>("protocols");
}

export function useSaveProtocolsConfig() {
  return useConfigMutation<ProtocolsConfig>("protocols");
}

export function useSimulationConfig() {
  return useConfigQuery<SimulationConfig>("simulation");
}

export function useSaveSimulationConfig() {
  return useConfigMutation<SimulationConfig>("simulation");
}

export function useTrackerServerConfig() {
  return useConfigQuery<TrackerServerConfig>("trackerserver");
}

export function useSaveTrackerServerConfig() {
  return useConfigMutation<TrackerServerConfig>("trackerserver");
}

export function useTrackerServerStats() {
  const interval = useRefetchInterval();
  return useQuery<TrackerServerStats>({
    queryKey: ["trackerserver", "stats"],
    queryFn: () => apiClient.get("/tracker/stats"),
    refetchInterval: interval,
  });
}

export function useTrackerServerTorrents() {
  const interval = useRefetchInterval();
  return useQuery<TrackerServerTorrent[]>({
    queryKey: ["trackerserver", "torrents"],
    queryFn: () => apiClient.get("/trackerserver/torrents"),
    refetchInterval: interval,
  });
}

export function useSchedulerConfig() {
  return useConfigQuery<SchedulerConfig>("scheduler");
}

export function useSaveSchedulerConfig() {
  return useConfigMutation<SchedulerConfig>("scheduler");
}

export function useAdvancedConfig() {
  return useConfigQuery<AdvancedConfig>("advanced");
}

export function useSaveAdvancedConfig() {
  return useConfigMutation<AdvancedConfig>("advanced");
}

export function useArrConnections() {
  return useQuery<ArrConnection[]>({
    queryKey: ["arrconnections"],
    queryFn: () => apiClient.get("/arrconnections"),
  });
}

export function useCreateArrConnection() {
  const queryClient = useQueryClient();
  return useMutation<ArrConnection, Error, Partial<ArrConnection>>({
    mutationFn: (connection) => apiClient.post("/arrconnections", connection),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["arrconnections"] }),
  });
}

export function useUpdateArrConnection() {
  const queryClient = useQueryClient();
  return useMutation<ArrConnection, Error, ArrConnection>({
    mutationFn: (connection) => apiClient.put(`/arrconnections/${connection.id}`, connection),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["arrconnections"] }),
  });
}

export function useDeleteArrConnection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/arrconnections/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["arrconnections"] }),
  });
}

export function useTestArrConnection() {
  return useMutation<ArrTestResult, Error, number>({
    mutationFn: (id) => apiClient.post(`/arrconnections/${id}/test`),
  });
}

export function useTestDirectArrConnection() {
  return useMutation<ArrTestResult, Error, Partial<ArrConnection>>({
    mutationFn: (connection) => apiClient.post("/arrconnections/test", connection),
  });
}

export function useArrSync() {
  const queryClient = useQueryClient();
  return useMutation<SyncResult, Error>({
    mutationFn: () => apiClient.post("/arrsync/sync"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["arrconnections"] });
    },
  });
}

export function useDownloadClients() {
  return useQuery<DownloadClientDefinition[]>({
    queryKey: ["downloadclients"],
    queryFn: () => apiClient.get("/downloadclients"),
  });
}

export function useCreateDownloadClient() {
  const queryClient = useQueryClient();
  return useMutation<DownloadClientDefinition, Error, Partial<DownloadClientDefinition>>({
    mutationFn: (client) => apiClient.post("/downloadclients", client),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["downloadclients"] }),
  });
}

export function useUpdateDownloadClient() {
  const queryClient = useQueryClient();
  return useMutation<DownloadClientDefinition, Error, DownloadClientDefinition>({
    mutationFn: (client) => apiClient.put(`/downloadclients/${client.id}`, client),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["downloadclients"] }),
  });
}

export function useDeleteDownloadClient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/downloadclients/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["downloadclients"] }),
  });
}

export function useTestDownloadClient() {
  return useMutation<DownloadClientTestResult, Error, number>({
    mutationFn: (id) => apiClient.post(`/downloadclients/${id}/test`),
  });
}

export function useTestDirectDownloadClient() {
  return useMutation<DownloadClientTestResult, Error, Partial<DownloadClientDefinition>>({
    mutationFn: (client) => apiClient.post("/downloadclients/test", client),
  });
}

export function useDownloadClientSync() {
  const queryClient = useQueryClient();
  return useMutation<SyncResult, Error>({
    mutationFn: () => apiClient.post("/downloadclientsync/sync"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["torrents"] }),
  });
}

export function useDownloadClientItems(clientId: number) {
  const interval = useRefetchInterval();
  return useQuery<DownloadClientRemoteItem[]>({
    queryKey: ["downloadclients", clientId, "items"],
    queryFn: () => apiClient.get(`/downloadclients/${clientId}/items`),
    enabled: clientId > 0,
    refetchInterval: interval,
  });
}

export function useImportDownloadClientTorrent(clientId: number) {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, string>({
    mutationFn: (infoHash) => apiClient.post(`/downloadclients/${clientId}/import/${infoHash}`),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["downloadclients", clientId, "items"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useImportDownloadClientTorrents(clientId: number) {
  const queryClient = useQueryClient();
  return useMutation<SyncResult, Error, string[]>({
    mutationFn: (infoHashes) =>
      apiClient.post(`/downloadclients/${clientId}/import`, { infoHashes }),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["downloadclients", clientId, "items"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useIndexers() {
  return useQuery<IndexerDefinition[]>({
    queryKey: ["indexers"],
    queryFn: () => apiClient.get("/indexers"),
  });
}

export function useCreateIndexer() {
  const queryClient = useQueryClient();
  return useMutation<IndexerDefinition, Error, Partial<IndexerDefinition>>({
    mutationFn: (indexer) => apiClient.post("/indexers", indexer),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useUpdateIndexer() {
  const queryClient = useQueryClient();
  return useMutation<IndexerDefinition, Error, IndexerDefinition>({
    mutationFn: (indexer) => apiClient.put(`/indexers/${indexer.id}`, indexer),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useDeleteIndexer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/indexers/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useTestIndexer() {
  return useMutation<IndexerTestResult, Error, number>({
    mutationFn: (id) => apiClient.post(`/indexers/${id}/test`),
  });
}

export function useTestDirectIndexer() {
  const queryClient = useQueryClient();
  return useMutation<IndexerTestResult, Error, Partial<IndexerDefinition>>({
    mutationFn: (indexer) => apiClient.post("/indexers/test", indexer),
  });
}

export function useSyncProwlarr() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; syncedCount: number }, Error, { url: string; apiKey: string }>({
    mutationFn: (data: { url: string; apiKey: string }) =>
      apiClient.post("/indexers/sync-prowlarr", data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useRssRules() {
  return useQuery<RssRule[]>({
    queryKey: ["rssrules"],
    queryFn: () => apiClient.get("/rssrules"),
  });
}

export function useCreateRssRule() {
  const queryClient = useQueryClient();
  return useMutation<RssRule, Error, Partial<RssRule>>({
    mutationFn: (rule) => apiClient.post("/rssrules", rule),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["rssrules"] }),
  });
}

export function useUpdateRssRule() {
  const queryClient = useQueryClient();
  return useMutation<RssRule, Error, RssRule>({
    mutationFn: (rule) => apiClient.put(`/rssrules/${rule.id}`, rule),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["rssrules"] }),
  });
}

export function useDeleteRssRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/rssrules/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["rssrules"] }),
  });
}

export function useSyncRss() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; grabbedCount: number }, Error, void>({
    mutationFn: () => apiClient.post("/rssrules/sync"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export * from "./useNotifications";
