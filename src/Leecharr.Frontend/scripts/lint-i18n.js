#!/usr/bin/env node

const fs = require("fs");
const path = require("path");

const ROOT_DIR = path.resolve(__dirname, "..");
const SRC_DIR = path.join(ROOT_DIR, "src");
const LOCALES_DIR = path.join(SRC_DIR, "i18n", "locales");

// 1. Gather all locale files
const EXPECTED_LOCALES = [
  "en", "zh-CN", "hi", "es", "ar", "fr", "bn", "pt", "ru", "ur",
  "id", "de", "ja", "mr", "te", "tr", "ta", "vi", "ko", "it"
];

function loadLocale(langCode) {
  const filePath = path.join(LOCALES_DIR, `${langCode}.ts`);
  if (!fs.existsSync(filePath)) {
    throw new Error(`Missing locale file: ${filePath}`);
  }
  const content = fs.readFileSync(filePath, "utf8");
  // Extract the exported object
  const match = content.match(/const\s+[a-zA-Z0-9_]+\s*:\s*I18nTranslations\s*=\s*(\{[\s\S]*\});\s*export default/);
  if (!match) {
    throw new Error(`Failed to parse I18nTranslations object from ${filePath}`);
  }
  return eval("(" + match[1] + ")");
}

// 2. Flatten object into dot-notated keys
function flattenKeys(obj, prefix = "") {
  let keys = {};
  for (const [k, v] of Object.entries(obj)) {
    const fullKey = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === "object" && !Array.isArray(v)) {
      Object.assign(keys, flattenKeys(v, fullKey));
    } else {
      keys[fullKey] = v;
    }
  }
  return keys;
}

// 3. Scan all source files for t(...) and translate(...) calls
function getSourceFiles(dir) {
  let results = [];
  const list = fs.readdirSync(dir);
  for (const file of list) {
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      if (file !== "node_modules" && file !== "dist" && file !== "locales") {
        results = results.concat(getSourceFiles(fullPath));
      }
    } else if (file.endsWith(".tsx") || (file.endsWith(".ts") && !file.endsWith("types.ts") && !file.endsWith(".d.ts"))) {
      results.push(fullPath);
    }
  }
  return results;
}

console.log("🔍 [i18n-lint] Auditing localization catalogues & source references...");

// Load source of truth: en.ts
const enData = loadLocale("en");
const enFlat = flattenKeys(enData);
const enKeys = new Set(Object.keys(enFlat));

console.log(`📖 Source of truth (en.ts): ${enKeys.size} total translation keys`);

let errorsFound = 0;

// Check 1: Ensure all other locales match en.ts exactly
for (const lang of EXPECTED_LOCALES) {
  if (lang === "en") continue;
  try {
    const locData = loadLocale(lang);
    const locFlat = flattenKeys(locData);
    const locKeys = new Set(Object.keys(locFlat));

    const missingInLoc = [...enKeys].filter(k => !locKeys.has(k));
    const extraInLoc = [...locKeys].filter(k => !enKeys.has(k));

    if (missingInLoc.length > 0) {
      console.error(`❌ [${lang}] Missing ${missingInLoc.length} keys defined in en.ts:`);
      missingInLoc.slice(0, 10).forEach(k => console.error(`    - ${k}`));
      if (missingInLoc.length > 10) console.error(`    ... and ${missingInLoc.length - 10} more`);
      errorsFound++;
    }

    if (extraInLoc.length > 0) {
      console.error(`⚠️ [${lang}] Has ${extraInLoc.length} extra keys not in en.ts:`);
      extraInLoc.slice(0, 10).forEach(k => console.error(`    + ${k}`));
      if (extraInLoc.length > 10) console.error(`    ... and ${extraInLoc.length - 10} more`);
      errorsFound++;
    }

    if (missingInLoc.length === 0 && extraInLoc.length === 0) {
      console.log(`✅ [${lang}] 100% key parity with en.ts (${locKeys.size} keys)`);
    }
  } catch (err) {
    console.error(`❌ [${lang}] Error reading locale: ${err.message}`);
    errorsFound++;
  }
}

// Check 2: Scan source code for t() keys and ensure they exist in en.ts
const sourceFiles = getSourceFiles(SRC_DIR);
const usedKeys = new Map();

for (const file of sourceFiles) {
  const content = fs.readFileSync(file, "utf8");
  const relPath = path.relative(SRC_DIR, file);

  // Match t("...") or t('...')
  const regex = /\bt\(\s*["'`]([a-zA-Z0-9_.]+)["'`]/g;
  let match;
  while ((match = regex.exec(content)) !== null) {
    const key = match[1];
    if (!usedKeys.has(key)) {
      usedKeys.set(key, []);
    }
    usedKeys.get(key).push(relPath);
  }
}

console.log(`🔎 Scanned ${sourceFiles.length} source files, found ${usedKeys.size} unique t() key references`);

const unmappedCodeKeys = [];
for (const [key, files] of usedKeys.entries()) {
  if (!enKeys.has(key)) {
    unmappedCodeKeys.push({ key, files: files.slice(0, 2) });
  }
}

if (unmappedCodeKeys.length > 0) {
  console.error(`❌ Found ${unmappedCodeKeys.length} t() keys used in code that are missing in en.ts:`);
  unmappedCodeKeys.forEach(item => {
    console.error(`    - "${item.key}" (in ${item.files.join(", ")})`);
  });
  errorsFound += unmappedCodeKeys.length;
} else {
  console.log(`✅ All ${usedKeys.size} t() key references in code exist in en.ts`);
}

if (errorsFound > 0) {
  console.error(`\n❌ i18n lint failed with ${errorsFound} error(s).`);
  process.exit(1);
} else {
  console.log("\n🎉 All 20 localization catalogues and source references are 100% synchronized and valid!");
  process.exit(0);
}
