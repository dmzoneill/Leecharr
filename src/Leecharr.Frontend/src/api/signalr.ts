import { useState, useEffect, useRef } from "react";
import * as signalR from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";
import { apiClient, getUrlBase } from "./client";

export type ConnectionStatus = "connected" | "disconnected" | "reconnecting";

export type ModelAction =
  "Unknown" | "Created" | "Updated" | "Deleted" | "Sync";

export interface TorrentSnapshot {
  id: number;
  name: string;
  status: string;
  progress: number;
  downloadSpeed: number;
  uploadSpeed: number;
  eta: number;
  size: number;
  totalSize?: number;
  active?: boolean;
}

export interface StateSnapshot {
  torrents: TorrentSnapshot[];
  downloadSpeed: number;
  uploadSpeed: number;
  activeCount?: number;
  totalCount?: number;
  timestampUtc: string;
}

export type MessageHandler = (message: {
  name: string;
  body: unknown;
  action?: ModelAction | number;
}) => void;

export type ReconnectingHandler = (error?: Error) => void;
export type ReconnectedHandler = (connectionId?: string) => void;
export type CloseHandler = (error?: Error) => void;
export type ConnectionStateChangeHandler = (connected: boolean) => void;

export function isUnauthorizedError(error: unknown): boolean {
  if (!error) return false;
  if (error instanceof signalR.HttpError) {
    return error.statusCode === 401 || error.statusCode === 403;
  }
  const err = error as {
    statusCode?: number;
    status?: number;
    message?: string;
  };
  if (
    err.statusCode === 401 ||
    err.statusCode === 403 ||
    err.status === 401 ||
    err.status === 403
  ) {
    return true;
  }
  const msg = typeof err.message === "string" ? err.message : String(error);
  return (
    msg.includes("401") ||
    msg.includes("403") ||
    msg.includes("Unauthorized") ||
    msg.includes("Forbidden")
  );
}

/**
 * Resilient SignalR retry policy implementing exponential backoff with jitter up to 30 seconds,
 * continuing indefinitely without permanently giving up (unless HTTP 401/403 is received).
 */
export class ExponentialBackoffRetryPolicy implements signalR.IRetryPolicy {
  private readonly maxDelayMs: number;
  private readonly initialDelayMs: number;

  constructor(maxDelayMs = 30000, initialDelayMs = 1000) {
    this.maxDelayMs = maxDelayMs;
    this.initialDelayMs = initialDelayMs;
  }

  public nextRetryDelayInMilliseconds(
    retryContext: signalR.RetryContext,
  ): number | null {
    // Abort reconnect loop if authentication failed (HTTP 401/403)
    if (isUnauthorizedError(retryContext.retryReason)) {
      console.warn(
        "SignalR reconnection aborted: HTTP 401/403 unauthorized or forbidden",
      );
      return null;
    }

    // Immediate retry on initial disconnect
    if (retryContext.previousRetryCount === 0) {
      return 0;
    }

    // Exponential backoff capped at maxDelayMs (30s)
    // Clamp exponent to prevent numerical overflow on long-running retries
    const exponent = Math.min(retryContext.previousRetryCount - 1, 10);
    const exponential = this.initialDelayMs * Math.pow(2, exponent);
    const baseDelay = Math.min(exponential, this.maxDelayMs);

    // Random jitter (up to 1s) to desynchronize concurrent client reconnections
    const jitter = Math.random() * 1000;
    const finalDelay = Math.min(this.maxDelayMs, baseDelay + jitter);

    // Never return null so reconnect retries continue indefinitely
    return Math.round(finalDelay);
  }
}

class SignalRManager {
  private connection: signalR.HubConnection | null = null;
  private messageHandlers: Set<MessageHandler> = new Set();
  private reconnectingHandlers: Set<ReconnectingHandler> = new Set();
  private reconnectedHandlers: Set<ReconnectedHandler> = new Set();
  private closeHandlers: Set<CloseHandler> = new Set();
  private connectionStateHandlers: Set<ConnectionStateChangeHandler> =
    new Set();
  private statusListeners: Set<(status: ConnectionStatus) => void> = new Set();
  private snapshotListeners: Set<(snapshot: StateSnapshot) => void> = new Set();

