import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { normalizeUrlBase } from "./HostSettingsTab";

void describe("HostSettingsTab - UrlBase sanitization (#999)", () => {
  void it("strips trailing slash from urlBase", () => {
    assert.strictEqual(normalizeUrlBase("/leecharr/"), "/leecharr");
    assert.strictEqual(normalizeUrlBase("/leecharr///"), "/leecharr");
  });

  void it("ensures leading slash when missing", () => {
    assert.strictEqual(normalizeUrlBase("leecharr"), "/leecharr");
    assert.strictEqual(normalizeUrlBase("leecharr/"), "/leecharr");
  });

  void it("handles root slash or empty string by returning empty string", () => {
    assert.strictEqual(normalizeUrlBase("/"), "");
    assert.strictEqual(normalizeUrlBase(""), "");
    assert.strictEqual(normalizeUrlBase("   "), "");
    assert.strictEqual(normalizeUrlBase("///"), "");
  });

  void it("preserves nested paths without trailing slashes", () => {
    assert.strictEqual(normalizeUrlBase("/apps/leecharr/"), "/apps/leecharr");
    assert.strictEqual(normalizeUrlBase("apps/leecharr"), "/apps/leecharr");
    assert.strictEqual(normalizeUrlBase("/nested/sub/path/"), "/nested/sub/path");
  });
});
