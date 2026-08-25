import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  SelectInput,
  NumberInput,
  Toggle,
} from "./shared";

interface NotificationSettings {
  enabled: boolean;
  position: string;
  autoDismissSeconds: number;
  showInfo: boolean;
  showSuccess: boolean;
  showWarning: boolean;
  showError: boolean;
}

const NOTIFICATION_SETTINGS_KEY = "leecharr-notification-settings";
const DEFAULT_NOTIFICATION_SETTINGS: NotificationSettings = {
  enabled: true,
  position: "top-right",
  autoDismissSeconds: 5,
  showInfo: true,
  showSuccess: true,
  showWarning: true,
  showError: true,
};

export function WebUiSettingsTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [form, setForm] = useState({
    themeStyle: "dark",
    colorScheme: "auto",
  });

  const [notifForm, setNotifForm] = useState<NotificationSettings>(() => {
    try {
      const stored = localStorage.getItem(NOTIFICATION_SETTINGS_KEY);
      return stored
        ? { ...DEFAULT_NOTIFICATION_SETTINGS, ...JSON.parse(stored) }
        : DEFAULT_NOTIFICATION_SETTINGS;
    } catch {
      return DEFAULT_NOTIFICATION_SETTINGS;
    }
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        themeStyle: config.themeStyle || "dark",
        colorScheme: config.colorScheme || "auto",
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

  const updateNotif = <K extends keyof NotificationSettings>(
    key: K,
    val: NotificationSettings[K],
  ) => {
    setNotifForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    try {
      localStorage.setItem(
        NOTIFICATION_SETTINGS_KEY,
        JSON.stringify(notifForm),
      );
    } catch (e) {
      console.error("Failed to save toast settings", e);
    }

    saveMutation.mutate(
      {
        ...config,
        themeStyle: form.themeStyle,
        colorScheme: form.colorScheme,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading appearance settings...
      </div>
    );
  }

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
        title="Visual Theme & Color Palette"
        description="Customize dark surfaces, contrast, and interactive brand accent colors."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label="Theme Surface Style"
            value={form.themeStyle}
            onChange={(v) => update("themeStyle", v)}
            options={[
              { value: "dark", label: "Midnight Charcoal (#10111A)" },
              { value: "indigo", label: "Deep Indigo Navy (#171B35)" },
              { value: "system", label: "Match Operating System" },
            ]}
            hint="Base background and elevated card surface colors"
          />

          <SelectInput
            label="Brand Accent Palette"
            value={form.colorScheme}
            onChange={(v) => update("colorScheme", v)}
            options={[
              { value: "auto", label: "Warm Amber / Gold (#FFD166)" },
              { value: "blue", label: "Sapphire Blue (#3B82F6)" },
              { value: "emerald", label: "Emerald Green (#10B981)" },
              { value: "purple", label: "Amethyst Purple (#8B5CF6)" },
            ]}
            hint="Interactive action buttons, speed pulse highlights, and progress bars"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="In-Browser Toast Notifications"
        description="Configure client-side notification popup alerts, docking anchor, and severity filters."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Enable In-Browser Popups"
            checked={notifForm.enabled}
            onChange={(v) => updateNotif("enabled", v)}
            hint="Display floating toast notifications for download completions and system events"
          />

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "1rem",
            }}
          >
            <SelectInput
              label="Screen Docking Position"
              value={notifForm.position}
              onChange={(v) => updateNotif("position", v)}
              disabled={!notifForm.enabled}
              options={[
                { value: "top-right", label: "Top Right" },
                { value: "top-left", label: "Top Left" },
                { value: "bottom-right", label: "Bottom Right" },
                { value: "bottom-left", label: "Bottom Left" },
              ]}
              hint="Corner anchor for alert popups"
            />

            <NumberInput
              label="Auto-Dismiss Lifetime"
              value={notifForm.autoDismissSeconds}
              onChange={(v) => updateNotif("autoDismissSeconds", v)}
              disabled={!notifForm.enabled}
              min={1}
              max={60}
              suffix="seconds"
              hint="Seconds before toast dismisses automatically (0 = stay forever)"
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
              Filter Visible Notification Categories
            </div>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
                gap: "0.75rem",
              }}
            >
              <Toggle
                label="Info Alerts"
                checked={notifForm.showInfo}
                onChange={(v) => updateNotif("showInfo", v)}
                disabled={!notifForm.enabled}
              />
              <Toggle
                label="Success Alerts"
                checked={notifForm.showSuccess}
                onChange={(v) => updateNotif("showSuccess", v)}
                disabled={!notifForm.enabled}
              />
              <Toggle
                label="Warning Alerts"
                checked={notifForm.showWarning}
                onChange={(v) => updateNotif("showWarning", v)}
                disabled={!notifForm.enabled}
              />
              <Toggle
                label="Critical Errors"
                checked={notifForm.showError}
                onChange={(v) => updateNotif("showError", v)}
                disabled={!notifForm.enabled}
              />
            </div>
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default WebUiSettingsTab;
