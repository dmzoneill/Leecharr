export interface FuzzySearchable {
  id?: string;
  title: string;
  subtitle?: string;
  category?: string;
}

export interface ParsedQuery {
  category?: string;
  query: string;
}

/**
 * Parses user input for optional category prefixes:
 * - `>`: Actions
 * - `act:` / `action:` / `actions:`: Actions
 * - `tor:` / `torrent:` / `torrents:`: Torrents
 * - `nav:` / `navigation:`: Navigation
 * - `set:` / `setting:` / `settings:`: Settings
 */
export function parseSearchQuery(rawQuery: string): ParsedQuery {
  const trimmed = rawQuery.trim();
  if (!trimmed) {
    return { query: "" };
  }

  if (trimmed.startsWith(">")) {
    return {
      category: "Actions",
      query: trimmed.slice(1).trim(),
    };
  }

  const prefixMatch = trimmed.match(
    /^(tor|torrent|torrents|nav|navigation|act|action|actions|set|setting|settings):\s*(.*)$/i,
  );
  if (prefixMatch) {
    const prefix = prefixMatch[1].toLowerCase();
    const rest = prefixMatch[2].trim();
    let category: string | undefined;

    if (prefix.startsWith("tor")) category = "Torrents";
    else if (prefix.startsWith("nav")) category = "Navigation";
    else if (prefix.startsWith("act")) category = "Actions";
    else if (prefix.startsWith("set")) category = "Settings";

    return { category, query: rest };
  }

  return { query: trimmed };
}

/**
 * Scores a single token against an item's title and subtitle.
 *
 * Scoring hierarchy:
 * 1. Exact title match: 100
 * 2. Title starts with query: 80
 * 3. Word boundary match (any word in title starts with query): 60
 * 4. Acronym / initialism match (e.g. "tb" -> "Tracker Boost", "gs" -> "General Settings"): 50
 * 5. Substring in title: 40
 * 6. Substring in subtitle: 20
 * 7. No match: 0
 */
function scoreSingleToken(
  title: string,
  subtitle: string | undefined,
  q: string,
): number {
  const titleLower = (title || "").toLowerCase().trim();
  const subLower = subtitle ? subtitle.toLowerCase().trim() : undefined;

  // 1. Exact title match: 100
  if (titleLower === q) {
    return 100;
  }

  // 2. Title starts with query: 80
  if (titleLower.startsWith(q)) {
    return 80;
  }

  // Split title into words by non-alphanumeric characters
  const words = titleLower.split(/[^a-zA-Z0-9]+/).filter(Boolean);

  // 3. Word boundary match in title: 60
  if (words.some((w) => w.startsWith(q))) {
    return 60;
  }

  // 4. Acronym / initialism match: 50
  if (q.length >= 2 && words.length >= 2) {
    const acronym = words.map((w) => w[0]).join("");
    if (acronym === q || acronym.startsWith(q)) {
      return 50;
    }
  }

  // 5. Substring in title: 40
  if (titleLower.includes(q)) {
    return 40;
  }

  // 6. Substring in subtitle: 20
  if (subLower && subLower.includes(q)) {
    return 20;
  }

  return 0;
}

/**
 * Computes a relevance score for an item against a query.
 * Multi-word queries require ALL tokens to match the item.
 */
export function scoreItem(
  item: { title: string; subtitle?: string },
  query: string,
): number {
  const trimmedQuery = query.trim().toLowerCase();
  if (!trimmedQuery) return 0;

  const tokens = trimmedQuery.split(/\s+/).filter(Boolean);
  if (tokens.length === 0) return 0;

  // Single token query
  if (tokens.length === 1) {
    return scoreSingleToken(item.title, item.subtitle, tokens[0]);
  }

  // Multi-word token matching: ALL tokens in the query must match the item
  const tokenScores: number[] = [];
  for (const token of tokens) {
    const s = scoreSingleToken(item.title, item.subtitle, token);
    if (s === 0) {
      return 0; // Token missing -> filtered out
    }
    tokenScores.push(s);
  }

  // Check if the full contiguous query matches directly
  const fullScore = scoreSingleToken(item.title, item.subtitle, trimmedQuery);
  if (fullScore === 100) return 100;
  if (fullScore === 80) return 90;
  if (fullScore === 60) return 75;
  if (fullScore === 40) return 65;

  // Multi-word non-contiguous match: average token score
  const avg =
    tokenScores.reduce((sum, score) => sum + score, 0) / tokenScores.length;
  return Math.round(avg);
}

/**
 * Filters and ranks items according to weighted fuzzy search rules and category prefix filtering.
 */
export function filterAndRankItems<T extends FuzzySearchable>(
  items: T[],
  rawQuery: string,
  limit = 30,
): T[] {
  const { category, query } = parseSearchQuery(rawQuery);

  let pool = items;
  if (category) {
    pool = pool.filter((item) => item.category === category);
  }

  if (!query) {
    return pool.slice(0, limit);
  }

  const scored: { item: T; score: number }[] = [];
  for (const item of pool) {
    const score = scoreItem(item, query);
    if (score > 0) {
      scored.push({ item, score });
    }
  }

  // Sort descending by score. JavaScript array sort is stable, preserving insertion order for ties.
  scored.sort((a, b) => b.score - a.score);

  return scored.slice(0, limit).map((s) => s.item);
}
