import { useTranslation } from "../i18n";
import { useRef, useEffect, useState, useCallback, useMemo } from "react";
import { useNavigate } from "react-router";
import * as d3 from "d3";
import { usePeerGraph, useTorrents } from "../api/hooks";
import type { PeerGraphNode } from "../api/types";

interface SimNode extends d3.SimulationNodeDatum, PeerGraphNode {}
interface SimLink extends d3.SimulationLinkDatum<SimNode> {
  type: string;
}

const NODE_COLORS: Record<string, string> = {
  center: "#c8a84e",
  torrent: "#27ae60",
  peer: "#3498db",
};

const LINK_COLORS: Record<string, string> = {
  seeds: "rgba(200, 168, 78, 0.6)",
  encrypted: "rgba(39, 174, 96, 0.6)",
  plain: "rgba(255, 255, 255, 0.2)",
};

function getTimeRange(hours: number): { start: string; end: string } {
  const end = new Date();
  const start = new Date(end.getTime() - hours * 60 * 60 * 1000);
  return {
    start: start.toISOString(),
    end: end.toISOString(),
  };
}

function PeerMap() {
  const { t } = useTranslation();

  const svgRef = useRef<SVGSVGElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [hours, setHours] = useState(1);
  const [selectedTorrentFilter, setSelectedTorrentFilter] =
    useState<string>("all");
  const [selectedNode, setSelectedNode] = useState<SimNode | null>(null);
  const [dimensions, setDimensions] = useState({ width: 800, height: 600 });
  const navigate = useNavigate();

  const { data: torrentsList } = useTorrents();
  const range = useMemo(() => getTimeRange(hours), [hours]);
  const {
    data: graphData,
    isLoading,
    isError,
  } = usePeerGraph(range.start, range.end);

  const zoomRef = useRef<d3.ZoomBehavior<SVGSVGElement, unknown> | null>(null);
  const simRef = useRef<d3.Simulation<SimNode, SimLink> | null>(null);
  const nodeMapRef = useRef<
    Map<
      string,
      {
        x: number;
        y: number;
        vx?: number;
        vy?: number;
        fx?: number | null;
        fy?: number | null;
      }
    >
  >(new Map());

  useEffect(() => {
    return () => {
      if (simRef.current) {
        simRef.current.stop();
        simRef.current = null;
      }
    };
  }, []);

  const updateDimensions = useCallback(() => {
    if (containerRef.current) {
      const rect = containerRef.current.getBoundingClientRect();
      setDimensions({
        width: Math.max(rect.width, 400),
        height: Math.max(rect.height, 400),
      });
    }
  }, []);

  useEffect(() => {
    updateDimensions();
    window.addEventListener("resize", updateDimensions);
    return () => window.removeEventListener("resize", updateDimensions);
  }, [updateDimensions]);

  useEffect(() => {
    if (!graphData || !svgRef.current) return;

    const svg = d3.select(svgRef.current);
    const { width, height } = dimensions;

    // Filter nodes if a specific torrent is selected
    let rawNodes = graphData.nodes;
    let rawLinks = graphData.links;

    if (selectedTorrentFilter !== "all") {
      const targetTorrentNode = rawNodes.find(
        (n) =>
          n.id === selectedTorrentFilter ||
          n.infoHash === selectedTorrentFilter,
      );
      if (targetTorrentNode) {
        const connectedPeerIds = new Set(
          rawLinks
            .filter(
              (l) =>
                l.source === targetTorrentNode.id ||
                l.target === targetTorrentNode.id,
            )
            .map((l) =>
              l.source === targetTorrentNode.id ? l.target : l.source,
            ),
        );
        connectedPeerIds.add(targetTorrentNode.id);
        const centerId = rawNodes.find((n) => n.type === "center")?.id;
        if (centerId) connectedPeerIds.add(centerId);

        rawNodes = rawNodes.filter((n) => connectedPeerIds.has(n.id));
        rawLinks = rawLinks.filter(
          (l) =>
            connectedPeerIds.has(l.source as string) &&
            connectedPeerIds.has(l.target as string),
        );
      }
    }

    const nodes: SimNode[] = rawNodes.map((n) => ({ ...n }));
    const links: SimLink[] = rawLinks.map((l) => ({
      source: l.source,
      target: l.target,
      type: l.type,
    }));

    const numTorrents = nodes.filter((n) => n.type === "torrent").length;
    const numPeers = nodes.filter((n) => n.type === "peer").length;

    // Dynamic radius based on torrent count to avoid cluster overlap
    const baseTorrentRadius = Math.max(
      220,
      Math.min(width, height) * 0.35,
      numTorrents * 12,
    );
    const peerDistance = Math.max(90, Math.min(130, 800 / (numPeers || 1)));

    // Preserve existing node coordinates for warm-start force simulation
    const nodeMap = nodeMapRef.current;
    let tIdx = 0;
    nodes.forEach((n) => {
      const prev = nodeMap.get(n.id);
      if (prev && prev.x !== undefined && prev.y !== undefined) {
        n.x = prev.x;
        n.y = prev.y;
        n.vx = prev.vx;
        n.vy = prev.vy;
      } else {
        // Pre-distribute brand new nodes
        if (n.type === "center") {
          n.x = width / 2;
          n.y = height / 2;
        } else if (n.type === "torrent") {
          const angle = (tIdx / (numTorrents || 1)) * 2 * Math.PI - Math.PI / 2;
          n.x = width / 2 + Math.cos(angle) * baseTorrentRadius;
          n.y = height / 2 + Math.sin(angle) * baseTorrentRadius;
        } else {
          // Peer: place near parent torrent if found
          const connectedLink = links.find((l) => {
            const s =
              typeof l.source === "object"
                ? (l.source as SimNode).id
                : l.source;
            const t =
              typeof l.target === "object"
                ? (l.target as SimNode).id
                : l.target;
            return s === n.id || t === n.id;
          });
          const torrentId = connectedLink
            ? (typeof connectedLink.source === "object"
                ? (connectedLink.source as SimNode).id
                : connectedLink.source) === n.id
              ? typeof connectedLink.target === "object"
                ? (connectedLink.target as SimNode).id
                : connectedLink.target
              : typeof connectedLink.source === "object"
                ? (connectedLink.source as SimNode).id
                : connectedLink.source
            : null;
          const parent = torrentId
            ? nodes.find((node) => node.id === torrentId)
            : null;
          if (parent && parent.x !== undefined && parent.y !== undefined) {
            n.x = parent.x + (Math.random() - 0.5) * 40;
            n.y = parent.y + (Math.random() - 0.5) * 40;
          } else {
            n.x = width / 2 + (Math.random() - 0.5) * 100;
            n.y = height / 2 + (Math.random() - 0.5) * 100;
          }
        }
      }

      if (n.type === "center") {
        n.fx = width / 2;
        n.fy = height / 2;
      } else if (prev?.fx !== undefined && prev.fx !== null) {
        n.fx = prev.fx;
        n.fy = prev.fy;
      }

      if (n.type === "torrent") {
        tIdx++;
      }
    });

    // Ensure persistent defs for arrow marker
    let defs = svg.select<SVGDefsElement>("defs");
    if (defs.empty()) {
      defs = svg.append("defs");
      defs
        .append("marker")
        .attr("id", "arrowhead")
        .attr("viewBox", "0 -5 10 10")
        .attr("refX", 22)
        .attr("refY", 0)
        .attr("markerWidth", 6)
        .attr("markerHeight", 6)
        .attr("orient", "auto")
        .append("path")
        .attr("d", "M0,-5L10,0L0,5")
        .attr("fill", "rgba(255, 255, 255, 0.4)");
    }

    // Ensure persistent container group for zoom/pan
    let g = svg.select<SVGGElement>("g.graph-container");
    if (g.empty()) {
      g = svg.append("g").attr("class", "graph-container");
    }

    // Attach zoom once and preserve transform
    if (!zoomRef.current) {
      const zoom = d3
        .zoom<SVGSVGElement, unknown>()
        .scaleExtent([0.1, 5])
        .on("zoom", (event) => {
          svg.select("g.graph-container").attr("transform", event.transform);
        });

      zoomRef.current = zoom;
      svg.call(zoom);
    }

    // Re-apply existing zoom transform to graph container
    const currentTransform = d3.zoomTransform(svg.node()!);
    g.attr("transform", currentTransform.toString());

    // Warm-start force simulation: reuse existing simulation if present
    let simulation = simRef.current;
    if (!simulation) {
      simulation = d3
        .forceSimulation<SimNode>(nodes)
        .force(
          "link",
          d3
            .forceLink<SimNode, SimLink>(links)
            .id((d) => d.id)
            .distance((d) =>
              d.type === "seeds" ? baseTorrentRadius : peerDistance,
            )
            .strength((d) => (d.type === "seeds" ? 0.6 : 0.8)),
        )
        .force(
          "charge",
          d3
            .forceManyBody<SimNode>()
            .strength((d) =>
              d.type === "center" ? -1200 : d.type === "torrent" ? -500 : -200,
            )
            .distanceMax(Math.max(width, height) * 1.5),
        )
        .force("center", d3.forceCenter(width / 2, height / 2).strength(0.06))
        .force(
          "collision",
          d3
            .forceCollide<SimNode>()
            .radius((d) =>
              d.type === "center" ? 48 : d.type === "torrent" ? 42 : 24,
            )
            .iterations(2),
        );
      simRef.current = simulation;
    } else {
      simulation.nodes(nodes);
      const linkForce = simulation.force("link") as d3.ForceLink<
        SimNode,
        SimLink
      >;
      if (linkForce) {
        linkForce
          .id((d) => d.id)
          .distance((d) =>
            d.type === "seeds" ? baseTorrentRadius : peerDistance,
          )
          .strength((d) => (d.type === "seeds" ? 0.6 : 0.8))
          .links(links);
      }
      const centerForce = simulation.force("center") as d3.ForceCenter<SimNode>;
      if (centerForce) {
        centerForce.x(width / 2).y(height / 2);
      }
      const chargeForce = simulation.force(
        "charge",
      ) as d3.ForceManyBody<SimNode>;
      if (chargeForce) {
        chargeForce.distanceMax(Math.max(width, height) * 1.5);
      }
      simulation.alpha(0.15).restart();
    }

    let linkGroup = g.select<SVGGElement>("g.links");
    if (linkGroup.empty()) {
      linkGroup = g.append("g").attr("class", "links");
    }

    const link = linkGroup
      .selectAll<SVGLineElement, SimLink>("line")
      .data(links, (d: SimLink) => {
        const s =
          typeof d.source === "object" ? (d.source as SimNode).id : d.source;
        const t =
          typeof d.target === "object" ? (d.target as SimNode).id : d.target;
        return `${s}->${t}`;
      })
      .join(
        (enter) =>
          enter
            .append("line")
            .attr(
              "stroke",
              (d) => LINK_COLORS[d.type] || "rgba(255, 255, 255, 0.2)",
            )
            .attr("stroke-width", (d) => (d.type === "seeds" ? 2 : 1.2))
            .attr("stroke-dasharray", (d) =>
              d.type === "encrypted" ? "4,4" : "none",
            )
            .attr("opacity", 0.7),
        (update) =>
          update
            .attr(
              "stroke",
              (d) => LINK_COLORS[d.type] || "rgba(255, 255, 255, 0.2)",
            )
            .attr("stroke-width", (d) => (d.type === "seeds" ? 2 : 1.2))
            .attr("stroke-dasharray", (d) =>
              d.type === "encrypted" ? "4,4" : "none",
            ),
        (exit) => exit.remove(),
      );

    const drag = d3
      .drag<SVGGElement, SimNode>()
      .on("start", (event, d) => {
        if (!event.active) simulation.alphaTarget(0.3).restart();
        d.fx = d.x;
        d.fy = d.y;
      })
      .on("drag", (event, d) => {
        d.fx = event.x;
        d.fy = event.y;
      })
      .on("end", (event, d) => {
        if (!event.active) simulation.alphaTarget(0);
        if (d.type !== "center") {
          d.fx = null;
          d.fy = null;
        }
      });

    let nodeGroup = g.select<SVGGElement>("g.nodes");
    if (nodeGroup.empty()) {
      nodeGroup = g.append("g").attr("class", "nodes");
    }

    const node = nodeGroup
      .selectAll<SVGGElement, SimNode>("g.node")
      .data(nodes, (d: SimNode) => d.id)
      .join(
        (enter) => {
          const gEnter = enter
            .append("g")
            .attr("class", "node")
            .style("cursor", "pointer");

          gEnter
            .append("circle")
            .attr("class", "node-circle")
            .attr("r", (d) => {
              if (d.type === "center") return 26;
              if (d.type === "torrent") return 15;
              return 9;
            })
            .attr("fill", (d) => NODE_COLORS[d.type] || "#666")
            .attr("stroke", "#111")
            .attr("stroke-width", 2)
            .attr("opacity", 0.95);

          gEnter
            .append("text")
            .attr("class", "node-label")
            .text((d) => {
              if (d.label.length > 18) {
                return d.label.substring(0, 16) + "...";
              }
              return d.label;
            })
            .attr("text-anchor", "middle")
            .attr("dy", (d) => {
              if (d.type === "center") return 44;
              if (d.type === "torrent") return 28;
              return 22;
            })
            .attr("fill", "var(--text-primary, #F8F4ED)")
            .style("filter", "drop-shadow(0px 1px 3px rgba(0, 0, 0, 0.95))")
            .attr("font-size", (d) => {
              if (d.type === "center") return "13px";
              if (d.type === "torrent") return "11px";
              return "9.5px";
            })
            .attr("font-weight", (d) => (d.type === "center" ? 700 : 600))
            .attr("letter-spacing", "0.02em")
            .attr("font-family", "inherit");

          gEnter
            .filter((d) => d.type === "center")
            .append("text")
            .attr("class", "node-icon")
            .text("⬢")
            .attr("text-anchor", "middle")
            .attr("dy", 6)
            .attr("fill", "#111")
            .attr("font-size", "22px");

          gEnter
            .filter((d) => d.type === "torrent")
            .append("text")
            .attr("class", "node-icon")
            .text("■")
            .attr("text-anchor", "middle")
            .attr("dy", 5)
            .attr("fill", "#111")
            .attr("font-size", "12px");

          gEnter
            .filter((d) => d.isEncrypted === true)
            .append("circle")
            .attr("class", "node-encrypted-badge")
            .attr("cx", (d) => (d.type === "peer" ? 8 : 15))
            .attr("cy", (d) => (d.type === "peer" ? -8 : -15))
            .attr("r", 5)
            .attr("fill", "#27ae60");

          gEnter.append("title").text((d) => {
            if (d.type === "center")
              return "Leecharr Instance (Click for details)";
            if (d.type === "torrent")
              return `Torrent: ${d.label}\n${d.infoHash || ""}\n(Click to view details)`;
            return `Peer: ${d.label}${d.isEncrypted ? " (encrypted)" : ""}\n(Click for details)`;
          });

          return gEnter;
        },
        (update) => {
          update.select("text.node-label").text((d) => {
            if (d.label.length > 18) {
              return d.label.substring(0, 16) + "...";
            }
            return d.label;
          });

          update.select("title").text((d) => {
            if (d.type === "center")
              return "Leecharr Instance (Click for details)";
            if (d.type === "torrent")
              return `Torrent: ${d.label}\n${d.infoHash || ""}\n(Click to view details)`;
            return `Peer: ${d.label}${d.isEncrypted ? " (encrypted)" : ""}\n(Click for details)`;
          });

          return update;
        },
        (exit) => exit.remove(),
      );

    node
      .call(drag)
      .on("click", (event, d) => {
        event.stopPropagation();
        setSelectedNode(d);
      })
      .on("mouseenter", (_event, d) => {
        const neighborIds = new Set<string>();
        neighborIds.add(d.id);
        links.forEach((l) => {
          const sId =
            typeof l.source === "object" ? (l.source as SimNode).id : l.source;
          const tId =
            typeof l.target === "object" ? (l.target as SimNode).id : l.target;
          if (sId === d.id) neighborIds.add(tId as string);
          if (tId === d.id) neighborIds.add(sId as string);
        });

        nodeGroup
          .selectAll<SVGGElement, SimNode>("g.node")
          .transition()
          .duration(150)
          .attr("opacity", (n) => (neighborIds.has(n.id) ? 1 : 0.2));

        linkGroup
          .selectAll<SVGLineElement, SimLink>("line")
          .transition()
          .duration(150)
          .attr("opacity", (l) => {
            const sId =
              typeof l.source === "object"
                ? (l.source as SimNode).id
                : l.source;
            const tId =
              typeof l.target === "object"
                ? (l.target as SimNode).id
                : l.target;
            return sId === d.id || tId === d.id ? 1 : 0.05;
          })
          .attr("stroke-width", (l) => {
            const sId =
              typeof l.source === "object"
                ? (l.source as SimNode).id
                : l.source;
            const tId =
              typeof l.target === "object"
                ? (l.target as SimNode).id
                : l.target;
            return sId === d.id || tId === d.id ? 2.5 : 1;
          });
      })
      .on("mouseleave", () => {
        nodeGroup
          .selectAll<SVGGElement, SimNode>("g.node")
          .transition()
          .duration(150)
          .attr("opacity", 1);
        linkGroup
          .selectAll<SVGLineElement, SimLink>("line")
          .transition()
          .duration(150)
          .attr("opacity", 0.7)
          .attr("stroke-width", (d) => (d.type === "seeds" ? 2 : 1.2));
      });

    // Position immediately to prevent frame flicker
    node.attr("transform", (d) => `translate(${d.x || 0},${d.y || 0})`);
    link
      .attr("x1", (d) => (d.source as SimNode).x || 0)
      .attr("y1", (d) => (d.source as SimNode).y || 0)
      .attr("x2", (d) => (d.target as SimNode).x || 0)
      .attr("y2", (d) => (d.target as SimNode).y || 0);

    simulation.on("tick", () => {
      link
        .attr("x1", (d) => (d.source as SimNode).x || 0)
        .attr("y1", (d) => (d.source as SimNode).y || 0)
        .attr("x2", (d) => (d.target as SimNode).x || 0)
        .attr("y2", (d) => (d.target as SimNode).y || 0);

      node.attr("transform", (d) => `translate(${d.x || 0},${d.y || 0})`);

      for (const n of nodes) {
        if (n.x !== undefined && n.y !== undefined) {
          nodeMapRef.current.set(n.id, {
            x: n.x,
            y: n.y,
            vx: n.vx,
            vy: n.vy,
            fx: n.fx,
            fy: n.fy,
          });
        }
      }
    });
  }, [graphData, dimensions, selectedTorrentFilter]);

  const handleZoomIn = () => {
    if (svgRef.current && zoomRef.current) {
      d3.select(svgRef.current)
        .transition()
        .duration(250)
        .call(zoomRef.current.scaleBy, 1.35);
    }
  };

  const handleZoomOut = () => {
    if (svgRef.current && zoomRef.current) {
      d3.select(svgRef.current)
        .transition()
        .duration(250)
        .call(zoomRef.current.scaleBy, 0.75);
    }
  };

  const handleResetZoom = () => {
    if (svgRef.current && zoomRef.current) {
      d3.select(svgRef.current)
        .transition()
        .duration(350)
        .call(zoomRef.current.transform, d3.zoomIdentity);
    }
  };

  const torrentCount =
    graphData?.nodes.filter((n) => n.type === "torrent").length ?? 0;
  const peerCount =
    graphData?.nodes.filter((n) => n.type === "peer").length ?? 0;

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      {/* Header Row */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "0.75rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {t("autogen.t_peer_map")}
            </h1>
            <span className="badge badge-primary">
              {peerCount} {t("autogen.t_connected_peers")}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            {t("autogen.t_live_swarm_topology_visualization_connec")}
          </div>
        </div>
      </div>

      {/* Control Toolbar */}
      <div className="peer-map-controls" style={{ flexShrink: 0 }}>
        <div style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
          <label className="peer-map-label">{t("autogen.t_time_range")}</label>
          {[1, 6, 12, 24].map((h) => (
            <button
              key={h}
              className={`peer-map-btn ${hours === h ? "peer-map-btn-active" : ""}`}
              onClick={() => setHours(h)}
            >
              {h}h
            </button>
          ))}
        </div>

        {/* Swarm Focus Filter */}
        <div style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
          <label className="peer-map-label">
            {t("autogen.t_filter_swarm")}
          </label>
          <select
            value={selectedTorrentFilter}
            onChange={(e) => setSelectedTorrentFilter(e.target.value)}
            style={{
              padding: "0.35rem 0.65rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary)",
              color: "inherit",
              fontSize: "0.82rem",
            }}
          >
            <option value="all">
              {t("autogen.t_all_torrents")}
              {torrentCount})
            </option>
            {torrentsList?.map((t) => (
              <option key={t.id} value={t.infoHash}>
                {t.name}
              </option>
            ))}
          </select>
        </div>

        <span className="peer-map-stats">
          📊 {torrentCount} {t("autogen.t_swarms")}
          {peerCount} {t("autogen.t_peers")}
        </span>
      </div>

      {/* Legend */}
      <div className="peer-map-legend" style={{ flexShrink: 0 }}>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-dot"
            style={{ background: NODE_COLORS.center }}
          />
          {t("autogen.t_leecharr")}
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-dot"
            style={{ background: NODE_COLORS.torrent }}
          />
          {t("autogen.t_torrent")}
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-dot"
            style={{ background: NODE_COLORS.peer }}
          />
          {t("autogen.t_peer")}
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-line"
            style={{
              borderColor: "#27ae60",
              borderStyle: "dashed",
            }}
          />
          {t("autogen.t_encrypted")}
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-line"
            style={{ borderColor: "rgba(255, 255, 255, 0.4)" }}
          />
          {t("autogen.t_plain")}
        </span>
      </div>

      {/* D3 Simulation Container */}
      <div
        ref={containerRef}
        className="peer-map-container"
        style={{
          position: "relative",
          flex: "1 1 auto",
          minHeight: 0,
          height: "100%",
        }}
      >
        {isLoading && (
          <div className="peer-map-loading">
            {t("autogen.t_loading_peer_topology")}
          </div>
        )}
        {!isLoading && isError && (
          <div className="peer-map-empty" style={{ color: "var(--danger)" }}>
            {t("autogen.t_failed_to_load_peer_topology_data")}
          </div>
        )}
        {!isLoading && !isError && peerCount === 0 && (
          <div className="peer-map-empty">
            {t("autogen.t_no_active_peer_connections_in_the_select")}
          </div>
        )}
        <svg
          ref={svgRef}
          width={dimensions.width}
          height={dimensions.height}
          className="peer-map-svg"
          onClick={() => setSelectedNode(null)}
        />

        {/* Floating Zoom Controls */}
        <div
          style={{
            position: "absolute",
            top: "1rem",
            right: "1rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.35rem",
            zIndex: 5,
          }}
        >
          <button
            className="btn btn-outline"
            style={{
              padding: "0.35rem 0.65rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "rgba(20, 20, 20, 0.85)",
              backdropFilter: "blur(4px)",
            }}
            onClick={handleZoomIn}
            title={t("autogen.t_zoom_in")}
          >
            ➕
          </button>
          <button
            className="btn btn-outline"
            style={{
              padding: "0.35rem 0.65rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "rgba(20, 20, 20, 0.85)",
              backdropFilter: "blur(4px)",
            }}
            onClick={handleZoomOut}
            title={t("autogen.t_zoom_out")}
          >
            ➖
          </button>
          <button
            className="btn btn-outline"
            style={{
              padding: "0.35rem 0.65rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "rgba(20, 20, 20, 0.85)",
              backdropFilter: "blur(4px)",
            }}
            onClick={handleResetZoom}
            title={t("autogen.t_reset_zoom_center")}
          >
            ⟲
          </button>
        </div>

        {/* Selected Node Details Flyout */}
        {selectedNode && (
          <div
            className="card"
            style={{
              position: "absolute",
              bottom: "1.25rem",
              right: "1.25rem",
              width: "300px",
              padding: "1.25rem",
              backgroundColor: "rgba(22, 22, 22, 0.95)",
              backdropFilter: "blur(8px)",
              border: "1px solid rgba(255, 255, 255, 0.12)",
              borderRadius: "8px",
              boxShadow: "0 12px 35px rgba(0, 0, 0, 0.7)",
              zIndex: 10,
            }}
            onClick={(e) => e.stopPropagation()}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "0.5rem",
              }}
            >
              <span
                className="badge"
                style={{
                  backgroundColor: NODE_COLORS[selectedNode.type] || "#666",
                  color: "#fff",
                  fontSize: "0.75rem",
                  textTransform: "uppercase",
                  borderRadius: "4px",
                }}
              >
                {selectedNode.type}
              </span>
              <button
                className="btn btn-small"
                style={{
                  border: "none",
                  background: "none",
                  fontSize: "0.85rem",
                  cursor: "pointer",
                  color: "var(--text-muted)",
                }}
                onClick={() => setSelectedNode(null)}
              >
                ✕
              </button>
            </div>

            <div
              style={{
                fontWeight: 600,
                fontSize: "0.95rem",
                wordBreak: "break-word",
                marginBottom: "0.5rem",
              }}
            >
              {selectedNode.label}
            </div>

            {selectedNode.infoHash && (
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  marginBottom: "0.75rem",
                }}
              >
                <code style={{ wordBreak: "break-all" }}>
                  {selectedNode.infoHash}
                </code>
              </div>
            )}

            {selectedNode.type === "torrent" && (
              <button
                className="btn btn-small btn-primary"
                style={{
                  width: "100%",
                  marginTop: "0.5rem",
                  borderRadius: "6px",
                }}
                onClick={() => navigate("/torrents")}
              >
                {t("autogen.t_open_in_torrents_view")}
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

export default PeerMap;
