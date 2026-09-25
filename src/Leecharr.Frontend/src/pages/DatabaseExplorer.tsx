import React, { useState, useEffect, useRef, useMemo, useCallback } from "react";
import * as d3 from "d3";
import { apiClient } from "../api/client";
import { useTranslation } from "../i18n";
import type {
  DatabaseTable,
  DatabaseTableSchema,
  DatabaseSchemaResponse,
  DatabaseQueryResult,
  DatabaseStorageResponse,
  DatabaseStorageItem,
  QueryPlanNode,
} from "../api/types";

function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`;
}

interface PlanTreeNode extends QueryPlanNode {
  children: PlanTreeNode[];
}

function parseQueryPlanNodes(nodes: QueryPlanNode[]): PlanTreeNode[] {
  const nodeMap = new Map<number, PlanTreeNode>();
  nodes.forEach((n) => {
    nodeMap.set(n.id, { ...n, children: [] });
  });

  const roots: PlanTreeNode[] = [];
  nodes.forEach((n) => {
    const item = nodeMap.get(n.id)!;
    if (n.parentId !== 0 && nodeMap.has(n.parentId)) {
      nodeMap.get(n.parentId)!.children.push(item);
    } else {
      roots.push(item);
    }
  });

  return roots;
}

function getPlanBadge(detail: string): { label: string; bg: string; color: string; icon: string } {
  const d = detail.toUpperCase();
  if (d.includes("SCAN")) {
    return { label: "FULL SCAN", bg: "rgba(245, 158, 11, 0.15)", color: "#f59e0b", icon: "⚠️" };
  }
  if (d.includes("USING INDEX") || d.includes("USING COVERING INDEX")) {
    return { label: "INDEX SEARCH", bg: "rgba(16, 185, 129, 0.15)", color: "#10b981", icon: "⚡" };
  }
  if (d.includes("INTEGER PRIMARY KEY") || d.includes("ROWID")) {
    return { label: "PRIMARY KEY", bg: "rgba(59, 130, 246, 0.15)", color: "#3b82f6", icon: "🎯" };
  }
  if (d.includes("TEMP B-TREE") || d.includes("TEMP TABLE")) {
    return { label: "TEMP B-TREE", bg: "rgba(139, 92, 246, 0.15)", color: "#8b5cf6", icon: "🔄" };
  }
  if (d.includes("SUBQUERY") || d.includes("COMPOUND")) {
    return { label: "SUBQUERY", bg: "rgba(6, 182, 212, 0.15)", color: "#06b6d4", icon: "📦" };
  }
  return { label: "QUERY STEP", bg: "rgba(255, 255, 255, 0.08)", color: "var(--text-secondary)", icon: "▶" };
}

interface NodePosition {
  x: number;
  y: number;
}

export default function DatabaseExplorer() {
  const { t } = useTranslation();

  // Navigation tabs
  const [activeTab, setActiveTab] = useState<"diagram" | "treemap" | "console" | "inspector">("diagram");

  // Storage Treemap state
  const [storageData, setStorageData] = useState<DatabaseStorageResponse | null>(null);
  const [isLoadingStorage, setIsLoadingStorage] = useState<boolean>(false);
  const [storageFilter, setStorageFilter] = useState<"all" | "table" | "index">("all");
  const [storageMetric, setStorageMetric] = useState<"bytes" | "rows" | "pages">("bytes");
  const [treemapSearch, setTreemapSearch] = useState<string>("");
  const [selectedStorageItem, setSelectedStorageItem] = useState<DatabaseStorageItem | null>(null);
  const [hoveredStorageItem, setHoveredStorageItem] = useState<DatabaseStorageItem | null>(null);
  const treemapContainerRef = useRef<HTMLDivElement | null>(null);
  const [treemapDimensions, setTreemapDimensions] = useState<{ width: number; height: number }>({ width: 800, height: 500 });

  // SQL Console Plan state
  const [consoleView, setConsoleView] = useState<"data" | "plan">("data");
  const [planViewMode, setPlanViewMode] = useState<"tree" | "table">("tree");

  // Schema state
  const [tables, setTables] = useState<DatabaseTable[]>([]);
  const [schema, setSchema] = useState<DatabaseSchemaResponse | null>(null);
  const [isLoadingSchema, setIsLoadingSchema] = useState(true);
  const [schemaError, setSchemaError] = useState<string | null>(null);

  // Inspector state
  const [selectedTable, setSelectedTable] = useState<string>("");

  // SQL Console state
  const [query, setQuery] = useState<string>("SELECT * FROM Torrents LIMIT 50;");
  const [isSafeMode, setIsSafeMode] = useState<boolean>(true);
  const [isExecuting, setIsExecuting] = useState<boolean>(false);
  const [queryResult, setQueryResult] = useState<DatabaseQueryResult | null>(null);
  const [showConfirmModal, setShowConfirmModal] = useState<boolean>(false);

  // Visualizer state
  const svgRef = useRef<SVGSVGElement | null>(null);
  const zoomGRef = useRef<SVGGElement | null>(null);
  const zoomBehaviorRef = useRef<d3.ZoomBehavior<SVGSVGElement, unknown> | null>(null);
  const [positions, setPositions] = useState<Record<string, NodePosition>>({});
  const [hoveredTable, setHoveredTable] = useState<string | null>(null);
  const [searchFilter, setSearchFilter] = useState<string>("");
  const [showMermaidModal, setShowMermaidModal] = useState<boolean>(false);
  const [copyFeedback, setCopyFeedback] = useState<string | null>(null);

  // Fetch storage stats
  const fetchStorage = useCallback(async () => {
    setIsLoadingStorage(true);
    try {
      const data = await apiClient.get<DatabaseStorageResponse>("/system/database/storage");
      setStorageData(data || null);
    } catch (err: any) {
      console.error("Failed to load storage data", err);
    } finally {
      setIsLoadingStorage(false);
    }
  }, []);

  // Fetch tables and schema
  const fetchSchema = useCallback(async () => {
    setIsLoadingSchema(true);
    setSchemaError(null);
    try {
      const [tablesData, schemaData] = await Promise.all([
        apiClient.get<DatabaseTable[]>("/system/database/tables"),
        apiClient.get<DatabaseSchemaResponse>("/system/database/schema"),
      ]);
      setTables(tablesData || []);
      setSchema(schemaData || null);

      if (tablesData && tablesData.length > 0 && !selectedTable) {
        setSelectedTable(tablesData[0].name);
      }
      fetchStorage();
    } catch (err: any) {
      setSchemaError(err?.message || "Failed to load database schema.");
    } finally {
      setIsLoadingSchema(false);
    }
  }, [selectedTable, fetchStorage]);

  useEffect(() => {
    fetchSchema();
  }, [fetchSchema]);

  // Treemap ResizeObserver
  useEffect(() => {
    if (!treemapContainerRef.current) return;
    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const { width, height } = entry.contentRect;
        if (width > 50 && height > 50) {
          setTreemapDimensions({ width: Math.floor(width), height: Math.floor(height) });
        }
      }
    });
    ro.observe(treemapContainerRef.current);
    return () => ro.disconnect();
  }, [activeTab]);

  // Compute Treemap layout
  const treemapLeaves = useMemo(() => {
    if (!storageData?.items || storageData.items.length === 0) return [];
    const filtered = storageData.items.filter((item) => {
      if (storageFilter === "table" && item.type !== "table") return false;
      if (storageFilter === "index" && item.type !== "index") return false;
      if (treemapSearch.trim()) {
        const q = treemapSearch.toLowerCase().trim();
        return item.name.toLowerCase().includes(q) || item.tableName.toLowerCase().includes(q);
      }
      return true;
    });

    if (filtered.length === 0) return [];

    const rootData = {
      name: "root",
      children: filtered.map((item) => ({
        ...item,
        value: Math.max(
          storageMetric === "bytes" ? item.bytes : (storageMetric === "rows" ? item.rowCount : item.pageCount),
          1
        ),
      })),
    };

    const root = d3
      .hierarchy<any>(rootData)
      .sum((d) => d.value || 0)
      .sort((a, b) => (b.value || 0) - (a.value || 0));

    d3
      .treemap<any>()
      .tile(d3.treemapSquarify)
      .size([Math.max(treemapDimensions.width, 300), Math.max(treemapDimensions.height, 300)])
      .padding(3)(root);

    return root.leaves();
  }, [storageData, storageFilter, storageMetric, treemapSearch, treemapDimensions]);

  // Initial grid layout for tables on visual canvas
  useEffect(() => {
    if (!schema?.tables || schema.tables.length === 0) return;

    setPositions((prev) => {
      const next = { ...prev };
      const colWidth = 320;
      const rowHeight = 360;
      const cols = Math.max(1, Math.ceil(Math.sqrt(schema.tables.length * 1.5)));

      schema.tables.forEach((t, index) => {
        if (!next[t.name]) {
          const col = index % cols;
          const row = Math.floor(index / cols);
          next[t.name] = {
            x: 50 + col * colWidth,
            y: 50 + row * rowHeight,
          };
        }
      });
      return next;
    });
  }, [schema]);

  // Setup D3 Zoom on SVG
  useEffect(() => {
    if (!svgRef.current || !zoomGRef.current) return;

    const svg = d3.select(svgRef.current);
    const zoomG = d3.select(zoomGRef.current);

    const zoom = d3
      .zoom<SVGSVGElement, unknown>()
      .scaleExtent([0.15, 3])
      .on("zoom", (event) => {
        zoomG.attr("transform", event.transform);
      });

    zoomBehaviorRef.current = zoom;
    svg.call(zoom);
  }, []);

  const handleZoom = (factor: number) => {
    if (!svgRef.current || !zoomBehaviorRef.current) return;
    d3.select(svgRef.current)
      .transition()
      .duration(250)
      .call(zoomBehaviorRef.current.scaleBy, factor);
  };

  const handleResetZoom = () => {
    if (!svgRef.current || !zoomBehaviorRef.current) return;
    d3.select(svgRef.current)
      .transition()
      .duration(300)
      .call(zoomBehaviorRef.current.transform, d3.zoomIdentity.translate(40, 40).scale(0.85));
  };

  // Dragging cards
  const startDrag = (e: React.MouseEvent, tableName: string) => {
    e.stopPropagation();
    const startX = e.clientX;
    const startY = e.clientY;
    const initialPos = positions[tableName] || { x: 50, y: 50 };

    const onMouseMove = (moveEvent: MouseEvent) => {
      const dx = moveEvent.clientX - startX;
      const dy = moveEvent.clientY - startY;
      setPositions((prev) => ({
        ...prev,
        [tableName]: {
          x: initialPos.x + dx,
          y: initialPos.y + dy,
        },
      }));
    };

    const onMouseUp = () => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
    };

    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
  };

  // Run SQL Query
  const runQuery = async (queryText?: string, bypassSafeCheck = false) => {
    const textToRun = (queryText ?? query).trim();
    if (!textToRun) return;

    const isWrite = /^\s*(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REPLACE|VACUUM|ATTACH|DETACH|REINDEX)\b/i.test(
      textToRun
    );

    if (isWrite && isSafeMode) {
      setQueryResult({
        success: false,
        errorMessage: "Safe Mode (Read-Only) is enabled. Disable Safe Mode to execute write queries.",
        executionTimeMs: 0,
        isQuery: false,
      });
      return;
    }

    if (isWrite && !bypassSafeCheck) {
      setShowConfirmModal(true);
      return;
    }

    setIsExecuting(true);
    setShowConfirmModal(false);

    try {
      const res = await apiClient.post<DatabaseQueryResult>("/system/database/query", {
        query: textToRun,
        readOnly: isSafeMode,
      });
      setQueryResult(res);
      // Refresh schema if DDL was run
      if (isWrite) {
        fetchSchema();
      }
    } catch (err: any) {
      setQueryResult({
        success: false,
        errorMessage: err?.message || "Execution failed.",
        executionTimeMs: 0,
        isQuery: false,
      });
    } finally {
      setIsExecuting(false);
    }
  };

  // Copy Mermaid ERD
  const copyMermaid = () => {
    if (!schema?.mermaidErd) return;
    navigator.clipboard.writeText(schema.mermaidErd);
    setCopyFeedback("Copied Mermaid ERD to clipboard!");
    setTimeout(() => setCopyFeedback(null), 3000);
  };

  // Quick query helper
  const handleSelectTableForQuery = (name: string) => {
    const newQuery = `SELECT * FROM "${name}" LIMIT 50;`;
    setQuery(newQuery);
    setSelectedTable(name);
    setActiveTab("console");
    runQuery(newQuery, false);
  };

  // Filtered tables for canvas/inspector
  const filteredTables = useMemo(() => {
    if (!schema?.tables) return [];
    if (!searchFilter.trim()) return schema.tables;
    const filter = searchFilter.toLowerCase();
    return schema.tables.filter(
      (t) =>
        t.name.toLowerCase().includes(filter) ||
        t.columns.some((c) => c.name.toLowerCase().includes(filter))
    );
  }, [schema?.tables, searchFilter]);

  // Current inspector table
  const currentInspectorTable = useMemo(() => {
    return schema?.tables.find((t) => t.name === selectedTable) || schema?.tables[0] || null;
  }, [schema?.tables, selectedTable]);

  // Relationships list for SVG links
  const relationships = useMemo(() => {
    if (!schema?.tables) return [];
    const list: Array<{
      source: string;
      target: string;
      fromCol: string;
      toCol: string;
    }> = [];

    schema.tables.forEach((t) => {
      (t.foreignKeys || []).forEach((fk) => {
        list.push({
          source: t.name,
          target: fk.toTable,
          fromCol: fk.fromColumn,
          toCol: fk.toColumn,
        });
      });
    });

    return list;
  }, [schema?.tables]);

  // CSV Export helper
  const exportCsv = () => {
    if (!queryResult || !queryResult.columns || !queryResult.rows) return;
    const headers = queryResult.columns.map((c) => `"${c.replace(/"/g, '""')}"`).join(",");
    const rows = queryResult.rows
      .map((r) =>
        r
          .map((v) =>
            v === null || v === undefined
              ? '""'
              : `"${String(v).replace(/"/g, '""')}"`
          )
          .join(",")
      )
      .join("\n");
    const blob = new Blob([`${headers}\n${rows}`], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.setAttribute("download", `query_export_${Date.now()}.csv`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  return (
    <div
      className="content-area"
      style={{
        padding: "1.5rem",
        width: "100%",
        height: "100%",
        maxHeight: "100%",
        boxSizing: "border-box",
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
        minHeight: 0,
      }}
    >
      {/* Page Header */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexWrap: "wrap",
          gap: "1rem",
          flexShrink: 0,
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.6rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>🗄️</span> {t("system.databaseExplorer", undefined, "Database Explorer")}
          </h1>
          <p style={{ color: "var(--text-muted)", margin: "0.25rem 0 0", fontSize: "0.9rem" }}>
            {t(
              "system.databaseExplorerSubtitle",
              undefined,
              "Inspect SQLite schema, visualize foreign key relations, and execute queries."
            )}
          </p>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          {/* Safe Mode Toggle */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
              padding: "0.4rem 0.8rem",
              backgroundColor: isSafeMode ? "rgba(40, 167, 69, 0.15)" : "rgba(220, 53, 69, 0.15)",
              border: `1px solid ${isSafeMode ? "rgba(40, 167, 69, 0.4)" : "rgba(220, 53, 69, 0.4)"}`,
              borderRadius: "6px",
              cursor: "pointer",
            }}
            onClick={() => setIsSafeMode(!isSafeMode)}
            title="When active, safe mode prevents accidental database modifications"
          >
            <span>{isSafeMode ? "🛡️" : "⚠️"}</span>
            <span
              style={{
                fontSize: "0.85rem",
                fontWeight: 600,
                color: isSafeMode ? "var(--success, #28a745)" : "var(--danger, #dc3545)",
              }}
            >
              {isSafeMode ? "Safe Mode (Read-Only)" : "Edit Mode (Read/Write)"}
            </span>
          </div>

          <button
            className="btn btn-outline btn-small"
            onClick={fetchSchema}
            disabled={isLoadingSchema}
            title="Refresh database schema"
          >
            {isLoadingSchema ? "Refreshing..." : "↻ Refresh"}
          </button>
        </div>
      </div>

      {schemaError && (
        <div
          style={{
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            backgroundColor: "rgba(220, 53, 69, 0.15)",
            color: "var(--danger, #dc3545)",
            border: "1px solid rgba(220, 53, 69, 0.4)",
            marginBottom: "1rem",
          }}
        >
          {schemaError}
        </div>
      )}

      {/* Tabs */}
      <div
        style={{
          display: "flex",
          borderBottom: "1px solid var(--border-light)",
          marginBottom: "1rem",
          gap: "0.5rem",
          flexShrink: 0,
        }}
      >
        <button
          className={`btn ${activeTab === "diagram" ? "btn-primary" : "btn-outline"}`}
          style={{ borderRadius: "6px 6px 0 0", borderBottom: "none" }}
          onClick={() => setActiveTab("diagram")}
        >
          <span>📊</span> Relational ER Diagram
        </button>
        <button
          className={`btn ${activeTab === "treemap" ? "btn-primary" : "btn-outline"}`}
          style={{ borderRadius: "6px 6px 0 0", borderBottom: "none" }}
          onClick={() => {
            setActiveTab("treemap");
            if (!storageData) fetchStorage();
          }}
        >
          <span>🗺️</span> Storage Treemap
        </button>
        <button
          className={`btn ${activeTab === "console" ? "btn-primary" : "btn-outline"}`}
          style={{ borderRadius: "6px 6px 0 0", borderBottom: "none" }}
          onClick={() => setActiveTab("console")}
        >
          <span>💻</span> SQL Console
        </button>
        <button
          className={`btn ${activeTab === "inspector" ? "btn-primary" : "btn-outline"}`}
          style={{ borderRadius: "6px 6px 0 0", borderBottom: "none" }}
          onClick={() => setActiveTab("inspector")}
        >
          <span>🔍</span> Table Inspector
        </button>
      </div>

      {/* TAB 1: RELATIONAL ER DIAGRAM (INTERACTIVE VISUALIZER) */}
      {activeTab === "diagram" && (
        <div
          className="card"
          style={{
            padding: 0,
            overflow: "hidden",
            flex: 1,
            minHeight: 0,
            display: "flex",
            flexDirection: "column",
            position: "relative",
          }}
        >
          {/* Canvas Toolbar */}
          <div
            style={{
              padding: "0.6rem 1rem",
              borderBottom: "1px solid var(--border-light)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              backgroundColor: "var(--bg-secondary, rgba(255,255,255,0.02))",
              zIndex: 10,
              gap: "0.75rem",
              flexWrap: "wrap",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
              <input
                type="text"
                className="form-control"
                style={{ padding: "0.3rem 0.6rem", fontSize: "0.85rem", width: "220px" }}
                placeholder="Filter tables or columns..."
                value={searchFilter}
                onChange={(e) => setSearchFilter(e.target.value)}
              />
              <span style={{ fontSize: "0.82rem", color: "var(--text-muted)" }}>
                {filteredTables.length} tables • {relationships.length} relations
              </span>
            </div>

            <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
              <button
                className="btn btn-outline btn-small"
                onClick={() => handleZoom(1.25)}
                title="Zoom In"
              >
                +
              </button>
              <button
                className="btn btn-outline btn-small"
                onClick={() => handleZoom(0.8)}
                title="Zoom Out"
              >
                -
              </button>
              <button
                className="btn btn-outline btn-small"
                onClick={handleResetZoom}
                title="Reset View"
              >
                Fit / Reset
              </button>
              <button
                className="btn btn-outline btn-small"
                onClick={copyMermaid}
                title="Copy Mermaid.js ERD representation"
              >
                {copyFeedback || "📋 Copy Mermaid ERD"}
              </button>
              <button
                className="btn btn-outline btn-small"
                onClick={() => setShowMermaidModal(true)}
                title="View Mermaid Code"
              >
                View Mermaid Source
              </button>
            </div>
          </div>

          {/* Interactive D3 Canvas */}
          <div style={{ flex: 1, position: "relative", overflow: "hidden", background: "var(--bg-primary)" }}>
            <svg
              ref={svgRef}
              style={{ width: "100%", height: "100%", cursor: "grab" }}
              xmlns="http://www.w3.org/2000/svg"
            >
              <defs>
                <marker
                  id="erd-arrow"
                  viewBox="0 0 10 10"
                  refX="10"
                  refY="5"
                  markerWidth="6"
                  markerHeight="6"
                  orient="auto-start-reverse"
                >
                  <path d="M 0 1 L 10 5 L 0 9 z" fill="var(--accent, #ffd166)" />
                </marker>
              </defs>

              <g ref={zoomGRef} className="erd-canvas">
                {/* Relationship Lines */}
                {relationships.map((rel, idx) => {
                  const srcPos = positions[rel.source];
                  const tgtPos = positions[rel.target];
                  if (!srcPos || !tgtPos) return null;

                  const isHighlighted =
                    hoveredTable === rel.source || hoveredTable === rel.target;

                  const cardWidth = 260;
                  const x1 = srcPos.x + cardWidth;
                  const y1 = srcPos.y + 40;
                  const x2 = tgtPos.x;
                  const y2 = tgtPos.y + 40;

                  const dx = Math.abs(x2 - x1) * 0.5;
                  const pathData = `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`;

                  return (
                    <g key={`rel-${idx}`}>
                      <path
                        d={pathData}
                        fill="none"
                        stroke={
                          isHighlighted
                            ? "var(--accent, #ffd166)"
                            : "rgba(255, 209, 102, 0.4)"
                        }
                        strokeWidth={isHighlighted ? 3 : 1.8}
                        strokeDasharray={isHighlighted ? "none" : "4 2"}
                        markerEnd="url(#erd-arrow)"
                        style={{ transition: "stroke 0.2s, stroke-width 0.2s" }}
                      />
                      <text
                        x={(x1 + x2) / 2}
                        y={(y1 + y2) / 2 - 6}
                        fill={isHighlighted ? "var(--accent, #ffd166)" : "var(--text-muted)"}
                        fontSize="11"
                        textAnchor="middle"
                        style={{ pointerEvents: "none" }}
                      >
                        {rel.fromCol}
                      </text>
                    </g>
                  );
                })}

                {/* Table Nodes */}
                {filteredTables.map((table) => {
                  const pos = positions[table.name] || { x: 50, y: 50 };
                  const isHovered = hoveredTable === table.name;
                  const cardWidth = 260;
                  const visibleCols = table.columns.slice(0, 10);

                  return (
                    <g
                      key={table.name}
                      transform={`translate(${pos.x}, ${pos.y})`}
                      onMouseEnter={() => setHoveredTable(table.name)}
                      onMouseLeave={() => setHoveredTable(null)}
                      style={{ cursor: "move" }}
                      onMouseDown={(e) => startDrag(e, table.name)}
                    >
                      {/* Card Background */}
                      <rect
                        width={cardWidth}
                        height={40 + visibleCols.length * 24 + (table.columns.length > 10 ? 25 : 8)}
                        rx="8"
                        fill="var(--bg-card, #1a1e29)"
                        stroke={
                          isHovered
                            ? "var(--accent, #ffd166)"
                            : "var(--border-light, rgba(255,255,255,0.12))"
                        }
                        strokeWidth={isHovered ? 2.5 : 1}
                        filter="drop-shadow(0px 4px 10px rgba(0,0,0,0.4))"
                      />

                      {/* Card Header */}
                      <rect
                        width={cardWidth}
                        height="36"
                        rx="8"
                        fill="rgba(255, 209, 102, 0.12)"
                      />
                      <rect
                        y="28"
                        width={cardWidth}
                        height="8"
                        fill="rgba(255, 209, 102, 0.12)"
                      />
                      <text
                        x="12"
                        y="23"
                        fill="var(--text-primary, #ffffff)"
                        fontSize="13"
                        fontWeight="700"
                      >
                        {table.name}
                      </text>
                      <text
                        x={cardWidth - 12}
                        y="23"
                        fill="var(--text-muted)"
                        fontSize="11"
                        textAnchor="end"
                      >
                        {table.rowCount} rows
                      </text>

                      {/* Columns */}
                      {visibleCols.map((col, cIdx) => {
                        const y = 56 + cIdx * 24;
                        const isFk = (table.foreignKeys || []).some((f) => f.fromColumn === col.name);

                        return (
                          <g key={col.name}>
                            {/* PK Badge */}
                            {col.isPrimaryKey && (
                              <rect
                                x="12"
                                y={y - 12}
                                width="20"
                                height="14"
                                rx="3"
                                fill="rgba(255, 209, 102, 0.25)"
                              />
                            )}
                            {col.isPrimaryKey && (
                              <text
                                x="22"
                                y={y - 1}
                                fill="var(--accent, #ffd166)"
                                fontSize="9"
                                fontWeight="700"
                                textAnchor="middle"
                              >
                                PK
                              </text>
                            )}

                            {/* FK Badge */}
                            {isFk && !col.isPrimaryKey && (
                              <rect
                                x="12"
                                y={y - 12}
                                width="20"
                                height="14"
                                rx="3"
                                fill="rgba(77, 171, 247, 0.25)"
                              />
                            )}
                            {isFk && !col.isPrimaryKey && (
                              <text
                                x="22"
                                y={y - 1}
                                fill="#4dabf7"
                                fontSize="9"
                                fontWeight="700"
                                textAnchor="middle"
                              >
                                FK
                              </text>
                            )}

                            {/* Column Name */}
                            <text
                              x={col.isPrimaryKey || isFk ? 38 : 14}
                              y={y}
                              fill="var(--text-primary, #ffffff)"
                              fontSize="12"
                              fontWeight={col.isPrimaryKey ? "600" : "400"}
                            >
                              {col.name.length > 18 ? `${col.name.slice(0, 16)}...` : col.name}
                            </text>

                            {/* Column Type */}
                            <text
                              x={cardWidth - 12}
                              y={y}
                              fill="var(--text-muted)"
                              fontSize="10"
                              textAnchor="end"
                            >
                              {col.type}
                            </text>
                          </g>
                        );
                      })}

                      {table.columns.length > 10 && (
                        <text
                          x={cardWidth / 2}
                          y={56 + visibleCols.length * 24 + 10}
                          fill="var(--text-muted)"
                          fontSize="10"
                          textAnchor="middle"
                          fontStyle="italic"
                        >
                          + {table.columns.length - 10} more columns
                        </text>
                      )}
                    </g>
                  );
                })}
              </g>
            </svg>
          </div>
        </div>
      )}

      {/* TAB: STORAGE TREEMAP */}
      {activeTab === "treemap" && (
        <div
          className="card"
          style={{
            padding: 0,
            overflow: "hidden",
            flex: 1,
            minHeight: 0,
            display: "flex",
            flexDirection: "column",
            position: "relative",
            boxSizing: "border-box",
          }}
        >
          {/* Treemap Toolbar */}
          <div
            style={{
              padding: "0.6rem 1rem",
              borderBottom: "1px solid var(--border-light)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "0.75rem",
              flexShrink: 0,
            }}
          >
            {/* Filters */}
            <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
              <span style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>Show:</span>
              <button
                className={`btn btn-small ${storageFilter === "all" ? "btn-primary" : "btn-outline"}`}
                onClick={() => setStorageFilter("all")}
              >
                All Objects
              </button>
              <button
                className={`btn btn-small ${storageFilter === "table" ? "btn-primary" : "btn-outline"}`}
                onClick={() => setStorageFilter("table")}
              >
                Tables Only
              </button>
              <button
                className={`btn btn-small ${storageFilter === "index" ? "btn-primary" : "btn-outline"}`}
                onClick={() => setStorageFilter("index")}
              >
                Indexes Only
              </button>

              <span style={{ fontSize: "0.85rem", color: "var(--text-muted)", marginLeft: "0.5rem" }}>Metric:</span>
              <select
                className="select"
                value={storageMetric}
                onChange={(e) => setStorageMetric(e.target.value as any)}
                style={{ padding: "0.25rem 0.5rem", fontSize: "0.85rem" }}
              >
                <option value="bytes">Disk Size (Bytes)</option>
                <option value="rows">Row Count</option>
                <option value="pages">Page Count</option>
              </select>
            </div>

            {/* Search & Actions */}
            <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
              <input
                type="text"
                placeholder="Filter table or index..."
                value={treemapSearch}
                onChange={(e) => setTreemapSearch(e.target.value)}
                style={{
                  padding: "0.3rem 0.6rem",
                  fontSize: "0.85rem",
                  borderRadius: "4px",
                  border: "1px solid var(--border-light)",
                  backgroundColor: "var(--bg-primary)",
                  color: "var(--text-primary)",
                  width: "180px",
                }}
              />
              <button
                className="btn btn-outline btn-small"
                onClick={fetchStorage}
                disabled={isLoadingStorage}
                title="Refresh Storage Treemap"
              >
                {isLoadingStorage ? "Refreshing..." : "↻ Refresh"}
              </button>
            </div>
          </div>

          {/* Stats Bar */}
          {storageData && (
            <div
              style={{
                display: "flex",
                gap: "1.5rem",
                padding: "0.5rem 1rem",
                backgroundColor: "rgba(255, 255, 255, 0.02)",
                borderBottom: "1px solid var(--border-light)",
                fontSize: "0.85rem",
                flexWrap: "wrap",
                flexShrink: 0,
              }}
            >
              <div>
                <span style={{ color: "var(--text-muted)" }}>Total Database Size: </span>
                <span style={{ fontWeight: 600, color: "var(--accent)" }}>{formatBytes(storageData.totalSizeBytes)}</span>
              </div>
              <div>
                <span style={{ color: "var(--text-muted)" }}>Allocated Pages: </span>
                <span>{storageData.pageCount.toLocaleString()} ({storageData.pageSize.toLocaleString()} B/page)</span>
              </div>
              <div>
                <span style={{ color: "var(--text-muted)" }}>Free Space: </span>
                <span style={{ color: storageData.freeSizeBytes > 0 ? "var(--warning, #ffc107)" : "var(--text-muted)" }}>
                  {formatBytes(storageData.freeSizeBytes)}
                </span>
              </div>
              <div>
                <span style={{ color: "var(--text-muted)" }}>Objects: </span>
                <span>{storageData.items.filter((i) => i.type === "table").length} tables, {storageData.items.filter((i) => i.type === "index").length} indexes</span>
              </div>
            </div>
          )}

          {/* Canvas Area */}
          <div
            ref={treemapContainerRef}
            style={{
              flex: 1,
              minHeight: 0,
              position: "relative",
              overflow: "hidden",
              backgroundColor: "var(--bg-primary, #0c0e14)",
            }}
          >
            {isLoadingStorage && (
              <div style={{ position: "absolute", top: "50%", left: "50%", transform: "translate(-50%, -50%)", color: "var(--text-muted)" }}>
                Computing SQLite Storage Treemap...
              </div>
            )}

            {!isLoadingStorage && treemapLeaves.length === 0 && (
              <div style={{ position: "absolute", top: "50%", left: "50%", transform: "translate(-50%, -50%)", color: "var(--text-muted)" }}>
                No database objects match current filter.
              </div>
            )}

            <svg
              width="100%"
              height="100%"
              style={{ display: "block" }}
            >
              <g>
                {treemapLeaves.map((leaf, idx) => {
                  const item = leaf.data as DatabaseStorageItem;
                  const w = Math.max(0, leaf.x1 - leaf.x0);
                  const h = Math.max(0, leaf.y1 - leaf.y0);
                  const isHovered = hoveredStorageItem?.name === item.name;
                  const isSelected = selectedStorageItem?.name === item.name;

                  let fill = "rgba(14, 165, 233, 0.2)";
                  let stroke = "rgba(14, 165, 233, 0.4)";
                  if (item.type === "index") {
                    fill = isHovered ? "rgba(139, 92, 246, 0.35)" : "rgba(139, 92, 246, 0.2)";
                    stroke = isSelected ? "var(--accent, #ffd166)" : "rgba(139, 92, 246, 0.45)";
                  } else if (item.type === "free") {
                    fill = isHovered ? "rgba(245, 158, 11, 0.35)" : "rgba(245, 158, 11, 0.2)";
                    stroke = isSelected ? "var(--accent, #ffd166)" : "rgba(245, 158, 11, 0.45)";
                  } else {
                    fill = isHovered ? "rgba(14, 165, 233, 0.35)" : "rgba(14, 165, 233, 0.2)";
                    stroke = isSelected ? "var(--accent, #ffd166)" : "rgba(14, 165, 233, 0.45)";
                  }

                  return (
                    <g
                      key={`${item.name}-${idx}`}
                      transform={`translate(${leaf.x0}, ${leaf.y0})`}
                      style={{ cursor: "pointer" }}
                      onClick={() => setSelectedStorageItem(item)}
                      onMouseEnter={() => setHoveredStorageItem(item)}
                      onMouseLeave={() => setHoveredStorageItem(null)}
                    >
                      <rect
                        width={w}
                        height={h}
                        fill={fill}
                        stroke={stroke}
                        strokeWidth={isSelected ? 2 : 1}
                        strokeDasharray={item.type === "free" ? "4 2" : undefined}
                        rx={3}
                      />

                      {w > 45 && h > 22 && (
                        <text
                          x={6}
                          y={16}
                          fill="var(--text-primary, #ffffff)"
                          fontSize={w > 120 ? "12" : "10"}
                          fontWeight="600"
                          style={{ pointerEvents: "none" }}
                        >
                          {item.name.length * 7 > w ? `${item.name.slice(0, Math.max(3, Math.floor(w / 8)))}…` : item.name}
                        </text>
                      )}

                      {w > 75 && h > 40 && (
                        <text
                          x={6}
                          y={32}
                          fill="var(--text-muted)"
                          fontSize="10"
                          style={{ pointerEvents: "none" }}
                        >
                          {storageMetric === "bytes"
                            ? formatBytes(item.bytes)
                            : storageMetric === "rows"
                            ? `${item.rowCount.toLocaleString()} rows`
                            : `${item.pageCount.toLocaleString()} pages`}
                        </text>
                      )}

                      {w > 90 && h > 56 && (
                        <text
                          x={6}
                          y={46}
                          fill="var(--accent, #ffd166)"
                          fontSize="10"
                          fontWeight="500"
                          style={{ pointerEvents: "none" }}
                        >
                          {item.percentage}%
                        </text>
                      )}
                    </g>
                  );
                })}
              </g>
            </svg>

            {/* Hover Tooltip */}
            {hoveredStorageItem && (
              <div
                style={{
                  position: "absolute",
                  bottom: "1rem",
                  right: "1rem",
                  backgroundColor: "rgba(16, 17, 26, 0.95)",
                  border: "1px solid var(--border-light)",
                  borderRadius: "6px",
                  padding: "0.75rem 1rem",
                  pointerEvents: "none",
                  boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
                  zIndex: 20,
                  maxWidth: "320px",
                  fontSize: "0.85rem",
                }}
              >
                <div style={{ fontWeight: 600, color: "var(--accent)", marginBottom: "0.35rem" }}>
                  {hoveredStorageItem.name}
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", gap: "1rem", color: "var(--text-muted)" }}>
                  <span>Type:</span>
                  <span style={{ color: "var(--text-primary)", textTransform: "capitalize" }}>{hoveredStorageItem.type}</span>
                </div>
                {hoveredStorageItem.tableName !== hoveredStorageItem.name && (
                  <div style={{ display: "flex", justifyContent: "space-between", gap: "1rem", color: "var(--text-muted)" }}>
                    <span>Table:</span>
                    <span style={{ color: "var(--text-primary)" }}>{hoveredStorageItem.tableName}</span>
                  </div>
                )}
                <div style={{ display: "flex", justifyContent: "space-between", gap: "1rem", color: "var(--text-muted)" }}>
                  <span>Size:</span>
                  <span style={{ color: "var(--text-primary)", fontWeight: 600 }}>{formatBytes(hoveredStorageItem.bytes)}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", gap: "1rem", color: "var(--text-muted)" }}>
                  <span>Percentage:</span>
                  <span style={{ color: "var(--text-primary)" }}>{hoveredStorageItem.percentage}% of DB</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", gap: "1rem", color: "var(--text-muted)" }}>
                  <span>Pages:</span>
                  <span style={{ color: "var(--text-primary)" }}>{hoveredStorageItem.pageCount.toLocaleString()}</span>
                </div>
                {hoveredStorageItem.rowCount > 0 && (
                  <div style={{ display: "flex", justifyContent: "space-between", gap: "1rem", color: "var(--text-muted)" }}>
                    <span>Rows:</span>
                    <span style={{ color: "var(--text-primary)" }}>{hoveredStorageItem.rowCount.toLocaleString()}</span>
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Selected Item Drawer */}
          {selectedStorageItem && (
            <div
              style={{
                padding: "0.75rem 1rem",
                borderTop: "1px solid var(--border-light)",
                backgroundColor: "rgba(255, 255, 255, 0.03)",
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                flexWrap: "wrap",
                gap: "0.75rem",
                flexShrink: 0,
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
                <span style={{ fontWeight: 600, fontSize: "0.95rem" }}>
                  Selected: {selectedStorageItem.name}
                </span>
                <span
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.15rem 0.45rem",
                    borderRadius: "4px",
                    backgroundColor: selectedStorageItem.type === "table" ? "rgba(14, 165, 233, 0.2)" : "rgba(139, 92, 246, 0.2)",
                    color: selectedStorageItem.type === "table" ? "#0ea5e9" : "#8b5cf6",
                    textTransform: "uppercase",
                    fontWeight: 600,
                  }}
                >
                  {selectedStorageItem.type}
                </span>
                <span style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
                  {formatBytes(selectedStorageItem.bytes)} ({selectedStorageItem.percentage}%) • {selectedStorageItem.pageCount} pages
                  {selectedStorageItem.rowCount > 0 ? ` • ${selectedStorageItem.rowCount.toLocaleString()} rows` : ""}
                </span>
              </div>

              <div style={{ display: "flex", gap: "0.5rem" }}>
                {selectedStorageItem.type === "table" && (
                  <>
                    <button
                      className="btn btn-outline btn-small"
                      onClick={() => {
                        setSelectedTable(selectedStorageItem.tableName);
                        setActiveTab("inspector");
                      }}
                    >
                      🔍 Inspect Schema
                    </button>
                    <button
                      className="btn btn-primary btn-small"
                      onClick={() => {
                        handleSelectTableForQuery(selectedStorageItem.tableName);
                        setActiveTab("console");
                      }}
                    >
                      💻 Query Table
                    </button>
                  </>
                )}
                <button
                  className="btn btn-outline btn-small"
                  onClick={() => setSelectedStorageItem(null)}
                >
                  ✕ Close
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* TAB 3: SQL CONSOLE */}
      {activeTab === "console" && (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "260px 1fr",
            gap: "1.25rem",
            flex: 1,
            minHeight: 0,
            overflow: "hidden",
          }}
        >
          {/* Left Table Sidebar */}
          <div
            className="card"
            style={{
              padding: "1rem",
              display: "flex",
              flexDirection: "column",
              minHeight: 0,
              height: "100%",
              overflow: "hidden",
              boxSizing: "border-box",
            }}
          >
            <h3 style={{ fontSize: "1rem", fontWeight: 600, margin: "0 0 0.75rem", flexShrink: 0 }}>
              Tables ({tables.length})
            </h3>
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.35rem",
                flex: 1,
                minHeight: 0,
                overflowY: "auto",
              }}
            >
              {tables.map((t) => (
                <div
                  key={t.name}
                  onClick={() => handleSelectTableForQuery(t.name)}
                  style={{
                    padding: "0.45rem 0.65rem",
                    borderRadius: "6px",
                    cursor: "pointer",
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    backgroundColor: selectedTable === t.name ? "rgba(255, 209, 102, 0.15)" : "transparent",
                    color: selectedTable === t.name ? "var(--accent, #ffd166)" : "var(--text-primary)",
                  }}
                  className="table-item-hover"
                >
                  <span style={{ fontSize: "0.875rem", fontWeight: 500 }}>{t.name}</span>
                  <span
                    style={{
                      fontSize: "0.75rem",
                      backgroundColor: "rgba(255, 255, 255, 0.08)",
                      padding: "0.15rem 0.4rem",
                      borderRadius: "10px",
                      color: "var(--text-muted)",
                    }}
                  >
                    {t.rowCount}
                  </span>
                </div>
              ))}
            </div>
          </div>

          {/* Right Editor & Results */}
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "1rem",
              flex: 1,
              minHeight: 0,
              height: "100%",
              overflow: "hidden",
            }}
          >
            {/* Editor Card */}
            <div className="card" style={{ padding: "1rem", flexShrink: 0 }}>
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: "0.6rem",
                  flexWrap: "wrap",
                  gap: "0.5rem",
                }}
              >
                <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                  <span style={{ fontWeight: 600, fontSize: "0.95rem" }}>SQL Query Editor</span>
                  <select
                    className="form-control"
                    style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem", width: "230px" }}
                    onChange={(e) => {
                      if (e.target.value) setQuery(e.target.value);
                    }}
                    value=""
                  >
                    <option value="" disabled>
                      Presets & Diagnostics...
                    </option>
                    <option value={`SELECT * FROM "${selectedTable || "Torrents"}" LIMIT 50;`}>
                      SELECT * FROM {selectedTable || "Torrents"}
                    </option>
                    <option value={`SELECT COUNT(*) as count FROM "${selectedTable || "Torrents"}";`}>
                      Count in {selectedTable || "Torrents"}
                    </option>
                    <option value="PRAGMA integrity_check;">PRAGMA integrity_check</option>
                    <option value="PRAGMA foreign_key_check;">PRAGMA foreign_key_check</option>
                    <option value="SELECT name, type FROM sqlite_master WHERE type IN ('table','index');">
                      List all tables and indexes
                    </option>
                  </select>
                </div>

                <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
                  <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>Ctrl+Enter to run</span>
                  <button
                    className="btn btn-primary"
                    onClick={() => runQuery()}
                    disabled={isExecuting}
                    style={{ padding: "0.35rem 0.85rem" }}
                  >
                    {isExecuting ? "Executing..." : "▶ Run Query"}
                  </button>
                </div>
              </div>

              <textarea
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={(e) => {
                  if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
                    e.preventDefault();
                    runQuery();
                  }
                }}
                rows={4}
                style={{
                  width: "100%",
                  fontFamily: "monospace",
                  fontSize: "0.9rem",
                  padding: "0.75rem",
                  borderRadius: "6px",
                  backgroundColor: "var(--bg-primary, #11141c)",
                  color: "var(--text-primary, #fff)",
                  border: "1px solid var(--border-light)",
                  resize: "vertical",
                  boxSizing: "border-box",
                }}
              />
            </div>

            {/* Results Card */}
            <div
              className="card"
              style={{
                padding: "1rem",
                flex: 1,
                minHeight: 0,
                overflow: "hidden",
                display: "flex",
                flexDirection: "column",
                boxSizing: "border-box",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: "0.75rem",
                  flexShrink: 0,
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
                  <h3 style={{ fontSize: "1rem", fontWeight: 600, margin: 0 }}>Results</h3>
                  {queryResult && (
                    <span
                      style={{
                        fontSize: "0.8rem",
                        padding: "0.2rem 0.5rem",
                        borderRadius: "4px",
                        backgroundColor: queryResult.success
                          ? "rgba(40, 167, 69, 0.15)"
                          : "rgba(220, 53, 69, 0.15)",
                        color: queryResult.success ? "var(--success, #28a745)" : "var(--danger, #dc3545)",
                        border: `1px solid ${queryResult.success ? "rgba(40, 167, 69, 0.3)" : "rgba(220, 53, 69, 0.3)"}`,
                      }}
                    >
                      {queryResult.success ? "✓ Success" : "✕ Error"} ({queryResult.executionTimeMs.toFixed(1)}ms)
                    </span>
                  )}
                  {queryResult?.message && (
                    <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                      {queryResult.message}
                    </span>
                  )}

                  {queryResult?.queryPlan && queryResult.queryPlan.length > 0 && (
                    <div style={{ display: "flex", gap: "0.25rem", marginLeft: "0.5rem" }}>
                      <button
                        className={`btn btn-small ${consoleView === "data" ? "btn-primary" : "btn-outline"}`}
                        style={{ padding: "0.15rem 0.5rem", fontSize: "0.75rem" }}
                        onClick={() => setConsoleView("data")}
                      >
                        📊 Data Grid ({queryResult.rows?.length ?? 0})
                      </button>
                      <button
                        className={`btn btn-small ${consoleView === "plan" ? "btn-primary" : "btn-outline"}`}
                        style={{ padding: "0.15rem 0.5rem", fontSize: "0.75rem" }}
                        onClick={() => setConsoleView("plan")}
                      >
                        ⚡ Query Plan DAG ({queryResult.queryPlan.length})
                      </button>
                    </div>
                  )}
                </div>

                {queryResult?.columns && queryResult.columns.length > 0 && consoleView === "data" && (
                  <button className="btn btn-outline btn-small" onClick={exportCsv}>
                    📥 Export CSV
                  </button>
                )}
              </div>

              {queryResult?.errorMessage && (
                <div
                  style={{
                    padding: "0.75rem 1rem",
                    borderRadius: "6px",
                    backgroundColor: "rgba(220, 53, 69, 0.15)",
                    color: "var(--danger, #dc3545)",
                    border: "1px solid rgba(220, 53, 69, 0.3)",
                    fontSize: "0.85rem",
                    fontFamily: "monospace",
                    flexShrink: 0,
                    marginBottom: "0.5rem",
                  }}
                >
                  {queryResult.errorMessage}
                </div>
              )}

              {/* Data Grid View */}
              {consoleView === "data" && queryResult?.columns && queryResult.rows && (
                <div style={{ flex: 1, minHeight: 0, overflowX: "auto", overflowY: "auto" }}>
                  <table
                    className="torrent-table"
                    style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem" }}
                  >
                    <thead>
                      <tr style={{ borderBottom: "1px solid var(--border-light)" }}>
                        {queryResult.columns.map((col) => (
                          <th
                            key={col}
                            style={{
                              padding: "0.5rem 0.75rem",
                              textAlign: "left",
                              backgroundColor: "rgba(255,255,255,0.03)",
                              position: "sticky",
                              top: 0,
                            }}
                          >
                            {col}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {queryResult.rows.map((row, rIdx) => (
                        <tr
                          key={rIdx}
                          style={{
                            borderBottom: "1px solid var(--border-light, rgba(255,255,255,0.05))",
                          }}
                        >
                          {row.map((val, cIdx) => (
                            <td
                              key={cIdx}
                              style={{
                                padding: "0.45rem 0.75rem",
                                whiteSpace: "nowrap",
                                maxWidth: "300px",
                                overflow: "hidden",
                                textOverflow: "ellipsis",
                              }}
                            >
                              {val === null || val === undefined ? (
                                <span style={{ color: "var(--text-muted)", fontStyle: "italic" }}>
                                  NULL
                                </span>
                              ) : typeof val === "boolean" ? (
                                <span
                                  style={{
                                    color: val ? "var(--success)" : "var(--danger)",
                                    fontWeight: 600,
                                  }}
                                >
                                  {val ? "TRUE" : "FALSE"}
                                </span>
                              ) : (
                                String(val)
                              )}
                            </td>
                          ))}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {/* Query Plan DAG View */}
              {consoleView === "plan" && queryResult?.queryPlan && (
                <div style={{ flex: 1, minHeight: 0, overflowY: "auto", display: "flex", flexDirection: "column", gap: "0.75rem" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem", flexShrink: 0 }}>
                    <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
                      SQLite Execution Plan DAG (EXPLAIN QUERY PLAN)
                    </div>
                    <div style={{ display: "flex", gap: "0.25rem" }}>
                      <button
                        className={`btn btn-small ${planViewMode === "tree" ? "btn-primary" : "btn-outline"}`}
                        style={{ padding: "0.15rem 0.5rem", fontSize: "0.75rem" }}
                        onClick={() => setPlanViewMode("tree")}
                      >
                        Visual Tree
                      </button>
                      <button
                        className={`btn btn-small ${planViewMode === "table" ? "btn-primary" : "btn-outline"}`}
                        style={{ padding: "0.15rem 0.5rem", fontSize: "0.75rem" }}
                        onClick={() => setPlanViewMode("table")}
                      >
                        Raw Plan Table
                      </button>
                    </div>
                  </div>

                  {planViewMode === "tree" ? (
                    <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem", padding: "0.5rem 0" }}>
                      {(() => {
                        const roots = parseQueryPlanNodes(queryResult.queryPlan);
                        const renderNode = (node: PlanTreeNode, depth: number) => {
                          const badge = getPlanBadge(node.detail);
                          return (
                            <div key={node.id} style={{ display: "flex", flexDirection: "column", gap: "0.35rem" }}>
                              <div
                                style={{
                                  marginLeft: depth > 0 ? `${depth * 28}px` : "0px",
                                  display: "flex",
                                  alignItems: "center",
                                  gap: "0.6rem",
                                  padding: "0.5rem 0.75rem",
                                  borderRadius: "6px",
                                  backgroundColor: "rgba(255, 255, 255, 0.03)",
                                  border: "1px solid var(--border-light)",
                                  borderLeft: `3px solid ${badge.color}`,
                                }}
                              >
                                <span
                                  style={{
                                    fontSize: "0.75rem",
                                    fontFamily: "monospace",
                                    color: "var(--text-muted)",
                                    backgroundColor: "rgba(255, 255, 255, 0.06)",
                                    padding: "0.1rem 0.35rem",
                                    borderRadius: "3px",
                                  }}
                                >
                                  #{node.id}
                                </span>
                                <span
                                  style={{
                                    fontSize: "0.75rem",
                                    fontWeight: 600,
                                    padding: "0.15rem 0.45rem",
                                    borderRadius: "4px",
                                    backgroundColor: badge.bg,
                                    color: badge.color,
                                    display: "inline-flex",
                                    alignItems: "center",
                                    gap: "0.3rem",
                                  }}
                                >
                                  <span>{badge.icon}</span> {badge.label}
                                </span>
                                <span style={{ fontSize: "0.85rem", fontFamily: "monospace", color: "var(--text-primary)" }}>
                                  {node.detail}
                                </span>
                              </div>
                              {node.children.map((child) => renderNode(child, depth + 1))}
                            </div>
                          );
                        };
                        return roots.map((r) => renderNode(r, 0));
                      })()}
                    </div>
                  ) : (
                    <table className="torrent-table" style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem" }}>
                      <thead>
                        <tr>
                          <th style={{ padding: "0.4rem 0.6rem", textAlign: "left" }}>ID</th>
                          <th style={{ padding: "0.4rem 0.6rem", textAlign: "left" }}>Parent ID</th>
                          <th style={{ padding: "0.4rem 0.6rem", textAlign: "left" }}>Detail</th>
                        </tr>
                      </thead>
                      <tbody>
                        {queryResult.queryPlan.map((node) => (
                          <tr key={node.id}>
                            <td style={{ padding: "0.4rem 0.6rem", fontFamily: "monospace" }}>{node.id}</td>
                            <td style={{ padding: "0.4rem 0.6rem", fontFamily: "monospace" }}>{node.parentId}</td>
                            <td style={{ padding: "0.4rem 0.6rem", fontFamily: "monospace" }}>{node.detail}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* TAB 3: TABLE INSPECTOR */}
      {activeTab === "inspector" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem", flex: 1, minHeight: 0, overflowY: "auto" }}>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "1rem",
              padding: "0.75rem 1rem",
              backgroundColor: "var(--bg-card)",
              borderRadius: "8px",
              border: "1px solid var(--border-light)",
            }}
          >
            <span style={{ fontWeight: 600 }}>Select Table:</span>
            <select
              className="form-control"
              style={{ width: "240px" }}
              value={selectedTable}
              onChange={(e) => setSelectedTable(e.target.value)}
            >
              {tables.map((t) => (
                <option key={t.name} value={t.name}>
                  {t.name} ({t.rowCount} rows)
                </option>
              ))}
            </select>

            <button
              className="btn btn-primary btn-small"
              onClick={() => handleSelectTableForQuery(selectedTable)}
            >
              ▶ Query Table
            </button>
          </div>

          {currentInspectorTable && (
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1.25rem" }}>
              {/* Columns Card */}
              <div className="card" style={{ gridColumn: "span 2", padding: "1rem" }}>
                <h3 style={{ fontSize: "1.05rem", fontWeight: 600, marginBottom: "0.75rem" }}>
                  Columns ({currentInspectorTable.columns.length})
                </h3>
                <div style={{ overflowX: "auto" }}>
                  <table className="torrent-table" style={{ width: "100%" }}>
                    <thead>
                      <tr>
                        <th>#</th>
                        <th>Name</th>
                        <th>Type</th>
                        <th>Primary Key</th>
                        <th>Not Null</th>
                        <th>Default</th>
                      </tr>
                    </thead>
                    <tbody>
                      {currentInspectorTable.columns.map((col) => (
                        <tr key={col.name}>
                          <td>{col.cid}</td>
                          <td style={{ fontWeight: 600 }}>{col.name}</td>
                          <td>
                            <code>{col.type}</code>
                          </td>
                          <td>{col.isPrimaryKey ? <span className="badge badge-seeding">PK</span> : "-"}</td>
                          <td>{col.notNull ? "YES" : "NO"}</td>
                          <td>{col.defaultValue || "-"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              {/* Foreign Keys Card */}
              <div className="card" style={{ padding: "1rem" }}>
                <h3 style={{ fontSize: "1.05rem", fontWeight: 600, marginBottom: "0.75rem" }}>
                  Foreign Keys ({currentInspectorTable.foreignKeys?.length || 0})
                </h3>
                {(!currentInspectorTable.foreignKeys || currentInspectorTable.foreignKeys.length === 0) ? (
                  <p style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}>No foreign keys defined.</p>
                ) : (
                  <table className="torrent-table" style={{ width: "100%" }}>
                    <thead>
                      <tr>
                        <th>Column</th>
                        <th>References</th>
                        <th>On Delete</th>
                      </tr>
                    </thead>
                    <tbody>
                      {currentInspectorTable.foreignKeys.map((fk) => (
                        <tr key={fk.id}>
                          <td>
                            <strong>{fk.fromColumn}</strong>
                          </td>
                          <td>
                            <code>{fk.toTable}.{fk.toColumn}</code>
                          </td>
                          <td>{fk.onDelete}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>

              {/* Indexes Card */}
              <div className="card" style={{ padding: "1rem" }}>
                <h3 style={{ fontSize: "1.05rem", fontWeight: 600, marginBottom: "0.75rem" }}>
                  Indexes ({currentInspectorTable.indexes?.length || 0})
                </h3>
                {(!currentInspectorTable.indexes || currentInspectorTable.indexes.length === 0) ? (
                  <p style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}>No indexes defined.</p>
                ) : (
                  <table className="torrent-table" style={{ width: "100%" }}>
                    <thead>
                      <tr>
                        <th>Name</th>
                        <th>Unique</th>
                        <th>Columns</th>
                      </tr>
                    </thead>
                    <tbody>
                      {currentInspectorTable.indexes.map((idx) => (
                        <tr key={idx.name}>
                          <td>{idx.name}</td>
                          <td>{idx.unique ? "YES" : "NO"}</td>
                          <td>{idx.columns.join(", ")}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Confirmation Modal for Edit Mode Mutations */}
      {showConfirmModal && (
        <div
          className="modal-overlay"
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0,0,0,0.7)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
          }}
        >
          <div className="card" style={{ maxWidth: "500px", width: "90%", padding: "1.5rem" }}>
            <h3 style={{ margin: "0 0 0.75rem", color: "var(--warning, #ffc107)" }}>
              ⚠️ Confirm Database Mutation
            </h3>
            <p style={{ fontSize: "0.9rem", color: "var(--text-secondary)" }}>
              You are about to execute a write statement against the live SQLite database in Edit Mode:
            </p>
            <pre
              style={{
                backgroundColor: "var(--bg-primary)",
                padding: "0.75rem",
                borderRadius: "4px",
                overflowX: "auto",
                fontSize: "0.85rem",
              }}
            >
              {query}
            </pre>
            <p style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
              This may alter or delete stored records permanently.
            </p>
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.75rem", marginTop: "1rem" }}>
              <button className="btn btn-outline" onClick={() => setShowConfirmModal(false)}>
                Cancel
              </button>
              <button className="btn btn-danger" onClick={() => runQuery(query, true)}>
                Execute Mutation
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Mermaid Source Modal */}
      {showMermaidModal && (
        <div
          className="modal-overlay"
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0,0,0,0.7)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
          }}
        >
          <div
            className="card"
            style={{ maxWidth: "700px", width: "90%", maxHeight: "80vh", display: "flex", flexDirection: "column", padding: "1.5rem" }}
          >
            <h3 style={{ margin: "0 0 0.75rem" }}>Mermaid ERD Source</h3>
            <textarea
              readOnly
              value={schema?.mermaidErd || ""}
              style={{
                flex: 1,
                fontFamily: "monospace",
                fontSize: "0.85rem",
                backgroundColor: "var(--bg-primary)",
                color: "var(--text-primary)",
                padding: "0.75rem",
                borderRadius: "6px",
                border: "1px solid var(--border-light)",
                resize: "none",
                minHeight: "350px",
              }}
            />
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.75rem", marginTop: "1rem" }}>
              <button className="btn btn-primary" onClick={copyMermaid}>
                {copyFeedback || "📋 Copy to Clipboard"}
              </button>
              <button className="btn btn-outline" onClick={() => setShowMermaidModal(false)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