  private activeTorrentSubscriptions = new Set<number>();
  private activeChannelSubscriptions = new Set<string>();

  private isStarting = false;
  private isStopped = false;
  private retryTimeout: ReturnType<typeof setTimeout> | null = null;
  private coldStartRetryCount = 0;

  public ensureConnection(): signalR.HubConnection {
    if (!this.connection) {
      const urlBase = getUrlBase();

      const apiKey = apiClient.getApiKey();
      const connectionOptions: signalR.IHttpConnectionOptions = {};
      if (apiKey && apiKey.trim().length > 0) {
        connectionOptions.accessTokenFactory = () =>
          apiClient.getApiKey() || "";
      }

      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(`${urlBase}/signalr/messages`, connectionOptions)
        .withAutomaticReconnect(new ExponentialBackoffRetryPolicy())
        .build();

      this.connection.on("receiveMessage", (message) => {
        for (const handler of this.messageHandlers) {
          try {
            handler(message);
          } catch (err) {
            console.error("Error in SignalR message handler:", err);
          }
        }
      });

      this.connection.on("stateSnapshot", (snapshot: StateSnapshot) => {
        if (snapshot) {
          this.notifySnapshot(snapshot);
        }
      });

      this.connection.onreconnecting((error) => {
        console.warn("SignalR connection reconnecting:", error);
        this.notifyConnectionChange(false);
        this.notifyStatus("reconnecting");
        for (const handler of this.reconnectingHandlers) {
          try {
            handler(error);
          } catch (err) {
            console.error("Error in SignalR onReconnecting handler:", err);
          }
        }
      });

      this.connection.onreconnected(async (connectionId) => {
        console.info("SignalR connection reconnected:", connectionId);
        this.coldStartRetryCount = 0;
        this.notifyConnectionChange(true);
        this.notifyStatus("connected");
        await this.resubscribeActiveGroups();
        await this.requestStateSnapshot();
        for (const handler of this.reconnectedHandlers) {
          try {
            handler(connectionId);
          } catch (err) {
            console.error("Error in SignalR onReconnected handler:", err);
          }
        }
      });

      this.connection.onclose((error) => {
        console.warn("SignalR connection closed:", error);
        this.notifyConnectionChange(false);
        this.notifyStatus("disconnected");
        for (const handler of this.closeHandlers) {
          try {
            handler(error);
          } catch (err) {
            console.error("Error in SignalR onClose handler:", err);
          }
        }

        // If connection closed unexpectedly and was not intentionally stopped,
        // continuously retry establishing the connection (unless unauthorized/forbidden)
        if (!this.isStopped && !isUnauthorizedError(error)) {
          this.scheduleColdStartRetry();
        }
      });
    }

    return this.connection;
  }

  public isConnected(): boolean {
    return this.connection?.state === signalR.HubConnectionState.Connected;
  }

  public getConnection(): signalR.HubConnection | null {
    return this.connection;
  }

  public getConnectionStatus(): ConnectionStatus {
    if (!this.connection) return "disconnected";
    switch (this.connection.state) {
      case signalR.HubConnectionState.Connected:
        return "connected";
      case signalR.HubConnectionState.Reconnecting:
        return "reconnecting";
      default:
        return "disconnected";
    }
  }

  public onReconnecting(cb: ReconnectingHandler): () => void {
    this.reconnectingHandlers.add(cb);
    return () => {
      this.reconnectingHandlers.delete(cb);
    };
  }

  public onReconnected(cb: ReconnectedHandler): () => void {
    this.reconnectedHandlers.add(cb);
    return () => {
      this.reconnectedHandlers.delete(cb);
    };
  }

