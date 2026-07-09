import { useTorrentFiles } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";

export function FilesTab({ torrentId }: { torrentId: number }) {
  const { data: files, isLoading, isError } = useTorrentFiles(torrentId);

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
          </tr>
        </thead>
        <tbody>
          {files.map((f) => (
            <tr key={f.id} className="torrent-table-row">
              <td className="mono">{f.path}</td>
              <td>{formatBytes(f.size)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
