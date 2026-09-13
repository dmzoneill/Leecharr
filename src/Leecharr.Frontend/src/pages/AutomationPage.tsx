import React, { useState, useMemo, useEffect } from "react";
import {
  useAutomationScripts,
  useCreateAutomationScript,
  useUpdateAutomationScript,
  useDeleteAutomationScript,
  useRunAutomationScript,
  useTestAutomationScript,
  useAutomationMarketplace,
  useInstallMarketplaceTemplate,
  useTorrents,
  useCategories,
  useTags,
} from "../api/hooks";
import type {
  AutomationScript,
  AutomationTrigger,
  AutomationLanguage,
  AutomationMarketplaceTemplate,
  AutomationExecutionResult,
} from "../api/types";
import { formatBytes } from "../utils/formatters";

// Visual Pipeline Interfaces
export type VisualActionType =
  | "addTag"
  | "removeTag"
  | "setCategory"
  | "pause"
  | "resume"
  | "remove"
  | "recheck"
  | "reannounce"
  | "setUploadLimit"
  | "setDownloadLimit"
  | "setRatioLimit"
  | "setSeedingTimeLimit"
  | "setPriority"
  | "setSequentialDownload"
  | "setSuperSeeding"
  | "moveFiles"
  | "addTracker"
  | "removeTracker"
  | "boostTracker"
  | "banPeer"
  | "sendNotification"
  | "notifyArr"
  | "syncArr"
  | "runScript"
  | "delay"
  | "log"
  | "setVariable"
  | "stopPipeline"
  | "extractArchive"
  | "cleanFiles"
  | "command"
  | "http";

export interface VisualAction {
  id: string;
  type: VisualActionType;
  value: string;
  extra?: Record<string, any>;
}

export interface VisualStep {
  id: string;
  name: string;
  conditionEnabled: boolean;
  conditionLeft: string;
  conditionOp: ">" | "<" | ">=" | "<=" | "==" | "!=";
  conditionRight: string;
  hasHttp: boolean;
  http: {
    method: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
    url: string;
    headers?: Record<string, string>;
    json?: boolean;
    body?: string;
    register?: string;
  };
  actions: VisualAction[];
}

export interface ActionDef {
  type: VisualActionType;
  label: string;
  placeholder?: string;
  extraHelp?: string;
}

export interface ActionGroup {
  group: string;
  items: ActionDef[];
}

export const COMMON_COMMANDS = [
  { name: "Backup", desc: "Create full database & config backup" },
  { name: "SyncArr", desc: "Sync connected Sonarr / Radarr instances" },
  { name: "WatchFolderScan", desc: "Scan watch folder for torrents" },
  { name: "TrackerBoostScan", desc: "Scan & optimize candidate trackers" },
  { name: "BlocklistUpdate", desc: "Update peer IP blocklist" },
  { name: "GeoIpUpdate", desc: "Update MaxMind GeoIP database" },
  { name: "RssSync", desc: "Poll RSS indexers for releases" },
];

export const ACTION_GROUPS: ActionGroup[] = [
  {
    group: "🏷️ Tags & Categories",
    items: [
      { type: "addTag" as VisualActionType, label: "➕ Add Tag", placeholder: "e.g. 4K-HDR, Verified, Freeleech" },
      { type: "removeTag" as VisualActionType, label: "➖ Remove Tag", placeholder: "e.g. Incomplete, Queued" },
      { type: "setCategory" as VisualActionType, label: "📁 Set Category", placeholder: "e.g. Movies, TV, Anime" },
    ],
  },
  {
    group: "⚡ Torrent State & Flow",
    items: [
      { type: "pause" as VisualActionType, label: "⏸️ Pause Torrent" },
      { type: "resume" as VisualActionType, label: "▶️ Resume Torrent" },
      { type: "remove" as VisualActionType, label: "🗑️ Remove Torrent", extraHelp: "Deletes torrent from client (optional data deletion)" },
      { type: "recheck" as VisualActionType, label: "🔍 Force Hash Recheck", extraHelp: "Verifies piece hashes on disk" },
      { type: "reannounce" as VisualActionType, label: "📢 Force Reannounce", extraHelp: "Forces immediate tracker update" },
    ],
  },
  {
    group: "🎛️ Limits & Priority",
    items: [
      { type: "setUploadLimit" as VisualActionType, label: "⬆️ Set Upload Limit (KB/s)", placeholder: "e.g. 1024 (0 for unlimited)" },
      { type: "setDownloadLimit" as VisualActionType, label: "⬇️ Set Download Limit (KB/s)", placeholder: "e.g. 5120 (0 for unlimited)" },
      { type: "setRatioLimit" as VisualActionType, label: "🎯 Set Stop Ratio Limit", placeholder: "e.g. 2.0" },
      { type: "setSeedingTimeLimit" as VisualActionType, label: "⏱️ Set Seeding Time Limit (minutes)", placeholder: "e.g. 2880 (48 hours)" },
      { type: "setPriority" as VisualActionType, label: "⚡ Set Torrent Priority", placeholder: "High, Normal, Low, DoNotDownload" },
      { type: "setSequentialDownload" as VisualActionType, label: "⏩ Sequential Download Toggle", placeholder: "true or false" },
      { type: "setSuperSeeding" as VisualActionType, label: "🌱 Initial / Super Seeding", placeholder: "true or false" },
    ],
  },
  {
    group: "📦 Storage & Files",
    items: [
      { type: "moveFiles" as VisualActionType, label: "📂 Move Torrent Files / Change Save Path", placeholder: "e.g. /media/completed/${category}" },
      { type: "extractArchive" as VisualActionType, label: "📦 Extract Archive (.rar/.zip/.7z)", placeholder: "/extracted/path (blank = current)" },
      { type: "cleanFiles" as VisualActionType, label: "🧹 Clean Unwanted Files", placeholder: "*.nfo, *.txt, *.sample" },
    ],
  },
  {
    group: "📡 Trackers & Peers",
    items: [
      { type: "addTracker" as VisualActionType, label: "➕ Add Announce URL", placeholder: "https://tracker.example.com/announce" },
      { type: "removeTracker" as VisualActionType, label: "➖ Remove Announce URL", placeholder: "https://tracker.example.com/announce" },
      { type: "boostTracker" as VisualActionType, label: "🚀 Boost Tracker Scrape", placeholder: "Tracker URL to prioritize" },
      { type: "banPeer" as VisualActionType, label: "🚫 Ban Peer IP / Subnet", placeholder: "192.168.1.100 or 10.0.0.0/24" },
    ],
  },
  {
    group: "🔔 Alerts & Servarr",
    items: [
      { type: "sendNotification" as VisualActionType, label: "🔔 Send System / Push Notification", placeholder: "Torrent ${torrent.name} completed!" },
      { type: "notifyArr" as VisualActionType, label: "🤖 Notify Servarr App (Sonarr/Radarr)", placeholder: "sonarr or radarr" },
      { type: "syncArr" as VisualActionType, label: "🔄 Trigger Servarr Rescan", placeholder: "Instance name or all" },
    ],
  },
  {
    group: "💻 Scripting & Flow Control",
    items: [
      { type: "runScript" as VisualActionType, label: "💻 Run Custom Host Script", placeholder: "/scripts/on_download.sh" },
      { type: "delay" as VisualActionType, label: "⏱️ Delay / Sleep (seconds)", placeholder: "e.g. 5" },
      { type: "log" as VisualActionType, label: "📝 Pipeline Log Message", placeholder: "Log message to output stream" },
      { type: "setVariable" as VisualActionType, label: "💾 Set Pipeline Variable", placeholder: "key=value" },
      { type: "stopPipeline" as VisualActionType, label: "🛑 Stop Pipeline Early", placeholder: "Reason for halting" },
      { type: "command" as VisualActionType, label: "⚙️ Run Internal Command", placeholder: "Backup, SyncArr, RssSync, etc." },
      { type: "http" as VisualActionType, label: "🌐 Send Custom HTTP Request", placeholder: "https://api.example.com/webhook" },
    ],
  },
];

export interface PropertyDef {
  value: string;
  label: string;
  group: string;
  type: "boolean" | "enum" | "category" | "number" | "string" | "custom";
  options?: { value: string; label: string }[];
  defaultOp: ">" | "<" | ">=" | "<=" | "==" | "!=";
  defaultValue: string;
  placeholder?: string;
  unit?: "bytes" | "speed" | "ratio" | "percent" | "count";
  presets?: { label: string; value: string }[];
}

