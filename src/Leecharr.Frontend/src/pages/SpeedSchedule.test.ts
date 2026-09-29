import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { isScheduleInSlot, isHourInSchedule } from "./SpeedSchedule";
import type { SpeedScheduleEntry } from "../api/types";

function createMockSchedule(
  overrides: Partial<SpeedScheduleEntry> = {},
): SpeedScheduleEntry {
  return {
    id: 1,
    name: "Test Schedule",
    days: 127,
    startTime: "00:00",
    endTime: "23:59",
    maxUploadSpeed: 1024,
    maxDownloadSpeed: 2048,
    isEnabled: true,
    priority: 0,
    ...overrides,
  };
}

describe("SpeedSchedule: isScheduleInSlot (#995)", () => {
  it("treats full-day bounds (00:00 to 24:00) as active across all 24 hours", () => {
    const schedule = createMockSchedule({
      startTime: "00:00",
      endTime: "24:00",
      days: 2, // Monday only
    });

    for (let hour = 0; hour < 24; hour++) {
      assert.strictEqual(
        isScheduleInSlot(schedule, hour, 2),
        true,
        `Hour ${hour} should be active for full-day 24:00 schedule`,
      );
      assert.strictEqual(
        isScheduleInSlot(schedule, hour, 1),
        false,
        `Hour ${hour} should not be active for non-matching day`,
      );
    }
  });

  it("treats full-day bounds (00:00 to 23:59) as active across all 24 hours", () => {
    const schedule = createMockSchedule({
      startTime: "00:00",
      endTime: "23:59",
      days: 4, // Tuesday only
    });

    for (let hour = 0; hour < 24; hour++) {
      assert.strictEqual(
        isScheduleInSlot(schedule, hour, 4),
        true,
        `Hour ${hour} should be active for full-day 23:59 schedule`,
      );
    }
  });

  it("does not treat identical non-full-day start and end times (e.g. 14:00 to 14:00) as a 24-hour schedule", () => {
    const schedule = createMockSchedule({
      startTime: "14:00",
      endTime: "14:00",
      days: 2, // Monday only
    });

    for (let hour = 0; hour < 24; hour++) {
      const expected = hour === 14;
      assert.strictEqual(
        isScheduleInSlot(schedule, hour, 2),
        expected,
        `Hour ${hour} expected ${expected} for 14:00-14:00 schedule`,
      );
    }
  });

  it("does not treat 00:00 to 00:00 as a 24-hour schedule, only hour 0 is active", () => {
    const schedule = createMockSchedule({
      startTime: "00:00",
      endTime: "00:00",
      days: 2,
    });

    for (let hour = 0; hour < 24; hour++) {
      const expected = hour === 0;
      assert.strictEqual(
        isScheduleInSlot(schedule, hour, 2),
        expected,
        `Hour ${hour} expected ${expected} for 00:00-00:00 schedule`,
      );
    }
  });

  it("correctly matches fractional currentHour when startH === endH", () => {
    const schedule = createMockSchedule({
      startTime: "14:00",
      endTime: "14:00",
      days: 2,
    });

    // 14:30
    assert.strictEqual(isScheduleInSlot(schedule, 14.5, 2), true);
    // 15:15
    assert.strictEqual(isScheduleInSlot(schedule, 15.25, 2), false);
    // 13:45
    assert.strictEqual(isScheduleInSlot(schedule, 13.75, 2), false);
  });

  it("correctly handles regular daytime windows (e.g. 09:00 to 17:00)", () => {
    const schedule = createMockSchedule({
      startTime: "09:00",
      endTime: "17:00",
      days: 2,
    });

    assert.strictEqual(isScheduleInSlot(schedule, 8, 2), false);
    assert.strictEqual(isScheduleInSlot(schedule, 9, 2), true);
    assert.strictEqual(isScheduleInSlot(schedule, 16, 2), true);
    assert.strictEqual(isScheduleInSlot(schedule, 17, 2), false);
  });

  it("correctly handles overnight windows spanning midnight (e.g. 22:00 to 04:00)", () => {
    const schedule = createMockSchedule({
      startTime: "22:00",
      endTime: "04:00",
      days: 2, // Monday (dayValue = 2)
    });

    // Monday evening: hours 22 and 23 should be active on day 2
    assert.strictEqual(isScheduleInSlot(schedule, 21, 2), false);
    assert.strictEqual(isScheduleInSlot(schedule, 22, 2), true);
    assert.strictEqual(isScheduleInSlot(schedule, 23, 2), true);

    // Tuesday morning: hours 0..3 should be active on Tuesday (dayValue = 4)
    assert.strictEqual(isScheduleInSlot(schedule, 0, 4), true);
    assert.strictEqual(isScheduleInSlot(schedule, 3, 4), true);
    assert.strictEqual(isScheduleInSlot(schedule, 4, 4), false);
  });
});

describe("SpeedSchedule: isHourInSchedule", () => {
  it("returns false if schedule is disabled regardless of slot match", () => {
    const disabled = createMockSchedule({
      startTime: "00:00",
      endTime: "24:00",
      isEnabled: false,
    });

    assert.strictEqual(isHourInSchedule(disabled, 12, 127), false);
  });

  it("returns true if schedule is enabled and slot matches", () => {
    const enabled = createMockSchedule({
      startTime: "00:00",
      endTime: "24:00",
      isEnabled: true,
    });

    assert.strictEqual(isHourInSchedule(enabled, 12, 127), true);
  });
});
