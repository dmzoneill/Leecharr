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
import { useTorrentFiles } from "../api/hooks";
import type { TorrentFileInfo } from "../api/types";
import {
  decodeBase64Bitfield,
  countVerifiedPieces,
  binBitfieldBlocks,
  mergeBitfields,
  VisualBlock,
} from "../utils/pieceMapUtils";

export interface PieceMapProps {
  torrentId?: number;
  pieceCount: number;
  pieceLength: number;
  progress?: number; // 0.0 - 1.0
  isSeeding?: boolean;
  bitfield?: string | null;
  className?: string;
  files?: TorrentFileInfo[];
}

export type PieceMapColorMode = "status" | "rarity" | "files";

interface FileBoundary {
  file: TorrentFileInfo;
  startByte: number;
  endByte: number;
  startPiece: number;
  endPiece: number;
  colorIndex: number;
}

const FILE_PALETTE = [
  "#3498db",
  "#9b59b6",
  "#e67e22",
  "#1abc9c",
  "#f39c12",
  "#e74c3c",
  "#2ecc71",
  "#e84393",
  "#00cec9",
  "#6c5ce7",
];

function getRarityColor(count: number): string {
  if (count <= 0) return "rgba(255, 255, 255, 0.05)";
  if (count === 1) return "#e74c3c"; // Rare red
  if (count <= 3) return "#e67e22"; // 2-3 orange
  if (count <= 5) return "#f1c40f"; // 4-5 yellow
  if (count <= 9) return "#82c91e"; // 6-9 lime
  return "#27ae60"; // 10+ green
}

