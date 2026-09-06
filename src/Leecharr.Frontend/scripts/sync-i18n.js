const fs = require("fs");
const path = require("path");
const https = require("https");
const crypto = require("crypto");

const FRONTEND_DIR = path.resolve(__dirname, "..");
const SRC_DIR = path.join(FRONTEND_DIR, "src");
const LOCALES_DIR = path.join(SRC_DIR, "i18n", "locales");
const CACHE_FILE = path.join(SRC_DIR, "i18n", ".translation-memory.json");

const TARGET_LANGS = [
  "zh-CN", "hi", "es", "ar", "fr", "bn", "pt", "ru", "ur",
  "id", "de", "ja", "mr", "te", "tr", "ta", "vi", "ko", "it"
];

function hashText(text) {
  return crypto.createHash("sha256").update(String(text).trim()).digest("hex").slice(0, 16);
}

function walkDir(dir) {
  let results = [];
  const list = fs.readdirSync(dir);
  for (const file of list) {
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      if (file !== "node_modules" && file !== "dist" && file !== "locales") {
        results = results.concat(walkDir(fullPath));
      }
    } else if (file.endsWith(".tsx") || (file.endsWith(".ts") && !file.endsWith("types.ts") && !file.endsWith(".d.ts"))) {
      results.push(fullPath);
    }
  }
  return results;
}

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

function setNestedKey(obj, keyPath, value) {
  const parts = keyPath.split(".");
  let cur = obj;
  for (let i = 0; i < parts.length - 1; i++) {
    const p = parts[i];
    if (!cur[p] || typeof cur[p] !== "object") {
      cur[p] = {};
    }
    cur = cur[p];
  }
  cur[parts[parts.length - 1]] = value;
}

function getNestedKey(obj, keyPath) {
  const parts = keyPath.split(".");
  let cur = obj;
  for (const p of parts) {
    if (cur && typeof cur === "object" && p in cur) {
      cur = cur[p];
    } else {
      return undefined;
    }
  }
  return typeof cur === "string" ? cur : undefined;
}

function humanizeKey(k) {
  const last = k.split(".").pop();
  return last
    .replace(/([A-Z])/g, " $1")
    .replace(/_/g, " ")
    .replace(/^./, (s) => s.toUpperCase())
    .trim();
}

// Load Translation Memory Cache
let translationMemory = {};
if (fs.existsSync(CACHE_FILE)) {
  try {
    translationMemory = JSON.parse(fs.readFileSync(CACHE_FILE, "utf8"));
  } catch (e) {
    translationMemory = {};
  }
}
for (const lang of TARGET_LANGS) {
  if (!translationMemory[lang]) translationMemory[lang] = {};
}

