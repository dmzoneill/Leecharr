import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { ArrConnection } from "../api/types";
import {
  getArrInstanceUrl,
  getDownloadClientUrl,
  getMediaDeepLink,
  getMusicBrainzUrl,
  getTmdbUrl,
} from "./arrLinks";

function createMockArrConnection(
  overrides: Partial<ArrConnection> = {},
): ArrConnection {
  return {
    id: 1,
    name: "Sonarr Main",
    arrType: "Sonarr",
    url: "http://192.168.1.100:8989",
    apiKey: "secret-key",
    enable: true,
    syncEnabled: true,
    enableAutomaticAdd: false,
    webhookEnabled: false,
    webhookHost: "",
    implementation: "Sonarr",
    configContract: "SonarrSettings",
    ...overrides,
  };
}

describe("arrLinks: getArrInstanceUrl", () => {
  it("should return null when source is null, empty, or whitespace", () => {
    const connections = [createMockArrConnection()];

    assert.equal(getArrInstanceUrl(null, connections), null);
    assert.equal(getArrInstanceUrl(undefined, connections), null);
    assert.equal(getArrInstanceUrl("", connections), null);
    assert.equal(getArrInstanceUrl("   ", connections), null);
  });

  it("should return null when connections array is null or undefined", () => {
    assert.equal(getArrInstanceUrl("sonarr", undefined), null);
  });

  it("should NOT match connections with null, empty, or whitespace arrType and name", () => {
    const emptyConnection = createMockArrConnection({
      id: 99,
      name: "",
      arrType: "",
      url: "http://empty-instance:8080",
    });

    const nullConnection = createMockArrConnection({
      id: 98,
      name: undefined,
      arrType: undefined,
      url: "http://null-instance:8080",
    });

    const whitespaceConnection = createMockArrConnection({
      id: 97,
      name: "   ",
      arrType: "   ",
      url: "http://whitespace-instance:8080",
    });

    const validRadarr = createMockArrConnection({
      id: 2,
      name: "Radarr 4K",
      arrType: "Radarr",
      url: "http://192.168.1.101:7878",
    });

    const connections = [
      emptyConnection,
      nullConnection,
      whitespaceConnection,
      validRadarr,
    ];

    // Testing source "sonarr": must NOT match emptyConnection (which previously matched because "sonarr".includes(""))
    assert.equal(
      getArrInstanceUrl("sonarr", connections),
      null,
      "Should not match any connection when only empty/whitespace connections precede",
    );

    // Testing source "radarr": must match validRadarr, skipping the empty/whitespace connections
    assert.equal(
      getArrInstanceUrl("radarr", connections),
      "http://192.168.1.101:7878",
    );
  });

  it("should match connection by exact arrType (case-insensitive)", () => {
    const connections = [
      createMockArrConnection({
        arrType: "Sonarr",
        url: "http://sonarr-host:8989",
      }),
    ];

    assert.equal(
      getArrInstanceUrl("sonarr", connections),
      "http://sonarr-host:8989",
    );
    assert.equal(
      getArrInstanceUrl("SONARR", connections),
      "http://sonarr-host:8989",
    );
  });

  it("should match connection by exact name (case-insensitive)", () => {
    const connections = [
      createMockArrConnection({
        name: "Anime-Stack",
        arrType: "Sonarr",
        url: "http://anime-sonarr:8989",
      }),
    ];

    assert.equal(
      getArrInstanceUrl("anime-stack", connections),
      "http://anime-sonarr:8989",
    );
  });

  it("should match connection by substring in source", () => {
    const connections = [
      createMockArrConnection({
        arrType: "Radarr",
        url: "http://radarr:7878",
      }),
    ];

    assert.equal(
      getArrInstanceUrl("radarr-movies-4k", connections),
      "http://radarr:7878",
    );
  });

  it("should ignore disabled connections", () => {
    const connections = [
      createMockArrConnection({
        enable: false,
        arrType: "Sonarr",
        url: "http://disabled:8989",
      }),
    ];

    assert.equal(getArrInstanceUrl("sonarr", connections), null);
  });

  it("should normalize URLs without protocol or with trailing slashes", () => {
    const connections = [
      createMockArrConnection({
        arrType: "Sonarr",
        url: "my-sonarr.local:8989///",
      }),
    ];

    assert.equal(
      getArrInstanceUrl("sonarr", connections),
      "http://my-sonarr.local:8989",
    );
  });
});

