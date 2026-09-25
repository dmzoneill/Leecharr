import { useState, useEffect, useRef } from "react";
import { useTorrents } from "../api/hooks";
import type { Torrent } from "../api/types";

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
  const item: AriaAnnouncement = {
    id: ++announcementCounter,
    message,
    priority,
  };
  listeners.forEach((listener) => listener(item));
}

/**
 * Hook to access screen reader announcement dispatcher.
 */
export function useAriaAnnouncer() {
  return { announce };
}

/**
 * Screen Reader Telemetry Announcer component.
 * Renders visually hidden ARIA live regions and connects to torrent state transitions.
 */
export default function AriaLiveAnnouncer() {
  const [politeMessage, setPoliteMessage] = useState<string>("");
  const [assertiveMessage, setAssertiveMessage] = useState<string>("");
  const politeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const assertiveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const { data: torrents } = useTorrents();
  const prevTorrentsRef = useRef<Map<number, Torrent> | null>(null);

  useEffect(() => {
    const handleAnnounce: AnnounceListener = (item) => {
      if (item.priority === "assertive") {
        if (assertiveTimerRef.current) {
          clearTimeout(assertiveTimerRef.current);
        }
        setAssertiveMessage(item.message);
        assertiveTimerRef.current = setTimeout(() => {
          setAssertiveMessage("");
        }, 7000);
      } else {
        if (politeTimerRef.current) {
          clearTimeout(politeTimerRef.current);
        }
        setPoliteMessage(item.message);
        politeTimerRef.current = setTimeout(() => {
          setPoliteMessage("");
        }, 7000);
      }
    };

    listeners.add(handleAnnounce);
    return () => {
      listeners.delete(handleAnnounce);
      if (politeTimerRef.current) clearTimeout(politeTimerRef.current);
      if (assertiveTimerRef.current) clearTimeout(assertiveTimerRef.current);
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
        announce(`Torrent "${torrent.name}" completed, now seeding`, "polite");
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
        const errorDetail = currError || "download interrupted";
        announce(
          `Torrent "${torrent.name}" encountered error: ${errorDetail}`,
          "assertive",
        );
      }
    }

    prevTorrentsRef.current = new Map(torrents.map((t) => [t.id, t]));
  }, [torrents]);

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
