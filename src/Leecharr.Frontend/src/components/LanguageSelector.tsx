import React, { useState, useRef, useEffect } from "react";
import { useI18nStore, useTranslation, languages } from "../i18n";

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
  const dropdownRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const activeLang = languages.find((l) => l.code === language) || languages[0];

  const filteredLanguages = languages.filter(
    (l) =>
      l.name.toLowerCase().includes(search.toLowerCase()) ||
      l.nativeName.toLowerCase().includes(search.toLowerCase()) ||
      l.code.toLowerCase().includes(search.toLowerCase()),
  );

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
      if (event.key === "Escape") {
        setIsOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, []);

  useEffect(() => {
    if (isOpen && inputRef.current) {
      inputRef.current.focus();
    }
  }, [isOpen]);

  const handleSelect = (langCode: string) => {
    setLanguage(langCode);
    setIsOpen(false);
    setSearch("");
  };

  return (
    <div className={`language-selector ${className}`} ref={dropdownRef}>
      <button
        type="button"
        className="language-selector-btn"
        onClick={() => setIsOpen(!isOpen)}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        title={t("common.languageTitle", "Language: {name} ({nativeName})", {
          name: activeLang.name,
          nativeName: activeLang.nativeName,
        })}
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
        <span className="language-selector-caret">▾</span>
      </button>

      {isOpen && (
        <div className={`language-selector-dropdown align-${align}`}>
          <div className="language-selector-search-box">
            <input
              ref={inputRef}
              type="text"
              className="language-selector-search-input"
              placeholder={t("common.searchLanguage", "Search language...")}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <ul className="language-selector-list" role="listbox">
            {filteredLanguages.map((lang) => {
              const isSelected = language === lang.code;
              return (
                <li
                  key={lang.code}
                  role="option"
                  aria-selected={isSelected}
                  className={`language-selector-item ${isSelected ? "active" : ""}`}
                  onClick={() => handleSelect(lang.code)}
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
