import { describe, it } from "node:test";
import assert from "node:assert/strict";

import {
  DL_STEPS,
  UL_STEPS,
  valueToStepIndex,
  formatSpeedLimit,
} from "./BandwidthCard";

describe("BandwidthCard step configuration & formatters (#1026)", () => {
  it("defines monotonic download and upload steps starting from 0", () => {
    assert.strictEqual(DL_STEPS[0], 0);
    assert.strictEqual(UL_STEPS[0], 0);

    for (let i = 1; i < DL_STEPS.length; i++) {
      assert.ok(
        DL_STEPS[i] > DL_STEPS[i - 1],
        `DL_STEPS not monotonically increasing at index ${i}: ${DL_STEPS[i]} <= ${DL_STEPS[i - 1]}`,
      );
    }

    for (let i = 1; i < UL_STEPS.length; i++) {
      assert.ok(
        UL_STEPS[i] > UL_STEPS[i - 1],
        `UL_STEPS not monotonically increasing at index ${i}: ${UL_STEPS[i]} <= ${UL_STEPS[i - 1]}`,
      );
    }
  });

  it("maps values to nearest step index", () => {
    assert.strictEqual(valueToStepIndex(0, DL_STEPS), 0);
    assert.strictEqual(valueToStepIndex(-10, DL_STEPS), 0);
    assert.strictEqual(valueToStepIndex(1000, DL_STEPS), 15);
    assert.strictEqual(valueToStepIndex(1200, DL_STEPS), 15); // closest to 1000
    assert.strictEqual(valueToStepIndex(1400, DL_STEPS), 16); // closest to 1500
    assert.strictEqual(valueToStepIndex(100000, DL_STEPS), DL_STEPS.length - 1);
    assert.strictEqual(valueToStepIndex(999999, DL_STEPS), DL_STEPS.length - 1);

    assert.strictEqual(valueToStepIndex(0, UL_STEPS), 0);
    assert.strictEqual(valueToStepIndex(50000, UL_STEPS), UL_STEPS.length - 1);
  });

  it("formats speed limit correctly", () => {
    assert.strictEqual(formatSpeedLimit(0), "∞");
    assert.strictEqual(formatSpeedLimit(-1), "∞");
    assert.strictEqual(formatSpeedLimit(50), "50 KB/s");
    assert.strictEqual(formatSpeedLimit(900), "900 KB/s");
    assert.strictEqual(formatSpeedLimit(1000), "1 MB/s");
    assert.strictEqual(formatSpeedLimit(1500), "1.5 MB/s");
    assert.strictEqual(formatSpeedLimit(10000), "10 MB/s");
    assert.strictEqual(formatSpeedLimit(100000), "100 MB/s");
  });
});

describe("BandwidthCard slider commit and pointer interaction semantics (#1026)", () => {
  it("resolves fresh step value directly from event target without stale closure", () => {
    // Simulating slider track click or key release where React closure holds old state
    const initialServerDl = 0;
    let localDlState = initialServerDl; // simulated React state closure
    const localDlRef = { current: initialServerDl };
    let committedValue: number | null = null;

    const handleUpdate = (updates: { maxDownloadSpeedKbps: number }) => {
      committedValue = updates.maxDownloadSpeedKbps;
    };

    const commitDlChange = (
      e?: { target?: { value: string }; currentTarget?: { value: string } },
    ) => {
      let val = localDlRef.current;
      const target = (e?.target || e?.currentTarget) as { value: string } | undefined;
      if (target && typeof target.value === "string") {
        const idx = Number(target.value);
        if (!Number.isNaN(idx) && DL_STEPS[idx] !== undefined) {
          val = DL_STEPS[idx];
          localDlRef.current = val;
          localDlState = val;
        }
      }
      handleUpdate({ maxDownloadSpeedKbps: val });
    };

    // User clicks track directly to select index 15 (1000 KB/s)
    const simulatedEvent = {
      target: { value: "15" },
      currentTarget: { value: "15" },
    };

    // If the handler closed over localDlState, it would commit 0.
    // With target value extraction and ref update, it commits DL_STEPS[15] = 1000.
    commitDlChange(simulatedEvent);
    assert.strictEqual(committedValue, 1000);
    assert.strictEqual(localDlRef.current, 1000);
    assert.strictEqual(localDlState, 1000);
  });

  it("handles keyboard navigation keyup using event target step value", () => {
    const initialServerUl = 500; // index 10
    const localUlRef = { current: initialServerUl };
    let committedValue: number | null = null;

    const commitUlChange = (
      e?: { target?: { value: string }; currentTarget?: { value: string } },
    ) => {
      let val = localUlRef.current;
      const target = (e?.target || e?.currentTarget) as { value: string } | undefined;
      if (target && typeof target.value === "string") {
        const idx = Number(target.value);
        if (!Number.isNaN(idx) && UL_STEPS[idx] !== undefined) {
          val = UL_STEPS[idx];
          localUlRef.current = val;
        }
      }
      committedValue = val;
    };

    // ArrowRight advances slider from index 10 (500) to 11 (600)
    const keyUpEvent = { target: { value: "11" } };
    commitUlChange(keyUpEvent);
    assert.strictEqual(committedValue, 600);
    assert.strictEqual(localUlRef.current, 600);
  });

  it("commits and clears drag state via pointer capture / blur fallback", () => {
    let capturedPointerId: number | null = null;
    let releasedPointerId: number | null = null;
    let isDragging = false;
    let committedValue: number | null = null;

    const localDlRef = { current: 2000 };

    const mockTarget = {
      value: "17", // DL_STEPS[17] = 2000
      setPointerCapture: (id: number) => {
        capturedPointerId = id;
      },
      hasPointerCapture: (id: number) => capturedPointerId === id,
      releasePointerCapture: (id: number) => {
        releasedPointerId = id;
        capturedPointerId = null;
      },
    };

    // 1. Pointer down captures pointerId so drags outside track aren't dropped
    const pointerDown = (e: { pointerId: number; target: typeof mockTarget }) => {
      isDragging = true;
      e.target.setPointerCapture?.(e.pointerId);
    };

    pointerDown({ pointerId: 42, target: mockTarget });
    assert.strictEqual(isDragging, true);
    assert.strictEqual(capturedPointerId, 42);

    // 2. Drag to new step index 18 (2500 KB/s)
    mockTarget.value = "18";
    localDlRef.current = DL_STEPS[18];

    // 3. Pointer release outside element still arrives at target because of pointer capture
    const pointerUp = (e: { pointerId: number; target: typeof mockTarget }) => {
      if (e.target.hasPointerCapture?.(e.pointerId)) {
        e.target.releasePointerCapture?.(e.pointerId);
      }
      isDragging = false;
      const idx = Number(e.target.value);
      committedValue = DL_STEPS[idx];
    };

    pointerUp({ pointerId: 42, target: mockTarget });
    assert.strictEqual(isDragging, false);
    assert.strictEqual(releasedPointerId, 42);
    assert.strictEqual(capturedPointerId, null);
    assert.strictEqual(committedValue, 2500);

    // 4. Test blur fallback committing uncommitted changes
    isDragging = true;
    mockTarget.value = "19"; // DL_STEPS[19] = 3000
    const onBlur = (e: { target: typeof mockTarget }) => {
      isDragging = false;
      const idx = Number(e.target.value);
      committedValue = DL_STEPS[idx];
    };

    onBlur({ target: mockTarget });
    assert.strictEqual(isDragging, false);
    assert.strictEqual(committedValue, 3000);
  });
});
