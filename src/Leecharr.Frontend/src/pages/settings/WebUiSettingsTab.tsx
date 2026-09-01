import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { SaveBar, SectionCard, SelectInput } from "./shared";

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
    setForm((prev) => {
      const next = { ...prev, [key]: val };
      // Apply immediate live preview
      let theme = next.themeStyle;
      if (theme === "system") {
        theme = window.matchMedia("(prefers-color-scheme: light)").matches
          ? "light"
          : "dark";
      }
      document.documentElement.setAttribute("data-theme", theme);
      document.documentElement.setAttribute("data-accent", next.colorScheme);
      return next;
    });
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

  const surfaceHexMap: Record<
    string,
    { bg: string; card: string; border: string }
  > = {
    dark: { bg: "#10111A", card: "#171B35", border: "#23284B" },
    indigo: { bg: "#0B0E1E", card: "#131733", border: "#1F2552" },
    oled: { bg: "#000000", card: "#0A0B12", border: "#1A1D2E" },
    slate: { bg: "#0F141C", card: "#181F2C", border: "#242E40" },
    light: { bg: "#F5F0E5", card: "#FDFAF4", border: "#D4CBB8" },
    system: {
      bg: "var(--bg-primary)",
      card: "var(--bg-card)",
      border: "var(--border)",
    },
  };

  const accentHexMap: Record<string, string> = {
    auto: "#FFD166",
    blue: "#3B82F6",
    emerald: "#10B981",
    purple: "#8B5CF6",
    rose: "#F43F5E",
    cyan: "#06B6D4",
    amber: "#F59E0B",
  };

  const currentSurface = surfaceHexMap[form.themeStyle] || surfaceHexMap.dark;
  const currentAccent = accentHexMap[form.colorScheme] || "#FFD166";

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
            gap: "1.25rem",
            marginBottom: "1.5rem",
          }}
        >
          <SelectInput
            label="Theme Surface Style"
            value={form.themeStyle}
            onChange={(v) => update("themeStyle", v)}
            options={[
              { value: "dark", label: "Midnight Charcoal (#10111A) [Default]" },
              { value: "indigo", label: "Deep Indigo Navy (#0B0E1E)" },
              { value: "oled", label: "Pure Pitch Black OLED (#000000)" },
              { value: "slate", label: "Slate Charcoal (#0F141C)" },
              { value: "light", label: "Linen Cream Light (#F5F0E5)" },
              {
                value: "system",
                label: "Match Operating System (prefers-color-scheme)",
              },
            ]}
            hint="Base background, top navigation bar, and elevated card surface tones"
          />

          <SelectInput
            label="Brand Accent Palette"
            value={form.colorScheme}
            onChange={(v) => update("colorScheme", v)}
            options={[
              { value: "auto", label: "Warm Gold / Amber (#FFD166) [Default]" },
              { value: "blue", label: "Sapphire Blue (#3B82F6)" },
              { value: "emerald", label: "Emerald Green (#10B981)" },
              { value: "purple", label: "Amethyst Purple (#8B5CF6)" },
              { value: "rose", label: "Crimson Rose (#F43F5E)" },
              { value: "cyan", label: "Electric Cyan (#06B6D4)" },
              { value: "amber", label: "Deep Amber (#F59E0B)" },
            ]}
            hint="Interactive action buttons, active tab indicators, speed pulse highlights, and progress bars"
          />
        </div>

        {/* Live Interactive Palette Preview Card */}
        <div
          style={{
            padding: "1.25rem",
            background: currentSurface.card,
            borderRadius: "10px",
            border: `1px solid ${currentSurface.border}`,
            boxShadow: "0 4px 14px rgba(0,0,0,0.25)",
            transition: "all 0.25s ease",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              marginBottom: "1rem",
              flexWrap: "wrap",
              gap: "0.5rem",
            }}
          >
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
            >
              <div
                style={{
                  width: "16px",
                  height: "16px",
                  borderRadius: "50%",
                  background: currentAccent,
                  boxShadow: `0 0 10px ${currentAccent}80`,
                }}
              />
              <span
                style={{
                  fontWeight: 600,
                  fontSize: "0.95rem",
                  color: "var(--text-primary)",
                }}
              >
                Live Palette Preview
              </span>
              <span
                style={{
                  fontSize: "0.75rem",
                  padding: "0.2rem 0.5rem",
                  borderRadius: "4px",
                  background: "var(--accent-bg)",
                  color: "var(--accent)",
                  fontWeight: 600,
                }}
              >
                {form.colorScheme.toUpperCase()} ACCENT
              </span>
            </div>
            <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
              Changes take effect immediately in the preview
            </span>
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: "1rem",
              alignItems: "center",
            }}
          >
            <div>
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-secondary)",
                  marginBottom: "0.35rem",
                }}
              >
                Interactive Buttons
              </div>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  style={{
                    background: "var(--accent)",
                    color: "#10111a",
                    fontWeight: 700,
                    padding: "0.45rem 0.9rem",
                    borderRadius: "6px",
                    border: "none",
                    fontSize: "0.82rem",
                    cursor: "pointer",
                  }}
                >
                  Primary Action
                </button>
                <button
                  type="button"
                  style={{
                    background: "var(--accent-bg)",
                    color: "var(--accent)",
                    fontWeight: 600,
                    padding: "0.45rem 0.8rem",
                    borderRadius: "6px",
                    border: `1px solid var(--accent)`,
                    fontSize: "0.82rem",
                    cursor: "pointer",
                  }}
                >
                  Subtle Outline
                </button>
              </div>
            </div>

            <div>
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-secondary)",
                  marginBottom: "0.35rem",
                }}
              >
                Download Progress (72%)
              </div>
              <div
                style={{
                  height: "8px",
                  background: "var(--bg-primary)",
                  borderRadius: "4px",
                  overflow: "hidden",
                }}
              >
                <div
                  style={{
                    width: "72%",
                    height: "100%",
                    background: "var(--accent)",
                    borderRadius: "4px",
                    transition: "background 0.3s ease",
                  }}
                />
              </div>
            </div>

            <div>
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-secondary)",
                  marginBottom: "0.35rem",
                }}
              >
                Speed Metric Badge
              </div>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <span
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.35rem",
                    padding: "0.3rem 0.65rem",
                    borderRadius: "6px",
                    background: "var(--accent-bg)",
                    border: "1px solid var(--accent)",
                    color: "var(--accent)",
                    fontSize: "0.8rem",
                    fontWeight: 700,
                  }}
                >
                  ↓ 42.8 MB/s
                </span>
                <span
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.35rem",
                    padding: "0.3rem 0.65rem",
                    borderRadius: "6px",
                    background: "var(--bg-primary)",
                    border: "1px solid var(--border)",
                    color: "var(--text-secondary)",
                    fontSize: "0.8rem",
                  }}
                >
                  ↑ 5.2 MB/s
                </span>
              </div>
            </div>
          </div>
        </div>
      </SectionCard>
    </div>
  );
}

export default WebUiSettingsTab;
