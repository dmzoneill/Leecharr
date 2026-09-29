const storageMock = {
  getItem: () => null,
  setItem: () => {},
  removeItem: () => {},
  clear: () => {},
  key: () => null,
  length: 0,
};
Object.assign(globalThis, {
  localStorage: storageMock,
  window: { localStorage: storageMock },
});

import { describe, it } from "node:test";
import assert from "node:assert/strict";

// Require after polyfilling localStorage so ApiClient initialization succeeds
// eslint-disable-next-line @typescript-eslint/no-require-imports
const { GLOBAL_CONN_OPTIONS, PER_TORRENT_CONN_OPTIONS } = require("./NetworkSwarmCard");

describe("NetworkSwarmCard connection options", () => {
  it("defines standard connection presets", () => {
    assert.deepStrictEqual(GLOBAL_CONN_OPTIONS, [100, 200, 300, 500, 1000]);
    assert.deepStrictEqual(PER_TORRENT_CONN_OPTIONS, [20, 50, 80, 100, 200]);
  });

  it("identifies custom global connection limits not in preset list", () => {
    const customLimits = [150, 250, 400, 600, 800, 1500];
    for (const limit of customLimits) {
      assert.strictEqual(
        GLOBAL_CONN_OPTIONS.includes(limit),
        false,
        `Expected ${limit} to be recognized as custom global limit`,
      );
    }
  });

  it("identifies preset global connection limits", () => {
    for (const preset of GLOBAL_CONN_OPTIONS) {
      assert.strictEqual(
        GLOBAL_CONN_OPTIONS.includes(preset),
        true,
        `Expected ${preset} to be recognized as preset global limit`,
      );
    }
  });

  it("identifies custom per-torrent connection limits not in preset list", () => {
    const customLimits = [10, 30, 60, 150, 250];
    for (const limit of customLimits) {
      assert.strictEqual(
        PER_TORRENT_CONN_OPTIONS.includes(limit),
        false,
        `Expected ${limit} to be recognized as custom per-torrent limit`,
      );
    }
  });

  it("identifies preset per-torrent connection limits", () => {
    for (const preset of PER_TORRENT_CONN_OPTIONS) {
      assert.strictEqual(
        PER_TORRENT_CONN_OPTIONS.includes(preset),
        true,
        `Expected ${preset} to be recognized as preset per-torrent limit`,
      );
    }
  });
});
