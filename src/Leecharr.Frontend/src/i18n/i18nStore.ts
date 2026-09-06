import { create } from "zustand";
import { I18nTranslations } from "./types";
import { languages } from "./languages";
import en from "./locales/en";

interface I18nStore {
  language: string;
  translations: I18nTranslations;
  setLanguage: (lang: string) => Promise<void>;
  isLoading: boolean;
}

const getBrowserLanguage = () => {
  const browserLang = navigator.language.split("-")[0];
  const supported = languages.find((l) => l.code === browserLang);
  return supported ? supported.code : "en";
};

export const useI18nStore = create<I18nStore>((set) => ({
  language: localStorage.getItem("leecharr_lang") || getBrowserLanguage(),
  translations: en as I18nTranslations, // Start with English as fallback
  isLoading: false,
  setLanguage: async (langCode: string) => {
    set({ isLoading: true });
    try {
      // Dynamic import of the locale file
      const module = await import(`./locales/${langCode}.ts`);
      const newTranslations = module.default;

      localStorage.setItem("leecharr_lang", langCode);
      const langConfig = languages.find((l) => l.code === langCode);

      if (langConfig) {
        document.documentElement.lang = langCode;
        document.documentElement.dir = langConfig.rtl ? "rtl" : "ltr";
      }

      set({
        language: langCode,
        translations: newTranslations,
        isLoading: false,
      });
    } catch (error) {
      console.error(
        `Failed to load translations for language: ${langCode}`,
        error,
      );
      // Fallback to English
      set({
        language: "en",
        translations: en as I18nTranslations,
        isLoading: false,
      });
    }
  },
}));
