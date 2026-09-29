// Ensure localStorage exists in Node test environment before importing modules that touch it
if (typeof globalThis.localStorage === "undefined") {
  const store = new Map<string, string>();
  (globalThis as unknown as { localStorage: unknown }).localStorage = {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, val: string) => store.set(key, val),
    removeItem: (key: string) => store.delete(key),
    clear: () => store.clear(),
  };
}

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { TorrentFileInfo } from "../api/types";
import {
  computeFileBoundaries,
  getFileBlockColor,
  hexToRgba,
  FILE_PALETTE,
} from "./PieceMap";

describe("PieceMap File Boundaries and Colors (#1024)", () => {
  describe("computeFileBoundaries", () => {
    it("returns empty array when files is null, undefined, or empty", () => {
      assert.deepEqual(computeFileBoundaries(null, 16384), []);
      assert.deepEqual(computeFileBoundaries(undefined, 16384), []);
      assert.deepEqual(computeFileBoundaries([], 16384), []);
    });

    it("uses backend pieceOffset and pieceCount when present", () => {
      const files: TorrentFileInfo[] = [
        {
          id: 1,
          torrentId: 42,
          path: "Season 1/Episode 01.mkv",
          size: 1048576,
          pieceOffset: 0,
          pieceCount: 16,
        },
        {
          id: 2,
          torrentId: 42,
          path: "Season 1/Episode 02.mkv",
          size: 1048576,
          pieceOffset: 16,
          pieceCount: 16,
        },
        {
          id: 3,
          torrentId: 42,
          path: "Season 1/Sample.mkv",
          size: 65536,
          pieceOffset: 32,
          pieceCount: 1,
        },
      ];

      const boundaries = computeFileBoundaries(files, 65536);
      assert.strictEqual(boundaries.length, 3);

      assert.strictEqual(boundaries[0].startPiece, 0);
      assert.strictEqual(boundaries[0].endPiece, 15);
      assert.strictEqual(boundaries[0].colorIndex, 0);

      assert.strictEqual(boundaries[1].startPiece, 16);
      assert.strictEqual(boundaries[1].endPiece, 31);
      assert.strictEqual(boundaries[1].colorIndex, 1);

      assert.strictEqual(boundaries[2].startPiece, 32);
      assert.strictEqual(boundaries[2].endPiece, 32);
      assert.strictEqual(boundaries[2].colorIndex, 2);
    });

    it("handles non-contiguous or reordered files based on backend offsets", () => {
      const files: TorrentFileInfo[] = [
        {
          id: 2,
          torrentId: 42,
          path: "disc2.iso",
          size: 5000000,
          pieceOffset: 100,
          pieceCount: 50,
        },
        {
          id: 1,
          torrentId: 42,
          path: "disc1.iso",
          size: 5000000,
          pieceOffset: 0,
          pieceCount: 50,
        },
      ];

      const boundaries = computeFileBoundaries(files, 65536);
      assert.strictEqual(boundaries[0].startPiece, 100);
      assert.strictEqual(boundaries[0].endPiece, 149);

      assert.strictEqual(boundaries[1].startPiece, 0);
      assert.strictEqual(boundaries[1].endPiece, 49);
    });

    it("handles 0-count empty files correctly without crashing or negative ranges", () => {
      const files: TorrentFileInfo[] = [
        {
          id: 1,
          torrentId: 42,
          path: "empty.txt",
          size: 0,
          pieceOffset: 12,
          pieceCount: 0,
        },
      ];

      const boundaries = computeFileBoundaries(files, 65536);
      assert.strictEqual(boundaries[0].startPiece, 12);
      assert.strictEqual(boundaries[0].endPiece, 12);
    });

    it("falls back to sequential byte calculation when pieceOffset/pieceCount are missing", () => {
      const files: TorrentFileInfo[] = [
        {
          id: 1,
          torrentId: 42,
          path: "file1.bin",
          size: 32768,
          pieceOffset: undefined as unknown as number,
          pieceCount: undefined as unknown as number,
        },
        {
          id: 2,
          torrentId: 42,
          path: "file2.bin",
          size: 16384,
          pieceOffset: null as unknown as number,
          pieceCount: null as unknown as number,
        },
      ];

      const pieceLength = 16384;
      const boundaries = computeFileBoundaries(files, pieceLength);

      assert.strictEqual(boundaries[0].startByte, 0);
      assert.strictEqual(boundaries[0].endByte, 32768);
      assert.strictEqual(boundaries[0].startPiece, 0);
      assert.strictEqual(boundaries[0].endPiece, 1);

      assert.strictEqual(boundaries[1].startByte, 32768);
      assert.strictEqual(boundaries[1].endByte, 49152);
      assert.strictEqual(boundaries[1].startPiece, 2);
      assert.strictEqual(boundaries[1].endPiece, 2);
    });
  });

  describe("hexToRgba", () => {
    it("converts 6-digit hex color to rgba with specified alpha", () => {
      assert.strictEqual(hexToRgba("#3498db", 0.18), "rgba(52, 152, 219, 0.18)");
      assert.strictEqual(hexToRgba("#e74c3c", 1), "rgba(231, 76, 60, 1)");
    });

    it("converts 3-digit hex color to rgba", () => {
      assert.strictEqual(hexToRgba("#fff", 0.5), "rgba(255, 255, 255, 0.5)");
    });

    it("returns raw string if not starting with #", () => {
      assert.strictEqual(hexToRgba("rgb(1, 2, 3)", 0.5), "rgb(1, 2, 3)");
    });
  });

  describe("getFileBlockColor", () => {
    const fileColor = FILE_PALETTE[0]; // "#3498db"

    it("returns solid opaque color for completed blocks", () => {
      const resString = getFileBlockColor("complete", false, fileColor);
      assert.strictEqual(resString.fillColor, fileColor);
      assert.strictEqual(resString.strokeColor, fileColor);

      const resNumeric = getFileBlockColor(2, false, fileColor);
      assert.strictEqual(resNumeric.fillColor, fileColor);
      assert.strictEqual(resNumeric.strokeColor, fileColor);

      const resIsComplete = getFileBlockColor("missing", true, fileColor);
      assert.strictEqual(resIsComplete.fillColor, fileColor);
      assert.strictEqual(resIsComplete.strokeColor, fileColor);
    });

    it("dims missing blocks with low alpha while preserving file boundary stroke", () => {
      const resMissing = getFileBlockColor("missing", false, fileColor);
      assert.strictEqual(resMissing.fillColor, hexToRgba(fileColor, 0.18));
      assert.strictEqual(resMissing.strokeColor, hexToRgba(fileColor, 0.35));

      const resNumericZero = getFileBlockColor(0, false, fileColor);
      assert.strictEqual(resNumericZero.fillColor, hexToRgba(fileColor, 0.18));
      assert.strictEqual(resNumericZero.strokeColor, hexToRgba(fileColor, 0.35));
    });

    it("returns semi-transparent fill and solid stroke for active downloading blocks", () => {
      const resActive = getFileBlockColor("active", false, fileColor);
      assert.strictEqual(resActive.fillColor, hexToRgba(fileColor, 0.55));
      assert.strictEqual(resActive.strokeColor, fileColor);

      const resNumericOne = getFileBlockColor(1, false, fileColor);
      assert.strictEqual(resNumericOne.fillColor, hexToRgba(fileColor, 0.55));
      assert.strictEqual(resNumericOne.strokeColor, fileColor);
    });
  });
});
