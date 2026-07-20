import { useState, useEffect } from "react";
import { useSchedulerConfig, useSaveSchedulerConfig } from "../../api/hooks";
import type { SchedulerConfig } from "../../api/types";
import { SaveBar, Toggle, NumberInput, SectionCard } from "./shared";

export function SchedulerTab() {
  const { data: config, isLoading } = useSchedulerConfig();
  const save = useSaveSchedulerConfig();
  const [form, setForm] = useState<SchedulerConfig>({
    id: 1,
    schedulerEnabled: false,
    schedulerStartHour: 22,
    schedulerStartMinute: 0,
    schedulerEndHour: 6,
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
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof SchedulerConfig>(
    key: K,
    value: SchedulerConfig[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading configuration...</div>;

  const days: { key: keyof SchedulerConfig; label: string }[] = [
    { key: "schedulerMonday", label: "Monday" },
    { key: "schedulerTuesday", label: "Tuesday" },
    { key: "schedulerWednesday", label: "Wednesday" },
    { key: "schedulerThursday", label: "Thursday" },
    { key: "schedulerFriday", label: "Friday" },
    { key: "schedulerSaturday", label: "Saturday" },
    { key: "schedulerSunday", label: "Sunday" },
  ];

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={save.isPending}
        isError={save.isError}
        isSuccess={save.isSuccess}
        error={save.error}
        onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })}
      />

      <SectionCard
        title="Speed Limit Scheduler"
        description="Automatically engage alternative rate limits during scheduled time windows"
      >
        <Toggle
          label="Scheduler Enabled"
          checked={form.schedulerEnabled}
          onChange={(v) => set("schedulerEnabled", v)}
          hint="Automatically apply alternative speed profile on the schedule below"
        />
      </SectionCard>

      <SectionCard
        title="Time Window"
        description="Daily start and end times for the throttled speed window (24-hour clock)"
      >
        <NumberInput
          label="Start Hour"
          value={form.schedulerStartHour}
          onChange={(v) => set("schedulerStartHour", v)}
          min={0}
          max={23}
          disabled={!form.schedulerEnabled}
          hint="Window start hour (0 - 23)"
        />
        <NumberInput
          label="Start Minute"
          value={form.schedulerStartMinute}
          onChange={(v) => set("schedulerStartMinute", v)}
          min={0}
          max={59}
          disabled={!form.schedulerEnabled}
        />
        <NumberInput
          label="End Hour"
          value={form.schedulerEndHour}
          onChange={(v) => set("schedulerEndHour", v)}
          min={0}
          max={23}
          disabled={!form.schedulerEnabled}
          hint="Window end hour (0 - 23)"
        />
        <NumberInput
          label="End Minute"
          value={form.schedulerEndMinute}
          onChange={(v) => set("schedulerEndMinute", v)}
          min={0}
          max={59}
          disabled={!form.schedulerEnabled}
        />
      </SectionCard>

      <SectionCard
        title="Active Schedule Days"
        description="Select the days of the week when the scheduled speed profile is enforced"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
            gap: "0.5rem",
          }}
        >
          {days.map((day) => (
            <Toggle
              key={day.key}
              label={day.label}
              checked={form[day.key] as boolean}
              onChange={(v) => set(day.key, v as never)}
            />
          ))}
        </div>
      </SectionCard>
    </div>
  );
}
