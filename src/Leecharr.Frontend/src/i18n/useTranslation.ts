import { useCallback } from "react";
import { useI18nStore } from "./i18nStore";

export const useTranslation = () => {
  const translations = useI18nStore((state) => state.translations);

  const t = useCallback(
    (key: string, params?: Record<string, string | number>) => {
      const keys = key.split(".");
      let value: any = translations;

      for (const k of keys) {
        if (value && typeof value === "object" && k in value) {
          value = value[k];
        } else {
          return key; // Fallback to key itself if not found
        }
      }

      if (typeof value !== "string") {
        return key;
      }

      if (params) {
        return Object.entries(params).reduce((acc, [paramKey, paramValue]) => {
          return acc.replace(
            new RegExp(`{{\\s*${paramKey}\\s*}}`, "g"),
            String(paramValue),
          );
        }, value);
      }

      return value;
    },
    [translations],
  );

  return { t };
};
