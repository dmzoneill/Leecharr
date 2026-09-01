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

  // Generate a sampled representation of blocks for the grid visualizer
  const displayBlocks = useMemo(() => {
    const numBlocks = Math.min(Math.max(totalPieces, 60), 480);
    const blocks: {
      startIndex: number;
      endIndex: number;
      status: "complete" | "missing" | "active";
    }[] = [];

    const piecesPerBlock = totalPieces / numBlocks;

    for (let i = 0; i < numBlocks; i++) {
      const startIdx = Math.floor(i * piecesPerBlock);
      const endIdx = Math.min(
        totalPieces - 1,
        Math.floor((i + 1) * piecesPerBlock) - 1,
      );
      const blockProgress = (i + 0.5) / numBlocks;
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
        startIndex: startIdx,
        endIndex: Math.max(startIdx, endIdx),
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
            gridTemplateColumns: "repeat(auto-fill, minmax(13px, 1fr))",
            gap: "3px",
            padding: "0.6rem",
            backgroundColor: "rgba(0, 0, 0, 0.35)",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.1))",
            minHeight: "140px",
            maxHeight: "320px",
            overflowY: "auto",
          }}
        >
          {displayBlocks.map((b, i) => {
            const isSingle = b.startIndex === b.endIndex;
            const pieceLabel = isSingle
              ? `Piece #${b.startIndex}`
              : `Pieces #${b.startIndex} - #${b.endIndex}`;
            return (
              <div
                key={i}
                onMouseEnter={() => setHoveredPiece(b.startIndex)}
                onMouseLeave={() => setHoveredPiece(null)}
                style={{
                  height: "14px",
                  borderRadius: "2px",
                  backgroundColor:
                    b.status === "complete"
                      ? "#27ae60"
                      : b.status === "active"
                        ? "#3b82f6"
                        : "rgba(255, 255, 255, 0.05)",
                  border:
                    b.status === "complete"
                      ? "1px solid #2ecc71"
                      : b.status === "active"
                        ? "1px solid #60a5fa"
                        : "1px solid rgba(255, 255, 255, 0.12)",
                  boxShadow:
                    b.status === "complete"
                      ? "0 0 3px rgba(39, 174, 96, 0.35)"
                      : b.status === "active"
                        ? "0 0 5px rgba(59, 130, 246, 0.6)"
                        : "none",
                  cursor: "pointer",
                  transition: "transform 0.1s ease",
                }}
                title={`${pieceLabel} (${formatBytes(pieceLength * (b.endIndex - b.startIndex + 1))}) - ${b.status === "complete" ? "Verified / Seeded" : b.status === "active" ? "Downloading" : "Missing"}`}
              />
            );
          })}
        </div>
      )}

      {/* Legend & Details footer */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.5rem",
          fontSize: "0.72rem",
          color: "var(--text-muted)",
        }}
      >
        <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap" }}>
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
                backgroundColor: "#3b82f6",
              }}
            />
            Active Download
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
                backgroundColor: "rgba(255,255,255,0.08)",
                border: "1px solid rgba(255,255,255,0.2)",
              }}
            />
            {totalPieces - completedPieces} Missing
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
