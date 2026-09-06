import * as signalR from "@microsoft/signalr";

export type MessageHandler = (message: {
  name: string;
  body: unknown;
  action?: number;
}) => void;

export type ReconnectingHandler = (error?: Error) => void;
export type ReconnectedHandler = (connectionId?: string) => void;
export type CloseHandler = (error?: Error) => void;

/**
 * Resilient SignalR retry policy implementing exponential backoff with jitter up to 30 seconds,
 * continuing indefinitely without permanently giving up.
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

  private isStarting = false;
  private isStopped = false;
  private retryTimeout: ReturnType<typeof setTimeout> | null = null;
  private coldStartRetryCount = 0;

  private ensureConnection(): signalR.HubConnection {
    if (!this.connection) {
      const urlBase =
        typeof window !== "undefined" && (window as any).Leecharr?.urlBase
          ? (window as any).Leecharr.urlBase.replace(/\/+$/, "")
          : "";

      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(`${urlBase}/signalr/messages`)
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

      this.connection.onreconnecting((error) => {
        console.warn("SignalR connection reconnecting:", error);
        for (const handler of this.reconnectingHandlers) {
          try {
            handler(error);
          } catch (err) {
            console.error("Error in SignalR onReconnecting handler:", err);
          }
        }
      });

      this.connection.onreconnected((connectionId) => {
        console.info("SignalR connection reconnected:", connectionId);
        this.coldStartRetryCount = 0;
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
        for (const handler of this.closeHandlers) {
          try {
            handler(error);
          } catch (err) {
            console.error("Error in SignalR onClose handler:", err);
          }
        }

        // If connection closed unexpectedly and was not intentionally stopped,
        // continuously retry establishing the connection
        if (!this.isStopped) {
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

  public subscribe(handler: MessageHandler): () => void {
    this.messageHandlers.add(handler);
    return () => {
      this.messageHandlers.delete(handler);
    };
  }

  public async startWithRetry(): Promise<void> {
    this.isStopped = false;

    if (
      this.connection &&
      this.connection.state === signalR.HubConnectionState.Connected
    ) {
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

        if (wasRetrying) {
          this.notifyReconnected(conn.connectionId || undefined);
        }
      } else {
        this.isStarting = false;
      }
    } catch (err) {
      console.warn("SignalR connection attempt failed, will retry:", err);
      this.isStarting = false;

      const errorObj = err instanceof Error ? err : new Error(String(err));
      this.notifyReconnecting(errorObj);

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
}

export const signalRManager = new SignalRManager();
