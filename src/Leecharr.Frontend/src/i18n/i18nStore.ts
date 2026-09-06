import { create } from "zustand";
import { I18nTranslations } from "./types";
import { languages, Language } from "./languages";
import en from "./locales/en";

interface I18nStore {
  language: string;
  translations: I18nTranslations;
  setLanguage: (lang: string) => Promise<void>;
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

// Set initial DOM attributes
if (typeof document !== "undefined") {
  const langConfig = languages.find((l) => l.code === initialLang);
  document.documentElement.lang = initialLang;
  document.documentElement.dir = langConfig?.rtl ? "rtl" : "ltr";
}

export const useI18nStore = create<I18nStore>((set) => ({
  language: initialLang,
  translations: en as I18nTranslations, // Start with English
  isLoading: false,
  setLanguage: async (langCode: string) => {
    set({ isLoading: true });
    try {
      const langConfig = languages.find((l) => l.code === langCode);
      const targetLang = langConfig ? langCode : DEFAULT_LANGUAGE;

      let newTranslations: I18nTranslations;
      if (targetLang === "en") {
        newTranslations = en as I18nTranslations;
      } else {
        const module = await import(`./locales/${targetLang}.ts`);
        newTranslations = module.default;
      }

      if (typeof localStorage !== "undefined") {
        localStorage.setItem("leecharr_lang", targetLang);
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
    } catch (error) {
      console.error(
        `Failed to load translations for language: ${langCode}, falling back to English`,
        error,
      );
      // Fallback to English
      if (typeof document !== "undefined") {
        document.documentElement.lang = DEFAULT_LANGUAGE;
        document.documentElement.dir = "ltr";
      }
      set({
        language: DEFAULT_LANGUAGE,
        translations: en as I18nTranslations,
        isLoading: false,
      });
    }
  },
}));

// If initial language is non-English, load its translations asynchronously
if (initialLang !== "en") {
  useI18nStore.getState().setLanguage(initialLang);
}
