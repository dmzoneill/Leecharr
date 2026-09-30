import {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
  useMemo,
  ReactNode,
} from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../api/hooks";
import { trackThemeChange } from "../utils/analytics";

export type ThemeStyle =
  "dark" | "indigo" | "oled" | "slate" | "light" | "system";

export type ColorScheme =
  "auto" | "blue" | "emerald" | "purple" | "rose" | "cyan" | "amber";

export type AccentPalette = ColorScheme;

export interface ThemeContextValue {
  theme: string;
  themeStyle: ThemeStyle;
  colorScheme: ColorScheme;
  accent: ColorScheme;
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
    const stored =
      localStorage.getItem(STORAGE_KEY_THEME) ||
      localStorage.getItem("seedarr-theme-style");
    if (
      stored === "dark" ||
      stored === "indigo" ||
      stored === "oled" ||
      stored === "slate" ||
      stored === "light" ||
      stored === "system"
    ) {
      return stored as ThemeStyle;
    }
    const legacy =
      localStorage.getItem("leecharr-theme") ||
      localStorage.getItem("seedarr-theme");
    if (legacy === "light" || legacy === "dark") {
      return legacy;
    }
  } catch {
    /* localStorage unavailable */
  }
  return "dark";
}

function getInitialColorScheme(): ColorScheme {
  try {
    const stored =
      localStorage.getItem(STORAGE_KEY_ACCENT) ||
      localStorage.getItem("leecharr-accent") ||
      localStorage.getItem("seedarr-color-scheme") ||
      localStorage.getItem("seedarr-accent");
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
    /* localStorage unavailable */
  }
  return "auto";
}

function hasStoredThemeStyle(): boolean {
  try {
    return Boolean(
      localStorage.getItem(STORAGE_KEY_THEME) ||
      localStorage.getItem("seedarr-theme-style") ||
      localStorage.getItem("leecharr-theme") ||
      localStorage.getItem("seedarr-theme"),
    );
  } catch {
    return false;
  }
}

function hasStoredColorScheme(): boolean {
  try {
    return Boolean(
      localStorage.getItem(STORAGE_KEY_ACCENT) ||
      localStorage.getItem("leecharr-accent") ||
      localStorage.getItem("seedarr-color-scheme") ||
      localStorage.getItem("seedarr-accent"),
    );
  } catch {
    return false;
  }
}

function applyThemeToDocument(theme: string, accent: string) {
  if (typeof document !== "undefined") {
    document.documentElement.setAttribute("data-theme", theme);
    document.documentElement.setAttribute("data-accent", accent);
  }
}

try {
  const initTheme = getInitialThemeStyle();
  const initAccent = getInitialColorScheme();
  const resolved = initTheme === "system" ? resolveSystemTheme() : initTheme;
  applyThemeToDocument(resolved, initAccent);
} catch {
  /* SSR or test environment */
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const { data: generalConfig } = useGeneralConfig();
  const saveGeneralConfig = useSaveGeneralConfig();

  const [themeStyle, setThemeStyleState] =
    useState<ThemeStyle>(getInitialThemeStyle);
  const [colorScheme, setColorSchemeState] = useState<ColorScheme>(
    getInitialColorScheme,
  );
  const [systemIsLight, setSystemIsLight] = useState<boolean>(
    () => resolveSystemTheme() === "light",
  );

  useEffect(() => {
    if (!hasStoredThemeStyle() && generalConfig?.themeStyle) {
      setThemeStyleState(generalConfig.themeStyle as ThemeStyle);
    }
    if (!hasStoredColorScheme() && generalConfig?.colorScheme) {
      setColorSchemeState(generalConfig.colorScheme as ColorScheme);
    }
  }, [generalConfig?.themeStyle, generalConfig?.colorScheme]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const handleStorage = (e: StorageEvent) => {
      if (
        (e.key === STORAGE_KEY_THEME || e.key === "seedarr-theme-style") &&
        e.newValue
      ) {
        setThemeStyleState(e.newValue as ThemeStyle);
      } else if (
        (e.key === STORAGE_KEY_ACCENT ||
          e.key === "leecharr-accent" ||
          e.key === "seedarr-color-scheme" ||
          e.key === "seedarr-accent") &&
        e.newValue
      ) {
        setColorSchemeState(e.newValue as ColorScheme);
      }
    };
    window.addEventListener("storage", handleStorage);
    return () => window.removeEventListener("storage", handleStorage);
  }, []);

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

  useEffect(() => {
    applyThemeToDocument(effectiveTheme, colorScheme);
    try {
      localStorage.setItem(STORAGE_KEY_THEME, themeStyle);
      localStorage.setItem(STORAGE_KEY_ACCENT, colorScheme);
      localStorage.setItem("leecharr-accent", colorScheme);
      localStorage.setItem(
        "leecharr-theme",
        effectiveTheme === "light" ? "light" : "dark",
      );
    } catch {
      /* storage quota exceeded or restricted */
    }
  }, [effectiveTheme, themeStyle, colorScheme]);

  const setThemeStyle = useCallback((style: ThemeStyle) => {
    trackThemeChange(style);
    setThemeStyleState(style);
    try {
      localStorage.setItem(STORAGE_KEY_THEME, style);
      localStorage.setItem(
        "leecharr-theme",
        style === "light" ? "light" : "dark",
      );
    } catch {
      /* storage quota exceeded or restricted */
    }
  }, []);

  const setColorScheme = useCallback((scheme: ColorScheme) => {
    setColorSchemeState(scheme);
    try {
      localStorage.setItem(STORAGE_KEY_ACCENT, scheme);
      localStorage.setItem("leecharr-accent", scheme);
    } catch {
      /* storage quota exceeded or restricted */
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
      trackThemeChange(next);
      try {
        localStorage.setItem(STORAGE_KEY_THEME, next);
        localStorage.setItem("leecharr-theme", next);
      } catch {
        /* storage quota exceeded or restricted */
      }
      if (generalConfig) {
        saveGeneralConfig.mutate({
          ...generalConfig,
          themeStyle: next,
        });
      }
      return next;
    });
  }, [systemIsLight, generalConfig, saveGeneralConfig]);

  const value = useMemo(
    () => ({
      theme: effectiveTheme,
      themeStyle,
      colorScheme,
      accent: colorScheme,
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
