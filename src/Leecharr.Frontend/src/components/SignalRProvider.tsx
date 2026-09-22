import { useEffect, useRef, createContext, useContext, useMemo, type ReactNode } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { HubConnectionState } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import {
  useSignalR,
  getSignalRConnection,
  reconnectSignalR,
  subscribeToTorrent,
  unsubscribeFromTorrent,
  subscribeToChannel,
  unsubscribeFromChannel,
  type ConnectionStatus,
} from "../api/signalr";
import { useToast } from "../context/ToastContext";
import { useTorrentStore } from "../stores/useTorrentStore";

export {
  subscribeToTorrent,
  unsubscribeFromTorrent,
  subscribeToChannel,
  unsubscribeFromChannel,
};

export interface SignalRContextValue {
  connection: HubConnection;
  status: ConnectionStatus;
  connected: boolean;
  isReconnecting: boolean;
  reconnect: () => Promise<void>;
  subscribeToTorrent: (id: number) => Promise<void>;
  unsubscribeFromTorrent: (id: number) => Promise<void>;
  subscribeToChannel: (name: string) => Promise<void>;
  unsubscribeFromChannel: (name: string) => Promise<void>;
}

export const SignalRContext = createContext<SignalRContextValue | null>(null);

export function useSignalRContext(): SignalRContextValue {
  const context = useContext(SignalRContext);
  if (context) {
    return context;
  }
  const conn = getSignalRConnection();
  return {
    connection: conn,
    status: conn.state === HubConnectionState.Connected ? "connected" : "disconnected",
    connected: conn.state === HubConnectionState.Connected,
    isReconnecting: conn.state === HubConnectionState.Reconnecting,
    reconnect: reconnectSignalR,
    subscribeToTorrent,
    unsubscribeFromTorrent,
    subscribeToChannel,
    unsubscribeFromChannel,
  };
}

export const EVENT_INVALIDATION_MAP: Record<string, string[][]> = {
  TorrentAdded: [["torrents"], ["trackerboost"]],
  torrent_added: [["torrents"], ["trackerboost"]],
  torrentAdded: [["torrents"], ["trackerboost"]],
  TorrentUpdated: [["torrents"], ["trackerboost"]],
  torrent_updated: [["torrents"], ["trackerboost"]],
  torrentUpdated: [["torrents"], ["trackerboost"]],
  TorrentDeleted: [["torrents"], ["trackerboost"]],
  torrent_deleted: [["torrents"], ["trackerboost"]],
  torrentDeleted: [["torrents"], ["trackerboost"]],
  TorrentRecheckProgress: [["torrents"]],
  torrent_recheck_progress: [["torrents"]],
  SeedingStatsUpdated: [["seeding", "stats"]],
  speed_update: [["seeding", "stats"]],
  speedPulse: [["seeding", "stats"]],
  speedUpdate: [["seeding", "stats"]],
  HealthCheckCompleted: [["health"]],
  health_warning: [["health"]],
  healthWarning: [["health"]],
  CommandStarted: [["system", "status"], ["system", "commands"]],
  CommandCompleted: [["system", "status"], ["system", "commands"]],
  TaskStarted: [["system", "tasks"], ["system", "status"]],
  TaskCompleted: [["system", "tasks"], ["system", "status"]],
  task_progress: [["system", "tasks"], ["system", "status"]],
  taskProgress: [["system", "tasks"], ["system", "status"]],
  AutomationExecuted: [["automation", "scripts"], ["automation"]],
  AutomationTriggerEvaluated: [["automation", "scripts"], ["automation"]],
  TrackerUpdated: [["trackerboost"]],
  TrackerAnnounced: [["trackerboost"]],
  trackerUpdated: [["trackerboost"]],
  trackerAnnounced: [["trackerboost"]],
  tracker_updated: [["trackerboost"]],
  tracker_announced: [["trackerboost"]],
  TrackerAnnounceEvent: [["trackerboost"]],
  subsystemSwitched: [["subsystems"], ["torrentengine"]],
  subsystem_switched: [["subsystems"], ["torrentengine"]],
};


export const RECONNECT_QUERY_KEYS: string[][] = [
  ["torrents"],
  ["trackerboost"],
  ["seeding", "stats"],
  ["seeding", "history"],
  ["categories"],
  ["tags"],
  ["speedschedule"],
  ["speedschedule", "active"],
  ["health"],
  ["system", "status"],
  ["system", "commands"],
  ["system", "tasks"],
  ["diskspace"],
  ["automation"],
  ["automation", "scripts"],
];

/**
 * Determines whether a SignalR message name is handled by dedicated named event handlers
 * to avoid duplicate query invalidation storms from generic receiveMessage dispatch.
 */
