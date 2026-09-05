import React from "react";
import { TerminalView } from "../terminal/TerminalView";
import type { Torrent } from "../../api/types";

export interface CliTabProps {
  torrent: Torrent;
}

export function CliTab({ torrent }: CliTabProps) {
  const savePath = torrent.savePath || torrent.sourcePath || "/downloads";

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        gap: "0.75rem",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      {/* Context Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.5rem",
          padding: "0.6rem 0.9rem",
          backgroundColor: "rgba(23, 27, 53, 0.6)",
          border: "1px solid var(--border-light, #1c203b)",
          borderRadius: "6px",
          fontSize: "0.82rem",
          flexShrink: 0,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}>
          <span style={{ fontSize: "1.1rem" }}>💻</span>
          <div>
            <div
              style={{ fontWeight: 600, color: "var(--text-primary, #f8f4ed)" }}
            >
              Torrent Working Directory
            </div>
            <div
              style={{
                color: "var(--text-muted, #8a879e)",
                fontSize: "0.75rem",
              }}
            >
              Shell session dropped directly into downloaded files location
            </div>
          </div>
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            fontSize: "0.75rem",
            color: "var(--text-secondary)",
          }}
        >
          <span>💡 Quick tips:</span>
          <code
            style={{
              backgroundColor: "rgba(255,255,255,0.06)",
              padding: "0.1rem 0.35rem",
              borderRadius: "3px",
            }}
          >
            ls -lh
          </code>
          <code
            style={{
              backgroundColor: "rgba(255,255,255,0.06)",
              padding: "0.1rem 0.35rem",
              borderRadius: "3px",
            }}
          >
            du -sh *
          </code>
          <code
            style={{
              backgroundColor: "rgba(255,255,255,0.06)",
              padding: "0.1rem 0.35rem",
              borderRadius: "3px",
            }}
          >
            file *
          </code>
        </div>
      </div>

      {/* Terminal View */}
      <div style={{ flex: "1 1 auto", minHeight: 0, overflow: "hidden" }}>
        <TerminalView cwd={savePath} title={`CLI: ${torrent.name}`} />
      </div>
    </div>
  );
}

export default CliTab;
