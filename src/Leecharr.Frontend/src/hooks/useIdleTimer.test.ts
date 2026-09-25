import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import {
  IdleTimerTracker,
  getStoredIdleTimeout,
  setStoredIdleTimeout,
  IDLE_TIMEOUT_STORAGE_KEY,
  DEFAULT_IDLE_TIMEOUT_SECONDS,
  TIMEOUT_OPTIONS,
} from "./useIdleTimer";

interface MockWindow {
  localStorage: MockLocalStorage;
  addEventListener: (type: string, listener: EventListener | ((e: StorageEvent) => void)) => void;
  removeEventListener: (type: string, listener: EventListener | ((e: StorageEvent) => void)) => void;
  dispatchEvent: (e: Event | { type: string; [key: string]: unknown }) => boolean;
}

interface TestGlobal {
  window?: MockWindow | unknown;
  __storageListeners?: Array<(e: StorageEvent) => void>;
}

const testGlobal = globalThis as unknown as TestGlobal;

class MockLocalStorage {
  private store = new Map<string, string>();

  getItem(key: string): string | null {
    return this.store.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    const oldValue = this.store.get(key) ?? null;
    this.store.set(key, value);

    if (typeof testGlobal.window !== "undefined") {
      const storageEvent = {
        key,
        newValue: value,
        oldValue,
      } as StorageEvent;

      const listeners = testGlobal.__storageListeners || [];
      for (const listener of listeners) {
        listener(storageEvent);
      }
    }
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  clear(): void {
    this.store.clear();
  }
}

describe("useIdleTimer / IdleTimerTracker", () => {
  let originalWindow: unknown;
  let mockStorage: MockLocalStorage;
  let storageListeners: Array<(e: StorageEvent) => void>;
  let windowListeners: Map<string, Set<EventListener>>;

  beforeEach(() => {
    originalWindow = testGlobal.window;
    mockStorage = new MockLocalStorage();
    storageListeners = [];
    windowListeners = new Map();

    testGlobal.__storageListeners = storageListeners;
    testGlobal.window = {
      localStorage: mockStorage,
      addEventListener: (type: string, listener: EventListener | ((e: StorageEvent) => void)) => {
        if (type === "storage") {
          storageListeners.push(listener as (e: StorageEvent) => void);
        } else {
          if (!windowListeners.has(type)) {
            windowListeners.set(type, new Set());
          }
          windowListeners.get(type)?.add(listener as EventListener);
        }
      },
      removeEventListener: (type: string, listener: EventListener | ((e: StorageEvent) => void)) => {
        if (type === "storage") {
          const idx = storageListeners.indexOf(listener as (e: StorageEvent) => void);
          if (idx !== -1) storageListeners.splice(idx, 1);
        } else {
          windowListeners.get(type)?.delete(listener as EventListener);
        }
      },
      dispatchEvent: (e: Event | { type: string; [key: string]: unknown }) => {
        if (e.type === "storage") {
          for (const l of storageListeners) {
            l(e as StorageEvent);
          }
        }
        return true;
      },
    } as MockWindow;
  });

  afterEach(() => {
    testGlobal.window = originalWindow;
    delete testGlobal.__storageListeners;
  });

  it("loads default timeout when localStorage is empty", () => {
    assert.equal(getStoredIdleTimeout(), DEFAULT_IDLE_TIMEOUT_SECONDS);
  });

  it("loads and parses valid timeout from localStorage", () => {
    mockStorage.setItem(IDLE_TIMEOUT_STORAGE_KEY, "300");
    assert.equal(getStoredIdleTimeout(), 300);

    mockStorage.setItem(IDLE_TIMEOUT_STORAGE_KEY, "0");
    assert.equal(getStoredIdleTimeout(), 0);
  });

  it("falls back to default on invalid or corrupted storage values", () => {
    mockStorage.setItem(IDLE_TIMEOUT_STORAGE_KEY, "not-a-number");
    assert.equal(getStoredIdleTimeout(), DEFAULT_IDLE_TIMEOUT_SECONDS);

    mockStorage.setItem(IDLE_TIMEOUT_STORAGE_KEY, "-50");
    assert.equal(getStoredIdleTimeout(), DEFAULT_IDLE_TIMEOUT_SECONDS);
  });

  it("setStoredIdleTimeout saves to localStorage and dispatches storage event", () => {
    let receivedEvent: StorageEvent | null = null;
    (testGlobal.window as MockWindow).addEventListener(
      "storage",
      (e: StorageEvent) => {
        receivedEvent = e;
      },
    );

    setStoredIdleTimeout(1800);
    assert.equal(mockStorage.getItem(IDLE_TIMEOUT_STORAGE_KEY), "1800");
    assert.ok(receivedEvent);
    assert.equal((receivedEvent as StorageEvent).key, IDLE_TIMEOUT_STORAGE_KEY);
    assert.equal((receivedEvent as StorageEvent).newValue, "1800");
  });

  it("provides expected timeout options with disabled and preset options", () => {
    assert.ok(TIMEOUT_OPTIONS.some((o) => o.value === 0));
    assert.ok(TIMEOUT_OPTIONS.some((o) => o.value === 300));
    assert.ok(TIMEOUT_OPTIONS.some((o) => o.value === 900));
    assert.ok(TIMEOUT_OPTIONS.some((o) => o.value === 1800));
    assert.ok(TIMEOUT_OPTIONS.some((o) => o.value === 3600));
  });

  it("tracks countdown and transitions to warning window then idle state", () => {
    let idleTriggered = false;
    let warningTriggeredWith: number | null = null;
    const tracker = new IdleTimerTracker({
      timeoutSeconds: 10,
      warningSeconds: 4,
      onIdle: () => {
        idleTriggered = true;
      },
      onWarning: (sec) => {
        warningTriggeredWith = sec;
      },
    });

    try {
      const state0 = tracker.getState();
      assert.equal(state0.isIdle, false);
      assert.equal(state0.isWarning, false);
      assert.equal(state0.remainingSeconds, 10);

      // Tick 5 seconds later: 5s remaining, warning threshold is 4s -> neither warning nor idle
      const baseTime = Date.now();
      tracker.tick(baseTime + 5000);
      assert.equal(tracker.getState().isIdle, false);
      assert.equal(tracker.getState().isWarning, false);
      assert.equal(tracker.getState().remainingSeconds, 5);

      // Tick 7 seconds later: 3s remaining, <= warningSeconds (4s) -> isWarning true
      tracker.tick(baseTime + 7000);
      assert.equal(tracker.getState().isIdle, false);
      assert.equal(tracker.getState().isWarning, true);
      assert.equal(tracker.getState().remainingSeconds, 3);
      assert.equal(warningTriggeredWith, 3);

      // Tick 10 seconds later: 0s remaining -> isIdle true
      tracker.tick(baseTime + 10000);
      assert.equal(tracker.getState().isIdle, true);
      assert.equal(tracker.getState().isWarning, false);
      assert.equal(tracker.getState().remainingSeconds, 0);
      assert.equal(idleTriggered, true);
    } finally {
      tracker.destroy();
    }
  });

  it("user activity resets timer and clears warning state", () => {
    let activeTriggered = false;
    const tracker = new IdleTimerTracker({
      timeoutSeconds: 10,
      warningSeconds: 4,
      onActive: () => {
        activeTriggered = true;
      },
    });

    try {
      const baseTime = Date.now();
      tracker.tick(baseTime + 8000);
      assert.equal(tracker.getState().isWarning, true);

      // Activity occurs
      const handled = tracker.recordActivity(baseTime + 8500);
      assert.equal(handled, true);
      assert.equal(tracker.getState().isWarning, false);
      assert.equal(tracker.getState().remainingSeconds, 10);
      assert.equal(activeTriggered, true);
    } finally {
      tracker.destroy();
    }
  });

  it("user activity does NOT dismiss idle lock once already idle", () => {
    const tracker = new IdleTimerTracker({
      timeoutSeconds: 5,
      warningSeconds: 2,
    });

    try {
      tracker.lock();
      assert.equal(tracker.getState().isIdle, true);

      const handled = tracker.recordActivity(Date.now() + 10000);
      assert.equal(handled, false);
      assert.equal(tracker.getState().isIdle, true);
    } finally {
      tracker.destroy();
    }
  });

  it("manual lock immediately locks session and manual unlock resets it", () => {
    let idleTriggered = false;
    const tracker = new IdleTimerTracker({
      timeoutSeconds: 100,
      onIdle: () => {
        idleTriggered = true;
      },
    });

    try {
      tracker.lock();
      assert.equal(tracker.getState().isIdle, true);
      assert.equal(tracker.getState().remainingSeconds, 0);
      assert.equal(idleTriggered, true);

      tracker.unlock();
      assert.equal(tracker.getState().isIdle, false);
      assert.equal(tracker.getState().remainingSeconds, 100);
    } finally {
      tracker.destroy();
    }
  });

  it("disabled timeout (0) never triggers warning or idle", () => {
    let idleTriggered = false;
    let warningTriggered = false;
    const tracker = new IdleTimerTracker({
      timeoutSeconds: 0,
      warningSeconds: 60,
      onIdle: () => {
        idleTriggered = true;
      },
      onWarning: () => {
        warningTriggered = true;
      },
    });

    try {
      const baseTime = Date.now();
      tracker.tick(baseTime + 100000);
      assert.equal(tracker.getState().isIdle, false);
      assert.equal(tracker.getState().isWarning, false);
      assert.equal(idleTriggered, false);
      assert.equal(warningTriggered, false);
    } finally {
      tracker.destroy();
    }
  });

  it("synchronizes timeout changes via storage events", () => {
    const tracker = new IdleTimerTracker({
      timeoutSeconds: 900,
    });

    try {
      assert.equal(tracker.getState().timeoutSeconds, 900);

      // Simulate a change in another tab via storage event
      const storageEvent = {
        key: IDLE_TIMEOUT_STORAGE_KEY,
        newValue: "300",
      } as StorageEvent;

      for (const listener of storageListeners) {
        listener(storageEvent);
      }

      assert.equal(tracker.getState().timeoutSeconds, 300);
      assert.equal(tracker.getState().remainingSeconds, 300);
    } finally {
      tracker.destroy();
    }
  });
});
