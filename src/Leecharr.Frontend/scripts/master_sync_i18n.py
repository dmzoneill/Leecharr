#!/usr/bin/env python3
import json
import re
import sys
import time
import hashlib
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
import urllib.request
import urllib.parse

SCRIPT_DIR = Path(__file__).resolve().parent
FRONTEND_DIR = SCRIPT_DIR.parent
SRC_DIR = FRONTEND_DIR / "src"
LOCALES_DIR = SRC_DIR / "i18n" / "locales"
CACHE_FILE = SRC_DIR / "i18n" / ".translation-memory.json"
TYPES_FILE = SRC_DIR / "i18n" / "types.ts"

LANGS = [
    "zh-CN",
    "hi",
    "es",
    "ar",
    "fr",
    "bn",
    "pt",
    "ru",
    "ur",
    "id",
    "de",
    "ja",
    "mr",
    "te",
    "tr",
    "ta",
    "vi",
    "ko",
    "it",
]

BRAND_TERMS = {
    "leecharr",
    "sonarr",
    "radarr",
    "lidarr",
    "prowlarr",
    "readarr",
    "servarr",
    "deluge",
    "qbittorrent",
    "transmission",
    "utorrent",
    "biglybt",
    "rtorrent",
    "sabnzbd",
    "nzbget",
    "aria2",
    "docker",
    "linux",
    "windows",
    "macos",
    "bittorrent",
    "torznab",
    "newznab",
    "http",
    "https",
    "tcp",
    "udp",
    "utp",
    "dht",
    "pex",
    "lpd",
    "bep",
    "upnp",
    "nat-pmp",
    "socks5",
    "socks4",
    "ipv4",
    "ipv6",
    "tls",
    "ssl",
    "rc4",
    "ja3",
    "jwt",
    "api",
    "url",
    "uri",
    "ip",
    "json",
    "xml",
    "ebml",
    "mkv",
    "mp4",
    "avi",
    "flac",
    "mp3",
    "sqlite",
    "postgresql",
    "dapper",
    "signalr",
    "kestrel",
    "nlog",
    "dryioc",
    "uuid",
    "hash",
    "infohash",
    "bep27",
    "bep29",
    "bep09",
    "bep10",
    "bep11",
    "bep15",
    "pwd:",
    "mb/s",
    "kb/s",
    "gb/s",
    "tb/s",
    "b/s",
    "kib/s",
    "mib/s",
    "gib/s",
    "tib/s",
}


def is_technical(text):
    t = str(text).strip().lower()
    if not t:
        return True
    if t in BRAND_TERMS:
        return True
    if re.match(r"^[\d\s.,:;/%()+\-_#*?!=<>[\]{}|@&~^\"\'`$]+$", t):
        return True
    return False


def hash_text(text):
    return hashlib.sha256(str(text).strip().encode("utf-8")).hexdigest()[:16]


