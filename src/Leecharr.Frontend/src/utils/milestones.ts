import type { Torrent, SeedingStats } from "../api/types";
import { formatBytes, formatRatio, formatSeconds } from "./formatters";

export interface MilestoneBadge {
  id: string;
  name: string;
  category: "ratio" | "volume" | "longevity" | "guardian" | "speed";
  icon: string;
  tier: "bronze" | "silver" | "gold" | "diamond";
  description: string;
  progress: number; // 0 to 100
  isUnlocked: boolean;
  currentValueText: string;
  targetValueText: string;
}

export interface HnrStatus {
  isCleared: boolean;
  requiredSeconds: number;
  seededSeconds: number;
  progressPercent: number;
  remainingSeconds: number;
  label: string;
}

export interface TrackerBufferSummary {
  tracker: string;
  torrentCount: number;
  totalUploaded: number;
  totalDownloaded: number;
  ratio: number;
  bufferBytes: number; // Safe download buffer before dropping below 1.0
  estimatedPointsPerHour: number;
}

/**
 * Calculates HNR (Hit & Run) minimum seeding clearance.
 * Standard private tracker rule: Seed for 72 hours (259,200s) or reach 1.0 ratio.
 */
export function calculateHnrStatus(
  torrent: Torrent,
  requiredHours: number = 72,
): HnrStatus {
  const requiredSeconds = requiredHours * 3600;
  const seededSeconds = torrent.seedingTime || 0;
  const ratio = torrent.ratio || 0;

  if (ratio >= 1.0 || seededSeconds >= requiredSeconds) {
    return {
      isCleared: true,
      requiredSeconds,
      seededSeconds,
      progressPercent: 100,
      remainingSeconds: 0,
      label: ratio >= 1.0 ? "Cleared (1.0+ Ratio)" : "Cleared (72h Met)",
    };
  }

  const progressPercent = Math.min(
    99.9,
    (seededSeconds / requiredSeconds) * 100,
  );
  const remainingSeconds = Math.max(0, requiredSeconds - seededSeconds);

  return {
    isCleared: false,
    requiredSeconds,
    seededSeconds,
    progressPercent,
    remainingSeconds,
    label: `${formatSeconds(seededSeconds)} / ${requiredHours}h (${progressPercent.toFixed(0)}%)`,
  };
}

/**
 * Computes individual torrent badges (e.g. Swarm Guardian, Diamond Ratio, Century Seeder).
 */
export function getTorrentBadges(
  torrent: Torrent,
): { label: string; icon: string; title: string; color: string }[] {
  const badges: {
    label: string;
    icon: string;
    title: string;
    color: string;
  }[] = [];

  // Swarm Guardian: <= 2 total seeders
  if ((torrent.seeders ?? 0) <= 2 && torrent.status === "Seeding") {
    badges.push({
      label: "Guardian",
      icon: "🛡️",
      title: `Swarm Guardian: Only ${torrent.seeders ?? 0} seeders in the world! You are keeping this swarm alive.`,
      color: "#e67e22",
    });
  }

  // Century Seeder: 100+ days (8,640,000s)
  if (torrent.seedingTime >= 8640000) {
    badges.push({
      label: "100d+",
      icon: "👑",
      title: `Century Seeder: Seeded for ${formatSeconds(torrent.seedingTime)} continuously!`,
      color: "#9b59b6",
    });
  } else if (torrent.seedingTime >= 2592000) {
    // 30+ days
    badges.push({
      label: "30d+",
      icon: "💎",
      title: `Perma-Seeder: Seeded for ${formatSeconds(torrent.seedingTime)}!`,
      color: "#3498db",
    });
  }

  // Ratio Badges
  if (torrent.ratio >= 10.0) {
    badges.push({
      label: `${torrent.ratio.toFixed(1)}x`,
      icon: "💎",
      title: `Diamond Ratio: ${formatRatio(torrent.ratio)}`,
      color: "#2ecc71",
    });
  } else if (torrent.ratio >= 5.0) {
    badges.push({
      label: `${torrent.ratio.toFixed(1)}x`,
      icon: "🥇",
      title: `Gold Ratio: ${formatRatio(torrent.ratio)}`,
      color: "#f1c40f",
    });
  } else if (torrent.ratio >= 2.0) {
    badges.push({
      label: `${torrent.ratio.toFixed(1)}x`,
      icon: "🥈",
      title: `Silver Ratio: ${formatRatio(torrent.ratio)}`,
      color: "#bdc3c7",
    });
  } else if (torrent.ratio >= 1.0) {
    badges.push({
      label: "1.0x",
      icon: "🥉",
      title: `Bronze Target Ratio met: ${formatRatio(torrent.ratio)}`,
      color: "#cd7f32",
    });
  }

  return badges;
}

