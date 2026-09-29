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
    assert.equal(isHandledByNamedEvent("unknownCustomEvent"), false);
    assert.equal(isHandledByNamedEvent(""), false);
    assert.equal(isHandledByNamedEvent(undefined), false);
  });
});
