export interface VisualBlock {
  startIndex: number;
  endIndex: number;
  status: "complete" | "missing" | "active";
  completedCount: number;
  totalInBlock: number;
}

// Precomputed 256-entry lookup table for popcount (number of set bits in a byte)
export const POPCOUNT_8 = new Uint8Array(256);
for (let i = 0; i < 256; i++) {
  let c = 0;
  let v = i;
  while (v > 0) {
    c += v & 1;
    v >>= 1;
  }
  POPCOUNT_8[i] = c;
}

/**
 * Decodes a base64-encoded bitfield string into a compact Uint8Array.
 */
export function decodeBase64Bitfield(
  base64: string | null | undefined,
): Uint8Array | null {
  if (!base64 || typeof base64 !== "string") return null;
  try {
    const binary = atob(base64);
    const len = binary.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
  } catch {
    return null;
  }
}

/**
 * Sets a contiguous range of piece bits in-place in a Uint8Array bitfield.
 */
export function setPieceRangeInPlace(
  target: Uint8Array,
  start: number,
  end: number,
): void {
  if (start < 0 || end < start) return;
  const startByte = start >> 3;
  const endByte = end >> 3;
  if (startByte >= target.length) return;

  const boundedEndByte = Math.min(endByte, target.length - 1);
  const startBit = start & 7;
  const endBit = boundedEndByte === endByte ? end & 7 : 7;

  if (startByte === boundedEndByte) {
    const mask = (0xff >> startBit) & ((0xff << (7 - endBit)) & 0xff) & 0xff;
    target[startByte] |= mask;
  } else {
    const startMask = (0xff >> startBit) & 0xff;
    target[startByte] |= startMask;

    if (boundedEndByte > startByte + 1) {
      target.fill(0xff, startByte + 1, boundedEndByte);
    }

    const endMask = (0xff << (7 - endBit)) & 0xff;
    target[boundedEndByte] |= endMask;
  }
}

/**
 * Sets multiple ranges of piece bits in-place in a Uint8Array bitfield.
 */
export function setPieceRangesInPlace(
  target: Uint8Array,
  ranges: Array<[number, number]> | number[][],
): void {
  if (!ranges || ranges.length === 0) return;
  for (let i = 0; i < ranges.length; i++) {
    const r = ranges[i];
    if (Array.isArray(r) && r.length >= 2) {
      setPieceRangeInPlace(target, r[0], r[1]);
    }
  }
}

/**
 * Sets a single piece bit in-place in a Uint8Array bitfield.
 */
export function setPieceBitInPlace(
  target: Uint8Array,
  pieceIndex: number,
): void {
  if (pieceIndex < 0) return;
  const byteIdx = pieceIndex >> 3;
  if (byteIdx < target.length) {
    const bitOffset = 7 - (pieceIndex & 7);
    target[byteIdx] |= 1 << bitOffset;
  }
}

/**
 * Sets multiple piece bits in-place in a Uint8Array bitfield.
 */
export function setPieceBitsInPlace(
  target: Uint8Array,
  pieceIndices: number[],
): void {
  if (!pieceIndices || pieceIndices.length === 0) return;
  for (let i = 0; i < pieceIndices.length; i++) {
    const idx = pieceIndices[i];
    if (typeof idx === "number" && idx >= 0) {
      const byteIdx = idx >> 3;
      if (byteIdx < target.length) {
        const bitOffset = 7 - (idx & 7);
        target[byteIdx] |= 1 << bitOffset;
      }
    }
  }
}

/**
 * Sets multiple piece ranges in a Uint8Array bitfield (allocating new buffer if necessary).
 */
export function setPieceRanges(
  current: Uint8Array | undefined,
  ranges: Array<[number, number]> | number[][],
): Uint8Array {
  if (!ranges || ranges.length === 0) {
    return current ? new Uint8Array(current) : new Uint8Array(0);
  }

  let maxIdx = -1;
  for (let i = 0; i < ranges.length; i++) {
    const r = ranges[i];
    if (Array.isArray(r) && r.length >= 2 && r[1] > maxIdx) {
      maxIdx = r[1];
    }
  }

  const requiredBytes = maxIdx >= 0 ? (maxIdx >> 3) + 1 : 0;
  const targetLen = Math.max(requiredBytes, current ? current.length : 0);
  const target = new Uint8Array(targetLen);
  if (current) {
    target.set(current);
  }

  setPieceRangesInPlace(target, ranges);
  return target;
}

/**
 * Sets a specific piece bit in a Uint8Array bitfield (BitTorrent MSB-first convention).
 */
export function setPieceBit(
  current: Uint8Array | undefined,
  pieceIndex: number,
): Uint8Array {
  if (typeof pieceIndex !== "number" || pieceIndex < 0) {
    return current ? new Uint8Array(current) : new Uint8Array(0);
  }

  const byteIdx = pieceIndex >> 3;
  const bitOffset = 7 - (pieceIndex & 7);
  const requiredBytes = byteIdx + 1;

  let target: Uint8Array;
  if (!current || current.length < requiredBytes) {
    target = new Uint8Array(
      Math.max(requiredBytes, current ? current.length : 0),
    );
    if (current) {
      target.set(current);
    }
  } else {
    target = new Uint8Array(current);
  }

  target[byteIdx] |= 1 << bitOffset;
  return target;
}

