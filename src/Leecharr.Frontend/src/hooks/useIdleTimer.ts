import { useState, useEffect, useRef, useCallback } from "react";

export const IDLE_TIMEOUT_STORAGE_KEY = "leecharr-idle-timeout";
export const DEFAULT_IDLE_TIMEOUT_SECONDS = 15 * 60;
export const DEFAULT_WARNING_SECONDS = 60;

export interface TimeoutOption {
  value: number;
  label: string;
}

export const TIMEOUT_OPTIONS: TimeoutOption[] = [
  { value: 0, label: "Disabled" },
  { value: 300, label: "5 minutes" },
  { value: 900, label: "15 minutes (Default)" },
  { value: 1800, label: "30 minutes" },
  { value: 3600, label: "1 hour" },
];

/**
 * Retrieves the configured idle timeout from localStorage.
 * Defaults to 900 seconds (15 minutes).
 */
export function getStoredIdleTimeout(): number {
  if (typeof window === "undefined" || !window.localStorage) {
    return DEFAULT_IDLE_TIMEOUT_SECONDS;
  }
  try {
    const raw = window.localStorage.getItem(IDLE_TIMEOUT_STORAGE_KEY);
    if (raw === null || raw === undefined) {
      return DEFAULT_IDLE_TIMEOUT_SECONDS;
    }
    const parsed = parseInt(raw, 10);
    return Number.isNaN(parsed) || parsed < 0
      ? DEFAULT_IDLE_TIMEOUT_SECONDS
      : parsed;
  } catch {
    return DEFAULT_IDLE_TIMEOUT_SECONDS;
  }
}

/**
 * Stores the idle timeout preference to localStorage and dispatches a storage event.
 */
export function setStoredIdleTimeout(seconds: number): void {
  const val = Math.max(0, seconds);
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      window.localStorage.setItem(IDLE_TIMEOUT_STORAGE_KEY, String(val));
      if (typeof window.dispatchEvent === "function") {
        window.dispatchEvent(
          new StorageEvent("storage", {
            key: IDLE_TIMEOUT_STORAGE_KEY,
            newValue: String(val),
          }),
        );
      }
    } catch {
      // ignore quota or security exceptions
    }
  }
}

export interface IdleTimerTrackerOptions {
  timeoutSeconds?: number;
  warningSeconds?: number;
  enabled?: boolean;
  throttleMs?: number;
  onIdle?: () => void;
  onWarning?: (remainingSeconds: number) => void;
  onActive?: () => void;
  onChange?: (state: IdleTimerState) => void;
}

export interface IdleTimerState {
  isIdle: boolean;
  isWarning: boolean;
  remainingSeconds: number;
  timeoutSeconds: number;
}

export class IdleTimerTracker {
  private timeoutSeconds: number;
  private warningSeconds: number;
  private enabled: boolean;
  private throttleMs: number;
  private isIdle = false;
  private isWarning = false;
  private remainingSeconds: number;
  private lastActivity: number;
  private lastThrottled = 0;
  private timerId: ReturnType<typeof setInterval> | null = null;
  private boundActivityHandler: (() => void) | null = null;
  private boundStorageHandler: ((e: StorageEvent) => void) | null = null;

  public onIdle?: () => void;
  public onWarning?: (remainingSeconds: number) => void;
  public onActive?: () => void;
  public onChange?: (state: IdleTimerState) => void;

  constructor(options: IdleTimerTrackerOptions = {}) {
    this.timeoutSeconds =
      options.timeoutSeconds !== undefined
        ? options.timeoutSeconds
        : getStoredIdleTimeout();
    this.warningSeconds = options.warningSeconds ?? DEFAULT_WARNING_SECONDS;
    this.enabled = options.enabled ?? true;
    this.throttleMs = options.throttleMs ?? 500;
    this.remainingSeconds = this.timeoutSeconds;
    this.lastActivity = Date.now();

    this.onIdle = options.onIdle;
    this.onWarning = options.onWarning;
    this.onActive = options.onActive;
    this.onChange = options.onChange;

    this.setupListeners();
    this.startInterval();
  }

