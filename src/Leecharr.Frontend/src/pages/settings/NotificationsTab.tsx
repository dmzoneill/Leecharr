import { useState } from "react";
import type { NotificationSettings } from "../../api/types";
import {
  Toggle,
  SelectInput,
  NumberInput,
  SaveBar,
  SectionCard,
} from "./shared";

const NOTIFICATION_SETTINGS_KEY = "seedarr-notification-settings";

const defaultNotificationSettings: NotificationSettings = {
  enabled: true,
  position: "top-right",
  autoDismissSeconds: 5,
  showInfo: true,
  showSuccess: true,
  showWarning: true,
  showError: true,
};

function useNotificationSettings(): [
  NotificationSettings,
  (settings: NotificationSettings) => void,
] {
  const [settings, setSettings] = useState<NotificationSettings>(() => {
    try {
      const stored = localStorage.getItem(NOTIFICATION_SETTINGS_KEY);
      return stored
        ? { ...defaultNotificationSettings, ...JSON.parse(stored) }
        : defaultNotificationSettings;
    } catch {
      return defaultNotificationSettings;
    }
  });

  const saveSettings = (newSettings: NotificationSettings) => {
    setSettings(newSettings);
    localStorage.setItem(
      NOTIFICATION_SETTINGS_KEY,
      JSON.stringify(newSettings),
    );
  };

  return [settings, saveSettings];
}

export function NotificationsTab() {
  const [settings, saveSettings] = useNotificationSettings();
  const [form, setForm] = useState<NotificationSettings>(settings);
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState(false);

  const set = <K extends keyof NotificationSettings>(
    key: K,
    value: NotificationSettings[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
    setSaved(false);
  };

  const handleSave = () => {
    saveSettings(form);
    setDirty(false);
    setSaved(true);
  };

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={false}
        isError={false}
        isSuccess={saved}
        error={null}
        onSave={handleSave}
      />

      <SectionCard
        title="UI Toast Notifications"
        description="Configure in-browser popup toasts and alert positions"
      >
        <Toggle
          label="Enable Notifications"
          checked={form.enabled}
          onChange={(v) => set("enabled", v)}
          hint="Show toast notification popups on application events"
        />
        <SelectInput
          label="Position"
          value={form.position}
          onChange={(v) => set("position", v)}
          options={[
            { value: "top-right", label: "Top Right" },
            { value: "top-left", label: "Top Left" },
            { value: "bottom-right", label: "Bottom Right" },
            { value: "bottom-left", label: "Bottom Left" },
          ]}
          disabled={!form.enabled}
          hint="Screen corner where notification popups will dock"
        />
        <NumberInput
          label="Auto-Dismiss Timeout"
          value={form.autoDismissSeconds}
          onChange={(v) => set("autoDismissSeconds", v)}
          min={1}
          max={60}
          suffix="seconds"
          disabled={!form.enabled}
          hint="Duration before toasts automatically fade out"
        />
      </SectionCard>

      <SectionCard
        title="Notification Event Filters"
        description="Filter specific alert categories"
      >
        <Toggle
          label="Information"
          checked={form.showInfo}
          onChange={(v) => set("showInfo", v)}
          hint="Informational background event notifications"
        />
        <Toggle
          label="Success"
          checked={form.showSuccess}
          onChange={(v) => set("showSuccess", v)}
          hint="Successful operations, imports, and torrent actions"
        />
        <Toggle
          label="Warning"
          checked={form.showWarning}
          onChange={(v) => set("showWarning", v)}
          hint="Tracker timeouts, rate throttling, and disk threshold warnings"
        />
        <Toggle
          label="Error"
          checked={form.showError}
          onChange={(v) => set("showError", v)}
          hint="Critical failures and network disconnects"
        />
      </SectionCard>
    </div>
  );
}
