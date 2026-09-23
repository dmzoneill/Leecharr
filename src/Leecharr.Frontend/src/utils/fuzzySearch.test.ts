import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  scoreItem,
  filterAndRankItems,
  parseSearchQuery,
  type FuzzySearchable,
} from "./fuzzySearch";

describe("fuzzySearch: parseSearchQuery", () => {
  it("should parse empty queries", () => {
    assert.deepEqual(parseSearchQuery(""), { query: "" });
    assert.deepEqual(parseSearchQuery("   "), { query: "" });
  });

  it("should parse action symbol prefix '>'", () => {
    assert.deepEqual(parseSearchQuery(">"), {
      category: "Actions",
      query: "",
    });
    assert.deepEqual(parseSearchQuery("> harvest"), {
      category: "Actions",
      query: "harvest",
    });
  });

  it("should parse named category prefixes", () => {
    assert.deepEqual(parseSearchQuery("tor: ubuntu"), {
      category: "Torrents",
      query: "ubuntu",
    });
    assert.deepEqual(parseSearchQuery("torrents: debian"), {
      category: "Torrents",
      query: "debian",
    });
    assert.deepEqual(parseSearchQuery("nav: dashboard"), {
      category: "Navigation",
      query: "dashboard",
    });
    assert.deepEqual(parseSearchQuery("act: probe"), {
      category: "Actions",
      query: "probe",
    });
    assert.deepEqual(parseSearchQuery("set: bittorrent"), {
      category: "Settings",
      query: "bittorrent",
    });
  });

  it("should leave non-prefixed queries untouched", () => {
    assert.deepEqual(parseSearchQuery("tracker boost"), {
      query: "tracker boost",
    });
  });
});

describe("fuzzySearch: scoreItem", () => {
  it("should score exact title match highest (100 pts)", () => {
    const exact = scoreItem({ title: "Tracker Boost" }, "Tracker Boost");
    const prefix = scoreItem(
      { title: "Tracker Boost Matrix" },
      "Tracker Boost",
    );
    const substring = scoreItem(
      { title: "Inbuilt Tracker Boost" },
      "Tracker Boost",
    );
    const sub = scoreItem(
      { title: "Settings", subtitle: "Manage Tracker Boost" },
      "Tracker Boost",
    );

    assert.equal(exact, 100);
    assert.ok(exact > prefix);
    assert.ok(prefix > substring);
    assert.ok(substring > sub);
  });

  it("should rank prefix match higher than word boundary and substring match", () => {
    const prefix = scoreItem({ title: "Tracker Boost" }, "track");
    const wordBoundary = scoreItem({ title: "Inbuilt Tracker" }, "track");
    const substring = scoreItem({ title: "Fastrack Engine" }, "track");
    const subtitle = scoreItem(
      { title: "Engine", subtitle: "Monitors track state" },
      "track",
    );

    assert.equal(prefix, 80);
    assert.equal(wordBoundary, 60);
    assert.equal(substring, 40);
    assert.equal(subtitle, 20);
  });

  it("should match acronyms and initialisms (e.g. 'tb' -> 'Tracker Boost')", () => {
    const tb = scoreItem({ title: "Tracker Boost" }, "tb");
    const gs = scoreItem({ title: "General Settings" }, "gs");
    const am = scoreItem({ title: "Activity Metrics" }, "am");
    const mismatch = scoreItem({ title: "Table Tennis" }, "tb");

    assert.equal(tb, 50);
    assert.equal(gs, 50);
    assert.equal(am, 50);
    assert.equal(mismatch, 0);
  });

  it("should perform multi-word matching across title and subtitle", () => {
    const match = scoreItem(
      {
        title: "Sync Trackers from Prowlarr Indexers",
        subtitle:
          "Extract public and configured trackers from all Prowlarr indexers",
      },
      "prowlarr sync",
    );
    assert.ok(match > 0);

    const missingOneToken = scoreItem(
      {
        title: "Sync Trackers from Deluge Indexers",
        subtitle: "Extract public and configured trackers",
      },
      "prowlarr sync",
    );
    assert.equal(missingOneToken, 0);
  });
});

interface TestSearchItem extends FuzzySearchable {
  id: string;
}

describe("fuzzySearch: filterAndRankItems", () => {
  const sampleItems: TestSearchItem[] = [
    {
      id: "nav-dash",
      category: "Navigation",
      title: "Dashboard",
      subtitle: "Overview, stats, charts and recent activity",
    },
    {
      id: "nav-tb",
      category: "Navigation",
      title: "Tracker Boost",
      subtitle: "Swarm optimizer and live scraping",
    },
    {
      id: "set-gen",
      category: "Settings",
      title: "General Settings",
      subtitle: "Application port and API key",
    },
    {
      id: "act-prowlarr",
      category: "Actions",
      title: "Sync Trackers from Prowlarr Indexers",
      subtitle: "Extract public and configured trackers from Prowlarr",
    },
    {
      id: "act-harvest",
      category: "Actions",
      title: "Harvest Trackers from Live Swarms",
      subtitle: "Discover new tracker endpoints",
    },
    {
      id: "tor-fedora",
      category: "Torrents",
      title: "Fedora 40",
      subtitle: "Linux OS",
    },
    {
      id: "tor-ubuntu",
      category: "Torrents",
      title: "Ubuntu 24.04 Desktop",
      subtitle: "Linux Desktop ISO",
    },
  ];

  it("should not match items just because of category 'Torrents' when searching 't'", () => {
    // "Fedora 40" has no 't' in title or subtitle. Its category is "Torrents" (which has 't').
    // Naive category substring matching would include "Fedora 40". Our weighted search must NOT.
    const results = filterAndRankItems(sampleItems, "t");
    const fedora = results.find((r) => r.id === "tor-fedora");
    assert.equal(fedora, undefined);
  });

  it("should match acronym 'tb' to 'Tracker Boost'", () => {
    const results = filterAndRankItems(sampleItems, "tb");
    assert.ok(results.length > 0);
    assert.equal(results[0].id, "nav-tb");
  });

  it("should match multi-word query 'prowlarr sync' to Prowlarr action", () => {
    const results = filterAndRankItems(sampleItems, "prowlarr sync");
    assert.equal(results.length, 1);
    assert.equal(results[0].id, "act-prowlarr");
  });

  it("should respect category prefix '>' to filter only Actions", () => {
    const results = filterAndRankItems(sampleItems, ">");
    assert.equal(results.length, 2);
    assert.ok(results.every((r) => r.category === "Actions"));
  });

  it("should respect category prefix 'tor:' to filter only Torrents", () => {
    const results = filterAndRankItems(sampleItems, "tor: ubuntu");
    assert.equal(results.length, 1);
    assert.equal(results[0].id, "tor-ubuntu");
  });

  it("should return unmodified order when query is empty", () => {
    const results = filterAndRankItems(sampleItems, "");
    assert.equal(results.length, sampleItems.length);
    assert.equal(results[0].id, sampleItems[0].id);
  });
});
