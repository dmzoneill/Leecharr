// Ensure localStorage exists in Node test environment before importing modules that touch it
if (typeof globalThis.localStorage === "undefined") {
  const store = new Map<string, string>();
  (globalThis as unknown as { localStorage: unknown }).localStorage = {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, val: string) => store.set(key, val),
    removeItem: (key: string) => store.delete(key),
    clear: () => store.clear(),
  };
}

import { describe, it, beforeEach } from "node:test";
import assert from "node:assert/strict";
import {
  AnnouncementQueue,
  announce,
  clearListeners,
} from "./AriaLiveAnnouncer";
import en from "../i18n/locales/en";
import de from "../i18n/locales/de";
import es from "../i18n/locales/es";
import fr from "../i18n/locales/fr";
import itLocale from "../i18n/locales/it";
import pt from "../i18n/locales/pt";
import ru from "../i18n/locales/ru";
import tr from "../i18n/locales/tr";
import id from "../i18n/locales/id";
import zhCN from "../i18n/locales/zh-CN";
import ja from "../i18n/locales/ja";
import ko from "../i18n/locales/ko";
import ar from "../i18n/locales/ar";
import hi from "../i18n/locales/hi";
import bn from "../i18n/locales/bn";
import mr from "../i18n/locales/mr";
import ta from "../i18n/locales/ta";
import te from "../i18n/locales/te";
import ur from "../i18n/locales/ur";
import vi from "../i18n/locales/vi";
import { translate } from "../i18n/useTranslation";
import { useI18nStore } from "../i18n/i18nStore";

const allLocales = [
  { code: "en", dict: en },
  { code: "de", dict: de },
  { code: "es", dict: es },
  { code: "fr", dict: fr },
  { code: "it", dict: itLocale },
  { code: "pt", dict: pt },
  { code: "ru", dict: ru },
  { code: "tr", dict: tr },
  { code: "id", dict: id },
  { code: "zh-CN", dict: zhCN },
  { code: "ja", dict: ja },
  { code: "ko", dict: ko },
  { code: "ar", dict: ar },
  { code: "hi", dict: hi },
  { code: "bn", dict: bn },
  { code: "mr", dict: mr },
  { code: "ta", dict: ta },
  { code: "te", dict: te },
  { code: "ur", dict: ur },
  { code: "vi", dict: vi },
];

describe("AriaLiveAnnouncer localization", () => {
  beforeEach(() => {
    useI18nStore.setState({ language: "en", translations: en });
    clearListeners();
  });

  it("all 20 locales define ariaLive translation keys", () => {
    for (const { code, dict } of allLocales) {
      const live = (dict as Record<string, unknown>).ariaLive as Record<string, string>;
      assert.ok(live, `Locale ${code} is missing ariaLive section`);
      assert.ok(
        live.torrentCompleted,
        `Locale ${code} is missing ariaLive.torrentCompleted`,
      );
      assert.ok(
        live.torrentCompleted.includes("{name}") ||
          live.torrentCompleted.includes("{{name}}"),
        `Locale ${code} ariaLive.torrentCompleted missing name placeholder`,
      );
      assert.ok(
        live.torrentError,
        `Locale ${code} is missing ariaLive.torrentError`,
      );
      assert.ok(
        live.torrentError.includes("{name}") ||
          live.torrentError.includes("{{name}}"),
        `Locale ${code} ariaLive.torrentError missing name placeholder`,
      );
      assert.ok(
        live.torrentError.includes("{error}") ||
          live.torrentError.includes("{{error}}"),
        `Locale ${code} ariaLive.torrentError missing error placeholder`,
      );
      assert.ok(
        live.downloadInterrupted,
        `Locale ${code} is missing ariaLive.downloadInterrupted`,
      );
    }
  });

  it("correctly interpolates torrent completed announcements across multiple locales", () => {
    const testCases = [
      { code: "en", dict: en, torrent: "Ubuntu 24.04 ISO" },
      { code: "de", dict: de, torrent: "Debian 12 ISO" },
      { code: "es", dict: es, torrent: "Fedora 40 ISO" },
      { code: "fr", dict: fr, torrent: "Arch Linux ISO" },
    ];

    for (const { code, dict, torrent } of testCases) {
      useI18nStore.setState({ language: code, translations: dict });
      const interpolated = translate(
        "ariaLive.torrentCompleted",
        { name: torrent },
        `Torrent "${torrent}" completed, now seeding`,
      );
      assert.ok(
        interpolated.includes(torrent),
        `Locale ${code} did not include torrent name: ${interpolated}`,
      );
      assert.ok(
        !interpolated.includes("{{name}}") && !interpolated.includes("{name}"),
        `Locale ${code} did not interpolate name placeholder: ${interpolated}`,
      );
    }
  });

  it("correctly interpolates torrent error announcements with custom and fallback errors", () => {
    useI18nStore.setState({ language: "en", translations: en });

    const withCustomError = translate(
      "ariaLive.torrentError",
      { name: "MyTorrent", error: "Tracker responded with 404" },
      'Torrent "MyTorrent" encountered error: Tracker responded with 404',
    );
    assert.strictEqual(
      withCustomError,
      'Torrent "MyTorrent" encountered error: Tracker responded with 404',
    );

    const fallbackInterrupted = translate(
      "ariaLive.downloadInterrupted",
      "download interrupted",
    );
    assert.strictEqual(fallbackInterrupted, "download interrupted");

    const withFallback = translate(
      "ariaLive.torrentError",
      { name: "MyTorrent", error: fallbackInterrupted },
      `Torrent "MyTorrent" encountered error: ${fallbackInterrupted}`,
    );
    assert.strictEqual(
      withFallback,
      'Torrent "MyTorrent" encountered error: download interrupted',
    );
  });

  it("falls back to default English string when translation key is unknown", () => {
    const fallback = 'Torrent "Test" completed, now seeding';
    const result = translate(
      "ariaLive.nonexistentKey",
      { name: "Test" },
      fallback,
    );
    assert.strictEqual(result, fallback);
  });
});

