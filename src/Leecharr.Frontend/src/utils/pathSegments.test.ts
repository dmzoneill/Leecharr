import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { getPathSegments } from "./pathSegments";

describe("getPathSegments", () => {
  it("returns an empty array for empty, undefined, or whitespace paths", () => {
    assert.deepEqual(getPathSegments(""), []);
    assert.deepEqual(getPathSegments(undefined as unknown as string), []);
    assert.deepEqual(getPathSegments("/"), []);
  });

  describe("Unix paths", () => {
    it("parses single folder path", () => {
      const segments = getPathSegments("/downloads");
      assert.deepEqual(segments, [
        { label: "downloads", fullPath: "/downloads" },
      ]);
    });

    it("parses multi-level Unix paths cumulatively", () => {
      const segments = getPathSegments("/downloads/media/movies");
      assert.deepEqual(segments, [
        { label: "downloads", fullPath: "/downloads" },
        { label: "media", fullPath: "/downloads/media" },
        { label: "movies", fullPath: "/downloads/media/movies" },
      ]);
    });

    it("handles trailing and consecutive slashes", () => {
      const segments = getPathSegments("//downloads///media//");
      assert.deepEqual(segments, [
        { label: "downloads", fullPath: "/downloads" },
        { label: "media", fullPath: "/downloads/media" },
      ]);
    });
  });

  describe("Windows paths", () => {
    it("parses Windows drive root", () => {
      assert.deepEqual(getPathSegments("C:\\"), [
        { label: "C:", fullPath: "C:/" },
      ]);
      assert.deepEqual(getPathSegments("C:"), [
        { label: "C:", fullPath: "C:/" },
      ]);
      assert.deepEqual(getPathSegments("D:/"), [
        { label: "D:", fullPath: "D:/" },
      ]);
    });

    it("parses backslash-separated Windows paths cleanly", () => {
      const segments = getPathSegments("C:\\Downloads\\Media");
      assert.deepEqual(segments, [
        { label: "C:", fullPath: "C:/" },
        { label: "Downloads", fullPath: "C:/Downloads" },
        { label: "Media", fullPath: "C:/Downloads/Media" },
      ]);
    });

    it("parses deep Windows paths without prepending leading slash", () => {
      const segments = getPathSegments("E:\\Torrents\\Complete\\TV\\Season 1");
      assert.deepEqual(segments, [
        { label: "E:", fullPath: "E:/" },
        { label: "Torrents", fullPath: "E:/Torrents" },
        { label: "Complete", fullPath: "E:/Torrents/Complete" },
        { label: "TV", fullPath: "E:/Torrents/Complete/TV" },
        { label: "Season 1", fullPath: "E:/Torrents/Complete/TV/Season 1" },
      ]);
    });

    it("handles mixed forward and backward slashes on Windows", () => {
      const segments = getPathSegments("C:\\Downloads/Media\\Sub");
      assert.deepEqual(segments, [
        { label: "C:", fullPath: "C:/" },
        { label: "Downloads", fullPath: "C:/Downloads" },
        { label: "Media", fullPath: "C:/Downloads/Media" },
        { label: "Sub", fullPath: "C:/Downloads/Media/Sub" },
      ]);
    });

    it("handles Windows paths with trailing backslashes", () => {
      const segments = getPathSegments("F:\\Data\\Backup\\");
      assert.deepEqual(segments, [
        { label: "F:", fullPath: "F:/" },
        { label: "Data", fullPath: "F:/Data" },
        { label: "Backup", fullPath: "F:/Data/Backup" },
      ]);
    });
  });
});