export const CONDITION_PROPERTIES: PropertyDef[] = [
  // Booleans & Flags
  {
    value: "${torrent.isPrivate}",
    label: "🔒 Is Private Tracker",
    group: "Booleans & Flags",
    type: "boolean",
    options: [
      { value: "true", label: "🔒 True (Private Tracker)" },
      { value: "false", label: "🌐 False (Public Tracker)" },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },
  {
    value: "${torrent.isComplete}",
    label: "✅ Is Download Completed",
    group: "Booleans & Flags",
    type: "boolean",
    options: [
      { value: "true", label: "✅ True (Completed / 100%)" },
      { value: "false", label: "⏳ False (Incomplete / Downloading)" },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },
  {
    value: "${system.vpnActive}",
    label: "🛡️ VPN Active / Protected",
    group: "Booleans & Flags",
    type: "boolean",
    options: [
      { value: "true", label: "🛡️ True (VPN Protected)" },
      { value: "false", label: "⚠️ False (VPN Down / Inactive)" },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },
  {
    value: "${system.isPortForwarded}",
    label: "🌐 Port Forwarded / Open",
    group: "Booleans & Flags",
    type: "boolean",
    options: [
      { value: "true", label: "🌐 True (Port Open / Forwarded)" },
      { value: "false", label: "🔒 False (Port Closed)" },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },

  // Status & Categories
  {
    value: "${torrent.status}",
    label: "🔄 Torrent State / Status",
    group: "Status & Categories",
    type: "enum",
    options: [
      { value: "'Downloading'", label: "⬇️ Downloading" },
      { value: "'Seeding'", label: "🌱 Seeding" },
      { value: "'Paused'", label: "⏸️ Paused" },
      { value: "'Stopped'", label: "⏹️ Stopped" },
      { value: "'Queued'", label: "⏳ Queued" },
      { value: "'Checking'", label: "🔍 Checking" },
      { value: "'Error'", label: "⚠️ Error" },
    ],
    defaultOp: "==",
    defaultValue: "'Downloading'",
  },
  {
    value: "${torrent.category}",
    label: "📁 Category Name",
    group: "Status & Categories",
    type: "category",
    defaultOp: "==",
    defaultValue: "",
    placeholder: "e.g. 'Movies' or 'TV'",
  },

  // Numbers & Metrics
  {
    value: "${torrent.size}",
    label: "💾 Torrent Size (bytes)",
    group: "Numbers & Metrics",
    type: "number",
    defaultOp: ">=",
    defaultValue: "1073741824",
    placeholder: "e.g. 1073741824 (1 GB)",
    unit: "bytes",
    presets: [
      { label: "500 MB", value: "524288000" },
      { label: "1 GB", value: "1073741824" },
      { label: "5 GB", value: "5368709120" },
      { label: "10 GB", value: "10737418240" },
      { label: "50 GB", value: "53687091200" },
      { label: "100 GB", value: "107374182400" },
    ],
  },
  {
    value: "${torrent.ratio}",
    label: "🎯 Share Ratio",
    group: "Numbers & Metrics",
    type: "number",
    defaultOp: ">=",
    defaultValue: "1.0",
    placeholder: "e.g. 1.0 or 2.5",
    unit: "ratio",
    presets: [
      { label: "0.5x", value: "0.5" },
      { label: "1.0x", value: "1.0" },
      { label: "1.5x", value: "1.5" },
      { label: "2.0x", value: "2.0" },
      { label: "3.0x", value: "3.0" },
      { label: "5.0x", value: "5.0" },
    ],
  },
  {
    value: "${torrent.progress}",
    label: "📈 Progress (%)",
    group: "Numbers & Metrics",
    type: "number",
    defaultOp: ">=",
    defaultValue: "100",
    placeholder: "e.g. 100 or 50",
    unit: "percent",
    presets: [
      { label: "25%", value: "25" },
      { label: "50%", value: "50" },
      { label: "75%", value: "75" },
      { label: "100%", value: "100" },
    ],
  },
  {
    value: "${torrent.downloadSpeed}",
    label: "⬇️ Download Speed (B/s)",
    group: "Numbers & Metrics",
    type: "number",
    defaultOp: ">",
    defaultValue: "1048576",
    placeholder: "e.g. 1048576 (1 MB/s)",
    unit: "speed",
    presets: [
      { label: "512 KB/s", value: "524288" },
      { label: "1 MB/s", value: "1048576" },
      { label: "5 MB/s", value: "5242880" },
      { label: "10 MB/s", value: "10485760" },
      { label: "50 MB/s", value: "52428800" },
    ],
  },
  {
    value: "${torrent.uploadSpeed}",
    label: "⬆️ Upload Speed (B/s)",
    group: "Numbers & Metrics",
    type: "number",
    defaultOp: ">",
    defaultValue: "524288",
    placeholder: "e.g. 524288 (512 KB/s)",
    unit: "speed",
    presets: [
      { label: "256 KB/s", value: "262144" },
      { label: "512 KB/s", value: "524288" },
      { label: "1 MB/s", value: "1048576" },
      { label: "5 MB/s", value: "5242880" },
      { label: "10 MB/s", value: "10485760" },
    ],
  },
  {
    value: "${torrent.seeders}",
    label: "🌱 Seeders Count",
    group: "Numbers & Metrics",
    type: "number",
    defaultOp: "<",
    defaultValue: "3",
    placeholder: "e.g. 3",
    unit: "count",
    presets: [
      { label: "0", value: "0" },
      { label: "1", value: "1" },
      { label: "3", value: "3" },
      { label: "5", value: "5" },
      { label: "10", value: "10" },
    ],
  },
  {
    value: "${torrent.leechers}",
    label: "👥 Leechers Count",
    group: "Numbers & Metrics",
    type: "number",
    defaultOp: ">",
    defaultValue: "5",
    placeholder: "e.g. 5",
    unit: "count",
    presets: [
      { label: "0", value: "0" },
      { label: "1", value: "1" },
      { label: "5", value: "5" },
      { label: "10", value: "10" },
      { label: "20", value: "20" },
    ],
  },

  // Text & Details
  {
    value: "${torrent.name}",
    label: "📝 Torrent Name / Title",
    group: "Text & Details",
    type: "string",
    defaultOp: "==",
    defaultValue: "''",
    placeholder: "e.g. '2160p' or 'REPACK'",
  },
  {
    value: "${torrent.tracker}",
    label: "📡 Tracker URL / Domain",
    group: "Text & Details",
    type: "string",
    defaultOp: "==",
    defaultValue: "''",
    placeholder: "e.g. 'tracker.example.com'",
  },
  {
    value: "${torrent.savePath}",
    label: "📂 Save Path Directory",
    group: "Text & Details",
    type: "string",
    defaultOp: "==",
    defaultValue: "''",
    placeholder: "e.g. '/downloads/complete'",
  },

  // Custom
  {
    value: "custom",
    label: "✏️ Custom Variable / Expression...",
    group: "Custom / Dynamic",
    type: "custom",
    defaultOp: "==",
    defaultValue: "",
    placeholder: "e.g. ${inputs.minRatio} or ${system.freeDiskBytes}",
  },
];

const TRIGGER_LABELS: Record<string, string> = {
  // Torrent Lifecycle & Goals
  TorrentAdded: "📥 On Torrent Added",
  TorrentCompleted: "✅ On Download Completed",
  RatioReached: "🎯 On Ratio / Seed Goal Reached",
  TorrentStarted: "▶️ On Torrent Resumed / Started",
  TorrentPaused: "⏸️ On Torrent Paused / Stopped",
  TorrentStalled: "⏳ On Torrent Stalled",
  SeedingTimeReached: "⌛ On Seeding Time Target Met",
  HashCheckCompleted: "🔍 On Hash Check Completed",
  ProgressMilestone: "📈 On Progress Milestone",
  TorrentStatusChanged: "🔄 On Torrent State Changed",
  TorrentDeleted: "🗑️ On Torrent Deleted",
  TorrentError: "⚠️ On Torrent Error",

  // Bandwidth & Speed
  SpeedThresholdExceeded: "🚀 On High Speed Threshold Exceeded",
  SpeedThresholdDropped: "📉 On Speed Drop Alert",
  BandwidthQuotaApproaching: "📊 On Bandwidth Quota Threshold",

  // Network & Security
  VpnDisconnected: "🛡️ On VPN KillSwitch",
  VpnRestored: "🌐 On VPN Restored",
  PortForwardingFailed: "🚫 On Port Forwarding / UPnP Failure",
  PeerBanned: "🛡️ On Malicious / Bad Peer Banned",
  TrackerUnreachable: "📡 On All Trackers Failed",
  TrackerBoostApplied: "⚡ On Tracker Boost Applied",

  // Storage & Disk
  DiskSpaceLow: "⚠️ On Low Disk Space Warning",
  DiskSpaceCritical: "🚨 On Critical Disk Space Emergency",
  FileMoveFailed: "❌ On File Move / Path Error",

  // Media & Processing
  MediaEnriched: "🎬 On Media Enriched",
  MediaInspectionFailed: "🎞️ On Media Corruption / Inspection Failed",
  ArchiveExtracted: "📦 On Archive Extracted",
  ExtractionFailed: "❌ On Extraction Failed",
  ArrImportCompleted: "📬 On Servarr Import Completed",

  // System & Lifecycle
  HealthRestored: "💚 On Health Restored",
  CategoryChanged: "📂 On Category Changed",
  ApplicationStarted: "🚀 On App Started",
  ApplicationUpdated: "🔄 On App Updated",
  BackupCompleted: "💾 On Backup Succeeded",
  BackupFailed: "❗ On Backup Failed",
  TaskFailed: "❌ On Scheduled Task Failed",
  Scheduled: "⏱️ Scheduled Interval",
  Manual: "🖐️ Manual Only",
};

// Convert Visual Steps to YAML DSL string
function visualStepsToYaml(pipelineName: string, trigger: string, steps: VisualStep[]): string {
  let yaml = `name: '${pipelineName.replace(/'/g, "''")}'\n`;
  yaml += `trigger: '${trigger}'\n`;
  yaml += `steps:\n`;

  if (steps.length === 0) {
    yaml += `  - name: 'Empty Step'\n    actions: []\n`;
    return yaml;
  }

  for (const step of steps) {
    yaml += `  - name: '${(step.name || "Step").replace(/'/g, "''")}'\n`;
    if (step.conditionEnabled && step.conditionLeft && step.conditionRight) {
      yaml += `    condition: '${step.conditionLeft} ${step.conditionOp} ${step.conditionRight}'\n`;
    }

    if (step.hasHttp && step.http.url.trim()) {
      yaml += `    http:\n`;
      yaml += `      method: '${step.http.method}'\n`;
      yaml += `      url: '${step.http.url.replace(/'/g, "''")}'\n`;
      if (step.http.json) {
        yaml += `      json: true\n`;
      }
      if (step.http.body.trim()) {
        try {
          const parsed = JSON.parse(step.http.body);
          yaml += `      body:\n`;
          for (const [k, v] of Object.entries(parsed)) {
            yaml += `        ${k}: '${String(v).replace(/'/g, "''")}'\n`;
          }
        } catch {
          yaml += `      body: '${step.http.body.replace(/'/g, "''")}'\n`;
        }
      }
      if (step.http.register.trim()) {
        yaml += `    register: '${step.http.register.trim()}'\n`;
      }
    }

    if (step.actions.length > 0) {
      yaml += `    actions:\n`;
      for (const act of step.actions) {
        if (act.type === "addTag") {
          yaml += `      - addTag: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "removeTag") {
          yaml += `      - removeTag: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "setCategory") {
          yaml += `      - setCategory: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "setUploadLimit") {
          yaml += `      - setUploadLimit: ${act.value || "0"}\n`;
        } else if (act.type === "setDownloadLimit") {
          yaml += `      - setDownloadLimit: ${act.value || "0"}\n`;
        } else if (act.type === "setRatioLimit") {
          yaml += `      - setRatioLimit: ${act.value || "2.0"}\n`;
        } else if (act.type === "setSeedingTimeLimit") {
          yaml += `      - setSeedingTimeLimit: ${act.value || "2880"}\n`;
        } else if (act.type === "setPriority") {
          yaml += `      - setPriority: '${(act.value || "Normal").replace(/'/g, "''")}'\n`;
        } else if (act.type === "setSequentialDownload") {
          yaml += `      - setSequentialDownload: ${act.value === "false" ? "false" : "true"}\n`;
        } else if (act.type === "setSuperSeeding") {
          yaml += `      - setSuperSeeding: ${act.value === "false" ? "false" : "true"}\n`;
        } else if (act.type === "moveFiles") {
          yaml += `      - moveFiles: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "extractArchive") {
          yaml += act.value ? `      - extractArchive: '${act.value.replace(/'/g, "''")}'\n` : `      - extractArchive: true\n`;
        } else if (act.type === "cleanUnwantedFiles") {
          yaml += `      - cleanUnwantedFiles: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "addTracker") {
          yaml += `      - addTracker: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "removeTracker") {
          yaml += `      - removeTracker: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "boostTracker") {
          yaml += `      - boostTracker: true\n`;
        } else if (act.type === "banPeer") {
          yaml += `      - banPeer: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "sendNotification") {
          yaml += `      - sendNotification: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "notifyArr") {
          yaml += act.value ? `      - notifyArr: '${act.value.replace(/'/g, "''")}'\n` : `      - notifyArr: true\n`;
        } else if (act.type === "runScript") {
          yaml += `      - runScript: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "delay") {
          yaml += `      - delay: ${act.value || "5"}\n`;
        } else if (act.type === "log") {
          yaml += `      - log: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "setVariable") {
          const parts = act.value.split("=");
          yaml += `      - setVariable:\n          key: '${(parts[0] || "myVar").trim()}'\n          value: '${(parts[1] || "").trim().replace(/'/g, "''")}'\n`;
        } else if (act.type === "stopPipeline") {
          yaml += `      - stopPipeline: '${(act.value || "Condition halted pipeline").replace(/'/g, "''")}'\n`;
        } else if (act.type === "command") {
          yaml += `      - command: '${act.value.replace(/'/g, "''")}'\n`;
        } else if (act.type === "pause") {
          yaml += `      - pause: true\n`;
        } else if (act.type === "resume") {
          yaml += `      - resume: true\n`;
        } else if (act.type === "recheck") {
          yaml += `      - recheck: true\n`;
        } else if (act.type === "reannounce") {
          yaml += `      - reannounce: true\n`;
        } else if (act.type === "remove") {
          yaml += `      - remove: true\n`;
          if (act.deleteData) {
            yaml += `        deleteData: true\n`;
          }
        }
      }
    }
  }

  return yaml;
}

