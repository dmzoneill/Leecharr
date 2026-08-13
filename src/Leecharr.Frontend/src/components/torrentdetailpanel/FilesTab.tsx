import { useTorrentFiles } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";
import type { Torrent } from "../../api/types";

export function FilesTab({
  torrent,
  torrentId,
}: {
  torrent?: Torrent;
  torrentId?: number;
}) {
  const effectiveId = torrentId ?? torrent?.id ?? 0;
  const { data: files, isLoading, isError } = useTorrentFiles(effectiveId);

  if (isLoading) return <PanelLoading>Loading files...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load files.</PanelEmpty>;
  if (!files || files.length === 0) return <PanelEmpty>No files</PanelEmpty>;

  return (
    <div className="detail-panel-table-wrap">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">Path</th>
            <th className="torrent-table-th">Size</th>
            <th className="torrent-table-th">Progress</th>
          </tr>
        </thead>
        <tbody>
          {files.map((f) => (
            <tr key={f.id} className="torrent-table-row">
              <td className="mono">{f.path}</td>
              <td>{formatBytes(f.size)}</td>
              <td>{((f.progress ?? 1.0) * 100).toFixed(1)}%</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