  public getState(): IdleTimerState {
    return {
      isIdle: this.isIdle,
      isWarning: this.isWarning,
      remainingSeconds: this.remainingSeconds,
      timeoutSeconds: this.timeoutSeconds,
    };
  }

  private emitChange(): void {
    if (this.onChange) {
      this.onChange(this.getState());
    }
  }

  public setTimeoutSeconds(seconds: number): void {
    this.timeoutSeconds = Math.max(0, seconds);
    this.remainingSeconds = this.timeoutSeconds;
    this.isWarning = false;
    this.lastActivity = Date.now();
    setStoredIdleTimeout(this.timeoutSeconds);
    this.emitChange();
  }

  public setEnabled(enabled: boolean): void {
    this.enabled = enabled;
    if (!enabled) {
      this.isWarning = false;
      this.emitChange();
    }
  }

  public reset(): void {
    this.lastActivity = Date.now();
    this.isIdle = false;
    this.isWarning = false;
    this.remainingSeconds = this.timeoutSeconds;
    this.emitChange();
    this.onActive?.();
  }

  public lock(): void {
    this.isIdle = true;
    this.isWarning = false;
    this.remainingSeconds = 0;
    this.emitChange();
    this.onIdle?.();
  }

  public unlock(): void {
    this.reset();
  }

  public recordActivity(now: number = Date.now()): boolean {
    // If screen is locked, activity does NOT unlock it!
    if (this.isIdle) {
      return false;
    }

    if (now - this.lastThrottled < this.throttleMs) {
      return false;
    }

    this.lastThrottled = now;
    this.lastActivity = now;

    const hadWarning = this.isWarning;
    this.isWarning = false;
    this.remainingSeconds = this.timeoutSeconds;
    this.emitChange();

    if (hadWarning) {
      this.onActive?.();
    }
    return true;
  }

  public tick(now: number = Date.now()): void {
    if (!this.enabled || this.timeoutSeconds <= 0 || this.isIdle) {
      if ((!this.enabled || this.timeoutSeconds <= 0) && this.isWarning) {
        this.isWarning = false;
        this.emitChange();
      }
      return;
    }

    const elapsed = Math.floor((now - this.lastActivity) / 1000);
    const remaining = Math.max(0, this.timeoutSeconds - elapsed);
    this.remainingSeconds = remaining;

    if (remaining <= 0) {
      this.isIdle = true;
      this.isWarning = false;
      this.remainingSeconds = 0;
      this.emitChange();
      this.onIdle?.();
    } else if (remaining <= this.warningSeconds) {
      const wasWarning = this.isWarning;
      this.isWarning = true;
      this.emitChange();
      if (!wasWarning) {
        this.onWarning?.(remaining);
      }
    } else {
      if (this.isWarning) {
        this.isWarning = false;
        this.emitChange();
      }
    }
  }

  private setupListeners(): void {
    if (
      typeof window === "undefined" ||
      typeof window.addEventListener !== "function"
    ) {
      return;
    }

    const events = [
      "keydown",
      "pointerdown",
      "mousemove",
      "touchstart",
      "scroll",
      "wheel",
    ];

    this.boundActivityHandler = () => {
      this.recordActivity();
    };

    events.forEach((evt) => {
      window.addEventListener(evt, this.boundActivityHandler!, {
        passive: true,
      });
    });

    this.boundStorageHandler = (e: StorageEvent) => {
      if (e.key === IDLE_TIMEOUT_STORAGE_KEY && e.newValue !== null) {
        const val = parseInt(e.newValue, 10);
        if (!Number.isNaN(val) && val >= 0 && val !== this.timeoutSeconds) {
          this.timeoutSeconds = val;
          this.remainingSeconds = val;
          this.isWarning = false;
          this.lastActivity = Date.now();
          this.emitChange();
        }
      }
    };

    window.addEventListener("storage", this.boundStorageHandler);
  }

