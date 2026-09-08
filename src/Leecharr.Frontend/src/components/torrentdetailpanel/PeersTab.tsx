import { useRef } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useTranslation } from "../../i18n";
import { usePeers } from "../../api/hooks";
import { formatBytes, formatSpeed } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";
import CountryFlag from "../CountryFlag";
import PeerClientBadge from "../PeerClientBadge";
import type { Torrent } from "../../api/types";

export function PeersTab({
  torrent,
  torrentId,
}: {
  torrent?: Torrent;
  torrentId?: number;
}) {
  const { t } = useTranslation();
  const effectiveId = torrentId ?? torrent?.id ?? 0;
  const { data: peers, isLoading, isError } = usePeers(effectiveId);
  const tableContainerRef = useRef<HTMLDivElement>(null);

  const peerList = peers || [];

  const rowVirtualizer = useVirtualizer({
    count: peerList.length,
    getScrollElement: () => tableContainerRef.current,
    estimateSize: () => 38,
    overscan: 10,
  });

  const virtualRows = rowVirtualizer.getVirtualItems();
  const totalHeight = rowVirtualizer.getTotalSize();
  const paddingTop = virtualRows.length > 0 ? virtualRows[0].start : 0;
  const paddingBottom =
    virtualRows.length > 0
      ? totalHeight - virtualRows[virtualRows.length - 1].end
      : 0;

  if (isLoading)
    return <PanelLoading>{t("torrents.detail.loadingPeers")}</PanelLoading>;
  if (isError)
    return <PanelEmpty>{t("torrents.detail.failedToLoadPeers")}</PanelEmpty>;
  if (!peers || peers.length === 0)
    return <PanelEmpty>{t("torrents.detail.noPeers")}</PanelEmpty>;

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
      }}
    >
      {torrent?.isPrivate && (
        <div
          style={{
            padding: "0.4rem 0.6rem",
            marginBottom: "0.5rem",
            borderRadius: "4px",
            fontSize: "0.72rem",
            backgroundColor: "rgba(239, 68, 68, 0.12)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            color: "#fca5a5",
            display: "flex",
            alignItems: "center",
            gap: "6px",
            flexShrink: 0,
          }}
        >
          <i className="fas fa-lock" />
          <span>{t("torrents.detail.peersPrivateBanner")}</span>
        </div>
      )}
      <div
        ref={tableContainerRef}
        className="detail-panel-table-wrap"
        style={{ flex: 1, overflow: "auto", minHeight: 0 }}
      >
        <table
          className="torrent-table"
          style={{ width: "100%", borderCollapse: "collapse" }}
        >
          <thead>
            <tr
              style={{
                position: "sticky",
                top: 0,
                backgroundColor: "var(--bg-primary, #10111A)",
                zIndex: 2,
              }}
            >
              <th className="torrent-table-th">
                {t("torrents.detail.colAddress")}
              </th>
              <th className="torrent-table-th">
                {t("torrents.detail.colClient")}
              </th>
              <th className="torrent-table-th">
                {t("torrents.detail.colProgress")}
              </th>
              <th className="torrent-table-th">
                {t("torrents.detail.colUpSpeed")}
              </th>
              <th className="torrent-table-th">
                {t("torrents.detail.colDownSpeed")}
              </th>
              <th className="torrent-table-th">
                {t("torrents.detail.colUploaded")}
              </th>
              <th className="torrent-table-th">
                {t("torrents.detail.colDownloaded")}
              </th>
              <th className="torrent-table-th">
                {t("torrents.detail.colFlags")}
              </th>
            </tr>
          </thead>
          <tbody>
            {paddingTop > 0 && (
              <tr>
                <td
                  colSpan={8}
                  style={{ height: `${paddingTop}px`, padding: 0, border: 0 }}
                />
              </tr>
            )}
            {virtualRows.map((virtualRow) => {
              const p = peerList[virtualRow.index];
              return (
                <tr
                  key={`${p.ip}:${p.port || p.id}`}
                  className="torrent-table-row"
                >
                  <td className="mono" style={{ whiteSpace: "nowrap" }}>
                    <div
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.4rem",
                      }}
                    >
                      <CountryFlag
                        ip={p.ip}
                        countryCode={p.countryCode}
                        countryName={p.countryName}
                      />
                      <span>
                        {p.ip}:{p.port}
                      </span>
                    </div>
                  </td>
                  <td>
                    <PeerClientBadge client={p.client} flags={p.flags} />
                  </td>
                  <td>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.4rem",
                      }}
                    >
                      <div
                        style={{
                          width: "45px",
                          height: "6px",
                          backgroundColor: "rgba(255,255,255,0.1)",
                          borderRadius: "3px",
                          overflow: "hidden",
                        }}
                      >
                        <div
                          style={{
                            width: `${Math.min(100, Math.max(0, p.progress * 100))}%`,
                            height: "100%",
                            backgroundColor: "var(--success, #27ae60)",
                          }}
                        />
                      </div>
                      <span style={{ fontSize: "0.78rem" }}>
                        {(p.progress * 100).toFixed(1)}%
                      </span>
                    </div>
                  </td>
                  <td>{formatSpeed(p.uploadSpeed)}</td>
                  <td>{formatSpeed(p.downloadSpeed)}</td>
                  <td>{formatBytes(p.uploaded)}</td>
                  <td>{formatBytes(p.downloaded)}</td>
                  <td className="mono" style={{ fontSize: "0.75rem" }}>
                    {p.flags}
                  </td>
                </tr>
              );
            })}
            {paddingBottom > 0 && (
              <tr>
                <td
                  colSpan={8}
                  style={{ height: `${paddingBottom}px`, padding: 0, border: 0 }}
                />
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
