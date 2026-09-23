import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Torrent } from "../api/types";
import { filterTorrents } from "./filterUtils";

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Ubuntu.24.04.LTS.Desktop.iso",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 4_000_000_000,
    pieceCount: 1000,
    pieceLength: 4_000_000,
    comment: null,
    createdBy: null,
    creationDate: null,
    isPrivate: false,
    status: "Seeding",
    uploaded: 10_000_000_000,
    downloaded: 4_000_000_000,
    ratio: 2.5,
    progress: 1.0,
    seeders: 25,
    leechers: 2,
    trackerUrl: "https://tracker.ubuntu.com/announce",
    trackers:
      overrides.trackers ??
      (overrides.trackerUrl
        ? [overrides.trackerUrl]
        : ["https://tracker.ubuntu.com/announce"]),
    category: "Linux",
    label: "OS",
    sourcePath: null,
    dateAdded: "2026-01-01T00:00:00Z",
    lastActive: null,
    priority: 1,
    uploadLimit: 0,
    downloadLimit: 0,
    forceStart: false,
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
    forceCompleted: true,
    seedingTime: 86400,
    initialSeeding: false,
    ...overrides,
  };
}

describe("filterUtils: filterTorrents", () => {
  const torrents: Torrent[] = [
    createMockTorrent({
      id: 1,
      name: "Ubuntu 24.04 Desktop",
      status: "Seeding",
      trackerUrl: "https://tracker.ubuntu.com:6969/announce",
      category: "Linux",
      label: "ISO",
      isPrivate: false,
    }),
    createMockTorrent({
      id: 2,
      name: "Debian 12 Bookworm",
      status: "Stopped",
      trackerUrl: "udp://tracker.debian.org:1337/announce",
      category: "Linux",
      label: "Archive",
      isPrivate: true,
    }),
    createMockTorrent({
      id: 3,
      name: "Arch Linux 2026",
      status: "Seeding",
      trackerUrl: "https://tracker.archlinux.org/announce",
      category: "Arch",
      label: "ISO",
      isPrivate: false,
    }),
    createMockTorrent({
      id: 4,
      name: "FreeBSD 14.1 Release",
      status: "Queued",
      trackerUrl: null,
      trackers: [],
      category: null,
      label: null,
      isPrivate: true,
    }),
  ];

  it("should filter by search text query", () => {
    const result = filterTorrents(torrents, { filter: "debian" });
    assert.equal(result.length, 1);
    assert.equal(result[0].id, 2);
  });

  it("should filter by status state", () => {
    const seeding = filterTorrents(torrents, { stateFilter: "Seeding" });
    assert.equal(seeding.length, 2);
    assert.deepEqual(
      seeding.map((t) => t.id),
      [1, 3],
    );

    const all = filterTorrents(torrents, { stateFilter: "All" });
    assert.equal(all.length, 4);
  });

  it("should filter by tracker domain", () => {
    const result = filterTorrents(torrents, {
      trackerFilter: "tracker.ubuntu.com",
    });
    assert.equal(result.length, 1);
    assert.equal(result[0].id, 1);
  });

  it("should filter by privacy", () => {
    const priv = filterTorrents(torrents, { privacyFilter: "Private" });
    assert.equal(priv.length, 2);
    assert.deepEqual(
      priv.map((t) => t.id),
      [2, 4],
    );

    const pub = filterTorrents(torrents, { privacyFilter: "Public" });
    assert.equal(pub.length, 2);
    assert.deepEqual(
      pub.map((t) => t.id),
      [1, 3],
    );
  });

  it("should filter by category and handle Uncategorized", () => {
    const linux = filterTorrents(torrents, { categoryFilter: "Linux" });
    assert.equal(linux.length, 2);
    assert.deepEqual(
      linux.map((t) => t.id),
      [1, 2],
    );

    const uncat = filterTorrents(torrents, {
      categoryFilter: "Uncategorized",
    });
    assert.equal(uncat.length, 1);
    assert.equal(uncat[0].id, 4);
  });

  it("should filter by tag / label and handle Untagged", () => {
    const iso = filterTorrents(torrents, { tagFilter: "ISO" });
    assert.equal(iso.length, 2);
    assert.deepEqual(
      iso.map((t) => t.id),
      [1, 3],
    );

    const untagged = filterTorrents(torrents, { tagFilter: "Untagged" });
    assert.equal(untagged.length, 1);
    assert.equal(untagged[0].id, 4);
  });

  it("should apply multiple filter combinations concurrently", () => {
    const result = filterTorrents(torrents, {
      filter: "Linux",
      stateFilter: "Seeding",
      categoryFilter: "Arch",
      tagFilter: "ISO",
    });
    assert.equal(result.length, 1);
    assert.equal(result[0].id, 3);
  });

  it("should return empty array for empty or undefined torrent list", () => {
    assert.deepEqual(filterTorrents(undefined, {}), []);
    assert.deepEqual(filterTorrents([], { filter: "test" }), []);
  });

  describe("tag filtering with tagIds and matching semantics", () => {
    const multiTagTorrents: Torrent[] = [
      createMockTorrent({
        id: 10,
        name: "Movie.2024.4K.HDR",
        tagIds: [1, 2], // 1=4K, 2=HDR
        label: "4K",
      }),
      createMockTorrent({
        id: 20,
        name: "Movie.2024.1080p.HDR",
        tagIds: [2, 3], // 2=HDR, 3=1080p
        label: "1080p",
      }),
      createMockTorrent({
        id: 30,
        name: "Movie.2024.4K.SDR",
        tagIds: [1, 4], // 1=4K, 4=SDR
        label: "4K",
      }),
      createMockTorrent({
        id: 40,
        name: "Untagged Movie",
        tagIds: [],
        label: null,
      }),
      createMockTorrent({
        id: 50,
        name: "Legacy Movie",
        tagIds: undefined,
        label: "Anime",
      }),
    ];

    it("should filter by single tag ID", () => {
      const result = filterTorrents(multiTagTorrents, {
        selectedTagIds: [1],
      });
      assert.equal(result.length, 2);
      assert.deepEqual(
        result.map((t) => t.id),
        [10, 30],
      );
    });

    it("should filter by single tag ID using Set<number>", () => {
      const result = filterTorrents(multiTagTorrents, {
        selectedTagIds: new Set([3]),
      });
      assert.equal(result.length, 1);
      assert.equal(result[0].id, 20);
    });

    it("should filter by multiple tags using OR semantics (union / ANY)", () => {
      const result = filterTorrents(multiTagTorrents, {
        selectedTagIds: [1, 3],
        tagMatchMode: "OR",
      });
      assert.equal(result.length, 3);
      assert.deepEqual(
        result.map((t) => t.id),
        [10, 20, 30],
      );
    });

    it("should default to OR semantics when tagMatchMode is omitted", () => {
      const result = filterTorrents(multiTagTorrents, {
        selectedTagIds: [1, 3],
      });
      assert.equal(result.length, 3);
      assert.deepEqual(
        result.map((t) => t.id),
        [10, 20, 30],
      );
    });

    it("should filter by multiple tags using AND semantics (intersection / ALL)", () => {
      const result = filterTorrents(multiTagTorrents, {
        selectedTagIds: [1, 2],
        tagMatchMode: "AND",
      });
      assert.equal(result.length, 1);
      assert.equal(result[0].id, 10);
    });

    it("should return empty array if no torrent matches all tags in AND mode", () => {
      const result = filterTorrents(multiTagTorrents, {
        selectedTagIds: [1, 3],
        tagMatchMode: "AND",
      });
      assert.equal(result.length, 0);
    });

    it("should filter untagged torrents", () => {
      const result = filterTorrents(multiTagTorrents, {
        tagFilter: "Untagged",
      });
      assert.equal(result.length, 1);
      assert.equal(result[0].id, 40);
    });

    it("should maintain backward compatibility with legacy tagFilter string", () => {
      const result = filterTorrents(multiTagTorrents, {
        tagFilter: "Anime",
      });
      assert.equal(result.length, 1);
      assert.equal(result[0].id, 50);
    });

    it("should ignore empty selectedTagIds and fall back to tagFilter", () => {
      const result = filterTorrents(multiTagTorrents, {
        selectedTagIds: [],
        tagFilter: "Anime",
      });
      assert.equal(result.length, 1);
      assert.equal(result[0].id, 50);
    });
  });
});