  public onClose(cb: CloseHandler): () => void {
    this.closeHandlers.add(cb);
    return () => {
      this.closeHandlers.delete(cb);
    };
  }

  public onConnectionChange(cb: ConnectionStateChangeHandler): () => void {
    this.connectionStateHandlers.add(cb);
    return () => {
      this.connectionStateHandlers.delete(cb);
    };
  }

  public onStatusChange(cb: (status: ConnectionStatus) => void): () => void {
    this.statusListeners.add(cb);
    return () => {
      this.statusListeners.delete(cb);
    };
  }

  public onStateSnapshot(cb: (snapshot: StateSnapshot) => void): () => void {
    this.snapshotListeners.add(cb);
    return () => {
      this.snapshotListeners.delete(cb);
    };
  }

  public notifySnapshot(snapshot: StateSnapshot): void {
    for (const handler of this.snapshotListeners) {
      try {
        handler(snapshot);
      } catch (err) {
        console.error("Error in SignalR snapshot listener:", err);
      }
    }
  }

  public subscribe(handler: MessageHandler): () => void {
    this.messageHandlers.add(handler);
    return () => {
      this.messageHandlers.delete(handler);
    };
  }

  public async subscribeToTorrent(torrentId: number): Promise<void> {
    this.activeTorrentSubscriptions.add(torrentId);
    const conn = this.ensureConnection();
    if (conn.state === signalR.HubConnectionState.Connected) {
      try {
        await conn.invoke("SubscribeToTorrent", torrentId);
      } catch (err) {
        console.warn("Failed to subscribe to torrent:", torrentId, err);
      }
    }
  }

  public async unsubscribeFromTorrent(torrentId: number): Promise<void> {
    this.activeTorrentSubscriptions.delete(torrentId);
    const conn = this.ensureConnection();
    if (conn.state === signalR.HubConnectionState.Connected) {
      try {
        await conn.invoke("UnsubscribeFromTorrent", torrentId);
      } catch (err) {
        console.warn("Failed to unsubscribe from torrent:", torrentId, err);
      }
    }
  }

  public async subscribeToChannel(channel: string): Promise<void> {
    if (!channel) return;
    this.activeChannelSubscriptions.add(channel.toLowerCase());
    const conn = this.ensureConnection();
    if (conn.state === signalR.HubConnectionState.Connected) {
      try {
        await conn.invoke("SubscribeToChannel", channel);
      } catch (err) {
        console.warn("Failed to subscribe to channel:", channel, err);
      }
    }
  }

  public async unsubscribeFromChannel(channel: string): Promise<void> {
    if (!channel) return;
    this.activeChannelSubscriptions.delete(channel.toLowerCase());
    const conn = this.ensureConnection();
    if (conn.state === signalR.HubConnectionState.Connected) {
      try {
        await conn.invoke("UnsubscribeFromChannel", channel);
      } catch (err) {
        console.warn("Failed to unsubscribe from channel:", channel, err);
      }
    }
  }

  public async resubscribeActiveGroups(): Promise<void> {
    const conn = this.ensureConnection();
    if (conn.state !== signalR.HubConnectionState.Connected) return;

    for (const tid of this.activeTorrentSubscriptions) {
      try {
        await conn.invoke("SubscribeToTorrent", tid);
      } catch (err) {
        console.warn("Failed to resubscribe to torrent:", tid, err);
      }
    }

    for (const ch of this.activeChannelSubscriptions) {
      try {
        await conn.invoke("SubscribeToChannel", ch);
      } catch (err) {
        console.warn("Failed to resubscribe to channel:", ch, err);
      }
    }
  }

  public async requestStateSnapshot(): Promise<StateSnapshot | null> {
    const conn = this.ensureConnection();
    if (conn.state === signalR.HubConnectionState.Connected) {
      try {
        const snapshot = await conn.invoke<StateSnapshot>(
          "RequestStateSnapshot",
        );
        if (snapshot) {
          this.notifySnapshot(snapshot);
        }
        return snapshot;
      } catch (err) {
        console.warn("Failed to request state snapshot:", err);
      }
    }
    return null;
  }