export function isHandledByNamedEvent(name?: string): boolean {
  if (!name) return false;
  const lower = name.toLowerCase();
  return (
    lower.includes("torrent") ||
    lower.includes("seeding") ||
    lower.includes("health") ||
    lower.includes("command") ||
    lower.includes("system") ||
    lower.includes("task") ||
    lower.includes("automation") ||
    lower.includes("speed") ||
    lower.includes("tracker")
  );
}

export default function SignalRProvider({ children }: { children?: ReactNode } = {}) {
  const queryClient = useQueryClient();
  const {
    connection,
    status,
    connected,
    isReconnecting,
    reconnect,
    subscribeToTorrent: subTorrent,
    unsubscribeFromTorrent: unsubTorrent,
    subscribeToChannel: subChannel,
    unsubscribeFromChannel: unsubChannel,
  } = useSignalR(queryClient);
  const { showToast } = useToast();
  const showToastRef = useRef(showToast);
  showToastRef.current = showToast;

  useEffect(() => {
    const handleTelemetryPayload = (body: unknown) => {
      if (!body) return;
      let updates: Array<{ id: number; [key: string]: unknown }> = [];
      if (Array.isArray(body)) {
        updates = body as Array<{ id: number; [key: string]: unknown }>;
      } else if (typeof body === "object") {
        const obj = body as Record<string, unknown>;
        if (Array.isArray(obj.torrents)) {
          updates = obj.torrents as Array<{ id: number; [key: string]: unknown }>;
        } else if (typeof obj.id === "number") {
          updates = [obj as { id: number; [key: string]: unknown }];
        } else {
          updates = Object.entries(obj).map(([id, data]) => ({
            id: Number(id) || (data as { id?: number })?.id || 0,
            ...(typeof data === "object" && data !== null ? data : {}),
          }));
        }
      }
      if (updates.length > 0) {
        useTorrentStore.getState().updateTelemetry(updates);
      }
    };

    const handlePieceMapPayload = (body: unknown) => {
      if (!body || typeof body !== "object") return;
      const b = body as { torrentId?: number; id?: number };
      const tid = Number(b.torrentId || b.id);
      if (tid) {
        useTorrentStore.getState().updatePieceMap(tid, b);
      }
    };

    const onSpeedPulse = (data: unknown) => {
      handleTelemetryPayload(data);
    };
    connection.on("speedPulse", onSpeedPulse);
    connection.on("speed_update", onSpeedPulse);

    const onStateSnapshot = (data: unknown) => {
      handleTelemetryPayload(data);
    };
    connection.on("stateSnapshot", onStateSnapshot);

    const onPieceMapUpdated = (data: unknown) => {
      handlePieceMapPayload(data);
    };
    connection.on("pieceMapUpdated", onPieceMapUpdated);
    connection.on("piece_map_updated", onPieceMapUpdated);

    // 1. Generic receiveMessage dispatcher for unmapped events
    const onReceiveMessage = (msg: unknown) => {
      if (!msg || typeof msg !== "object") return;
      const message = msg as {
        name?: string;
        body?: unknown;
        action?: string;
      };
      const name = (message.name ?? "").toLowerCase();

      if (name === "speedpulse" || name === "speed_update" || name === "statesnapshot") {
        handleTelemetryPayload(message.body);
        return;
      }
      if (name === "piecemapupdated" || name === "piece_map_updated") {
        handlePieceMapPayload(message.body);
        return;
      }

      // Eliminate duplicate invalidations for events already handled by named event listeners
      if (isHandledByNamedEvent(name)) {
        return;
      }

      if (name.includes("tracker")) {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
        const bodyObj = message.body as Record<string, unknown> | undefined;
        const torrentId =
          typeof bodyObj?.torrentId === "number"
            ? bodyObj.torrentId
            : typeof bodyObj?.TorrentId === "number"
            ? bodyObj.TorrentId
            : undefined;
        if (torrentId) {
          queryClient.invalidateQueries({
            queryKey: ["torrents", torrentId, "trackers"],
          });
        }
      } else if (name.includes("category")) {
        queryClient.invalidateQueries({ queryKey: ["categories"] });
      } else if (name.includes("tag")) {
        queryClient.invalidateQueries({ queryKey: ["tags"] });
      } else if (name.includes("seeding")) {
        queryClient.invalidateQueries({ queryKey: ["seeding", "stats"] });
      } else if (name.includes("schedule") || name.includes("speedschedule")) {
        queryClient.invalidateQueries({ queryKey: ["speedschedule"] });
        queryClient.invalidateQueries({ queryKey: ["speedschedule", "active"] });
      }
    };

    connection.on("receiveMessage", onReceiveMessage);

    // 2. Direct named event handlers
    const handlers: Array<[string, (data?: unknown) => void]> = [];

    for (const [event, queryKeys] of Object.entries(EVENT_INVALIDATION_MAP)) {
      const handler = (data?: unknown) => {
        for (const key of queryKeys) {
          queryClient.invalidateQueries({ queryKey: key });
        }

        const lowerEvent = event.toLowerCase();

        // Entity-specific invalidations for torrents
        if (lowerEvent.includes("torrent")) {
          const bodyObj = data as Record<string, unknown> | undefined;
          const torrentId =
            typeof bodyObj?.id === "number"
              ? bodyObj.id
              : typeof bodyObj?.torrentId === "number"
              ? bodyObj.torrentId
              : typeof bodyObj?.TorrentId === "number"
              ? bodyObj.TorrentId
              : undefined;

          if (torrentId) {
            queryClient.invalidateQueries({
              queryKey: ["torrents", torrentId],
            });
            queryClient.invalidateQueries({
              queryKey: ["torrents", torrentId, "trackers"],
            });

            if (lowerEvent.includes("deleted")) {
              useTorrentStore.getState().removeTorrent(torrentId);
            } else if (bodyObj) {
              useTorrentStore.getState().updateTelemetry([
                { id: torrentId, ...bodyObj },
              ]);
            }
          }
        }

        // Entity-specific invalidations for trackers
        if (lowerEvent.includes("tracker")) {
          const bodyObj = data as Record<string, unknown> | undefined;
          const torrentId =
            typeof bodyObj?.torrentId === "number"
              ? bodyObj.torrentId
              : typeof bodyObj?.TorrentId === "number"
              ? bodyObj.TorrentId
              : undefined;

          if (torrentId) {
            queryClient.invalidateQueries({
              queryKey: ["torrents", torrentId, "trackers"],
            });
          }
        }

        // Fire toast notifications for key events
        if (lowerEvent === "torrentadded" || lowerEvent === "torrent_added") {
          const name =
            data && typeof data === "object" && "name" in data
              ? String((data as Record<string, unknown>).name)
              : undefined;
          showToastRef.current(
            name ? `Torrent added: ${name}` : "Torrent added",
            "success",
          );
        } else if (lowerEvent === "torrentdeleted" || lowerEvent === "torrent_deleted") {
          showToastRef.current("Torrent removed", "info");
        } else if (lowerEvent.includes("automationexecuted")) {
          const body = data as Record<string, unknown> | undefined;
          const isSuccess = body?.success !== false && body?.Success !== false;
          showToastRef.current(
            isSuccess
              ? "Automation execution completed"
              : "Automation execution encountered error",
            isSuccess ? "success" : "error",
          );
        }
      };

      handlers.push([event, handler]);
      connection.on(event, handler);
    }

    // 3. Reconnection query cache synchronization
    const handleReconnected = () => {
      for (const key of RECONNECT_QUERY_KEYS) {
        queryClient.invalidateQueries({ queryKey: key });
      }
    };

    connection.onreconnected(handleReconnected);

    return () => {
      connection.off("receiveMessage", onReceiveMessage);
      connection.off("speedPulse", onSpeedPulse);
      connection.off("speed_update", onSpeedPulse);
      connection.off("stateSnapshot", onStateSnapshot);
      connection.off("pieceMapUpdated", onPieceMapUpdated);
      connection.off("piece_map_updated", onPieceMapUpdated);
      for (const [event, handler] of handlers) {
        connection.off(event, handler);
      }
      const callbacks = (
        connection as unknown as { _reconnectedCallbacks?: Array<unknown> }
      )._reconnectedCallbacks;
      if (callbacks) {
        const idx = callbacks.indexOf(handleReconnected);
        if (idx !== -1) {
          callbacks.splice(idx, 1);
        }
      }
    };
  }, [connection, queryClient]);

  const dotColor =
    status === "connected"
      ? "var(--signalr-connected, #22c55e)"
      : status === "reconnecting"
        ? "var(--signalr-reconnecting, #f59e0b)"
        : "var(--signalr-disconnected, #ef4444)";

  const title =
    status === "connected"
      ? "Real-time: connected"
      : status === "reconnecting"
        ? "Real-time: reconnecting..."
        : "Real-time: disconnected";

  const contextValue = useMemo<SignalRContextValue>(
    () => ({
      connection,
      status,
      connected,
      isReconnecting,
      reconnect,
      subscribeToTorrent: subTorrent,
      unsubscribeFromTorrent: unsubTorrent,
      subscribeToChannel: subChannel,
      unsubscribeFromChannel: unsubChannel,
    }),
    [
      connection,
      status,
      connected,
      isReconnecting,
      reconnect,
      subTorrent,
      unsubTorrent,
      subChannel,
      unsubChannel,
    ],
  );

  return (
    <SignalRContext.Provider value={contextValue}>
      {children}
      <span
        title={title}
        aria-label={title}
        style={{
          display: "inline-block",
          width: 8,
          height: 8,
          borderRadius: "50%",
          backgroundColor: dotColor,
          position: "fixed",
          bottom: 12,
          right: 12,
          zIndex: 9999,
          transition: "background-color 0.3s ease",
        }}
      />
    </SignalRContext.Provider>
  );
}
