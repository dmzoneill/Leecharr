import React, {
  useState,
  useMemo,
  useRef,
  useEffect,
  useCallback,
} from "react";
import { useTranslation } from "../i18n";
import { formatBytes } from "../utils/formatters";
import { useTorrentStore } from "../stores/useTorrentStore";

export interface PieceMapProps {
  torrentId?: number;
  pieceCount: number;
  pieceLength: number;
  progress: number; // 0.0 - 1.0
  isSeeding?: boolean;
  bitfield?: string | null;
  className?: string;
}

export function PieceMap({
  torrentId,
  pieceCount,
  pieceLength,
  progress = 0,
  isSeeding = false,
  bitfield,
  className,
}: PieceMapProps) {
  const { t } = useTranslation();
  const [viewMode, setViewMode] = useState<"bar" | "grid">("bar");
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);

  const containerRef = useRef<HTMLDivElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const layoutRef = useRef({ cols: 0, blockSize: 0, gap: 0 });

  // Subscribe to live SignalR piece map bitmap updates
  const livePieceData = useTorrentStore((state) =>
    torrentId ? state.pieceMaps[torrentId] : undefined,
  );

  const totalPieces = Math.max(1, pieceCount);

  const bitfieldBytes = useMemo(() => {
    if (!bitfield || typeof bitfield !== "string") return null;
    try {
      const binary = atob(bitfield);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
      }
      return bytes;
    } catch {
      return null;
    }
  }, [bitfield]);

  const isPieceVerified = useCallback(
    (pieceIdx: number): boolean => {
      if (progress >= 1.0 || isSeeding) {
        return true;
      }
      if (livePieceData?.verifiedIndices?.has(pieceIdx)) {
        return true;
      }
      if (bitfieldBytes && bitfieldBytes.length > 0) {
        const byteIdx = Math.floor(pieceIdx / 8);
        if (byteIdx < bitfieldBytes.length) {
          const bitOffset = 7 - (pieceIdx % 8);
          return (bitfieldBytes[byteIdx] & (1 << bitOffset)) !== 0;
        }
      }
      return false;
    },
    [progress, isSeeding, livePieceData, bitfieldBytes],
  );

  // Generate a sampled representation of blocks for visualizer
  const displayBlocks = useMemo(() => {
    const numBlocks = Math.min(totalPieces, 480);
    const blocks: {
      startIndex: number;
      endIndex: number;
      status: "complete" | "missing" | "active";
      completedCount: number;
      totalInBlock: number;
    }[] = [];

    const piecesPerBlock = totalPieces / numBlocks;

    for (let i = 0; i < numBlocks; i++) {
      const startIdx = Math.floor(i * piecesPerBlock);
      const endIdx = Math.min(
        totalPieces - 1,
        Math.floor((i + 1) * piecesPerBlock) - 1,
      );
      const totalInBlock = Math.max(1, endIdx - startIdx + 1);

      let completedCount = 0;
      for (let p = startIdx; p <= endIdx; p++) {
        if (isPieceVerified(p)) {
          completedCount++;
        }
      }

      let status: "complete" | "missing" | "active" = "missing";
      if (progress >= 1.0 || isSeeding || completedCount === totalInBlock) {
        status = "complete";
      } else if (completedCount > 0) {
        status = "active";
      }

      blocks.push({
        startIndex: startIdx,
        endIndex: Math.max(startIdx, endIdx),
        status,
        completedCount,
        totalInBlock,
      });
    }

    return blocks;
  }, [totalPieces, isPieceVerified, progress, isSeeding]);

  const completedPieces = useMemo(() => {
    if (progress >= 1.0 || isSeeding) {
      return totalPieces;
    }
    let count = 0;
    for (let p = 0; p < totalPieces; p++) {
      if (isPieceVerified(p)) {
        count++;
      }
    }
    if (count === 0 && progress > 0) {
      return Math.floor(progress * totalPieces);
    }
    return count;
  }, [progress, isSeeding, totalPieces, isPieceVerified]);

  const verifiedPercentage = useMemo(() => {
    if (progress >= 1.0 || isSeeding) {
      return 100;
    }
    if (totalPieces > 0 && completedPieces > 0) {
      return (completedPieces / totalPieces) * 100;
    }
    return Math.min(100, Math.max(0, progress * 100));
  }, [progress, isSeeding, totalPieces, completedPieces]);

  const blockSize = 14;
  const gap = 3;

  // Render canvas via requestAnimationFrame with DPI scaling
  useEffect(() => {
    if (viewMode !== "grid") return;
    const canvas = canvasRef.current;
    if (!canvas) return;

    let animId: number;

    const render = () => {
      const container = containerRef.current;
      if (!container) return;

      const availWidth = Math.max(200, container.clientWidth - 24);
      const cols = Math.max(
        1,
        Math.floor((availWidth + gap) / (blockSize + gap)),
      );
      const rows = Math.ceil(displayBlocks.length / cols);
      const width = cols * (blockSize + gap) - gap;
      const height = rows * (blockSize + gap) - gap;

      layoutRef.current = { cols, blockSize, gap };

      const dpr = window.devicePixelRatio || 1;
      if (
        canvas.width !== Math.floor(width * dpr) ||
        canvas.height !== Math.floor(height * dpr)
      ) {
        canvas.width = Math.floor(width * dpr);
        canvas.height = Math.floor(height * dpr);
        canvas.style.width = `${width}px`;
        canvas.style.height = `${height}px`;
      }

      const ctx = canvas.getContext("2d");
      if (!ctx) return;

      ctx.save();
      ctx.scale(dpr, dpr);
      ctx.clearRect(0, 0, width, height);

      for (let i = 0; i < displayBlocks.length; i++) {
        const b = displayBlocks[i];
        const col = i % cols;
        const row = Math.floor(i / cols);
        const x = col * (blockSize + gap);
        const y = row * (blockSize + gap);

        const isHovered = hoveredIndex === i;

        if (b.status === "complete") {
          ctx.fillStyle = isHovered ? "#2ecc71" : "#27ae60";
          ctx.strokeStyle = "#2ecc71";
        } else if (b.status === "active") {
          ctx.fillStyle = isHovered ? "#60a5fa" : "#3b82f6";
          ctx.strokeStyle = "#60a5fa";
        } else {
          ctx.fillStyle = isHovered
            ? "rgba(255, 255, 255, 0.15)"
            : "rgba(255, 255, 255, 0.05)";
          ctx.strokeStyle = "rgba(255, 255, 255, 0.12)";
        }

        const radius = 2;
        ctx.beginPath();
        if (typeof (ctx as any).roundRect === "function") {
          (ctx as any).roundRect(x, y, blockSize, blockSize, radius);
        } else {
          ctx.rect(x, y, blockSize, blockSize);
        }
        ctx.fill();
        ctx.lineWidth = 1;
        ctx.stroke();

        if (isHovered) {
          ctx.strokeStyle = "#ffd166";
          ctx.lineWidth = 2;
          ctx.beginPath();
          if (typeof (ctx as any).roundRect === "function") {
            (ctx as any).roundRect(
              x - 1,
              y - 1,
              blockSize + 2,
              blockSize + 2,
              3,
            );
          } else {
            ctx.rect(x - 1, y - 1, blockSize + 2, blockSize + 2);
          }
          ctx.stroke();
        }
      }

      ctx.restore();
    };

    animId = requestAnimationFrame(render);

    const container = containerRef.current;
    let resizeObserver: ResizeObserver | undefined;
    if (container && typeof ResizeObserver !== "undefined") {
      resizeObserver = new ResizeObserver(() => {
        requestAnimationFrame(render);
      });
      resizeObserver.observe(container);
    }

    return () => {
      cancelAnimationFrame(animId);
      resizeObserver?.disconnect();
    };
  }, [viewMode, displayBlocks, hoveredIndex]);

  const handleMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const container = containerRef.current;
    if (!canvas || !container) return;

    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    const { cols, blockSize, gap } = layoutRef.current;
    if (cols <= 0) return;

    const col = Math.floor(x / (blockSize + gap));
    const row = Math.floor(y / (blockSize + gap));
    const withinBlockX = x % (blockSize + gap) <= blockSize;
    const withinBlockY = y % (blockSize + gap) <= blockSize;

    if (col >= 0 && col < cols && withinBlockX && withinBlockY) {
      const idx = row * cols + col;
      if (idx >= 0 && idx < displayBlocks.length) {
        setHoveredIndex(idx);
        return;
      }
    }
    setHoveredIndex(null);
  };

  const handleMouseLeave = () => {
    setHoveredIndex(null);
  };

  const hoveredBlock =
    hoveredIndex !== null && displayBlocks[hoveredIndex]
      ? displayBlocks[hoveredIndex]
      : null;

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
            🧩 {t("torrents.detail.pieceMapTitle")}
          </span>
          <span
            className={`badge ${verifiedPercentage >= 100 ? "badge-success" : "badge-primary"}`}
            style={{ fontSize: "0.72rem" }}
          >
            {t("torrents.detail.pieceMapVerifiedPercent", {
              percent: verifiedPercentage.toFixed(1),
            })}
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
            {t("torrents.detail.pieceMapPiecesInfo", {
              count: totalPieces.toLocaleString(),
              size: formatBytes(pieceLength),
            })}
          </span>
          <div className="view-toggle" style={{ margin: 0 }}>
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "bar" ? "active" : ""}`}
              onClick={() => setViewMode("bar")}
              style={{ padding: "0.15rem 0.4rem", fontSize: "0.7rem" }}
              title={t("torrents.detail.pieceMapLinearBarView")}
            >
              {t("torrents.detail.pieceMapBar")}
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "grid" ? "active" : ""}`}
              onClick={() => setViewMode("grid")}
              style={{ padding: "0.15rem 0.4rem", fontSize: "0.7rem" }}
              title={t("torrents.detail.pieceMapMatrixGridView")}
            >
              {t("torrents.detail.pieceMapGrid")}
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
              display: "flex",
            }}
          >
            {displayBlocks.map((b, idx) => (
              <div
                key={idx}
                onMouseEnter={() => setHoveredIndex(idx)}
                onMouseLeave={() => setHoveredIndex(null)}
                style={{
                  flex: 1,
                  height: "100%",
                  backgroundColor:
                    b.status === "complete"
                      ? "#27ae60"
                      : b.status === "active"
                        ? "#3b82f6"
                        : "transparent",
                  outline: hoveredIndex === idx ? "1px solid #ffd166" : "none",
                  zIndex: hoveredIndex === idx ? 2 : 1,
                }}
              />
            ))}
          </div>
        </div>
      ) : (
        /* Matrix Grid Canvas Mode */
        <div
          ref={containerRef}
          style={{
            position: "relative",
            padding: "0.6rem",
            backgroundColor: "rgba(0, 0, 0, 0.35)",
            borderRadius: "6px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.1))",
            minHeight: "140px",
            maxHeight: "320px",
            overflowY: "auto",
            display: "flex",
            justifyContent: "center",
          }}
        >
          <canvas
            ref={canvasRef}
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
            style={{
              display: "block",
              cursor: "pointer",
            }}
          />
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
            {t("torrents.detail.pieceMapCompleteLegend", {
              completed: completedPieces,
              total: totalPieces,
            })}
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
            {t("torrents.detail.pieceMapActiveLegend")}
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
            {t("torrents.detail.pieceMapMissingLegend", {
              count: Math.max(0, totalPieces - completedPieces),
            })}
          </span>
        </div>
        {hoveredBlock && (
          <span
            style={{ fontFamily: "monospace", color: "var(--accent, #ffd166)" }}
          >
            {hoveredBlock.startIndex === hoveredBlock.endIndex
              ? t("torrents.detail.pieceMapPieceSingle", {
                  index: hoveredBlock.startIndex,
                })
              : t("torrents.detail.pieceMapPieceRange", {
                  start: hoveredBlock.startIndex,
                  end: hoveredBlock.endIndex,
                })}{" "}
            (
            {formatBytes(
              pieceLength *
                (hoveredBlock.endIndex - hoveredBlock.startIndex + 1),
            )}
            ) -{" "}
            {hoveredBlock.status === "complete"
              ? t("torrents.detail.pieceMapVerifiedSeeded")
              : hoveredBlock.status === "active"
                ? t("torrents.detail.pieceMapPartial", {
                    completed: hoveredBlock.completedCount,
                    total: hoveredBlock.totalInBlock,
                  })
                : t("torrents.detail.pieceMapMissing")}
          </span>
        )}
      </div>
    </div>
  );
}

export default PieceMap;