  public async startWithRetry(): Promise<void> {
    this.isStopped = false;

    if (
      this.connection &&
      this.connection.state === signalR.HubConnectionState.Connected
    ) {
      this.notifyConnectionChange(true);
      this.notifyStatus("connected");
      return;
    }

    if (this.isStarting) {
      return;
    }

    const conn = this.ensureConnection();

    if (
      conn.state === signalR.HubConnectionState.Connecting ||
      conn.state === signalR.HubConnectionState.Reconnecting
    ) {
      return;
    }

    this.isStarting = true;

    try {
      if (conn.state === signalR.HubConnectionState.Disconnected) {
        await conn.start();
        console.info("SignalR connection established successfully");

        const wasRetrying = this.coldStartRetryCount > 0;
        this.coldStartRetryCount = 0;
        this.isStarting = false;

        if (this.retryTimeout) {
          clearTimeout(this.retryTimeout);
          this.retryTimeout = null;
        }

        this.notifyConnectionChange(true);
        this.notifyStatus("connected");
        await this.resubscribeActiveGroups();

        if (wasRetrying) {
          this.notifyReconnected(conn.connectionId || undefined);
        }
      } else {
        this.isStarting = false;
        if (conn.state === signalR.HubConnectionState.Connected) {
          this.notifyConnectionChange(true);
          this.notifyStatus("connected");
        }
      }
    } catch (err) {
      this.isStarting = false;
      this.notifyConnectionChange(false);
      this.notifyStatus("disconnected");

      const errorObj = err instanceof Error ? err : new Error(String(err));
      this.notifyReconnecting(errorObj);

      if (isUnauthorizedError(err)) {
        console.warn(
          "SignalR connection aborted due to 401/403 authorization failure:",
          err,
        );
        return;
      }

      console.warn("SignalR connection attempt failed, will retry:", err);
      this.scheduleColdStartRetry();
    }
  }

  public async start(): Promise<void> {
    return this.startWithRetry();
  }

  public async stop(): Promise<void> {
    this.isStopped = true;
    if (this.retryTimeout) {
      clearTimeout(this.retryTimeout);
      this.retryTimeout = null;
    }
    if (this.connection) {
      await this.connection.stop();
    }
    this.notifyConnectionChange(false);
    this.notifyStatus("disconnected");
  }

  private scheduleColdStartRetry(): void {
    if (this.isStopped) return;
    if (this.retryTimeout !== null) return;

    if (
      this.connection &&
      (this.connection.state === signalR.HubConnectionState.Connected ||
        this.connection.state === signalR.HubConnectionState.Connecting ||
        this.connection.state === signalR.HubConnectionState.Reconnecting)
    ) {
      return;
    }

    const exponent = Math.min(this.coldStartRetryCount, 10);
    const baseDelay = Math.min(1000 * Math.pow(2, exponent), 30000);
    const jitter = Math.random() * 1000;
    const delay = Math.min(30000, baseDelay + jitter);
    this.coldStartRetryCount++;

    this.retryTimeout = setTimeout(() => {
      this.retryTimeout = null;
      this.startWithRetry().catch((err) => {
        console.warn("Error during SignalR retry attempt:", err);
      });
    }, delay);
  }

  private notifyReconnecting(error?: Error): void {
    for (const handler of this.reconnectingHandlers) {
      try {
        handler(error);
      } catch (e) {
        console.error("Error in SignalR onReconnecting handler:", e);
      }
    }
  }

  private notifyReconnected(connectionId?: string): void {
    for (const handler of this.reconnectedHandlers) {
      try {
        handler(connectionId);
      } catch (e) {
        console.error("Error in SignalR onReconnected handler:", e);
      }
    }
  }

