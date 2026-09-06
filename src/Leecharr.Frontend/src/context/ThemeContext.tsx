import {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
} from "react";
import type { ReactNode } from "react";

export type ThemeStyle =
  "dark" | "indigo" | "oled" | "slate" | "light" | "system";
export type AccentPalette =
  "auto" | "blue" | "emerald" | "purple" | "rose" | "cyan" | "amber";
export type Theme = "dark" | "light" | ThemeStyle;

export interface ThemeContextValue {
  theme: Theme;
  themeStyle: ThemeStyle;
  colorScheme: AccentPalette;
  setThemeStyle: (style: ThemeStyle) => void;
  setColorScheme: (scheme: AccentPalette) => void;
  toggleTheme: () => void;
}

const STORAGE_KEY = "leecharr-theme";
const THEME_STYLE_KEY = "leecharr-theme-style";
const ACCENT_KEY = "leecharr-accent";

const VALID_STYLES: ThemeStyle[] = [
  "dark",
  "indigo",
  "oled",
  "slate",
  "light",
  "system",
];
const VALID_ACCENTS: AccentPalette[] = [
  "auto",
  "blue",
  "emerald",
  "purple",
  "rose",
  "cyan",
  "amber",
];

const ThemeContext = createContext<ThemeContextValue | null>(null);

function getInitialThemeStyle(): ThemeStyle {
  try {
    const stored =
      localStorage.getItem(THEME_STYLE_KEY) ||
      localStorage.getItem(STORAGE_KEY);
    if (stored && VALID_STYLES.includes(stored as ThemeStyle)) {
      return stored as ThemeStyle;
    }
  } catch {
    // localStorage may be unavailable
  }
  return "dark";
}

function getInitialAccent(): AccentPalette {
  try {
    const stored = localStorage.getItem(ACCENT_KEY);
    if (stored && VALID_ACCENTS.includes(stored as AccentPalette)) {
      return stored as AccentPalette;
    }
  } catch {
    // localStorage may be unavailable
  }
  return "auto";
}

function resolveEffectiveTheme(
  style: ThemeStyle,
): "dark" | "light" | "indigo" | "oled" | "slate" {
  if (style === "system") {
    if (typeof window !== "undefined" && window.matchMedia) {
      return window.matchMedia("(prefers-color-scheme: light)").matches
        ? "light"
        : "dark";
    }
    return "dark";
  }
  return style;
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [themeStyle, setThemeStyleState] =
    useState<ThemeStyle>(getInitialThemeStyle);
  const [colorScheme, setColorSchemeState] =
    useState<AccentPalette>(getInitialAccent);

  const applyThemeAndAccent = useCallback(
    (style: ThemeStyle, accent: AccentPalette) => {
      const resolved = resolveEffectiveTheme(style);
      document.documentElement.setAttribute("data-theme", resolved);
      document.documentElement.setAttribute("data-accent", accent);
      try {
        localStorage.setItem(STORAGE_KEY, resolved);
        localStorage.setItem(THEME_STYLE_KEY, style);
        localStorage.setItem(ACCENT_KEY, accent);
      } catch {
        // localStorage may be unavailable
      }
    },
    [],
  );

  useEffect(() => {
    applyThemeAndAccent(themeStyle, colorScheme);

    if (
      themeStyle === "system" &&
      typeof window !== "undefined" &&
      window.matchMedia
    ) {
      const mediaQuery = window.matchMedia("(prefers-color-scheme: light)");
      const handler = () => applyThemeAndAccent(themeStyle, colorScheme);
      mediaQuery.addEventListener("change", handler);
      return () => mediaQuery.removeEventListener("change", handler);
    }
  }, [themeStyle, colorScheme, applyThemeAndAccent]);

  const setThemeStyle = useCallback((style: ThemeStyle) => {
    setThemeStyleState(style);
  }, []);

  const setColorScheme = useCallback((scheme: AccentPalette) => {
    setColorSchemeState(scheme);
  }, []);

  const toggleTheme = useCallback(() => {
    setThemeStyleState((prev) => {
      const resolved = resolveEffectiveTheme(prev);
      return resolved === "light" ? "dark" : "light";
    });
  }, []);

  const effectiveTheme = resolveEffectiveTheme(themeStyle);

  return (
    <ThemeContext.Provider
      value={{
        theme: effectiveTheme,
        themeStyle,
        colorScheme,
        setThemeStyle,
        setColorScheme,
        toggleTheme,
      }}
    >
      {children}
    </ThemeContext.Provider>
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
