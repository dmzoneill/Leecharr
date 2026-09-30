// Ensure localStorage and window exist in Node test environment before importing modules that touch them
if (typeof globalThis.localStorage === "undefined") {
  const store = new Map<string, string>();
  (globalThis as unknown as { localStorage: unknown }).localStorage = {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, val: string) => store.set(key, val),
    removeItem: (key: string) => store.delete(key),
    clear: () => store.clear(),
  };
}

if (typeof globalThis.window === "undefined") {
  (globalThis as unknown as { window: unknown }).window = {
    location: {
      origin: "http://localhost:3000",
      pathname: "/",
      protocol: "http:",
      host: "localhost:3000",
    },
  };
}

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  EVENT_INVALIDATION_MAP,
  RECONNECT_QUERY_KEYS,
  isHandledByNamedEvent,
} from "./SignalRProvider";
import { signalRManager } from "../api/signalr";
import * as signalR from "@microsoft/signalr";

describe("SignalRProvider Event Invalidation and Handling (#1022)", () => {
  it("does not map speedPulse in EVENT_INVALIDATION_MAP to prevent 1-second query invalidation storm", () => {
    // speedPulse is high-frequency ephemeral telemetry updated directly in useTorrentStore
    // It should NEVER trigger REST query invalidations against /api/v1/seeding/stats
    assert.equal(
      "speedPulse" in EVENT_INVALIDATION_MAP,
      false,
      "speedPulse should not be present in EVENT_INVALIDATION_MAP"
    );
    assert.equal(
      "speed_update" in EVENT_INVALIDATION_MAP,
      false,
      "speed_update should not be present in EVENT_INVALIDATION_MAP"
    );
  });

  it("maps SeedingStatsUpdated to invalidating seeding stats", () => {
    // Persistent seeding stats changes should still invalidate seeding stats queries
    assert.deepEqual(EVENT_INVALIDATION_MAP.SeedingStatsUpdated, [
      ["seeding", "stats"],
    ]);
  });

  it("contains necessary entity invalidation mappings", () => {
    assert.deepEqual(EVENT_INVALIDATION_MAP.TorrentAdded, [
      ["torrents"],
      ["trackerboost"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.TorrentUpdated, [
      ["torrents"],
      ["trackerboost"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.TorrentDeleted, [
      ["torrents"],
      ["trackerboost"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.TorrentRecheckProgress, [
      ["torrents"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.HealthCheckCompleted, [["health"]]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.TrackerUpdated, [
      ["torrents"],
      ["trackerboost"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.TrackerAnnounced, [
      ["torrents"],
      ["trackerboost"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.TrackerAnnounceEvent, [
      ["torrents"],
      ["trackerboost"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.speedscheduleAdded, [
      ["speedschedule"],
      ["speedschedule", "active"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.speedscheduleUpdated, [
      ["speedschedule"],
      ["speedschedule", "active"],
    ]);
    assert.deepEqual(EVENT_INVALIDATION_MAP.speedscheduleDeleted, [
      ["speedschedule"],
      ["speedschedule", "active"],
    ]);
  });

  it("RECONNECT_QUERY_KEYS includes seeding stats and torrents for reconnect resync", () => {
    const hasSeedingStats = RECONNECT_QUERY_KEYS.some(
      (key) => key.length === 2 && key[0] === "seeding" && key[1] === "stats"
    );
    const hasTorrents = RECONNECT_QUERY_KEYS.some(
      (key) => key.length === 1 && key[0] === "torrents"
    );
    assert.equal(hasSeedingStats, true);
    assert.equal(hasTorrents, true);
  });

  it("isHandledByNamedEvent correctly classifies events", () => {
    assert.equal(isHandledByNamedEvent("speedPulse"), true);
    assert.equal(isHandledByNamedEvent("speed_update"), true);
    assert.equal(isHandledByNamedEvent("TorrentAdded"), true);
    assert.equal(isHandledByNamedEvent("SeedingStatsUpdated"), true);
    assert.equal(isHandledByNamedEvent("HealthCheckCompleted"), true);
    assert.equal(isHandledByNamedEvent("CommandStarted"), true);
    assert.equal(isHandledByNamedEvent("TaskCompleted"), true);
    assert.equal(isHandledByNamedEvent("TrackerAnnounced"), true);
    assert.equal(isHandledByNamedEvent("tracker"), false);
    assert.equal(isHandledByNamedEvent("seeding"), false);
    assert.equal(isHandledByNamedEvent("speedschedule"), false);
    assert.equal(isHandledByNamedEvent("schedule"), false);
    assert.equal(isHandledByNamedEvent("unknownCustomEvent"), false);
    assert.equal(isHandledByNamedEvent(""), false);
    assert.equal(isHandledByNamedEvent(undefined), false);
  });
});

describe("SignalRProvider and signalRManager Reconnection Cache Synchronization (#1023)", () => {
  it("registers onReconnected handler with signalRManager and unsubscribes properly", () => {
    let callCount = 0;
    const unsub = signalRManager.onReconnected(() => {
      callCount++;
    });

    assert.equal(typeof unsub, "function");
    unsub();
  });

  it("invalidates all RECONNECT_QUERY_KEYS on reconnection", () => {
    const invalidatedKeys: Array<unknown> = [];
    const mockQueryClient = {
      invalidateQueries: ({ queryKey }: { queryKey: readonly unknown[] }) => {
        invalidatedKeys.push(queryKey);
      },
    };

    const handleReconnected = () => {
      for (const key of RECONNECT_QUERY_KEYS) {
        mockQueryClient.invalidateQueries({ queryKey: key });
      }
    };

    const unsub = signalRManager.onReconnected(handleReconnected);

    // Simulate reconnected event dispatch via private notifyReconnected
    (
      signalRManager as unknown as {
        notifyReconnected: (id?: string) => void;
      }
    ).notifyReconnected("conn-1");

    assert.equal(invalidatedKeys.length, RECONNECT_QUERY_KEYS.length);
    for (const key of RECONNECT_QUERY_KEYS) {
      assert.ok(
        invalidatedKeys.includes(key),
        `Expected ${JSON.stringify(key)} to be invalidated`
      );
    }

    unsub();
  });

  it("startWithRetry requests stateSnapshot and triggers onReconnected on reconnection even when coldStartRetryCount is 0", async () => {
    let snapshotRequested = false;
    let reconnectedConnectionId: string | undefined;

    const mockConn = {
      state: signalR.HubConnectionState.Disconnected,
      connectionId: "test-conn-456",
      start: async () => {
        mockConn.state = signalR.HubConnectionState.Connected;
      },
      invoke: async (method: string) => {
        if (method === "RequestStateSnapshot") {
          snapshotRequested = true;
          return null;
        }
        return null;
      },
      on: () => {},
      off: () => {},
      onreconnected: () => {},
      onreconnecting: () => {},
      onclose: () => {},
    };

    const originalConn = (
      signalRManager as unknown as { connection: unknown }
    ).connection;
    (signalRManager as unknown as { connection: unknown }).connection =
      mockConn;
    (signalRManager as unknown as { isStarting: boolean }).isStarting = false;
    (signalRManager as unknown as { isStopped: boolean }).isStopped = false;
    (
      signalRManager as unknown as { coldStartRetryCount: number }
    ).coldStartRetryCount = 0;

    const unsub = signalRManager.onReconnected((id) => {
      reconnectedConnectionId = id;
    });

    try {
      await signalRManager.startWithRetry();

      assert.equal(
        snapshotRequested,
        true,
        "requestStateSnapshot should be called on reconnection"
      );
      assert.equal(
        reconnectedConnectionId,
        "test-conn-456",
        "notifyReconnected should be called on reconnection even with coldStartRetryCount = 0"
      );
    } finally {
      unsub();
      (signalRManager as unknown as { connection: unknown }).connection =
        originalConn;
    }
  });
});
