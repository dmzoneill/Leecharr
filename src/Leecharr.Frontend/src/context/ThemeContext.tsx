import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
  useMemo,
} from "react";
import type { ReactNode } from "react";
import { useGeneralConfig } from "../api/hooks";

export type ThemeStyle =
  "dark" | "light" | "indigo" | "oled" | "slate" | "system";

export type ColorScheme =
  "auto" | "blue" | "emerald" | "purple" | "rose" | "cyan" | "amber";

export interface ThemeContextValue {
  theme: string;
  themeStyle: ThemeStyle;
  colorScheme: ColorScheme;
  isDark: boolean;
  toggleTheme: () => void;
  setThemeStyle: (style: ThemeStyle) => void;
  setColorScheme: (scheme: ColorScheme) => void;
}

const STORAGE_KEY_THEME = "leecharr-theme-style";
const STORAGE_KEY_ACCENT = "leecharr-color-scheme";

const ThemeContext = createContext<ThemeContextValue | null>(null);

function resolveSystemTheme(): "light" | "dark" {
  if (
    typeof window !== "undefined" &&
    window.matchMedia &&
    window.matchMedia("(prefers-color-scheme: light)").matches
  ) {
    return "light";
  }
  return "dark";
}

function getInitialThemeStyle(): ThemeStyle {
  try {
    const stored = localStorage.getItem(STORAGE_KEY_THEME);
    if (
      stored === "dark" ||
      stored === "light" ||
      stored === "indigo" ||
      stored === "oled" ||
      stored === "slate" ||
      stored === "system"
    ) {
      return stored as ThemeStyle;
    }
    const legacy = localStorage.getItem("leecharr-theme");
    if (legacy === "light" || legacy === "dark") {
      return legacy;
    }
  } catch {
    // localStorage unavailable
  }
  return "dark";
}

function getInitialColorScheme(): ColorScheme {
  try {
    const stored = localStorage.getItem(STORAGE_KEY_ACCENT);
    if (
      stored === "auto" ||
      stored === "blue" ||
      stored === "emerald" ||
      stored === "purple" ||
      stored === "rose" ||
      stored === "cyan" ||
      stored === "amber"
    ) {
      return stored as ColorScheme;
    }
  } catch {
    // localStorage unavailable
  }
  return "auto";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const { data: generalConfig } = useGeneralConfig();

  const [themeStyle, setThemeStyleState] =
    useState<ThemeStyle>(getInitialThemeStyle);
  const [colorScheme, setColorSchemeState] = useState<ColorScheme>(
    getInitialColorScheme,
  );
  const [systemIsLight, setSystemIsLight] = useState<boolean>(
    () => resolveSystemTheme() === "light",
  );

  // Sync with server generalConfig when it loads
  useEffect(() => {
    if (generalConfig) {
      if (generalConfig.themeStyle) {
        setThemeStyleState(generalConfig.themeStyle as ThemeStyle);
      }
      if (generalConfig.colorScheme) {
        setColorSchemeState(generalConfig.colorScheme as ColorScheme);
      }
    }
  }, [generalConfig?.themeStyle, generalConfig?.colorScheme]);

  // Listen to system prefers-color-scheme changes
  useEffect(() => {
    if (typeof window === "undefined" || !window.matchMedia) return;
    const mediaQuery = window.matchMedia("(prefers-color-scheme: light)");
    const handler = (e: MediaQueryListEvent) => {
      setSystemIsLight(e.matches);
    };
    mediaQuery.addEventListener("change", handler);
    return () => mediaQuery.removeEventListener("change", handler);
  }, []);

  const effectiveTheme = useMemo(() => {
    if (themeStyle === "system") {
      return systemIsLight ? "light" : "dark";
    }
    return themeStyle;
  }, [themeStyle, systemIsLight]);

  const isDark = effectiveTheme !== "light";

  // Apply data-theme and data-accent attributes to document root
  useEffect(() => {
    if (typeof document !== "undefined") {
      document.documentElement.setAttribute("data-theme", effectiveTheme);
      document.documentElement.setAttribute("data-accent", colorScheme);
    }
  }, [effectiveTheme, colorScheme]);

  const setThemeStyle = useCallback((style: ThemeStyle) => {
    setThemeStyleState(style);
    try {
      localStorage.setItem(STORAGE_KEY_THEME, style);
      localStorage.setItem(
        "leecharr-theme",
        style === "light" ? "light" : "dark",
      );
    } catch {
      // ignore
    }
  }, []);

  const setColorScheme = useCallback((scheme: ColorScheme) => {
    setColorSchemeState(scheme);
    try {
      localStorage.setItem(STORAGE_KEY_ACCENT, scheme);
    } catch {
      // ignore
    }
  }, []);

  const toggleTheme = useCallback(() => {
    setThemeStyleState((prev) => {
      let next: ThemeStyle;
      if (prev === "light") {
        next = "dark";
      } else if (prev === "system") {
        next = systemIsLight ? "dark" : "light";
      } else {
        next = "light";
      }
      try {
        localStorage.setItem(STORAGE_KEY_THEME, next);
        localStorage.setItem("leecharr-theme", next);
      } catch {
        // ignore
      }
      return next;
    });
  }, [systemIsLight]);

  const value = useMemo(
    () => ({
      theme: effectiveTheme,
      themeStyle,
      colorScheme,
      isDark,
      toggleTheme,
      setThemeStyle,
      setColorScheme,
    }),
    [
      effectiveTheme,
      themeStyle,
      colorScheme,
      isDark,
      toggleTheme,
      setThemeStyle,
      setColorScheme,
    ],
  );

  return (
    <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error("useTheme must be used within a ThemeProvider");
  }
  return ctx;
}

export default ThemeContext;
