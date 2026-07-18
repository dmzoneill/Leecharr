import {
  useDownloadHistory,
  useArrConnections,
  useIndexers,
} from "../../api/hooks";
import { formatBytes, formatDate } from "../../utils/formatters";
import {
  getMediaDeepLink,
  getImdbUrl,
  getTmdbUrl,
  getProwlarrUrl,
} from "../../utils/arrLinks";
import { getTorrentBadges, calculateHnrStatus } from "../../utils/milestones";
import PieceMap from "../PieceMap";
import SeedingSimulator from "../SeedingSimulator";
import type { Torrent } from "../../api/types";
import { InfoRow } from "./shared";

export function DetailsTab({ torrent }: { torrent: Torrent }) {
  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();
  const { data: indexers } = useIndexers();

  // Find corresponding enriched history entry by infoHash or name match
  const historyMatch = history?.find(
    (h) =>
      (torrent.infoHash &&
        h.infoHash?.toLowerCase() === torrent.infoHash.toLowerCase()) ||
      h.title?.toLowerCase() === torrent.name?.toLowerCase(),
  );

  const meta = historyMatch?.metadata;
  const arrLink = historyMatch
    ? getMediaDeepLink(historyMatch, arrConnections)
    : null;
  const imdbUrl = getImdbUrl(meta?.imdbId, meta?.title || torrent.name);
  const tmdbUrl = getTmdbUrl(meta?.tmdbId, meta?.mediaType);
  const prowlarrUrl = getProwlarrUrl(indexers, meta?.title || torrent.name);

  const badges = getTorrentBadges(torrent);
  const hnr = calculateHnrStatus(torrent);

  const rows: [string, string][] = [
    ["Name", torrent.name],
    ["Info Hash", torrent.infoHash],
    ["Total Size", formatBytes(torrent.totalSize)],
    ["Pieces", `${torrent.pieceCount} x ${formatBytes(torrent.pieceLength)}`],
    ["Private", torrent.isPrivate ? "Yes" : "No"],
    ["Tracker", torrent.trackerUrl ?? "-"],
  ];
  if (torrent.creationDate)
    rows.push(["Created", formatDate(torrent.creationDate)]);
  if (torrent.createdBy) rows.push(["Created By", torrent.createdBy]);
  if (torrent.comment) rows.push(["Comment", torrent.comment]);
  if (torrent.sourcePath) rows.push(["Source Path", torrent.sourcePath]);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      {/* Arr & Metadata Integration Banner */}
      {(arrLink || meta || prowlarrUrl) && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "0.75rem",
            padding: "0.6rem 0.8rem",
            backgroundColor: "var(--bg-secondary, #222)",
            borderRadius: "6px",
            border: "1px solid var(--border-color, #333)",
          }}
        >
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            {meta?.posterUrl && (
              <img
                src={meta.posterUrl}
                alt=""
                style={{
                  width: "32px",
                  height: "46px",
                  objectFit: "cover",
                  borderRadius: "3px",
                }}
              />
            )}
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.85rem" }}>
                {meta?.title || torrent.name}{" "}
                {meta?.year ? `(${meta.year})` : ""}
              </div>
              {meta?.genres && (
                <div
                  style={{
                    fontSize: "0.7rem",
                    color: "var(--text-muted, #888)",
                  }}
                >
                  {meta.genres.slice(0, 3).join(", ")}
                </div>
              )}
            </div>
          </div>

          <div
            style={{
              display: "flex",
              gap: "0.4rem",
              alignItems: "center",
              flexWrap: "wrap",
            }}
          >
            {arrLink && (
              <a
                href={arrLink.url}
                target="_blank"
                rel="noopener noreferrer"
                className="btn btn-primary"
                style={{
                  fontSize: "0.75rem",
                  padding: "0.2rem 0.5rem",
                  textDecoration: "none",
                }}
                title={arrLink.label}
              >
                🔗 {arrLink.label} ↗
              </a>
            )}
            <a
              href={imdbUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="badge"
              style={{
                backgroundColor: "#f5c518",
                color: "#000",
                fontWeight: 700,
                fontSize: "0.7rem",
                padding: "0.2rem 0.45rem",
                textDecoration: "none",
              }}
              title="Open IMDb"
            >
              IMDb ↗
            </a>
            {tmdbUrl && (
              <a
                href={tmdbUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="badge"
                style={{
                  backgroundColor: "#01b4e4",
                  color: "#fff",
                  fontWeight: 700,
                  fontSize: "0.7rem",
                  padding: "0.2rem 0.45rem",
                  textDecoration: "none",
                }}
                title="Open TMDb"
              >
                TMDb ↗
              </a>
            )}
            {prowlarrUrl && (
              <a
                href={prowlarrUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="badge badge-secondary"
                style={{
                  fontSize: "0.7rem",
                  padding: "0.2rem 0.45rem",
                  textDecoration: "none",
                }}
                title="Search in Prowlarr"
              >
                Prowlarr ↗
              </a>
            )}
          </div>
        </div>
      )}

      {/* Gamification Badges & HNR Clearance Bar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.5rem",
          padding: "0.4rem 0.75rem",
          backgroundColor: "var(--bg-secondary, #222)",
          borderRadius: "4px",
          border: "1px solid var(--border-color, #333)",
          fontSize: "0.8rem",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.4rem",
            flexWrap: "wrap",
          }}
        >
          <span style={{ color: "var(--text-muted)", fontSize: "0.75rem" }}>
            Badges:
          </span>
          {badges.length === 0 ? (
            <span style={{ color: "var(--text-muted)", fontSize: "0.75rem" }}>
              Building ratio...
            </span>
          ) : (
            badges.map((b, i) => (
              <span
                key={i}
                className="badge"
                style={{
                  backgroundColor: b.color,
                  color: "#fff",
                  fontSize: "0.7rem",
                  padding: "0.1rem 0.35rem",
                }}
                title={b.title}
              >
                {b.icon} {b.label}
              </span>
            ))
          )}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
          <span style={{ color: "var(--text-muted)", fontSize: "0.75rem" }}>
            HNR:
          </span>
          <span
            className={`badge ${hnr.isCleared ? "badge-success" : "badge-warning"}`}
            style={{ fontSize: "0.7rem", padding: "0.1rem 0.35rem" }}
          >
            {hnr.label}
          </span>
        </div>
      </div>

      <PieceMap
        pieceCount={torrent.pieceCount}
        pieceLength={torrent.pieceLength}
        progress={torrent.progress}
        isSeeding={torrent.status === "Seeding"}
      />

      <div className="detail-panel-grid">
        {rows.map(([label, value]) => (
          <InfoRow key={label} label={label} value={value} mono />
        ))}
      </div>

      <SeedingSimulator
        currentUploaded={torrent.uploaded}
        totalSize={torrent.totalSize}
        currentRatio={torrent.ratio}
        currentUploadSpeed={torrent.uploadSpeed}
        seedingTimeSeconds={torrent.seedingTime}
      />
    </div>
  );
}
