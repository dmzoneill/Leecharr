interface SkeletonLineProps {
  width?: string;
  height?: string;
  className?: string;
}

export function SkeletonLine({
  width = "100%",
  height = "1rem",
  className = "",
}: SkeletonLineProps) {
  return (
    <div
      className={`skeleton skeleton-line ${className}`}
      style={{ width, height }}
    />
  );
}

interface SkeletonCardProps {
  className?: string;
}

export function SkeletonCard({ className = "" }: SkeletonCardProps) {
  return (
    <div className={`skeleton skeleton-card stat-card ${className}`}>
      <SkeletonLine width="60%" height="1.8rem" />
      <SkeletonLine width="80%" height="0.75rem" />
    </div>
  );
}

interface SkeletonTableRowProps {
  columns?: number;
  className?: string;
}

export function SkeletonTableRow({
  columns = 6,
  className = "",
}: SkeletonTableRowProps) {
  return (
    <tr className={`torrent-table-row ${className}`}>
      {Array.from({ length: columns }, (_, i) => (
        <td key={i}>
          <SkeletonLine width={i === 0 ? "80%" : "60%"} height="0.85rem" />
        </td>
      ))}
    </tr>
  );
}

interface SkeletonGridProps {
  count?: number;
  className?: string;
}

export function SkeletonGrid({ count = 4, className = "" }: SkeletonGridProps) {
  return (
    <div className={`stats-grid ${className}`}>
      {Array.from({ length: count }, (_, i) => (
        <SkeletonCard key={i} />
      ))}
    </div>
  );
}
