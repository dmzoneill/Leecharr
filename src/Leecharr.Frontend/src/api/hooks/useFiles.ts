import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import type { FileBrowserListing } from "../types";

export function useFileListing(path?: string) {
  return useQuery<FileBrowserListing>({
    queryKey: ["files", "listing", path ?? ""],
    queryFn: () =>
      apiClient.get(`/files${path ? `?path=${encodeURIComponent(path)}` : ""}`),
  });
}

export function useCreateDirectory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (dirPath: string) =>
      apiClient.post(`/files/mkdir`, { path: dirPath }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["files", "listing"] }),
  });
}

export function useRenameFileEntry() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ path, newName }: { path: string; newName: string }) =>
      apiClient.put(`/files/rename`, { path, newName }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["files", "listing"] }),
  });
}

export function useDeleteFileEntry() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (filePath: string) =>
      apiClient.delete(`/files?path=${encodeURIComponent(filePath)}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["files", "listing"] }),
  });
}

export function useBatchDeleteFiles() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (paths: string[]) =>
      apiClient.post(`/files/batch-delete`, { paths }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["files", "listing"] }),
  });
}

export function usePasteFiles() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      sources,
      destination,
      operation,
    }: {
      sources: string[];
      destination: string;
      operation: "copy" | "move";
    }) => apiClient.post(`/files/paste`, { sources, destination, operation }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["files", "listing"] }),
  });
}

export function useFilePreview(path?: string, enabled = true) {
  return useQuery<import("../types").FilePreviewResult>({
    queryKey: ["files", "preview", path ?? ""],
    queryFn: () =>
      apiClient.get(
        `/files/preview?path=${encodeURIComponent(path || "")}&maxBytes=262144`,
      ),
    enabled: enabled && !!path,
  });
}
