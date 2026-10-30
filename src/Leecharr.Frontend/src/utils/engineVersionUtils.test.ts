import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { TorrentEngine, TorrentEngineCapabilities } from "../api/types";
import {
  resolveActiveEngineVersion,
  getSupportedEngineVersions,
  hasMultipleEngineVersions,
  getEngineVersionCapabilities,
  isEngineVersionActive,
  buildEngineProbeUrl,
  buildEngineSwitchUrl,
  formatSwitchEngineRequest,
} from "./engineVersionUtils";

function createMockCapabilities(
  overrides: Partial<TorrentEngineCapabilities> = {},
): TorrentEngineCapabilities {
  return {
    supportsUtp: true,
    supportsDht: true,
    supportsPex: true,
    supportsLpd: true,
    supportsV2Torrents: true,
    supportsSequentialDownload: true,
    supportsFastResume: true,
    supportsCustomPiecePickers: false,
    supportsDynamicRateLimits: true,
    supportsSparseAllocation: true,
    supportsMemoryMappedIo: true,
    supportsEncryptionToggle: true,
    ...overrides,
  };
}

function createMockEngine(
  overrides: Partial<TorrentEngine> = {},
): TorrentEngine {
  return {
    id: 1,
    engineId: "LibTorrent",
    displayName: "libtorrent (Rasterbar C++)",
    version: "2.1.1",
    activeVersion: "2.1.1",
    supportedVersions: ["1.2.20", "2.1.1"],
    isActive: true,
    isAvailable: true,
    status: "Running",
    description: "High performance BitTorrent engine",
    capabilities: createMockCapabilities(),
    versionCapabilities: {
      "1.2.20": createMockCapabilities({
        supportsV2Torrents: false,
        supportsMemoryMappedIo: false,
      }),
      "2.1.1": createMockCapabilities({
        supportsV2Torrents: true,
        supportsMemoryMappedIo: true,
      }),
    },
    warnings: [],
    ...overrides,
  };
}

void describe("engineVersionUtils", () => {
  void describe("resolveActiveEngineVersion", () => {
    void it("should return activeVersion when present", () => {
      const engine = createMockEngine({
        activeVersion: "2.1.1",
        version: "unknown",
      });
      assert.equal(resolveActiveEngineVersion(engine), "2.1.1");
    });

    void it("should fallback to version when activeVersion is undefined", () => {
      const engine = createMockEngine({
        activeVersion: undefined,
        version: "3.0.2",
      });
      assert.equal(resolveActiveEngineVersion(engine), "3.0.2");
    });

    void it("should return default fallback when neither is available", () => {
      const engine = createMockEngine({
        activeVersion: undefined,
        version: "",
      });
      assert.equal(resolveActiveEngineVersion(engine), "1.0.0");
    });
  });

  void describe("getSupportedEngineVersions", () => {
    void it("should return supportedVersions array when populated", () => {
      const engine = createMockEngine({
        supportedVersions: ["1.2.20", "2.1.1"],
      });
      assert.deepEqual(getSupportedEngineVersions(engine), ["1.2.20", "2.1.1"]);
    });

    void it("should return array with single version if supportedVersions is missing", () => {
      const engine = createMockEngine({
        supportedVersions: undefined,
        version: "4.0.5",
      });
      assert.deepEqual(getSupportedEngineVersions(engine), ["4.0.5"]);
    });

    void it("should return empty array if no versions available", () => {
      const engine = createMockEngine({ supportedVersions: [], version: "" });
      assert.deepEqual(getSupportedEngineVersions(engine), []);
    });
  });

  void describe("hasMultipleEngineVersions", () => {
    void it("should return true when multiple versions are supported", () => {
      const engine = createMockEngine({
        supportedVersions: ["1.2.20", "2.1.1"],
      });
      assert.equal(hasMultipleEngineVersions(engine), true);
    });

    void it("should return false when only one version is supported", () => {
      const engine = createMockEngine({ supportedVersions: ["3.0.2"] });
      assert.equal(hasMultipleEngineVersions(engine), false);
    });
  });

  void describe("getEngineVersionCapabilities", () => {
    void it("should resolve differential capabilities for version 1.2.20", () => {
      const engine = createMockEngine();
      const caps = getEngineVersionCapabilities(engine, "1.2.20");
      assert.equal(caps.supportsV2Torrents, false);
      assert.equal(caps.supportsMemoryMappedIo, false);
      assert.equal(caps.supportsUtp, true);
    });

    void it("should resolve differential capabilities for version 2.1.1", () => {
      const engine = createMockEngine();
      const caps = getEngineVersionCapabilities(engine, "2.1.1");
      assert.equal(caps.supportsV2Torrents, true);
      assert.equal(caps.supportsMemoryMappedIo, true);
      assert.equal(caps.supportsUtp, true);
    });

    void it("should fallback to base capabilities if requested version not in map", () => {
      const engine = createMockEngine();
      const caps = getEngineVersionCapabilities(engine, "nonexistent");
      assert.deepEqual(caps, engine.capabilities);
    });
  });

  void describe("isEngineVersionActive", () => {
    void it("should return true when engineId matches and version matches activeVersion", () => {
      const engine = createMockEngine({
        engineId: "LibTorrent",
        activeVersion: "2.1.1",
      });
      assert.equal(isEngineVersionActive(engine, "2.1.1", "libtorrent"), true);
    });

    void it("should return false when version does not match activeVersion", () => {
      const engine = createMockEngine({
        engineId: "LibTorrent",
        activeVersion: "2.1.1",
      });
      assert.equal(
        isEngineVersionActive(engine, "1.2.20", "libtorrent"),
        false,
      );
    });

    void it("should return false when engine is not active", () => {
      const engine = createMockEngine({
        engineId: "LibTorrent",
        activeVersion: "2.1.1",
      });
      assert.equal(
        isEngineVersionActive(engine, "2.1.1", "monotorrent"),
        false,
      );
    });
  });

  void describe("buildEngineProbeUrl", () => {
    void it("should include version query param when specified", () => {
      assert.equal(
        buildEngineProbeUrl("LibTorrent", "1.2.20"),
        "/torrentengine/LibTorrent/probe?version=1.2.20",
      );
    });

    void it("should omit version query param when not specified", () => {
      assert.equal(
        buildEngineProbeUrl("MonoTorrent"),
        "/torrentengine/MonoTorrent/probe",
      );
    });
  });

  void describe("buildEngineSwitchUrl", () => {
    void it("should include version query param when specified", () => {
      assert.equal(
        buildEngineSwitchUrl("2.1.1"),
        "/torrentengine/switch?version=2.1.1",
      );
    });

    void it("should omit query param when version is omitted", () => {
      assert.equal(buildEngineSwitchUrl(), "/torrentengine/switch");
    });
  });

  void describe("formatSwitchEngineRequest", () => {
    void it("should format request with engineId, version, and preserveTransfers", () => {
      const req = formatSwitchEngineRequest("LibTorrent", "1.2.20", true);
      assert.deepEqual(req, {
        engineId: "LibTorrent",
        version: "1.2.20",
        preserveTransfers: true,
      });
    });

    void it("should omit version property when not provided", () => {
      const req = formatSwitchEngineRequest("MonoTorrent");
      assert.deepEqual(req, {
        engineId: "MonoTorrent",
        version: undefined,
        preserveTransfers: true,
      });
    });
  });
});
