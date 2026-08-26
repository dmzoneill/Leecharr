import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  SelectInput,
} from "./shared";

export function WebUiSettingsTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [form, setForm] = useState({
    themeStyle: "dark",
    colorScheme: "auto",
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

  const handleSave = () => {
    if (!config) return;
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
    </div>
  );
}

export default WebUiSettingsTab;