def parse_ts_dict(file_path):
    p = Path(file_path)
    if not p.is_file():
        return {}
    content = p.read_text(encoding="utf-8")
    m = re.search(r"=\s*(\{[\s\S]*\});\s*export default", content)
    if not m:
        return {}
    raw_js = m.group(1)
    tmp_file = p.with_name(p.name + ".tmp.cjs")
    try:
        tmp_file.write_text(
            f"const obj = ({raw_js}); process.stdout.write(JSON.stringify(obj));",
            encoding="utf-8",
        )
        import subprocess

        res = subprocess.run(
            ["node", str(tmp_file)],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        if res.returncode == 0 and res.stdout:
            return json.loads(res.stdout)
    finally:
        if tmp_file.exists():
            tmp_file.unlink()
    return {}


def flatten_keys(obj, prefix=""):
    res = {}
    for k, v in obj.items():
        full = f"{prefix}.{k}" if prefix else k
        if isinstance(v, dict):
            res.update(flatten_keys(v, full))
        else:
            res[full] = str(v)
    return res


def unflatten_keys(flat):
    res = {}
    for key, val in flat.items():
        parts = key.split(".")
        cur = res
        for p in parts[:-1]:
            if p not in cur or not isinstance(cur[p], dict):
                cur[p] = {}
            cur = cur[p]
        cur[parts[-1]] = val
    return res


def mask_variables(text):
    var_map = []

    def repl(m):
        idx = len(var_map)
        var_map.append(m.group(0))
        return f"___V{idx}___"

    masked = re.sub(r"(\{\{[a-zA-Z0-9_]+\}\}|\{[a-zA-Z0-9_]+\})", repl, text)
    return masked, var_map


def unmask_variables(text, var_map):
    res = text
    for idx, orig in enumerate(var_map):
        pattern = re.compile(rf"___\s*V{idx}\s*___", re.IGNORECASE)
        res = pattern.sub(orig, res)
    return res


def translate_phrase(text, target_lang):
    if is_technical(text):
        return text

    masked, var_map = mask_variables(text)

    for attempt in range(4):
        try:
            url = f"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=en&tl={urllib.parse.quote(target_lang)}&q={urllib.parse.quote(masked)}"
            req = urllib.request.Request(
                url,
                headers={
                    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
                },
            )
            with urllib.request.urlopen(req, timeout=8) as response:
                raw = response.read().decode("utf-8")
                parsed = json.loads(raw)
                res = parsed[0] if isinstance(parsed, list) else parsed
                if res and str(res).strip():
                    return unmask_variables(str(res).strip(), var_map)
        except Exception:
            time.sleep(0.2 * (attempt + 1))

    return unmask_variables(masked, var_map)


def format_ts_file(obj, var_name):
    json_str = json.dumps(obj, indent=2, ensure_ascii=False)
    lines = [
        'import { I18nTranslations } from "../types";',
        "",
        f"const {var_name}: I18nTranslations = {json_str};",
        "",
        f"export default {var_name};",
        "",
    ]
    return "\n".join(lines)


def generate_types_file(tree):
    def build_interface(obj, indent="  "):
        lines = ["{\n"]
        for k, v in obj.items():
            prop_key = json.dumps(k)
            if isinstance(v, dict):
                sub = build_interface(v, indent + "  ")
                lines.append(f"{indent}{prop_key}: {sub};\n")
            else:
                lines.append(f"{indent}{prop_key}: string;\n")
        lines.append(f"{indent[:-2]}}}")
        return "".join(lines)

    body = build_interface(tree)
    content = f"""// Auto-generated by master_sync_i18n.py. Do not edit manually.

export type I18nTranslations = {body};
"""
    TYPES_FILE.write_text(content, encoding="utf-8")


def main():
    print(
        "🚀 Starting Leecharr Deep Localization Synchronization (Google Dict Engine)..."
    )

    # 1. Scan codebase for referenced keys
    used_keys = set()
    for p in SRC_DIR.rglob("*"):
        if not p.is_file():
            continue
        rel_parts = p.relative_to(SRC_DIR).parts
        if "node_modules" in rel_parts or "dist" in rel_parts or "locales" in rel_parts:
            continue
        if p.suffix == ".tsx" or (
            p.suffix == ".ts"
            and not p.name.endswith("types.ts")
            and not p.name.endswith(".d.ts")
        ):
            code = p.read_text(encoding="utf-8", errors="ignore")
            matches = re.findall(
                r'\b(?:t|translate)\(\s*["\'`]([a-zA-Z0-9_.]+)["\'`]', code
            )
            for m in matches:
                used_keys.add(m)
    print(f"ℹ️  Found {len(used_keys)} distinct translation key references in codebase.")

    # 2. Load en.ts
    en_file = LOCALES_DIR / "en.ts"
    en_tree = parse_ts_dict(en_file)
    if not en_tree:
        print("❌ Failed to parse en.ts")
        sys.exit(1)

    flat_en = flatten_keys(en_tree)
    clean_flat_en = dict(flat_en)

    total_keys = len(clean_flat_en)
    print(f"📖 Canonical English dictionary: {total_keys} keys")

    clean_en_tree = unflatten_keys(clean_flat_en)
    en_file.write_text(format_ts_file(clean_en_tree, "en"), encoding="utf-8")

    generate_types_file(clean_en_tree)
    print("✅ types.ts regenerated.")

    # 3. Load / Reset Translation Memory Cache
    memory = {}
    if CACHE_FILE.is_file():
        try:
            memory = json.loads(CACHE_FILE.read_text(encoding="utf-8"))
        except Exception:
            memory = {}

    for lang in LANGS:
        if lang not in memory:
            memory[lang] = {}

    # 4. Process each language
    for lang in LANGS:
        var_name = "zhCN" if lang == "zh-CN" else lang
        lang_file = LOCALES_DIR / f"{lang}.ts"
        existing_tree = parse_ts_dict(lang_file)
        existing_flat = flatten_keys(existing_tree)

        phrases_to_translate = {}
        result_flat = {}

        for key, en_val in clean_flat_en.items():
            en_hash = hash_text(en_val)
            cached_trans = memory[lang].get(en_hash)
            cur_val = existing_flat.get(key)

            if cached_trans and (
                is_technical(en_val)
                or cached_trans.strip() != en_val.strip()
                or len(en_val.strip()) <= 3
            ):
                result_flat[key] = cached_trans
            elif cur_val and (
                is_technical(en_val)
                or cur_val.strip() != en_val.strip()
                or len(en_val.strip()) <= 3
            ):
                result_flat[key] = cur_val
                memory[lang][en_hash] = cur_val
            elif is_technical(en_val):
                result_flat[key] = en_val
                memory[lang][en_hash] = en_val
            else:
                phrases_to_translate[en_hash] = en_val

        missing_count = len(phrases_to_translate)
        if missing_count > 0:
            print(f"🌍 [{lang}] Translating {missing_count} authentic phrases...")

            def task(h, text):
                return h, text, translate_phrase(text, lang)

            with ThreadPoolExecutor(max_workers=32) as executor:
                futures = [
                    executor.submit(task, h, t) for h, t in phrases_to_translate.items()
                ]
                completed = 0
                for fut in as_completed(futures):
                    h, orig_text, trans_text = fut.result()
                    memory[lang][h] = trans_text
                    completed += 1
                    if completed % 500 == 0 or completed == missing_count:
                        print(
                            f"   [{lang}] Progress: {completed}/{missing_count} phrases translated"
                        )

            for key, en_val in clean_flat_en.items():
                en_hash = hash_text(en_val)
                result_flat[key] = memory[lang].get(en_hash, en_val)
        else:
            print(
                f"✅ [{lang}] 100% up-to-date and fully translated ({total_keys} keys)"
            )

        # Unflatten and save .ts file
        new_tree = unflatten_keys(result_flat)
        ts_content = format_ts_file(new_tree, var_name)
        lang_file.write_text(ts_content, encoding="utf-8")

    # 5. Save updated translation memory cache
    CACHE_FILE.write_text(
        json.dumps(memory, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    print(
        "\n🎉 Full Deep Translation Synchronization Complete Across All 20 Languages!"
    )


if __name__ == "__main__":
    main()
