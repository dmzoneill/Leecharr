import React, { useState, useEffect } from "react";
import { useLocation } from "react-router";
import { TerminalView } from "../components/terminal/TerminalView";
import { useBitTorrentConfig } from "../api/hooks";

export function TerminalPage() {
  const location = useLocation();
  const queryParams = new URLSearchParams(location.search);
  const initialPath = queryParams.get("path");

  const { data: config } = useBitTorrentConfig();
  const [downloadDir, setDownloadDir] = useState<string>("/downloads");
  const [activePath, setActivePath] = useState<string>(initialPath || "/downloads");
  const [customPath, setCustomPath] = useState<string>("");

  useEffect(() => {
    if (config?.downloadDir) {
      setDownloadDir(config.downloadDir);
      if (!initialPath) {
        setActivePath(config.downloadDir);
      }
    }
  }, [config, initialPath]);

  const handleApplyCustom = (e: React.FormEvent) => {
    e.preventDefault();
    if (customPath.trim()) {
      setActivePath(customPath.trim());
    }
  };

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        gap: "1rem",
      }}
    >
      {/* Top Header Card */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          padding: "0.85rem 1.25rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <span style={{ fontSize: "1.5rem" }}>💻</span>
          <div>
            <h2 style={{ margin: 0, fontSize: "1.15rem", fontWeight: 600 }}>
              System Download CLI
            </h2>
            <p
              style={{
                margin: 0,
                fontSize: "0.8rem",
                color: "var(--text-muted)",
              }}
            >
              Interactive terminal with direct access to inspect download
              volumes, permissions, and file trees.
            </p>
          </div>
        </div>

        {/* Quick Directory Presets & Custom Path Input */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            flexWrap: "wrap",
          }}
        >
          <button
            type="button"
            className={`btn ${activePath === downloadDir ? "btn-primary" : "btn-outline"}`}
            style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
            onClick={() => setActivePath(downloadDir)}
          >
            📥 Downloads Root
          </button>

          <button
            type="button"
            className={`btn ${activePath === `${downloadDir}/incomplete` ? "btn-primary" : "btn-outline"}`}
            style={{ fontSize: "0.8rem", padding: "0.3rem 0.65rem" }}
            onClick={() => setActivePath(`${downloadDir}/incomplete`)}
          >
            ⏳ Incomplete
          </button>

          <form
            onSubmit={handleApplyCustom}
            style={{ display: "flex", alignItems: "center", gap: "0.3rem" }}
          >
            <input
              type="text"
              placeholder="Or enter path..."
              value={customPath}
              onChange={(e) => setCustomPath(e.target.value)}
              style={{
                fontSize: "0.8rem",
                padding: "0.3rem 0.6rem",
                borderRadius: "4px",
                border: "1px solid var(--border-light, #1c203b)",
                backgroundColor: "var(--bg-card, #131627)",
                color: "var(--text-primary, #f8f4ed)",
                width: "180px",
              }}
            />
            <button
              type="submit"
              className="btn btn-outline"
              style={{ fontSize: "0.8rem", padding: "0.3rem 0.6rem" }}
            >
              Go
            </button>
          </form>
        </div>
      </div>

      {/* Terminal View */}
      <div
        style={{
          flex: "1 1 auto",
          minHeight: "550px",
          height: "calc(100vh - 210px)",
        }}
      >
        <TerminalView
          key={activePath}
          cwd={activePath}
          title={`Shell: ${activePath}`}
          height="100%"
        />
      </div>
    </div>
  );
}

export default TerminalPage;
