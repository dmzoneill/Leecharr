import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import type {
  AutomationScript,
  AutomationExecutionResult,
  AutomationMarketplaceTemplate,
  AutomationTestRequest,
  InstallMarketplaceTemplateRequest,
} from "../types";

export function useAutomationScripts() {
  return useQuery<AutomationScript[]>({
    queryKey: ["automation", "scripts"],
    queryFn: () => apiClient.get("/automation"),
  });
}

export function useAutomationScript(id: number) {
  return useQuery<AutomationScript>({
    queryKey: ["automation", "scripts", id],
    queryFn: () => apiClient.get(`/automation/${id}`),
    enabled: id > 0,
  });
}

export function useCreateAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<AutomationScript, Error, Partial<AutomationScript>>({
    mutationFn: (script) => apiClient.post("/automation", script),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}

export function useUpdateAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<AutomationScript, Error, AutomationScript>({
    mutationFn: (script) => apiClient.put("/automation", script),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}

export function useDeleteAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/automation/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}

export function useRunAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<AutomationExecutionResult, Error, { id: number; torrentId?: number }>({
    mutationFn: ({ id, torrentId }) =>
      apiClient.post(`/automation/${id}/run${torrentId ? `?torrentId=${torrentId}` : ""}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useTestAutomationScript() {
  return useMutation<AutomationExecutionResult, Error, AutomationTestRequest>({
    mutationFn: (req) => apiClient.post("/automation/test", req),
  });
}

export function useAutomationMarketplace() {
  return useQuery<AutomationMarketplaceTemplate[]>({
    queryKey: ["automation", "marketplace"],
    queryFn: () => apiClient.get("/automation/marketplace"),
  });
}

export function useInstallMarketplaceTemplate() {
  const queryClient = useQueryClient();
  return useMutation<AutomationScript, Error, InstallMarketplaceTemplateRequest>({
    mutationFn: (req) => apiClient.post("/automation/marketplace/install", req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}