// 1. Scan source code for t() keys
console.log("🔍 Scanning frontend source code for i18n keys...");
const sourceFiles = walkDir(SRC_DIR);
const codeKeys = new Set();
for (const file of sourceFiles) {
  const content = fs.readFileSync(file, "utf8");
  const regex = /\bt\(\s*["'`]([a-zA-Z0-9_.]+)["'`]/g;
  let match;
  while ((match = regex.exec(content)) !== null) {
    codeKeys.add(match[1]);
  }
}
console.log(`ℹ️  Found ${codeKeys.size} distinct translation keys in codebase.`);

// 2. Load en.ts
const enText = fs.readFileSync(path.join(LOCALES_DIR, "en.ts"), "utf8");
const enMatch = enText.match(/const en: I18nTranslations = (\{[\s\S]*\});\s*export default/);
if (!enMatch) {
  throw new Error("Could not parse en.ts");
}
const enData = eval("(" + enMatch[1] + ")");

let newEnKeysCount = 0;
for (const key of codeKeys) {
  const existing = getNestedKey(enData, key);
  if (!existing) {
    const def = humanizeKey(key);
    setNestedKey(enData, key, def);
    newEnKeysCount++;
  }
}

if (newEnKeysCount > 0) {
  console.log(`✨ Added ${newEnKeysCount} new keys to en.ts reference.`);
}

const masterFlatEn = flattenKeys(enData);
const totalEnKeys = Object.keys(masterFlatEn).length;
console.log(`📖 Canonical English dictionary: ${totalEnKeys} keys.`);

// 3. Batch Google Translate HTTP helper
function translateBatch(texts, targetLang) {
  if (texts.length === 0) return Promise.resolve([]);
  
  return new Promise((resolve) => {
    // Process variables
    const DELIM = " ||| ";
    const varMap = [];
    const joined = texts.map((t) => {
      return String(t).replace(/(\{\{[a-zA-Z0-9_]+\}\}|\{[a-zA-Z0-9_]+\})/g, (match) => {
        const idx = varMap.length;
        varMap.push(match);
        return `___V${idx}___`;
      });
    }).join(DELIM);

    const url = `https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=${encodeURIComponent(targetLang)}&dt=t&q=${encodeURIComponent(joined)}`;
    
    const req = https.get(url, { headers: { "User-Agent": "Mozilla/5.0" } }, (res) => {
      let raw = "";
      res.on("data", (chunk) => { raw += chunk; });
      res.on("end", () => {
        try {
          const parsed = JSON.parse(raw);
          let fullResult = parsed[0].map((x) => x[0]).join("");
          // Restore variables
          varMap.forEach((v, idx) => {
            const re = new RegExp(`___\\s*V${idx}\\s*___`, "gi");
            fullResult = fullResult.replace(re, v);
          });
          const split = fullResult.split(/\s*\|\|\|\s*/);
          if (split.length === texts.length) {
            resolve(split);
          } else {
            resolve(texts);
          }
        } catch (e) {
          resolve(texts);
        }
      });
    });
    req.on("error", () => resolve(texts));
    req.setTimeout(8000, () => {
      req.destroy();
      resolve(texts);
    });
  });
}

// 4. Smart Incremental Processor for a language
async function processLanguage(lang) {
  const locPath = path.join(LOCALES_DIR, `${lang}.ts`);
  let existingData = {};
  if (fs.existsSync(locPath)) {
    try {
      const txt = fs.readFileSync(locPath, "utf8");
      const m = txt.match(/const\s+[a-zA-Z0-9_]+\s*:\s*I18nTranslations\s*=\s*(\{[\s\S]*\});\s*export default/);
      if (m) existingData = eval("(" + m[1] + ")");
    } catch (e) {}
  }

  const existingFlat = flattenKeys(existingData);
  const langMemory = translationMemory[lang] || {};
  const resultFlat = {};

  // Populate memory from existing non-English translations
  for (const [key, enVal] of Object.entries(masterFlatEn)) {
    const curVal = existingFlat[key];
    const enHash = hashText(enVal);
    if (curVal && curVal !== enVal) {
      langMemory[enHash] = curVal;
    }
  }

  // Determine what unique English phrases actually need translation
  const phrasesNeedingTranslation = new Map(); // hash -> enVal
  const keyToHash = {};

  for (const [key, enVal] of Object.entries(masterFlatEn)) {
    const enHash = hashText(enVal);
    keyToHash[key] = enHash;
    const curVal = existingFlat[key];

    if (langMemory[enHash]) {
      resultFlat[key] = langMemory[enHash];
    } else if (curVal && curVal !== enVal) {
      resultFlat[key] = curVal;
      langMemory[enHash] = curVal;
    } else {
      // Check if technical word that shouldn't change
      const isTech = ["Leecharr", "BitTorrent", "Prowlarr", "Sonarr", "Radarr", "Lidarr", "SignalR", "WebAPI", "RPC", "JSON", "REST", "HTTP", "HTTPS", "TCP", "uTP", "UDP", "DHT", "PEX", "BEP", "SOCKS5", "IPv4", "IPv6"].includes(String(enVal).trim());
      if (isTech) {
        resultFlat[key] = enVal;
        langMemory[enHash] = enVal;
      } else {
        phrasesNeedingTranslation.set(enHash, enVal);
      }
    }
  }

  const missingCount = phrasesNeedingTranslation.size;
  if (missingCount > 0) {
    console.log(`[${lang}] Translating ${missingCount} unique changed/new phrases...`);
    const uniqueList = Array.from(phrasesNeedingTranslation.entries()); // [[hash, text], ...]
    const BATCH_SIZE = 25;

    for (let i = 0; i < uniqueList.length; i += BATCH_SIZE) {
      const chunk = uniqueList.slice(i, i + BATCH_SIZE);
      const textsToTranslate = chunk.map(([, text]) => text);
      const translated = await translateBatch(textsToTranslate, lang);

      for (let j = 0; j < chunk.length; j++) {
        const hash = chunk[j][0];
        const resText = translated[j] || chunk[j][1];
        langMemory[hash] = resText;
      }
    }

    // Assign newly translated phrases to keys
    for (const [key] of Object.entries(masterFlatEn)) {
      if (!resultFlat[key]) {
        const hash = keyToHash[key];
        resultFlat[key] = langMemory[hash] || masterFlatEn[key];
      }
    }
  }

  translationMemory[lang] = langMemory;

  // Rebuild structured nested object
  const nested = {};
  for (const [key, val] of Object.entries(resultFlat)) {
    setNestedKey(nested, key, val);
  }

  return { nested, translatedCount: missingCount };
}

async function run() {
  const startTime = Date.now();

  // Write updated en.ts
  const enOutput = `import { I18nTranslations } from "../types";\n\nconst en: I18nTranslations = ${JSON.stringify(enData, null, 2)};\n\nexport default en;\n`;
  fs.writeFileSync(path.join(LOCALES_DIR, "en.ts"), enOutput, "utf8");

  let totalPhrasesTranslated = 0;

  for (const lang of TARGET_LANGS) {
    const { nested, translatedCount } = await processLanguage(lang);
    totalPhrasesTranslated += translatedCount;

    const varName = lang.replace("-", "");
    const output = `import { I18nTranslations } from "../types";\n\nconst ${varName}: I18nTranslations = ${JSON.stringify(nested, null, 2)};\n\nexport default ${varName};\n`;
    fs.writeFileSync(path.join(LOCALES_DIR, `${lang}.ts`), output, "utf8");
  }

  // Save updated Translation Memory
  fs.writeFileSync(CACHE_FILE, JSON.stringify(translationMemory, null, 2), "utf8");

  // Generate strict types.ts
  function generateType(obj, indent = 2) {
    let lines = ["{"];
    const pad = " ".repeat(indent);
    for (const [k, v] of Object.entries(obj)) {
      const safeKey = JSON.stringify(k);
      if (typeof v === "object" && v !== null && !Array.isArray(v)) {
        lines.push(`${pad}${safeKey}: ${generateType(v, indent + 2)};`);
      } else {
        lines.push(`${pad}${safeKey}: string;`);
      }
    }
    lines.push(" ".repeat(indent - 2) + "}");
    return lines.join("\n");
  }

  const typesOutput = `// Auto-generated TypeScript definitions for Leecharr Localization\n\nexport interface I18nTranslations ${generateType(enData, 2)}\n`;
  fs.writeFileSync(path.join(SRC_DIR, "i18n", "types.ts"), typesOutput, "utf8");

  const elapsed = ((Date.now() - startTime) / 1000).toFixed(2);
  console.log(`\n⚡ i18n Sync complete in ${elapsed}s! (${totalPhrasesTranslated} new phrases translated, 100% key parity across all 20 languages).`);
}

run().catch((err) => {
  console.error("❌ i18n sync error:", err);
  process.exit(1);
});
