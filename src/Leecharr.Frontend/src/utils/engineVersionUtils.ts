import type {
  TorrentEngine,
  TorrentEngineCapabilities,
  SwitchEngineRequest,
} from "../api/types";

export function resolveActiveEngineVersion(engine: TorrentEngine): string {
  return engine.activeVersion || engine.version || "1.0.0";
}

export function getSupportedEngineVersions(engine: TorrentEngine): string[] {
  if (engine.supportedVersions && engine.supportedVersions.length > 0) {
    return engine.supportedVersions;
  }
  return engine.version ? [engine.version] : [];
}

export function hasMultipleEngineVersions(engine: TorrentEngine): boolean {
  return getSupportedEngineVersions(engine).length > 1;
}

export function getEngineVersionCapabilities(
  engine: TorrentEngine,
  version?: string,
): TorrentEngineCapabilities {
  if (version && engine.versionCapabilities?.[version]) {
    return engine.versionCapabilities[version];
  }
  return engine.capabilities;
}

export function isEngineVersionActive(
  engine: TorrentEngine,
  version: string,
  currentActiveEngineId: string,
): boolean {
  const isEngineActive =
    engine.engineId.toLowerCase() === currentActiveEngineId.toLowerCase();
  const activeVer = resolveActiveEngineVersion(engine);
  return isEngineActive && activeVer === version;
}

export function buildEngineProbeUrl(engineId: string, version?: string): string {
  const params = version ? `?version=${encodeURIComponent(version)}` : "";
  return `/torrentengine/${encodeURIComponent(engineId)}/probe${params}`;
}

export function buildEngineSwitchUrl(version?: string): string {
  const params = version ? `?version=${encodeURIComponent(version)}` : "";
  return `/torrentengine/switch${params}`;
}

export function formatSwitchEngineRequest(
  engineId: string,
  version?: string,
  preserveTransfers: boolean = true,
): SwitchEngineRequest {
  return {
    engineId,
    version: version || undefined,
    preserveTransfers,
  };
}
