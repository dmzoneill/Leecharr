import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Torrent, SeedingStats } from "../api/types";
import {
  calculateAchievements,
  calculateHnrStatus,
  calculateTrackerBuffers,
  getTorrentBadges,
} from "./milestones";
import { formatBytes, formatRatio } from "./formatters";

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Test Release",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 1024 * 1024 * 1024, // 1 GB
    pieceCount: 1024,
    pieceLength: 1024 * 1024,
    comment: null,
    createdBy: null,
    creationDate: null,
    isPrivate: false,
    status: "Seeding",
    uploaded: 0,
    downloaded: 0,
    ratio: 0,
    progress: 100,
    seeders: 5,
    leechers: 1,
    trackerUrl: "https://tracker.example.com/announce",
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
    uploadSpeed: 1024 * 1024,
    downloadSpeed: 0,
    active: true,
    availability: 1,
    eta: 0,
    sortOrder: 0,
    forceCompleted: false,
    seedingTime: 3600,
    ...overrides,
  };
}

describe("formatters: formatRatio and formatBytes", () => {
  it("formatRatio should return '-' for Infinity or NaN", () => {
    assert.equal(formatRatio(Infinity), "-");
    assert.equal(formatRatio(-Infinity), "-");
    assert.equal(formatRatio(NaN), "-");
  });

  it("formatRatio should format finite numbers to two decimals", () => {
    assert.equal(formatRatio(0), "0.00");
    assert.equal(formatRatio(1.5), "1.50");
    assert.equal(formatRatio(10.256), "10.26");
  });

  it("formatBytes should format negative values with minus prefix", () => {
    const hundredGb = 100 * 1024 * 1024 * 1024;
    assert.equal(formatBytes(-hundredGb), "-100.0 GB");
    assert.equal(formatBytes(hundredGb), "100.0 GB");
    assert.equal(formatBytes(0), "0 B");
    assert.equal(formatBytes(Infinity), "0 B");
    assert.equal(formatBytes(NaN), "0 B");
  });
});

describe("milestones: getTorrentBadges", () => {
  it("should not award ratio badges when ratio is Infinity", () => {
    const torrent = createMockTorrent({
      ratio: Infinity,
      downloaded: 0,
      uploaded: 50 * 1024 * 1024,
    });

    const badges = getTorrentBadges(torrent);
    const ratioBadges = badges.filter(
      (b) => b.title.includes("Ratio") || b.label.includes("x"),
    );

    assert.equal(ratioBadges.length, 0);
  });

  it("should not award ratio badges when ratio is NaN", () => {
    const torrent = createMockTorrent({
      ratio: NaN,
      downloaded: 0,
      uploaded: 0,
    });

    const badges = getTorrentBadges(torrent);
    const ratioBadges = badges.filter(
      (b) => b.title.includes("Ratio") || b.label.includes("x"),
    );

    assert.equal(ratioBadges.length, 0);
  });

  it("should not award ratio badges when traffic volume is below 10 MB threshold", () => {
    const torrent = createMockTorrent({
      ratio: 15.0, // High ratio but trivial traffic
      downloaded: 1024, // 1 KB
      uploaded: 15360, // 15 KB
    });

    const badges = getTorrentBadges(torrent);
    const ratioBadges = badges.filter(
      (b) => b.title.includes("Ratio") || b.label.includes("x"),
    );

    assert.equal(ratioBadges.length, 0);
  });

  it("should award Diamond ratio badge when ratio >= 10.0 and meaningful traffic exists", () => {
    const torrent = createMockTorrent({
      ratio: 12.5,
      downloaded: 100 * 1024 * 1024, // 100 MB
      uploaded: 1250 * 1024 * 1024, // 1.25 GB
    });

    const badges = getTorrentBadges(torrent);
    const diamondBadge = badges.find((b) => b.title.includes("Diamond Ratio"));

    assert.ok(diamondBadge, "Expected Diamond Ratio badge");
    assert.equal(diamondBadge.label, "12.5x");
    assert.equal(diamondBadge.title, "Diamond Ratio: 12.50");
  });

  it("should award Gold ratio badge when ratio >= 5.0 and < 10.0", () => {
    const torrent = createMockTorrent({
      ratio: 5.5,
      downloaded: 50 * 1024 * 1024,
      uploaded: 275 * 1024 * 1024,
    });

    const badges = getTorrentBadges(torrent);
    const goldBadge = badges.find((b) => b.title.includes("Gold Ratio"));

    assert.ok(goldBadge, "Expected Gold Ratio badge");
    assert.equal(goldBadge.label, "5.5x");
  });
});