  private startInterval(): void {
    if (typeof setInterval === "function") {
      this.timerId = setInterval(() => {
        this.tick();
      }, 1000);
    }
  }

  public destroy(): void {
    if (this.timerId) {
      clearInterval(this.timerId);
      this.timerId = null;
    }

    if (
      typeof window !== "undefined" &&
      typeof window.removeEventListener === "function"
    ) {
      if (this.boundActivityHandler) {
        const events = [
          "keydown",
          "pointerdown",
          "mousemove",
          "touchstart",
          "scroll",
          "wheel",
        ];
        events.forEach((evt) => {
          window.removeEventListener(evt, this.boundActivityHandler!);
        });
        this.boundActivityHandler = null;
      }

      if (this.boundStorageHandler) {
        window.removeEventListener("storage", this.boundStorageHandler);
        this.boundStorageHandler = null;
      }
    }
  }
}

export interface UseIdleTimerOptions {
  timeoutSeconds?: number;
  warningSeconds?: number;
  enabled?: boolean;
  onIdle?: () => void;
  onWarning?: (remainingSeconds: number) => void;
  onActive?: () => void;
}

export interface UseIdleTimerReturn {
  isIdle: boolean;
  isWarning: boolean;
  remainingSeconds: number;
  timeoutSeconds: number;
  setTimeoutSeconds: (seconds: number) => void;
  resetTimer: () => void;
  lockSession: () => void;
  unlockSession: () => void;
}

/**
 * React Hook for tracking user activity and session inactivity timeout.
 */
export function useIdleTimer(
  options: UseIdleTimerOptions = {},
): UseIdleTimerReturn {
  const {
    timeoutSeconds: explicitTimeout,
    warningSeconds = DEFAULT_WARNING_SECONDS,
    enabled = true,
    onIdle,
    onWarning,
    onActive,
  } = options;

  const [state, setState] = useState<IdleTimerState>(() => {
    const timeout =
      explicitTimeout !== undefined ? explicitTimeout : getStoredIdleTimeout();
    return {
      isIdle: false,
      isWarning: false,
      remainingSeconds: timeout,
      timeoutSeconds: timeout,
    };
  });

  const trackerRef = useRef<IdleTimerTracker | null>(null);
  const callbacksRef = useRef({ onIdle, onWarning, onActive });
  callbacksRef.current = { onIdle, onWarning, onActive };

  useEffect(() => {
    const tracker = new IdleTimerTracker({
      timeoutSeconds: explicitTimeout,
      warningSeconds,
      enabled,
      onIdle: () => callbacksRef.current.onIdle?.(),
      onWarning: (sec) => callbacksRef.current.onWarning?.(sec),
      onActive: () => callbacksRef.current.onActive?.(),
      onChange: (next) => setState(next),
    });

    trackerRef.current = tracker;

    return () => {
      tracker.destroy();
      trackerRef.current = null;
    };
  }, [explicitTimeout, warningSeconds]);

  useEffect(() => {
    if (trackerRef.current) {
      trackerRef.current.setEnabled(enabled);
    }
  }, [enabled]);

  const setTimeoutSeconds = useCallback((seconds: number) => {
    trackerRef.current?.setTimeoutSeconds(seconds);
  }, []);

  const resetTimer = useCallback(() => {
    trackerRef.current?.reset();
  }, []);

  const lockSession = useCallback(() => {
    trackerRef.current?.lock();
  }, []);

  const unlockSession = useCallback(() => {
    trackerRef.current?.unlock();
  }, []);

  return {
    isIdle: state.isIdle,
    isWarning: state.isWarning,
    remainingSeconds: state.remainingSeconds,
    timeoutSeconds: state.timeoutSeconds,
    setTimeoutSeconds,
    resetTimer,
    lockSession,
    unlockSession,
  };
}
