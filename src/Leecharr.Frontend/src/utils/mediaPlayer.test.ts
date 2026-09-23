import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  isPlayableFile,
  isAudioFile,
  parseMediaBadges,
  buildStreamUrl,
  buildDownloadUrl,
  buildExternalPlayerUrl,
  getAbsoluteUrl,
  cleanUpMediaElement,
  setSubtitleTrackActive,
  getCodecErrorMessage,
  PLAYABLE_EXTENSIONS,
  AUDIO_EXTENSIONS,
} from "./mediaPlayer";
import type { SubtitleTrack } from "../api/types";

describe("mediaPlayer: isPlayableFile and isAudioFile", () => {
  it("should identify all supported video and audio extensions as playable", () => {
    for (const ext of PLAYABLE_EXTENSIONS) {
      assert.equal(isPlayableFile(`video${ext}`), true);
      assert.equal(isPlayableFile(`path/to/my_file${ext.toUpperCase()}`), true);
    }
  });

  it("should reject non-media file extensions", () => {
    const nonPlayable = [
      "readme.txt",
      "disc.iso",
      "movie.nfo",
      "subtitle.srt",
      "subs.vtt",
      "archive.zip",
      "setup.exe",
      null,
      undefined,
      "",
    ];
    for (const f of nonPlayable) {
      assert.equal(isPlayableFile(f), false);
    }
  });

  it("should distinguish audio-only files from video files", () => {
    for (const ext of AUDIO_EXTENSIONS) {
      assert.equal(isAudioFile(`track${ext}`), true);
    }
    assert.equal(isAudioFile("movie.mp4"), false);
    assert.equal(isAudioFile("episode.mkv"), false);
    assert.equal(isAudioFile(null), false);
  });
});

describe("mediaPlayer: parseMediaBadges", () => {
  it("should detect resolution, video codec, audio codec, and container from filename", () => {
    const badges1 = parseMediaBadges(
      "Cosmos.S01E01.1080p.HEVC.x265.AC3-GROUP.mkv",
      "Cosmos S01",
    );
    assert.equal(badges1.resolution, "1080p");
    assert.equal(badges1.videoCodec, "HEVC / H.265");
    assert.equal(badges1.audioCodec, "AC3 / Dolby Digital");
    assert.equal(badges1.container, "MKV");
    assert.ok(badges1.badges.includes("1080p"));
    assert.ok(badges1.badges.includes("HEVC"));
    assert.ok(badges1.badges.includes("AC3"));
    assert.ok(badges1.badges.includes("MKV"));

    const badges2 = parseMediaBadges(
      "Dune.Part.Two.2024.2160p.UHD.AV1.Atmos.mkv",
      "Dune Part Two",
    );
    assert.equal(badges2.resolution, "4K / 2160p");
    assert.equal(badges2.videoCodec, "AV1");
    assert.equal(badges2.audioCodec, "Dolby TrueHD / Atmos");
    assert.ok(badges2.badges.includes("4K"));
    assert.ok(badges2.badges.includes("AV1"));
    assert.ok(badges2.badges.includes("TrueHD"));
  });

  it("should handle files with minimal or no metadata gracefully", () => {
    const badges = parseMediaBadges("audio_sample.mp3");
    assert.equal(badges.audioCodec, "MP3");
    assert.equal(badges.container, "MP3");
    assert.equal(badges.resolution, undefined);
    assert.equal(badges.videoCodec, undefined);
  });
});

describe("mediaPlayer: URL builders", () => {
  it("should build valid torrent stream and download URLs", () => {
    assert.equal(buildStreamUrl(42, 7), "/api/v1/torrent/42/files/7/stream");
    assert.equal(
      buildDownloadUrl(42, 7),
      "/api/v1/torrent/42/files/7/download",
    );
  });

  it("should build absolute URLs with origin", () => {
    const abs = getAbsoluteUrl(
      "/api/v1/torrent/10/files/2/stream",
      "https://seedarr.example.com",
    );
    assert.equal(
      abs,
      "https://seedarr.example.com/api/v1/torrent/10/files/2/stream",
    );
  });

  it("should build external player deep links for VLC and MPV", () => {
    const streamUrl = "/api/v1/torrent/15/files/1/stream";
    const origin = "http://127.0.0.1:5000";

    const vlc = buildExternalPlayerUrl("vlc", streamUrl, origin);
    assert.equal(
      vlc,
      "vlc://http://127.0.0.1:5000/api/v1/torrent/15/files/1/stream",
    );

    const mpv = buildExternalPlayerUrl("mpv", streamUrl, origin);
    assert.equal(
      mpv,
      "web+mpv://http://127.0.0.1:5000/api/v1/torrent/15/files/1/stream",
    );
  });
});

