import React from "react";
import { useDiskSpace } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";

interface DiskStorageBadgeProps {
  compact?: boolean;
  onClick?: () => void;
  className?: string;
}

export const DiskStorageBadge: React.FC<DiskStorageBadgeProps> = ({
  compact = false,
  onClick,
  className = "",
}) => {
  const { data: diskSpaces, isLoading } = useDiskSpace();

  if (isLoading || !diskSpaces || diskSpaces.length === 0) {
    return (
      <div
        className={`disk-storage-badge loading ${className}`}
        title="Loading storage space..."
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: "6px",
          padding: compact ? "2px 6px" : "4px 10px",
          backgroundColor: "rgba(255, 255, 255, 0.04)",
          borderRadius: "6px",
          fontSize: compact ? "0.75rem" : "0.82rem",
          color: "var(--text-muted, #7e8092)",
        }}
      >
        <span>💾</span>
        <span>Storage...</span>
      </div>
    );
  }

  // Primary download storage volume (first disk space entry)
  const primary = diskSpaces[0];
  const freeBytes = primary.freeSpace ?? 0;
  const totalBytes = primary.totalSpace ?? 1;
  const usedBytes = Math.max(0, totalBytes - freeBytes);
  const usedPct = Math.min(100, Math.max(0, Math.round((usedBytes / totalBytes) * 100)));
  const isLowSpace = freeBytes < 20 * 1024 * 1024 * 1024 || usedPct >= 90; // < 20GB or > 90% full

  return (
    <div
      className={`disk-storage-badge ${isLowSpace ? "low-space" : ""} ${className}`}
      onClick={onClick}
      title={`Storage volume: ${primary.path || "/"}\nFree: ${formatBytes(freeBytes)} / Total: ${formatBytes(totalBytes)} (${100 - usedPct}% free)`}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "6px",
        padding: compact ? "2px 8px" : "4px 10px",
        backgroundColor: isLowSpace
          ? "rgba(235, 87, 87, 0.15)"
          : "rgba(255, 255, 255, 0.05)",
        border: isLowSpace
          ? "1px solid rgba(235, 87, 87, 0.4)"
          : "1px solid rgba(255, 255, 255, 0.08)",
        borderRadius: "6px",
        fontSize: compact ? "0.78rem" : "0.82rem",
        color: isLowSpace ? "#ff6b6b" : "var(--text-secondary, #c7c5d3)",
        cursor: onClick ? "pointer" : "default",
        userSelect: "none",
        transition: "all 0.2s ease",
      }}
    >
      <span style={{ fontSize: "0.85rem" }}>💾</span>
      <span style={{ fontWeight: 600, color: isLowSpace ? "#ff6b6b" : "var(--text-primary, #f8f4ed)" }}>
        {formatBytes(freeBytes)} Free
      </span>
      <div
        style={{
          width: compact ? "36px" : "48px",
          height: "5px",
          backgroundColor: "rgba(255, 255, 255, 0.12)",
          borderRadius: "3px",
          overflow: "hidden",
          marginLeft: "2px",
        }}
      >
        <div
          style={{
            width: `${usedPct}%`,
            height: "100%",
            backgroundColor: isLowSpace
              ? "var(--danger, #eb5757)"
              : usedPct > 75
                ? "var(--accent, #ffd166)"
                : "var(--success, #27ae60)",
            transition: "width 0.3s ease",
          }}
        />
      </div>
    </div>
  );
};

export default DiskStorageBadge;
