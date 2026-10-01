/**
 * Cryptographically secure pseudorandom utilities replacing Math.random() (SonarCloud typescript:S2245).
 */

/**
 * Returns a cryptographically secure pseudorandom number in the range [0, 1).
 */
export function secureRandom(): number {
  const buf = new Uint32Array(2);
  crypto.getRandomValues(buf);
  return ((buf[0] >>> 5) * 67108864 + (buf[1] >>> 6)) / 9007199254740992;
}

/**
 * Generates a cryptographically secure random hexadecimal identifier with an optional prefix.
 */
export function generateRandomId(prefix = ""): string {
  const bytes = new Uint8Array(8);
  crypto.getRandomValues(bytes);
  const randomStr = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return prefix ? `${prefix}${randomStr}` : randomStr;
}
