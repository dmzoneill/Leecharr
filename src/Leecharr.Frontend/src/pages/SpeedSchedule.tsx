import { useTranslation } from "../i18n";
import React, { useState, useRef, useEffect, useCallback } from "react";
import {
  useSpeedSchedules,
  useActiveSpeedLimits,
  useCreateSpeedSchedule,
  useUpdateSpeedSchedule,
  useDeleteSpeedSchedule,
} from "../api/hooks";
import { formatSpeed } from "../utils/formatters";
import type { SpeedScheduleEntry } from "../api/types";
import { useToast } from "../context/ToastContext";
import { useConfirm } from "../context/ConfirmContext";
import { useEscapeKey } from "../hooks/useEscapeKey";

const DAY_FLAGS = [
  { label: "Sun", value: 1 },
  { label: "Mon", value: 2 },
  { label: "Tue", value: 4 },
  { label: "Wed", value: 8 },
  { label: "Thu", value: 16 },
  { label: "Fri", value: 32 },
  { label: "Sat", value: 64 },
];

const PRESETS = {
  everyday: 127,
  weekdays: 62,
  weekends: 65,
};

const BLOCK_COLORS = [
  "var(--accent, #ffd166)",
  "#06d6a0",
  "#118ab2",
  "#a78bfa",
  "#f78c6c",
  "#4ade80",
];

function daysToLabels(days: number): string {
  if (days === PRESETS.everyday) return "Every day";
  if (days === PRESETS.weekdays) return "Weekdays";
  if (days === PRESETS.weekends) return "Weekends";
  return (
    DAY_FLAGS.filter((d) => (days & d.value) !== 0)
      .map((d) => d.label)
      .join(", ") || "None"
  );
}

function timeToHour(time: string): number {
  const [h, m] = time.split(":").map(Number);
  return h + (m || 0) / 60;
}

export function isScheduleInSlot(
  s: SpeedScheduleEntry,
  hour: number,
  dayValue: number,
): boolean {
  const startH = timeToHour(s.startTime);
  const endH = timeToHour(s.endTime);

  if (startH === endH) {
    return (s.days & dayValue) !== 0;
  }

  if (startH <= endH) {
    return (s.days & dayValue) !== 0 && startH <= hour && endH > hour;
  }

  // Overnight schedule spanning midnight:
  // 1) Evening portion on current day (>= startH)
  if (hour >= startH && (s.days & dayValue) !== 0) return true;
  // 2) Morning portion from previous day (< endH)
  // Days bitflags: Sunday=1, Monday=2, Tuesday=4, Wednesday=8, Thursday=16, Friday=32, Saturday=64
  // Previous day of Sunday (1) is Saturday (64). For other days, dayValue >> 1.
  const prevDayValue = dayValue === 1 ? 64 : dayValue >> 1;
  if (hour < endH && (s.days & prevDayValue) !== 0) return true;

  return false;
}

export function isHourInSchedule(
  s: SpeedScheduleEntry,
  hour: number,
  dayValue: number,
): boolean {
  if (!s.isEnabled) return false;
  return isScheduleInSlot(s, hour, dayValue);
}

const EMPTY_SCHEDULE: Omit<SpeedScheduleEntry, "id"> = {
  name: "",
  days: 127,
  startTime: "00:00",
  endTime: "23:59",
  maxUploadSpeed: 0,
  maxDownloadSpeed: 0,
  isEnabled: true,
  priority: 0,
};

