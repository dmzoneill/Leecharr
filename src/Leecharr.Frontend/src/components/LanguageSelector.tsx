import React, { useState, useRef, useEffect } from "react";
import { useI18nStore, useTranslation, languages } from "../i18n";
import { trackLanguageChange } from "../utils/analytics";

export interface LanguageSelectorProps {
  align?: "left" | "right";
  className?: string;
  showFullLabel?: boolean;
}

export const LanguageSelector: React.FC<LanguageSelectorProps> = ({
  align = "right",
  className = "",
  showFullLabel = false,
}) => {
  const { language, setLanguage } = useI18nStore();
  const { t } = useTranslation();
  const [isOpen, setIsOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [highlightedIndex, setHighlightedIndex] = useState<number>(-1);
  const dropdownRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const activeLang = languages.find((l) => l.code === language) || languages[0];

  const filteredLanguages = languages.filter(
    (l) =>
      l.name.toLowerCase().includes(search.toLowerCase()) ||
      l.nativeName.toLowerCase().includes(search.toLowerCase()) ||
      l.code.toLowerCase().includes(search.toLowerCase()),
  );

  const defaultTitle = `Language: ${activeLang.name} (${activeLang.nativeName})`;
  const localizedTitle = t(
    "common.languageTitle",
    "Language: {name} ({nativeName})",
    {
      name: activeLang.name,
      nativeName: activeLang.nativeName,
    },
  );
  const titleTooltip =
    localizedTitle &&
    (localizedTitle.includes(activeLang.name) ||
      localizedTitle.includes(activeLang.nativeName))
      ? localizedTitle
      : defaultTitle;

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (
        dropdownRef.current &&
        !dropdownRef.current.contains(event.target as Node)
      ) {
        setIsOpen(false);
      }
    };
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && isOpen) {
        event.preventDefault();
        setIsOpen(false);
        buttonRef.current?.focus();
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen]);

  useEffect(() => {
    if (isOpen) {
      inputRef.current?.focus();
      const currentIdx = filteredLanguages.findIndex((l) => l.code === language);
      setHighlightedIndex(
        currentIdx >= 0 ? currentIdx : filteredLanguages.length > 0 ? 0 : -1,
      );
    } else {
      setSearch("");
      setHighlightedIndex(-1);
    }
  }, [isOpen, filteredLanguages, language]);

  useEffect(() => {
    if (highlightedIndex >= 0 && listRef.current) {
      const activeEl = listRef.current.children[
        highlightedIndex
      ] as HTMLElement | undefined;
      if (activeEl?.scrollIntoView) {
        activeEl.scrollIntoView({ block: "nearest" });
      }
    }
  }, [highlightedIndex]);

  const handleSelect = (langCode: string) => {
    trackLanguageChange(langCode);
    setLanguage(langCode);
    setIsOpen(false);
    setSearch("");
    buttonRef.current?.focus();
  };

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearch(e.target.value);
    setHighlightedIndex(0);
  };

  const handleInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      if (filteredLanguages.length === 0) return;
      setHighlightedIndex((prev) =>
        prev < 0 || prev >= filteredLanguages.length - 1 ? 0 : prev + 1,
      );
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      if (filteredLanguages.length === 0) return;
      setHighlightedIndex((prev) =>
        prev <= 0 ? filteredLanguages.length - 1 : prev - 1,
      );
    } else if (event.key === "Enter" || (event.key === " " && search === "")) {
      event.preventDefault();
      if (
        highlightedIndex >= 0 &&
        highlightedIndex < filteredLanguages.length
      ) {
        handleSelect(filteredLanguages[highlightedIndex].code);
      } else if (filteredLanguages.length > 0) {
        handleSelect(filteredLanguages[0].code);
      }
    } else if (event.key === "Escape") {
      event.preventDefault();
      setIsOpen(false);
      buttonRef.current?.focus();
    }
  };

  const handleOptionKeyDown = (
    event: React.KeyboardEvent<HTMLLIElement>,
    langCode: string,
    index: number,
  ) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      handleSelect(langCode);
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      if (filteredLanguages.length === 0) return;
      const nextIndex = (index + 1) % filteredLanguages.length;
      setHighlightedIndex(nextIndex);
      const nextEl = listRef.current?.children[nextIndex] as HTMLElement | undefined;
      nextEl?.focus();
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      if (filteredLanguages.length === 0) return;
      const prevIndex = index <= 0 ? filteredLanguages.length - 1 : index - 1;
      setHighlightedIndex(prevIndex);
      const prevEl = listRef.current?.children[prevIndex] as HTMLElement | undefined;
      prevEl?.focus();
    } else if (event.key === "Escape") {
      event.preventDefault();
      setIsOpen(false);
      buttonRef.current?.focus();
    }
  };

  return (
    <div className={`language-selector ${className}`} ref={dropdownRef}>
      <button
        ref={buttonRef}
        type="button"
        className="language-selector-btn"
        onClick={() => setIsOpen(!isOpen)}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        title={titleTooltip}
      >
        <span className="language-selector-btn-flag">{activeLang.flag}</span>
        {showFullLabel ? (
          <span className="language-selector-btn-full">
            {activeLang.nativeName} ({activeLang.name})
          </span>
        ) : (
          <span className="language-selector-btn-code">
            {activeLang.code.toUpperCase()}
          </span>
        )}
      </button>

      {isOpen && (
        <div className={`language-selector-dropdown align-${align}`}>
          <div className="language-selector-search-box">
            <input
              ref={inputRef}
              type="text"
              className="language-selector-search-input"
              placeholder={t("common.searchLanguage", "Search language...")}
              aria-label="Search language"
              role="combobox"
              aria-autocomplete="list"
              aria-expanded={isOpen}
              aria-controls="language-selector-list"
              aria-activedescendant={
                highlightedIndex >= 0 && filteredLanguages[highlightedIndex]
                  ? `language-option-${filteredLanguages[highlightedIndex].code}`
                  : undefined
              }
              value={search}
              onChange={handleSearchChange}
              onKeyDown={handleInputKeyDown}
            />
          </div>
          <ul
            ref={listRef}
            id="language-selector-list"
            className="language-selector-list"
            role="listbox"
            aria-label="Languages"
          >
            {filteredLanguages.map((lang, index) => {
              const isSelected = language === lang.code;
              const isHighlighted = highlightedIndex === index;
              return (
                <li
                  key={lang.code}
                  id={`language-option-${lang.code}`}
                  role="option"
                  tabIndex={0}
                  aria-selected={isSelected}
                  className={`language-selector-item ${isSelected ? "active" : ""} ${isHighlighted ? "highlighted" : ""}`}
                  onClick={() => handleSelect(lang.code)}
                  onMouseEnter={() => setHighlightedIndex(index)}
                  onFocus={() => setHighlightedIndex(index)}
                  onKeyDown={(e) => handleOptionKeyDown(e, lang.code, index)}
                >
                  <div className="language-selector-item-left">
                    <span className="language-selector-flag">{lang.flag}</span>
                    <div className="language-selector-text">
                      <div className="language-selector-native-row">
                        <span className="language-selector-native">
                          {lang.nativeName}
                        </span>
                        {lang.rtl && (
                          <span className="language-selector-rtl-badge">
                            RTL
                          </span>
                        )}
                      </div>
                      <span className="language-selector-english">
                        {lang.name}
                      </span>
                    </div>
                  </div>
                  {isSelected && (
                    <span className="language-selector-checkmark">✓</span>
                  )}
                </li>
              );
            })}
            {filteredLanguages.length === 0 && (
              <li className="language-selector-empty">
                {t("common.noLanguagesFound", "No languages found")}
              </li>
            )}
          </ul>
        </div>
      )}
    </div>
  );
};
