/**
 * Patch @cubone/react-file-manager to expand supported file extension icon mappings.
 * Automatically run on postinstall (and in CI/Docker builds).
 */
const fs = require('fs');
const path = require('path');

const targetFile = path.resolve(__dirname, '../node_modules/@cubone/react-file-manager/dist/react-file-manager.es.js');

if (!fs.existsSync(targetFile)) {
  console.log('[patch-file-manager] @cubone/react-file-manager not installed, skipping patch.');
  process.exit(0);
}

let content = fs.readFileSync(targetFile, 'utf8');

// Check if already patched
if (content.includes('mkv: /* @__PURE__ */ c(Nt,')) {
  console.log('[patch-file-manager] @cubone/react-file-manager already patched.');
  process.exit(0);
}

// Locate the icon map _e = (n) => ({ ... })
const needle = 'mp4: /* @__PURE__ */ c(Nt, { size: n }),';
if (!content.includes(needle)) {
  console.warn('[patch-file-manager] Could not find needle in react-file-manager.es.js');
  process.exit(0);
}

const additions = [
  // Video extensions (Nt)
  'mkv: /* @__PURE__ */ c(Nt, { size: n }),',
  'avi: /* @__PURE__ */ c(Nt, { size: n }),',
  'mov: /* @__PURE__ */ c(Nt, { size: n }),',
  'm4v: /* @__PURE__ */ c(Nt, { size: n }),',
  'flv: /* @__PURE__ */ c(Nt, { size: n }),',
  'wmv: /* @__PURE__ */ c(Nt, { size: n }),',
  'ts: /* @__PURE__ */ c(Nt, { size: n }),',
  'm2ts: /* @__PURE__ */ c(Nt, { size: n }),',
  'mpg: /* @__PURE__ */ c(Nt, { size: n }),',
  'mpeg: /* @__PURE__ */ c(Nt, { size: n }),',
  'vob: /* @__PURE__ */ c(Nt, { size: n }),',
  'ogv: /* @__PURE__ */ c(Nt, { size: n }),',
  '"3gp": /* @__PURE__ */ c(Nt, { size: n }),',
  'divx: /* @__PURE__ */ c(Nt, { size: n }),',
  'rmvb: /* @__PURE__ */ c(Nt, { size: n }),',
  'asf: /* @__PURE__ */ c(Nt, { size: n }),',

  // Audio extensions (Ct)
  'flac: /* @__PURE__ */ c(Ct, { size: n }),',
  'wav: /* @__PURE__ */ c(Ct, { size: n }),',
  'aac: /* @__PURE__ */ c(Ct, { size: n }),',
  'ogg: /* @__PURE__ */ c(Ct, { size: n }),',
  'opus: /* @__PURE__ */ c(Ct, { size: n }),',
  'wma: /* @__PURE__ */ c(Ct, { size: n }),',
  'alac: /* @__PURE__ */ c(Ct, { size: n }),',
  'ape: /* @__PURE__ */ c(Ct, { size: n }),',
  'mka: /* @__PURE__ */ c(Ct, { size: n }),',
  'mid: /* @__PURE__ */ c(Ct, { size: n }),',
  'midi: /* @__PURE__ */ c(Ct, { size: n }),',
  'ac3: /* @__PURE__ */ c(Ct, { size: n }),',
  'dts: /* @__PURE__ */ c(Ct, { size: n }),',
  'eac3: /* @__PURE__ */ c(Ct, { size: n }),',
  'aiff: /* @__PURE__ */ c(Ct, { size: n }),',

  // Archive extensions (uo)
  'rar: /* @__PURE__ */ c(uo, { size: n }),',
  '"7z": /* @__PURE__ */ c(uo, { size: n }),',
  'tar: /* @__PURE__ */ c(uo, { size: n }),',
  'gz: /* @__PURE__ */ c(uo, { size: n }),',
  'bz2: /* @__PURE__ */ c(uo, { size: n }),',
  'xz: /* @__PURE__ */ c(uo, { size: n }),',
  'zst: /* @__PURE__ */ c(uo, { size: n }),',
  'tgz: /* @__PURE__ */ c(uo, { size: n }),',
  'tbz2: /* @__PURE__ */ c(uo, { size: n }),',
  'cab: /* @__PURE__ */ c(uo, { size: n }),',
  'iso: /* @__PURE__ */ c(uo, { size: n }),',
  'img: /* @__PURE__ */ c(uo, { size: n }),',
  'dmg: /* @__PURE__ */ c(uo, { size: n }),',

  // Text, NFO, Subtitles & Metadata (lo)
  'nfo: /* @__PURE__ */ c(lo, { size: n }),',
  'diz: /* @__PURE__ */ c(lo, { size: n }),',
  'srt: /* @__PURE__ */ c(lo, { size: n }),',
  'vtt: /* @__PURE__ */ c(lo, { size: n }),',
  'ass: /* @__PURE__ */ c(lo, { size: n }),',
  'ssa: /* @__PURE__ */ c(lo, { size: n }),',
  'sub: /* @__PURE__ */ c(lo, { size: n }),',
  'idx: /* @__PURE__ */ c(lo, { size: n }),',
  'log: /* @__PURE__ */ c(lo, { size: n }),',
  'cue: /* @__PURE__ */ c(lo, { size: n }),',
  'sfv: /* @__PURE__ */ c(lo, { size: n }),',
  'md5: /* @__PURE__ */ c(lo, { size: n }),',
  'sha256: /* @__PURE__ */ c(lo, { size: n }),',

  // Torrents, Config & Shell (re)
  'torrent: /* @__PURE__ */ c(re, { size: n }),',
  'yaml: /* @__PURE__ */ c(re, { size: n }),',
  'yml: /* @__PURE__ */ c(re, { size: n }),',
  'toml: /* @__PURE__ */ c(re, { size: n }),',
  'ini: /* @__PURE__ */ c(re, { size: n }),',
  'conf: /* @__PURE__ */ c(re, { size: n }),',
  'config: /* @__PURE__ */ c(re, { size: n }),',
  'env: /* @__PURE__ */ c(re, { size: n }),',
  'sh: /* @__PURE__ */ c(re, { size: n }),',
  'bash: /* @__PURE__ */ c(re, { size: n }),',
  'zsh: /* @__PURE__ */ c(re, { size: n }),',
  'bat: /* @__PURE__ */ c(re, { size: n }),',
  'cmd: /* @__PURE__ */ c(re, { size: n }),',
  'ps1: /* @__PURE__ */ c(re, { size: n }),',

  // Documents & Ebooks (Et, St)
  'epub: /* @__PURE__ */ c(Et, { size: n }),',
  'mobi: /* @__PURE__ */ c(Et, { size: n }),',
  'azw: /* @__PURE__ */ c(Et, { size: n }),',
  'azw3: /* @__PURE__ */ c(Et, { size: n }),',
  'cbz: /* @__PURE__ */ c(Et, { size: n }),',
  'cbr: /* @__PURE__ */ c(Et, { size: n }),',
  'rtf: /* @__PURE__ */ c(Et, { size: n }),',
  'odt: /* @__PURE__ */ c(Et, { size: n }),',
  'tsv: /* @__PURE__ */ c(St, { size: n }),',
  'ods: /* @__PURE__ */ c(St, { size: n }),',

  // Executables & Installers (ro)
  'msi: /* @__PURE__ */ c(ro, { size: n }),',
  'bin: /* @__PURE__ */ c(ro, { size: n }),',
  'apk: /* @__PURE__ */ c(ro, { size: n }),',
  'deb: /* @__PURE__ */ c(ro, { size: n }),',
  'rpm: /* @__PURE__ */ c(ro, { size: n }),',
  'run: /* @__PURE__ */ c(ro, { size: n }),',
  'app: /* @__PURE__ */ c(ro, { size: n }),',
  'pkg: /* @__PURE__ */ c(ro, { size: n }),',

  // Additional Image extensions (et)
  'webp: /* @__PURE__ */ c(et, { size: n }),',
  'gif: /* @__PURE__ */ c(et, { size: n }),',
  'bmp: /* @__PURE__ */ c(et, { size: n }),',
  'ico: /* @__PURE__ */ c(et, { size: n }),',
  'tiff: /* @__PURE__ */ c(et, { size: n }),',
  'tif: /* @__PURE__ */ c(et, { size: n }),',
  'heic: /* @__PURE__ */ c(et, { size: n }),',
  'heif: /* @__PURE__ */ c(et, { size: n }),',
  'avif: /* @__PURE__ */ c(et, { size: n }),',
].join('\n  ');

content = content.replace(needle, `${needle}\n  ${additions}`);
fs.writeFileSync(targetFile, content, 'utf8');
console.log('[patch-file-manager] Successfully patched @cubone/react-file-manager with expanded file icons.');
