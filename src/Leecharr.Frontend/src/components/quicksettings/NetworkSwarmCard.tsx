import React from "react";
import {
  useNetworkConfig,
  useSaveNetworkConfig,
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useNetworkStatus,
} from "../../api/hooks";
import { UsersIcon, WifiIcon } from "../icons/UIIcons";
import { useToast } from "../../context/ToastContext";

export const NetworkSwarmCard: React.FC = () => {
  const { data: netConfig, isLoading: netLoading } = useNetworkConfig();
  const saveNetMutation = useSaveNetworkConfig();

  const { data: btConfig, isLoading: btLoading } = useBitTorrentConfig();
  const saveBtMutation = useSaveBitTorrentConfig();

  const { data: netStatus } = useNetworkStatus();
  const { showToast } = useToast();

  const handleNetUpdate = (
    updates: Partial<import("../../api/types").NetworkConfig>,
  ) => {
    if (!netConfig) return;
    saveNetMutation.mutate(
      {
        ...netConfig,
        ...updates,
      },
      {
        onError: (err: any) => {
          showToast(
            `Failed to update network settings: ${err.message}`,
            "error",
          );
        },
      },
    );
  };

  const handleBtUpdate = (
    updates: Partial<import("../../api/types").BitTorrentConfig>,
  ) => {
    if (!btConfig) return;
    saveBtMutation.mutate(
      {
        ...btConfig,
        ...updates,
      },
      {
        onError: (err: any) => {
          showToast(
            `Failed to update protocol settings: ${err.message}`,
            "error",
          );
        },
      },
    );
  };

  if (netLoading || btLoading || !netConfig || !btConfig) {
    return (
      <div className="quick-card loading">
        <div className="quick-card-header">
          <span className="quick-card-title">🛡️ Network & Swarm</span>
        </div>
        <div className="quick-card-body">Loading network settings...</div>
      </div>
    );
  }

  const globalConns = netConfig.maxGlobalConnections ?? 300;
  const perTorrentConns = netConfig.maxPerTorrentConnections ?? 50;
  const vpnKillSwitch = netConfig.enableVpnKillSwitch ?? false;
  const dht = btConfig.enableDht ?? true;
  const pex = btConfig.enablePex ?? true;
  const lpd = btConfig.enableLpd ?? true;

  const activeInterface =
    netStatus?.networkInterface || netConfig.bindInterface || "All";

  return (
    <div className="quick-card">
      <div className="quick-card-header">
        <span className="quick-card-title">🛡️ Network & Swarms</span>
        <button
          type="button"
          className={`quick-vpn-badge ${vpnKillSwitch ? "vpn-active" : ""}`}
          onClick={() =>
            handleNetUpdate({ enableVpnKillSwitch: !vpnKillSwitch })
          }
          title={
            vpnKillSwitch
              ? `VPN Kill Switch Active (Bound: ${activeInterface}) - Click to disable`
              : "VPN Kill Switch Disabled - Click to enable"
          }
        >
          {vpnKillSwitch ? "🛡️ Kill Switch ON" : "Kill Switch OFF"}
        </button>
      </div>

      <div className="quick-card-body">
        {/* Max Global Connections */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <UsersIcon size={12} />
            <span>Max Conns:</span>
          </div>
          <div className="quick-inline-inputs">
            <div
              className="quick-mini-input-group"
              title="Global maximum peer connections"
            >
              <span className="quick-mini-label">Global:</span>
              <select
                className="quick-select"
                value={globalConns}
                onChange={(e) =>
                  handleNetUpdate({
                    maxGlobalConnections: parseInt(e.target.value, 10),
                  })
                }
              >
                <option value={100}>100</option>
                <option value={200}>200</option>
                <option value={300}>300</option>
                <option value={500}>500</option>
                <option value={1000}>1000</option>
              </select>
            </div>
            <div
              className="quick-mini-input-group"
              title="Per-torrent maximum peer connections"
            >
              <span className="quick-mini-label">Per-Torrent:</span>
              <select
                className="quick-select"
                value={perTorrentConns}
                onChange={(e) =>
                  handleNetUpdate({
                    maxPerTorrentConnections: parseInt(e.target.value, 10),
                  })
                }
              >
                <option value={20}>20</option>
                <option value={50}>50</option>
                <option value={80}>80</option>
                <option value={100}>100</option>
                <option value={200}>200</option>
              </select>
            </div>
          </div>
        </div>

        {/* Protocol Switches */}
        <div className="quick-control-row">
          <div className="quick-control-label">
            <WifiIcon size={12} />
            <span>Protocols:</span>
          </div>
          <div className="quick-protocol-chips">
            <label
              className={`protocol-chip ${dht ? "active" : ""}`}
              title="Distributed Hash Table (Trackerless Swarms)"
            >
              <input
                type="checkbox"
                checked={dht}
                onChange={(e) =>
                  handleBtUpdate({ enableDht: e.target.checked })
                }
              />
              <span>DHT</span>
            </label>
            <label
              className={`protocol-chip ${pex ? "active" : ""}`}
              title="Peer Exchange (Fast Swarm Expansion)"
            >
              <input
                type="checkbox"
                checked={pex}
                onChange={(e) =>
                  handleBtUpdate({ enablePex: e.target.checked })
                }
              />
              <span>PEX</span>
            </label>
            <label
              className={`protocol-chip ${lpd ? "active" : ""}`}
              title="Local Peer Discovery (LAN Multicast)"
            >
              <input
                type="checkbox"
                checked={lpd}
                onChange={(e) =>
                  handleBtUpdate({ enableLpd: e.target.checked })
                }
              />
              <span>LPD</span>
            </label>
          </div>
        </div>
      </div>
    </div>
  );
};

export default NetworkSwarmCard;
