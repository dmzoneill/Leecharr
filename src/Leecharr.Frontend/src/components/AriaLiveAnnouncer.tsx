import { useState, useEffect, useRef } from "react";
import { useTorrents } from "../api/hooks";
import type { Torrent } from "../api/types";
import { useTranslation } from "../i18n";

export type AriaPriority = "polite" | "assertive";

export interface AriaAnnouncement {
  id: number;
  message: string;
  priority: AriaPriority;
}

type AnnounceListener = (announcement: AriaAnnouncement) => void;

let announcementCounter = 0;
const listeners = new Set<AnnounceListener>();

/**
 * Dispatches an announcement to screen reader live regions.
 */
export function announce(
  message: string,
  priority: AriaPriority = "polite",
): void {
  const trimmed = message?.trim();
  if (!trimmed) return;

  const item: AriaAnnouncement = {
    id: ++announcementCounter,
    message: trimmed,
    priority,
  };
  listeners.forEach((listener) => listener(item));
}

/**
 * Clears all registered announcement listeners.
 */
export function clearListeners(): void {
  listeners.clear();
}

/**
 * Hook to access screen reader announcement dispatcher.
 */
export function useAriaAnnouncer() {
  return { announce };
}

export interface AnnouncementQueueOptions {
  /** Duration in ms to display message when no further messages are queued (default 5000ms) */
  displayDuration?: number;
  /** Duration in ms to display message when other messages are waiting in queue (default 3000ms) */
  queuedDuration?: number;
  /** Gap in ms between consecutive announcements to ensure screen reader detects text mutation (default 150ms) */
  transitionGap?: number;
  /** Callback fired whenever the currently active announcement text changes */
  onMessageChange?: (message: string) => void;
}

/**
 * Message queue manager for ARIA live announcements.
 * Guarantees concurrent announcements are displayed sequentially without dropped messages.
 */
export class AnnouncementQueue {
  private queue: string[] = [];
  private currentMessage: string = "";
  private isProcessing: boolean = false;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private gapTimer: ReturnType<typeof setTimeout> | null = null;
  private displayDuration: number;
  private queuedDuration: number;
  private transitionGap: number;
  private onMessageChange: (message: string) => void;

  constructor(options?: AnnouncementQueueOptions) {
    this.displayDuration = options?.displayDuration ?? 5000;
    this.queuedDuration = options?.queuedDuration ?? 3000;
    this.transitionGap = options?.transitionGap ?? 150;
    this.onMessageChange = options?.onMessageChange ?? (() => {});
  }

  public setOptions(options: Partial<AnnouncementQueueOptions>): void {
    if (options.displayDuration !== undefined) {
      this.displayDuration = options.displayDuration;
    }
    if (options.queuedDuration !== undefined) {
      this.queuedDuration = options.queuedDuration;
    }
    if (options.transitionGap !== undefined) {
      this.transitionGap = options.transitionGap;
    }
    if (options.onMessageChange !== undefined) {
      this.onMessageChange = options.onMessageChange;
    }
  }

  public enqueue(message: string): void {
    const trimmed = message?.trim();
    if (!trimmed) return;

    this.queue.push(trimmed);
    if (!this.isProcessing) {
      this.processNext();
    }
  }

  public getCurrentMessage(): string {
    return this.currentMessage;
  }

  public getQueue(): string[] {
    return [...this.queue];
  }

  public getQueueLength(): number {
    return this.queue.length;
  }

  public isBusy(): boolean {
    return this.isProcessing;
  }

  private processNext(): void {
    if (this.queue.length === 0) {
      this.isProcessing = false;
      this.currentMessage = "";
      this.onMessageChange("");
      return;
    }

    this.isProcessing = true;
    const nextMessage = this.queue.shift()!;
    this.currentMessage = nextMessage;
    this.onMessageChange(nextMessage);

    const duration =
      this.queue.length >= 4
        ? Math.max(1500, Math.floor(this.queuedDuration / 2))
        : this.queue.length > 0
          ? this.queuedDuration
          : this.displayDuration;

    this.timer = setTimeout(() => {
      this.timer = null;
      if (this.queue.length > 0) {
        // Blank briefly so assistive technologies register text mutation
        this.currentMessage = "";
        this.onMessageChange("");
        this.gapTimer = setTimeout(() => {
          this.gapTimer = null;
          this.processNext();
        }, this.transitionGap);
      } else {
        this.processNext();
      }
    }, duration);
  }

  public clear(): void {
    this.queue = [];
    this.currentMessage = "";
    this.isProcessing = false;
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    if (this.gapTimer) {
      clearTimeout(this.gapTimer);
      this.gapTimer = null;
    }
    this.onMessageChange("");
  }
}

export interface AriaLiveAnnouncerProps {
  displayDuration?: number;
  queuedDuration?: number;
  transitionGap?: number;
}

