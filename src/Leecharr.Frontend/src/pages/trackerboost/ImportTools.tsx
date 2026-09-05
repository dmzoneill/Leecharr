import { useState, useMemo } from "react";
import {
  useTrackerBoostSettings,
  useUpdateTrackerBoostSettings,
  useDownloadClients,
  useHarvestDownloadTrackers,
  useHarvestProwlarrTrackers,
  useHarvestFeedTrackers,
  useScanTrackerBoostTrackers,
  useBulkImportTrackerBoostTrackers,
} from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import type { TrackerBoostSettings } from "../../api/types";

export interface ImportToolsProps {
  showModal?: boolean;
  onCloseModal?: () => void;
}

export function ImportTools({ showModal, onCloseModal }: ImportToolsProps) {
  const { data: settings } = useTrackerBoostSettings();
  const updateSettings = useUpdateTrackerBoostSettings();
  const { data: downloadClients } = useDownloadClients();
  const { showToast } = useToast();

  const harvestDownloads = useHarvestDownloadTrackers();
  const harvestProwlarr = useHarvestProwlarrTrackers();
  const harvestFeeds = useHarvestFeedTrackers();
  const scanTrackers = useScanTrackerBoostTrackers();
  const bulkImportTrackers = useBulkImportTrackerBoostTrackers();

  const [localShowModal, setLocalShowModal] = useState(false);
  const [bulkImportText, setBulkImportText] = useState("");
  const [isBulkImporting, setIsBulkImporting] = useState(false);

  const isModalOpen = showModal ?? localShowModal;
  const handleClose = onCloseModal ?? (() => setLocalShowModal(false));

  const handleToggleSetting = (key: keyof TrackerBoostSettings) => {
    if (!settings) return;
    const updated = { ...settings, [key]: !settings[key] };
    updateSettings.mutate(updated, {
      onSuccess: () => {
        showToast("TrackerBoost settings updated", "success");
      },
    });
  };

  const handleHarvestDownloads = () => {
    harvestDownloads.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Harvested ${res.harvestedCount} new trackers from active downloads`, "success");
      },
      onError: (err) => {
        showToast(`Failed to harvest from downloads: ${err.message}`, "error");
      },
    });
  };

  const handleHarvestProwlarr = () => {
    harvestProwlarr.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Harvested ${res.harvestedCount} trackers from Prowlarr`, "success");
      },
      onError: (err) => {
        showToast(`Failed to harvest from Prowlarr: ${err.message}`, "error");
      },
    });
  };

  const handleHarvestFeeds = () => {
    harvestFeeds.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Harvested ${res.harvestedCount} trackers from public feeds`, "success");
      },
      onError: (err) => {
        showToast(`Failed to harvest from feeds: ${err.message}`, "error");
      },
    });
  };

  const handleScanAll = () => {
    scanTrackers.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Probed ${res.testedCount} tracker endpoints`, "success");
      },
      onError: (err) => {
        showToast(`Failed to probe trackers: ${err.message}`, "error");
      },
    });
  };

  const handleBulkImportTrackers = async () => {
    if (!bulkImportText.trim()) return;
    const lines = bulkImportText
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter((l) => l.startsWith("http://") || l.startsWith("https://") || l.startsWith("udp://"));

    if (lines.length === 0) {
      showToast("No valid http://, https://, or udp:// tracker URLs found.", "error");
      return;
    }

    setIsBulkImporting(true);
    try {
      const res = await bulkImportTrackers.mutateAsync({ trackersText: lines.join("\n") });
      handleClose();
      setBulkImportText("");
      showToast(`Successfully processed ${lines.length} trackers (${res.importedCount} added)!`, "success");
    } catch (err: any) {
      showToast(`Failed to bulk import trackers: ${err?.message || "Unknown error"}`, "error");
    } finally {
      setIsBulkImporting(false);
    }
  };

  const enabledClientsCount = useMemo(() => {
    return (downloadClients ?? []).filter((c) => c.enable).length;
  }, [downloadClients]);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      {/* Automation Toggles */}
      <div className="card" style={{ padding: "1.25rem" }}>
        <h3 style={{ margin: "0 0 0.5rem 0" }}>⚡ Automation & Background Optimization</h3>
        <p
          style={{
            fontSize: "0.85rem",
            color: "var(--text-muted)",
            margin: "0 0 1rem 0",
          }}
        >
          TrackerBoost runs as a background service to constantly discover new trackers, monitor
          health, and optimize swarms across Leecharr and connected download clients.
        </p>

        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={settings?.autoBoostEnabled ?? true}
              onChange={() => handleToggleSetting("autoBoostEnabled")}
              style={{ width: "1.1rem", height: "1.1rem" }}
            />
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                Automatic Background Swarm Boosting (Enabled by Default)
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                Periodically queries candidate trackers and automatically injects verified positive
                matches into active downloads.
              </div>
            </div>
          </label>

          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={settings?.autoHarvestEnabled ?? true}
              onChange={() => handleToggleSetting("autoHarvestEnabled")}
              style={{ width: "1.1rem", height: "1.1rem" }}
            />
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                Automatic Swarm Tracker Harvesting (Enabled by Default)
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                Continuously extracts and catalogues new public tracker endpoints from downloading
                torrents to grow the tracker database.
              </div>
            </div>
          </label>

          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={settings?.onlyVerified ?? true}
              onChange={() => handleToggleSetting("onlyVerified")}
              style={{ width: "1.1rem", height: "1.1rem" }}
            />
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                Scrape Verification Guard (Strict Mode)
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                Only injects trackers that respond with active seeders or leechers for the specific
                info_hash, preventing client clutter.
              </div>
            </div>
          </label>
        </div>
      </div>

      {/* Connected Download Agents */}
      <div className="card" style={{ padding: "1.25rem" }}>
        <h3 style={{ margin: "0 0 0.5rem 0" }}>Connected Download Agents</h3>
        <p
          style={{
            fontSize: "0.85rem",
            color: "var(--text-muted)",
            margin: "0 0 1rem 0",
          }}
        >
          TrackerBoost coordinates with your download clients (qBittorrent, Transmission, Deluge) to
          inject verified trackers into active physical downloads.
        </p>
        <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
          {(downloadClients ?? [])
            .filter((c) => c.enable)
            .map((client) => (
              <span
                key={client.id}
                className="badge badge-primary"
                style={{ padding: "0.4rem 0.75rem", fontSize: "0.85rem" }}
              >
                ⚡ {client.name} ({client.clientType})
              </span>
            ))}
          {enabledClientsCount === 0 && (
            <span style={{ fontSize: "0.85rem", color: "var(--warning)" }}>
              No download agents currently configured. Add qBittorrent or Transmission in Settings
              ⚙️ to boost real downloads.
            </span>
          )}
        </div>
      </div>

      {/* Discovery Feeds */}
      <div className="card" style={{ padding: "1.25rem" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: "0.5rem",
          }}
        >
          <h3 style={{ margin: "0" }}>Manual Discovery & Import Triggers</h3>
          <button className="btn btn-primary" onClick={() => setLocalShowModal(true)}>
            📥 Bulk Import Trackers
          </button>
        </div>
        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            flexWrap: "wrap",
            marginTop: "0.75rem",
          }}
        >
          <button
            className="btn btn-action"
            onClick={handleHarvestDownloads}
            disabled={harvestDownloads.isPending}
          >
            {harvestDownloads.isPending ? "⏳ Harvesting Swarms..." : "🔄 Harvest Live Swarms"}
          </button>
          <button
            className="btn btn-action"
            onClick={handleHarvestProwlarr}
            disabled={harvestProwlarr.isPending}
          >
            {harvestProwlarr.isPending ? "⏳ Syncing Prowlarr..." : "🔄 Sync Prowlarr Trackers"}
          </button>
          <button
            className="btn btn-action"
            onClick={handleHarvestFeeds}
            disabled={harvestFeeds.isPending}
          >
            {harvestFeeds.isPending ? "⏳ Syncing Feeds..." : "🌐 Sync Curated Feeds"}
          </button>
          <button
            className="btn btn-action"
            onClick={handleScanAll}
            disabled={scanTrackers.isPending}
          >
            {scanTrackers.isPending ? "⏳ Probing Trackers..." : "📡 Probe All Trackers"}
          </button>
        </div>
      </div>

      {/* BULK IMPORT MODAL */}
      {isModalOpen && (
        <div
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0, 0, 0, 0.75)",
            backdropFilter: "blur(6px)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
            padding: "1rem",
          }}
        >
          <div
            className="card"
            style={{
              width: "100%",
              maxWidth: "560px",
              padding: "1.5rem",
              borderRadius: "8px",
              display: "flex",
              flexDirection: "column",
              gap: "1rem",
              backgroundColor: "var(--bg-card)",
              boxShadow: "0 8px 32px rgba(0,0,0,0.5)",
              border: "1px solid var(--border)",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <h3 style={{ margin: 0 }}>📥 Bulk Import Tracker URLs</h3>
              <button
                type="button"
                className="btn btn-outline"
                style={{ padding: "0.2rem 0.5rem" }}
                onClick={handleClose}
              >
                ✕
              </button>
            </div>

            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-muted)",
                margin: 0,
              }}
            >
              Paste tracker announce URLs (one per line). Supported protocols: <code>udp://</code>,{" "}
              <code>http://</code>, <code>https://</code>.
            </p>

            <textarea
              className="form-control"
              rows={8}
              placeholder="udp://tracker.opentrackr.org:1337/announce&#10;http://tracker.example.com/announce&#10;udp://open.stealth.si:80/announce"
              value={bulkImportText}
              onChange={(e) => setBulkImportText(e.target.value)}
              style={{ fontFamily: "monospace", fontSize: "0.82rem" }}
            />

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button type="button" className="btn btn-action" onClick={handleClose}>
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleBulkImportTrackers}
                disabled={isBulkImporting || !bulkImportText.trim()}
              >
                {isBulkImporting ? "Importing Trackers..." : "Import Trackers"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default ImportTools;
