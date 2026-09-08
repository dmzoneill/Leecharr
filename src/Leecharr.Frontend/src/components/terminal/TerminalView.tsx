import React, { useEffect, useRef, useState, useCallback } from "react";
import { useTranslation } from "../../i18n";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import "@xterm/xterm/css/xterm.css";
import { apiClient, getUrlBase } from "../../api/client";

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
  const { t } = useTranslation();
  const containerRef = useRef<HTMLDivElement>(null);
  const termRef = useRef<Terminal | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const pingIntervalRef = useRef<number | null>(null);
  const resizeTimeoutRef = useRef<number | null>(null);
  const reconnectTimeoutRef = useRef<number | null>(null);
  const copyTimeoutRef = useRef<number | null>(null);
  const reconnectAttemptRef = useRef<number>(0);
  const isExplicitExitRef = useRef<boolean>(false);
  const isUnmountedRef = useRef<boolean>(false);

  const [connected, setConnected] = useState(false);
  const [connecting, setConnecting] = useState(true);
  const [copied, setCopied] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);

  const connectWebSocket = useCallback(() => {
    if (isUnmountedRef.current || !containerRef.current) return;

    if (reconnectTimeoutRef.current) {
      window.clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = null;
    }

    if (pingIntervalRef.current) {
      window.clearInterval(pingIntervalRef.current);
      pingIntervalRef.current = null;
    }

    if (wsRef.current) {
      wsRef.current.onopen = null;
      wsRef.current.onmessage = null;
      wsRef.current.onerror = null;
      wsRef.current.onclose = null;
      wsRef.current.close();
      wsRef.current = null;
    }

    setConnecting(true);
    setConnected(false);

    // Create xterm instance matching Leecharr dark aesthetics if not already initialized
    if (!termRef.current) {
      const term = new Terminal({
        cursorBlink: true,
        cursorStyle: "block",
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

      term.onData((data) => {
        if (wsRef.current && wsRef.current.readyState === WebSocket.OPEN) {
          wsRef.current.send(JSON.stringify({ type: "input", data }));
        }
      });

      termRef.current = term;
      fitAddonRef.current = fitAddon;
    }

    const term = termRef.current;
    const fitAddon = fitAddonRef.current;

    try {
      fitAddon?.fit();
    } catch {
      // Ignored if hidden
    }

    if (autoFocus) {
      term?.focus();
    }

    // Build WebSocket URL
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    const apiKey = apiClient.getApiKey();
    const params = new URLSearchParams({
      cwd: cwd || "",
      cols: Math.max(10, term?.cols || 80).toString(),
      rows: Math.max(5, term?.rows || 24).toString(),
    });

    if (apiKey) {
      params.set("apikey", apiKey);
    }

    const wsUrl = `${protocol}//${window.location.host}${getUrlBase()}/api/v1/terminal/ws?${params.toString()}`;
    const ws = new WebSocket(wsUrl);
    wsRef.current = ws;

    ws.onopen = () => {
      if (isUnmountedRef.current) {
        ws.close();
        return;
      }
      reconnectAttemptRef.current = 0;
      isExplicitExitRef.current = false;
      setConnected(true);
      setConnecting(false);
      term?.writeln("\x1b[1;33m⚡ Connected to Leecharr Native Shell\x1b[0m");
      if (cwd) {
        term?.writeln(`\x1b[90m📂 Working directory: ${cwd}\x1b[0m\r\n`);
      }
      try {
        fitAddon?.fit();
      } catch {
        // Ignored
      }
      if (autoFocus) {
        term?.focus();
      }

      // Send initial size
      if (term) {
        ws.send(
          JSON.stringify({
            type: "resize",
            cols: term.cols,
            rows: term.rows,
          }),
        );
      }

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
          term?.write(msg.data);
        } else if (msg.type === "exit") {
          isExplicitExitRef.current = true;
          term?.writeln("\r\n\x1b[1;31m[Session terminated by host]\x1b[0m");
          setConnected(false);
          setConnecting(false);
        }
      } catch {
        term?.write(event.data);
      }
    };

    ws.onerror = () => {
      if (isUnmountedRef.current) return;
      setConnected(false);
      if (!isExplicitExitRef.current) {
        term?.writeln(
          "\r\n\x1b[1;31m⚠️ Terminal WebSocket connection error.\x1b[0m",
        );
      }
    };

    ws.onclose = () => {
      if (isUnmountedRef.current) return;
      setConnected(false);
      if (pingIntervalRef.current) {
        clearInterval(pingIntervalRef.current);
        pingIntervalRef.current = null;
      }

      if (isExplicitExitRef.current) {
        setConnecting(false);
        return;
      }

      setConnecting(true);
      const attempt = reconnectAttemptRef.current;
      const baseDelay = Math.min(30000, 1000 * Math.pow(2, attempt));
      const jitterFactor = 0.8 + Math.random() * 0.4; // ±20% jitter
      const delay = Math.round(baseDelay * jitterFactor);
      reconnectAttemptRef.current = attempt + 1;

      term?.writeln(
        `\r\n\x1b[90m⚡ Connection dropped. Reconnecting in ${(delay / 1000).toFixed(1)}s (attempt ${attempt + 1})...\x1b[0m`,
      );

      reconnectTimeoutRef.current = window.setTimeout(() => {
        connectWebSocket();
      }, delay);
    };
  }, [cwd, autoFocus]);

  const handleManualReconnect = useCallback(() => {
    isExplicitExitRef.current = false;
    reconnectAttemptRef.current = 0;
    connectWebSocket();
  }, [connectWebSocket]);

  const handleResize = useCallback(() => {
    if (resizeTimeoutRef.current) {
      window.clearTimeout(resizeTimeoutRef.current);
    }

    resizeTimeoutRef.current = window.setTimeout(() => {
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
    }, 100);
  }, []);

  useEffect(() => {
    isUnmountedRef.current = false;
    connectWebSocket();

    window.addEventListener("resize", handleResize);

    return () => {
      isUnmountedRef.current = true;
      window.removeEventListener("resize", handleResize);
      if (reconnectTimeoutRef.current) {
        window.clearTimeout(reconnectTimeoutRef.current);
        reconnectTimeoutRef.current = null;
      }
      if (resizeTimeoutRef.current) {
        window.clearTimeout(resizeTimeoutRef.current);
        resizeTimeoutRef.current = null;
      }
      if (copyTimeoutRef.current) {
        window.clearTimeout(copyTimeoutRef.current);
        copyTimeoutRef.current = null;
      }
      if (pingIntervalRef.current) {
        clearInterval(pingIntervalRef.current);
        pingIntervalRef.current = null;
      }
      if (wsRef.current) {
        wsRef.current.onopen = null;
        wsRef.current.onmessage = null;
        wsRef.current.onerror = null;
        wsRef.current.onclose = null;
        wsRef.current.close();
        wsRef.current = null;
      }
      if (termRef.current) {
        termRef.current.dispose();
        termRef.current = null;
      }
      fitAddonRef.current = null;
    };
  }, [connectWebSocket, handleResize]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      handleResize();
      termRef.current?.focus();
    }, 100);
    return () => window.clearTimeout(timer);
  }, [isFullscreen, handleResize]);

  useEffect(() => {
    if (!containerRef.current) return;
    const observer = new ResizeObserver(() => {
      handleResize();
    });
    observer.observe(containerRef.current);
    return () => observer.disconnect();
  }, [handleResize]);

  const handleCopyPath = () => {
    if (!cwd) return;
    navigator.clipboard.writeText(cwd);
    setCopied(true);
    if (copyTimeoutRef.current) {
      window.clearTimeout(copyTimeoutRef.current);
    }
    copyTimeoutRef.current = window.setTimeout(() => {
      if (!isUnmountedRef.current) {
        setCopied(false);
      }
      copyTimeoutRef.current = null;
    }, 2000);
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
                ? t("common.connected", "Connected")
                : connecting
                  ? t("common.connecting", "Connecting...")
                  : t("common.disconnected", "Disconnected")
            }
          />
          <span
            style={{ fontWeight: 600, color: "var(--text-primary, #f8f4ed)" }}
          >
            {title || t("terminal.interactiveShell", "Interactive Shell")}
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
              title={t("terminal.copyPath")}
            >
              {copied ? `✓ ${t("common.copied")}` : t("terminal.copyPath")}
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
            title={t("terminal.clearTerminal")}
          >
            {t("terminal.clear")}
          </button>

          {!connected && (
            <button
              type="button"
              onClick={handleManualReconnect}
              className="btn btn-primary"
              style={{
                padding: "0.2rem 0.5rem",
                fontSize: "0.75rem",
                borderRadius: "4px",
              }}
              title={t("terminal.reconnect")}
            >
              {t("terminal.reconnect")}
            </button>
          )}

          <button
            type="button"
            onClick={() => {
              setIsFullscreen((prev) => !prev);
            }}
            className="btn btn-outline"
            style={{
              padding: "0.2rem 0.5rem",
              fontSize: "0.75rem",
              borderRadius: "4px",
            }}
            title={
              isFullscreen ? t("terminal.restore") : t("terminal.fullscreen")
            }
          >
            {isFullscreen ? t("terminal.restore") : t("terminal.fullscreen")}
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