describe("arrLinks: getDownloadClientUrl", () => {
  it("should build URL with host, port, and scheme", () => {
    const url = getDownloadClientUrl({
      host: "localhost",
      port: 8080,
      useSsl: false,
    });
    assert.equal(url, "http://localhost:8080");
  });

  it("should support HTTPS when useSsl is true", () => {
    const url = getDownloadClientUrl({
      host: "myclient.seedarr.net",
      port: 443,
      useSsl: true,
    });
    assert.equal(url, "https://myclient.seedarr.net:443");
  });

  it("should support urlBase and strip leading/trailing slashes", () => {
    const url1 = getDownloadClientUrl({
      host: "10.0.0.5",
      port: 8080,
      useSsl: false,
      urlBase: "/qbittorrent/",
    });
    assert.equal(url1, "http://10.0.0.5:8080/qbittorrent");

    const url2 = getDownloadClientUrl({
      host: "10.0.0.5",
      port: 9091,
      useSsl: false,
      urlBase: "transmission/web",
    });
    assert.equal(url2, "http://10.0.0.5:9091/transmission/web");
  });

  it("should return empty string when host is empty or null", () => {
    assert.equal(getDownloadClientUrl({ host: "" }), "");
    assert.equal(getDownloadClientUrl({ host: undefined }), "");
  });
});

describe("arrLinks: getMediaDeepLink", () => {
  it("should build deep link for Sonarr series with mediaId", () => {
    const connections = [
      createMockArrConnection({
        arrType: "Sonarr",
        url: "http://sonarr:8989",
      }),
    ];

    const link = getMediaDeepLink(
      {
        source: "Sonarr",
        metadata: { mediaId: 123, mediaType: "series" },
      },
      connections,
    );

    assert.ok(link);
    assert.equal(link.url, "http://sonarr:8989/series/123");
    assert.equal(link.label, "Open in Sonarr");
    assert.equal(link.appName, "Sonarr");
  });

  it("should build deep link for Radarr movie with mediaId", () => {
    const connections = [
      createMockArrConnection({
        arrType: "Radarr",
        url: "http://radarr:7878",
      }),
    ];

    const link = getMediaDeepLink(
      {
        source: "Radarr",
        metadata: { mediaId: 456, mediaType: "movie" },
      },
      connections,
    );

    assert.ok(link);
    assert.equal(link.url, "http://radarr:7878/movie/456");
    assert.equal(link.label, "Open in Radarr");
    assert.equal(link.appName, "Radarr");
  });

  it("should return null when no connection matches source", () => {
    const connections = [
      createMockArrConnection({
        arrType: "Sonarr",
        url: "http://sonarr:8989",
      }),
    ];

    const link = getMediaDeepLink(
      {
        source: "UnknownSource",
      },
      connections,
    );

    assert.equal(link, null);
  });
});

describe("arrLinks: getMusicBrainzUrl", () => {
  it("should return null when musicBrainzId is null, undefined, or empty", () => {
    assert.equal(getMusicBrainzUrl(null), null);
    assert.equal(getMusicBrainzUrl(undefined), null);
    assert.equal(getMusicBrainzUrl(""), null);
    assert.equal(getMusicBrainzUrl("   "), null);
  });

  it("should return MusicBrainz URL for valid ID", () => {
    assert.equal(
      getMusicBrainzUrl("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
      "https://musicbrainz.org/release-group/a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    );
  });
});

describe("arrLinks: getTmdbUrl", () => {
  it("should return null when tmdbId is null, undefined, 0, or empty", () => {
    assert.equal(getTmdbUrl(null), null);
    assert.equal(getTmdbUrl(undefined), null);
    assert.equal(getTmdbUrl(0), null);
    assert.equal(getTmdbUrl(""), null);
    assert.equal(getTmdbUrl("   "), null);
  });

  it("should return movie URL when mediaType is 'movie'", () => {
    assert.equal(
      getTmdbUrl(27205, "movie"),
      "https://www.themoviedb.org/movie/27205",
    );
  });

  it("should return tv URL when mediaType is 'tv'", () => {
    assert.equal(
      getTmdbUrl(1399, "tv"),
      "https://www.themoviedb.org/tv/1399",
    );
  });

  it("should infer movie URL when arrType is 'Radarr'", () => {
    assert.equal(
      getTmdbUrl(27205, null, "Radarr"),
      "https://www.themoviedb.org/movie/27205",
    );
    assert.equal(
      getTmdbUrl(27205, undefined, "radarr"),
      "https://www.themoviedb.org/movie/27205",
    );
  });

  it("should infer tv URL when arrType is 'Sonarr'", () => {
    assert.equal(
      getTmdbUrl(1399, null, "Sonarr"),
      "https://www.themoviedb.org/tv/1399",
    );
    assert.equal(
      getTmdbUrl(1399, undefined, "sonarr"),
      "https://www.themoviedb.org/tv/1399",
    );
  });

  it("should infer movie URL when mediaType is passed as 'Radarr'", () => {
    assert.equal(
      getTmdbUrl(27205, "Radarr"),
      "https://www.themoviedb.org/movie/27205",
    );
  });

  it("should default to tv URL when neither mediaType nor arrType indicate movie", () => {
    assert.equal(
      getTmdbUrl(1399),
      "https://www.themoviedb.org/tv/1399",
    );
    assert.equal(
      getTmdbUrl(1399, null, null),
      "https://www.themoviedb.org/tv/1399",
    );
  });
});
