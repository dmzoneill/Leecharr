import { create } from "zustand";
import { I18nTranslations } from "./types";
import { languages, Language } from "./languages";
import { localeMap, en } from "./locales";

interface I18nStore {
  language: string;
  translations: I18nTranslations;
  setLanguage: (lang: string) => void;
  isLoading: boolean;
}

const DEFAULT_LANGUAGE = "en";

/**
 * Determines the initial language:
 * 1. Saved user preference from localStorage (if valid)
 * 2. System/browser language from navigator.languages or navigator.language
 * 3. Default fallback to English ('en')
 */
export function getInitialLanguage(): string {
  try {
    // 1. User manual preference
    if (typeof localStorage !== "undefined") {
      const saved = localStorage.getItem("leecharr_lang");
      if (saved && languages.some((l) => l.code === saved)) {
        return saved;
      }
    }

    // 2. System/Browser language detection
    if (typeof navigator !== "undefined") {
      const browserLanguages: string[] = [];
      if (Array.isArray(navigator.languages)) {
        browserLanguages.push(...navigator.languages);
      }
      if (navigator.language) {
        browserLanguages.push(navigator.language);
      }

      for (const rawLang of browserLanguages) {
        if (!rawLang || typeof rawLang !== "string") continue;
        const normalized = rawLang.trim().toLowerCase();

        // Exact code match (e.g. 'zh-cn', 'en', 'fr')
        const exact = languages.find(
          (l) => l.code.toLowerCase() === normalized,
        );
        if (exact) return exact.code;

        // Chinese variants handling
        if (normalized.startsWith("zh")) {
          return "zh-CN";
        }

        // Prefix match (e.g. 'es-mx' -> 'es', 'pt-br' -> 'pt', 'de-at' -> 'de')
        const prefix = normalized.split("-")[0];
        const prefixMatch = languages.find(
          (l) => l.code.toLowerCase() === prefix,
        );
        if (prefixMatch) return prefixMatch.code;
      }
    }
  } catch (err) {
    // Ignore access errors (e.g. sandboxed iframe or strict privacy)
  }

  // 3. Fallback to English
  return DEFAULT_LANGUAGE;
}

const initialLang = getInitialLanguage();
const initialTranslations = localeMap[initialLang] || en;

// Set initial DOM attributes
if (typeof document !== "undefined") {
  const langConfig = languages.find((l) => l.code === initialLang);
  document.documentElement.lang = initialLang;
  document.documentElement.dir = langConfig?.rtl ? "rtl" : "ltr";
}

export const useI18nStore = create<I18nStore>((set) => ({
  language: initialLang,
  translations: initialTranslations,
  isLoading: false,
  setLanguage: (langCode: string) => {
    const langConfig = languages.find((l) => l.code === langCode);
    const targetLang = langConfig ? langCode : DEFAULT_LANGUAGE;
    const newTranslations = localeMap[targetLang] || en;

    if (typeof localStorage !== "undefined") {
      try {
        localStorage.setItem("leecharr_lang", targetLang);
      } catch {
        // ignore quota / private browsing errors
      }
    }

    if (typeof document !== "undefined") {
      const activeConfig = languages.find((l) => l.code === targetLang);
      document.documentElement.lang = targetLang;
      document.documentElement.dir = activeConfig?.rtl ? "rtl" : "ltr";
    }

    set({
      language: targetLang,
      translations: newTranslations,
      isLoading: false,
    });
  },
}));