export function PieceMap({
  torrentId,
  pieceCount,
  pieceLength,
  progress = 0,
  isSeeding = false,
  bitfield,
  className,
  files: propFiles,
}: PieceMapProps) {
  const { t } = useTranslation();
  const [viewMode, setViewMode] = useState<"bar" | "grid">("bar");
  const [colorMode, setColorMode] = useState<PieceMapColorMode>("status");
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);
  const [selectedFileIndex, setSelectedFileIndex] = useState<number | null>(
    null,
  );
  const [hoveredFileIndex, setHoveredFileIndex] = useState<number | null>(null);

  const containerRef = useRef<HTMLDivElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const barContainerRef = useRef<HTMLDivElement | null>(null);
  const barCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const layoutRef = useRef({ cols: 0, blockSize: 0, gap: 0 });

  // Fetch torrent files if not passed
  const { data: fetchedFiles } = useTorrentFiles(torrentId ?? 0);

  // Compute file boundaries
  const fileBoundaries = useMemo<FileBoundary[]>(() => {
    const files = propFiles || fetchedFiles;
    if (!files || files.length === 0) return [];
    let curByte = 0;
    return files.map((file, idx) => {
      const startByte = curByte;
      const endByte = curByte + file.size;
      curByte = endByte;
      const startPiece = Math.floor(startByte / Math.max(1, pieceLength));
      const endPiece = Math.max(
        startPiece,
        Math.floor(Math.max(0, endByte - 1) / Math.max(1, pieceLength)),
      );
      return {
        file,
        startByte,
        endByte,
        startPiece,
        endPiece,
        colorIndex: idx,
      };
    });
  }, [propFiles, fetchedFiles, pieceLength]);

  const activeFileIndex =
    hoveredFileIndex !== null ? hoveredFileIndex : selectedFileIndex;
  const activeFileBoundary =
    activeFileIndex !== null && fileBoundaries[activeFileIndex]
      ? fileBoundaries[activeFileIndex]
      : null;

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

  // Resolve files for a visual block
  const getFilesForBlock = useCallback(
    (b: VisualBlock) => {
      const results: { name: string; size: number }[] = [];
      for (const fb of fileBoundaries) {
        if (fb.startPiece <= b.endIndex && fb.endPiece >= b.startIndex) {
          const fileName = fb.file.path.split("/").pop() || fb.file.path;
          results.push({ name: fileName, size: fb.file.size });
        }
      }
      return results;
    },
    [fileBoundaries],
  );

  // Render Bar View directly to HTML5 Canvas
  const renderBar = useCallback(() => {
    if (viewMode !== "bar") return;
    const canvas = barCanvasRef.current;
    const container = barContainerRef.current;
    if (!canvas || !container) return;

    const currentBlocks = displayBlocksRef.current;
    const currentHovered = hoveredIndexRef.current;
    const availWidth = Math.max(100, container.clientWidth);
    const height = 24;

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

    if (currentBlocks.length > 0) {
      const numBlocks = currentBlocks.length;
      for (let i = 0; i < numBlocks; i++) {
        const b = currentBlocks[i];
        const x0 = (i / numBlocks) * availWidth;
        const x1 = ((i + 1) / numBlocks) * availWidth;
        const blockW = Math.max(0.5, x1 - x0);

        let fillColor = "rgba(255, 255, 255, 0.04)";

        if (colorMode === "files") {
          if (activeFileBoundary) {
            const overlaps =
              b.startIndex <= activeFileBoundary.endPiece &&
              b.endIndex >= activeFileBoundary.startPiece;
            fillColor = overlaps
              ? FILE_PALETTE[
                  activeFileBoundary.colorIndex % FILE_PALETTE.length
                ]
              : "#1a1815";
          } else {
            const containingFb = fileBoundaries.find(
              (fb) =>
                b.startIndex <= fb.endPiece && b.endIndex >= fb.startPiece,
            );
            fillColor = containingFb
              ? FILE_PALETTE[containingFb.colorIndex % FILE_PALETTE.length]
              : b.status === "complete"
                ? "#27ae60"
                : "rgba(255, 255, 255, 0.04)";
          }
        } else if (colorMode === "rarity") {
          const estRarity =
            isComplete || b.status === "complete"
              ? 10
              : b.status === "active"
                ? 3
                : 0;
          fillColor = getRarityColor(estRarity);
        } else {
          // Status mode
          if (isComplete || b.status === "complete") {
            fillColor = currentHovered === i ? "#2ecc71" : "#27ae60";
          } else if (b.status === "active") {
            fillColor = currentHovered === i ? "#60a5fa" : "#3b82f6";
          } else {
            fillColor =
              currentHovered === i
                ? "rgba(255, 255, 255, 0.12)"
                : "rgba(255, 255, 255, 0.04)";
          }
        }

        ctx.fillStyle = fillColor;
        ctx.fillRect(x0, 0, blockW, height);
      }

      if (
        currentHovered !== null &&
        currentHovered >= 0 &&
        currentHovered < numBlocks
      ) {
        const hx0 = (currentHovered / numBlocks) * availWidth;
        const hx1 = ((currentHovered + 1) / numBlocks) * availWidth;
        const hW = Math.max(3, hx1 - hx0);
        ctx.strokeStyle = "#ffd166";
        ctx.lineWidth = 2;
        ctx.strokeRect(hx0, 1, hW, height - 2);
      }
    } else if (isComplete) {
      ctx.fillStyle = "#27ae60";
      ctx.fillRect(0, 0, availWidth, height);
    }

    ctx.restore();
  }, [viewMode, colorMode, isComplete, activeFileBoundary, fileBoundaries]);

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
      let fillColor = "rgba(255, 255, 255, 0.05)";
      let strokeColor = "rgba(255, 255, 255, 0.12)";

      if (colorMode === "files") {
        if (activeFileBoundary) {
          const overlaps =
            b.startIndex <= activeFileBoundary.endPiece &&
            b.endIndex >= activeFileBoundary.startPiece;
          fillColor = overlaps
            ? FILE_PALETTE[activeFileBoundary.colorIndex % FILE_PALETTE.length]
            : "#1a1815";
          strokeColor = overlaps
            ? FILE_PALETTE[activeFileBoundary.colorIndex % FILE_PALETTE.length]
            : "#222";
        } else {
          const containingFb = fileBoundaries.find(
            (fb) => b.startIndex <= fb.endPiece && b.endIndex >= fb.startPiece,
          );
          if (containingFb) {
            fillColor =
              FILE_PALETTE[containingFb.colorIndex % FILE_PALETTE.length];
            strokeColor = fillColor;
          } else {
            fillColor =
              b.status === "complete" ? "#27ae60" : "rgba(255, 255, 255, 0.05)";
            strokeColor = "rgba(255, 255, 255, 0.12)";
          }
        }
      } else if (colorMode === "rarity") {
        const estRarity =
          isComplete || b.status === "complete"
            ? 10
            : b.status === "active"
              ? 3
              : 0;
        fillColor = getRarityColor(estRarity);
        strokeColor = fillColor;
      } else {
        // Status mode
        if (b.status === "complete") {
          fillColor = isHovered ? "#2ecc71" : "#27ae60";
          strokeColor = "#2ecc71";
        } else if (b.status === "active") {
          fillColor = isHovered ? "#60a5fa" : "#3b82f6";
          strokeColor = "#60a5fa";
        } else {
          fillColor = isHovered
            ? "rgba(255, 255, 255, 0.15)"
            : "rgba(255, 255, 255, 0.05)";
          strokeColor = "rgba(255, 255, 255, 0.12)";
        }
      }

      ctx.fillStyle = fillColor;
      ctx.strokeStyle = strokeColor;

      const roundRectCtx = ctx as CanvasRenderingContext2D & {
        roundRect?(x: number, y: number, w: number, h: number, radii?: number): void;
      };

      const radius = 2;
      ctx.beginPath();
      if (typeof roundRectCtx.roundRect === "function") {
        roundRectCtx.roundRect(x, y, blockSize, blockSize, radius);
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
        if (typeof roundRectCtx.roundRect === "function") {
          roundRectCtx.roundRect(x - 1, y - 1, blockSize + 2, blockSize + 2, 3);
        } else {
          ctx.rect(x - 1, y - 1, blockSize + 2, blockSize + 2);
        }
        ctx.stroke();
      }
    }

    ctx.restore();
  }, [viewMode, colorMode, isComplete, activeFileBoundary, fileBoundaries]);

  // Redraw canvas on data, hover, or mode change
  useEffect(() => {
    if (viewMode === "bar") {
      const animId = requestAnimationFrame(renderBar);
      return () => cancelAnimationFrame(animId);
    } else {
      const animId = requestAnimationFrame(renderGrid);
      return () => cancelAnimationFrame(animId);
    }
  }, [
    viewMode,
    colorMode,
    displayBlocks,
    hoveredIndex,
    activeFileBoundary,
    renderBar,
    renderGrid,
  ]);

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

  const hoveredFiles = hoveredBlock ? getFilesForBlock(hoveredBlock) : [];

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
      {/* Header with stats, layout toggles, and color mode toggles */}
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

        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            flexWrap: "wrap",
          }}
        >
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

          {/* Layout Mode Toggle: Bar vs Grid */}
          <div
            className="view-toggle"
            style={{ margin: 0, display: "flex", gap: "2px" }}
          >
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

          {/* Color Mode Toggle: Status vs Rarity vs Files */}
          <div
            className="view-toggle"
            style={{ margin: 0, display: "flex", gap: "2px" }}
          >
            <button
              type="button"
              className={`view-toggle-btn ${colorMode === "status" ? "active" : ""}`}
              onClick={() => setColorMode("status")}
              style={{
                padding: "0.15rem 0.4rem",
                fontSize: "0.7rem",
                backgroundColor:
                  colorMode === "status"
                    ? "var(--accent, #ffd166)"
                    : "transparent",
                color: colorMode === "status" ? "#000" : "inherit",
              }}
              title="Download Status: Missing, Active, Verified"
            >
              {t("torrents.detail.pieceMapStatus")}
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${colorMode === "rarity" ? "active" : ""}`}
              onClick={() => setColorMode("rarity")}
              style={{
                padding: "0.15rem 0.4rem",
                fontSize: "0.7rem",
                backgroundColor:
                  colorMode === "rarity"
                    ? "var(--accent, #ffd166)"
                    : "transparent",
                color: colorMode === "rarity" ? "#000" : "inherit",
              }}
              title="Swarm Availability Heatmap (Rare -> Common)"
            >
              {t("torrents.detail.pieceMapRarity")}
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${colorMode === "files" ? "active" : ""}`}
              onClick={() => setColorMode("files")}
              style={{
                padding: "0.15rem 0.4rem",
                fontSize: "0.7rem",
                backgroundColor:
                  colorMode === "files"
                    ? "var(--accent, #ffd166)"
                    : "transparent",
                color: colorMode === "files" ? "#000" : "inherit",
              }}
              title="File Boundary Overlays"
            >
              {t("torrents.detail.pieceMapFileBoundaries")}
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
            height: "26px",
            backgroundColor: "rgba(0, 0, 0, 0.4)",
            borderRadius: "4px",
            overflow: "hidden",
            border: "1px solid var(--border-light)",
            display: "flex",
            alignItems: "center",
          }}
        >
          <canvas
            ref={barCanvasRef}
            onMouseMove={handleBarMouseMove}
            onMouseLeave={handleMouseLeave}
            style={{
              display: "block",
              width: "100%",
              height: "24px",
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
            border: "1px solid var(--border-light)",
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

      {/* File Boundary Selector List (when File Boundaries mode active) */}
      {colorMode === "files" && fileBoundaries.length > 0 && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "0.35rem",
            backgroundColor: "rgba(0, 0, 0, 0.2)",
            padding: "0.5rem",
            borderRadius: "4px",
            border: "1px solid var(--border-light)",
            maxHeight: "130px",
            overflowY: "auto",
          }}
        >
          <div
            style={{
              fontSize: "0.72rem",
              fontWeight: 600,
              color: "var(--text-secondary)",
              display: "flex",
              justifyContent: "space-between",
            }}
          >
            <span>
              {t("torrents.detail.pieceMapFilesCount", {
                count: fileBoundaries.length,
              })}
            </span>
            {selectedFileIndex !== null && (
              <button
                type="button"
                onClick={() => setSelectedFileIndex(null)}
                style={{
                  background: "none",
                  border: "none",
                  color: "var(--accent, #ffd166)",
                  fontSize: "0.7rem",
                  cursor: "pointer",
                  padding: 0,
                }}
              >
                Clear selection
              </button>
            )}
          </div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: "0.35rem" }}>
            {fileBoundaries.map((fb, idx) => {
              const isSelected = selectedFileIndex === idx;
              const isHovered = hoveredFileIndex === idx;
              const color = FILE_PALETTE[fb.colorIndex % FILE_PALETTE.length];
              const fileName = fb.file.path.split("/").pop() || fb.file.path;

              return (
                <button
                  key={fb.file.id || idx}
                  type="button"
                  onMouseEnter={() => setHoveredFileIndex(idx)}
                  onMouseLeave={() => setHoveredFileIndex(null)}
                  onClick={() => setSelectedFileIndex(isSelected ? null : idx)}
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.35rem",
                    padding: "0.2rem 0.5rem",
                    fontSize: "0.68rem",
                    borderRadius: "3px",
                    border: isSelected
                      ? "1px solid #fff"
                      : isHovered
                        ? `1px solid ${color}`
                        : "1px solid var(--border-light)",
                    backgroundColor:
                      isSelected || isHovered
                        ? "rgba(255,255,255,0.12)"
                        : "rgba(0,0,0,0.3)",
                    color: "#fff",
                    cursor: "pointer",
                  }}
                  title={`${fb.file.path} (Pieces ${fb.startPiece} - ${fb.endPiece})`}
                >
                  <span
                    style={{
                      width: "8px",
                      height: "8px",
                      borderRadius: "2px",
                      backgroundColor: color,
                    }}
                  />
                  <span
                    style={{
                      maxWidth: "160px",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {fileName}
                  </span>
                  <span
                    style={{ color: "var(--text-muted)", fontSize: "0.64rem" }}
                  >
                    ({formatBytes(fb.file.size)})
                  </span>
                </button>
              );
            })}
          </div>
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
        {colorMode === "status" && (
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
                  border: "1px solid var(--border)",
                }}
              />
              {t("torrents.detail.pieceMapMissingLegend", {
                count: Math.max(0, totalPieces - completedPieces),
              })}
            </span>
          </div>
        )}

        {colorMode === "rarity" && (
          <div
            style={{
              display: "flex",
              gap: "0.6rem",
              alignItems: "center",
              flexWrap: "wrap",
            }}
          >
            <span>{t("torrents.detail.pieceMapAvailability")}:</span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "rgba(255, 255, 255, 0.05)",
                }}
              />{" "}
              0
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#e74c3c",
                }}
              />{" "}
              1 (Rare)
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#e67e22",
                }}
              />{" "}
              2-3
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#f1c40f",
                }}
              />{" "}
              4-5
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#82c91e",
                }}
              />{" "}
              6-9
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#27ae60",
                }}
              />{" "}
              10+
            </span>
          </div>
        )}

        {colorMode === "files" && (
          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <span>
              {t("torrents.detail.pieceMapFilesCount", {
                count: fileBoundaries.length,
              })}
            </span>
          </div>
        )}

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
            {hoveredFiles.length > 0 &&
              ` • [${hoveredFiles[0].name}${hoveredFiles.length > 1 ? ` +${hoveredFiles.length - 1}` : ""}]`}
          </span>
        )}
      </div>
    </div>
  );
}

export default PieceMap;
