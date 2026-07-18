import { useState, useEffect } from "react";
import { useUpdateTorrent } from "../../api/hooks";
import type { Torrent } from "../../api/types";

const PRIORITY_OPTIONS = [
  { value: "0", label: "Low" },
  { value: "1", label: "Normal" },
  { value: "2", label: "High" },
];

export function OptionsTab({ torrent }: { torrent: Torrent }) {
  const updateTorrent = useUpdateTorrent();
  const [priority, setPriority] = useState(String(torrent.priority));
  const [uploadLimit, setUploadLimit] = useState(torrent.uploadLimit);
  const [downloadLimit, setDownloadLimit] = useState(torrent.downloadLimit);
  const [superSeeding, setSuperSeeding] = useState(torrent.superSeeding);
  const [forceStart, setForceStart] = useState(torrent.forceStart);
  const [sequentialDownload, setSequentialDownload] = useState(
    torrent.sequentialDownload,
  );
  const [active, setActive] = useState(torrent.active);
  const [label, setLabel] = useState(torrent.label ?? "");
  const [uploadSpeed, setUploadSpeed] = useState(torrent.uploadSpeed);
  const [downloadSpeed, setDownloadSpeed] = useState(torrent.downloadSpeed);
  const [announceInterval, setAnnounceInterval] = useState(
    torrent.announceInterval,
  );
  const [nextUpdate, setNextUpdate] = useState(torrent.nextUpdate);
  const [threshold, setThreshold] = useState(torrent.threshold);
  const [smallTorrentLimit, setSmallTorrentLimit] = useState(
    torrent.smallTorrentLimit,
  );
  const [uploaded, setUploaded] = useState(torrent.uploaded);
  const [downloaded, setDownloaded] = useState(torrent.downloaded);
  const [sessionUploaded, setSessionUploaded] = useState(
    torrent.sessionUploaded,
  );
  const [sessionDownloaded, setSessionDownloaded] = useState(
    torrent.sessionDownloaded,
  );
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (dirty) return;
    setPriority(String(torrent.priority));
    setUploadLimit(torrent.uploadLimit);
    setDownloadLimit(torrent.downloadLimit);
    setSuperSeeding(torrent.superSeeding);
    setForceStart(torrent.forceStart);
    setSequentialDownload(torrent.sequentialDownload);
    setActive(torrent.active);
    setLabel(torrent.label ?? "");
    setUploadSpeed(torrent.uploadSpeed);
    setDownloadSpeed(torrent.downloadSpeed);
    setAnnounceInterval(torrent.announceInterval);
    setNextUpdate(torrent.nextUpdate);
    setThreshold(torrent.threshold);
    setSmallTorrentLimit(torrent.smallTorrentLimit);
    setUploaded(torrent.uploaded);
    setDownloaded(torrent.downloaded);
    setSessionUploaded(torrent.sessionUploaded);
    setSessionDownloaded(torrent.sessionDownloaded);
  }, [torrent, dirty]);

  const handleSave = () => {
    updateTorrent.mutate(
      {
        ...torrent,
        priority: parseInt(priority, 10),
        uploadLimit,
        downloadLimit,
        superSeeding,
        forceStart,
        sequentialDownload,
        active,
        label: label || null,
        uploadSpeed,
        downloadSpeed,
        announceInterval,
        nextUpdate,
        threshold,
        smallTorrentLimit,
        uploaded,
        downloaded,
        sessionUploaded,
        sessionDownloaded,
      },
      { onSuccess: () => setDirty(false) },
    );
  };

  const mark =
    <T,>(setter: (v: T) => void) =>
    (v: T) => {
      setter(v);
      setDirty(true);
    };
  const numChange =
    (setter: (v: number) => void) => (e: React.ChangeEvent<HTMLInputElement>) =>
      mark(setter)(parseInt(e.target.value, 10) || 0);

  return (
    <div className="detail-panel-options">
      <div className="options-section-title">Transfer</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Priority</label>
          <select
            className="form-select"
            value={priority}
            onChange={(e) => mark(setPriority)(e.target.value)}
          >
            {PRIORITY_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Upload Limit (KB/s)</label>
          <input
            type="number"
            className="form-input"
            value={uploadLimit}
            onChange={numChange(setUploadLimit)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Download Limit (KB/s)</label>
          <input
            type="number"
            className="form-input"
            value={downloadLimit}
            onChange={numChange(setDownloadLimit)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Upload Speed (B/s)</label>
          <input
            type="number"
            className="form-input"
            value={uploadSpeed}
            onChange={numChange(setUploadSpeed)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Download Speed (B/s)</label>
          <input
            type="number"
            className="form-input"
            value={downloadSpeed}
            onChange={numChange(setDownloadSpeed)}
            min={0}
          />
        </div>
      </div>

      <div className="options-section-title">Seeding</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Active</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={active}
              onChange={(e) => mark(setActive)(e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Super Seeding</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={superSeeding}
              onChange={(e) => mark(setSuperSeeding)(e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Force Start</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={forceStart}
              onChange={(e) => mark(setForceStart)(e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Sequential Download</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={sequentialDownload}
              onChange={(e) => mark(setSequentialDownload)(e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Label</label>
          <input
            type="text"
            className="form-input"
            value={label}
            onChange={(e) => mark(setLabel)(e.target.value)}
            placeholder="e.g. movies"
          />
        </div>
      </div>

      <div className="options-section-title">Simulation</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Announce Interval (s)</label>
          <input
            type="number"
            className="form-input"
            value={announceInterval}
            onChange={numChange(setAnnounceInterval)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Next Update (s)</label>
          <input
            type="number"
            className="form-input"
            value={nextUpdate}
            onChange={numChange(setNextUpdate)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Threshold</label>
          <input
            type="number"
            className="form-input"
            value={threshold}
            onChange={numChange(setThreshold)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Small Torrent Limit</label>
          <input
            type="number"
            className="form-input"
            value={smallTorrentLimit}
            onChange={numChange(setSmallTorrentLimit)}
            min={0}
          />
        </div>
      </div>

      <div className="options-section-title">Totals</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Total Uploaded</label>
          <input
            type="number"
            className="form-input"
            value={uploaded}
            onChange={numChange(setUploaded)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Total Downloaded</label>
          <input
            type="number"
            className="form-input"
            value={downloaded}
            onChange={numChange(setDownloaded)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Session Uploaded</label>
          <input
            type="number"
            className="form-input"
            value={sessionUploaded}
            onChange={numChange(setSessionUploaded)}
            min={0}
          />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Session Downloaded</label>
          <input
            type="number"
            className="form-input"
            value={sessionDownloaded}
            onChange={numChange(setSessionDownloaded)}
            min={0}
          />
        </div>
      </div>

      <div className="form-actions">
        <button
          className="btn btn-success btn-small"
          onClick={handleSave}
          disabled={!dirty || updateTorrent.isPending}
        >
          {updateTorrent.isPending ? "Saving..." : "Save"}
        </button>
      </div>
    </div>
  );
}
