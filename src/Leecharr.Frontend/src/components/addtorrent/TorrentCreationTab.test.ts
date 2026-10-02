import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  createTorrentBlob,
  getTorrentDownloadFilename,
  triggerBlobDownload,
  downloadTorrentFile,
} from "./TorrentCreationTab";

describe("TorrentCreationTab: torrent download functionality (#1012)", () => {
  describe("createTorrentBlob", () => {
    it("creates a Blob with application/x-bittorrent type from base64 string", async () => {
      // "d10:created by8:Leecharre" in bencoded format
      const bencodedText = "d10:created by8:Leecharre";
      const base64Str = Buffer.from(bencodedText, "utf8").toString("base64");

      const blob = createTorrentBlob(base64Str);
      assert.strictEqual(blob.type, "application/x-bittorrent");
      assert.strictEqual(blob.size, bencodedText.length);

      const arrayBuffer = await blob.arrayBuffer();
      const decodedText = Buffer.from(arrayBuffer).toString("utf8");
      assert.strictEqual(decodedText, bencodedText);
    });

    it("handles base64 string with leading and trailing whitespace", async () => {
      const bencodedText = "d4:infodee";
      const base64Str = `  ${Buffer.from(bencodedText, "utf8").toString("base64")}  \n`;

      const blob = createTorrentBlob(base64Str);
      assert.strictEqual(blob.type, "application/x-bittorrent");
      assert.strictEqual(blob.size, bencodedText.length);
    });

    it("creates a Blob from Uint8Array", async () => {
      const bytes = new Uint8Array([0x64, 0x31, 0x30, 0x65]);
      const blob = createTorrentBlob(bytes);
      assert.strictEqual(blob.type, "application/x-bittorrent");
      assert.strictEqual(blob.size, 4);

      const arrayBuffer = await blob.arrayBuffer();
      assert.deepStrictEqual(new Uint8Array(arrayBuffer), bytes);
    });

    it("creates a Blob from number array", async () => {
      const numbers = [100, 49, 48, 101];
      const blob = createTorrentBlob(numbers);
      assert.strictEqual(blob.type, "application/x-bittorrent");
      assert.strictEqual(blob.size, 4);

      const arrayBuffer = await blob.arrayBuffer();
      assert.deepStrictEqual(
        new Uint8Array(arrayBuffer),
        new Uint8Array(numbers),
      );
    });

    it("throws an error for invalid input types", () => {
      assert.throws(
        () => createTorrentBlob(null as unknown as string),
        /Invalid torrent file bytes format/,
      );
      assert.throws(
        () => createTorrentBlob(undefined as unknown as string),
        /Invalid torrent file bytes format/,
      );
      assert.throws(
        () => createTorrentBlob(12345 as unknown as string),
        /Invalid torrent file bytes format/,
      );
    });
  });

  describe("getTorrentDownloadFilename", () => {
    it("uses torrentName when provided and appends .torrent if missing", () => {
      assert.strictEqual(
        getTorrentDownloadFilename("/some/path", "MyCustomTorrent"),
        "MyCustomTorrent.torrent",
      );
    });

    it("does not duplicate .torrent extension when torrentName already ends with .torrent", () => {
      assert.strictEqual(
        getTorrentDownloadFilename("/some/path", "MyCustomTorrent.torrent"),
        "MyCustomTorrent.torrent",
      );
      assert.strictEqual(
        getTorrentDownloadFilename("/some/path", "MyCustomTorrent.TORRENT"),
        "MyCustomTorrent.TORRENT",
      );
    });

    it("extracts base filename from sourcePath when torrentName is omitted", () => {
      assert.strictEqual(
        getTorrentDownloadFilename("/downloads/complete/Linux-Distro.iso"),
        "Linux-Distro.iso.torrent",
      );
    });

    it("handles Windows backslash separators in sourcePath", () => {
      assert.strictEqual(
        getTorrentDownloadFilename("D:\\torrents\\source_data\\SampleArchive.zip"),
        "SampleArchive.zip.torrent",
      );
    });

    it("handles trailing slashes in directory sourcePath", () => {
      assert.strictEqual(
        getTorrentDownloadFilename("/storage/media/MyMovieFolder/"),
        "MyMovieFolder.torrent",
      );
      assert.strictEqual(
        getTorrentDownloadFilename("C:\\media\\album\\\\"),
        "album.torrent",
      );
    });

    it("does not duplicate .torrent when sourcePath already ends in .torrent", () => {
      assert.strictEqual(
        getTorrentDownloadFilename("/downloads/existing.torrent"),
        "existing.torrent",
      );
    });

    it("falls back to infoHash.torrent when name and sourcePath are empty", () => {
      assert.strictEqual(
        getTorrentDownloadFilename(
          "",
          "",
          "0123456789abcdef0123456789abcdef01234567",
        ),
        "0123456789abcdef0123456789abcdef01234567.torrent",
      );
    });

    it("falls back to created.torrent when all parameters are empty or whitespace", () => {
      assert.strictEqual(getTorrentDownloadFilename(), "created.torrent");
      assert.strictEqual(getTorrentDownloadFilename("   ", "   ", "   "), "created.torrent");
    });
  });

  describe("triggerBlobDownload and downloadTorrentFile", () => {
    it("returns false gracefully in Node/SSR environment without throwing", () => {
      const blob = new Blob(["test"], { type: "application/x-bittorrent" });
      const result = triggerBlobDownload(blob, "test.torrent");
      assert.strictEqual(result, false);
    });

    it("creates anchor element, sets attributes, clicks, and cleans up when DOM is mocked", () => {
      let createdUrl = "";
      let clicked = false;
      let revokedUrl = "";
      let appendedChild: unknown = null;
      let removedChild: unknown = null;

      const mockAnchor = {
        href: "",
        download: "",
        click: () => {
          clicked = true;
        },
        setAttribute: (attr: string, val: string) => {
          if (attr === "download") {
            mockAnchor.download = val;
          }
        },
      };

      const mockDoc = {
        createElement: (tag: string) => {
          if (tag === "a") return mockAnchor;
          return {};
        },
        body: {
          appendChild: (node: unknown) => {
            appendedChild = node;
          },
          removeChild: (node: unknown) => {
            removedChild = node;
          },
        },
      };

      const mockWindow = {
        URL: {
          createObjectURL: (_blob: Blob) => {
            createdUrl = "blob:http://localhost:5000/mock-uuid";
            return createdUrl;
          },
          revokeObjectURL: (url: string) => {
            revokedUrl = url;
          },
        },
      };

      // Set globals for test
      (globalThis as unknown as { window: unknown }).window = mockWindow;
      (globalThis as unknown as { document: unknown }).document = mockDoc;

      try {
        const bytes = Buffer.from("d10:created by8:Leecharre").toString("base64");
        const success = downloadTorrentFile(bytes, "downloaded.torrent");

        assert.strictEqual(success, true);
        assert.strictEqual(mockAnchor.download, "downloaded.torrent");
        assert.strictEqual(mockAnchor.href, createdUrl);
        assert.strictEqual(clicked, true);
        assert.strictEqual(appendedChild, mockAnchor);
        assert.strictEqual(removedChild, mockAnchor);
        assert.strictEqual(revokedUrl, createdUrl);
      } finally {
        delete (globalThis as unknown as { window?: unknown }).window;
        delete (globalThis as unknown as { document?: unknown }).document;
      }
    });
  });
});