describe("milestones: calculateAchievements", () => {
  it("should not unlock ratio achievements when library only has Infinity or NaN ratios", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        id: 1,
        ratio: Infinity,
        downloaded: 0,
        uploaded: 100 * 1024 * 1024,
      }),
      createMockTorrent({
        id: 2,
        ratio: NaN,
        downloaded: 0,
        uploaded: 0,
      }),
      createMockTorrent({
        id: 3,
        ratio: 50.0,
        downloaded: 100, // trivial traffic (< 10 MB)
        uploaded: 5000,
      }),
    ];

    const stats: SeedingStats = {
      totalUploaded: 100 * 1024 * 1024,
      totalDownloaded: 0,
      activeTorrents: 3,
      averageRatio: 0,
    };

    const achievements = calculateAchievements(torrents, stats);
    const ratioAchievements = achievements.badges.filter(
      (b) => b.category === "ratio",
    );

    for (const badge of ratioAchievements) {
      assert.equal(
        badge.isUnlocked,
        false,
        `Expected ${badge.id} to remain locked`,
      );
      assert.notEqual(badge.currentValueText, "Infinity");
      assert.notEqual(badge.currentValueText, "NaN");
      assert.equal(badge.currentValueText, "0.00");
    }
  });

  it("should unlock ratio achievements when qualifying torrent meets thresholds", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        id: 1,
        ratio: 10.5,
        downloaded: 50 * 1024 * 1024, // 50 MB (qualifies)
        uploaded: 525 * 1024 * 1024,
      }),
    ];

    const stats: SeedingStats = {
      totalUploaded: 525 * 1024 * 1024,
      totalDownloaded: 50 * 1024 * 1024,
      activeTorrents: 1,
      averageRatio: 10.5,
    };

    const achievements = calculateAchievements(torrents, stats);
    const ratio1 = achievements.badges.find((b) => b.id === "ratio_1");
    const ratio5 = achievements.badges.find((b) => b.id === "ratio_5");
    const ratio10 = achievements.badges.find((b) => b.id === "ratio_10");

    assert.ok(ratio1?.isUnlocked);
    assert.ok(ratio5?.isUnlocked);
    assert.ok(ratio10?.isUnlocked);
    assert.equal(ratio10?.currentValueText, "10.50");
  });
});

describe("milestones: calculateTrackerBuffers", () => {
  it("should calculate negative bufferBytes when downloaded exceeds uploaded (true deficit)", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        trackerUrl: "https://private-tracker.org/announce",
        uploaded: 50 * 1024 * 1024 * 1024, // 50 GB
        downloaded: 150 * 1024 * 1024 * 1024, // 150 GB
      }),
    ];

    const buffers = calculateTrackerBuffers(torrents);

    assert.equal(buffers.length, 1);
    const summary = buffers[0];
    assert.equal(summary.tracker, "private-tracker.org");
    assert.equal(
      summary.bufferBytes,
      -100 * 1024 * 1024 * 1024,
      "Expected -100 GB buffer deficit",
    );
    assert.ok(summary.bufferBytes < 0, "Buffer bytes must be negative");
    assert.equal(summary.ratio, 50 / 150);
  });

  it("should calculate positive bufferBytes when uploaded exceeds downloaded", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        trackerUrl: "https://private-tracker.org/announce",
        uploaded: 200 * 1024 * 1024 * 1024, // 200 GB
        downloaded: 50 * 1024 * 1024 * 1024, // 50 GB
      }),
    ];

    const buffers = calculateTrackerBuffers(torrents);

    assert.equal(buffers.length, 1);
    const summary = buffers[0];
    assert.equal(
      summary.bufferBytes,
      150 * 1024 * 1024 * 1024,
      "Expected +150 GB safe buffer",
    );
    assert.ok(summary.bufferBytes > 0);
  });

  it("should have zero bufferBytes when uploaded equals downloaded", () => {
    const torrents: Torrent[] = [
      createMockTorrent({
        trackerUrl: "https://tracker.net/announce",
        uploaded: 10 * 1024 * 1024 * 1024,
        downloaded: 10 * 1024 * 1024 * 1024,
      }),
    ];

    const buffers = calculateTrackerBuffers(torrents);

    assert.equal(buffers.length, 1);
    assert.equal(buffers[0].bufferBytes, 0);
  });
});

describe("milestones: calculateHnrStatus", () => {
  it("should not clear HNR when ratio is Infinity", () => {
    const torrent = createMockTorrent({
      isPrivate: true,
      status: "Seeding",
      progress: 1.0,
      ratio: Infinity,
      seedingTime: 1000, // < 72h
    });

    const status = calculateHnrStatus(torrent, 72);

    assert.equal(status.isCleared, false);
  });

  it("should return cleared for public torrents without HnR rules", () => {
    const torrent = createMockTorrent({
      isPrivate: false,
      seedingTime: 120,
    });

    const status = calculateHnrStatus(torrent, 72);

    assert.equal(status.isCleared, true);
    assert.equal(status.requiredSeconds, 0);
    assert.equal(status.progressPercent, 100);
    assert.equal(status.remainingSeconds, 0);
    assert.equal(status.label, "Public Swarm (No HnR rules)");
  });

  it("should not clear HNR for incomplete private torrent even with ratio >= 1.0", () => {
    const torrent = createMockTorrent({
      isPrivate: true,
      status: "Downloading",
      progress: 0.5,
      ratio: 1.5,
      seedingTime: 3600,
    });

    const status = calculateHnrStatus(torrent, 72);

    assert.equal(status.isCleared, false);
    assert.equal(status.progressPercent, 0);
    assert.equal(status.remainingSeconds, 72 * 3600);
    assert.equal(
      status.label,
      "Downloading (HnR timer starts after completion)",
    );
  });

  it("should clear HNR for completed private torrent with ratio >= 1.0", () => {
    const torrent = createMockTorrent({
      isPrivate: true,
      status: "Seeding",
      progress: 1.0,
      ratio: 1.2,
      seedingTime: 1000,
    });

    const status = calculateHnrStatus(torrent, 72);

    assert.equal(status.isCleared, true);
    assert.equal(status.progressPercent, 100);
    assert.equal(status.label, "Cleared (1.0+ Ratio)");
  });

  it("should clear HNR for completed private torrent with seed time met", () => {
    const torrent = createMockTorrent({
      isPrivate: true,
      status: "Seeding",
      progress: 1.0,
      ratio: 0.5,
      seedingTime: 72 * 3600,
    });

    const status = calculateHnrStatus(torrent, 72);

    assert.equal(status.isCleared, true);
    assert.equal(status.progressPercent, 100);
    assert.equal(status.label, "Cleared (72h Met)");
  });
});
