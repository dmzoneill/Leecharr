import {
  formatBytes,
  formatDate,
  formatRatio,
  formatSeconds,
} from "../../utils/formatters";
import type { Torrent } from "../../api/types";
import { InfoRow } from "./shared";

export function StatusTab({ torrent }: { torrent: Torrent }) {
  const rows: [string, string][] = [
    ["Status", torrent.status],
    ["Progress", `${(torrent.progress * 100).toFixed(1)}%`],
    ["Uploaded", formatBytes(torrent.uploaded)],
    ["Downloaded", formatBytes(torrent.downloaded)],
    ["Ratio", formatRatio(torrent.ratio)],
    ["Seeding Time", formatSeconds(torrent.seedingTime)],
    ["Seeders", String(torrent.seeders)],
    ["Leechers", String(torrent.leechers)],
    [
      "Upload Limit",
      torrent.uploadLimit > 0 ? `${torrent.uploadLimit} KB/s` : "Global",
    ],
    [
      "Download Limit",
      torrent.downloadLimit > 0 ? `${torrent.downloadLimit} KB/s` : "Global",
    ],
    [
      "Priority",
      torrent.priority === 2
        ? "High"
        : torrent.priority === 1
          ? "Normal"
          : "Low",
    ],
    ["Super Seeding", torrent.superSeeding ? "Yes" : "No"],
    ["Force Start", torrent.forceStart ? "Yes" : "No"],
    ["Label", torrent.label ?? "-"],
    ["Added", formatDate(torrent.dateAdded)],
    ["Last Active", formatDate(torrent.lastActive)],
  ];

  return (
    <div className="detail-panel-grid">
      {rows.map(([label, value]) => (
        <InfoRow key={label} label={label} value={value} />
      ))}
    </div>
  );
}
