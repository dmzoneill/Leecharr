import { useTranslation } from "../i18n";
import React, { useState } from "react";
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

export function isHourInSchedule(
  s: SpeedScheduleEntry,
  hour: number,
  dayValue: number,
): boolean {
  if (!s.isEnabled) return false;
  const startH = timeToHour(s.startTime);
  const endH = timeToHour(s.endTime);

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
          border: "1px solid rgba(255, 255, 255, 0.12)",
        }}
      >
        <h2 style={{ margin: "0 0 1.25rem", fontSize: "1.25rem" }}>
          {schedule.id ? "Edit Speed Schedule" : "Add Speed Schedule"}
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
              {t("autogen.t_schedule_name")}
            </span>
            <input
              className="form-input"
              type="text"
              placeholder={t("autogen.t_e_g_night_seeding_boost")}
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
                {t("autogen.t_active_days")}
              </span>
              <div style={{ display: "flex", gap: 4 }}>
                <button
                  type="button"
                  className={`btn btn-small ${form.days === PRESETS.everyday ? "btn-primary" : "btn-outline"}`}
                  onClick={() => setForm({ ...form, days: PRESETS.everyday })}
                  style={{ fontSize: "0.72rem", padding: "2px 6px" }}
                >
                  {t("autogen.t_all_days")}
                </button>
                <button
                  type="button"
                  className={`btn btn-small ${form.days === PRESETS.weekdays ? "btn-primary" : "btn-outline"}`}
                  onClick={() => setForm({ ...form, days: PRESETS.weekdays })}
                  style={{ fontSize: "0.72rem", padding: "2px 6px" }}
                >
                  {t("autogen.t_weekdays")}
                </button>
                <button
                  type="button"
                  className={`btn btn-small ${form.days === PRESETS.weekends ? "btn-primary" : "btn-outline"}`}
                  onClick={() => setForm({ ...form, days: PRESETS.weekends })}
                  style={{ fontSize: "0.72rem", padding: "2px 6px" }}
                >
                  {t("autogen.t_weekends")}
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
                {t("autogen.t_start_time")}
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
                {t("autogen.t_end_time")}
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
                {t("autogen.t_max_upload_kb_s_0_unlimited")}
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
                {t("autogen.t_max_download_kb_s_0_unlimited")}
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
                {t("autogen.t_priority")}
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
                {t("autogen.t_schedule_enabled")}
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
              {t("autogen.t_cancel")}
            </button>
            <button
              className="btn btn-primary btn-small"
              onClick={() => onSave(form)}
              disabled={isPending || !form.name.trim()}
              type="button"
            >
              {isPending ? "Saving..." : "Save Schedule"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function WeeklyCalendar({ schedules }: { schedules: SpeedScheduleEntry[] }) {
  const { t } = useTranslation();

  const hours = Array.from({ length: 24 }, (_, i) => i);

  return (
    <div
      className="card"
      style={{
        overflowX: "auto",
        marginBottom: "1.25rem",
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        border: "1px solid rgba(255, 255, 255, 0.08)",
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
          {t("autogen.t_weekly_schedule_view")}
        </h3>
        <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
          {t("autogen.t_24_hour_time_matrix")}
        </span>
      </div>

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "55px repeat(7, 1fr)",
          gap: 0,
          minWidth: 640,
          border: "1px solid rgba(255, 255, 255, 0.08)",
          borderRadius: "6px",
          overflow: "hidden",
        }}
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
              borderBottom: "1px solid rgba(255, 255, 255, 0.08)",
              borderLeft: "1px solid rgba(255, 255, 255, 0.08)",
              color: "var(--accent, #ffd166)",
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
                borderTop: "1px solid rgba(255, 255, 255, 0.04)",
                backgroundColor: "var(--bg-secondary)",
                fontFamily: "monospace",
              }}
            >
              {String(hour).padStart(2, "0")}:00
            </div>
            {DAY_FLAGS.map((day) => {
              const active = schedules
                .filter((s) => isHourInSchedule(s, hour, day.value))
                .sort((a, b) => (b.priority ?? 0) - (a.priority ?? 0));
              const top = active[0];
              return (
                <div
                  key={`${hour}-${day.value}`}
                  style={{
                    height: 22,
                    borderTop: "1px solid rgba(255, 255, 255, 0.04)",
                    borderLeft: "1px solid rgba(255, 255, 255, 0.04)",
                    backgroundColor: top
                      ? BLOCK_COLORS[
                          schedules.indexOf(top) % BLOCK_COLORS.length
                        ]
                      : "transparent",
                    opacity: top ? 0.85 : 1,
                    transition: "all 0.15s ease",
                  }}
                  title={
                    top
                      ? `${top.name}: ${top.maxUploadSpeed > 0 ? formatSpeed(top.maxUploadSpeed * 1024) : top.maxUploadSpeed < 0 ? "Paused" : "Unlimited"} up / ${top.maxDownloadSpeed > 0 ? formatSpeed(top.maxDownloadSpeed * 1024) : top.maxDownloadSpeed < 0 ? "Paused" : "Unlimited"} down`
                      : "Unthrottled"
                  }
                />
              );
            })}
          </React.Fragment>
        ))}
      </div>

      {schedules.length > 0 && (
        <div
          style={{ display: "flex", gap: 16, marginTop: 14, flexWrap: "wrap" }}
        >
          {schedules.map((s, i) => (
            <div
              key={s.id}
              style={{ display: "flex", alignItems: "center", gap: 6 }}
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
                style={{ fontSize: "0.82rem", opacity: s.isEnabled ? 1 : 0.5 }}
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
          showToast("Speed schedule updated", "info");
          setModal(null);
        },
        onError: (err: any) => {
          showToast(err?.message || "Failed to update speed schedule", "error");
        },
      });
    } else {
      createSchedule.mutate(form, {
        onSuccess: () => {
          showToast("Speed schedule created", "info");
          setModal(null);
        },
        onError: (err: any) => {
          showToast(err?.message || "Failed to create speed schedule", "error");
        },
      });
    }
  }

  async function handleDelete(id: number, name: string) {
    const ok = await confirm({
      title: "Delete Speed Schedule",
      message: `Are you sure you want to delete the schedule "${name}"?`,
      danger: true,
      confirmText: "Delete",
    });
    if (!ok) return;

    deleteSchedule.mutate(id, {
      onSuccess: () => showToast("Speed schedule deleted", "info"),
      onError: (err: any) =>
        showToast(err?.message || "Failed to delete schedule", "error"),
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
    <div className="content-area">
      {/* Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.25rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {t("autogen.t_speed_schedule")}
              {scheduleCount})
            </h1>
            <span className="badge badge-primary">
              {t("autogen.t_bandwidth_rules")}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("autogen.t_manage_time_based_upload_and_download_sp")}
          </div>
        </div>

        <div className="page-header-actions">
          <button
            className="btn btn-primary"
            onClick={() => setModal({ ...EMPTY_SCHEDULE })}
          >
            {t("autogen.t_add_schedule")}
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
            border: "1px solid rgba(255, 255, 255, 0.08)",
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
            {t("autogen.t_active_schedule")}
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
                {t("autogen.t_paused")}
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
                {t("autogen.t_none_global_rate")}
              </span>
            )}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {isPaused
              ? "All BitTorrent transfers paused by schedule"
              : isThrottled
                ? "Time-based scheduled rate is enforced"
                : "Standard global rate configuration"}
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
            border: "1px solid rgba(255, 255, 255, 0.08)",
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
            {t("autogen.t_active_upload_limit")}
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
              ? "Paused (0 KB/s)"
              : activeUploadKbps > 0
                ? formatSpeed(activeUploadKbps * 1024)
                : "Unlimited"}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {isPaused
              ? "Upload traffic halted"
              : activeUploadKbps > 0
                ? "Enforced rate throttle across active torrents"
                : "No upload bandwidth restriction"}
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
            border: "1px solid rgba(255, 255, 255, 0.08)",
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
            {t("autogen.t_active_download_limit")}
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
              ? "Paused (0 KB/s)"
              : activeDownloadKbps > 0
                ? formatSpeed(activeDownloadKbps * 1024)
                : "Unlimited"}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {isPaused
              ? "Download traffic halted"
              : activeDownloadKbps > 0
                ? "Enforced rate throttle across downloads"
                : "No download bandwidth restriction"}
          </div>
        </div>
      </div>

      {/* Weekly View */}
      {!isLoading && !isError && <WeeklyCalendar schedules={schedules ?? []} />}

      {/* Schedules Table */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: "1px solid rgba(255, 255, 255, 0.08)",
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
            {t("autogen.t_configured_schedules")}
            {scheduleCount})
          </h3>
        </div>

        {isLoading ? (
          <p className="loading">{t("autogen.t_loading_schedules")}</p>
        ) : isError ? (
          <p className="error">{t("autogen.t_failed_to_load_schedule_data")}</p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">{t("autogen.t_status")}</th>
                  <th className="torrent-table-th">{t("autogen.t_name")}</th>
                  <th className="torrent-table-th">{t("autogen.t_days")}</th>
                  <th className="torrent-table-th">
                    {t("autogen.t_time_window")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_upload_limit")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_download_limit")}
                  </th>
                  <th className="torrent-table-th">
                    {t("autogen.t_priority")}
                  </th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    {t("autogen.t_actions")}
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
                        {t("autogen.t_no_speed_schedules_configured")}
                      </div>
                      <div
                        style={{
                          fontSize: "0.85rem",
                          color: "var(--text-muted)",
                          maxWidth: "440px",
                          margin: "0 auto 1.25rem",
                        }}
                      >
                        {t(
                          "autogen.t_create_scheduled_speed_rules_to_throttle",
                        )}
                      </div>
                      <button
                        className="btn btn-primary btn-small"
                        onClick={() => setModal({ ...EMPTY_SCHEDULE })}
                      >
                        {t("autogen.t_add_first_schedule")}
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
                          {s.isEnabled ? "Enabled" : "Disabled"}
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
                            ? "Paused"
                            : "Unlimited"}
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
                            ? "Paused"
                            : "Unlimited"}
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
                            {t("autogen.t_edit")}
                          </button>
                          <button
                            className="btn btn-small btn-danger"
                            onClick={() => handleDelete(s.id, s.name)}
                          >
                            {t("autogen.t_delete")}
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
