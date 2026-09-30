import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import {
  STORAGE_KEY_HIDE_GUIDE,
  getHideGuideKey,
  isMaskedApiKey,
  shouldHideGettingStarted,
} from "./GettingStartedModal";

class MockLocalStorage {
  private store = new Map<string, string>();

  getItem(key: string): string | null {
    return this.store.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  clear(): void {
    this.store.clear();
  }
}

describe("GettingStartedModal (#1019)", () => {
  let mockStorage: MockLocalStorage;
  const originalLocalStorage = globalThis.localStorage;

  beforeEach(() => {
    mockStorage = new MockLocalStorage();
    Object.defineProperty(globalThis, "localStorage", {
      value: mockStorage,
      writable: true,
      configurable: true,
    });
    (globalThis as unknown as { window: unknown }).window = {
      localStorage: mockStorage,
    };
  });

  afterEach(() => {
    delete (globalThis as unknown as { window?: unknown }).window;
    Object.defineProperty(globalThis, "localStorage", {
      value: originalLocalStorage,
      writable: true,
      configurable: true,
    });
  });

  describe("getHideGuideKey", () => {
    it("returns global key when instanceUuid is not provided", () => {
      assert.strictEqual(getHideGuideKey(), STORAGE_KEY_HIDE_GUIDE);
      assert.strictEqual(getHideGuideKey(undefined), STORAGE_KEY_HIDE_GUIDE);
      assert.strictEqual(getHideGuideKey(""), STORAGE_KEY_HIDE_GUIDE);
    });

    it("returns scoped key when instanceUuid is provided", () => {
      assert.strictEqual(
        getHideGuideKey("inst-1234"),
        `${STORAGE_KEY_HIDE_GUIDE}_inst-1234`,
      );
    });
  });

  describe("shouldHideGettingStarted", () => {
    it("returns false when no preference is saved", () => {
      assert.strictEqual(shouldHideGettingStarted("inst-1234"), false);
      assert.strictEqual(shouldHideGettingStarted(), false);
    });

    it("returns true when global preference is true even if instance key is null", () => {
      mockStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "true");
      assert.strictEqual(shouldHideGettingStarted("inst-1234"), true);
      assert.strictEqual(shouldHideGettingStarted(), true);
    });

    it("returns true when instance key is true even if global key is null", () => {
      mockStorage.setItem(`${STORAGE_KEY_HIDE_GUIDE}_inst-1234`, "true");
      assert.strictEqual(shouldHideGettingStarted("inst-1234"), true);
      assert.strictEqual(shouldHideGettingStarted("inst-other"), false);
    });

    it("returns false when both keys are explicitly false", () => {
      mockStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "false");
      mockStorage.setItem(`${STORAGE_KEY_HIDE_GUIDE}_inst-1234`, "false");
      assert.strictEqual(shouldHideGettingStarted("inst-1234"), false);
    });
  });

  describe("isMaskedApiKey", () => {
    it("returns true for undefined or empty apiKey", () => {
      assert.strictEqual(isMaskedApiKey(undefined), true);
      assert.strictEqual(isMaskedApiKey(""), true);
    });

    it("returns true for asterisk-masked keys", () => {
      assert.strictEqual(isMaskedApiKey("********"), true);
      assert.strictEqual(isMaskedApiKey("abc*123"), true);
      assert.strictEqual(isMaskedApiKey("*"), true);
    });

    it("returns false for genuine unmasked API keys", () => {
      assert.strictEqual(isMaskedApiKey("d41d8cd98f00b204e9800998ecf8427e"), false);
      assert.strictEqual(isMaskedApiKey("SonarrApiKey12345"), false);
    });
  });
});
