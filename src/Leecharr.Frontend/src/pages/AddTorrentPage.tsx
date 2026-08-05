import React from "react";
import AddTorrentForm from "../components/AddTorrentForm";

interface AddTorrentPageProps {
  onSuccess?: () => void;
}

export function AddTorrentPage({ onSuccess }: AddTorrentPageProps) {
  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <h1 className="page-heading" style={{ margin: 0 }}>
            Add Torrent
          </h1>
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
          border: "1px solid var(--border-light, #1c203b)",
          flex: "1 1 auto",
          display: "flex",
          flexDirection: "column",
          minHeight: 0,
          overflow: "hidden",
          width: "100%",
        }}
      >
        <AddTorrentForm isModal={false} onSuccess={onSuccess} />
      </div>
    </div>
  );
}

export default AddTorrentPage;
