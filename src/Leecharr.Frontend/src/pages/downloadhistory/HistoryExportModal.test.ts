import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { DownloadHistoryEntry } from "../../api/types";
import { generateExportData } from "./HistoryExportModal";

function createMockHistoryEntry(
  overrides: Partial<DownloadHistoryEntry> = {},
): DownloadHistoryEntry {
  return {
    id: 1,
    torrentId: 10,
    title: "Ubuntu.24.04.LTS.Desktop.iso",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 4_000_000_000,
    uploaded: 8_000_000_000,
    downloaded: 4_000_000_000,
    ratio: 2.0,
    seedingTime: 3600,
    primaryTracker: "https://tracker.ubuntu.com/announce",
    indexerName: "UbuntuTracker",
    source: "Manual",
    magnetUrl:
      "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu",
    downloadUrl: null,
    status: "Completed",
    removalReason: null,
    isPrivate: false,
    dateAdded: "2026-01-01T12:00:00Z",
    dateCompleted: "2026-01-01T13:00:00Z",
    dateRemoved: null,
    dataJson: null,
    ...overrides,
  };
}

describe("HistoryExportModal: generateExportData", () => {
  it("should return empty string for empty items array", () => {
    assert.strictEqual(generateExportData([], "json"), "");
    assert.strictEqual(generateExportData([], "csv"), "");
    assert.strictEqual(generateExportData([], "txt"), "");
  });

  describe("JSON Export", () => {
    it("should export full metadata including Downloaded, IndexerName, IsPrivate, and MagnetUrl", () => {
      const entry = createMockHistoryEntry({
        downloaded: 4_000_000_000,
        indexerName: "MyIndexer",
        isPrivate: true,
        magnetUrl: "magnet:?xt=urn:btih:test1234",
      });

      const jsonStr = generateExportData([entry], "json");
      const parsed = JSON.parse(jsonStr);

      assert.strictEqual(parsed.length, 1);
      assert.strictEqual(parsed[0].id, 1);
      assert.strictEqual(parsed[0].title, "Ubuntu.24.04.LTS.Desktop.iso");
      assert.strictEqual(parsed[0].downloaded, 4_000_000_000);
      assert.strictEqual(parsed[0].indexerName, "MyIndexer");
      assert.strictEqual(parsed[0].isPrivate, true);
      assert.strictEqual(parsed[0].magnetUrl, "magnet:?xt=urn:btih:test1234");
    });
  });

  describe("CSV Export", () => {
    it("should include Downloaded, Indexer, IsPrivate, and MagnetUrl headers", () => {
      const entry = createMockHistoryEntry();
      const csv = generateExportData([entry], "csv");
      const [headerLine] = csv.split("\n");
      const headers = headerLine.split(",");

      assert.ok(headers.includes("Downloaded"), "Missing Downloaded header");
      assert.ok(headers.includes("Indexer"), "Missing Indexer header");
      assert.ok(headers.includes("IsPrivate"), "Missing IsPrivate header");
      assert.ok(headers.includes("MagnetUrl"), "Missing MagnetUrl header");

      const expectedHeaders = [
        "ID",
        "Title",
        "InfoHash",
        "TotalSize",
        "FormattedSize",
        "Uploaded",
        "Downloaded",
        "Ratio",
        "SeedingTime",
        "Source",
        "PrimaryTracker",
        "Indexer",
        "IsPrivate",
        "MagnetUrl",
        "Status",
        "DateAdded",
        "DateCompleted",
        "DateRemoved",
      ];
      assert.deepStrictEqual(headers, expectedHeaders);
    });

    it("should populate row fields matching the new headers", () => {
      const entry = createMockHistoryEntry({
        id: 42,
        downloaded: 5_242_880,
        indexerName: "ProwlarrIndex",
        isPrivate: true,
        magnetUrl: "magnet:?xt=urn:btih:hash42",
      });

      const csv = generateExportData([entry], "csv");
      const lines = csv.split("\n");
      assert.strictEqual(lines.length, 2);

      const row = lines[1];
      assert.ok(
        row.includes("5242880"),
        "Row should include downloaded volume",
      );
      assert.ok(row.includes('"ProwlarrIndex"'), "Row should include indexer");
      assert.ok(row.includes("true"), "Row should include isPrivate boolean");
      assert.ok(
        row.includes('"magnet:?xt=urn:btih:hash42"'),
        "Row should include magnet url",
      );
    });
  });

  describe("TXT Export", () => {
    it("should export clean text listing title, infoHash, and magnetUrl", () => {
      const entry = createMockHistoryEntry({
        title: "ArchLinux-2026.iso",
        infoHash: "deadbeef1234567890deadbeef1234567890dead",
        magnetUrl:
          "magnet:?xt=urn:btih:deadbeef1234567890deadbeef1234567890dead&dn=ArchLinux",
      });

      const txt = generateExportData([entry], "txt");
      const lines = txt.split("\n");

      assert.strictEqual(lines[0], "Title: ArchLinux-2026.iso");
      assert.strictEqual(
        lines[1],
        "InfoHash: deadbeef1234567890deadbeef1234567890dead",
      );
      assert.strictEqual(
        lines[2],
        "Magnet: magnet:?xt=urn:btih:deadbeef1234567890deadbeef1234567890dead&dn=ArchLinux",
      );
    });

    it("should omit Magnet line when magnetUrl is not available", () => {
      const entry = createMockHistoryEntry({
        title: "Debian-12.iso",
        infoHash: "cafebabe1234567890cafebabe1234567890cafebabe",
        magnetUrl: null,
      });

      const txt = generateExportData([entry], "txt");
      const lines = txt.split("\n");

      assert.strictEqual(lines.length, 2);
      assert.strictEqual(lines[0], "Title: Debian-12.iso");
      assert.strictEqual(
        lines[1],
        "InfoHash: cafebabe1234567890cafebabe1234567890cafebabe",
      );
    });

    it("should separate multiple items with empty lines", () => {
      const entry1 = createMockHistoryEntry({
        id: 1,
        title: "Item 1",
        infoHash: "hash1",
        magnetUrl: "magnet:1",
      });
      const entry2 = createMockHistoryEntry({
        id: 2,
        title: "Item 2",
        infoHash: "hash2",
        magnetUrl: "magnet:2",
      });

      const txt = generateExportData([entry1, entry2], "txt");
      const blocks = txt.split("\n\n");

      assert.strictEqual(blocks.length, 2);
      assert.ok(blocks[0].includes("Title: Item 1"));
      assert.ok(blocks[1].includes("Title: Item 2"));
    });
  });
});