/**
 * Calculates comprehensive user achievements across the whole library.
 */
export function calculateAchievements(
  torrents: Torrent[] | undefined,
  stats: SeedingStats | undefined,
): {
  badges: MilestoneBadge[];
  unlockedCount: number;
  totalCount: number;
  overallLevel: number;
  rankTitle: string;
  totalSwarmGuardians: Torrent[];
} {
  const tList = torrents ?? [];
  const totalUploaded = stats?.totalUploaded ?? 0;
  const maxRatio =
    tList.length > 0 ? Math.max(...tList.map((t) => t.ratio || 0)) : 0;
  const maxSeedTime =
    tList.length > 0 ? Math.max(...tList.map((t) => t.seedingTime || 0)) : 0;
  const swarmGuardians = tList.filter(
    (t) => (t.seeders ?? 0) <= 2 && t.status === "Seeding",
  );

  const badges: MilestoneBadge[] = [
    {
      id: "ratio_1",
      name: "First Seed",
      category: "ratio",
      icon: "🥉",
      tier: "bronze",
      description: "Reach a 1.0x ratio on any torrent in your library.",
      progress: Math.min(100, (maxRatio / 1.0) * 100),
      isUnlocked: maxRatio >= 1.0,
      currentValueText: formatRatio(maxRatio),
      targetValueText: "1.00",
    },
    {
      id: "ratio_5",
      name: "High Multiplier",
      category: "ratio",
      icon: "🥇",
      tier: "gold",
      description: "Reach a 5.0x ratio on any individual torrent.",
      progress: Math.min(100, (maxRatio / 5.0) * 100),
      isUnlocked: maxRatio >= 5.0,
      currentValueText: formatRatio(maxRatio),
      targetValueText: "5.00",
    },
    {
      id: "ratio_10",
      name: "Diamond Seeder",
      category: "ratio",
      icon: "💎",
      tier: "diamond",
      description: "Reach an incredible 10.0x ratio on a torrent.",
      progress: Math.min(100, (maxRatio / 10.0) * 100),
      isUnlocked: maxRatio >= 10.0,
      currentValueText: formatRatio(maxRatio),
      targetValueText: "10.00",
    },
    {
      id: "volume_100gb",
      name: "Centurion Seeder",
      category: "volume",
      icon: "🥈",
      tier: "silver",
      description: "Upload at least 100 GB of total data across all swarms.",
      progress: Math.min(
        100,
        (totalUploaded / (100 * 1024 * 1024 * 1024)) * 100,
      ),
      isUnlocked: totalUploaded >= 100 * 1024 * 1024 * 1024,
      currentValueText: formatBytes(totalUploaded),
      targetValueText: "100 GB",
    },
    {
      id: "volume_1tb",
      name: "Terabyte Titan",
      category: "volume",
      icon: "🥇",
      tier: "gold",
      description: "Upload at least 1 TB of total data to peers worldwide.",
      progress: Math.min(
        100,
        (totalUploaded / (1024 * 1024 * 1024 * 1024)) * 100,
      ),
      isUnlocked: totalUploaded >= 1024 * 1024 * 1024 * 1024,
      currentValueText: formatBytes(totalUploaded),
      targetValueText: "1 TB",
    },
    {
      id: "volume_10tb",
      name: "Petabyte Pioneer",
      category: "volume",
      icon: "💎",
      tier: "diamond",
      description:
        "Upload a staggering 10 TB of data to the BitTorrent network.",
      progress: Math.min(
        100,
        (totalUploaded / (10 * 1024 * 1024 * 1024 * 1024)) * 100,
      ),
      isUnlocked: totalUploaded >= 10 * 1024 * 1024 * 1024 * 1024,
      currentValueText: formatBytes(totalUploaded),
      targetValueText: "10 TB",
    },
    {
      id: "guardian_1",
      name: "Swarm Guardian",
      category: "guardian",
      icon: "🛡️",
      tier: "silver",
      description:
        "Keep at least 1 rare torrent alive where total seeders is 2 or fewer.",
      progress: Math.min(100, (swarmGuardians.length / 1) * 100),
      isUnlocked: swarmGuardians.length >= 1,
      currentValueText: `${swarmGuardians.length} Rare Torrents`,
      targetValueText: "1 Torrent",
    },
    {
      id: "guardian_5",
      name: "Archive Defender",
      category: "guardian",
      icon: "🏰",
      tier: "gold",
      description: "Protect 5 or more rare/dying swarms from extinction.",
      progress: Math.min(100, (swarmGuardians.length / 5) * 100),
      isUnlocked: swarmGuardians.length >= 5,
      currentValueText: `${swarmGuardians.length} Protected`,
      targetValueText: "5 Torrents",
    },
    {
      id: "longevity_30d",
      name: "Perma-Seeder",
      category: "longevity",
      icon: "⏳",
      tier: "silver",
      description:
        "Keep a torrent seeding continuously for 30 days (720 hours).",
      progress: Math.min(100, (maxSeedTime / (30 * 86400)) * 100),
      isUnlocked: maxSeedTime >= 30 * 86400,
      currentValueText: formatSeconds(maxSeedTime),
      targetValueText: "30 Days",
    },
    {
      id: "longevity_100d",
      name: "Century Seeder",
      category: "longevity",
      icon: "👑",
      tier: "diamond",
      description: "Keep an archive seeding continuously for over 100 days.",
      progress: Math.min(100, (maxSeedTime / (100 * 86400)) * 100),
      isUnlocked: maxSeedTime >= 100 * 86400,
      currentValueText: formatSeconds(maxSeedTime),
      targetValueText: "100 Days",
    },
  ];

  const unlockedCount = badges.filter((b) => b.isUnlocked).length;
  const totalCount = badges.length;
  const overallLevel = Math.max(
    1,
    Math.floor((unlockedCount / totalCount) * 10),
  );

  const rankTitles = [
    "Novice Seeder",
    "Apprentice Peer",
    "Swarm Contributor",
    "Reliable Seeder",
    "Bandwidth Benefactor",
    "Dedicated Archivist",
    "Swarm Guardian",
    "Master Seeder",
    "Torrent Legend",
    "Immortal Seeder",
  ];

  const rankTitle =
    rankTitles[Math.min(overallLevel - 1, rankTitles.length - 1)];

  return {
    badges,
    unlockedCount,
    totalCount,
    overallLevel,
    rankTitle,
    totalSwarmGuardians: swarmGuardians,
  };
}

