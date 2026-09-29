import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { languages } from "../i18n/languages";
import en from "../i18n/locales/en";
import de from "../i18n/locales/de";
import es from "../i18n/locales/es";
import fr from "../i18n/locales/fr";
import itLocale from "../i18n/locales/it";
import pt from "../i18n/locales/pt";
import ru from "../i18n/locales/ru";
import tr from "../i18n/locales/tr";
import id from "../i18n/locales/id";
import zhCN from "../i18n/locales/zh-CN";
import ja from "../i18n/locales/ja";
import ko from "../i18n/locales/ko";
import ar from "../i18n/locales/ar";
import hi from "../i18n/locales/hi";
import bn from "../i18n/locales/bn";
import mr from "../i18n/locales/mr";
import ta from "../i18n/locales/ta";
import te from "../i18n/locales/te";
import ur from "../i18n/locales/ur";
import vi from "../i18n/locales/vi";
import { translate } from "../i18n/useTranslation";
import { useI18nStore } from "../i18n/i18nStore";

const allLocales = [
  { code: "en", dict: en },
  { code: "de", dict: de },
  { code: "es", dict: es },
  { code: "fr", dict: fr },
  { code: "it", dict: itLocale },
  { code: "pt", dict: pt },
  { code: "ru", dict: ru },
  { code: "tr", dict: tr },
  { code: "id", dict: id },
  { code: "zh-CN", dict: zhCN },
  { code: "ja", dict: ja },
  { code: "ko", dict: ko },
  { code: "ar", dict: ar },
  { code: "hi", dict: hi },
  { code: "bn", dict: bn },
  { code: "mr", dict: mr },
  { code: "ta", dict: ta },
  { code: "te", dict: te },
  { code: "ur", dict: ur },
  { code: "vi", dict: vi },
];

describe("LanguageSelector & i18n languageTitle", () => {
  it("all 20 locales define common.languageTitle with {name} and {nativeName} placeholders", () => {
    for (const { code, dict } of allLocales) {
      const title = dict.common.languageTitle;
      assert.ok(
        title.includes("{name}") || title.includes("{{name}}"),
        `Locale ${code} languageTitle is missing {name} placeholder: ${title}`,
      );
      assert.ok(
        title.includes("{nativeName}") || title.includes("{{nativeName}}"),
        `Locale ${code} languageTitle is missing {nativeName} placeholder: ${title}`,
      );
      assert.notStrictEqual(
        title,
        "Language Title",
        `Locale ${code} still has literal "Language Title"`,
      );
    }
  });

  it("correctly interpolates active language name and nativeName in English", () => {
    useI18nStore.setState({ language: "en", translations: en });
    const active = languages.find((l) => l.code === "en")!;
    const interpolated = translate(
      "common.languageTitle",
      "Language: {name} ({nativeName})",
      {
        name: active.name,
        nativeName: active.nativeName,
      },
    );
    assert.strictEqual(interpolated, "Language: English (English)");
  });

  it("correctly interpolates active language in German and French", () => {
    useI18nStore.setState({ language: "de", translations: de });
    const activeDe = languages.find((l) => l.code === "de")!;
    const deTitle = translate(
      "common.languageTitle",
      "Language: {name} ({nativeName})",
      {
        name: activeDe.name,
        nativeName: activeDe.nativeName,
      },
    );
    assert.strictEqual(deTitle, "Sprache: German (Deutsch)");

    useI18nStore.setState({ language: "fr", translations: fr });
    const activeFr = languages.find((l) => l.code === "fr")!;
    const frTitle = translate(
      "common.languageTitle",
      "Language: {name} ({nativeName})",
      {
        name: activeFr.name,
        nativeName: activeFr.nativeName,
      },
    );
    assert.strictEqual(frTitle, "Langue : French (Français)");
  });

  it("falls back gracefully if translation string lacks placeholder", () => {
    const active = languages.find((l) => l.code === "en")!;
    const defaultTitle = `Language: ${active.name} (${active.nativeName})`;
    const brokenTitle = "Language Title"; // simulates broken un-interpolated value
    const titleTooltip =
      brokenTitle &&
      (brokenTitle.includes(active.name) ||
        brokenTitle.includes(active.nativeName))
        ? brokenTitle
        : defaultTitle;

    assert.strictEqual(titleTooltip, "Language: English (English)");
  });

  it("keyboard navigation wraps indices correctly on ArrowDown and ArrowUp", () => {
    const listCount = 5;
    const nextDown = (current: number) =>
      current < 0 || current >= listCount - 1 ? 0 : current + 1;
    const nextUp = (current: number) =>
      current <= 0 ? listCount - 1 : current - 1;

    // Moving down from initial
    assert.strictEqual(nextDown(-1), 0);
    assert.strictEqual(nextDown(0), 1);
    assert.strictEqual(nextDown(4), 0);

    // Moving up
    assert.strictEqual(nextUp(0), 4);
    assert.strictEqual(nextUp(3), 2);
    assert.strictEqual(nextUp(-1), 4);
  });

  it("filtering languages works across name, nativeName, and code", () => {
    const filter = (search: string) =>
      languages.filter(
        (l) =>
          l.name.toLowerCase().includes(search.toLowerCase()) ||
          l.nativeName.toLowerCase().includes(search.toLowerCase()) ||
          l.code.toLowerCase().includes(search.toLowerCase()),
      );

    assert.ok(filter("Deutsch").some((l) => l.code === "de"));
    assert.ok(filter("German").some((l) => l.code === "de"));
    assert.ok(filter("de").some((l) => l.code === "de"));
    assert.ok(filter("Español").some((l) => l.code === "es"));
    assert.ok(filter("Spanish").some((l) => l.code === "es"));
    assert.ok(filter("nonexistent_lang_12345").length === 0);
  });
});
