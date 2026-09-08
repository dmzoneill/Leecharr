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
import {
  decodeBase64Bitfield,
  countVerifiedPieces,
  binBitfieldBlocks,
  mergeBitfields,
} from "../utils/pieceMapUtils";

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
  const barContainerRef = useRef<HTMLDivElement | null>(null);
  const barCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const layoutRef = useRef({ cols: 0, blockSize: 0, gap: 0 });

  // Subscribe to live SignalR piece map bitmap updates with version counter
  const livePieceData = useTorrentStore((state) =>
    torrentId ? state.pieceMaps[torrentId] : undefined,
  );
  const liveBitfield = livePieceData?.bitfield;
  const liveVersion = livePieceData?.version ?? 0;

  const totalPieces = Math.max(
    1,
    Number.isFinite(pieceCount) && pieceCount > 0 ? pieceCount : 1,
  );
  const isComplete = progress >= 1.0 || isSeeding;

  const propBitfieldBytes = useMemo(() => {
    return decodeBase64Bitfield(bitfield);
  }, [bitfield]);

  const effectiveBitfield = useMemo(() => {
    if (liveBitfield && propBitfieldBytes) {
      return mergeBitfields(propBitfieldBytes, liveBitfield);
    }
    return liveBitfield || propBitfieldBytes || null;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [liveBitfield, liveVersion, propBitfieldBytes]);

  // Generate a sampled representation of blocks for visualizer via fast bitwise binning
  const displayBlocks = useMemo(() => {
    return binBitfieldBlocks(effectiveBitfield, totalPieces, 480, isComplete);
  }, [effectiveBitfield, totalPieces, isComplete]);

  const completedPieces = useMemo(() => {
    if (isComplete) {
      return totalPieces;
    }
    if (effectiveBitfield && effectiveBitfield.length > 0) {
      return countVerifiedPieces(effectiveBitfield, totalPieces);
    }
    if (progress > 0) {
      return Math.floor(progress * totalPieces);
    }
    return 0;
  }, [isComplete, totalPieces, effectiveBitfield, progress]);

  const verifiedPercentage = useMemo(() => {
    if (isComplete) {
      return 100;
    }
    if (totalPieces > 0 && completedPieces > 0) {
      return (completedPieces / totalPieces) * 100;
    }
    return Math.min(100, Math.max(0, progress * 100));
  }, [isComplete, totalPieces, completedPieces, progress]);

  const blockSize = 14;
  const gap = 3;

  const displayBlocksRef = useRef(displayBlocks);
  displayBlocksRef.current = displayBlocks;
  const hoveredIndexRef = useRef<number | null>(null);
  hoveredIndexRef.current = hoveredIndex;

  // Render Bar View directly to HTML5 Canvas
  const renderBar = useCallback(() => {
    if (viewMode !== "bar") return;
    const canvas = barCanvasRef.current;
    const container = barContainerRef.current;
    if (!canvas || !container) return;

    const currentBlocks = displayBlocksRef.current;
    const currentHovered = hoveredIndexRef.current;
    const availWidth = Math.max(100, container.clientWidth);
    const height = 22;

    const dpr = window.devicePixelRatio || 1;
    const targetW = Math.max(0, Math.floor(availWidth * dpr));
    const targetH = Math.max(0, Math.floor(height * dpr));

    if (canvas.width !== targetW || canvas.height !== targetH) {
      canvas.width = targetW;
      canvas.height = targetH;
      canvas.style.width = `${availWidth}px`;
      canvas.style.height = `${height}px`;
    }

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, availWidth, height);

    if (availWidth === 0) {
      ctx.restore();
      return;
    }

    if (isComplete) {
      ctx.fillStyle = "#27ae60";
      ctx.fillRect(0, 0, availWidth, height);
    } else if (currentBlocks.length > 0) {
      const numBlocks = currentBlocks.length;
      for (let i = 0; i < numBlocks; i++) {
        const b = currentBlocks[i];
        const x0 = (i / numBlocks) * availWidth;
        const x1 = ((i + 1) / numBlocks) * availWidth;
        const blockW = Math.max(0.5, x1 - x0);

        if (b.status === "complete") {
          ctx.fillStyle = currentHovered === i ? "#2ecc71" : "#27ae60";
          ctx.fillRect(x0, 0, blockW, height);
        } else if (b.status === "active") {
          ctx.fillStyle = currentHovered === i ? "#60a5fa" : "#3b82f6";
          ctx.fillRect(x0, 0, blockW, height);
        } else {
          ctx.fillStyle =
            currentHovered === i
              ? "rgba(255, 255, 255, 0.12)"
              : "rgba(255, 255, 255, 0.04)";
          ctx.fillRect(x0, 0, blockW, height);
        }
      }

      if (
        currentHovered !== null &&
        currentHovered >= 0 &&
        currentHovered < numBlocks
      ) {
        const hx0 = (currentHovered / numBlocks) * availWidth;
        const hx1 = ((currentHovered + 1) / numBlocks) * availWidth;
        const hW = Math.max(2, hx1 - hx0);
        ctx.strokeStyle = "#ffd166";
        ctx.lineWidth = 2;
        ctx.strokeRect(hx0, 1, hW, height - 2);
      }
    }

    ctx.restore();
  }, [viewMode, isComplete]);

  // Render Matrix Grid View with virtualized Canvas drawing
  const renderGrid = useCallback(() => {
    if (viewMode !== "grid") return;
    const canvas = canvasRef.current;
    const container = containerRef.current;
    if (!canvas || !container) return;

    const currentBlocks = displayBlocksRef.current;
    const currentHovered = hoveredIndexRef.current;

    const availWidth = Math.max(200, container.clientWidth - 24);
    const cols = Math.max(
      1,
      Math.floor((availWidth + gap) / (blockSize + gap)),
    );
    const totalRows = Math.max(0, Math.ceil(currentBlocks.length / cols));
    const width = Math.max(0, cols * (blockSize + gap) - gap);
    const height = Math.max(0, totalRows * (blockSize + gap) - gap);

    layoutRef.current = { cols, blockSize, gap };

    const dpr = window.devicePixelRatio || 1;
    const targetCanvasW = Math.max(0, Math.floor(width * dpr));
    const targetCanvasH = Math.max(0, Math.floor(height * dpr));

    if (canvas.width !== targetCanvasW || canvas.height !== targetCanvasH) {
      canvas.width = targetCanvasW;
      canvas.height = targetCanvasH;
      canvas.style.width = `${width}px`;
      canvas.style.height = `${height}px`;
    }

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, width, height);

    if (
      currentBlocks.length === 0 ||
      totalRows === 0 ||
      width === 0 ||
      height === 0
    ) {
      ctx.restore();
      return;
    }

    // Viewport row virtualization
    const scrollTop = container.scrollTop;
    const clientHeight = container.clientHeight || 240;
    const startRow = Math.max(0, Math.floor(scrollTop / (blockSize + gap)) - 2);
    const endRow = Math.min(
      totalRows - 1,
      Math.ceil((scrollTop + clientHeight) / (blockSize + gap)) + 2,
    );

    const startIdx = startRow * cols;
    const endIdx = Math.min(currentBlocks.length - 1, (endRow + 1) * cols - 1);

    for (let i = startIdx; i <= endIdx; i++) {
      const b = currentBlocks[i];
      if (!b) continue;
      const col = i % cols;
      const row = Math.floor(i / cols);
      const x = col * (blockSize + gap);
      const y = row * (blockSize + gap);

      const isHovered = currentHovered === i;

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
          (ctx as any).roundRect(x - 1, y - 1, blockSize + 2, blockSize + 2, 3);
        } else {
          ctx.rect(x - 1, y - 1, blockSize + 2, blockSize + 2);
        }
        ctx.stroke();
      }
    }

    ctx.restore();
  }, [viewMode]);

  // Redraw canvas on data or hover change
  useEffect(() => {
    if (viewMode === "bar") {
      const animId = requestAnimationFrame(renderBar);
      return () => cancelAnimationFrame(animId);
    } else {
      const animId = requestAnimationFrame(renderGrid);
      return () => cancelAnimationFrame(animId);
    }
  }, [viewMode, displayBlocks, hoveredIndex, renderBar, renderGrid]);

  // ResizeObserver for Grid container
  useEffect(() => {
    if (viewMode !== "grid") return;
    const container = containerRef.current;
    if (!container || typeof ResizeObserver === "undefined") return;

    let animId: number | null = null;
    const resizeObserver = new ResizeObserver(() => {
      if (animId !== null) cancelAnimationFrame(animId);
      animId = requestAnimationFrame(renderGrid);
    });
    resizeObserver.observe(container);

    return () => {
      if (animId !== null) cancelAnimationFrame(animId);
      resizeObserver.disconnect();
    };
  }, [viewMode, renderGrid]);

  // ResizeObserver for Bar container
  useEffect(() => {
    if (viewMode !== "bar") return;
    const container = barContainerRef.current;
    if (!container || typeof ResizeObserver === "undefined") return;

    let animId: number | null = null;
    const resizeObserver = new ResizeObserver(() => {
      if (animId !== null) cancelAnimationFrame(animId);
      animId = requestAnimationFrame(renderBar);
    });
    resizeObserver.observe(container);

    return () => {
      if (animId !== null) cancelAnimationFrame(animId);
      resizeObserver.disconnect();
    };
  }, [viewMode, renderBar]);

  const handleGridMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const container = containerRef.current;
    if (!canvas || !container || displayBlocks.length === 0) {
      setHoveredIndex(null);
      return;
    }

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

  const handleBarMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = barCanvasRef.current;
    if (!canvas || displayBlocks.length === 0) return;
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const pct = Math.max(0, Math.min(1, x / rect.width));
    const idx = Math.min(
      displayBlocks.length - 1,
      Math.floor(pct * displayBlocks.length),
    );
    setHoveredIndex(idx);
  };

  const handleMouseLeave = () => {
    setHoveredIndex(null);
  };

  const handleGridScroll = () => {
    if (viewMode === "grid") {
      renderGrid();
    }
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

      {/* Bar Mode - rendered via HTML5 Canvas */}
      {viewMode === "bar" ? (
        <div
          ref={barContainerRef}
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
          <canvas
            ref={barCanvasRef}
            onMouseMove={handleBarMouseMove}
            onMouseLeave={handleMouseLeave}
            style={{
              display: "block",
              width: "100%",
              height: "22px",
              cursor: "pointer",
            }}
          />
        </div>
      ) : (
        /* Matrix Grid Canvas Mode */
        <div
          ref={containerRef}
          onScroll={handleGridScroll}
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
            onMouseMove={handleGridMouseMove}
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
