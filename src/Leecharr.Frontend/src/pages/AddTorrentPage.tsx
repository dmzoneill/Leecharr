import { useTranslation } from "../i18n";
import AddTorrentForm from "../components/AddTorrentForm";

interface AddTorrentPageProps {
  onSuccess?: () => void;
}

export function AddTorrentPage({ onSuccess }: AddTorrentPageProps) {
  const { t } = useTranslation();
  return (
    <div
      className="content-area add-torrent-page"
      style={{
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
      }}
    >
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>➕</span> {t("addTorrent.title", "Add Torrent")}
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {t(
              "addTorrent.subtitle",
              "Upload torrent files or download via magnet link",
            )}
          </p>
        </div>
      </div>

      <div
        className="card"
        style={{
          borderRadius: "8px",
          padding: "1.5rem",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          backgroundColor: "var(--bg-secondary, #171b35)",
          border: "1px solid var(--border-light)",
          flex: "1 1 auto",
          display: "flex",
          flexDirection: "column",
          minHeight: 0,
          width: "100%",
        }}
      >
        <AddTorrentForm isModal={false} onSuccess={onSuccess} />
      </div>
    </div>
  );
}

export default AddTorrentPage;