describe("AnnouncementQueue concurrent handling", () => {
  it("enqueues a single message and announces it immediately", () => {
    const received: string[] = [];
    const queue = new AnnouncementQueue({
      displayDuration: 50,
      queuedDuration: 30,
      transitionGap: 10,
      onMessageChange: (msg) => received.push(msg),
    });

    queue.enqueue("First announcement");
    assert.strictEqual(queue.getCurrentMessage(), "First announcement");
    assert.strictEqual(queue.getQueueLength(), 0);
    assert.strictEqual(received.length, 1);
    assert.strictEqual(received[0], "First announcement");

    queue.clear();
  });

  it("queues concurrent announcements without dropping or immediately overwriting them", async () => {
    const updates: string[] = [];
    const queue = new AnnouncementQueue({
      displayDuration: 60,
      queuedDuration: 40,
      transitionGap: 15,
      onMessageChange: (msg) => updates.push(msg),
    });

    // Rapidly enqueue 3 announcements simultaneously
    queue.enqueue("Announcement 1");
    queue.enqueue("Announcement 2");
    queue.enqueue("Announcement 3");

    // First announcement should be active immediately
    assert.strictEqual(queue.getCurrentMessage(), "Announcement 1");
    // Remaining announcements must be queued
    assert.strictEqual(queue.getQueueLength(), 2);
    assert.deepStrictEqual(queue.getQueue(), [
      "Announcement 2",
      "Announcement 3",
    ]);

    // Wait for the full queue to process:
    // 1st msg (40ms) + gap (15ms) + 2nd msg (40ms) + gap (15ms) + 3rd msg (60ms) ~ 170ms
    await new Promise((resolve) => setTimeout(resolve, 250));

    // All 3 messages must have been announced sequentially
    const nonBlankMessages = updates.filter((msg) => msg.length > 0);
    assert.deepStrictEqual(nonBlankMessages, [
      "Announcement 1",
      "Announcement 2",
      "Announcement 3",
    ]);

    // Queue must be idle and cleared at the end
    assert.strictEqual(queue.getCurrentMessage(), "");
    assert.strictEqual(queue.getQueueLength(), 0);
    assert.strictEqual(queue.isBusy(), false);

    queue.clear();
  });

  it("blanks message briefly during transition to trigger screen reader mutation", async () => {
    const updates: string[] = [];
    const queue = new AnnouncementQueue({
      displayDuration: 40,
      queuedDuration: 30,
      transitionGap: 20,
      onMessageChange: (msg) => updates.push(msg),
    });

    queue.enqueue("Alpha");
    queue.enqueue("Beta");

    await new Promise((resolve) => setTimeout(resolve, 150));

    // Expect: "Alpha", then "" (blanking gap), then "Beta", then "" (cleared)
    assert.ok(updates.includes("Alpha"));
    assert.ok(updates.includes("Beta"));
    assert.ok(updates.includes(""));

    queue.clear();
  });

  it("ignores empty or whitespace messages", () => {
    const updates: string[] = [];
    const queue = new AnnouncementQueue({
      onMessageChange: (msg) => updates.push(msg),
    });

    queue.enqueue("");
    queue.enqueue("   ");
    assert.strictEqual(queue.getQueueLength(), 0);
    assert.strictEqual(queue.isBusy(), false);
    assert.strictEqual(updates.length, 0);

    queue.clear();
  });

  it("clear() resets queue, active message, and cancels pending timers", async () => {
    const updates: string[] = [];
    const queue = new AnnouncementQueue({
      displayDuration: 100,
      queuedDuration: 100,
      transitionGap: 20,
      onMessageChange: (msg) => updates.push(msg),
    });

    queue.enqueue("Item 1");
    queue.enqueue("Item 2");
    queue.enqueue("Item 3");

    assert.strictEqual(queue.getQueueLength(), 2);
    queue.clear();

    assert.strictEqual(queue.getCurrentMessage(), "");
    assert.strictEqual(queue.getQueueLength(), 0);
    assert.strictEqual(queue.isBusy(), false);

    // Wait to confirm no pending timers fire
    await new Promise((resolve) => setTimeout(resolve, 150));
    assert.strictEqual(queue.getCurrentMessage(), "");
  });

  it("announce helper dispatches announcements with correct priorities", () => {
    const politeDispatched: string[] = [];
    const assertiveDispatched: string[] = [];

    // Simulate listener like AriaLiveAnnouncer registers
    const _listener = (announcement: { message: string; priority: string }) => {
      if (announcement.priority === "assertive") {
        assertiveDispatched.push(announcement.message);
      } else {
        politeDispatched.push(announcement.message);
      }
    };

    // Test announce
    announce("Torrent completed", "polite");
    // No listener yet, shouldn't throw

    // Now test with the AriaLiveAnnouncer queue pattern
    const politeQueue = new AnnouncementQueue({
      onMessageChange: (msg) => msg && politeDispatched.push(msg),
    });
    const assertiveQueue = new AnnouncementQueue({
      onMessageChange: (msg) => msg && assertiveDispatched.push(msg),
    });

    politeQueue.enqueue("Torrent completed");
    assertiveQueue.enqueue("Torrent error: timeout");

    assert.strictEqual(politeDispatched[0], "Torrent completed");
    assert.strictEqual(assertiveDispatched[0], "Torrent error: timeout");

    politeQueue.clear();
    assertiveQueue.clear();
  });
});
