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

export type TranslationParams =
  | Record<string, string | number | boolean | null | undefined>
  | (string | number | boolean | null | undefined)[];

function interpolate(text: string, params?: TranslationParams): string {
  if (!params) return text;
  if (Array.isArray(params)) {
    return params.reduce<string>((acc, val, idx) => {
      return acc.replace(new RegExp(`\\{${idx}\\}`, "g"), String(val ?? ""));
    }, text);
  }
  return Object.entries(params).reduce<string>(
    (acc, [paramKey, paramValue]) => {
      return acc
        .replace(
          new RegExp(`\\{\\{\\s*${paramKey}\\s*\\}\\}`, "g"),
          String(paramValue ?? ""),
        )
        .replace(
          new RegExp(`\\{${paramKey}\\}`, "g"),
          String(paramValue ?? ""),
        );
    },
    text,
  );
}

export function translate(
  key: string,
  defaultOrParams?: string | TranslationParams,
  params?: TranslationParams,
): string {
  const translations = useI18nStore.getState().translations;
  const keys = key.split(".");
  let value = lookupKey(translations, keys);
  if (!value && translations !== en) {
    value = lookupKey(en, keys);
  }
  if (!value) {
    if (typeof defaultOrParams === "string") {
      value = defaultOrParams;
    } else {
      value = key;
    }
  }
  const actualParams =
    typeof defaultOrParams === "object" ? defaultOrParams : params;
  return interpolate(value, actualParams);
}

export const useTranslation = () => {
  const translations = useI18nStore((state) => state.translations);

  const t = useCallback(
    (
      key: string,
      defaultOrParams?: string | TranslationParams,
      params?: TranslationParams,
    ) => {
      const keys = key.split(".");
      let value = lookupKey(translations, keys);

      if (!value && translations !== en) {
        value = lookupKey(en, keys);
      }

      if (!value) {
        if (typeof defaultOrParams === "string") {
          value = defaultOrParams;
        } else {
          value = key;
        }
      }

      const actualParams =
        typeof defaultOrParams === "object" ? defaultOrParams : params;
      return interpolate(value, actualParams);
    },
    [translations],
  );

  return { t };
};
