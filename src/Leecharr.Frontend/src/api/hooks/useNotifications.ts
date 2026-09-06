import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";
import type { NotificationResource, NotificationTestResult } from "../types";

export function useNotifications() {
  return useQuery<NotificationResource[]>({
    queryKey: ["notifications"],
    queryFn: () => apiClient.get("/notifications"),
  });
}

export function useCreateNotification() {
  const queryClient = useQueryClient();
  return useMutation<
    NotificationResource,
    Error,
    Partial<NotificationResource>
  >({
    mutationFn: (notification) =>
      apiClient.post("/notifications", notification),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}

export function useUpdateNotification() {
  const queryClient = useQueryClient();
  return useMutation<NotificationResource, Error, NotificationResource>({
    mutationFn: (notification) =>
      apiClient.put(`/notifications/${notification.id}`, notification),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}

export function useDeleteNotification() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/notifications/${id}`),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}

export function useTestNotification() {
  return useMutation<NotificationTestResult, Error, number>({
    mutationFn: (id: number) => apiClient.post(`/notifications/${id}/test`),
  });
}

export function useTestDirectNotification() {
  return useMutation<
    NotificationTestResult,
    Error,
    Partial<NotificationResource>
  >({
    mutationFn: (notification) =>
      apiClient.post("/notifications/test", notification),
  });
}
