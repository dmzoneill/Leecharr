import { useState, useMemo } from "react";
import { formatBytes } from "../utils/formatters";

interface PieceMapProps {
  pieceCount: number;
  pieceLength: number;
  progress: number; // 0.0 - 1.0
  isSeeding?: boolean;
  className?: string;
}

export function PieceMap({
  pieceCount,
  pieceLength,
  progress,
  isSeeding = true,
  className,
}: PieceMapProps) {
  const [viewMode, setViewMode] = useState<"bar" | "grid">("bar");
  const [hoveredPiece, setHoveredPiece] = useState<number | null>(null);

  const totalPieces = Math.max(1, pieceCount);
  const completedPieces = Math.floor(progress * totalPieces);

  // Generate a sampled representation of 120 blocks for the grid visualizer
  const displayBlocks = useMemo(() => {
    const NUM_BLOCKS = 120;
    const blocks: {
      index: number;
      status: "complete" | "missing" | "active";
    }[] = [];

    for (let i = 0; i < NUM_BLOCKS; i++) {
      const blockProgress = (i + 0.5) / NUM_BLOCKS;
      let status: "complete" | "missing" | "active" = "missing";

      if (progress >= 1.0 || isSeeding) {
        status = "complete";
      } else if (blockProgress <= progress) {
        status = "complete";
      } else if (
        blockProgress <= progress + 0.05 &&
        progress > 0 &&
        progress < 1
      ) {
        status = "active";
      }

      blocks.push({
        index: Math.floor((i / NUM_BLOCKS) * totalPieces),
        status,
      });
    }

    return blocks;
  }, [progress, isSeeding, totalPieces]);

  return (
    <div
      className={className}
      style={{
        padding: "0.85rem",
        backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
        borderRadius: "8px",
        border: "1px solid var(--border-light)",
        display: "flex",
        flexDirection: "column",
        gap: "0.6rem",
      }}
    >
      {/* Header with stats and view toggles */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.5rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <span style={{ fontWeight: 600, fontSize: "0.85rem" }}>
            🧩 BitTorrent Piece Map
          </span>
          <span
            className={`badge ${progress >= 1.0 ? "badge-success" : "badge-primary"}`}
            style={{ fontSize: "0.72rem" }}
          >
            {(progress * 100).toFixed(1)}% Verified
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <span
            style={{
              fontSize: "0.75rem",
              color: "var(--text-muted)",
              fontFamily: "monospace",
            }}
          >
            {totalPieces.toLocaleString()} pieces @ {formatBytes(pieceLength)}
          </span>
          <div className="view-toggle" style={{ margin: 0 }}>
            <button
              className={`view-toggle-btn ${viewMode === "bar" ? "active" : ""}`}
              onClick={() => setViewMode("bar")}
              style={{ padding: "0.15rem 0.4rem", fontSize: "0.7rem" }}
              title="Linear Bar View"
            >
              Bar
            </button>
            <button
              className={`view-toggle-btn ${viewMode === "grid" ? "active" : ""}`}
              onClick={() => setViewMode("grid")}
              style={{ padding: "0.15rem 0.4rem", fontSize: "0.7rem" }}
              title="Matrix Grid View"
            >
              Grid
            </button>
          </div>
        </div>
      </div>

      {/* Bar Mode */}
      {viewMode === "bar" ? (
        <div
          style={{ display: "flex", flexDirection: "column", gap: "0.3rem" }}
        >
          <div
            style={{
              position: "relative",
              width: "100%",
              height: "22px",
              backgroundColor: "rgba(255, 255, 255, 0.06)",
              borderRadius: "4px",
              overflow: "hidden",
              border: "1px solid var(--border-light)",
            }}
          >
            <div
              style={{
                width: `${Math.min(100, Math.max(0, progress * 100))}%`,
                height: "100%",
                background:
                  progress >= 1.0
                    ? "linear-gradient(90deg, #27ae60 0%, #2ecc71 100%)"
                    : "linear-gradient(90deg, #c8a84e 0%, #e67e22 100%)",
                transition: "width 0.3s ease",
              }}
            />
            {/* Hash Check Overlay tickmarks */}
            <div
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                right: 0,
                bottom: 0,
                backgroundImage:
                  "repeating-linear-gradient(90deg, transparent 0, transparent 19px, rgba(0, 0, 0, 0.25) 19px, rgba(0, 0, 0, 0.25) 20px)",
                pointerEvents: "none",
              }}
            />
          </div>
        </div>
      ) : (
        /* Matrix Grid Mode */
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(12px, 1fr))",
            gap: "3px",
            padding: "0.35rem 0",
          }}
        >
          {displayBlocks.map((b, i) => (
            <div
              key={i}
              onMouseEnter={() => setHoveredPiece(b.index)}
              onMouseLeave={() => setHoveredPiece(null)}
              style={{
                height: "14px",
                borderRadius: "2px",
                backgroundColor:
                  b.status === "complete"
                    ? "#27ae60"
                    : b.status === "active"
                      ? "#3498db"
                      : "rgba(255, 255, 255, 0.08)",
                boxShadow:
                  b.status === "complete"
                    ? "0 0 4px rgba(39, 174, 96, 0.3)"
                    : "none",
                cursor: "pointer",
                transition: "transform 0.1s ease",
              }}
              title={`Piece #${b.index} (${formatBytes(pieceLength)}) - ${b.status === "complete" ? "Seeded / Verified" : b.status === "active" ? "Downloading" : "Missing"}`}
            />
          ))}
        </div>
      )}

      {/* Legend & Details footer */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          fontSize: "0.72rem",
          color: "var(--text-muted)",
        }}
      >
        <div style={{ display: "flex", gap: "0.75rem" }}>
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.3rem",
            }}
          >
            <span
              style={{
                width: "8px",
                height: "8px",
                borderRadius: "2px",
                backgroundColor: "#27ae60",
              }}
            />
            {completedPieces} / {totalPieces} Complete
          </span>
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.3rem",
            }}
          >
            <span
              style={{
                width: "8px",
                height: "8px",
                borderRadius: "2px",
                backgroundColor: "rgba(255,255,255,0.15)",
              }}
            />
            {totalPieces - completedPieces} Remaining
          </span>
        </div>
        {hoveredPiece !== null && (
          <span style={{ fontFamily: "monospace", color: "var(--accent)" }}>
            Hovering: Piece #{hoveredPiece}
          </span>
        )}
      </div>
    </div>
  );
}

export default PieceMap;