/**
 * Sets multiple piece bits in a Uint8Array bitfield in a single pass.
 */
export function setPieceBits(
  current: Uint8Array | undefined,
  pieceIndices: number[],
): Uint8Array {
  if (!pieceIndices || pieceIndices.length === 0) {
    return current ? new Uint8Array(current) : new Uint8Array(0);
  }

  let maxIdx = -1;
  for (let i = 0; i < pieceIndices.length; i++) {
    const idx = pieceIndices[i];
    if (typeof idx === "number" && idx > maxIdx) {
      maxIdx = idx;
    }
  }

  const requiredBytes = maxIdx >= 0 ? (maxIdx >> 3) + 1 : 0;
  const targetLen = Math.max(requiredBytes, current ? current.length : 0);
  const target = new Uint8Array(targetLen);
  if (current) {
    target.set(current);
  }

  setPieceBitsInPlace(target, pieceIndices);
  return target;
}

/**
 * Merges two bitfields via bitwise OR.
 */
export function mergeBitfields(
  base: Uint8Array | undefined,
  incoming: Uint8Array,
): Uint8Array {
  const len = Math.max(base ? base.length : 0, incoming.length);
  const target = new Uint8Array(len);
  if (base) {
    target.set(base);
  }
  for (let i = 0; i < incoming.length; i++) {
    target[i] |= incoming[i];
  }
  return target;
}

/**
 * Checks if a specific piece index is verified in a Uint8Array bitfield.
 */
export function isPieceVerifiedInBitfield(
  bitfield: Uint8Array | null | undefined,
  pieceIndex: number,
): boolean {
  if (!bitfield || typeof pieceIndex !== "number" || pieceIndex < 0) {
    return false;
  }
  const byteIdx = pieceIndex >> 3;
  if (byteIdx >= bitfield.length) {
    return false;
  }
  const bitOffset = 7 - (pieceIndex & 7);
  return (bitfield[byteIdx] & (1 << bitOffset)) !== 0;
}

/**
 * Counts the total number of verified pieces in a Uint8Array bitfield using O(1) byte popcount lookups.
 */
export function countVerifiedPieces(
  bitfield: Uint8Array | null | undefined,
  totalPieces?: number,
): number {
  if (!bitfield || bitfield.length === 0) return 0;
  let count = 0;
  for (let i = 0; i < bitfield.length; i++) {
    count += POPCOUNT_8[bitfield[i]];
  }
  return typeof totalPieces === "number" ? Math.min(count, totalPieces) : count;
}

/**
 * Aggregates piece verification across visual blocks (e.g. 480 blocks) using fast bitwise operations.
 */
export function binBitfieldBlocks(
  bitfield: Uint8Array | null | undefined,
  totalPieces: number,
  numBlocks = 480,
  isComplete = false,
): VisualBlock[] {
  const actualPieces = Math.max(
    1,
    Number.isFinite(totalPieces) && totalPieces > 0 ? totalPieces : 1,
  );
  const actualBlocks = Math.max(
    1,
    Math.min(
      actualPieces,
      Number.isFinite(numBlocks) && numBlocks > 0 ? numBlocks : 1,
    ),
  );
  const blocks: VisualBlock[] = [];
  const piecesPerBlock = actualPieces / actualBlocks;

  for (let i = 0; i < actualBlocks; i++) {
    const startIdx = Math.floor(i * piecesPerBlock);
    const endIdx = Math.min(
      actualPieces - 1,
      Math.floor((i + 1) * piecesPerBlock) - 1,
    );
    const totalInBlock = Math.max(1, endIdx - startIdx + 1);

    let completedCount = 0;
    if (isComplete) {
      completedCount = totalInBlock;
    } else if (bitfield && bitfield.length > 0) {
      const startByte = startIdx >> 3;
      const endByte = endIdx >> 3;
      const startBit = startIdx & 7;
      const endBit = endIdx & 7;

      if (startByte === endByte) {
        if (startByte < bitfield.length) {
          const mask = (0xff >> startBit) & ((0xff << (7 - endBit)) & 0xff);
          completedCount = POPCOUNT_8[bitfield[startByte] & mask];
        }
      } else {
        if (startByte < bitfield.length) {
          const mask = 0xff >> startBit;
          completedCount += POPCOUNT_8[bitfield[startByte] & mask];
        }
        const middleLimit = Math.min(endByte, bitfield.length);
        for (let b = startByte + 1; b < middleLimit; b++) {
          completedCount += POPCOUNT_8[bitfield[b]];
        }
        if (endByte < bitfield.length) {
          const mask = (0xff << (7 - endBit)) & 0xff;
          completedCount += POPCOUNT_8[bitfield[endByte] & mask];
        }
      }
    }

    let status: "complete" | "missing" | "active" = "missing";
    if (isComplete || completedCount === totalInBlock) {
      status = "complete";
    } else if (completedCount > 0) {
      status = "active";
    }

    blocks.push({
      startIndex: startIdx,
      endIndex: Math.max(startIdx, endIdx),
      status,
      completedCount: Math.min(completedCount, totalInBlock),
      totalInBlock,
    });
  }

  return blocks;
}
