import React, { useState, useRef, useEffect } from "react";
import { useI18nStore, languages, Language } from "../i18n";

export const LanguageSelector: React.FC = () => {
  const { language, setLanguage } = useI18nStore();
  const [isOpen, setIsOpen] = useState(false);
  const [search, setSearch] = useState("");
  const dropdownRef = useRef<HTMLDivElement>(null);

  const activeLang = languages.find((l) => l.code === language) || languages[0];

  const filteredLanguages = languages.filter(
    (l) =>
      l.name.toLowerCase().includes(search.toLowerCase()) ||
      l.nativeName.toLowerCase().includes(search.toLowerCase()),
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
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const handleSelect = (langCode: string) => {
    setLanguage(langCode);
    setIsOpen(false);
    setSearch("");
  };

  return (
    <div className="relative inline-block text-left" ref={dropdownRef}>
      <button
        type="button"
        className="inline-flex items-center justify-center w-full px-4 py-2 text-sm font-medium text-[#F8F4ED] bg-[#171B35] rounded-md hover:bg-[#23284B] focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-[#FFD166] transition-colors"
        onClick={() => setIsOpen(!isOpen)}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
      >
        <span className="mr-2" role="img" aria-label={activeLang.name}>
          {activeLang.flag}
        </span>
        {activeLang.code.toUpperCase()}
      </button>

      {isOpen && (
        <div className="absolute right-0 rtl:right-auto rtl:left-0 z-50 w-56 mt-2 origin-top-right bg-[#171B35] rounded-md shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none">
          <div className="p-2 border-b border-[#23284B]">
            <input
              type="text"
              className="w-full px-3 py-1 text-sm bg-[#10111A] text-[#F8F4ED] placeholder-[#C7C5D3] rounded border border-[#23284B] focus:outline-none focus:ring-1 focus:ring-[#FFD166]"
              placeholder="Search..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              autoFocus
            />
          </div>
          <ul
            className="max-h-60 py-1 overflow-auto text-base sm:text-sm"
            role="listbox"
          >
            {filteredLanguages.map((lang) => (
              <li
                key={lang.code}
                role="option"
                aria-selected={language === lang.code}
                className={`relative cursor-default select-none py-2 pl-10 rtl:pl-4 rtl:pr-10 pr-4 hover:bg-[#23284B] transition-colors ${
                  language === lang.code
                    ? "text-[#FFD166] bg-[#23284B]"
                    : "text-[#F8F4ED]"
                }`}
                onClick={() => handleSelect(lang.code)}
              >
                <span className="absolute inset-y-0 left-0 rtl:left-auto rtl:right-0 flex items-center pl-3 rtl:pl-0 rtl:pr-3">
                  <span role="img" aria-label={lang.name}>
                    {lang.flag}
                  </span>
                </span>
                <div className="flex flex-col">
                  <span
                    className={`block truncate ${language === lang.code ? "font-medium" : "font-normal"}`}
                  >
                    {lang.nativeName}{" "}
                    {lang.rtl && (
                      <span className="ml-2 rtl:mr-2 rtl:ml-0 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium bg-[#10111A] text-[#C7C5D3]">
                        RTL
                      </span>
                    )}
                  </span>
                  <span className="block truncate text-xs text-[#C7C5D3]">
                    {lang.name}
                  </span>
                </div>
                {language === lang.code && (
                  <span className="absolute inset-y-0 right-0 rtl:right-auto rtl:left-0 flex items-center pr-3 rtl:pr-0 rtl:pl-3 text-[#FFD166]">
                    ✓
                  </span>
                )}
              </li>
            ))}
            {filteredLanguages.length === 0 && (
              <li className="py-2 px-4 text-sm text-[#C7C5D3] text-center">
                No languages found
              </li>
            )}
          </ul>
        </div>
      )}
    </div>
  );
};
