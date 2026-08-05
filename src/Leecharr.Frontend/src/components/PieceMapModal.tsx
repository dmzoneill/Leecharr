import React, { useEffect, useRef } from "react";
import { Torrent } from "../api/types";

interface PieceMapModalProps {
  torrent: Torrent;
  onClose: () => void;
}

export const PieceMapModal: React.FC<PieceMapModalProps> = ({
  torrent,
  onClose,
}) => {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const totalPieces = torrent.pieceCount || 100;
    const progressPieces = Math.floor((torrent.progress || 0) * totalPieces);

    const cols = Math.ceil(Math.sqrt(totalPieces * 2));
    const rows = Math.ceil(totalPieces / cols);

    const pieceWidth = Math.max(4, Math.floor(canvas.width / cols));
    const pieceHeight = Math.max(4, Math.floor(canvas.height / rows));

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    for (let i = 0; i < totalPieces; i++) {
      const col = i % cols;
      const row = Math.floor(i / cols);

      const x = col * pieceWidth;
      const y = row * pieceHeight;

      if (i < progressPieces) {
        ctx.fillStyle = "#ffd166"; // Finished piece (Warm Gold)
      } else if (i === progressPieces && torrent.status === "downloading") {
        ctx.fillStyle = "#38bdf8"; // Currently downloading (Sky Blue)
      } else {
        ctx.fillStyle = "#23284b"; // Missing piece (Deep Indigo)
      }

      ctx.fillRect(x + 0.5, y + 0.5, pieceWidth - 1, pieceHeight - 1);
    }
  }, [torrent]);

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-content piece-map-modal"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h3>Piece Map &mdash; {torrent.name}</h3>
          <button className="btn-close" onClick={onClose}>
            &times;
          </button>
        </div>
        <div className="modal-body">
          <div className="piece-map-stats">
            <span>
              <strong>Total Pieces:</strong> {torrent.pieceCount || "Unknown"}
            </span>
            <span>
              <strong>Piece Length:</strong>{" "}
              {Math.round((torrent.pieceLength || 0) / 1024)} KB
            </span>
            <span>
              <strong>Downloaded:</strong>{" "}
              {Math.round((torrent.progress || 0) * 100)}%
            </span>
          </div>
          <div className="canvas-wrapper">
            <canvas
              ref={canvasRef}
              width={600}
              height={260}
              className="piece-canvas"
            />
          </div>
          <div className="piece-legend">
            <span className="legend-item">
              <span className="legend-box piece-done"></span> Completed
            </span>
            <span className="legend-item">
              <span className="legend-box piece-active"></span> In-Flight
            </span>
            <span className="legend-item">
              <span className="legend-box piece-missing"></span> Missing
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
