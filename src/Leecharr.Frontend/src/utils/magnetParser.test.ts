import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { validateTorrentFile, parseMagnetUri } from "./magnetParser";

describe("magnetParser: validateTorrentFile", () => {
  it("should accept valid .torrent files", () => {
    const result = validateTorrentFile({
      name: "ubuntu-22.04.iso.torrent",
      size: 15420,
    });
    assert.deepEqual(result, { valid: true });
  });

  it("should accept uppercase .TORRENT files", () => {
    const result = validateTorrentFile({
      name: "ARCHLINUX.TORRENT",
      size: 50000,
    });
    assert.deepEqual(result, { valid: true });
  });

  it("should reject files without .torrent extension", () => {
    const result1 = validateTorrentFile({
      name: "malicious.exe",
      size: 1024,
    });
    assert.deepEqual(result1, {
      valid: false,
      error: "File must have a .torrent extension",
    });

    const result2 = validateTorrentFile({
      name: "ubuntu.iso",
      size: 50000,
    });
    assert.deepEqual(result2, {
      valid: false,
      error: "File must have a .torrent extension",
    });
  });

  it("should reject 0-byte torrent files", () => {
    const result = validateTorrentFile({
      name: "empty.torrent",
      size: 0,
    });
    assert.deepEqual(result, {
      valid: false,
      error: "Torrent file cannot be empty (0 bytes)",
    });
  });

  it("should reject torrent files exceeding 10 MB limit", () => {
    const elevenMb = 11 * 1024 * 1024;
    const result = validateTorrentFile({
      name: "massive.torrent",
      size: elevenMb,
    });
    assert.deepEqual(result, {
      valid: false,
      error: "Torrent file exceeds maximum allowed size of 10 MB",
    });
  });
});

describe("magnetParser: parseMagnetUri", () => {
  it("should parse standard v1 hex (40 chars) magnet link", () => {
    const uri =
      "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu+22.04&tr=http%3A%2F%2Ftracker.example.com%2Fannounce";
    const result = parseMagnetUri(uri);

    assert.equal(result.valid, true);
    assert.equal(result.infoHash, "0123456789abcdef0123456789abcdef01234567");
    assert.equal(result.name, "Ubuntu 22.04");
    assert.deepEqual(result.trackers, ["http://tracker.example.com/announce"]);
    assert.equal(result.isV2, false);
  });

  it("should parse v2 multihash (64 chars urn:btmh:) magnet link", () => {
    const v2Hash =
      "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    const uri = `magnet:?xt=urn:btmh:1220${v2Hash}&dn=BEP52+V2+Release&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce`;
    const result = parseMagnetUri(uri);

    assert.equal(result.valid, true);
    assert.equal(result.infoHash, v2Hash);
    assert.equal(result.name, "BEP52 V2 Release");
    assert.deepEqual(result.trackers, [
      "udp://tracker.opentrackr.org:1337/announce",
    ]);
    assert.equal(result.isV2, true);
  });

  it("should parse base32 (32 chars) v1 magnet link", () => {
    const base32Hash = "4XGBA443T33O3L4U3Y4D6OEXWMSJ6Q5Z";
    const uri = `magnet:?xt=urn:btih:${base32Hash}&dn=Base32+Release`;
    const result = parseMagnetUri(uri);

    assert.equal(result.valid, true);
    assert.equal(result.infoHash, base32Hash);
    assert.equal(result.name, "Base32 Release");
    assert.equal(result.isV2, false);
  });

  it("should correctly decode display names with special characters and percent encoding", () => {
    const uri =
      "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Debian%20GNU%2FLinux%2012%20%5BBookworm%5D";
    const result = parseMagnetUri(uri);

    assert.equal(result.valid, true);
    assert.equal(result.name, "Debian GNU/Linux 12 [Bookworm]");
  });

  it("should handle invalid URIs and non-magnet links gracefully", () => {
    assert.deepEqual(parseMagnetUri(""), { valid: false, trackers: [] });
    assert.deepEqual(parseMagnetUri("http://example.com/file.torrent"), {
      valid: false,
      trackers: [],
    });
    assert.deepEqual(parseMagnetUri("magnet:?dn=NoHashTorrent"), {
      valid: false,
      infoHash: undefined,
      name: "NoHashTorrent",
      trackers: [],
      isV2: false,
    });
  });
});
