import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { parseMagnetPreview } from "./MagnetInputTab";

describe("MagnetInputTab: parseMagnetPreview (#1015)", () => {
  it("parses lowercase magnet:? URI", () => {
    const uri =
      "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu+22.04&tr=http%3A%2F%2Ftracker.example.com%2Fannounce";
    const preview = parseMagnetPreview(uri);
    assert.ok(preview);
    assert.equal(preview.name, "Ubuntu 22.04");
    assert.equal(preview.hash, "0123456789abcdef0123456789abcdef01234567");
    assert.equal(preview.trackerCount, 1);
  });

  it("parses uppercase MAGNET:? URI per RFC 3986", () => {
    const uri =
      "MAGNET:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu+22.04&tr=http%3A%2F%2Ftracker.example.com%2Fannounce";
    const preview = parseMagnetPreview(uri);
    assert.ok(preview);
    assert.equal(preview.name, "Ubuntu 22.04");
    assert.equal(preview.hash, "0123456789abcdef0123456789abcdef01234567");
    assert.equal(preview.trackerCount, 1);
  });

  it("parses mixed-case Magnet:? URI", () => {
    const uri =
      "Magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Test+Torrent";
    const preview = parseMagnetPreview(uri);
    assert.ok(preview);
    assert.equal(preview.name, "Test Torrent");
    assert.equal(preview.hash, "0123456789abcdef0123456789abcdef01234567");
    assert.equal(preview.trackerCount, 0);
  });

  it("returns null for non-magnet URIs", () => {
    assert.equal(parseMagnetPreview("http://example.com/test.torrent"), null);
    assert.equal(parseMagnetPreview("https://example.com"), null);
    assert.equal(parseMagnetPreview(""), null);
    assert.equal(parseMagnetPreview("   "), null);
  });

  it("handles leading and trailing whitespace with uppercase scheme", () => {
    const uri =
      "   MAGNET:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Cleaned   ";
    const preview = parseMagnetPreview(uri);
    assert.ok(preview);
    assert.equal(preview.name, "Cleaned");
    assert.equal(preview.hash, "0123456789abcdef0123456789abcdef01234567");
  });
});