describe("mediaPlayer: cleanUpMediaElement stream lifecycle cleanup", () => {
  it("should pause, remove src, remove child source elements, and call load() to flush pipeline", () => {
    let paused = false;
    let srcAttribute: string | null = "blob:http://localhost/123";
    let loaded = false;

    const childSource = { tagName: "SOURCE" };
    const children: any[] = [childSource];

    const mockMediaElement: any = {
      pause: () => {
        paused = true;
      },
      removeAttribute: (attr: string) => {
        if (attr === "src") srcAttribute = null;
      },
      get firstChild() {
        return children[0] ?? null;
      },
      removeChild: (child: any) => {
        const idx = children.indexOf(child);
        if (idx !== -1) children.splice(idx, 1);
      },
      load: () => {
        loaded = true;
      },
    };

    cleanUpMediaElement(mockMediaElement);

    assert.equal(paused, true);
    assert.equal(srcAttribute, null);
    assert.equal(children.length, 0);
    assert.equal(loaded, true);
  });

  it("should not throw on null, undefined, or aborted media element", () => {
    assert.doesNotThrow(() => cleanUpMediaElement(null));
    assert.doesNotThrow(() => cleanUpMediaElement(undefined));

    const brokenElement: any = {
      pause: () => {
        throw new Error("AbortError");
      },
      removeAttribute: () => {},
      load: () => {},
    };
    assert.doesNotThrow(() => cleanUpMediaElement(brokenElement));
  });
});

describe("mediaPlayer: Subtitle track switching and textTracks synchronization", () => {
  it("should disable all text tracks when subtitle mode is set to off", () => {
    const textTracks: any = [
      { mode: "showing", label: "English", language: "en" },
      { mode: "disabled", label: "Spanish", language: "es" },
    ];

    setSubtitleTrackActive(textTracks, "off");

    assert.equal(textTracks[0].mode, "disabled");
    assert.equal(textTracks[1].mode, "disabled");
  });

  it("should activate the selected subtitle track and disable all other tracks", () => {
    const textTracks: any = [
      { mode: "showing", label: "English", language: "en" },
      { mode: "disabled", label: "French", language: "fr" },
      { mode: "disabled", label: "German", language: "de" },
    ];

    const subtitles: SubtitleTrack[] = [
      {
        trackId: 101,
        title: "English",
        language: "English",
        twoLetterCode: "en",
        format: "vtt",
        path: "sub1.vtt",
        isExternal: false,
        isForced: false,
        isHearingImpaired: false,
        isDefault: true,
        url: "/api/v1/torrent/1/files/1/subtitles/101.vtt",
      },
      {
        trackId: 102,
        title: "French",
        language: "French",
        twoLetterCode: "fr",
        format: "vtt",
        path: "sub2.vtt",
        isExternal: true,
        isForced: false,
        isHearingImpaired: false,
        isDefault: false,
        url: "/api/v1/torrent/1/files/1/subtitles/102.vtt",
      },
    ];

    // Select French (trackId 102)
    setSubtitleTrackActive(textTracks, 102, subtitles);

    assert.equal(textTracks[0].mode, "disabled");
    assert.equal(textTracks[1].mode, "showing");
    assert.equal(textTracks[2].mode, "disabled");
  });
});

describe("mediaPlayer: Codec error recovery message generator", () => {
  it("should generate actionable diagnostic message for MEDIA_ERR_SRC_NOT_SUPPORTED (code 4)", () => {
    const msg = getCodecErrorMessage(4, "Movie.2024.1080p.HEVC.x265.AC3.mkv");
    assert.ok(msg.includes("cannot decode this media codec"));
    assert.ok(msg.includes("HEVC / H.265 video"));
    assert.ok(msg.includes("AC3 / DTS surround audio"));
    assert.ok(msg.includes("Matroska (.mkv) container"));
  });

  it("should handle decode errors (code 3) gracefully", () => {
    const msg = getCodecErrorMessage(3, "video.mp4");
    assert.ok(msg.includes("media decoding error occurred"));
  });
});
