import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import {
  ALL_COLUMNS,
  loadVisibleColumns,
  saveVisibleColumns,
  loadTableSortPreferences,
  saveTableSortPreferences,
  loadColumnOrder,
  STORAGE_KEY,
  SORT_KEY_STORAGE,
  SORT_ASC_STORAGE,
  COL_ORDER_STORAGE,
} from "./columnPreferences";

class MockLocalStorage {
  private store = new Map<string, string>();

  getItem(key: string): string | null {
    return this.store.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  clear(): void {
    this.store.clear();
  }
}

describe("columnPreferences - Category column (#1007)", () => {
  let mockStorage: MockLocalStorage;
  const originalLocalStorage = globalThis.localStorage;

  beforeEach(() => {
    mockStorage = new MockLocalStorage();
    Object.defineProperty(globalThis, "localStorage", {
      value: mockStorage,
      writable: true,
      configurable: true,
    });
  });

  afterEach(() => {
    Object.defineProperty(globalThis, "localStorage", {
      value: originalLocalStorage,
      writable: true,
      configurable: true,
    });
  });

  it("includes Category column in ALL_COLUMNS master list", () => {
    const categoryCol = ALL_COLUMNS.find((col) => col.key === "category");
    assert.ok(categoryCol, "category column must exist in ALL_COLUMNS");
    assert.deepEqual(categoryCol, {
      key: "category",
      label: "Category",
      sortable: true,
      category: "basic",
    });
  });

  it("ensures ALL_COLUMNS has unique keys", () => {
    const keys = ALL_COLUMNS.map((col) => col.key);
    const uniqueKeys = new Set(keys);
    assert.equal(keys.length, uniqueKeys.size, "ALL_COLUMNS keys must be unique");
  });

  it("allows Category column to be loaded from localStorage visible columns", () => {
    mockStorage.setItem(STORAGE_KEY, JSON.stringify(["name", "category", "status"]));
    const loaded = loadVisibleColumns();
    assert.ok(loaded.has("category"), "loaded visible columns should contain category");
    assert.ok(loaded.has("name"));
    assert.ok(loaded.has("status"));
  });

  it("allows Category column to be saved to localStorage visible columns", () => {
    saveVisibleColumns(new Set(["name", "category"]));
    const raw = mockStorage.getItem(STORAGE_KEY);
    assert.ok(raw, "localStorage should contain saved columns");
    const parsed = JSON.parse(raw);
    assert.ok(parsed.includes("category"), "saved columns should include category");
  });

  it("recognizes Category as a valid sort key in loadTableSortPreferences", () => {
    mockStorage.setItem(SORT_KEY_STORAGE, "category");
    mockStorage.setItem(SORT_ASC_STORAGE, "false");
    const { sortKey, sortAsc } = loadTableSortPreferences();
    assert.equal(sortKey, "category");
    assert.equal(sortAsc, false);
  });

  it("preserves Category in loadColumnOrder", () => {
    mockStorage.setItem(COL_ORDER_STORAGE, JSON.stringify(["category", "name", "#"]));
    const order = loadColumnOrder();
    assert.equal(order[0], "category");
    assert.ok(order.includes("name"));
    assert.ok(order.includes("#"));
  });
});
