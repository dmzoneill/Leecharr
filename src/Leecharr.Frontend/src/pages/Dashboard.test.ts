import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Torrent } from "../api/types";
import {
  calculateCompletedLibrarySize,
  calculateTotalLibrarySize,
  getActiveSpeedScheduleMode,
  formatSpeedScheduleLimit,
} from "./Dashboard";

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Test Torrent",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 1024 * 1024 * 1024,
    pieceCount: 1024,
    pieceLength: 1024 * 1024,
    comment: null,
    createdBy: null,
    creationDate: null,
    isPrivate: false,
    status: "Downloading",
    uploaded: 0,
    downloaded: 0,
    ratio: 0,
    progress: 0,
    seeders: 0,
    leechers: 0,
    trackerUrl: null,
    sourcePath: null,
    dateAdded: "2026-01-01T00:00:00Z",
    lastActive: null,
    priority: 0,
    uploadLimit: 0,
    downloadLimit: 0,
    forceStart: false,
    initialSeeding: false,
    label: null,
    sequentialDownload: false,
    announceInterval: 1800,
    nextUpdate: 1800,
    sessionUploaded: 0,
    sessionDownloaded: 0,
    smallTorrentLimit: 0,
    threshold: 0,
    uploadSpeed: 0,
    downloadSpeed: 0,
    active: true,
    availability: 1,
    eta: 0,
    sortOrder: 0,
    forceCompleted: false,
    ...overrides,
  };
}

describe("Dashboard Library Size Calculations (#994)", () => {
  it("returns 0 for empty torrent list", () => {
    assert.strictEqual(calculateCompletedLibrarySize([]), 0);
    assert.strictEqual(calculateTotalLibrarySize([]), 0);
  });

  it("calculates completed library size for seeding and completed torrents", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        id: 1,
        totalSize: 500_000_000,
        status: "Seeding",
        progress: 1.0,
      }),
      createMockTorrent({
        id: 2,
        totalSize: 300_000_000,
        status: "Completed",
        progress: 1.0,
      }),
      createMockTorrent({
        id: 3,
        totalSize: 200_000_000,
        status: "Downloading",
        progress: 0.4,
      }),
    ];

    assert.strictEqual(calculateCompletedLibrarySize(torrents), 800_000_000);
    assert.strictEqual(calculateTotalLibrarySize(torrents), 800_000_000);
  });

  it("falls back to totalSize of library if no torrents are completed yet", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        id: 1,
        totalSize: 500_000_000,
        status: "Downloading",
        progress: 0.1,
      }),
      createMockTorrent({
        id: 2,
        totalSize: 300_000_000,
        status: "Downloading",
        progress: 0.2,
      }),
    ];

    assert.strictEqual(calculateCompletedLibrarySize(torrents), 0);
    assert.strictEqual(calculateTotalLibrarySize(torrents), 800_000_000);
  });

  it("recognizes completed torrents by dateCompleted or progress >= 1", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        id: 1,
        totalSize: 400_000_000,
        status: "Paused",
        progress: 1.0,
      }),
      createMockTorrent({
        id: 2,
        totalSize: 600_000_000,
        status: "Stopped",
        progress: 0.0,
        dateCompleted: "2026-01-02T00:00:00Z",
      }),
    ];

    assert.strictEqual(calculateCompletedLibrarySize(torrents), 1_000_000_000);
    assert.strictEqual(calculateTotalLibrarySize(torrents), 1_000_000_000);
  });
});

describe("Dashboard Speed Schedule Status Card (#992)", () => {
  it("displays 'Paused' on active mode badge when isPaused is true", () => {
    assert.strictEqual(
      getActiveSpeedScheduleMode({ isPaused: true, isThrottled: true }),
      "Paused",
    );
    assert.strictEqual(
      getActiveSpeedScheduleMode({ isPaused: true, isThrottled: false }),
      "Paused",
    );
  });

  it("displays 'Mode Throttled' when throttled and not paused", () => {
    assert.strictEqual(
      getActiveSpeedScheduleMode({ isPaused: false, isThrottled: true }),
      "Mode Throttled",
    );
  });

  it("displays 'Mode Scheduled' when scheduler is enabled and not throttled/paused", () => {
    assert.strictEqual(
      getActiveSpeedScheduleMode({ isPaused: false, isThrottled: false }, true),
      "Mode Scheduled",
    );
  });

  it("displays 'Mode Normal' when no active schedule/throttle/pause", () => {
    assert.strictEqual(
      getActiveSpeedScheduleMode(
        { isPaused: false, isThrottled: false },
        false,
      ),
      "Mode Normal",
    );
    assert.strictEqual(getActiveSpeedScheduleMode(null, false), "Mode Normal");
  });

  it("formats speed limits as 'Paused' when direction is paused", () => {
    assert.strictEqual(formatSpeedScheduleLimit(true, 0), "Paused");
    assert.strictEqual(formatSpeedScheduleLimit(true, 1024), "Paused");
  });

  it("formats speed limits with speed when direction is active and limit > 0", () => {
    assert.strictEqual(formatSpeedScheduleLimit(false, 2048), "2048 KB/s");
  });

  it("formats speed limits as 'Unlimited' when direction is not paused and limit is 0 or null", () => {
    assert.strictEqual(formatSpeedScheduleLimit(false, 0), "Unlimited");
    assert.strictEqual(formatSpeedScheduleLimit(false, null), "Unlimited");
    assert.strictEqual(formatSpeedScheduleLimit(false, undefined), "Unlimited");
  });
});
