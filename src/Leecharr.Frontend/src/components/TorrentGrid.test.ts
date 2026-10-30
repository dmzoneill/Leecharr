import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  computeRangeSelection,
  computeEffectiveContextMenuSelection,
} from "./TorrentGrid";

void describe("TorrentGrid Selection and Context Menu (#1010)", () => {
  void describe("computeRangeSelection", () => {
    const torrentIds = [10, 20, 30, 40, 50];

    void it("selects forward range correctly", () => {
      const result = computeRangeSelection(torrentIds, 1, 3);
      assert.deepEqual(result, [20, 30, 40]);
    });

    void it("selects backward range correctly when anchor is after target", () => {
      const result = computeRangeSelection(torrentIds, 4, 2);
      assert.deepEqual(result, [30, 40, 50]);
    });

    void it("selects single item when anchor equals target", () => {
      const result = computeRangeSelection(torrentIds, 2, 2);
      assert.deepEqual(result, [30]);
    });
  });

  void describe("computeEffectiveContextMenuSelection", () => {
    void it("preserves multi-selection when right-clicking already selected torrent", () => {
      const selected = new Set([10, 20, 30]);
      const effective = computeEffectiveContextMenuSelection(selected, 20);
      assert.deepEqual(Array.from(effective), [10, 20, 30]);
    });

    void it("replaces selection with clicked torrent when not currently selected", () => {
      const selected = new Set([10, 20]);
      const effective = computeEffectiveContextMenuSelection(selected, 30);
      assert.deepEqual(Array.from(effective), [30]);
    });

    void it("returns existing selected IDs unchanged when right-clicking container background", () => {
      const selected = new Set([10, 20]);
      const effective = computeEffectiveContextMenuSelection(selected, null);
      assert.deepEqual(Array.from(effective), [10, 20]);
    });
  });
});
