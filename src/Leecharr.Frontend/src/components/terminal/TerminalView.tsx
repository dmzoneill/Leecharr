import React, { useEffect, useRef, useState, useCallback } from "react";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import { apiClient } from "../../api/client";

export interface TerminalViewProps {
  cwd?: string;
  title?: string;
  height?: string | number;
  autoFocus?: boolean;
}

export function TerminalView({
  cwd = "",
  title,
  height = "100%",
  autoFocus = true,
}: TerminalViewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const termRef = useRef<Terminal | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const pingIntervalRef = useRef<number | null>(null);

  const [connected, setConnected] = useState(false);
  const [connecting, setConnecting] = useState(true);
  const [copied, setCopied] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);

  const connect = useCallback(() => {
    if (!containerRef.current) return;

    // Clean up existing session if any
    if (wsRef.current) {
      wsRef.current.close();
      wsRef.current = null;
    }
    if (termRef.current) {
      termRef.current.dispose();
      termRef.current = null;
    }
    if (pingIntervalRef.current) {
      window.clearInterval(pingIntervalRef.current);
      pingIntervalRef.current = null;
    }

    setConnecting(true);
    setConnected(false);

    // Create xterm instance matching Leecharr dark aesthetics
    const term = new Terminal({
      cursorBlink: true,
      cursorStyle: "bar",
      fontSize: 13,
      lineHeight: 1.25,
      fontFamily:
        'ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace',
      theme: {
        background: "#0c0e1a",
        foreground: "#f8f4ed",
        cursor: "#ffd166",
        cursorAccent: "#0c0e1a",
        selectionBackground: "rgba(255, 209, 102, 0.35)",
        black: "#171b35",
        red: "#ef4444",
        green: "#22c55e",
        yellow: "#ffd166",
        blue: "#38bdf8",
        magenta: "#c084fc",
        cyan: "#06b6d4",
        white: "#f8f4ed",
        brightBlack: "#4b5563",
        brightRed: "#f87171",
        brightGreen: "#4ade80",
        brightYellow: "#fde047",
        brightBlue: "#60a5fa",
        brightMagenta: "#d8b4fe",
        brightCyan: "#22d3ee",
        brightWhite: "#ffffff",
      },
      convertEol: true,
      scrollback: 5000,
    });

    const fitAddon = new FitAddon();
    term.loadAddon(fitAddon);

    term.open(containerRef.current);
    try {
      fitAddon.fit();
    } catch {
      // Ignored if hidden
    }

    termRef.current = term;
    fitAddonRef.current = fitAddon;

    if (autoFocus) {
      term.focus();
    }

    // Build WebSocket URL
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    const apiKey = apiClient.getApiKey();
    const params = new URLSearchParams({
      cwd: cwd || "",
      cols: Math.max(10, term.cols || 80).toString(),
      rows: Math.max(5, term.rows || 24).toString(),
    });

    if (apiKey) {
      params.set("apikey", apiKey);
    }

    const urlBase =
      typeof window !== "undefined" && (window as any).Leecharr?.urlBase
        ? (window as any).Leecharr.urlBase.replace(/\/+$/, "")
        : "";

    const wsUrl = `${protocol}//${window.location.host}${urlBase}/api/v1/terminal/ws?${params.toString()}`;
    const ws = new WebSocket(wsUrl);
    wsRef.current = ws;

    ws.onopen = () => {
      setConnected(true);
      setConnecting(false);
      term.writeln("\x1b[1;33m⚡ Connected to Leecharr Native Shell\x1b[0m");
      if (cwd) {
        term.writeln(`\x1b[90m📂 Working directory: ${cwd}\x1b[0m\r\n`);
      }
      fitAddon.fit();

      // Send initial size
      ws.send(
        JSON.stringify({
          type: "resize",
          cols: term.cols,
          rows: term.rows,
        }),
      );

      // Start ping heartbeat
      pingIntervalRef.current = window.setInterval(() => {
        if (ws.readyState === WebSocket.OPEN) {
          ws.send(JSON.stringify({ type: "ping" }));
        }
      }, 25000);
    };

    ws.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        if (msg.type === "output" && msg.data) {
          term.write(msg.data);
        } else if (msg.type === "exit") {
          term.writeln("\r\n\x1b[1;31m[Session terminated by host]\x1b[0m");
          setConnected(false);
        }
      } catch {
        term.write(event.data);
      }
    };

    ws.onerror = () => {
      setConnected(false);
      setConnecting(false);
      term.writeln(
        "\r\n\x1b[1;31m⚠️ Terminal WebSocket connection error.\x1b[0m",
      );
    };

    ws.onclose = () => {
      setConnected(false);
      setConnecting(false);
      if (pingIntervalRef.current) {
        clearInterval(pingIntervalRef.current);
        pingIntervalRef.current = null;
      }
    };

    // Forward terminal input to backend
    term.onData((data) => {
      if (ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ type: "input", data }));
      }
    });
  }, [cwd, autoFocus]);

  useEffect(() => {
    connect();

    const handleResize = () => {
      if (fitAddonRef.current && termRef.current && wsRef.current) {
        try {
          fitAddonRef.current.fit();
          if (wsRef.current.readyState === WebSocket.OPEN) {
            wsRef.current.send(
              JSON.stringify({
                type: "resize",
                cols: termRef.current.cols,
                rows: termRef.current.rows,
              }),
            );
          }
        } catch {
          // Ignored
        }
      }
    };

    window.addEventListener("resize", handleResize);

    return () => {
      window.removeEventListener("resize", handleResize);
      if (pingIntervalRef.current) {
        clearInterval(pingIntervalRef.current);
      }
      if (wsRef.current) {
        wsRef.current.close();
      }
      if (termRef.current) {
        termRef.current.dispose();
      }
    };
  }, [connect]);

  const handleCopyPath = () => {
    if (!cwd) return;
    navigator.clipboard.writeText(cwd);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleClear = () => {
    if (termRef.current) {
      termRef.current.clear();
      termRef.current.focus();
    }
  };

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: isFullscreen ? "100vh" : height,
        width: isFullscreen ? "100vw" : "100%",
        position: isFullscreen ? "fixed" : "relative",
        top: isFullscreen ? 0 : undefined,
        left: isFullscreen ? 0 : undefined,
        zIndex: isFullscreen ? 99999 : undefined,
        backgroundColor: "#0c0e1a",
        border: isFullscreen
          ? "none"
          : "1px solid var(--border-light, #1c203b)",
        borderRadius: isFullscreen ? 0 : "8px",
        overflow: "hidden",
        boxShadow: isFullscreen ? "none" : "0 4px 14px rgba(0, 0, 0, 0.35)",
      }}
    >
      {/* Terminal Toolbar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          padding: "0.4rem 0.75rem",
          backgroundColor: "#131627",
          borderBottom: "1px solid var(--border-light, #1c203b)",
          fontSize: "0.8rem",
          flexShrink: 0,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <span
            style={{
              width: "8px",
              height: "8px",
              borderRadius: "50%",
              backgroundColor: connected
                ? "#22c55e"
                : connecting
                  ? "#ffd166"
                  : "#ef4444",
            }}
            title={
              connected
                ? "Connected"
                : connecting
                  ? "Connecting..."
                  : "Disconnected"
            }
          />
          <span
            style={{ fontWeight: 600, color: "var(--text-primary, #f8f4ed)" }}
          >
            {title || "Interactive Shell"}
          </span>

          {cwd && (
            <span
              style={{
                fontFamily: "monospace",
                fontSize: "0.75rem",
                backgroundColor: "rgba(255, 255, 255, 0.06)",
                padding: "0.15rem 0.45rem",
                borderRadius: "4px",
                color: "var(--accent, #ffd166)",
                maxWidth: "350px",
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
              }}
              title={cwd}
            >
              {cwd}
            </span>
          )}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
          {cwd && (
            <button
              type="button"
              onClick={handleCopyPath}
              className="btn btn-outline"
              style={{
                padding: "0.2rem 0.5rem",
                fontSize: "0.75rem",
                borderRadius: "4px",
              }}
              title="Copy Directory Path to Clipboard"
            >
              {copied ? "✓ Copied" : "Copy Path"}
            </button>
          )}

          <button
            type="button"
            onClick={handleClear}
            className="btn btn-outline"
            style={{
              padding: "0.2rem 0.5rem",
              fontSize: "0.75rem",
              borderRadius: "4px",
            }}
            title="Clear Terminal Screen"
          >
            Clear
          </button>

          {!connected && (
            <button
              type="button"
              onClick={connect}
              className="btn btn-primary"
              style={{
                padding: "0.2rem 0.5rem",
                fontSize: "0.75rem",
                borderRadius: "4px",
              }}
              title="Reconnect Session"
            >
              Reconnect
            </button>
          )}

          <button
            type="button"
            onClick={() => {
              setIsFullscreen((prev) => !prev);
              setTimeout(() => fitAddonRef.current?.fit(), 100);
            }}
            className="btn btn-outline"
            style={{
              padding: "0.2rem 0.5rem",
              fontSize: "0.75rem",
              borderRadius: "4px",
            }}
            title={isFullscreen ? "Exit Fullscreen" : "Fullscreen Mode"}
          >
            {isFullscreen ? "🗗 Restore" : "🗖 Fullscreen"}
          </button>
        </div>
      </div>

      {/* Terminal Canvas Container */}
      <div
        ref={containerRef}
        style={{
          flex: "1 1 auto",
          minHeight: 0,
          padding: "0.5rem",
          overflow: "hidden",
          backgroundColor: "#0c0e1a",
        }}
        onClick={() => termRef.current?.focus()}
      />
    </div>
  );
}

export default TerminalView;