/**
 * Screen Reader Telemetry Announcer component.
 * Renders visually hidden ARIA live regions and connects to torrent state transitions.
 */
export default function AriaLiveAnnouncer({
  displayDuration = 5000,
  queuedDuration = 3000,
  transitionGap = 150,
}: AriaLiveAnnouncerProps = {}) {
  const { t } = useTranslation();
  const [politeMessage, setPoliteMessage] = useState<string>("");
  const [assertiveMessage, setAssertiveMessage] = useState<string>("");

  const { data: torrents } = useTorrents();
  const prevTorrentsRef = useRef<Map<number, Torrent> | null>(null);

  const politeQueueRef = useRef<AnnouncementQueue | null>(null);
  const assertiveQueueRef = useRef<AnnouncementQueue | null>(null);

  if (!politeQueueRef.current) {
    politeQueueRef.current = new AnnouncementQueue({
      displayDuration,
      queuedDuration,
      transitionGap,
      onMessageChange: (msg) => setPoliteMessage(msg),
    });
  }

  if (!assertiveQueueRef.current) {
    assertiveQueueRef.current = new AnnouncementQueue({
      displayDuration,
      queuedDuration,
      transitionGap,
      onMessageChange: (msg) => setAssertiveMessage(msg),
    });
  }

  useEffect(() => {
    politeQueueRef.current?.setOptions({
      displayDuration,
      queuedDuration,
      transitionGap,
      onMessageChange: setPoliteMessage,
    });
    assertiveQueueRef.current?.setOptions({
      displayDuration,
      queuedDuration,
      transitionGap,
      onMessageChange: setAssertiveMessage,
    });
  }, [displayDuration, queuedDuration, transitionGap]);

  useEffect(() => {
    const politeQueue = politeQueueRef.current!;
    const assertiveQueue = assertiveQueueRef.current!;

    const handleAnnounce: AnnounceListener = (item) => {
      if (item.priority === "assertive") {
        assertiveQueue.enqueue(item.message);
      } else {
        politeQueue.enqueue(item.message);
      }
    };

    listeners.add(handleAnnounce);
    return () => {
      listeners.delete(handleAnnounce);
      politeQueue.clear();
      assertiveQueue.clear();
    };
  }, []);

  // Monitor torrent state transitions
  useEffect(() => {
    if (!torrents) return;

    if (!prevTorrentsRef.current) {
      // First observation: seed the state map without triggering spurious announcements
      prevTorrentsRef.current = new Map(torrents.map((t) => [t.id, t]));
      return;
    }

    const prevMap = prevTorrentsRef.current;
    for (const torrent of torrents) {
      const prev = prevMap.get(torrent.id);
      if (!prev) continue;

      // 1. Torrent completed / now seeding transition
      const wasDownloading = prev.status === "Downloading";
      const isCompletedOrSeeding =
        torrent.status === "Completed" || torrent.status === "Seeding";

      if (wasDownloading && isCompletedOrSeeding) {
        const defaultCompleted = `Torrent "${torrent.name}" completed, now seeding`;
        const completedMsg =
          t(
            "ariaLive.torrentCompleted",
            { name: torrent.name },
            defaultCompleted,
          ) ||
          t(
            "aria.torrentCompleted",
            { name: torrent.name },
            defaultCompleted,
          );
        announce(completedMsg, "polite");
      }

      // 2. Torrent error transition
      const prevError = (prev as unknown as { errorMessage?: string })
        .errorMessage;
      const currError = (torrent as unknown as { errorMessage?: string })
        .errorMessage;
      const hadError = prev.status === "Error" || Boolean(prevError);
      const hasError =
        torrent.status === "Error" || (Boolean(currError) && !prevError);

      if (!hadError && hasError) {
        const defaultInterrupted = "download interrupted";
        const fallbackError =
          t("ariaLive.downloadInterrupted", defaultInterrupted) ||
          t("aria.downloadInterrupted", defaultInterrupted);
        const errorDetail = currError || fallbackError;
        const defaultError = `Torrent "${torrent.name}" encountered error: ${errorDetail}`;
        const errorMsg =
          t(
            "ariaLive.torrentError",
            { name: torrent.name, error: errorDetail },
            defaultError,
          ) ||
          t(
            "aria.torrentError",
            { name: torrent.name, error: errorDetail },
            defaultError,
          );
        announce(errorMsg, "assertive");
      }
    }

    prevTorrentsRef.current = new Map(torrents.map((t) => [t.id, t]));
  }, [torrents, t]);

  return (
    <div className="sr-only" aria-hidden="false">
      <div
        role="status"
        aria-live="polite"
        aria-atomic="true"
        className="sr-only"
      >
        {politeMessage}
      </div>
      <div
        role="alert"
        aria-live="assertive"
        aria-atomic="true"
        className="sr-only"
      >
        {assertiveMessage}
      </div>
    </div>
  );
}
