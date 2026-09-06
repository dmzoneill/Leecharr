import { useCallback } from "react";
import { useI18nStore } from "./i18nStore";
import en from "./locales/en";

function lookupKey(obj: any, keys: string[]): string | null {
  let value: any = obj;
  for (const k of keys) {
    if (value && typeof value === "object" && k in value) {
      value = value[k];
    } else {
      return null;
    }
  }
  return typeof value === "string" ? value : null;
}

export const useTranslation = () => {
  const translations = useI18nStore((state) => state.translations);

  const t = useCallback(
    (key: string, params?: Record<string, string | number>) => {
      const keys = key.split(".");
      // 1. Look up in active translations
      let value = lookupKey(translations, keys);

      // 2. Fall back to English if missing
      if (!value && translations !== en) {
        value = lookupKey(en, keys);
      }

      // 3. Fall back to raw key if still not found
      if (!value) {
        value = key;
      }

      if (params) {
        return Object.entries(params).reduce((acc, [paramKey, paramValue]) => {
          return acc.replace(
            new RegExp(`{{\\s*${paramKey}\\s*}}`, "g"),
            String(paramValue),
          );
        }, value!);
      }

      return value;
    },
    [translations],
  );

  return { t };
};
