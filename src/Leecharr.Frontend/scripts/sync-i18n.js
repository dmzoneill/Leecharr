const fs = require("fs");
const path = require("path");
const https = require("https");
const crypto = require("crypto");
const { execSync } = require("child_process");

const FRONTEND_DIR = path.resolve(__dirname, "..");
const PYTHON_SCRIPT = path.join(__dirname, "master_sync_i18n.py");

try {
  console.log("🚀 Running master i18n synchronization...");
  execSync(`python3 "${PYTHON_SCRIPT}"`, {
    stdio: "inherit",
    cwd: FRONTEND_DIR,
  });
} catch (err) {
  console.error("❌ Sync failed:", err);
  process.exit(1);
}
