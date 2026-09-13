import { useTranslation } from "../i18n";
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
import { useConfirm } from "../context/ConfirmContext";

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
  deleteData?: boolean;
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

export const getCommonCommands = (t: any) => [
  { name: "Backup", desc: t("automation.commands.createFullDatabaseConfigBackup") },
  { name: "SyncArr", desc: t("automation.commands.syncConnectedSonarrRadarrInsta") },
  { name: "WatchFolderScan", desc: t("automation.commands.scanWatchFolderForTorrents") },
  { name: "TrackerBoostScan", desc: t("automation.commands.scanOptimizeCandidateTrackers") },
  { name: "BlocklistUpdate", desc: t("automation.commands.updatePeerIpBlocklist") },
  { name: "GeoIpUpdate", desc: t("automation.commands.updateMaxmindGeoipDatabase") },
  { name: "RssSync", desc: t("automation.commands.pollRssIndexersForReleases") },
];

export const getActionGroups = (t: any): ActionGroup[] => [
  {
    group: t("automation.actions.tagsCategories"),
    items: [
      { type: "addTag" as VisualActionType, label: t("automation.actions.addTag.label"), placeholder: t("automation.actions.eg4khdrVerifiedFreeleech") },
      { type: "removeTag" as VisualActionType, label: t("automation.actions.removeTag.label"), placeholder: t("automation.actions.egIncompleteQueued") },
      { type: "setCategory" as VisualActionType, label: t("automation.actions.setCategory.label"), placeholder: t("automation.actions.egMoviesTvAnime") },
    ],
  },
  {
    group: t("automation.actions.torrentStateFlow"),
    items: [
      { type: "pause" as VisualActionType, label: t("automation.actions.pauseTorrent") },
      { type: "resume" as VisualActionType, label: t("automation.actions.resumeTorrent") },
      { type: "remove" as VisualActionType, label: t("automation.actions.removeTorrent"), extraHelp: t("automation.actions.deletesTorrentFromClientOption") },
      { type: "recheck" as VisualActionType, label: t("automation.ui.forceHashRecheck"), extraHelp: t("automation.actions.verifiesPieceHashesOnDisk") },
      { type: "reannounce" as VisualActionType, label: t("automation.actions.forceReannounce"), extraHelp: t("automation.actions.forcesImmediateTrackerUpdate") },
    ],
  },
  {
    group: t("automation.actions.limitsPriority"),
    items: [
      { type: "setUploadLimit" as VisualActionType, label: t("automation.actions.setUploadLimitKbs"), placeholder: t("automation.actions.eg10240ForUnlimited") },
      { type: "setDownloadLimit" as VisualActionType, label: t("automation.actions.setDownloadLimitKbs"), placeholder: t("automation.actions.eg51200ForUnlimited") },
      { type: "setRatioLimit" as VisualActionType, label: t("automation.actions.setStopRatioLimit"), placeholder: t("automation.actions.eg20") },
      { type: "setSeedingTimeLimit" as VisualActionType, label: t("automation.actions.setSeedingTimeLimitMinutes"), placeholder: t("automation.actions.eg288048Hours") },
      { type: "setPriority" as VisualActionType, label: t("automation.actions.setTorrentPriority"), placeholder: t("automation.actions.highNormalLowDonotdownload") },
      { type: "setSequentialDownload" as VisualActionType, label: t("automation.actions.sequentialDownloadToggle"), placeholder: t("automation.actions.trueOrFalse") },
      { type: "setSuperSeeding" as VisualActionType, label: t("automation.actions.initialSuperSeeding"), placeholder: t("automation.actions.trueOrFalse") },
    ],
  },
  {
    group: t("automation.actions.storageFiles"),
    items: [
      { type: "moveFiles" as VisualActionType, label: t("automation.actions.moveTorrentFilesChangeSave"), placeholder: t("automation.actions.egMediacompletedcategory") },
      { type: "extractArchive" as VisualActionType, label: t("automation.actions.extractArchiveRarzip7z"), placeholder: t("automation.actions.extractedpathBlankCurrent") },
      { type: "cleanFiles" as VisualActionType, label: t("automation.actions.cleanUnwantedFiles"), placeholder: t("automation.actions.nfoTxtSample") },
    ],
  },
  {
    group: t("automation.actions.trackersPeers"),
    items: [
      { type: "addTracker" as VisualActionType, label: t("automation.actions.addAnnounceUrl"), placeholder: t("automation.actions.httpstrackerexamplecomannounce") },
      { type: "removeTracker" as VisualActionType, label: t("automation.actions.removeAnnounceUrl"), placeholder: t("automation.actions.httpstrackerexamplecomannounce") },
      { type: "boostTracker" as VisualActionType, label: t("automation.actions.boostTrackerScrape"), placeholder: t("automation.actions.trackerUrlToPrioritize") },
      { type: "banPeer" as VisualActionType, label: t("automation.actions.banPeerIpSubnet"), placeholder: t("automation.actions.1921681100Or1000024") },
    ],
  },
  {
    group: t("automation.actions.alertsServarr"),
    items: [
      { type: "sendNotification" as VisualActionType, label: t("automation.actions.sendSystemPushNotification"), placeholder: t("automation.actions.torrentTorrentnameCompleted") },
      { type: "notifyArr" as VisualActionType, label: t("automation.actions.notifyServarrAppSonarrradarr"), placeholder: t("automation.actions.sonarrOrRadarr") },
      { type: "syncArr" as VisualActionType, label: t("automation.actions.triggerServarrRescan"), placeholder: t("automation.actions.instanceNameOrAll") },
    ],
  },
  {
    group: t("automation.actions.scriptingFlowControl"),
    items: [
      { type: "runScript" as VisualActionType, label: t("automation.actions.runCustomHostScript"), placeholder: t("automation.actions.scriptsondownloadsh") },
      { type: "delay" as VisualActionType, label: t("automation.actions.delaySleepSeconds"), placeholder: t("automation.actions.eg5") },
      { type: "log" as VisualActionType, label: t("automation.actions.pipelineLogMessage"), placeholder: t("automation.actions.logMessageToOutputStream") },
      { type: "setVariable" as VisualActionType, label: t("automation.actions.setPipelineVariable"), placeholder: t("automation.actions.keyvalue") },
      { type: "stopPipeline" as VisualActionType, label: t("automation.actions.stopPipelineEarly"), placeholder: t("automation.actions.reasonForHalting") },
      { type: "command" as VisualActionType, label: t("automation.actions.runInternalCommand"), placeholder: t("automation.actions.backupSyncarrRsssyncEtc") },
      { type: "http" as VisualActionType, label: t("automation.actions.sendCustomHttpRequest"), placeholder: t("automation.actions.httpsapiexamplecomwebhook") },
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

export const getConditionProperties = (t: any): PropertyDef[] => [
  // Booleans & Flags
  {
    value: "${torrent.isPrivate}",
    label: t("automation.conditions.isPrivateTracker"),
    group: t("automation.conditions.booleansFlags"),
    type: "boolean",
    options: [
      { value: "true", label: t("automation.conditions.truePrivateTracker") },
      { value: "false", label: t("automation.conditions.falsePublicTracker") },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },
  {
    value: "${torrent.isComplete}",
    label: t("automation.conditions.isDownloadCompleted"),
    group: t("automation.conditions.booleansFlags"),
    type: "boolean",
    options: [
      { value: "true", label: t("automation.conditions.trueCompleted100") },
      { value: "false", label: t("automation.conditions.falseIncompleteDownloading") },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },
  {
    value: "${system.vpnActive}",
    label: t("automation.conditions.vpnActiveProtected"),
    group: t("automation.conditions.booleansFlags"),
    type: "boolean",
    options: [
      { value: "true", label: t("automation.conditions.trueVpnProtected") },
      { value: "false", label: t("automation.conditions.falseVpnDownInactive") },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },
  {
    value: "${system.isPortForwarded}",
    label: t("automation.conditions.portForwardedOpen"),
    group: t("automation.conditions.booleansFlags"),
    type: "boolean",
    options: [
      { value: "true", label: t("automation.conditions.truePortOpenForwarded") },
      { value: "false", label: t("automation.conditions.falsePortClosed") },
    ],
    defaultOp: "==",
    defaultValue: "true",
  },

  // Status & Categories
  {
    value: "${torrent.status}",
    label: t("automation.conditions.torrentStateStatus"),
    group: t("automation.conditions.statusCategories"),
    type: "enum",
    options: [
      { value: "'Downloading'", label: t("automation.conditions.downloading") },
      { value: "'Seeding'", label: t("automation.conditions.seeding") },
      { value: "'Paused'", label: t("automation.conditions.paused") },
      { value: "'Stopped'", label: t("automation.conditions.stopped") },
      { value: "'Queued'", label: t("automation.conditions.queued") },
      { value: "'Checking'", label: t("automation.conditions.checking") },
      { value: "'Error'", label: t("automation.conditions.error") },
    ],
    defaultOp: "==",
    defaultValue: "'Downloading'",
  },
  {
    value: "${torrent.category}",
    label: t("automation.conditions.categoryName"),
    group: t("automation.conditions.statusCategories"),
    type: "category",
    defaultOp: "==",
    defaultValue: "",
    placeholder: t("automation.conditions.egMoviesOrTv"),
  },

  // Numbers & Metrics
  {
    value: "${sampleTorrent?.size}",
    label: t("automation.conditions.torrentSizeBytes"),
    group: t("automation.conditions.numbersMetrics"),
    type: "number",
    defaultOp: ">=",
    defaultValue: "1073741824",
    placeholder: t("automation.conditions.eg10737418241Gb"),
    unit: "bytes",
    presets: [
      { label: t("automation.conditions.500Mb"), value: "524288000" },
      { label: t("automation.conditions.1Gb"), value: "1073741824" },
      { label: t("automation.conditions.5Gb"), value: "5368709120" },
      { label: t("automation.conditions.10Gb"), value: "10737418240" },
      { label: t("automation.conditions.50Gb"), value: "53687091200" },
      { label: t("automation.conditions.100Gb"), value: "107374182400" },
    ],
  },
  {
    value: "${torrent.ratio}",
    label: t("automation.conditions.shareRatio"),
    group: t("automation.conditions.numbersMetrics"),
    type: "number",
    defaultOp: ">=",
    defaultValue: "1.0",
    placeholder: t("automation.conditions.eg10Or25"),
    unit: "ratio",
    presets: [
      { label: t("automation.conditions.05x"), value: "0.5" },
      { label: t("automation.conditions.10x"), value: "1.0" },
      { label: t("automation.conditions.15x"), value: "1.5" },
      { label: t("automation.conditions.20x"), value: "2.0" },
      { label: t("automation.conditions.30x"), value: "3.0" },
      { label: t("automation.conditions.50x"), value: "5.0" },
    ],
  },
  {
    value: "${torrent.progress}",
    label: t("automation.conditions.progress"),
    group: t("automation.conditions.numbersMetrics"),
    type: "number",
    defaultOp: ">=",
    defaultValue: "100",
    placeholder: t("automation.conditions.eg100Or50"),
    unit: "percent",
    presets: [
      { label: t("automation.conditions.25"), value: "25" },
      { label: t("automation.conditions.50"), value: "50" },
      { label: t("automation.conditions.75"), value: "75" },
      { label: t("automation.conditions.100"), value: "100" },
    ],
  },
  {
    value: "${torrent.downloadSpeed}",
    label: t("automation.conditions.downloadSpeedBs"),
    group: t("automation.conditions.numbersMetrics"),
    type: "number",
    defaultOp: ">",
    defaultValue: "1048576",
    placeholder: t("automation.conditions.eg10485761Mbs"),
    unit: "speed",
    presets: [
      { label: t("automation.conditions.512Kbs"), value: "524288" },
      { label: t("automation.conditions.1Mbs"), value: "1048576" },
      { label: t("automation.conditions.5Mbs"), value: "5242880" },
      { label: t("automation.conditions.10Mbs"), value: "10485760" },
      { label: t("automation.conditions.50Mbs"), value: "52428800" },
    ],
  },
  {
    value: "${torrent.uploadSpeed}",
    label: t("automation.conditions.uploadSpeedBs"),
    group: t("automation.conditions.numbersMetrics"),
    type: "number",
    defaultOp: ">",
    defaultValue: "524288",
    placeholder: t("automation.conditions.eg524288512Kbs"),
    unit: "speed",
    presets: [
      { label: t("automation.conditions.256Kbs"), value: "262144" },
      { label: t("automation.conditions.512Kbs"), value: "524288" },
      { label: t("automation.conditions.1Mbs"), value: "1048576" },
      { label: t("automation.conditions.5Mbs"), value: "5242880" },
      { label: t("automation.conditions.10Mbs"), value: "10485760" },
    ],
  },
  {
    value: "${torrent.seeders}",
    label: t("automation.conditions.seedersCount"),
    group: t("automation.conditions.numbersMetrics"),
    type: "number",
    defaultOp: "<",
    defaultValue: "3",
    placeholder: t("automation.conditions.eg3"),
    unit: "count",
    presets: [
      { label: t("automation.conditions.0"), value: "0" },
      { label: t("automation.conditions.1"), value: "1" },
      { label: t("automation.conditions.3"), value: "3" },
      { label: t("automation.conditions.5"), value: "5" },
      { label: t("automation.conditions.10"), value: "10" },
    ],
  },
  {
    value: "${torrent.leechers}",
    label: t("automation.conditions.leechersCount"),
    group: t("automation.conditions.numbersMetrics"),
    type: "number",
    defaultOp: ">",
    defaultValue: "5",
    placeholder: t("automation.actions.eg5"),
    unit: "count",
    presets: [
      { label: t("automation.conditions.0"), value: "0" },
      { label: t("automation.conditions.1"), value: "1" },
      { label: t("automation.conditions.5"), value: "5" },
      { label: t("automation.conditions.10"), value: "10" },
      { label: t("automation.conditions.20"), value: "20" },
    ],
  },

  // Text & Details
  {
    value: "${sampleTorrent?.name}",
    label: t("automation.conditions.torrentNameTitle"),
    group: t("automation.conditions.textDetails"),
    type: "string",
    defaultOp: "==",
    defaultValue: "''",
    placeholder: t("automation.conditions.eg2160pOrRepack"),
  },
  {
    value: "${torrent.tracker}",
    label: t("automation.conditions.trackerUrlDomain"),
    group: t("automation.conditions.textDetails"),
    type: "string",
    defaultOp: "==",
    defaultValue: "''",
    placeholder: t("automation.conditions.egTrackerexamplecom"),
  },
  {
    value: "${torrent.savePath}",
    label: t("automation.conditions.savePathDirectory"),
    group: t("automation.conditions.textDetails"),
    type: "string",
    defaultOp: "==",
    defaultValue: "''",
    placeholder: t("automation.conditions.egDownloadscomplete"),
  },

  // Custom
  {
    value: "custom",
    label: t("automation.conditions.customVariableExpression"),
    group: t("automation.conditions.customDynamic"),
    type: "custom",
    defaultOp: "==",
    defaultValue: "",
    placeholder: t("automation.conditions.egInputsminratioOrSystemfreedi"),
  },
];

const getTriggerLabels = (t: any): Record<string, string> => ({
  // Torrent Lifecycle & Goals
  TorrentAdded: t("automation.triggers.onTorrentAdded"),
  TorrentCompleted: t("automation.triggers.onDownloadCompleted"),
  RatioReached: t("automation.triggers.onRatioSeedGoalReached"),
  TorrentStarted: t("automation.triggers.onTorrentResumedStarted"),
  TorrentPaused: t("automation.triggers.onTorrentPausedStopped"),
  TorrentStalled: t("automation.triggers.onTorrentStalled"),
  SeedingTimeReached: t("automation.triggers.onSeedingTimeTargetMet"),
  HashCheckCompleted: t("automation.triggers.onHashCheckCompleted"),
  ProgressMilestone: t("automation.triggers.onProgressMilestone"),
  TorrentStatusChanged: t("automation.triggers.onTorrentStateChanged"),
  TorrentDeleted: t("automation.triggers.onTorrentDeleted"),
  TorrentError: t("automation.triggers.onTorrentError"),

  // Bandwidth & Speed
  SpeedThresholdExceeded: t("automation.triggers.onHighSpeedThresholdExceeded"),
  SpeedThresholdDropped: t("automation.triggers.onSpeedDropAlert"),
  BandwidthQuotaApproaching: t("automation.triggers.onBandwidthQuotaThreshold"),

  // Network & Security
  VpnDisconnected: t("automation.triggers.onVpnKillswitch"),
  VpnRestored: t("automation.triggers.onVpnRestored"),
  PortForwardingFailed: t("automation.triggers.onPortForwardingUpnpFailure"),
  PeerBanned: t("automation.triggers.onMaliciousBadPeerBanned"),
  TrackerUnreachable: t("automation.triggers.onAllTrackersFailed"),
  TrackerBoostApplied: t("automation.triggers.onTrackerBoostApplied"),

  // Storage & Disk
  DiskSpaceLow: t("automation.triggers.onLowDiskSpaceWarning"),
  DiskSpaceCritical: t("automation.triggers.onCriticalDiskSpaceEmergency"),
  FileMoveFailed: t("automation.triggers.onFileMovePathError"),

  // Media & Processing
  MediaEnriched: t("automation.triggers.onMediaEnriched"),
  MediaInspectionFailed: t("automation.triggers.onMediaCorruptionInspectionFai"),
  ArchiveExtracted: t("automation.triggers.onArchiveExtracted"),
  ExtractionFailed: t("automation.triggers.onExtractionFailed"),
  ArrImportCompleted: t("automation.triggers.onServarrImportCompleted"),

  // System & Lifecycle
  HealthRestored: t("automation.triggers.onHealthRestored"),
  CategoryChanged: t("automation.triggers.onCategoryChanged"),
  ApplicationStarted: t("automation.triggers.onAppStarted"),
  ApplicationUpdated: t("automation.triggers.onAppUpdated"),
  BackupCompleted: t("automation.triggers.onBackupSucceeded"),
  BackupFailed: t("automation.triggers.onBackupFailed"),
  TaskFailed: t("automation.triggers.onScheduledTaskFailed"),
  Scheduled: t("automation.triggers.scheduledInterval"),
  Manual: t("automation.triggers.manualOnly"),
});

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
      if (step.http.body?.trim()) {
        try {
          const parsed = JSON.parse(step.http.body!);
          yaml += `      body:\n`;
          for (const [k, v] of Object.entries(parsed)) {
            yaml += `        ${k}: '${String(v).replace(/'/g, "''")}'\n`;
          }
        } catch {
          yaml += `      body: '${step.http.body!.replace(/'/g, "''")}'\n`;
        }
      }
      if (step.http.register?.trim()) {
        yaml += `    register: '${step.http.register?.trim()}'\n`;
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
        } else if (act.type === "cleanFiles") {
          yaml += `      - cleanFiles: '${act.value.replace(/'/g, "''")}'\n`;
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
        } else if (act.type === "http") {
          if (!act.extra || Object.keys(act.extra).length === 0) {
            yaml += `      - http: '${act.value.replace(/'/g, "''")}'\n`;
          } else {
            yaml += `      - http:\n`;
            if (act.extra.method) yaml += `          method: '${act.extra.method}'\n`;
            if (act.extra.url || act.value) yaml += `          url: '${(act.extra.url || act.value).replace(/'/g, "''")}'\n`;

            let headers = act.extra.headers || {};
            if (act.extra.auth === "Bearer Token") headers["Authorization"] = "Bearer ${inputs.apiToken}";
            else if (act.extra.auth === "API Key (X-Api-Key)") headers["X-Api-Key"] = "${inputs.apiKey}";
            else if (act.extra.auth === "Basic Auth") headers["Authorization"] = "Basic ${inputs.basicAuth}";

            if (Object.keys(headers).length > 0) {
              yaml += `          headers:\n`;
              for (const [k, v] of Object.entries(headers)) {
                yaml += `            ${k}: '${String(v).replace(/'/g, "''")}'\n`;
              }
            }
            if (act.extra.json) yaml += `          json: true\n`;
            if (act.extra.body) yaml += `          body: '${act.extra.body.replace(/'/g, "''")}'\n`;
            if (act.extra.timeoutSeconds) yaml += `          timeoutSeconds: ${act.extra.timeoutSeconds}\n`;
            if (act.extra.allowInsecure) yaml += `          allowInsecure: true\n`;
            if (act.extra.continueOnError) yaml += `          continueOnError: true\n`;
            if (act.extra.register) yaml += `          register: '${act.extra.register}'\n`;
          }
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
          if (act.extra?.deleteData) {
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
        conditionLeft: "${sampleTorrent?.size}",
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
        conditionLeft: "${sampleTorrent?.size}",
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
          currentStep.conditionLeft = parts[0]?.trim() || "${sampleTorrent?.size}";
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
    } else if (currentStep && inActions && trimmed.startsWith("- cleanFiles:")) {
      const v = trimmed.match(/- cleanFiles:\s*['"]?([^'"]+)['"]?/);
      if (v) currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "cleanFiles", value: v[1] });
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
    } else if (currentStep && inActions && trimmed.startsWith("- http:")) {
      const v = trimmed.match(/- http:\s*['"]?([^'"]+)['"]?/);
      if (v) {
        currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "http", value: v[1], extra: {} });
      } else {
        currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "http", value: "", extra: { method: "POST", url: "", json: true } });
      }
    } else if (currentStep && inActions && trimmed.startsWith("url:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/url:\s*['"]?([^'"]+)['"]?/);
      if (v) {
        currentStep.actions[currentStep.actions.length - 1].value = v[1];
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.url = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("method:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/method:\s*['"]?([^'"]+)['"]?/);
      if (v) {
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.method = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("body:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/body:\s*['"]?([^'"]+)['"]?/);
      if (v) {
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.body = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("register:") && currentStep.actions.length > 0 && currentStep.actions[currentStep.actions.length - 1].type === "http") {
      const v = trimmed.match(/register:\s*['"]?([^'"]+)['"]?/);
      if (v) {
        if (!currentStep.actions[currentStep.actions.length - 1].extra) currentStep.actions[currentStep.actions.length - 1].extra = {};
        currentStep.actions[currentStep.actions.length - 1].extra!.register = v[1];
      }
    } else if (currentStep && inActions && trimmed.startsWith("- pause:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "pause", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- resume:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "resume", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- recheck:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "recheck", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- reannounce:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "reannounce", value: "" });
    } else if (currentStep && inActions && trimmed.startsWith("- remove:")) {
      currentStep.actions.push({ id: `act-${Date.now()}-${Math.random()}`, type: "remove", value: "", deleteData: undefined });
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
          conditionLeft: "${sampleTorrent?.size}",
          conditionOp: ">",
          conditionRight: "0",
          hasHttp: false,
          http: { method: "POST", url: "", json: true, body: "{}", register: "" },
          actions: [{ id: "act-1", type: "addTag", value: "Processed" }],
        },
      ];
}


function tGroup(t: any, label: string) {
  if (label.includes("Tags & Categories")) return t("automation.groups.tagsAndCategories", { defaultValue: label });
  if (label.includes("Torrent State")) return t("automation.groups.torrentState", { defaultValue: label });
  if (label.includes("Limits & Priority")) return t("automation.groups.limitsAndPriority", { defaultValue: label });
  if (label.includes("Storage & Files")) return t("automation.groups.storageAndFiles", { defaultValue: label });
  if (label.includes("Trackers & Peers")) return t("automation.groups.trackersAndPeers", { defaultValue: label });
  if (label.includes("Alerts & Servarr")) return t("automation.groups.notificationsAndAlerts", { defaultValue: label });
  if (label.includes("Scripting & Flow Control")) return t("automation.groups.controlFlow", { defaultValue: label });
  if (label.includes("Media Post-Processing")) return t("automation.groups.mediaPostProcessing", { defaultValue: label });
  if (label.includes("HTTP Request")) return t("automation.groups.httpRequest", { defaultValue: label });
  if (label.includes("Commands")) return t("automation.groups.commands", { defaultValue: label });
  return t(label, { defaultValue: label });
}

function tTrigger(t: any, key: string, defaultLabel: string) {
  const map: any = {
    TorrentAdded: "torrentAdded",
    TorrentCompleted: "torrentFinished",
    RatioReached: "ratioReached",
    SeedingTimeReached: "timeLimitReached",
    TrackerUnreachable: "trackerError",
    SpeedThresholdDropped: "speedDrop",
    DiskSpaceLow: "diskSpaceLow",
    Scheduled: "hourlySchedule",
    Manual: "manual"
  };
  if (map[key]) return t(["automation", "triggers", map[key]].join("."), { defaultValue: defaultLabel });
  return t(["automation", "triggers", key].join("."), { defaultValue: defaultLabel });
}

function tAction(t: any, type: string, field: "label" | "placeholder" | "extraHelp", defaultText?: string) {
  if (!defaultText) return defaultText;
  return t(["automation", "actions", type, field].join("."), { defaultValue: defaultText });
}

function tCommand(t: any, name: string, defaultDesc: string) {
  return t(["automation", "commands", name].join("."), { defaultValue: defaultDesc });
}

export function AutomationPage() {
  const { t } = useTranslation();
  const confirm = useConfirm();

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
        conditionLeft: "${sampleTorrent?.size}",
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

  async function handleDeleteScript(id: number) {
    const targetScript = scriptList.find((s) => s.id === id);
    const ok = await confirm({
      title: "Delete Automation Pipeline",
      message: `Are you sure you want to delete "${targetScript?.name || "this automation pipeline"}"? This action cannot be undone.`,
      confirmText: "Delete Pipeline",
      danger: true,
    });
    if (ok) {
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
            <span>⚡</span> {t("automation.ui.automationPipelinesMarketplace")}</h1>
          <p style={{ color: "var(--text-muted, #888)", margin: "0.25rem 0 0 0", fontSize: "0.9rem" }}>
            {t("automation.ui.visualDraganddropWorkflowBuild")}</p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem" }}>
          <button
            className={`btn ${activeTab === "scripts" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("scripts")}
          >
            {t("automation.tabs.visual")} ({scriptList.length})
          </button>
          <button
            className={`btn ${activeTab === "history" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("history")}
          >
            {t("automation.tabs.logs")}
          </button>
          <button
            className={`btn ${activeTab === "marketplace" ? "btn-primary" : "btn-secondary"}`}
            onClick={() => setActiveTab("marketplace")}
          >
            {t("automation.tabs.marketplace")} ({templateList.length})
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
                placeholder={t("automation.ui.searchPipelines")}
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
                <option value="All">{t("automation.ui.allTriggers")}</option>
                {Object.entries(getTriggerLabels(t)).map(([k, label]) => (
                  <option key={k} value={k}>{label}</option>
                ))}
              </select>
            </div>

            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button className="btn btn-primary" onClick={() => openNewScript("Yaml")}>
                ✨ + {t("automation.tabs.visual")}
              </button>
              <button className="btn btn-secondary" onClick={() => openNewScript("JavaScript")}>
                {t("automation.ui.javascriptScript")}</button>
            </div>
          </div>

          {loadingScripts ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
              {t("automation.ui.loadingAutomationPipelines")}</div>
          ) : filteredScripts.length === 0 ? (
            <div className="panel" style={{ padding: "3rem", textAlign: "center" }}>
              <div style={{ fontSize: "2.5rem", marginBottom: "0.5rem" }}>{t("automation.ui.text1")}</div>
              <h3 style={{ margin: "0 0 0.5rem 0" }}>{t("automation.ui.noAutomationPipelinesFound")}</h3>
              <p style={{ color: "var(--text-muted)", maxWidth: "480px", margin: "0 auto 1.5rem auto" }}>
                {t("automation.ui.createAutomatedActionsForWhen")}</p>
              <div style={{ display: "flex", gap: "0.5rem", justifyContent: "center" }}>
                <button className="btn btn-primary" onClick={() => openNewScript("Yaml")}>
                  {t("automation.ui.buildVisualPipeline")}</button>
                <button className="btn btn-secondary" onClick={() => setActiveTab("marketplace")}>
                  {t("automation.ui.browseMarketplace")}</button>
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
                    borderLeft: `4px solid ${script.isEnabled ? "var(--accent, #3b82f6)" : "var(--border)"}`,
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
                        {tTrigger(t, script.trigger.toString(), getTriggerLabels(t)[script.trigger.toString()]) || script.trigger.toString()}
                      </span>
                      {script.targetCategories && script.targetCategories.length > 0 && (
                        <span className="badge" style={{ backgroundColor: "rgba(168, 85, 247, 0.15)", color: "#c084fc" }}>
                          {t("automation.ui.text2")}{script.targetCategories.join(", ")}
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
                        borderTop: "1px solid var(--border)",
                        paddingTop: "0.75rem",
                        marginBottom: "0.75rem",
                      }}
                    >
                      <span>
                        {t("automation.ui.lastRun")}{" "}
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
                          title={t("automation.ui.viewExecutionLog")}
                        >
                          {t("automation.ui.logs")}</button>
                      )}
                      <button
                        className="btn btn-sm btn-secondary"
                        onClick={() => handleRunNow(script.id)}
                        disabled={isRunningId === script.id}
                        title={t("automation.ui.triggerRunImmediately")}
                      >
                        {isRunningId === script.id ? "⏳ Running..." : "▶️ Run"}
                      </button>
                      <button className="btn btn-sm btn-secondary" onClick={() => openEditScript(script)}>
                        {t("automation.ui.edit")}</button>
                      <button
                        className="btn btn-sm btn-danger"
                        onClick={() => handleDeleteScript(script.id)}
                        title={t("automation.ui.deleteScript")}
                      >
                        {t("automation.ui.text3")}</button>
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
            <h2 style={{ fontSize: "1.2rem", margin: "0 0 1rem 0" }}>{t("automation.ui.pipelineExecutionHistory")}</h2>
            <p style={{ color: "var(--text-muted)", fontSize: "0.9rem", marginBottom: "1.5rem" }}>
              {t("automation.ui.detailedExecutionTracesAndStep")}</p>

            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.9rem" }}>
              <thead>
                <tr style={{ borderBottom: "1px solid var(--border)", textAlign: "left" }}>
                  <th style={{ padding: "0.75rem 0.5rem" }}>{t("automation.ui.status")}</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>{t("automation.ui.pipelineName")}</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>{t("automation.ui.trigger")}</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>{t("automation.ui.executedAt")}</th>
                  <th style={{ padding: "0.75rem 0.5rem" }}>{t(["automation", "ui", "actions"].join("."))}</th>
                </tr>
              </thead>
              <tbody>
                {scriptList.filter((s) => s.lastExecutedAt).length === 0 ? (
                  <tr>
                    <td colSpan={5} style={{ textAlign: "center", padding: "2rem", color: "var(--text-muted)" }}>
                      {t("automation.ui.noPipelineExecutionRunsRecorde")}</td>
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
                          <span className="badge">{tTrigger(t, s.trigger.toString(), getTriggerLabels(t)[s.trigger.toString()]) || s.trigger.toString()}</span>
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
                            {t("automation.ui.traceInspector")}</button>
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
              placeholder={t("automation.ui.searchCommunityTemplates")}
              className="input"
              style={{ width: "240px" }}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          {loadingTemplates ? (
            <div className="panel" style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
              {t("automation.ui.loadingMarketplaceCatalog")}</div>
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
                      {t("automation.ui.by")}<span style={{ fontWeight: 600, color: "var(--text-primary)" }}>{template.author}</span> •{" "}
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
                    {t("automation.ui.installPipeline")}</button>
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
                    ✨ {t("automation.tabs.visual")}
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
                    {t("automation.ui.codeYamlView")}</button>
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
                  <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>{t("automation.ui.pipelineName")}</label>
                  <input
                    type="text"
                    className="form-control"
                    value={editingScript.name || ""}
                    onChange={(e) => setEditingScript({ ...editingScript, name: e.target.value })}
                    placeholder={t("automation.ui.eg4kMovieAutozapBackup")}
                  />
                </div>

                <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                  <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>{t("automation.ui.triggerEvent")}</label>
                  <select
                    className="form-control"
                    value={editingScript.trigger?.toString()}
                    onChange={(e) => setEditingScript({ ...editingScript, trigger: e.target.value as AutomationTrigger })}
                  >
                    {Object.entries(getTriggerLabels(t)).map(([k, label]) => (
                      <option key={k} value={k}>{label}</option>
                    ))}
                  </select>
                </div>

                <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                  <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>{t("automation.ui.engineFormat")}</label>
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
                    <option value="Yaml">{t("automation.ui.yamlVisualPipelineDsl")}</option>
                    <option value="JavaScript">{t("automation.ui.javascriptSandboxedJint")}</option>
                  </select>
                </div>
              </div>

              {/* Form Row 2: Description */}
              <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem", marginBottom: "1.25rem" }}>
                <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>{t("automation.ui.description")}</label>
                <input
                  type="text"
                  className="form-control"
                  value={editingScript.description || ""}
                  onChange={(e) => setEditingScript({ ...editingScript, description: e.target.value })}
                  placeholder={t("automation.ui.summaryOfWhatThisAutomation")}
                />
              </div>

              {/* EDITOR MODE 1: VISUAL PIPELINE BUILDER */}
              {editorMode === "visual" && (
                <div style={{ marginBottom: "1rem" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.75rem" }}>
                    <label style={{ fontSize: "0.95rem", fontWeight: 700, color: "var(--accent)" }}>
                      {t("automation.ui.pipelineSteps")}{visualSteps.length})
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
                          conditionLeft: "${sampleTorrent?.size}",
                          conditionOp: ">",
                          conditionRight: "1000000000",
                          hasHttp: false,
                          http: { method: "POST", url: "", json: true, body: "{}", register: "" },
                          actions: [{ id: `act-${Date.now()}`, type: "addTag", value: "Auto-Tagged" }],
                        };
                        updateVisualSteps([...visualSteps, newStep]);
                      }}
                    >
                      {t("automation.ui.addStep")}</button>
                  </div>

                  {visualSteps.map((step, stepIdx) => (
                    <div
                      key={step.id}
                      className="panel"
                      style={{
                        marginBottom: "1rem",
                        backgroundColor: "var(--bg-primary, #1e1b18)",
                        border: "1px solid var(--border-light)",
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
                            placeholder={t("automation.ui.stepName")}
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
                            {t("automation.ui.text4")}</button>
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
                            {t("automation.ui.text5")}</button>
                          <button
                            type="button"
                            className="btn btn-sm btn-danger"
                            style={{ padding: "0.3rem 0.6rem" }}
                            onClick={() => {
                              const copy = visualSteps.filter((_, idx) => idx !== stepIdx);
                              updateVisualSteps(copy);
                            }}
                          >
                            {t("automation.ui.text3")}</button>
                        </div>
                      </div>

                      {/* Condition Builder */}
                      <div
                        style={{
                          backgroundColor: "var(--bg-secondary, #2a2620)",
                          border: "1px solid var(--border-light)",
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
                          {t("automation.ui.onlyRunThisStepIf")}</label>

                        {step.conditionEnabled && (() => {
                          const propDef = getConditionProperties(t).find((p) => p.value === step.conditionLeft) || {
                            value: "custom",
                            label: "Custom",
                            group: "Custom / Dynamic",
                            type: "custom" as const,
                            defaultOp: "==" as const,
                            defaultValue: "",
                          };

                          const isCustomLeft = !getConditionProperties(t).some((p) => p.value === step.conditionLeft && p.value !== "custom");

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
                              <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", width: "100%" }}>
                                {/* Left Property Select (L-Value) */}
                                <div style={{ width: isCustomLeft ? "260px" : "220px", flexShrink: 0, display: "flex", flexDirection: "column", gap: "0.2rem" }}>
                                  <select
                                    className="form-control"
                                    style={{ width: "100%", fontWeight: 500 }}
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
                                        const found = getConditionProperties(t).find((p) => p.value === newVal);
                                        if (found) {
                                          copy[stepIdx].conditionOp = found.defaultOp;
                                          copy[stepIdx].conditionRight = found.defaultValue;
                                        }
                                      }
                                      updateVisualSteps(copy);
                                    }}
                                  >
                                    {Array.from(new Set(getConditionProperties(t).map((p) => p.group))).map((groupName) => (
                                      <optgroup key={groupName} label={groupName}>
                                        {getConditionProperties(t).filter((p) => p.group === groupName).map((p) => (
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
                                      style={{ width: "100%", fontSize: "0.8rem", padding: "0.25rem 0.5rem" }}
                                      value={step.conditionLeft}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        copy[stepIdx].conditionLeft = e.target.value;
                                        updateVisualSteps(copy);
                                      }}
                                      placeholder={t("automation.ui.egInputsmyproperty")}
                                    />
                                  )}
                                </div>

                                {/* Operator Select (Comparison) */}
                                <div style={{ width: "150px", flexShrink: 0 }}>
                                  <select
                                    className="form-control"
                                    style={{ width: "100%", textAlign: "center", fontWeight: 700 }}
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
                                </div>

                                {/* Right Value (R-Value): Type-Aware Presets, Numeric Chips or Custom Input */}
                                <div style={{ flex: 1, minWidth: 0, display: "flex", gap: "0.35rem", alignItems: "center" }}>
                                  {presetOptions && !isCustomRight ? (
                                    <div style={{ display: "flex", gap: "0.35rem", width: "100%", alignItems: "center" }}>
                                      <select
                                        className="form-control"
                                        style={{ width: "100%", fontWeight: 500 }}
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
                                        <option value="__custom__">{t("automation.ui.customExpressionVariable")}</option>
                                      </select>
                                    </div>
                                  ) : propDef.type === "number" ? (
                                    <div style={{ display: "flex", flexDirection: "column", gap: "0.3rem", width: "100%" }}>
                                      <div style={{ display: "flex", gap: "0.35rem", alignItems: "center", width: "100%" }}>
                                        <input
                                          type="text"
                                          className="form-control"
                                          style={{ flex: 1, minWidth: 0 }}
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
                                              flexShrink: 0,
                                            }}
                                          >
                                            {numericLiveHint}
                                          </span>
                                        )}
                                      </div>
                                      {propDef.presets && propDef.presets.length > 0 && (
                                        <div style={{ display: "flex", gap: "0.3rem", flexWrap: "wrap", alignItems: "center" }}>
                                          <span style={{ fontSize: "0.7rem", color: "var(--text-muted)", marginRight: "0.15rem" }}>{t("automation.ui.quickSet")}</span>
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
                                        style={{ flex: 1, minWidth: 0 }}
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
                                          title={t("automation.ui.switchBackToPresetsDropdown")}
                                          style={{ padding: "0.35rem 0.6rem", fontSize: "0.75rem", whiteSpace: "nowrap", flexShrink: 0 }}
                                          onClick={() => {
                                            const copy = [...visualSteps];
                                            copy[stepIdx].conditionRight = presetOptions![0].value;
                                            updateVisualSteps(copy);
                                          }}
                                        >
                                          {t("automation.ui.presets")}</button>
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
                          <span style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--text-secondary)" }}>{t("automation.ui.stepActions")}</span>
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
                              {t("automation.ui.tag")}</button>
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
                              {t("automation.ui.category")}</button>
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
                              {t("automation.ui.limit")}</button>
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
                              {t("automation.ui.alert")}</button>
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
                              {t("automation.ui.servarr")}</button>
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
                              {t("automation.ui.command")}</button>
                          </div>
                        </div>

                        {step.actions.length === 0 ? (
                          <div style={{ fontSize: "0.825rem", color: "var(--text-muted)", fontStyle: "italic", padding: "0.6rem 0" }}>
                            {t("automation.ui.noActionsAddedClickAny")}</div>
                        ) : (
                          step.actions.map((act, actIdx) => {
                            const actDef = getActionGroups(t).flatMap((g) => g.items).find((i) => i.type === act.type);
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
                                  border: "1px solid var(--border-light)",
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
                                      copy[stepIdx].actions[actIdx].value = getCommonCommands(t)[0].name;
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
                                  {getActionGroups(t).map((group) => (
                                    <optgroup key={group.group} label={tGroup(t, group.group)}>
                                      {group.items.map((item) => (
                                        <option key={item.type} value={item.type}>{tAction(t, item.type, "label", item.label)}</option>
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
                                    {getCommonCommands(t).map((cmd) => (
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
                                        {t("automation.ui.text2")}{cat.name}
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
                                    <option value="High">{t("automation.ui.highPriority")}</option>
                                    <option value="Normal">{t("automation.ui.normalPriority")}</option>
                                    <option value="Low">{t("automation.ui.lowPriority")}</option>
                                    <option value="DoNotDownload">{t("automation.ui.doNotDownloadSkip")}</option>
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
                                    <option value="true">{t("automation.ui.enabledTrue")}</option>
                                    <option value="false">{t("automation.ui.disabledFalse")}</option>
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
                                    <option value="">{t("automation.ui.allConnectedServarrInstances")}</option>
                                    <option value="Sonarr">{t("automation.ui.sonarrTvShows")}</option>
                                    <option value="Radarr">{t("automation.ui.radarrMovies")}</option>
                                    <option value="Lidarr">{t("automation.ui.lidarrMusic")}</option>
                                    <option value="Readarr">{t("automation.ui.readarrBooks")}</option>
                                    <option value="Whisparr">{t("automation.ui.whisparrAdult")}</option>
                                  </select>
                                ) : act.type === "remove" ? (
                                  <label style={{ flex: 1, display: "flex", alignItems: "center", gap: "0.5rem", fontSize: "0.85rem", cursor: "pointer", color: "var(--color-danger, #ff6b6b)" }}>
                                    <input
                                      type="checkbox"
                                      checked={act.extra?.deleteData || false}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        copy[stepIdx].actions[actIdx].extra!.deleteData = e.target.checked;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    {t("automation.ui.alsoPermanentlyDeleteDownloade")}</label>
                                ) : act.type === "pause" || act.type === "resume" || act.type === "recheck" || act.type === "reannounce" || act.type === "boostTracker" ? (
                                  <span style={{ flex: 1, fontSize: "0.85rem", color: "var(--text-muted)", paddingLeft: "0.25rem" }}>
                                    {t("automation.ui.autoappliesToActiveSwarmTorren")}</span>
                                ) : act.type === "http" ? (
                                  <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.5rem" }}>
                                    <div style={{ display: "flex", gap: "0.5rem" }}>
                                      <select
                                        className="form-control"
                                        style={{ width: "100px", color: act.extra?.method === "GET" ? "#3b82f6" : act.extra?.method === "POST" ? "#10b981" : act.extra?.method === "PUT" ? "#f59e0b" : act.extra?.method === "DELETE" ? "#ef4444" : "#8b5cf6", fontWeight: "bold" }}
                                        value={act.extra?.method || "POST"}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.method = e.target.value;
                                          updateVisualSteps(copy);
                                        }}
                                      >
                                        <option value="GET" style={{ color: "#3b82f6" }}>{t("automation.ui.get")}</option>
                                        <option value="POST" style={{ color: "#10b981" }}>{t("automation.ui.post")}</option>
                                        <option value="PUT" style={{ color: "#f59e0b" }}>{t("automation.ui.put")}</option>
                                        <option value="DELETE" style={{ color: "#ef4444" }}>{t("automation.ui.delete")}</option>
                                        <option value="PATCH" style={{ color: "#8b5cf6" }}>{t("automation.ui.patch")}</option>
                                      </select>
                                      <input
                                        type="text"
                                        className="form-control"
                                        style={{ flex: 1 }}
                                        value={act.extra?.url || act.value || ""}
                                        placeholder={t("automation.ui.httpsexternalservicecomapiv1we")}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          copy[stepIdx].actions[actIdx].value = e.target.value;
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.url = e.target.value;
                                          updateVisualSteps(copy);
                                        }}
                                      />
                                    </div>
                                    <textarea
                                      className="form-control"
                                      style={{ minHeight: "80px", fontFamily: "monospace", fontSize: "0.85rem" }}
                                      placeholder={`{"event": "complete", "torrent": "\${sampleTorrent?.name}", "size": \${sampleTorrent?.size}}`}
                                      value={act.extra?.body || ""}
                                      onChange={(e) => {
                                        const copy = [...visualSteps];
                                        if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                        copy[stepIdx].actions[actIdx].extra!.body = e.target.value;
                                        updateVisualSteps(copy);
                                      }}
                                    />
                                    <div style={{ display: "flex", gap: "1rem", fontSize: "0.85rem", alignItems: "center", flexWrap: "wrap" }}>
                                      <button
                                        type="button"
                                        className="btn btn-sm btn-outline"
                                        title={t("automation.ui.insertTorrentJsonPayload")}
                                        onClick={(e) => {
                                          e.preventDefault();
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.body = '{\n  "event": "complete",\n  "torrent": "${sampleTorrent?.name}",\n  "size": ${sampleTorrent?.size},\n  "hash": "${torrent.infoHash}"\n}';
                                          updateVisualSteps(copy);
                                        }}
                                      >
                                        {t("automation.ui.template")}</button>
                                      <select
                                        className="form-control"
                                        style={{ width: "130px", fontSize: "0.8rem", padding: "0.2rem 0.5rem" }}
                                        value=""
                                        onChange={(e) => {
                                          const v = e.target.value;
                                          if (!v) return;
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          if (!copy[stepIdx].actions[actIdx].extra!.headers) copy[stepIdx].actions[actIdx].extra!.headers = {};
                                          if (v === "bearer") copy[stepIdx].actions[actIdx].extra!.headers["Authorization"] = "Bearer ${inputs.apiToken}";
                                          if (v === "apikey") copy[stepIdx].actions[actIdx].extra!.headers["X-Api-Key"] = "${inputs.apiKey}";
                                          if (v === "basic") copy[stepIdx].actions[actIdx].extra!.headers["Authorization"] = "Basic ${inputs.basicAuth}";
                                          updateVisualSteps(copy);
                                        }}
                                      >
                                        <option value="">{t("automation.ui.authPreset")}</option>
                                        <option value="bearer">{t("automation.ui.bearerToken")}</option>
                                        <option value="apikey">{t("automation.ui.apiKey")}</option>
                                        <option value="basic">{t("automation.ui.basicAuth")}</option>
                                      </select>
                                      <label style={{ display: "flex", alignItems: "center", gap: "0.3rem", cursor: "pointer" }}>
                                        <input
                                          type="checkbox"
                                          checked={act.extra?.allowInsecure || false}
                                          onChange={(e) => {
                                            const copy = [...visualSteps];
                                            if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                            copy[stepIdx].actions[actIdx].extra!.allowInsecure = e.target.checked;
                                            updateVisualSteps(copy);
                                          }}
                                        /> {t("automation.ui.allowInsecure")}</label>
                                      <label style={{ display: "flex", alignItems: "center", gap: "0.3rem", cursor: "pointer" }}>
                                        <input
                                          type="checkbox"
                                          checked={act.extra?.continueOnError || false}
                                          onChange={(e) => {
                                            const copy = [...visualSteps];
                                            if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                            copy[stepIdx].actions[actIdx].extra!.continueOnError = e.target.checked;
                                            updateVisualSteps(copy);
                                          }}
                                        /> {t("automation.ui.continueOnError")}</label>
                                      <input
                                        type="text"
                                        className="form-control"
                                        style={{ width: "120px", fontSize: "0.8rem", padding: "0.2rem 0.5rem" }}
                                        placeholder={t("automation.ui.registerVariable")}
                                        value={act.extra?.register || ""}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.register = e.target.value;
                                          updateVisualSteps(copy);
                                        }}
                                      />
                                      <input
                                        type="number"
                                        className="form-control"
                                        style={{ width: "80px", fontSize: "0.8rem", padding: "0.2rem 0.5rem" }}
                                        placeholder={t("automation.ui.timeoutS")}
                                        value={act.extra?.timeoutSeconds || ""}
                                        onChange={(e) => {
                                          const copy = [...visualSteps];
                                          if (!copy[stepIdx].actions[actIdx].extra) copy[stepIdx].actions[actIdx].extra = {};
                                          copy[stepIdx].actions[actIdx].extra!.timeoutSeconds = parseInt(e.target.value, 10);
                                          updateVisualSteps(copy);
                                        }}
                                      />
                                    </div>
                                  </div>
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
                                  title={t("automation.ui.deleteAction")}
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
                      {t("automation.ui.helpers")}<code>{t("automation.ui.systemruncommand")}</code>, <code>{t("automation.ui.apiget")}</code>, <code>{t("automation.ui.torrentaddtag")}</code>
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
                      border: "1px solid var(--border-light)",
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
                    <h4 style={{ margin: "0 0 0.25rem 0", fontSize: "0.95rem", fontWeight: 600 }}>{t("automation.ui.liveDryRunInspector")}</h4>
                    <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                      {t("automation.ui.testPipelineExecutionLogicSafe")}</span>
                  </div>

                  <div style={{ display: "flex", gap: "0.6rem", alignItems: "center" }}>
                    <select
                      className="form-control"
                      style={{ width: "320px", fontSize: "0.825rem" }}
                      value={testTorrentId}
                      onChange={(e) => setTestTorrentId(e.target.value ? Number(e.target.value) : undefined)}
                    >
                      <option value="">{t("automation.ui.sampleTorrentBigBuckBunny")}</option>
                      {(torrents || []).map((torr) => (
                        <option key={torr.id} value={torr.id}>{torr.name} ({(torr.totalSize / (1024 * 1024 * 1024)).toFixed(2)} {t("automation.ui.gb")}</option>
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
                      <span>{t("automation.ui.duration")}{testResult.executionTimeMs}{t("automation.ui.ms")}</span>
                    </div>

                    {testResult.tagsToAdd.length > 0 && (
                      <div style={{ marginBottom: "0.25rem" }}><strong>{t("automation.ui.tagsAdded")}</strong> {testResult.tagsToAdd.join(", ")}</div>
                    )}
                    {testResult.newCategory && (
                      <div style={{ marginBottom: "0.25rem" }}><strong>{t("automation.ui.newCategory")}</strong> {testResult.newCategory}</div>
                    )}
                    {(testResult as any).shouldRecheck && (
                      <div style={{ marginBottom: "0.25rem" }}><strong>{t("automation.ui.torrentAction")}</strong> {t("automation.ui.forceHashRecheck")}</div>
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
                {t("automation.ui.cancel")}</button>
              <button type="button" className="btn btn-primary" style={{ padding: "0.5rem 1.25rem", fontWeight: 600 }} onClick={handleSaveScript}>
                {t("automation.ui.savePipeline")}</button>
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
                <h3 style={{ margin: 0, fontSize: "1.2rem", fontWeight: 700 }}>{t("automation.ui.pipelineTrace")}{viewingLog.name}</h3>
                <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                  {t("automation.ui.trigger1")}{viewingLog.trigger} {t("automation.ui.executed")}{viewingLog.time || "Recently"}
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
                {t("automation.ui.status1")}{viewingLog.status}
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
                border: "1px solid var(--border)",
              }}
            >
              {viewingLog.log}
            </pre>

            <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "1.25rem" }}>
              <button className="btn btn-secondary" style={{ padding: "0.45rem 1.25rem" }} onClick={() => setLogModalOpen(false)}>{t("automation.ui.close")}</button>
            </div>
          </div>
        </div>
      )}

      {/* TEMPLATE INSTALL MODAL */}
      {installModalOpen && selectedTemplate && (
        <div className="modal-overlay">
          <div className="modal panel" style={{ width: "100%", maxWidth: "600px", padding: "1.75rem", backgroundColor: "var(--bg-secondary)" }}>
            <h3 style={{ margin: "0 0 0.5rem 0", fontSize: "1.25rem", fontWeight: 700 }}>{t("automation.ui.installCommunityPipeline")}</h3>
            <p style={{ color: "var(--text-muted)", fontSize: "0.85rem", marginBottom: "1.25rem", lineHeight: "1.4" }}>
              {selectedTemplate.description}
            </p>

            <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem", marginBottom: "1rem" }}>
              <label style={{ fontSize: "0.825rem", fontWeight: 600, color: "var(--text-secondary)" }}>{t("automation.ui.pipelineCustomName")}</label>
              <input
                type="text"
                className="form-control"
                value={customInstallName}
                onChange={(e) => setCustomInstallName(e.target.value)}
              />
            </div>

            {selectedTemplate.inputFields && selectedTemplate.inputFields.length > 0 && (
              <div style={{ borderTop: "1px solid var(--border)", paddingTop: "1rem", marginTop: "1rem" }}>
                <h4 style={{ fontSize: "0.95rem", margin: "0 0 0.75rem 0", fontWeight: 600 }}>{t("automation.ui.pipelineConfigurationParameter")}</h4>
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
              <button className="btn btn-secondary" style={{ padding: "0.45rem 1.25rem" }} onClick={() => setInstallModalOpen(false)}>{t("automation.ui.cancel")}</button>
              <button className="btn btn-primary" style={{ padding: "0.45rem 1.25rem", fontWeight: 600 }} onClick={handleInstallTemplate}>{t("automation.ui.installPipeline1")}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