/**
 * Calculates tracker buffer and bonus point generation across all configured trackers.
 */
export function calculateTrackerBuffers(
  torrents: Torrent[] | undefined,
): TrackerBufferSummary[] {
  const trackerMap: Record<
    string,
    { torrents: Torrent[]; uploaded: number; downloaded: number }
  > = {};

  (torrents ?? []).forEach((t) => {
    let domain = "Global / Public";
    if (t.trackerUrl) {
      try {
        domain = new URL(t.trackerUrl).hostname;
      } catch {
        domain = t.trackerUrl;
      }
    }
    if (!trackerMap[domain]) {
      trackerMap[domain] = { torrents: [], uploaded: 0, downloaded: 0 };
    }
    trackerMap[domain].torrents.push(t);
    trackerMap[domain].uploaded += t.uploaded || 0;
    trackerMap[domain].downloaded += t.downloaded || 0;
  });

  return Object.entries(trackerMap)
    .map(([tracker, data]) => {
      const ratio =
        data.downloaded > 0
          ? data.uploaded / data.downloaded
          : data.uploaded > 0
            ? 10.0
            : 0;
      // Buffer = data.uploaded - (data.downloaded * 1.0)
      const bufferBytes = Math.max(0, data.uploaded - data.downloaded);

      // Private tracker formula: Size(GB)^0.55 * (1 + days/10) / Seeders^0.5
      let estimatedPointsPerHour = 0;
      data.torrents.forEach((t) => {
        const sizeGb = (t.totalSize || 0) / (1024 * 1024 * 1024);
        const seedDays = (t.seedingTime || 0) / 86400;
        const seeders = Math.max(1, t.seeders || 1);
        const pts =
          Math.pow(Math.max(0.1, sizeGb), 0.55) *
          (1 + seedDays / 10) *
          (1 / Math.pow(seeders, 0.5));
        estimatedPointsPerHour += pts * 0.1; // Normalized scale
      });

      return {
        tracker,
        torrentCount: data.torrents.length,
        totalUploaded: data.uploaded,
        totalDownloaded: data.downloaded,
        ratio,
        bufferBytes,
        estimatedPointsPerHour: Math.round(estimatedPointsPerHour * 10) / 10,
      };
    })
    .sort((a, b) => b.totalUploaded - a.totalUploaded);
}
