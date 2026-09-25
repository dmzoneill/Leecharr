import { useCallback } from "react";
import { useI18nStore } from "./i18nStore";
import en from "./locales/en";

function lookupKey(obj: unknown, keys: string[]): string | null {
  let value: unknown = obj;
  for (const k of keys) {
    if (value && typeof value === "object" && k in value) {
      value = (value as Record<string, unknown>)[k];
    } else {
      return null;
    }
  }
  return typeof value === "string" ? value : null;
}

export type TranslationParams =
  | Record<string, string | number | boolean | null | undefined>
  | (string | number | boolean | null | undefined)[];

export type TFunction = {
  (key: string, defaultValue?: string, params?: TranslationParams): string;
  (key: string, params?: TranslationParams, defaultValue?: string): string;
};

function extractArgs(
  arg1?: string | TranslationParams,
  arg2?: string | TranslationParams,
): { defaultValue?: string; params?: TranslationParams } {
  let defaultValue: string | undefined;
  let params: TranslationParams | undefined;

  if (typeof arg1 === "string") {
    defaultValue = arg1;
    if (typeof arg2 === "object" && arg2 !== null) {
      params = arg2;
    }
  } else if (typeof arg2 === "string") {
    defaultValue = arg2;
    if (typeof arg1 === "object" && arg1 !== null) {
      params = arg1;
    }
  } else if (typeof arg1 === "object" && arg1 !== null) {
    params = arg1;
    if (
      !Array.isArray(arg1) &&
      "defaultValue" in arg1 &&
      typeof arg1.defaultValue === "string"
    ) {
      defaultValue = arg1.defaultValue;
    }
  }
  return { defaultValue, params };
}

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
  arg1?: string | TranslationParams,
  arg2?: string | TranslationParams,
): string {
  const translations = useI18nStore.getState().translations;
  const keys = key.split(".");
  let value = lookupKey(translations, keys);
  if (!value && translations !== en) {
    value = lookupKey(en, keys);
  }
  const { defaultValue, params } = extractArgs(arg1, arg2);
  if (!value) {
    value = defaultValue ?? key;
  }
  return interpolate(value ?? key, params);
}

export const useTranslation = () => {
  const translations = useI18nStore((state) => state.translations);

  const t: TFunction = useCallback(
    (
      key: string,
      arg1?: string | TranslationParams,
      arg2?: string | TranslationParams,
    ) => {
      const keys = key.split(".");
      let value = lookupKey(translations, keys);

      if (!value && translations !== en) {
        value = lookupKey(en, keys);
      }

      const { defaultValue, params } = extractArgs(arg1, arg2);
      if (!value) {
        value = defaultValue ?? key;
      }

      return interpolate(value ?? key, params);
    },
    [translations],
  ) as TFunction;

  return { t };
};

export default useTranslation;
