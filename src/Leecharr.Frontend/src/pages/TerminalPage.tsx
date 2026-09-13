import { useTranslation } from "../i18n";
import React, { useState, useEffect } from "react";
import { useLocation } from "react-router";
import { TerminalView } from "../components/terminal/TerminalView";
import { useBitTorrentConfig } from "../api/hooks";

export function TerminalPage() {
  const { t } = useTranslation();
  const location = useLocation();
  const queryParams = new URLSearchParams(location.search);
  const initialPath = queryParams.get("path");

  const { data: config } = useBitTorrentConfig();
  const [downloadDir, setDownloadDir] = useState<string>("/downloads");
  const [activePath, setActivePath] = useState<string>(
    initialPath || "/downloads",
  );
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
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        gap: "1rem",
        padding: "1.5rem",
        boxSizing: "border-box",
      }}
    >
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
          flexShrink: 0,
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
            <span>💻</span> {t("terminal.title")}
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {t("terminal.executeCommands")}
          </p>
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
            className={`btn ${activePath === downloadDir ? "btn-primary" : "btn-secondary"}`}
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={() => setActivePath(downloadDir)}
          >
            {t("terminal.downloadsRoot")}
          </button>

          <button
            type="button"
            className={`btn ${activePath === `${downloadDir}/incomplete` ? "btn-primary" : "btn-secondary"}`}
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={() => setActivePath(`${downloadDir}/incomplete`)}
          >
            {t("terminal.incomplete")}
          </button>

          <form
            onSubmit={handleApplyCustom}
            style={{ display: "flex", alignItems: "center", gap: "0.3rem" }}
          >
            <input
              type="text"
              placeholder={t("terminal.enterPath")}
              value={customPath}
              onChange={(e) => setCustomPath(e.target.value)}
              className="input"
              style={{
                fontSize: "0.85rem",
                padding: "0.35rem 0.6rem",
                width: "180px",
              }}
            />
            <button
              type="submit"
              className="btn btn-outline"
              style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            >
              {t("terminal.go")}
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
          title={t("terminal.shellTitle", "Shell: {path}", {
            path: activePath,
          })}
          height="100%"
        />
      </div>
    </div>
  );
}

export default TerminalPage;
