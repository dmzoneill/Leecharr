export interface PathSegment {
  label: string;
  fullPath: string;
}

export function getPathSegments(path: string): PathSegment[] {
  if (!path) return [];
  const normalized = path.replace(/\\/g, "/");
  const isWindowsDrive = /^[a-zA-Z]:/.test(normalized);
  const segments = normalized.split("/").filter(Boolean);
  const result: PathSegment[] = [];

  for (let i = 0; i < segments.length; i++) {
    if (isWindowsDrive && i === 0) {
      result.push({
        label: segments[0],
        fullPath: segments[0] + "/",
      });
    } else if (isWindowsDrive) {
      result.push({
        label: segments[i],
        fullPath: segments[0] + "/" + segments.slice(1, i + 1).join("/"),
      });
    } else {
      result.push({
        label: segments[i],
        fullPath: "/" + segments.slice(0, i + 1).join("/"),
      });
    }
  }

  return result;
}
