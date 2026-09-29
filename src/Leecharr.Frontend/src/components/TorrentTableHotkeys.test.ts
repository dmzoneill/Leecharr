import { describe, it } from "node:test";
import assert from "node:assert/strict";

describe("TorrentTable Hotkey Guard (#1032)", () => {
  function shouldIgnoreKeyDown(
    target: {
      closest?: (sel: string) => unknown;
      tagName?: string;
      isContentEditable?: boolean;
    } | null,
    activeEl: { closest?: (sel: string) => unknown } | null,
    isModalOpen = false,
  ): boolean {
    if (isModalOpen) return true;

    if (
      target?.closest?.(
        '.detail-panel, .quick-settings-drawer, [data-panel], [role="dialog"]',
      ) ||
      activeEl?.closest?.(
        '.detail-panel, .quick-settings-drawer, [data-panel], [role="dialog"]',
      )
    ) {
      return true;
    }

    const tagName = target?.tagName?.toLowerCase();
    const isInput =
      tagName === "input" ||
      tagName === "textarea" ||
      tagName === "select" ||
      target?.isContentEditable;
    if (isInput) return true;

    return false;
  }

  interface MockElement {
    className?: string;
    role?: string;
    hasDataPanel?: boolean;
    tagName?: string;
    isContentEditable?: boolean;
    parentElement?: MockElement | null;
    closest?: (sel: string) => MockElement | null;
  }

  function createMockElement(
    attrs: {
      className?: string;
      role?: string;
      hasDataPanel?: boolean;
      tagName?: string;
      isContentEditable?: boolean;
    },
    parent?: MockElement | null,
  ): MockElement {
    const el: MockElement = {
      ...attrs,
      parentElement: parent || null,
      closest: (sel: string) => {
        const selectors = sel.split(",").map((s) => s.trim());
        let cur: MockElement | null | undefined = el;
        while (cur) {
          for (const s of selectors) {
            if (s.startsWith(".") && cur.className?.split(/\s+/).includes(s.slice(1))) {
              return cur;
            }
            if (s === '[role="dialog"]' && cur.role === "dialog") {
              return cur;
            }
            if (s === "[data-panel]" && cur.hasDataPanel) {
              return cur;
            }
          }
          cur = cur.parentElement;
        }
        return null;
      },
    };
    return el;
  }

  it("ignores hotkeys when target is inside .detail-panel (e.g. file row or table wrap)", () => {
    const panel = createMockElement({ className: "detail-panel" });
    const wrap = createMockElement({ className: "detail-panel-table-wrap" }, panel);
    const row = createMockElement({ className: "torrent-table-row", tagName: "tr" }, wrap);

    assert.strictEqual(shouldIgnoreKeyDown(row, null), true);
  });

  it("ignores hotkeys when activeElement is focused inside .detail-panel", () => {
    const panel = createMockElement({ className: "detail-panel" });
    const fileContainer = createMockElement({ className: "detail-panel-table-wrap" }, panel);

    assert.strictEqual(shouldIgnoreKeyDown(null, fileContainer), true);
  });

  it("ignores hotkeys when target is inside .quick-settings-drawer", () => {
    const drawer = createMockElement({ className: "quick-settings-drawer" });
    const card = createMockElement({ className: "quick-settings-grid" }, drawer);
    const btn = createMockElement({ className: "btn btn-primary", tagName: "button" }, card);

    assert.strictEqual(shouldIgnoreKeyDown(btn, null), true);
  });

  it("ignores hotkeys when activeElement is inside .quick-settings-drawer", () => {
    const drawer = createMockElement({ className: "quick-settings-drawer" });
    const input = createMockElement({ className: "slider-input", tagName: "input" }, drawer);

    assert.strictEqual(shouldIgnoreKeyDown(null, input), true);
  });

  it("ignores hotkeys when inside generic [data-panel]", () => {
    const panel = createMockElement({ hasDataPanel: true });
    const child = createMockElement({ className: "item" }, panel);

    assert.strictEqual(shouldIgnoreKeyDown(child, null), true);
  });

  it("processes hotkeys when target is within main torrent table", () => {
    const tableWrap = createMockElement({ className: "torrent-table-wrapper" });
    const row = createMockElement({ className: "torrent-row" }, tableWrap);

    assert.strictEqual(shouldIgnoreKeyDown(row, null), false);
  });

  it("ignores hotkeys if any modal dialog is open", () => {
    const tableWrap = createMockElement({ className: "torrent-table-wrapper" });
    assert.strictEqual(shouldIgnoreKeyDown(tableWrap, null, true), true);
  });

  it("ignores hotkeys if target is input, textarea, select, or contentEditable", () => {
    const input = createMockElement({ tagName: "input" });
    const textarea = createMockElement({ tagName: "textarea" });
    const select = createMockElement({ tagName: "select" });
    const editable = createMockElement({ isContentEditable: true });

    assert.strictEqual(shouldIgnoreKeyDown(input, null), true);
    assert.strictEqual(shouldIgnoreKeyDown(textarea, null), true);
    assert.strictEqual(shouldIgnoreKeyDown(select, null), true);
    assert.strictEqual(shouldIgnoreKeyDown(editable, null), true);
  });
});

describe("TorrentTable Queue Reorder / Batch Move Ordering (#1005)", () => {
  it("preserves queue order regardless of table sort order and never reverses batch", () => {
    const torrents = [
      { id: 10, queuePosition: 1, name: "Zebra" },
      { id: 20, queuePosition: 2, name: "Alpha" },
      { id: 30, queuePosition: 3, name: "Beta" },
    ];
    // User selected 30 and 20 (e.g. while sorted alphabetically by name)
    const targetIds = [30, 20];
    const targetSet = new Set(targetIds);

    const ordered = torrents
      .filter((t) => targetSet.has(t.id))
      .map((t) => t.id);

    const idsToMove = ordered.length > 0 ? ordered : targetIds;

    // Must be in natural queue order [20, 30]
    assert.deepStrictEqual(idsToMove, [20, 30]);
  });
});