  private notifyConnectionChange(connected: boolean): void {
    for (const handler of this.connectionStateHandlers) {
      try {
        handler(connected);
      } catch (e) {
        console.error("Error in SignalR onConnectionChange handler:", e);
      }
    }
  }

  private notifyStatus(status: ConnectionStatus): void {
    for (const handler of this.statusListeners) {
      try {
        handler(status);
      } catch (e) {
        console.error("Error in SignalR status listener:", e);
      }
    }
  }
}

export const signalRManager = new SignalRManager();

export function getSignalRConnection(): signalR.HubConnection {
  return signalRManager.ensureConnection();
}

export function startSignalR(): Promise<void> {
  return signalRManager.startWithRetry();
}

export function stopSignalR(): Promise<void> {
  return signalRManager.stop();
}

export function reconnectSignalR(): Promise<void> {
  return signalRManager.startWithRetry();
}

export function subscribeToTorrent(torrentId: number): Promise<void> {
  return signalRManager.subscribeToTorrent(torrentId);
}

export function unsubscribeFromTorrent(torrentId: number): Promise<void> {
  return signalRManager.unsubscribeFromTorrent(torrentId);
}

export function subscribeToChannel(channel: string): Promise<void> {
  return signalRManager.subscribeToChannel(channel);
}

export function unsubscribeFromChannel(channel: string): Promise<void> {
  return signalRManager.unsubscribeFromChannel(channel);
}

export function requestStateSnapshot(): Promise<StateSnapshot | null> {
  return signalRManager.requestStateSnapshot();
}

export function onStateSnapshot(
  callback: (snapshot: StateSnapshot) => void,
): () => void {
  return signalRManager.onStateSnapshot(callback);
}

export function onSignalRMessage(
  action: string,
  callback: (data: unknown) => void,
): () => void {
  const conn = getSignalRConnection();
  conn.on(action, callback);
  return () => {
    conn.off(action, callback);
  };
}

export function useIsSignalRConnected(): boolean {
  const [connected, setConnected] = useState<boolean>(() =>
    signalRManager.isConnected(),
  );

  useEffect(() => {
    setConnected(signalRManager.isConnected());
    const unsub = signalRManager.onConnectionChange((isConnected) => {
      setConnected(isConnected);
    });
    return () => {
      unsub();
    };
  }, []);

  return connected;
}

export function useIsDocumentVisible(): boolean {
  const [visible, setVisible] = useState<boolean>(() =>
    typeof document !== "undefined"
      ? document.visibilityState === "visible"
      : true,
  );

  useEffect(() => {
    if (typeof document === "undefined") return;
    const handleVisibilityChange = () => {
      setVisible(document.visibilityState === "visible");
    };
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, []);

  return visible;
}

export function useSignalR(queryClient?: QueryClient) {
  const conn = getSignalRConnection();
  const [status, setStatus] = useState<ConnectionStatus>(() =>
    signalRManager.getConnectionStatus(),
  );
  const queryClientRef = useRef(queryClient);
  queryClientRef.current = queryClient;

  useEffect(() => {
    let isMounted = true;

    const handleStatusChange = (newStatus: ConnectionStatus) => {
      if (isMounted) {
        setStatus(newStatus);
      }
    };

    const unsub = signalRManager.onStatusChange(handleStatusChange);
    setStatus(signalRManager.getConnectionStatus());

    if (!signalRManager.isConnected()) {
      signalRManager.startWithRetry().catch(() => {});
    }

    return () => {
      isMounted = false;
      unsub();
    };
  }, []);

  return {
    connection: conn,
    status,
    connected: status === "connected",
    isReconnecting: status === "reconnecting",
    reconnect: reconnectSignalR,
    disconnect: stopSignalR,
    requestStateSnapshot,
    subscribeToTorrent,
    unsubscribeFromTorrent,
    subscribeToChannel,
    unsubscribeFromChannel,
  };
}
