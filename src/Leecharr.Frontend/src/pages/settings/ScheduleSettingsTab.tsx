import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import { useSchedulerConfig, useSaveSchedulerConfig } from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, Toggle } from "./shared";

export function ScheduleSettingsTab() {
  const { t } = useTranslation();

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

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
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
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.schedule.loading")}
      </div>
    );
  }

  const days = [
    {
      key: "schedulerMonday" as const,
      label: t("settingsTabs.schedule.days.monday"),
    },
    {
      key: "schedulerTuesday" as const,
      label: t("settingsTabs.schedule.days.tuesday"),
    },
    {
      key: "schedulerWednesday" as const,
      label: t("settingsTabs.schedule.days.wednesday"),
    },
    {
      key: "schedulerThursday" as const,
      label: t("settingsTabs.schedule.days.thursday"),
    },
    {
      key: "schedulerFriday" as const,
      label: t("settingsTabs.schedule.days.friday"),
    },
    {
      key: "schedulerSaturday" as const,
      label: t("settingsTabs.schedule.days.saturday"),
    },
    {
      key: "schedulerSunday" as const,
      label: t("settingsTabs.schedule.days.sunday"),
    },
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
        title={t("settingsTabs.schedule.title")}
        description={t("settingsTabs.schedule.description")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settingsTabs.schedule.enable")}
            checked={form.schedulerEnabled}
            onChange={(v) => update("schedulerEnabled", v)}
            hint={t("settingsTabs.schedule.enableHint")}
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
              gap: "1rem",
            }}
          >
            <NumberInput
              label={t("settingsTabs.schedule.startHour")}
              value={form.schedulerStartHour}
              onChange={(v) => update("schedulerStartHour", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={23}
              suffix={t("settingsTabs.schedule.hourSuffix")}
            />

            <NumberInput
              label={t("settingsTabs.schedule.startMinute")}
              value={form.schedulerStartMinute}
              onChange={(v) => update("schedulerStartMinute", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={59}
              suffix={t("settingsTabs.schedule.minuteSuffix")}
            />

            <NumberInput
              label={t("settingsTabs.schedule.endHour")}
              value={form.schedulerEndHour}
              onChange={(v) => update("schedulerEndHour", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={23}
              suffix={t("settingsTabs.schedule.hourSuffix")}
            />

            <NumberInput
              label={t("settingsTabs.schedule.endMinute")}
              value={form.schedulerEndMinute}
              onChange={(v) => update("schedulerEndMinute", v)}
              disabled={!form.schedulerEnabled}
              min={0}
              max={59}
              suffix={t("settingsTabs.schedule.minuteSuffix")}
            />
          </div>

          <div
            style={{
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <div
              style={{
                fontSize: "0.85rem",
                fontWeight: 600,
                color: "var(--text-secondary)",
                marginBottom: "0.75rem",
              }}
            >
              {t("settingsTabs.schedule.activeDays")}
            </div>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
                gap: "0.75rem",
              }}
            >
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
