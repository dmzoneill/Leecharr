import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { parseIndexerIds } from "./IndexersTab";

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
