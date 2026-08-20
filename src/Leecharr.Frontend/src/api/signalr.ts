import * as signalR from "@microsoft/signalr";

type MessageHandler = (message: {
  name: string;
  body: unknown;
  action?: number;
}) => void;

class SignalRManager {
  private connection: signalR.HubConnection | null = null;
  private handlers: Set<MessageHandler> = new Set();

  public start() {
    if (this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl("/signalr/messages")
      .withAutomaticReconnect()
      .build();

    this.connection.on("receiveMessage", (message) => {
      for (const handler of this.handlers) {
        handler(message);
      }
    });

    this.connection.start().catch((err) => {
      console.warn("SignalR Connection Error:", err);
    });
  }

  public subscribe(handler: MessageHandler) {
    this.handlers.add(handler);
    return () => {
      this.handlers.delete(handler);
    };
  }
}

export const signalRManager = new SignalRManager();