// Convert YAML DSL to Visual Steps
function yamlToVisualSteps(code: string): VisualStep[] {
  if (!code || !code.includes("steps:")) {
    return [
      {
        id: "step-1",
        name: "Check and Tag",
        conditionEnabled: true,
        conditionLeft: "${torrent.size}",
        conditionOp: ">",
        conditionRight: "1000000000",
        hasHttp: false,
        http: { method: "POST", url: "", json: true, body: "{}", register: "" },
        actions: [
          { id: "act-1", type: "addTag", value: "Large-Download" },
        ],
      },
    ];
  }

  const steps: VisualStep[] = [];
  const lines = code.split("\n");
  let currentStep: VisualStep | null = null;
  let inActions = false;
  let inHttp = false;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();

    if (trimmed.startsWith("- name:")) {
      if (currentStep) {
        steps.push(currentStep);
      }
      const nameMatch = trimmed.match(/- name:\s*['"]?([^'"]+)['"]?/);
      currentStep = {
        id: `step-${steps.length + 1}-${Date.now()}`,
        name: nameMatch ? nameMatch[1] : "Step",
        conditionEnabled: false,
        conditionLeft: "${torrent.size}",
        conditionOp: ">",
        conditionRight: "1000000000",
        hasHttp: false,
        http: { method: "POST", url: "", json: true, body: "{}", register: "" },
        actions: [],
      };
      inActions = false;
      inHttp = false;
    } else if (currentStep && trimmed.startsWith("condition:")) {
      const condMatch = trimmed.match(/condition:\s*['"]?([^'"]+)['"]?/);
      if (condMatch) {
        currentStep.conditionEnabled = true;
        const expr = condMatch[1].trim();
        const opMatch = expr.match(/(>=|<=|==|!=|>|<)/);
        if (opMatch) {
          const op = opMatch[1] as any;
          const parts = expr.split(op);
          currentStep.conditionLeft = parts[0]?.trim() || "${torrent.size}";
          currentStep.conditionOp = op;
          currentStep.conditionRight = parts[1]?.trim() || "0";
        } else {
          if (expr.startsWith("!")) {
            currentStep.conditionLeft = expr.substring(1).trim();
            currentStep.conditionOp = "==";
            currentStep.conditionRight = "false";
          } else {
            currentStep.conditionLeft = expr;
            currentStep.conditionOp = "==";
            currentStep.conditionRight = "true";
          }
        }
      }
    } else if (currentStep && trimmed.startsWith("http:")) {
      currentStep.hasHttp = true;
      inHttp = true;
      inActions = false;
    } else if (currentStep && inHttp && trimmed.startsWith("method:")) {
      const m = trimmed.match(/method:\s*['"]?([^'"]+)['"]?/);
      if (m && currentStep) currentStep.http.method = m[1] as any;
    } else if (currentStep && inHttp && trimmed.startsWith("url:")) {
      const u = trimmed.match(/url:\s*['"]?([^'"]+)['"]?/);
      if (u && currentStep) currentStep.http.url = u[1];
    } else if (currentStep && inHttp && trimmed.startsWith("register:")) {
      const r = trimmed.match(/register:\s*['"]?([^'"]+)['"]?/);
      if (r && currentStep) currentStep.http.register = r[1];
    } else if (currentStep && trimmed.startsWith("actions:")) {
      inActions = true;
      inHttp = false;
    } else if (currentStep && inActions && trimmed.startsWith("- addTag:")) {
      const v = trimmed.match(/- addTag:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "addTag", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- removeTag:")) {
      const v = trimmed.match(/- removeTag:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "removeTag", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setCategory:")) {
      const v = trimmed.match(/- setCategory:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setCategory", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setUploadLimit:")) {
      const v = trimmed.match(/- setUploadLimit:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setUploadLimit", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setDownloadLimit:")) {
      const v = trimmed.match(/- setDownloadLimit:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setDownloadLimit", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setRatioLimit:")) {
      const v = trimmed.match(/- setRatioLimit:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setRatioLimit", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setSeedingTimeLimit:")) {
      const v = trimmed.match(/- setSeedingTimeLimit:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setSeedingTimeLimit", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setPriority:")) {
      const v = trimmed.match(/- setPriority:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setPriority", value: v[1] });
    } else if (currentStep && inActions && (trimmed.startsWith("- setSequentialDownload:") || trimmed.startsWith("- setSequential:"))) {
      const v = trimmed.match(/- setSequential(?:Download)?:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setSequentialDownload", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- setSuperSeeding:")) {
      const v = trimmed.match(/- setSuperSeeding:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "setSuperSeeding", value: v[1] });
    } else if (currentStep && inActions && (trimmed.startsWith("- moveFiles:") || trimmed.startsWith("- setSavePath:"))) {
      const v = trimmed.match(/- (?:moveFiles|setSavePath):\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "moveFiles", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- extractArchive:")) {
      const v = trimmed.match(/- extractArchive:\s*['"]?([^'"]+)['"]?/);
      const val = v && v[1] !== "true" ? v[1] : "";
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "extractArchive", value: val });
    } else if (currentStep && inActions && trimmed.startsWith("- cleanUnwantedFiles:")) {
      const v = trimmed.match(/- cleanUnwantedFiles:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "cleanUnwantedFiles", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- addTracker:")) {
      const v = trimmed.match(/- addTracker:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "addTracker", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- removeTracker:")) {
      const v = trimmed.match(/- removeTracker:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "removeTracker", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- boostTracker:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "boostTracker", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- banPeer:")) {
      const v = trimmed.match(/- banPeer:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "banPeer", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- sendNotification:")) {
      const v = trimmed.match(/- sendNotification:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "sendNotification", value: v[1] });
    } else if (currentStep && inActions && (trimmed.startsWith("- notifyArr:") || trimmed.startsWith("- syncArr:"))) {
      const v = trimmed.match(/- (?:notifyArr|syncArr):\s*['"]?([^'"]+)['"]?/);
      const val = v && v[1] !== "true" ? v[1] : "";
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "notifyArr", value: val });
    } else if (currentStep && inActions && trimmed.startsWith("- runScript:")) {
      const v = trimmed.match(/- runScript:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "runScript", value: v[1] });
    } else if (currentStep && inActions && (trimmed.startsWith("- delay:") || trimmed.startsWith("- sleep:"))) {
      const v = trimmed.match(/- (?:delay|sleep):\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "delay", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- log:")) {
      const v = trimmed.match(/- log:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "log", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- stopPipeline:")) {
      const v = trimmed.match(/- stopPipeline:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "stopPipeline", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- command:")) {
      const v = trimmed.match(/- command:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "command", value: v[1] });
    } else if (currentStep && inActions && trimmed.startsWith("- pause:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "pause", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- resume:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "resume", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- recheck:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "recheck", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- reannounce:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "reannounce", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- remove:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "remove", value: "", deleteData: false });
    }
  }

  if (currentStep) {
    steps.push(currentStep);
  }

  return steps.length > 0
    ? steps
    : [
        {
          id: "step-default",
          name: "Action Step",
          conditionEnabled: false,
          conditionLeft: "${torrent.size}",
          conditionOp: ">",
          conditionRight: "0",
          hasHttp: false,
          http: { method: "POST", url: "", json: true, body: "{}", register: "" },
          actions: [{ id: "act-1", type: "addTag", value: "Processed" }],
        },
      ];
}

export function AutomationPage() {
  const { data: scripts, isLoading: loadingScripts } = useAutomationScripts();
  const { data: templates, isLoading: loadingTemplates } = useAutomationMarketplace();
  const { data: torrents } = useTorrents();
  const { data: categories } = useCategories();
  const { data: tags } = useTags();

  const createScript = useCreateAutomationScript();
  const updateScript = useUpdateAutomationScript();
  const deleteScript = useDeleteAutomationScript();
  const runScript = useRunAutomationScript();
  const testScript = useTestAutomationScript();
  const installTemplate = useInstallMarketplaceTemplate();

  const [activeTab, setActiveTab] = useState<"scripts" | "marketplace" | "history">("scripts");
  const [selectedCategoryFilter, setSelectedCategoryFilter] = useState<string>("All");
  const [selectedTriggerFilter, setSelectedTriggerFilter] = useState<string>("All");
  const [searchQuery, setSearchQuery] = useState("");

  // Modals state
  const [editorOpen, setEditorOpen] = useState(false);
  const [editingScript, setEditingScript] = useState<Partial<AutomationScript> | null>(null);
  const [editorMode, setEditorMode] = useState<"visual" | "code">("visual");
  const [visualSteps, setVisualSteps] = useState<VisualStep[]>([]);

  const [logModalOpen, setLogModalOpen] = useState(false);
  const [viewingLog, setViewingLog] = useState<{
    name: string;
    trigger?: string;
    time?: string;
    log: string;
    status: string;
    durationMs?: number;
  } | null>(null);

  const [installModalOpen, setInstallModalOpen] = useState(false);
  const [selectedTemplate, setSelectedTemplate] = useState<AutomationMarketplaceTemplate | null>(null);
  const [templateInputs, setTemplateInputs] = useState<Record<string, string>>({});
  const [customInstallName, setCustomInstallName] = useState("");

  // Test Runner state in Editor
  const [testTorrentId, setTestTorrentId] = useState<number | undefined>(undefined);
  const [testResult, setTestResult] = useState<AutomationExecutionResult | null>(null);
  const [isTesting, setIsTesting] = useState(false);
  const [isRunningId, setIsRunningId] = useState<number | null>(null);

  const scriptList = scripts || [];
  const templateList = templates || [];

  const filteredScripts = useMemo(() => {
    return scriptList.filter((s) => {
      if (selectedTriggerFilter !== "All" && s.trigger.toString() !== selectedTriggerFilter) {
        return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase();
        const matchName = s.name.toLowerCase().includes(q);
        const matchDesc = s.description?.toLowerCase().includes(q) || false;
        if (!matchName && !matchDesc) return false;
      }
      return true;
    });
  }, [scriptList, selectedTriggerFilter, searchQuery]);

  const filteredTemplates = useMemo(() => {
    return templateList.filter((t) => {
      if (selectedCategoryFilter !== "All" && t.category !== selectedCategoryFilter) {
        return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase();
        const matchName = t.name.toLowerCase().includes(q);
        const matchDesc = t.description.toLowerCase().includes(q);
        if (!matchName && !matchDesc) return false;
      }
      return true;
    });
  }, [templateList, selectedCategoryFilter, searchQuery]);

  const marketplaceCategories = useMemo(() => {
    const set = new Set<string>();
    templateList.forEach((t) => set.add(t.category));
    return ["All", ...Array.from(set)];
  }, [templateList]);

  // Sync visual steps when editing script opens
  useEffect(() => {
    if (editingScript) {
      if (editingScript.language === "Yaml" || editingScript.language === 1) {
        setVisualSteps(yamlToVisualSteps(editingScript.code || ""));
      } else {
        setEditorMode("code");
      }
    }
  }, [editingScript?.id, editingScript?.language]);

  // Sync visual steps back to YAML code
  function updateVisualSteps(newSteps: VisualStep[]) {
    setVisualSteps(newSteps);
    if (editingScript) {
      const generatedYaml = visualStepsToYaml(
        editingScript.name || "Automation Pipeline",
        editingScript.trigger?.toString() || "TorrentCompleted",
        newSteps
      );
      setEditingScript({ ...editingScript, code: generatedYaml });
    }
  }

  function openNewScript(language: "Yaml" | "JavaScript" = "Yaml") {
    const initialSteps: VisualStep[] = [
      {
        id: "step-1",
        name: "Filter Large Torrents",
        conditionEnabled: true,
        conditionLeft: "${torrent.size}",
        conditionOp: ">",
        conditionRight: "5000000000",
        hasHttp: false,
        http: { method: "POST", url: "", json: true, body: "{}", register: "" },
        actions: [
          { id: "act-1", type: "addTag", value: "Large-Release" },
          { id: "act-2", type: "command", value: "Backup" },
        ],
      },
    ];

    const initialCode = language === "Yaml"
      ? visualStepsToYaml("New Automation Pipeline", "TorrentCompleted", initialSteps)
      : `// JavaScript Contexts: torrent, system, api, http, html, inputs, secrets, console
console.log('Processing torrent: ' + (torrent ? torrent.name : 'System Event'));

if (torrent) {
  if (torrent.size > 5000000000) {
    torrent.addTag('Large-Release');
  }
}

// Execute system command
// system.runCommand('Backup');
`;

    setEditingScript({
      name: language === "Yaml" ? "New Automation Pipeline" : "New Custom Script",
      description: "",
      trigger: "TorrentCompleted",
      language: language,
      code: initialCode,
      inputsJson: "{}",
      isEnabled: true,
      targetCategories: [],
      targetTagIds: [],
    });
    setVisualSteps(initialSteps);
    setEditorMode(language === "Yaml" ? "visual" : "code");
    setTestResult(null);
    setEditorOpen(true);
  }

  function openEditScript(script: AutomationScript) {
    setEditingScript({ ...script });
    const isYaml = script.language === "Yaml" || script.language === 1;
    if (isYaml) {
      setVisualSteps(yamlToVisualSteps(script.code || ""));
      setEditorMode("visual");
    } else {
      setEditorMode("code");
    }
    setTestResult(null);
    setEditorOpen(true);
  }

  function handleSaveScript() {
    if (!editingScript || !editingScript.name?.trim()) return;

    let finalScript = { ...editingScript };
    if ((finalScript.language === "Yaml" || finalScript.language === 1) && editorMode === "visual") {
      finalScript.code = visualStepsToYaml(
        finalScript.name || "Pipeline",
        finalScript.trigger?.toString() || "TorrentCompleted",
        visualSteps
      );
    }

    if (finalScript.id && finalScript.id > 0) {
      updateScript.mutate(finalScript as AutomationScript, {
        onSuccess: () => setEditorOpen(false),
      });
    } else {
      createScript.mutate(finalScript, {
        onSuccess: () => setEditorOpen(false),
      });
    }
  }

  function handleDeleteScript(id: number) {
    if (window.confirm("Are you sure you want to delete this automation pipeline?")) {
      deleteScript.mutate(id);
    }
  }

  function handleToggleEnabled(script: AutomationScript) {
    updateScript.mutate({
      ...script,
      isEnabled: !script.isEnabled,
    });
  }

  function handleRunNow(id: number, torrentId?: number) {
    setIsRunningId(id);
    const targetScript = scriptList.find((s) => s.id === id);
    runScript.mutate(
      { id, torrentId },
      {
        onSuccess: (res) => {
          setIsRunningId(null);
          setViewingLog({
            name: targetScript?.name || `Script #${id}`,
            trigger: targetScript?.trigger?.toString() || "Manual",
            time: new Date().toLocaleTimeString(),
            log: res.outputLog || (res.success ? "Completed with no output." : (res.error || "Failed")),
            status: res.success ? "Success" : "Failed",
            durationMs: res.executionTimeMs,
          });
          setLogModalOpen(true);
        },
        onError: (err) => {
          setIsRunningId(null);
          alert(`Execution error: ${err.message}`);
        },
      }
    );
  }

  function handleDryRun() {
    if (!editingScript) return;
    setIsTesting(true);
    setTestResult(null);

    let scriptPayload = { ...editingScript };
    if ((scriptPayload.language === "Yaml" || scriptPayload.language === 1) && editorMode === "visual") {
      scriptPayload.code = visualStepsToYaml(
        scriptPayload.name || "Pipeline",
        scriptPayload.trigger?.toString() || "TorrentCompleted",
        visualSteps
      );
    }

    testScript.mutate(
      {
        script: scriptPayload,
        torrentId: testTorrentId || (torrents && torrents[0]?.id),
      },
      {
        onSuccess: (res) => {
          setTestResult(res);
          setIsTesting(false);
        },
        onError: (err) => {
          setIsTesting(false);
          setTestResult({
            success: false,
            error: err.message,
            executionTimeMs: 0,
            tagsToAdd: [],
            tagsToRemove: [],
            shouldPause: false,
            shouldResume: false,
            shouldRemove: false,
            deleteDataOnRemove: false,
          });
        },
      }
    );
  }

  function handleInstallTemplate() {
    if (!selectedTemplate) return;
    installTemplate.mutate(
      {
        templateId: selectedTemplate.id,
        customName: customInstallName || selectedTemplate.name,
        customInputs: templateInputs,
      },
      {
        onSuccess: () => {
          setInstallModalOpen(false);
          setActiveTab("scripts");
        },
        onError: (err) => {
          alert(`Installation failed: ${err.message}`);
        },
      }
    );
  }

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1 style={{ fontSize: "1.75rem", fontWeight: 700, margin: 0, display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span>⚡</span> Automation Pipelines & Marketplace
          </h1>
          <p style={{ color: "var(--text-muted, #888)", margin: "0.25rem 0 0 0", fontSize: "0.9rem" }}>
            Visual drag-and-drop workflow builder, low-code scripting engine, and community pipeline registry.
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem" }}>
          <button
            className={`btn ${activeTab === "scripts" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("scripts")}
          >
            📋 My Pipelines ({scriptList.length})
          </button>
          <button
            className={`btn ${activeTab === "history" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("history")}
          >
            📊 Run History
          </button>
          <button
            className={`btn ${activeTab === "marketplace" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("marketplace")}
          >
            🛍️ Marketplace ({templateList.length})
          </button>
        </div>
      </div>

      {/* TAB 1: MY SCRIPTS / PIPELINES */}
      {activeTab === "scripts" && (
        <div>
          {/* Action Bar */}
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "1rem",
              flexWrap: "wrap",
              gap: "0.75rem",
            }}
          >
            <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
              <input
                type="text"
                placeholder="Search pipelines..."
                className="input"
                style={{ width: "240px" }}
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />
              <select
                className="input"
                style={{ width: "190px" }}
                value={selectedTriggerFilter}
                onChange={(e) => setSelectedTriggerFilter(e.target.value)}
              >
                <option value="All">All Triggers</option>
                {Object.entries(TRIGGER_LABELS).map(([k, label]) => (
                  <option key={k} value={k}>{label}</option>
                ))}
              </select>
            </div>

            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button className="btn btn-primary" onClick={() => openNewScript("Yaml")}>
                ✨ + Visual Pipeline Builder
              </button>
              <button className="btn btn-secondary" onClick={() => openNewScript("JavaScript")}>
                💻 + JavaScript Script
              </button>
            </div>
          </div>

          {loadingScripts ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
              Loading automation pipelines...
            </div>
          ) : filteredScripts.length === 0 ? (
            <div className="panel" style={{ padding: "3rem", textAlign: "center" }}>
              <div style={{ fontSize: "2.5rem", marginBottom: "0.5rem" }}>⚙️</div>
              <h3 style={{ margin: "0 0 0.5rem 0" }}>No Automation Pipelines Found</h3>
              <p style={{ color: "var(--text-muted)", maxWidth: "480px", margin: "0 auto 1.5rem auto" }}>
                Create automated actions for when torrents are added, completed, hit ratio goals, or browse the Community Marketplace.
              </p>
              <div style={{ display: "flex", gap: "0.5rem", justifyContent: "center" }}>
                <button className="btn btn-primary" onClick={() => openNewScript("Yaml")}>
                  ✨ Build Visual Pipeline
                </button>
                <button className="btn btn-secondary" onClick={() => setActiveTab("marketplace")}>
                  🛍️ Browse Marketplace
                </button>
              </div>
            </div>
          ) : (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(360px, 1fr))", gap: "1rem" }}>
              {filteredScripts.map((script) => (
                <div
                  key={script.id}
                  className="panel"
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    borderLeft: `4px solid ${script.isEnabled ? "var(--accent, #3b82f6)" : "var(--border, #444)"}`,
                    padding: "1.25rem",
                  }}
                >
                  <div>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "0.5rem" }}>
                      <div>
                        <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 600 }}>{script.name}</h3>
                        <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                          {script.language === 1 || script.language === "Yaml" ? "📦 Visual Pipeline (YAML)" : "💻 JavaScript"}
                        </span>
                      </div>
                      <label style={{ display: "flex", alignItems: "center", cursor: "pointer", gap: "0.35rem" }}>
                        <input
                          type="checkbox"
                          checked={script.isEnabled}
                          onChange={() => handleToggleEnabled(script)}
                        />
                        <span style={{ fontSize: "0.8rem", fontWeight: 600 }}>
                          {script.isEnabled ? "Active" : "Disabled"}
                        </span>
                      </label>
                    </div>

                    <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 0.75rem 0", minHeight: "2.4rem" }}>
                      {script.description || "No description provided."}
                    </p>

                    <div style={{ display: "flex", flexWrap: "wrap", gap: "0.35rem", marginBottom: "1rem" }}>
                      <span className="badge" style={{ backgroundColor: "rgba(59, 130, 246, 0.15)", color: "#60a5fa" }}>
                        {TRIGGER_LABELS[script.trigger.toString()] || script.trigger.toString()}
                      </span>
                      {script.targetCategories && script.targetCategories.length > 0 && (
                        <span className="badge" style={{ backgroundColor: "rgba(168, 85, 247, 0.15)", color: "#c084fc" }}>
                          📁 {script.targetCategories.join(", ")}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Footer metadata & buttons */}
                  <div>
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        fontSize: "0.75rem",
                        color: "var(--text-muted)",
                        borderTop: "1px solid var(--border, #333)",
                        paddingTop: "0.75rem",
                        marginBottom: "0.75rem",
                      }}
                    >
                      <span>
                        Last Run:{" "}
                        {script.lastExecutedAt
                          ? new Date(script.lastExecutedAt).toLocaleTimeString()
                          : "Never"}
                      </span>
                      {script.lastExecutionStatus && (
                        <span
                          style={{
                            fontWeight: 600,
                            color: script.lastExecutionStatus === "Success" ? "#22c55e" : "#ef4444",
                          }}
                        >
                          {script.lastExecutionStatus === "Success" ? "● Succeeded" : "● Failed"}
                        </span>
                      )}
                    </div>

                    <div style={{ display: "flex", gap: "0.4rem", justifyContent: "flex-end" }}>
                      {script.lastExecutionLog && (
                        <button
                          className="btn btn-sm btn-secondary"
                          onClick={() => {
                            setViewingLog({
                              name: script.name,
                              trigger: script.trigger.toString(),
                              time: script.lastExecutedAt ? new Date(script.lastExecutedAt).toLocaleString() : undefined,
                              log: script.lastExecutionLog || "",
                              status: script.lastExecutionStatus || "Unknown",
                            });
                            setLogModalOpen(true);
                          }}
                          title="View Execution Log"
                        >
                          📜 Logs
                        </button>
                      )}
                      <button
                        className="btn btn-sm btn-secondary"
                        onClick={() => handleRunNow(script.id)}
                        disabled={isRunningId === script.id}
                        title="Trigger run immediately"
                      >
                        {isRunningId === script.id ? "⏳ Running..." : "▶️ Run"}
                      </button>
                      <button className="btn btn-sm btn-secondary" onClick={() => openEditScript(script)}>
                        ✏️ Edit
                      </button>
                      <button
                        className="btn btn-sm btn-danger"
                        onClick={() => handleDeleteScript(script.id)}
                        title="Delete Script"
                      >
                        🗑️
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* TAB 2: PIPELINE RUN HISTORY & VIEWER */}
      {activeTab === "history" && (
        <div>
          <div className="panel" style={{ padding: "1.5rem" }}>
            <h2 style={{ fontSize: "1.2rem", margin: "0 0 1rem 0" }}>📊 Pipeline Execution History</h2>
            <p style={{ color: "var(--text-muted)", fontSize: "0.9rem", marginBottom: "1.5rem" }}>
              Detailed execution traces and step-by-step performance metrics for recent pipeline runs.
            </p>

            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.9rem" }}>
              <thead>
                <tr style={{ borderBottom: "1px solid var(--border)", textAlign: "left" }}>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Status</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Pipeline Name</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Trigger</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Executed At</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {scriptList.filter((s) => s.lastExecutedAt).length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ textAlign: "center", padding: "2rem", color: "var(--text-muted)" }}>
                      No pipeline execution runs recorded yet. Trigger a run or wait for an event.
                    </td>
                  </tr>
                ) : (
                  scriptList
                    .filter((s) => s.lastExecutedAt)
                    .sort((a, b) => (new Date(b.lastExecutedAt!).getTime() - new Date(a.lastExecutedAt!).getTime()))
                    .map((s) => (
                      <tr key={s.id} style={{ borderBottom: "1px solid var(--border)" }}>
                        <td style={{ padding: "0.75rem 0.5rem" }}>
                          <span
                            className="badge"
                            style={{
                              backgroundColor: s.lastExecutionStatus === "Success" ? "rgba(34, 197, 94, 0.15)" : "rgba(239, 68, 68, 0.15)",
                              color: s.lastExecutionStatus === "Success" ? "#22c55e" : "#ef4444",
                            }}
                          >
                            {s.lastExecutionStatus === "Success" ? "✅ Success" : "❌ Failed"}
                          </span>
                        </td>
                        <td style={{ padding: "0.75rem 0.5rem", fontWeight: 600 }}>{s.name}</td>
                        <td style={{ padding: "0.75rem 0.5rem" }}>
                          <span className="badge">{TRIGGER_LABELS[s.trigger.toString()] || s.trigger.toString()}</span>
                        </td>
                        <td style={{ padding: "0.75rem 0.5rem", color: "var(--text-muted)" }}>
                          {s.lastExecutedAt ? new Date(s.lastExecutedAt).toLocaleString() : "Unknown"}
                        </td>
                        <td style={{ padding: "0.75rem 0.5rem" }}>
                          <button
                            className="btn btn-sm btn-secondary"
                            onClick={() => {
                              setViewingLog({
                                name: s.name,
                                trigger: s.trigger.toString(),
                                time: s.lastExecutedAt ? new Date(s.lastExecutedAt).toLocaleString() : undefined,
                                log: s.lastExecutionLog || "",
                                status: s.lastExecutionStatus || "Unknown",
                              });
                              setLogModalOpen(true);
                            }}
                          >
                            🔍 Trace Inspector
                          </button>
                        </td>
                      </tr>
                    ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 3: MARKETPLACE */}
      {activeTab === "marketplace" && (
        <div>
          {/* Marketplace Category Filters */}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem", flexWrap: "wrap", gap: "0.5rem" }}>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
              {marketplaceCategories.map((cat) => (
                <button
                  key={cat}
                  className={`btn btn-sm ${selectedCategoryFilter === cat ? "btn-primary" : "btn-secondary"}`}
                  onClick={() => setSelectedCategoryFilter(cat)}
                >
                  {cat}
                </button>
              ))}
            </div>

            <input
              type="text"
              placeholder="Search community templates..."
              className="input"
              style={{ width: "240px" }}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          {loadingTemplates ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
              Loading marketplace catalog...
            </div>
          ) : (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(360px, 1fr))", gap: "1rem" }}>
              {filteredTemplates.map((template) => (
                <div
                  key={template.id}
                  className="panel"
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    padding: "1.25rem",
                  }}
                >
                  <div>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "0.5rem" }}>
                      <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 600 }}>{template.name}</h3>
                      <span className="badge" style={{ backgroundColor: "rgba(59, 130, 246, 0.15)", color: "#60a5fa" }}>
                        v{template.version}
                      </span>
                    </div>

                    <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginBottom: "0.5rem" }}>
                      by <span style={{ fontWeight: 600, color: "var(--text-primary)" }}>{template.author}</span> •{" "}
                      <span className="badge" style={{ fontSize: "0.7rem" }}>{template.category}</span>
                    </div>

                    <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 1rem 0" }}>
                      {template.description}
                    </p>
                  </div>

                  <button
                    className="btn btn-primary"
                    style={{ width: "100%" }}
                    onClick={() => {
                      setSelectedTemplate(template);
                      setTemplateInputs({ ...template.defaultInputs });
                      setCustomInstallName(template.name);
                      setInstallModalOpen(true);
                    }}
                  >
                    ⬇️ Install Pipeline
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* SCRIPT & PIPELINE EDITOR MODAL */}
      {editorOpen && editingScript && (
        <div className="modal-overlay">
          <div
            className="modal panel"
            style={{
              width: "100%",
              maxWidth: "1050px",
              maxHeight: "92vh",
              display: "flex",
              flexDirection: "column",
              padding: "1.5rem 1.75rem",
              overflow: "hidden",
              backgroundColor: "var(--bg-secondary)",
            }}
          >
            {/* Modal Header */}
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1.25rem", borderBottom: "1px solid var(--border)", paddingBottom: "1rem" }}>
              <div style={{ display: "flex", alignItems: "center", gap: "1.25rem", flexWrap: "wrap" }}>
                <h2 style={{ margin: 0, fontSize: "1.3rem", fontWeight: 700, color: "var(--text-primary)" }}>
                  {editingScript.id ? "Edit Automation Pipeline" : "Create Automation Pipeline"}
                </h2>
                {/* Visual vs Code Mode Toggle */}
                <div style={{ display: "flex", backgroundColor: "var(--bg-primary, #1a1815)", borderRadius: "8px", padding: "3px", border: "1px solid var(--border-light)" }}>
                  <button
                    type="button"
                    className={`btn btn-sm ${editorMode === "visual" ? "btn-primary" : "btn-secondary"}`}
                    style={{ padding: "0.35rem 0.9rem", fontSize: "0.825rem", fontWeight: 600, borderRadius: "6px" }}
                    onClick={() => {
                      if (editorMode !== "visual") {
                        setVisualSteps(yamlToVisualSteps(editingScript.code || ""));
                        setEditorMode("visual");
                      }
                    }}
                  >
                    ✨ Visual Pipeline Builder
                  </button>
                  <button
                    type="button"
                    className={`btn btn-sm ${editorMode === "code" ? "btn-primary" : "btn-secondary"}`}
                    style={{ padding: "0.35rem 0.9rem", fontSize: "0.825rem", fontWeight: 600, borderRadius: "6px" }}
                    onClick={() => {
                      if (editorMode !== "code") {
                        const generatedYaml = visualStepsToYaml(
                          editingScript.name || "Pipeline",
                          editingScript.trigger?.toString() || "TorrentCompleted",
                          visualSteps
                        );
                        setEditingScript({ ...editingScript, code: generatedYaml });
                        setEditorMode("code");
                      }
                    }}
                  >
                    💻 Code / YAML View
                  </button>
                </div>
              </div>
              <button
                type="button"
                className="btn btn-sm btn-secondary"
                style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", borderRadius: "6px", fontSize: "1rem" }}
                onClick={() => setEditorOpen(false)}
              >
                ✕
              </button>
            </div>

            <div style={{ overflowY: "auto", flex: 1, paddingRight: "0.5rem" }}>
              {/* Form Row 1: Name, Trigger, Language */}
              <div style={{ display: "grid", gridTemplateColumns: "1.4fr 1fr 1fr", gap: "1rem", marginBottom: "1rem" }}>
                <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                  <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>Pipeline Name</label>
                  <input
                    type="text"
                    className="form-control"
                    value={editingScript.name || ""}
                    onChange={(e) => setEditingScript({ ...editingScript, name: e.target.value })}
                    placeholder="e.g. 4K Movie Auto-Zap & Backup"
                  />
                </div>

                <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                  <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>Trigger Event</label>
                  <select
                    className="form-control"
                    value={editingScript.trigger?.toString()}
                    onChange={(e) => setEditingScript({ ...editingScript, trigger: e.target.value as AutomationTrigger })}
                  >
                    {Object.entries(TRIGGER_LABELS).map(([k, label]) => (
                      <option key={k} value={k}>{label}</option>
                    ))}
                  </select>
                </div>

                <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                  <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>Engine / Format</label>
                  <select
                    className="form-control"
                    value={editingScript.language?.toString()}
                    onChange={(e) => {
                      const newLang = e.target.value as AutomationLanguage;
                      setEditingScript({ ...editingScript, language: newLang });
                      if (newLang === "JavaScript") {
                        setEditorMode("code");
                      }
                    }}
                  >
                    <option value="Yaml">YAML (Visual Pipeline DSL)</option>
                    <option value="JavaScript">JavaScript (Sandboxed Jint)</option>
                  </select>
                </div>
              </div>

              {/* Form Row 2: Description */}
              <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem", marginBottom: "1.25rem" }}>
                <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>Description</label>
                <input
                  type="text"
                  className="form-control"
                  value={editingScript.description || ""}
                  onChange={(e) => setEditingScript({ ...editingScript, description: e.target.value })}
                  placeholder="Summary of what this automation pipeline performs..."
                />
              </div>

              {/* EDITOR MODE 1: VISUAL PIPELINE BUILDER */}
              {editorMode === "visual" && (
                <div style={{ marginBottom: "1rem" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.75rem" }}>
                    <label style={{ fontSize: "0.95rem", fontWeight: 700, color: "var(--accent)" }}>
                      ✨ Pipeline Steps ({visualSteps.length})
                    </label>
                    <button
                      type="button"
                      className="btn btn-sm btn-primary"
                      style={{ display: "flex", alignItems: "center", gap: "0.35rem", padding: "0.4rem 0.85rem" }}
                      onClick={() => {
                        const newStep: VisualStep = {
                          id: `step-${visualSteps.length + 1}-${Date.now()}`,
                          name: `Step ${visualSteps.length + 1}`,
                          conditionEnabled: false,
                          conditionLeft: "${torrent.size}",
                          conditionOp: ">",
                          conditionRight: "1000000000",
                          hasHttp: false,
                          http: { method: "POST", url: "", json: true, body: "{}", register: "" },
                          actions: [{ id: `act-${Date.now()}`, type: "addTag", value: "Auto-Tagged" }],
                        };
                        updateVisualSteps([...visualSteps, newStep]);
                      }}
                    >
                      ➕ Add Step
                    </button>
                  </div>

                  {visualSteps.map((step, stepIdx) => (
                    <div
                      key={step.id}
                      className="panel"
                      style={{
                        marginBottom: "1rem",
                        backgroundColor: "var(--bg-primary, #1e1b18)",
                        border: "1px solid var(--border-light, #3a352e)",
                        padding: "1.25rem",
                        borderRadius: "8px",
                      }}
                    >
                      {/* Step Header */}
                      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.85rem", gap: "0.75rem" }}>
                        <div style={{ display: "flex", alignItems: "center", gap: "0.6rem", flex: 1 }}>
                          <span
                            style={{
                              backgroundColor: "var(--accent, #c8a84e)",
                              color: "#fff",
                              borderRadius: "50%",
                              width: "24px",
                              height: "24px",
                              display: "inline-flex",
                              alignItems: "center",
                              justifyContent: "center",
                              fontSize: "0.75rem",
                              fontWeight: 700,
                              flexShrink: 0,
                            }}
                          >
                            {stepIdx + 1}
                          </span>
                          <input
                            type="text"
                            className="form-control"
                            style={{ fontWeight: 600, flex: 1, maxWidth: "450px" }}
                            value={step.name}
                            onChange={(e) => {
                              const copy = [...visualSteps];
                              copy[stepIdx].name = e.target.value;
                              updateVisualSteps(copy);
                            }}
                            placeholder="Step Name"
                          />
                        </div>

                        <div style={{ display: "flex", gap: "0.35rem" }}>
                          <button
                            type="button"
                            className="btn btn-sm btn-secondary"
                            disabled={stepIdx === 0}
                            style={{ padding: "0.3rem 0.6rem" }}
                            onClick={() => {
                              const copy = [...visualSteps];
                              const temp = copy[stepIdx];
                              copy[stepIdx] = copy[stepIdx - 1];
                              copy[stepIdx - 1] = temp;
                              updateVisualSteps(copy);
                            }}
                          >
                            ⬆️
                          </button>
                          <button
                            type="button"
                            className="btn btn-sm btn-secondary"
                            disabled={stepIdx === visualSteps.length - 1}
                            style={{ padding: "0.3rem 0.6rem" }}
                            onClick={() => {
                              const copy = [...visualSteps];
                              const temp = copy[stepIdx];
                              copy[stepIdx] = copy[stepIdx + 1];
                              copy[stepIdx + 1] = temp;
                              updateVisualSteps(copy);
                            }}
                          >
                            ⬇️
                          </button>
                          <button
                            type="button"
                            className="btn btn-sm btn-danger"
                            style={{ padding: "0.3rem 0.6rem" }}
                            onClick={() => {
                              const copy = visualSteps.filter((_, idx) => idx !== stepIdx);
                              updateVisualSteps(copy);
                            }}
                          >
                            🗑️
                          </button>
                        </div>
                      </div>

                      {/* Condition Builder */}
                      <div
                        style={{
                          backgroundColor: "var(--bg-secondary, #2a2620)",
                          border: "1px solid var(--border-light, #3a352e)",
                          padding: "0.85rem 1rem",
                          borderRadius: "6px",
                          marginBottom: "1rem",
                        }}
                      >
                        <label style={{ display: "flex", alignItems: "center", gap: "0.6rem", fontSize: "0.85rem", fontWeight: 600, cursor: "pointer", color: "var(--text-primary)" }}>
                          <input
                            type="checkbox"
                            checked={step.conditionEnabled}
                            onChange={(e) => {
                              const copy = [...visualSteps];
                              copy[stepIdx].conditionEnabled = e.target.checked;
                              updateVisualSteps(copy);
                            }}
                          />
                          Only run this step if condition matches (IF condition)
                        </label>

                        {step.conditionEnabled && (() => {
                          const propDef = CONDITION_PROPERTIES.find((p) => p.value === step.conditionLeft) || {
                            value: "custom",
                            label: "Custom",
                            group: "Custom / Dynamic",
                            type: "custom" as const,
                            defaultOp: "==" as const,
                            defaultValue: "",
                          };

                          const isCustomLeft = !CONDITION_PROPERTIES.some((p) => p.value === step.conditionLeft && p.value !== "custom");

                          // Operator definitions per type
                          const opOptions = (() => {
                            switch (propDef.type) {
                              case "boolean":
                                return [
                                  { value: "==", label: "is / equals (==)" },
                                  { value: "!=", label: "is not (!=)" },
                                ];
                              case "enum":
                              case "category":
                              case "string":
                                return [
                                  { value: "==", label: "equals (==)" },
                                  { value: "!=", label: "not equals (!=)" },
                                ];
                              case "number":
                                return [
                                  { value: ">=", label: ">= (at least)" },
                                  { value: ">", label: "> (greater than)" },
                                  { value: "<=", label: "<= (at most)" },
                                  { value: "<", label: "< (less than)" },
                                  { value: "==", label: "== (exact)" },
                                  { value: "!=", label: "!= (not equal)" },
                                ];
                              case "custom":
                              default:
                                return [
                                  { value: "==", label: "==" },
                                  { value: "!=", label: "!=" },
                                  { value: ">=", label: ">=" },
                                  { value: "<=", label: "<=" },
                                  { value: ">", label: ">" },
                                  { value: "<", label: "<" },
                                ];
                            }
                          })();

                          // Auto-correct operator if invalid for current type
                          if (!opOptions.some((op) => op.value === step.conditionOp)) {
                            step.conditionOp = propDef.defaultOp;
                          }

                          // Determine available preset options for Right Value
                          let presetOptions: { value: string; label: string }[] | null = null;
                          if (propDef.type === "boolean" && propDef.options) {
                            presetOptions = propDef.options;
                          } else if (propDef.type === "enum" && propDef.options) {
                            presetOptions = propDef.options;
                          } else if (propDef.type === "category") {
                            presetOptions = (categories || []).map((c) => ({
                              value: `'${c.name}'`,
                              label: `📁 ${c.name}`,
                            }));
                          }

                          // Boolean value normalization & custom toggle check
                          const isCustomRight = (() => {
                            if (propDef.type === "boolean") {
                              const trimmed = step.conditionRight.trim().toLowerCase();
                              if (trimmed === "true" || trimmed === "false") return false;
                              if (trimmed.startsWith("${") || trimmed.includes("inputs.") || trimmed.includes("secrets.")) return true;
                              // Invalid/legacy value: normalize immediately
                              step.conditionRight = "true";
                              return false;
                            }
                            if (presetOptions) {
                              const isPresetMatch = presetOptions.some((o) => o.value.toLowerCase() === step.conditionRight.trim().toLowerCase());
                              return !isPresetMatch && step.conditionRight.trim() !== "";
                            }
                            return true;
                          })();

                          // Formatted live hint for numeric values
                          const numericLiveHint = (() => {
                            if (propDef.type !== "number" || !step.conditionRight) return null;
                            const num = Number(step.conditionRight);
                            if (isNaN(num)) return null;
                            if (propDef.unit === "bytes") {
                              return formatBytes(num);
                            }
                            if (propDef.unit === "speed") {
                              return `${formatBytes(num)}/s`;
                            }
                            if (propDef.unit === "ratio") {
                              return `${num.toFixed(2)}x ratio`;
                            }
                            if (propDef.unit === "percent") {
                              return `${num}%`;
                            }
                            if (propDef.unit === "count") {
                              return `${num} peers`;
                            }
                            return null;
                          })();

                          return (
                            <div style={{ marginTop: "0.75rem", display: "flex", flexDirection: "column", gap: "0.5rem" }}>
                              <div style={{ display: "flex", gap: "0.6rem", alignItems: "center", flexWrap: "wrap" }}>
                                {/* Left Property Select */}
                                <div style={{ display: "flex", flexDirection: "column", gap: "0.2rem", flex: isCustomLeft ? 1 : undefined }}>
                                  <select
                                    className="form-control"
                                    style={{ width: isCustomLeft ? "100%" : "230px", flexShrink: 0, fontWeight: 500 }}
                                    value={isCustomLeft ? "custom" : step.conditionLeft}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      const newVal = e.target.value;
                                      if (newVal === "custom") {
                                        copy[stepIdx].conditionLeft = "${inputs.customProp}";
                                        copy[stepIdx].conditionOp = "==";
                                        copy[stepIdx].conditionRight = "";
                                      } else {
                                        copy[stepIdx].conditionLeft = newVal;
                                        const found = CONDITION_PROPERTIES.find((p) => p.value === newVal);
                                        if (found) {
                                          copy[stepIdx].conditionOp = found.defaultOp;
                                          copy[stepIdx].conditionRight = found.defaultValue;
                                        }
                                      }
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    {Array.from(new Set(CONDITION_PROPERTIES.map((p) => p.group))).map((groupName) => (
                                      <optgroup key={groupName} label={groupName}>
                                        {CONDITION_PROPERTIES.filter((p) => p.group === groupName).map((p) => (
                                          <option key={p.value} value={p.value}>
                                            {p.label}
                                          </option>
                                        ))}
                                      </optgroup>
                                    ))}
                                  </select>
                                  {isCustomLeft && (
                                    <input
                                      type="text"
                                      className="form-control"
                                      style={{ fontSize: "0.8rem", padding: "0.25rem 0.5rem" }}
                                      value={step.conditionLeft}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        copy[stepIdx].conditionLeft = e.target.value;
                                        updateVisualSteps(copy);
                                      }}
                                      placeholder="e.g. ${inputs.myProperty}"
                                    />
                                  )}
                                </div>

                                {/* Operator Select (Filtered per type) */}
                                <select
                                  className="form-control"
                                  style={{ minWidth: "90px", flexShrink: 0, textAlign: "center", fontWeight: 700 }}
                                  value={step.conditionOp}
                                  onChange={(e) => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].conditionOp = e.target.value as any;
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  {opOptions.map((op) => (
                                    <option key={op.value} value={op.value}>
                                      {op.label}
                                    </option>
                                  ))}
                                </select>

                                {/* Right Value: Type-Aware Presets, Numeric Chips or Custom Input */}
                                <div style={{ flex: 1, minWidth: "220px", display: "flex", gap: "0.35rem", alignItems: "center" }}>
                                  {presetOptions && !isCustomRight ? (
                                    <div style={{ display: "flex", gap: "0.35rem", width: "100%", alignItems: "center" }}>
                                      <select
                                        className="form-control"
                                        style={{ flex: 1, fontWeight: 500 }}
                                        value={step.conditionRight.trim()}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          if (e.target.value === "__custom__") {
                                            copy[stepIdx].conditionRight = propDef.type === "boolean" ? "${inputs.isPrivate}" : "";
                                          } else {
                                            copy[stepIdx].conditionRight = e.target.value;
                                          }
                                          updateVisualSteps(copy);
                                        }}
                                      >
                                        {presetOptions.map((opt) => (
                                          <option key={opt.value} value={opt.value}>
                                            {opt.label}
                                          </option>
                                        ))}
                                        <option value="__custom__">✏️ Custom Expression / Variable...</option>
                                      </select>
                                    </div>
                                  ) : propDef.type === "number" ? (
                                    <div style={{ display: "flex", flexDirection: "column", gap: "0.3rem", width: "100%" }}>
                                      <div style={{ display: "flex", gap: "0.35rem", alignItems: "center" }}>
                                        <input
                                          type="text"
                                          className="form-control"
                                          style={{ flex: 1 }}
                                          value={step.conditionRight}
                                          onChange={(e) => {
                                            const copy = [...visualSteps];
                                            copy[stepIdx].conditionRight = e.target.value;
                                            updateVisualSteps(copy);
                                          }}
                                          placeholder={propDef.placeholder || "e.g. 1000000000"}
                                        />
                                        {numericLiveHint && (
                                          <span
                                            className="badge"
                                            style={{
                                              padding: "0.35rem 0.6rem",
                                              fontSize: "0.8rem",
                                              whiteSpace: "nowrap",
                                              backgroundColor: "rgba(59, 130, 246, 0.15)",
                                              color: "var(--accent, #38bdf8)",
                                              border: "1px solid rgba(59, 130, 246, 0.3)",
                                              borderRadius: "4px",
                                            }}
                                          >
                                            {numericLiveHint}
                                          </span>
                                        )}
                                      </div>
                                      {propDef.presets && propDef.presets.length > 0 && (
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap", alignItems: "center" }}>
                                          <span style={{ fontSize: "0.7rem", color: "var(--text-muted)", marginRight: "0.15rem" }}>Quick set:</span>
                                          {propDef.presets.map((preset) => (
                                            <button
                                              key={preset.value}
                                              type="button"
                                              className="btn btn-secondary"
                                              style={{
                                                fontSize: "0.7rem",
                                                padding: "0.15rem 0.45rem",
                                                backgroundColor: step.conditionRight === preset.value ? "var(--accent, #38bdf8)" : undefined,
                                                color: step.conditionRight === preset.value ? "#000" : undefined,
                                                fontWeight: step.conditionRight === preset.value ? 700 : 400,
                                              }}
                                              onClick={() => {
                                                const copy = [...visualSteps];
                                                copy[stepIdx].conditionRight = preset.value;
                                                updateVisualSteps(copy);
                                              }}
                                            >
                                              {preset.label}
                                            </button>
                                          ))}
                                        </div>
                                      )}
                                    </div>
                                  ) : (
                                    <div style={{ display: "flex", gap: "0.35rem", width: "100%", alignItems: "center" }}>
                                      <input
                                        type="text"
                                        className="form-control"
                                        style={{ flex: 1 }}
                                        value={step.conditionRight}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          copy[stepIdx].conditionRight = e.target.value;
                                          updateVisualSteps(copy);
                                        }}
                                        placeholder={propDef.placeholder || "e.g. 'Custom Value' or ${inputs.val}"}
                                      />
                                      {presetOptions && (
                                        <button
                                          type="button"
                                          className="btn btn-sm btn-secondary"
                                          title="Switch back to presets dropdown"
                                          style={{ padding: "0.35rem 0.6rem", fontSize: "0.75rem", whiteSpace: "nowrap" }}
                                          onClick={() => {
                                            const copy = [...visualSteps];
                                            copy[stepIdx].conditionRight = presetOptions![0].value;
                                            updateVisualSteps(copy);
                                          }}
                                        >
                                          🔄 Presets
                                        </button>
                                      )}
                                    </div>
                                  )}
                                </div>
                              </div>

                              {/* Helper text based on type */}
                              <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", paddingLeft: "2px" }}>
                                {propDef.type === "boolean" && "💡 Boolean flag: Evaluates True (On/Yes) or False (Off/No)."}
                                {propDef.type === "enum" && "💡 State evaluation: Matches against the torrent's lifecycle state or enter a custom status."}
                                {propDef.type === "category" && "💡 Category evaluation: Select an existing category or enter a custom pattern."}
                                {propDef.type === "number" && "💡 Numeric comparison: Value is evaluated in bytes, ratios, counts, or percentages."}
                                {propDef.type === "string" && "💡 String matching: Supports exact matches or pattern values in quotes."}
                                {propDef.type === "custom" && "💡 Custom expression: Uses lazy matching with variables, inputs, or system properties."}
                              </div>
                            </div>
                          );
                        })()}
                      </div>

                      {/* Actions List */}
                      <div>
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.65rem", flexWrap: "wrap", gap: "0.5rem" }}>
                          <span style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--text-secondary)" }}>⚡ Step Actions</span>
                          <div style={{ display: "flex", gap: "0.35rem", flexWrap: "wrap" }}>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "addTag", value: "New-Tag" });
                                updateVisualSteps(copy);
                              }}
                            >
                              🏷️ + Tag
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "setCategory", value: categories?.[0]?.name || "Movies" });
                                updateVisualSteps(copy);
                              }}
                            >
                              📁 + Category
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "setUploadLimit", value: "1024" });
                                updateVisualSteps(copy);
                              }}
                            >
                              ⚡ + Limit
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "sendNotification", value: "Torrent event triggered notification" });
                                updateVisualSteps(copy);
                              }}
                            >
                              🔔 + Alert
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "notifyArr", value: "" });
                                updateVisualSteps(copy);
                              }}
                            >
                              📬 + Servarr
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                              onClick={() => {
                                const copy = [...visualSteps];
                                copy[stepIdx].actions.push({ id: `act-${Date.now()}`, type: "command", value: "Backup" });
                                updateVisualSteps(copy);
                              }}
                            >
                              ⚙️ + Command
                            </button>
                          </div>
                        </div>

                        {step.actions.length === 0 ? (
                          <div style={{ fontSize: "0.825rem", color: "var(--text-muted)", fontStyle: "italic", padding: "0.6rem 0" }}>
                            No actions added. Click any button above or select from the actions library below.
                          </div>
                        ) : (
                          step.actions.map((act, actIdx) => {
                            const actDef = ACTION_GROUPS.flatMap((g) => g.items).find((i) => i.type === act.type);
                            const placeholder = actDef?.placeholder || "Action value";

                            return (
                              <div
                                key={act.id}
                                style={{
                                  display: "flex",
                                  alignItems: "center",
                                  gap: "0.5rem",
                                  marginBottom: "0.5rem",
                                  backgroundColor: "var(--bg-secondary, #2a2620)",
                                  border: "1px solid var(--border-light, #3a352e)",
                                  padding: "0.5rem 0.75rem",
                                  borderRadius: "6px",
                                  flexWrap: "wrap",
                                }}
                              >
                                <select
                                  className="form-control"
                                  style={{ width: "220px", flexShrink: 0, fontWeight: 500 }}
                                  value={act.type}
                                  onChange={(e) => {
                                    const copy = [...visualSteps];
                                    const newType = e.target.value as VisualActionType;
                                    copy[stepIdx].actions[actIdx].type = newType;
                                    if (newType === "setCategory" && categories?.[0]) {
                                      copy[stepIdx].actions[actIdx].value = categories[0].name;
                                    } else if (newType === "command") {
                                      copy[stepIdx].actions[actIdx].value = COMMON_COMMANDS[0].name;
                                    } else if (newType === "setPriority") {
                                      copy[stepIdx].actions[actIdx].value = "High";
                                    } else if (newType === "setSequentialDownload" || newType === "setSuperSeeding") {
                                      copy[stepIdx].actions[actIdx].value = "true";
                                    } else if (newType === "notifyArr") {
                                      copy[stepIdx].actions[actIdx].value = "";
                                    }
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  {ACTION_GROUPS.map((group) => (
                                    <optgroup key={group.group} label={group.group}>
                                      {group.items.map((item) => (
                                        <option key={item.type} value={item.type}>
                                          {item.label}
                                        </option>
                                      ))}
                                    </optgroup>
                                  ))}
                                </select>

                                {/* Action value rendering */}
                                {act.type === "command" ? (
                                  <select
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    {COMMON_COMMANDS.map((cmd) => (
                                      <option key={cmd.name} value={cmd.name}>
                                        {cmd.name} — {cmd.desc}
                                      </option>
                                    ))}
                                  </select>
                                ) : act.type === "setCategory" ? (
                                  <select
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    {(categories || []).map((cat) => (
                                      <option key={cat.id} value={cat.name}>
                                        📁 {cat.name}
                                      </option>
                                    ))}
                                  </select>
                                ) : act.type === "setPriority" ? (
                                  <select
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value || "Normal"}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    <option value="High">⚡ High Priority</option>
                                    <option value="Normal">🔹 Normal Priority</option>
                                    <option value="Low">🔻 Low Priority</option>
                                    <option value="DoNotDownload">🚫 Do Not Download (Skip)</option>
                                  </select>
                                ) : act.type === "setSequentialDownload" || act.type === "setSuperSeeding" ? (
                                  <select
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value || "true"}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    <option value="true">✅ Enabled (True)</option>
                                    <option value="false">❌ Disabled (False)</option>
                                  </select>
                                ) : act.type === "notifyArr" ? (
                                  <select
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    <option value="">🌐 All Connected Servarr Instances</option>
                                    <option value="Sonarr">📺 Sonarr (TV Shows)</option>
                                    <option value="Radarr">🎬 Radarr (Movies)</option>
                                    <option value="Lidarr">🎵 Lidarr (Music)</option>
                                    <option value="Readarr">📚 Readarr (Books)</option>
                                    <option value="Whisparr">🔞 Whisparr (Adult)</option>
                                  </select>
                                ) : act.type === "remove" ? (
                                  <label style={{ flex: 1, display: "flex", alignItems: "center", gap: "0.5rem", fontSize: "0.85rem", cursor: "pointer", color: "var(--color-danger, #ff6b6b)" }}>
                                    <input
                                      type="checkbox"
                                      checked={act.deleteData || false}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        copy[stepIdx].actions[actIdx].deleteData = e.target.checked;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    🗑️ Also permanently delete downloaded files from disk
                                  </label>
                                ) : act.type === "pause" || act.type === "resume" || act.type === "recheck" || act.type === "reannounce" || act.type === "boostTracker" ? (
                                  <span style={{ flex: 1, fontSize: "0.85rem", color: "var(--text-muted)", paddingLeft: "0.25rem" }}>
                                    ✨ Auto-applies to active swarm & torrent
                                  </span>
                                ) : (
                                  <input
                                    type={act.type === "setUploadLimit" || act.type === "setDownloadLimit" || act.type === "setRatioLimit" || act.type === "setSeedingTimeLimit" || act.type === "delay" ? "number" : "text"}
                                    className="form-control"
                                    style={{ flex: 1, minWidth: "180px" }}
                                    value={act.value}
                                    onChange={(e) => {
                                      const copy = [...visualSteps];
                                      copy[stepIdx].actions[actIdx].value = e.target.value;
                                      updateVisualSteps(copy);
                                    }}
                                    placeholder={placeholder}
                                  />
                                )}

                                <button
                                  type="button"
                                  className="btn btn-sm btn-secondary"
                                  style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0, borderRadius: "6px" }}
                                  title="Delete Action"
                                  onClick={() => {
                                    const copy = [...visualSteps];
                                    copy[stepIdx].actions = copy[stepIdx].actions.filter((_, idx) => idx !== actIdx);
                                    updateVisualSteps(copy);
                                  }}
                                >
                                  ✕
                                </button>
                              </div>
                            );
                          })
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {/* EDITOR MODE 2: CODE / YAML / JS VIEW */}
              {editorMode === "code" && (
                <div style={{ marginBottom: "1.25rem", width: "100%" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem" }}>
                    <label style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--text-secondary)" }}>
                      {editingScript.language === "Yaml" || editingScript.language === 1 ? "YAML Pipeline DSL" : "JavaScript Code"}
                    </label>
                    <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                      Helpers: <code>system.runCommand()</code>, <code>api.get()</code>, <code>torrent.addTag()</code>
                    </span>
                  </div>
                  <textarea
                    className="form-control"
                    rows={16}
                    spellCheck={false}
                    autoCapitalize="none"
                    autoComplete="off"
                    autoCorrect="off"
                    style={{
                      fontFamily: "'Cascadia Code', 'Fira Code', 'JetBrains Mono', Consolas, Menlo, monospace",
                      fontSize: "0.875rem",
                      backgroundColor: "var(--bg-primary, #141310)",
                      color: "#e2e8f0",
                      lineHeight: "1.6",
                      padding: "1rem 1.25rem",
                      borderRadius: "8px",
                      border: "1px solid var(--border-light, #3a352e)",
                      width: "100%",
                      minHeight: "360px",
                      boxSizing: "border-box",
                      tabSize: 2,
                      whiteSpace: "pre",
                      resize: "vertical",
                    }}
                    value={editingScript.code || ""}
                    onChange={(e) => setEditingScript({ ...editingScript, code: e.target.value })}
                    onKeyDown={(e) => {
                      if (e.key === "Tab") {
                        e.preventDefault();
                        const target = e.currentTarget;
                        const start = target.selectionStart;
                        const end = target.selectionEnd;
                        const value = target.value;
                        const newValue = value.substring(0, start) + "  " + value.substring(end);
                        setEditingScript({ ...editingScript, code: newValue });
                        requestAnimationFrame(() => {
                          target.selectionStart = target.selectionEnd = start + 2;
                        });
                      }
                    }}
                  />
                </div>
              )}

              {/* LIVE DRY RUN & TEST RUNNER */}
              <div
                style={{
                  borderTop: "1px solid var(--border)",
                  paddingTop: "1.25rem",
                  marginTop: "1.25rem",
                  backgroundColor: "var(--bg-primary, rgba(0,0,0,0.15))",
                  border: "1px solid var(--border-light)",
                  padding: "1.1rem 1.25rem",
                  borderRadius: "8px",
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.85rem", gap: "1rem", flexWrap: "wrap" }}>
                  <div>
                    <h4 style={{ margin: "0 0 0.25rem 0", fontSize: "0.95rem", fontWeight: 600 }}>⚡ Live Dry Run Inspector</h4>
                    <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                      Test pipeline execution logic safely against active torrents without writing mutations.
                    </span>
                  </div>

                  <div style={{ display: "flex", gap: "0.6rem", alignItems: "center" }}>
                    <select
                      className="form-control"
                      style={{ width: "320px", fontSize: "0.825rem" }}
                      value={testTorrentId}
                      onChange={(e) => setTestTorrentId(e.target.value ? Number(e.target.value) : undefined)}
                    >
                      <option value="">Sample Torrent (Big Buck Bunny 1080p)</option>
                      {(torrents || []).map((t) => (
                        <option key={t.id} value={t.id}>{t.name} ({(t.totalSize / (1024 * 1024 * 1024)).toFixed(2)} GB)</option>
                      ))}
                    </select>

                    <button
                      type="button"
                      className="btn btn-primary"
                      style={{ display: "flex", alignItems: "center", gap: "0.4rem", padding: "0.45rem 1rem", fontWeight: 600 }}
                      onClick={handleDryRun}
                      disabled={isTesting}
                    >
                      {isTesting ? "⏳ Evaluating..." : "▶️ Dry Run"}
                    </button>
                  </div>
                </div>

                {testResult && (
                  <div
                    style={{
                      backgroundColor: testResult.success ? "rgba(34, 197, 94, 0.08)" : "rgba(239, 68, 68, 0.08)",
                      border: `1px solid ${testResult.success ? "#22c55e" : "#ef4444"}`,
                      borderRadius: "6px",
                      padding: "0.75rem 1rem",
                      fontSize: "0.85rem",
                      marginTop: "0.85rem",
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", fontWeight: 600, marginBottom: "0.5rem" }}>
                      <span style={{ color: testResult.success ? "#22c55e" : "#ef4444" }}>
                        {testResult.success ? "✅ Pipeline Dry Run Succeeded" : "❌ Pipeline Dry Run Failed"}
                      </span>
                      <span>Duration: {testResult.executionTimeMs}ms</span>
                    </div>

                    {testResult.tagsToAdd.length > 0 && (
                      <div style={{ marginBottom: "0.25rem" }}><strong>Tags Added:</strong> {testResult.tagsToAdd.join(", ")}</div>
                    )}
                    {testResult.newCategory && (
                      <div style={{ marginBottom: "0.25rem" }}><strong>New Category:</strong> {testResult.newCategory}</div>
                    )}
                    {testResult.shouldRecheck && (
                      <div style={{ marginBottom: "0.25rem" }}><strong>Torrent Action:</strong> 🔍 Force Hash Recheck</div>
                    )}

                    {testResult.outputLog && (
                      <pre
                        style={{
                          marginTop: "0.5rem",
                          marginBottom: 0,
                          backgroundColor: "#000",
                          color: "#a3e635",
                          padding: "0.6rem 0.85rem",
                          borderRadius: "4px",
                          fontSize: "0.775rem",
                          maxHeight: "150px",
                          overflowY: "auto",
                        }}
                      >
                        {testResult.outputLog}
                      </pre>
                    )}
                  </div>
                )}
              </div>
            </div>

            {/* Modal Footer */}
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.75rem", marginTop: "1.25rem", borderTop: "1px solid var(--border)", paddingTop: "1rem" }}>
              <button type="button" className="btn btn-secondary" style={{ padding: "0.5rem 1.25rem" }} onClick={() => setEditorOpen(false)}>
                Cancel
              </button>
              <button type="button" className="btn btn-primary" style={{ padding: "0.5rem 1.25rem", fontWeight: 600 }} onClick={handleSaveScript}>
                💾 Save Pipeline
              </button>
            </div>
          </div>
        </div>
      )}

      {/* PIPELINE RUN TRACE VIEWER MODAL */}
      {logModalOpen && viewingLog && (
        <div className="modal-overlay">
          <div
            className="modal panel"
            style={{
              width: "100%",
              maxWidth: "800px",
              maxHeight: "85vh",
              display: "flex",
              flexDirection: "column",
              padding: "1.5rem 1.75rem",
              backgroundColor: "var(--bg-secondary)",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
              <div>
                <h3 style={{ margin: 0, fontSize: "1.2rem", fontWeight: 700 }}>🔍 Pipeline Trace: {viewingLog.name}</h3>
                <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                  Trigger: {viewingLog.trigger} • Executed: {viewingLog.time || "Recently"}
                </span>
              </div>
              <button
                type="button"
                className="btn btn-sm btn-secondary"
                style={{ width: "32px", height: "32px", padding: 0, display: "flex", alignItems: "center", justifyContent: "center", borderRadius: "6px" }}
                onClick={() => setLogModalOpen(false)}
              >
                ✕
              </button>
            </div>

            <div style={{ marginBottom: "0.75rem", display: "flex", gap: "0.5rem" }}>
              <span
                className="badge"
                style={{
                  backgroundColor: viewingLog.status === "Success" ? "rgba(34, 197, 94, 0.15)" : "rgba(239, 68, 68, 0.15)",
                  color: viewingLog.status === "Success" ? "#22c55e" : "#ef4444",
                  padding: "0.25rem 0.6rem",
                  borderRadius: "4px",
                  fontWeight: 600,
                }}
              >
                Status: {viewingLog.status}
              </span>
            </div>

            <pre
              style={{
                flex: 1,
                overflowY: "auto",
                backgroundColor: "#0d1117",
                color: "#c9d1d9",
                padding: "1rem",
                borderRadius: "6px",
                fontFamily: "monospace",
                fontSize: "0.8rem",
                lineHeight: "1.5",
                whiteSpace: "pre-wrap",
                border: "1px solid #30363d",
              }}
            >
              {viewingLog.log}
            </pre>

            <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "1.25rem" }}>
              <button className="btn btn-secondary" style={{ padding: "0.45rem 1.25rem" }} onClick={() => setLogModalOpen(false)}>Close</button>
            </div>
          </div>
        </div>
      )}

      {/* TEMPLATE INSTALL MODAL */}
      {installModalOpen && selectedTemplate && (
        <div className="modal-overlay">
          <div className="modal panel" style={{ width: "100%", maxWidth: "600px", padding: "1.75rem", backgroundColor: "var(--bg-secondary)" }}>
            <h3 style={{ margin: "0 0 0.5rem 0", fontSize: "1.25rem", fontWeight: 700 }}>Install Community Pipeline</h3>
            <p style={{ color: "var(--text-muted)", fontSize: "0.85rem", marginBottom: "1.25rem", lineHeight: "1.4" }}>
              {selectedTemplate.description}
            </p>

            <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem", marginBottom: "1rem" }}>
              <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>Pipeline Custom Name</label>
              <input
                type="text"
                className="form-control"
                value={customInstallName}
                onChange={(e) => setCustomInstallName(e.target.value)}
              />
            </div>

            {selectedTemplate.inputFields && selectedTemplate.inputFields.length > 0 && (
              <div style={{ borderTop: "1px solid var(--border)", paddingTop: "1rem", marginTop: "1rem" }}>
                <h4 style={{ fontSize: "0.95rem", margin: "0 0 0.75rem 0", fontWeight: 600 }}>Pipeline Configuration Parameters</h4>
                {selectedTemplate.inputFields.map((field) => (
                  <div key={field.key} style={{ display: "flex", flexDirection: "column", gap: "0.4rem", marginBottom: "0.85rem" }}>
                    <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>{field.label}</label>
                    <input
                      type={field.type === "password" ? "password" : "text"}
                      className="form-control"
                      value={templateInputs[field.key] || ""}
                      onChange={(e) =>
                        setTemplateInputs({
                          ...templateInputs,
                          [field.key]: e.target.value,
                        })
                      }
                      placeholder={field.description}
                    />
                    <span style={{ fontSize: "0.775rem", color: "var(--text-muted)" }}>{field.description}</span>
                  </div>
                ))}
              </div>
            )}

            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.75rem", marginTop: "1.5rem", borderTop: "1px solid var(--border)", paddingTop: "1rem" }}>
              <button className="btn btn-secondary" style={{ padding: "0.45rem 1.25rem" }} onClick={() => setInstallModalOpen(false)}>Cancel</button>
              <button className="btn btn-primary" style={{ padding: "0.45rem 1.25rem", fontWeight: 600 }} onClick={handleInstallTemplate}>Install Pipeline</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
