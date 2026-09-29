import { describe, it, afterEach } from "node:test";
import assert from "node:assert/strict";
import {
  updateTorrentCategory,
  getPackageExportUrl,
  buildMagnetLink,
} from "./TorrentContextMenu";
import { copyToClipboard } from "../utils/clipboard";
import type { Torrent } from "../api/types";

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Sample Torrent",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 1000000,
    pieceCount: 100,
    pieceLength: 10000,
    comment: null,
    createdBy: null,
    creationDate: null,
    isPrivate: false,
    status: "seeding",
    uploaded: 500,
    downloaded: 1000,
    ratio: 0.5,
    progress: 100,
    seeders: 5,
    leechers: 2,
    trackerUrl: "http://tracker.example.com/announce",
    sourcePath: null,
    dateAdded: "2026-01-01T00:00:00Z",
    lastActive: null,
    priority: 1,
    uploadLimit: 0,
    downloadLimit: 0,
    initialSeeding: false,
    forceStart: false,
    label: "tag1, tag2",
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
    category: "initial-category",
    ...overrides,
  };
}

describe("TorrentContextMenu (#1008)", () => {
  describe("updateTorrentCategory", () => {
    it("updates category while preserving existing label / tags", () => {
      const torrent = createMockTorrent({
        category: "old-category",
        label: "important-tag, high-priority",
      });

      const updated = updateTorrentCategory(torrent, "movies");

      assert.strictEqual(updated.category, "movies");
      assert.strictEqual(updated.label, "important-tag, high-priority");
      assert.strictEqual(updated.id, torrent.id);
      assert.strictEqual(updated.name, torrent.name);
    });

    it("trims whitespace from category without clobbering label", () => {
      const torrent = createMockTorrent({
        label: "preserve-me",
      });

      const updated = updateTorrentCategory(torrent, "   tv-series   ");

      assert.strictEqual(updated.category, "tv-series");
      assert.strictEqual(updated.label, "preserve-me");
    });

    it("clearing category preserves existing tags", () => {
      const torrent = createMockTorrent({
        category: "movies",
        label: "tagA, tagB",
      });

      const updated = updateTorrentCategory(torrent, "");

      assert.strictEqual(updated.category, "");
      assert.strictEqual(updated.label, "tagA, tagB");
    });
  });

  describe("getPackageExportUrl", () => {
    const originalWindow = globalThis.window;

    afterEach(() => {
      globalThis.window = originalWindow;
    });

    it("generates correct export URL without urlBase", () => {
      (globalThis as unknown as { window: unknown }).window = {
        Leecharr: { urlBase: "" },
      };
      const url = getPackageExportUrl([1, 2, 3]);
      assert.strictEqual(url, "/api/v1/packages/export?torrentIds=1,2,3");
    });

    it("prefixes export URL with getUrlBase() when running on reverse proxy subpath", () => {
      (globalThis as unknown as { window: unknown }).window = {
        Leecharr: { urlBase: "/leecharr" },
      };
      const url = getPackageExportUrl([42]);
      assert.strictEqual(url, "/leecharr/api/v1/packages/export?torrentIds=42");
    });

    it("handles trailing slashes in urlBase correctly", () => {
      (globalThis as unknown as { window: unknown }).window = {
        Leecharr: { urlBase: "/subpath/" },
      };
      const url = getPackageExportUrl([10, 20]);
      assert.strictEqual(
        url,
        "/subpath/api/v1/packages/export?torrentIds=10,20",
      );
    });
  });

  describe("buildMagnetLink", () => {
    it("creates standard 40-char v1 magnet link", () => {
      const torrent = createMockTorrent({
        name: "Test Linux ISO",
        infoHash: "da39a3ee5e6b4b0d3255bfef95601890afd80709",
        trackerUrl: "udp://tracker.openbittorrent.com:80",
      });
      const magnet = buildMagnetLink(torrent);
      assert.ok(
        magnet.startsWith(
          "magnet:?xt=urn:btih:da39a3ee5e6b4b0d3255bfef95601890afd80709",
        ),
      );
      assert.ok(magnet.includes("dn=Test%20Linux%20ISO"));
      assert.ok(
        magnet.includes("tr=udp%3A%2F%2Ftracker.openbittorrent.com%3A80"),
      );
    });
  });

  describe("copyToClipboard safe fallback", () => {
    it("does not throw when navigator.clipboard is undefined (HTTP LAN context)", async () => {
      const originalNavigator = globalThis.navigator;
      try {
        // Simulate non-secure HTTP context where navigator.clipboard is undefined
        (globalThis as unknown as { navigator: unknown }).navigator = {};
        // Should safely return without throwing unhandled TypeError
        const result = await copyToClipboard("test-content");
        assert.strictEqual(typeof result, "boolean");
      } finally {
        globalThis.navigator = originalNavigator;
      }
    });
  });
});
