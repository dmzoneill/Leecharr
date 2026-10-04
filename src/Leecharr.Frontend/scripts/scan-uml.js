#!/usr/bin/env node
// Copyright (c) FeedItOut. All rights reserved.
/**
 * Frontend Code Scanner for Mermaid UML Architecture Generation.
 * Scans React components, context providers, and custom hooks to generate Mermaid graph TD markup.
 */

const fs = require("fs");
const path = require("path");

const srcDir = path.resolve(__dirname, "../src");
const outputFile = path.resolve(__dirname, "../src/assets/frontend-uml.mermaid");

function scanDirectory(dir, fileList = []) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name !== "node_modules" && entry.name !== "dist" && entry.name !== "coverage") {
        scanDirectory(fullPath, fileList);
      }
    } else if (entry.isFile() && (entry.name.endsWith(".tsx") || entry.name.endsWith(".ts"))) {
      if (!entry.name.endsWith(".test.ts") && !entry.name.endsWith(".test.tsx")) {
        fileList.push(fullPath);
      }
    }
  }
  return fileList;
}

function parseImportsAndExports(files) {
  const components = [];
  const edges = [];
  const registeredNodes = new Set();

  for (const file of files) {
    const content = fs.readFileSync(file, "utf8");
    const baseName = path.basename(file, path.extname(file));
    const relPath = path.relative(srcDir, file).replace(/\\/g, "/");

    // Detect role
    let role = "Component";
    if (relPath.startsWith("context/")) role = "Context";
    else if (relPath.startsWith("hooks/")) role = "Hook";
    else if (relPath.startsWith("pages/")) role = "Page";
    else if (relPath.startsWith("api/")) role = "Api";

    if (!registeredNodes.has(baseName)) {
      registeredNodes.add(baseName);
      components.push({ id: baseName, role, path: relPath });
    }

    // Parse import statements referencing local files
    const importRegex = /import\s+.*?from\s+["'](\..*?)["']/g;
    let match;
    while ((match = importRegex.exec(content)) !== null) {
      const importPath = match[1];
      const targetBase = path.basename(importPath, path.extname(importPath));
      if (targetBase && targetBase !== "." && targetBase !== "..") {
        edges.push({ from: baseName, to: targetBase });
      }
    }
  }

  return { components, edges };
}

function generateMermaid(data) {
  const lines = ["graph TD", "    %% Auto-generated Frontend Component & Context UML Topology"];

  const subgraphs = {
    Context: [],
    Page: [],
    Component: [],
    Hook: [],
    Api: []
  };

  for (const comp of data.components) {
    if (subgraphs[comp.role]) {
      subgraphs[comp.role].push(comp);
    }
  }

  for (const [role, items] of Object.entries(subgraphs)) {
    if (items.length > 0) {
      lines.push(`    subgraph ${role}s`);
      for (const item of items.slice(0, 15)) {
        lines.push(`        ${item.id}["${item.id}"]`);
      }
      lines.push("    end");
    }
  }

  const validIds = new Set(data.components.map(c => c.id));
  const seenEdges = new Set();

  for (const edge of data.edges) {
    if (validIds.has(edge.from) && validIds.has(edge.to) && edge.from !== edge.to) {
      const key = `${edge.from}->${edge.to}`;
      if (!seenEdges.has(key)) {
        seenEdges.add(key);
        lines.push(`    ${edge.from} --> ${edge.to}`);
      }
    }
  }

  return lines.join("\n");
}

function main() {
  const files = scanDirectory(srcDir);
  const data = parseImportsAndExports(files);
  const mermaid = generateMermaid(data);

  const assetsDir = path.dirname(outputFile);
  if (!fs.existsSync(assetsDir)) {
    fs.mkdirSync(assetsDir, { recursive: true });
  }

  fs.writeFileSync(outputFile, mermaid, "utf8");
  console.log(`Scanned ${files.length} frontend source files.`);
  console.log(`Generated Mermaid UML markup with ${data.components.length} nodes and written to ${outputFile}`);
}

main();