function ScheduleModal({
  schedule,
  onSave,
  onCancel,
  isPending,
}: {
  schedule: Partial<SpeedScheduleEntry>;
  onSave: (s: Partial<SpeedScheduleEntry>) => void;
  onCancel: () => void;
  isPending: boolean;
}) {
  const { t } = useTranslation();
  useEscapeKey(onCancel);
  const [form, setForm] = useState({ ...EMPTY_SCHEDULE, ...schedule });

  function toggleDay(value: number) {
    setForm({ ...form, days: form.days ^ value });
  }

  return (
    <div className="modal-overlay" onClick={onCancel}>
      <div
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          maxWidth: 520,
          borderRadius: "8px",
          boxShadow: "0 16px 40px rgba(0, 0, 0, 0.7)",
          border: "1px solid var(--border)",
        }}
      >
        <h2 style={{ margin: "0 0 1.25rem", fontSize: "1.25rem" }}>
          {schedule.id
            ? t("speedSchedule.editTitle")
            : t("speedSchedule.addTitle")}
        </h2>
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <label>
            <span
              className="status-label"
              style={{
                display: "block",
                marginBottom: "0.25rem",
                fontWeight: 600,
                fontSize: "0.82rem",
              }}
            >
              {t("speedSchedule.scheduleName")}
            </span>
            <input
              className="form-input"
              type="text"
              placeholder={t("speedSchedule.namePlaceholder")}
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              style={{ width: "100%", borderRadius: "6px" }}
            />
          </label>

          <div>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "0.4rem",
              }}
            >
              <span
                className="status-label"
                style={{
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                {t("speedSchedule.activeDays")}
              </span>
              <div style={{ display: "flex", gap: 4 }}>
                <button
                  type="button"
                  className={`btn btn-small ${form.days === PRESETS.everyday ? "btn-primary" : "btn-outline"}`}
                  onClick={() => setForm({ ...form, days: PRESETS.everyday })}
                  style={{ fontSize: "0.72rem", padding: "2px 6px" }}
                >
                  {t("speedSchedule.allDays")}
                </button>
                <button
                  type="button"
                  className={`btn btn-small ${form.days === PRESETS.weekdays ? "btn-primary" : "btn-outline"}`}
                  onClick={() => setForm({ ...form, days: PRESETS.weekdays })}
                  style={{ fontSize: "0.72rem", padding: "2px 6px" }}
                >
                  {t("speedSchedule.workdays")}
                </button>
                <button
                  type="button"
                  className={`btn btn-small ${form.days === PRESETS.weekends ? "btn-primary" : "btn-outline"}`}
                  onClick={() => setForm({ ...form, days: PRESETS.weekends })}
                  style={{ fontSize: "0.72rem", padding: "2px 6px" }}
                >
                  {t("speedSchedule.weekends")}
                </button>
              </div>
            </div>
            <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
              {DAY_FLAGS.map((d) => (
                <button
                  key={d.value}
                  className={`btn btn-small ${form.days & d.value ? "btn-primary" : "btn-outline"}`}
                  onClick={() => toggleDay(d.value)}
                  type="button"
                  style={{ minWidth: "42px", borderRadius: "4px" }}
                >
                  {d.label}
                </button>
              ))}
            </div>
          </div>

          <div style={{ display: "flex", gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                {t("speedSchedule.startTime")}
              </span>
              <input
                className="form-input"
                type="time"
                value={form.startTime}
                onChange={(e) =>
                  setForm({ ...form, startTime: e.target.value })
                }
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                {t("speedSchedule.endTime")}
              </span>
              <input
                className="form-input"
                type="time"
                value={form.endTime}
                onChange={(e) => setForm({ ...form, endTime: e.target.value })}
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
          </div>

          <div style={{ display: "flex", gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                {t("speedSchedule.maxUploadSpeed")}
              </span>
              <input
                className="form-input"
                type="number"
                min={0}
                value={form.maxUploadSpeed || ""}
                onChange={(e) =>
                  setForm({
                    ...form,
                    maxUploadSpeed: Math.max(
                      0,
                      parseInt(e.target.value, 10) || 0,
                    ),
                  })
                }
                placeholder="0"
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                {t("speedSchedule.maxDownloadSpeed")}
              </span>
              <input
                className="form-input"
                type="number"
                min={0}
                value={form.maxDownloadSpeed || ""}
                onChange={(e) =>
                  setForm({
                    ...form,
                    maxDownloadSpeed: Math.max(
                      0,
                      parseInt(e.target.value, 10) || 0,
                    ),
                  })
                }
                placeholder="0"
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
          </div>

          <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                {t("speedSchedule.priority")}
              </span>
              <input
                className="form-input"
                type="number"
                value={form.priority}
                onChange={(e) =>
                  setForm({ ...form, priority: Number(e.target.value) })
                }
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
            <label
              style={{
                flex: 1,
                display: "flex",
                alignItems: "center",
                gap: 8,
                paddingTop: 18,
                cursor: "pointer",
              }}
            >
              <input
                type="checkbox"
                checked={form.isEnabled}
                onChange={(e) =>
                  setForm({ ...form, isEnabled: e.target.checked })
                }
              />
              <span style={{ fontWeight: 600, fontSize: "0.85rem" }}>
                {t("speedSchedule.enableSchedule")}
              </span>
            </label>
          </div>

          <div
            style={{
              display: "flex",
              gap: 8,
              justifyContent: "flex-end",
              marginTop: 10,
              paddingTop: 12,
              borderTop: "1px solid var(--border-light)",
            }}
          >
            <button
              className="btn btn-outline btn-small"
              onClick={onCancel}
              type="button"
            >
              {t("common.cancel")}
            </button>
            <button
              className="btn btn-primary btn-small"
              onClick={() => onSave(form)}
              disabled={isPending || !form.name.trim()}
              type="button"
            >
              {isPending ? t("common.saving") : t("speedSchedule.saveChanges")}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

interface WeeklyCalendarProps {
  schedules: SpeedScheduleEntry[];
  onSelectRange?: (range: {
    days: number;
    startTime: string;
    endTime: string;
  }) => void;
  onToggleSchedule?: (schedule: SpeedScheduleEntry) => void;
}

function WeeklyCalendar({
  schedules,
  onSelectRange,
  onToggleSchedule,
}: WeeklyCalendarProps) {
  const { t } = useTranslation();
  const hours = Array.from({ length: 24 }, (_, i) => i);

  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState<{
    dayIdx: number;
    hour: number;
  } | null>(null);
  const [dragCurrent, setDragCurrent] = useState<{
    dayIdx: number;
    hour: number;
  } | null>(null);

  const [selectedRange, setSelectedRange] = useState<{
    minDay: number;
    maxDay: number;
    minHour: number;
    maxHour: number;
    days: number;
    startTime: string;
    endTime: string;
  } | null>(null);

  const isDraggingRef = useRef(false);
  const dragStartRef = useRef<{ dayIdx: number; hour: number } | null>(null);
  const dragCurrentRef = useRef<{ dayIdx: number; hour: number } | null>(null);
  const hasMovedRef = useRef(false);
  const schedulesRef = useRef(schedules);
  schedulesRef.current = schedules;
  const onToggleScheduleRef = useRef(onToggleSchedule);
  onToggleScheduleRef.current = onToggleSchedule;

  const getScheduleForSlot = useCallback((dayIdx: number, hour: number) => {
    const currentSchedules = schedulesRef.current;
    const day = DAY_FLAGS[dayIdx];
    const matching = currentSchedules.filter((s) =>
      isScheduleInSlot(s, hour, day.value),
    );
    const active = matching.filter((s) => s.isEnabled);
    if (active.length > 0) {
      return [...active].sort(
        (a, b) => (b.priority ?? 0) - (a.priority ?? 0),
      )[0];
    }
    if (matching.length > 0) {
      return [...matching].sort(
        (a, b) => (b.priority ?? 0) - (a.priority ?? 0),
      )[0];
    }
    return null;
  }, []);

  const finishDrag = useCallback(() => {
    if (!isDraggingRef.current) return;
    isDraggingRef.current = false;
    setIsDragging(false);

    const start = dragStartRef.current;
    const current = dragCurrentRef.current;
    const hasMoved = hasMovedRef.current;

    if (!start || !current) return;

    const minD = Math.min(start.dayIdx, current.dayIdx);
    const maxD = Math.max(start.dayIdx, current.dayIdx);
    const minH = Math.min(start.hour, current.hour);
    const maxH = Math.max(start.hour, current.hour);

    if (!hasMoved) {
      // Single click toggle on cell with an existing schedule
      const clickedSlotSchedule = getScheduleForSlot(start.dayIdx, start.hour);
      if (clickedSlotSchedule) {
        onToggleScheduleRef.current?.(clickedSlotSchedule);
        setSelectedRange(null);
        return;
      }
    }

    // Either a drag across cells or a single click on an empty cell
    let dayMask = 0;
    for (let i = minD; i <= maxD; i++) {
      dayMask |= DAY_FLAGS[i].value;
    }
    const startTime = `${String(minH).padStart(2, "0")}:00`;
    const endTime =
      maxH === 23 ? "23:59" : `${String(maxH + 1).padStart(2, "0")}:00`;

    setSelectedRange({
      minDay: minD,
      maxDay: maxD,
      minHour: minH,
      maxHour: maxH,
      days: dayMask,
      startTime,
      endTime,
    });
  }, [getScheduleForSlot]);

  // Window mouseup and touchend listeners when dragging begins
  useEffect(() => {
    if (!isDragging) return;

    const handleGlobalMouseUp = () => {
      finishDrag();
    };
    const handleGlobalTouchEnd = () => {
      finishDrag();
    };

    window.addEventListener("mouseup", handleGlobalMouseUp);
    window.addEventListener("touchend", handleGlobalTouchEnd);
    window.addEventListener("touchcancel", handleGlobalTouchEnd);
    return () => {
      window.removeEventListener("mouseup", handleGlobalMouseUp);
      window.removeEventListener("touchend", handleGlobalTouchEnd);
      window.removeEventListener("touchcancel", handleGlobalTouchEnd);
    };
  }, [isDragging, finishDrag]);

  const handleMouseDown = (
    e: React.MouseEvent,
    dayIdx: number,
    hour: number,
  ) => {
    if (e.button !== 0) return;
    e.preventDefault();
    isDraggingRef.current = true;
    dragStartRef.current = { dayIdx, hour };
    dragCurrentRef.current = { dayIdx, hour };
    hasMovedRef.current = false;
    setIsDragging(true);
    setDragStart({ dayIdx, hour });
    setDragCurrent({ dayIdx, hour });
  };

  const handleMouseEnter = (dayIdx: number, hour: number) => {
    if (!isDraggingRef.current) return;
    if (
      dragStartRef.current &&
      (dragStartRef.current.dayIdx !== dayIdx ||
        dragStartRef.current.hour !== hour)
    ) {
      hasMovedRef.current = true;
    }
    dragCurrentRef.current = { dayIdx, hour };
    setDragCurrent({ dayIdx, hour });
  };

  const handleTouchStart = (
    e: React.TouchEvent,
    dayIdx: number,
    hour: number,
  ) => {
    isDraggingRef.current = true;
    dragStartRef.current = { dayIdx, hour };
    dragCurrentRef.current = { dayIdx, hour };
    hasMovedRef.current = false;
    setIsDragging(true);
    setDragStart({ dayIdx, hour });
    setDragCurrent({ dayIdx, hour });
  };

  const handleTouchMove = (e: React.TouchEvent) => {
    if (!isDraggingRef.current) return;
    const touch = e.touches[0];
    if (!touch) return;
    const target = document.elementFromPoint(touch.clientX, touch.clientY);
    if (!target) return;
    const cell = target.closest<HTMLElement>("[data-calendar-cell]");
    if (
      cell &&
      cell.dataset.dayIdx !== undefined &&
      cell.dataset.hour !== undefined
    ) {
      const d = parseInt(cell.dataset.dayIdx, 10);
      const h = parseInt(cell.dataset.hour, 10);
      if (!isNaN(d) && !isNaN(h)) {
        if (
          dragStartRef.current &&
          (dragStartRef.current.dayIdx !== d || dragStartRef.current.hour !== h)
        ) {
          hasMovedRef.current = true;
        }
        dragCurrentRef.current = { dayIdx: d, hour: h };
        setDragCurrent({ dayIdx: d, hour: h });
      }
    }
  };

  const handleTouchEnd = () => {
    finishDrag();
  };

  const selectedOverlappingSchedules = selectedRange
    ? schedules.filter((s) => {
        for (let d = selectedRange.minDay; d <= selectedRange.maxDay; d++) {
          const day = DAY_FLAGS[d];
          for (let h = selectedRange.minHour; h <= selectedRange.maxHour; h++) {
            if (isScheduleInSlot(s, h, day.value)) {
              return true;
            }
          }
        }
        return false;
      })
    : [];

  return (
    <div
      className="card"
      style={{
        overflowX: "auto",
        marginBottom: "1.25rem",
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        border: "1px solid var(--border-light)",
        padding: "1.25rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexWrap: "wrap",
          gap: "0.5rem",
        }}
      >
        <h3 style={{ margin: 0, fontSize: "1.05rem" }}>
          {t("speedSchedule.weeklyView")}
        </h3>
        <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
          {t("speedSchedule.gridDragHint")}
        </span>
      </div>

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "55px repeat(7, 1fr)",
          gap: 0,
          minWidth: 640,
          border: "1px solid var(--border-light)",
          borderRadius: "6px",
          overflow: "hidden",
          touchAction: "none",
          userSelect: "none",
        }}
        onTouchMove={handleTouchMove}
        onTouchEnd={handleTouchEnd}
      >
        <div style={{ backgroundColor: "var(--bg-secondary)" }} />
        {DAY_FLAGS.map((d) => (
          <div
            key={d.value}
            style={{
              textAlign: "center",
              fontWeight: 600,
              fontSize: "0.82rem",
              padding: "6px 0",
              backgroundColor: "var(--bg-secondary)",
              borderBottom: "1px solid var(--border-light)",
              borderLeft: "1px solid var(--border-light)",
              color: "var(--accent, #ffd166)",
              userSelect: "none",
            }}
          >
            {d.label}
          </div>
        ))}
        {hours.map((hour) => (
          <React.Fragment key={hour}>
            <div
              style={{
                fontSize: "0.72rem",
                color: "var(--text-muted)",
                textAlign: "right",
                paddingRight: 8,
                paddingTop: 3,
                borderTop: "1px solid var(--border-light)",
                backgroundColor: "var(--bg-secondary)",
                fontFamily: "monospace",
                userSelect: "none",
              }}
            >
              {String(hour).padStart(2, "0")}:00
            </div>
            {DAY_FLAGS.map((day, dayIdx) => {
              const matching = schedules.filter((s) =>
                isScheduleInSlot(s, hour, day.value),
              );
              const active = matching.filter((s) => s.isEnabled);
              const top = [...active].sort(
                (a, b) => (b.priority ?? 0) - (a.priority ?? 0),
              )[0];
              const inactiveTop = !top
                ? [...matching].sort(
                    (a, b) => (b.priority ?? 0) - (a.priority ?? 0),
                  )[0]
                : undefined;

              const isBeingDragged =
                isDragging &&
                dragStart !== null &&
                dragCurrent !== null &&
                dayIdx >= Math.min(dragStart.dayIdx, dragCurrent.dayIdx) &&
                dayIdx <= Math.max(dragStart.dayIdx, dragCurrent.dayIdx) &&
                hour >= Math.min(dragStart.hour, dragCurrent.hour) &&
                hour <= Math.max(dragStart.hour, dragCurrent.hour);

              const isRangeSelected =
                selectedRange !== null &&
                dayIdx >= selectedRange.minDay &&
                dayIdx <= selectedRange.maxDay &&
                hour >= selectedRange.minHour &&
                hour <= selectedRange.maxHour;

              const isHighlighted = isBeingDragged || isRangeSelected;

              return (
                <div
                  key={`${hour}-${day.value}`}
                  data-calendar-cell="true"
                  data-day-idx={dayIdx}
                  data-hour={hour}
                  onMouseDown={(e) => handleMouseDown(e, dayIdx, hour)}
                  onMouseEnter={() => handleMouseEnter(dayIdx, hour)}
                  onTouchStart={(e) => handleTouchStart(e, dayIdx, hour)}
                  onTouchMove={handleTouchMove}
                  onTouchEnd={handleTouchEnd}
                  onDoubleClick={() => {
                    const sTime = `${String(hour).padStart(2, "0")}:00`;
                    const eTime =
                      hour === 23
                        ? "23:59"
                        : `${String(hour + 1).padStart(2, "0")}:00`;
                    onSelectRange?.({
                      days: day.value,
                      startTime: sTime,
                      endTime: eTime,
                    });
                  }}
                  style={{
                    height: 22,
                    borderTop: "1px solid var(--border-light)",
                    borderLeft: "1px solid var(--border-light)",
                    backgroundColor: isHighlighted
                      ? "rgba(255, 209, 102, 0.45)"
                      : top
                        ? BLOCK_COLORS[
                            schedules.indexOf(top) % BLOCK_COLORS.length
                          ]
                        : inactiveTop
                          ? "rgba(255, 255, 255, 0.05)"
                          : "transparent",
                    opacity: isHighlighted
                      ? 1
                      : top
                        ? 0.85
                        : inactiveTop
                          ? 0.35
                          : 1,
                    outline: isHighlighted
                      ? "2px solid var(--accent, #ffd166)"
                      : inactiveTop
                        ? "1px dashed rgba(255, 255, 255, 0.2)"
                        : "none",
                    outlineOffset: isHighlighted ? "-2px" : "-1px",
                    cursor: "pointer",
                    touchAction: "none",
                    userSelect: "none",
                    WebkitUserSelect: "none",
                    transition: isDragging ? "none" : "all 0.15s ease",
                    position: "relative",
                    zIndex: isHighlighted ? 2 : 1,
                  }}
                  title={
                    isHighlighted
                      ? `Selected: ${day.label} ${String(hour).padStart(2, "0")}:00`
                      : top
                        ? `${top.name} (Active - click to toggle): ${top.maxUploadSpeed > 0 ? formatSpeed(top.maxUploadSpeed * 1024) : top.maxUploadSpeed < 0 ? "Paused" : "Unlimited"} up / ${top.maxDownloadSpeed > 0 ? formatSpeed(top.maxDownloadSpeed * 1024) : top.maxDownloadSpeed < 0 ? "Paused" : "Unlimited"} down`
                        : inactiveTop
                          ? `${inactiveTop.name} (Disabled - click to enable)`
                          : `Unthrottled (${day.label} ${String(hour).padStart(2, "0")}:00 - click to toggle, drag to paint)`
                  }
                />
              );
            })}
          </React.Fragment>
        ))}
      </div>

      {selectedRange && (
        <div
          style={{
            marginTop: "1rem",
            padding: "0.75rem 1rem",
            backgroundColor: "var(--bg-secondary)",
            borderRadius: "6px",
            border: "1px solid var(--accent, #ffd166)",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span style={{ fontSize: "1.1rem" }}>✨</span>
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.88rem" }}>
                {t("speedSchedule.selectedRange")}:{" "}
                {daysToLabels(selectedRange.days)} ({selectedRange.startTime} -{" "}
                {selectedRange.endTime})
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                {t("speedSchedule.hrsPerDay", {
                  hours: selectedRange.maxHour - selectedRange.minHour + 1,
                  plural:
                    selectedRange.maxHour - selectedRange.minHour + 1 > 1
                      ? "s"
                      : "",
                  days: selectedRange.maxDay - selectedRange.minDay + 1,
                  dayPlural:
                    selectedRange.maxDay - selectedRange.minDay + 1 > 1
                      ? "s"
                      : "",
                })}
              </div>
            </div>
          </div>

          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <button
              type="button"
              className="btn btn-primary btn-small"
              onClick={() => {
                onSelectRange?.({
                  days: selectedRange.days,
                  startTime: selectedRange.startTime,
                  endTime: selectedRange.endTime,
                });
                setSelectedRange(null);
              }}
            >
              {t("speedSchedule.addScheduleForRange")}
            </button>
            {selectedOverlappingSchedules.length > 0 && (
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => {
                  selectedOverlappingSchedules.forEach((s) =>
                    onToggleSchedule?.(s),
                  );
                  setSelectedRange(null);
                }}
              >
                {t("speedSchedule.toggleSchedules", {
                  count: selectedOverlappingSchedules.length,
                })}
              </button>
            )}
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={() => setSelectedRange(null)}
            >
              {t("speedSchedule.clearSelection")}
            </button>
          </div>
        </div>
      )}

      {schedules.length > 0 && (
        <div
          style={{ display: "flex", gap: 16, marginTop: 14, flexWrap: "wrap" }}
        >
          {schedules.map((s, i) => (
            <div
              key={s.id}
              onClick={() => onToggleSchedule?.(s)}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 6,
                cursor: onToggleSchedule ? "pointer" : "default",
                userSelect: "none",
                padding: "2px 6px",
                borderRadius: "4px",
                backgroundColor: "rgba(255, 255, 255, 0.03)",
              }}
              title="Click to toggle schedule enabled/disabled"
            >
              <div
                style={{
                  width: 12,
                  height: 12,
                  borderRadius: 3,
                  backgroundColor: BLOCK_COLORS[i % BLOCK_COLORS.length],
                  opacity: s.isEnabled ? 0.9 : 0.3,
                }}
              />
              <span
                style={{
                  fontSize: "0.82rem",
                  opacity: s.isEnabled ? 1 : 0.5,
                  textDecoration: s.isEnabled ? "none" : "line-through",
                }}
              >
                {s.name} ({s.startTime} - {s.endTime})
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function SpeedSchedule() {
  const { t } = useTranslation();
  const { showToast } = useToast();
  const confirm = useConfirm();
  const { data: schedules, isLoading, isError } = useSpeedSchedules();
  const { data: activeLimits } = useActiveSpeedLimits();
  const createSchedule = useCreateSpeedSchedule();
  const updateSchedule = useUpdateSpeedSchedule();
  const deleteSchedule = useDeleteSpeedSchedule();

  const [modal, setModal] = useState<Partial<SpeedScheduleEntry> | null>(null);

  function handleSave(form: Partial<SpeedScheduleEntry>) {
    if (form.id) {
      updateSchedule.mutate(form as SpeedScheduleEntry, {
        onSuccess: () => {
          showToast(
            t("speedSchedule.updatedToast", "Speed schedule updated"),
            "info",
          );
          setModal(null);
        },
        onError: (err: any) => {
          showToast(
            err?.message ||
              t("speedSchedule.updateError", "Failed to update speed schedule"),
            "error",
          );
        },
      });
    } else {
      createSchedule.mutate(form, {
        onSuccess: () => {
          showToast(
            t("speedSchedule.createdToast", "Speed schedule created"),
            "info",
          );
          setModal(null);
        },
        onError: (err: any) => {
          showToast(
            err?.message ||
              t("speedSchedule.createError", "Failed to create speed schedule"),
            "error",
          );
        },
      });
    }
  }

  async function handleDelete(id: number, name: string) {
    const ok = await confirm({
      title: t("speedSchedule.deleteTitle", "Delete Speed Schedule"),
      message: t(
        "speedSchedule.deleteConfirm",
        'Are you sure you want to delete the schedule "{name}"?',
        { name },
      ),
      danger: true,
      confirmText: t("common.delete", "Delete"),
    });
    if (!ok) return;

    deleteSchedule.mutate(id, {
      onSuccess: () =>
        showToast(
          t("speedSchedule.deletedToast", "Speed schedule deleted"),
          "info",
        ),
      onError: (err: any) =>
        showToast(
          err?.message ||
            t("speedSchedule.deleteError", "Failed to delete schedule"),
          "error",
        ),
    });
  }

  const isThrottled = Boolean(
    activeLimits?.isThrottled ?? activeLimits?.isScheduleActive,
  );
  const isPaused = Boolean(activeLimits?.isPaused);
  const now = new Date();
  const currentHour = now.getHours() + now.getMinutes() / 60;
  const currentDayFlag = 1 << now.getDay();
  const activeSchedule = schedules
    ?.filter((s) => isHourInSchedule(s, currentHour, currentDayFlag))
    .sort((a, b) => (b.priority ?? 0) - (a.priority ?? 0))[0];
  const activeScheduleName =
    activeLimits?.activeScheduleName ||
    activeSchedule?.name ||
    (isThrottled ? "Scheduled Limit" : "");

  const activeUploadKbps =
    activeLimits?.maxUploadSpeedKbps ??
    (activeLimits?.maxUploadSpeed
      ? Math.round(activeLimits.maxUploadSpeed / 1024)
      : 0);
  const activeDownloadKbps =
    activeLimits?.maxDownloadSpeedKbps ??
    (activeLimits?.maxDownloadSpeed
      ? Math.round(activeLimits.maxDownloadSpeed / 1024)
      : 0);

  const scheduleCount = schedules?.length ?? 0;

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>⏱️</span> {t("speedSchedule.title")} ({scheduleCount})
            <span
              className="badge badge-primary"
              style={{ fontSize: "0.8rem", marginLeft: "0.25rem" }}
            >
              {t("speedSchedule.bandwidthRules")}
            </span>
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {t("speedSchedule.subtitle")}
          </p>
        </div>

        <div>
          <button
            className="btn btn-primary"
            onClick={() => setModal({ ...EMPTY_SCHEDULE })}
          >
            {t("speedSchedule.addSchedule")}
          </button>
        </div>
      </div>

      {/* Active Rate Limits Stat Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
          gap: "1rem",
          marginBottom: "1.25rem",
        }}
      >
        <div
          className="card"
          style={{
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            padding: "1rem 1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            border: "1px solid var(--border-light)",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
            }}
          >
            {t("speedSchedule.activeSchedule")}
          </div>
          <div
            style={{
              fontSize: "1.3rem",
              fontWeight: 700,
              color: "var(--text-primary)",
              margin: "0.35rem 0",
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            {isPaused ? (
              <span
                className="badge badge-danger"
                style={{ fontSize: "0.85rem" }}
              >
                {t("speedSchedule.paused")}
              </span>
            ) : isThrottled ? (
              <span
                className="badge badge-primary"
                style={{ fontSize: "0.85rem" }}
              >
                ⚡ {activeScheduleName}
              </span>
            ) : (
              <span style={{ color: "var(--text-muted)", fontSize: "1.05rem" }}>
                {t("speedSchedule.noneGlobalRate")}
              </span>
            )}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {isPaused
              ? t("speedSchedule.pausedDesc")
              : isThrottled
                ? t("speedSchedule.throttledDesc")
                : t("speedSchedule.standardDesc")}
          </div>
        </div>

        <div
          className="card"
          style={{
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            padding: "1rem 1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            border: "1px solid var(--border-light)",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
            }}
          >
            {t("speedSchedule.activeUploadLimit")}
          </div>
          <div
            style={{
              fontSize: "1.3rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              margin: "0.35rem 0",
            }}
          >
            {isPaused
              ? t("speedSchedule.pausedRate")
              : activeUploadKbps > 0
                ? formatSpeed(activeUploadKbps * 1024)
                : t("speedSchedule.unlimited")}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {isPaused
              ? t("speedSchedule.uploadHalted")
              : activeUploadKbps > 0
                ? t("speedSchedule.uploadThrottled")
                : t("speedSchedule.noUploadRestriction")}
          </div>
        </div>

        <div
          className="card"
          style={{
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            padding: "1rem 1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            border: "1px solid var(--border-light)",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
            }}
          >
            {t("speedSchedule.activeDownloadLimit")}
          </div>
          <div
            style={{
              fontSize: "1.3rem",
              fontWeight: 700,
              color: "var(--accent, #ffd166)",
              margin: "0.35rem 0",
            }}
          >
            {isPaused
              ? t("speedSchedule.pausedRate")
              : activeDownloadKbps > 0
                ? formatSpeed(activeDownloadKbps * 1024)
                : t("speedSchedule.unlimited")}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {isPaused
              ? t("speedSchedule.downloadHalted")
              : activeDownloadKbps > 0
                ? t("speedSchedule.downloadThrottled")
                : t("speedSchedule.noDownloadRestriction")}
          </div>
        </div>
      </div>

      {/* Weekly View */}
      {!isLoading && !isError && (
        <WeeklyCalendar
          schedules={schedules ?? []}
          onSelectRange={(range) => {
            setModal({
              ...EMPTY_SCHEDULE,
              days: range.days,
              startTime: range.startTime,
              endTime: range.endTime,
            });
          }}
          onToggleSchedule={(s) => {
            updateSchedule.mutate({
              ...s,
              isEnabled: !s.isEnabled,
            });
          }}
        />
      )}

      {/* Schedules Table */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: "1px solid var(--border-light)",
          padding: "1.25rem",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
          }}
        >
          <h3 style={{ margin: 0, fontSize: "1.05rem" }}>
            {t("speedSchedule.configuredSchedules")} ({scheduleCount})
          </h3>
        </div>

        {isLoading ? (
          <p className="loading">{t("speedSchedule.loadingSchedules")}</p>
        ) : isError ? (
          <p className="error">{t("speedSchedule.failedToLoad")}</p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">{t("common.status")}</th>
                  <th className="torrent-table-th">{t("common.name")}</th>
                  <th className="torrent-table-th">
                    {t("speedSchedule.daysCol")}
                  </th>
                  <th className="torrent-table-th">
                    {t("speedSchedule.timeWindow")}
                  </th>
                  <th className="torrent-table-th">
                    {t("speedSchedule.uploadLimit")}
                  </th>
                  <th className="torrent-table-th">
                    {t("speedSchedule.downloadLimit")}
                  </th>
                  <th className="torrent-table-th">
                    {t("speedSchedule.priority")}
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    {t("common.actions")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {(schedules ?? []).length === 0 ? (
                  <tr>
                    <td
                      colSpan={8}
                      style={{ textAlign: "center", padding: "2.5rem 1rem" }}
                    >
                      <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>
                        ⏱️
                      </div>
                      <div
                        style={{
                          fontWeight: 600,
                          fontSize: "1rem",
                          color: "var(--text-secondary)",
                          marginBottom: "0.25rem",
                        }}
                      >
                        {t("speedSchedule.noSchedules")}
                      </div>
                      <div
                        style={{
                          fontSize: "0.85rem",
                          color: "var(--text-muted)",
                          maxWidth: "440px",
                          margin: "0 auto 1.25rem",
                        }}
                      >
                        {t("speedSchedule.createHint")}
                      </div>
                      <button
                        className="btn btn-primary btn-small"
                        onClick={() => setModal({ ...EMPTY_SCHEDULE })}
                      >
                        {t("speedSchedule.addFirstSchedule")}
                      </button>
                    </td>
                  </tr>
                ) : (
                  (schedules ?? []).map((s) => (
                    <tr key={s.id} className="torrent-table-row">
                      <td>
                        <span
                          className={`badge ${s.isEnabled ? "badge-primary" : "badge-secondary"}`}
                          style={{ fontSize: "0.75rem" }}
                        >
                          {s.isEnabled
                            ? t("common.enabled")
                            : t("common.disabled")}
                        </span>
                      </td>
                      <td style={{ fontWeight: 600 }}>{s.name}</td>
                      <td>
                        <span
                          className="badge"
                          style={{
                            backgroundColor: "var(--bg-secondary)",
                            border: "1px solid var(--border-light)",
                            fontSize: "0.75rem",
                          }}
                        >
                          {daysToLabels(s.days)}
                        </span>
                      </td>
                      <td
                        style={{ fontFamily: "monospace", fontSize: "0.85rem" }}
                      >
                        {s.startTime} - {s.endTime}
                      </td>
                      <td
                        style={{
                          color: "var(--accent, #ffd166)",
                          fontWeight: 600,
                        }}
                      >
                        {s.maxUploadSpeed > 0
                          ? formatSpeed(s.maxUploadSpeed * 1024)
                          : s.maxUploadSpeed < 0
                            ? t("speedSchedule.paused")
                            : t("common.unlimited")}
                      </td>
                      <td
                        style={{
                          color: "var(--accent, #ffd166)",
                          fontWeight: 600,
                        }}
                      >
                        {s.maxDownloadSpeed > 0
                          ? formatSpeed(s.maxDownloadSpeed * 1024)
                          : s.maxDownloadSpeed < 0
                            ? t("speedSchedule.paused")
                            : t("common.unlimited")}
                      </td>
                      <td>
                        <span
                          className="badge"
                          style={{
                            backgroundColor: "var(--bg-secondary)",
                            fontSize: "0.75rem",
                          }}
                        >
                          P{s.priority}
                        </span>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <div style={{ display: "inline-flex", gap: 6 }}>
                          <button
                            className="btn btn-small btn-outline"
                            onClick={() => setModal({ ...s })}
                          >
                            {t("common.edit")}
                          </button>
                          <button
                            className="btn btn-small btn-danger"
                            onClick={() => handleDelete(s.id, s.name)}
                          >
                            {t("common.delete")}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {modal && (
        <ScheduleModal
          schedule={modal}
          onSave={handleSave}
          onCancel={() => setModal(null)}
          isPending={createSchedule.isPending || updateSchedule.isPending}
        />
      )}
    </div>
  );
}

export default SpeedSchedule;
