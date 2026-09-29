import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { parseIndexerIds, normalizeIndexerPayload } from "./IndexersTab";

describe("IndexersTab - parseIndexerIds (#997)", () => {
  it("parses single integer id", () => {
    assert.deepStrictEqual(parseIndexerIds("1"), [1]);
  });

  it("parses comma-separated ids with spaces", () => {
    assert.deepStrictEqual(parseIndexerIds("1, 2, 3"), [1, 2, 3]);
  });

  it("parses comma-separated ids without spaces", () => {
    assert.deepStrictEqual(parseIndexerIds("1,2,3"), [1, 2, 3]);
  });

  it("handles trailing comma while typing without dropping valid ids", () => {
    assert.deepStrictEqual(parseIndexerIds("1,"), [1]);
    assert.deepStrictEqual(parseIndexerIds("1, 2,"), [1, 2]);
  });

  it("handles leading and trailing whitespace", () => {
    assert.deepStrictEqual(parseIndexerIds("   1 ,  2 , 3   "), [1, 2, 3]);
  });

  it("filters out invalid tokens, zeros, and negative numbers", () => {
    assert.deepStrictEqual(parseIndexerIds("0, -1, foo, 42, bar, 99"), [42, 99]);
  });

  it("returns empty array for empty or whitespace-only input", () => {
    assert.deepStrictEqual(parseIndexerIds(""), []);
    assert.deepStrictEqual(parseIndexerIds("   "), []);
    assert.deepStrictEqual(parseIndexerIds(",,,"), []);
  });
});

describe("IndexersTab - normalizeIndexerPayload (#988)", () => {
  it("normalizes Prowlarr indexer payload and removes api path suffix", () => {
    const payload = normalizeIndexerPayload({
      name: "My Prowlarr",
      url: "http://localhost:9696/api/v1/",
      indexerType: "Prowlarr",
    });
    assert.strictEqual(payload.url, "http://localhost:9696");
    assert.strictEqual(payload.name, "My Prowlarr");
    assert.strictEqual(payload.implementation, "ProwlarrIndexer");
  });

  it("sets default Prowlarr name and implementation if omitted", () => {
    const payload = normalizeIndexerPayload({
      url: "http://localhost:9696",
      indexerType: "Prowlarr",
    });
    assert.strictEqual(payload.name, "Prowlarr");
    assert.strictEqual(payload.implementation, "ProwlarrIndexer");
  });
});
