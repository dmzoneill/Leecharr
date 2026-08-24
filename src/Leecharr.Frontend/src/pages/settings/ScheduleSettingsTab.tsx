import React, { useState, useEffect } from "react";
import { useSchedulerConfig, useSaveSchedulerConfig } from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  Toggle,
} from "./shared";

export function ScheduleSettingsTab() {
  const { data: config, isLoading } = useSchedulerConfig();
  const saveMutation = useSaveSchedulerConfig();

  const [form, setForm] = useState({
    schedulerEnabled: false,
    schedulerStartHour: 8,
    schedulerStartMinute: 0,
    schedulerEndHour: 23,
    schedulerEndMinute: 0,
    schedulerMonday: true,
    schedulerTuesday: true,
    schedulerWednesday: true,
    schedulerThursday: true,
    schedulerFriday: true,
    schedulerSaturday: true,
    schedulerSunday: true,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        schedulerEnabled: config.schedulerEnabled ?? false,
        schedulerStartHour: config.schedulerStartHour ?? 8,
        schedulerStartMinute: config.schedulerStartMinute ?? 0,
        schedulerEndHour: config.schedulerEndHour ?? 23,
        schedulerEndMinute: config.schedulerEndMinute ?? 0,
        schedulerMonday: config.schedulerMonday ?? true,
        schedulerTuesday: config.schedulerTuesday ?? true,
        schedulerWednesday: config.schedulerWednesday ?? true,
        schedulerThursday: config.schedulerThursday ?? true,
        schedulerFriday: config.schedulerFriday ?? true,
        schedulerSaturday: config.schedulerSaturday ?? true,
        schedulerSunday: config.schedulerSunday ?? true,
      });
      setDirty(false);
    }
  }, [config]);

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        schedulerEnabled: form.schedulerEnabled,
        schedulerStartHour: form.schedulerStartHour,
        schedulerStartMinute: form.schedulerStartMinute,
        schedulerEndHour: form.schedulerEndHour,
        schedulerEndMinute: form.schedulerEndMinute,
        schedulerMonday: form.schedulerMonday,
        schedulerTuesday: form.schedulerTuesday,
        schedulerWednesday: form.schedulerWednesday,
        schedulerThursday: form.schedulerThursday,
        schedulerFriday: form.schedulerFriday,
        schedulerSaturday: form.schedulerSaturday,
        schedulerSunday: form.schedulerSunday,
      },
      {
        onSuccess: () => setDirty(false),
      }
    );
  };

  if (isLoading) {
    return <div className="loading" style={{ padding: "2rem" }}>Loading scheduler settings...</div>;
  }

  const days = [
    { key: "schedulerMonday" as const, label: "Monday" },
    { key: "schedulerTuesday" as const, label: "Tuesday" },
    { key: "schedulerWednesday" as const, label: "Wednesday" },
    { key: "schedulerThursday" as const, label: "Thursday" },
    { key: "schedulerFriday" as const, label: "Friday" },
    { key: "schedulerSaturday" as const, label: "Saturday" },
    { key: "schedulerSunday" as const, label: "Sunday" },
  ];

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={saveMutation.isPending}
        isError={saveMutation.isError}
        isSuccess={saveMutation.isSuccess}
        error={saveMutation.error as Error | null}
        onSave={handleSave}
      />

      <SectionCard
        title="24x7 Hourly Speed Scheduler"
        description="Automatically engage alternative throttled rate profiles during defined daily windows."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Enable Automated Speed Scheduler"
            checked={form.schedulerEnabled}
            onChange={(v) => update("schedulerEnabled", v)}
            hint="Switches between normal speed and throttled alternative speed according to schedule"
          />

          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: "1rem" }}>
            <NumberInput
              label="Schedule Window Start Hour"
              value={form.schedulerStartHour}
              onChange={(v) => update("schedulerStartHour", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={23}
              suffix="hour (0-23)"
            />

            <NumberInput
              label="Start Minute"
              value={form.schedulerStartMinute}
              onChange={(v) => update("schedulerStartMinute", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={59}
              suffix="min"
            />

            <NumberInput
              label="Schedule Window End Hour"
              value={form.schedulerEndHour}
              onChange={(v) => update("schedulerEndHour", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={23}
              suffix="hour (0-23)"
            />

            <NumberInput
              label="End Minute"
              value={form.schedulerEndMinute}
              onChange={(v) => update("schedulerEndMinute", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={59}
              suffix="min"
            />
          </div>

          <div style={{ borderTop: "1px solid var(--border-light)", paddingTop: "1rem" }}>
            <div style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--text-secondary)", marginBottom: "0.75rem" }}>
              Active Days of the Week
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))", gap: "0.75rem" }}>
              {days.map(({ key, label }) => (
                <Toggle
                  key={key}
                  label={label}
                  checked={form[key]}
                  onChange={(v) => update(key, v)}
                  disabled={!form.schedulerEnabled}
                />
              ))}
            </div>
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default ScheduleSettingsTab;
