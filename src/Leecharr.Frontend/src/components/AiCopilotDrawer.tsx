import React, { useState, useEffect, useRef } from "react";
import {
  SparklesIcon,
  BotIcon,
  UserIcon,
  SendIcon,
  ShieldCheckIcon,
  RefreshIcon,
  AlertIcon,
  CheckCircleIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  CloseIcon,
} from "./icons/AiIcons";
import {
  useAiStatus,
  useAiChat,
  useAiParseRelease,
  useAiMalwareCheck,
  useAiConfig,
} from "../api/hooks";
import type { AiParsedRelease, AiMalwareRiskAssessment } from "../api/types";
import { useTranslation } from "../i18n";

interface Message {
  id: string;
  sender: "user" | "bot";
  text: string;
  timestamp: string;
  metadata?: {
    parsedRelease?: AiParsedRelease;
    malwareRisk?: AiMalwareRiskAssessment;
    provider?: string;
  };
}

interface Position {
  x: number;
  y: number;
}

export const AiCopilotDrawer: React.FC = () => {
  const { t } = useTranslation();
  const [isOpen, setIsOpen] = useState<boolean>(() => {
    return localStorage.getItem("leecharr_copilot_open") === "true";
  });
  const [isExpanded, setIsExpanded] = useState<boolean>(false);
  const [inputMessage, setInputMessage] = useState<string>("");
  const [activeTab, setActiveTab] = useState<"chat" | "parse" | "security">(
    "chat",
  );

  // Draggable button position state
  const [buttonPos, setButtonPos] = useState<Position | null>(() => {
    const saved = localStorage.getItem("leecharr_copilot_btn_pos");
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        if (typeof parsed.x === "number" && typeof parsed.y === "number") {
          return parsed;
        }
      } catch {
        // ignore
      }
    }
    return null;
  });

  const isDraggingRef = useRef(false);
  const dragStartRef = useRef<{
    startX: number;
    startY: number;
    origX: number;
    origY: number;
  }>({
    startX: 0,
    startY: 0,
    origX: 0,
    origY: 0,
  });
  const hasMovedRef = useRef(false);

  // Parse release tab state
  const [releaseInput, setReleaseInput] = useState("");
  const [parsedResult, setParsedResult] = useState<AiParsedRelease | null>(
    null,
  );

  // Security scanner tab state
  const [securityInput, setSecurityInput] = useState("");
  const [securityResult, setSecurityResult] =
    useState<AiMalwareRiskAssessment | null>(null);

  const [messages, setMessages] = useState<Message[]>([
    {
      id: "welcome",
      sender: "bot",
      text: t("copilot.welcomeMessage"),
      timestamp: new Date().toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
      }),
    },
  ]);

  const messagesEndRef = useRef<HTMLDivElement>(null);

  const { data: aiStatus } = useAiStatus();
  const { data: aiConfig } = useAiConfig();
  const chatMutation = useAiChat();
  const parseMutation = useAiParseRelease();
  const malwareMutation = useAiMalwareCheck();

  useEffect(() => {
    localStorage.setItem("leecharr_copilot_open", isOpen ? "true" : "false");
  }, [isOpen]);

  useEffect(() => {
    if (isOpen) {
      messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
    }
  }, [messages, isOpen, activeTab]);

  const handlePointerDown = (e: React.PointerEvent<HTMLButtonElement>) => {
    const btn = e.currentTarget.getBoundingClientRect();
    isDraggingRef.current = true;
    hasMovedRef.current = false;
    dragStartRef.current = {
      startX: e.clientX,
      startY: e.clientY,
      origX: buttonPos ? buttonPos.x : btn.left,
      origY: buttonPos ? buttonPos.y : btn.top,
    };
    try {
      e.currentTarget.setPointerCapture(e.pointerId);
    } catch {
      // ignore
    }
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLButtonElement>) => {
    if (!isDraggingRef.current) return;
    const dx = e.clientX - dragStartRef.current.startX;
    const dy = e.clientY - dragStartRef.current.startY;
    if (Math.abs(dx) > 4 || Math.abs(dy) > 4) {
      hasMovedRef.current = true;
    }
    const newX = Math.min(
      Math.max(10, dragStartRef.current.origX + dx),
      window.innerWidth - 180,
    );
    const newY = Math.min(
      Math.max(10, dragStartRef.current.origY + dy),
      window.innerHeight - 50,
    );
    setButtonPos({ x: newX, y: newY });
  };

  const handlePointerUp = (e: React.PointerEvent<HTMLButtonElement>) => {
    if (!isDraggingRef.current) return;
    isDraggingRef.current = false;
    try {
      e.currentTarget.releasePointerCapture(e.pointerId);
    } catch {
      // ignore
    }
    if (!hasMovedRef.current) {
      setIsOpen(true);
    } else if (buttonPos) {
      localStorage.setItem(
        "leecharr_copilot_btn_pos",
        JSON.stringify(buttonPos),
      );
    }
  };

  const handleSendMessage = (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    const query = inputMessage.trim();
    if (!query || chatMutation.isPending) return;

    const userMsg: Message = {
      id: Date.now().toString(),
      sender: "user",
      text: query,
      timestamp: new Date().toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
      }),
    };

    setMessages((prev) => [...prev, userMsg]);
    setInputMessage("");

    chatMutation.mutate(
      { message: query },
      {
        onSuccess: (data) => {
          const botMsg: Message = {
            id: (Date.now() + 1).toString(),
            sender: "bot",
            text: data.reply || "No response received.",
            timestamp: new Date().toLocaleTimeString([], {
              hour: "2-digit",
              minute: "2-digit",
            }),
            metadata: { provider: data.provider },
          };
          setMessages((prev) => [...prev, botMsg]);
        },
        onError: (err) => {
          const errMsg: Message = {
            id: (Date.now() + 1).toString(),
            sender: "bot",
            text: `⚠️ Error: ${err.message || "Failed to reach AI engine"}`,
            timestamp: new Date().toLocaleTimeString([], {
              hour: "2-digit",
              minute: "2-digit",
            }),
          };
          setMessages((prev) => [...prev, errMsg]);
        },
      },
    );
  };

  const handleParseRelease = () => {
    if (!releaseInput.trim() || parseMutation.isPending) return;
    parseMutation.mutate(
      { releaseName: releaseInput.trim() },
      {
        onSuccess: (data) => setParsedResult(data),
      },
    );
  };

  const handleCheckSecurity = () => {
    if (!securityInput.trim() || malwareMutation.isPending) return;
    const lines = securityInput
      .split("\n")
      .map((l) => l.trim())
      .filter(Boolean);
    malwareMutation.mutate(
      {
        torrentName: lines[0] || "Sample",
        fileNames: lines,
      },
      {
        onSuccess: (data) => setSecurityResult(data),
      },
    );
  };

  const activeProvider = aiStatus?.activeProviderId || "RuleHeuristic";
  const isButtonEnabled = aiConfig?.enableCopilotButton !== false;

  return (
    <>
      {/* Draggable Discrete Floating Trigger Button */}
      {!isOpen && isButtonEnabled && (
        <button
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          style={
            buttonPos
              ? {
                  position: "fixed",
                  left: `${buttonPos.x}px`,
                  top: `${buttonPos.y}px`,
                  zIndex: 40,
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  padding: "0.55rem 0.85rem",
                  borderRadius: "9999px",
                  backgroundColor: "var(--bg-secondary, #171B35)",
                  border: "1px solid var(--border-color, #23284B)",
                  color: "var(--text-primary, #F8F4ED)",
                  boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
                  cursor: "grab",
                  userSelect: "none",
                  touchAction: "none",
                }
              : {
                  position: "fixed",
                  bottom: "50px", // 50px offset from bottom to ensure clean clearance above the bottom status bar (status bar is ~32px)
                  right: "1.25rem",
                  zIndex: 40,
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  padding: "0.55rem 0.85rem",
                  borderRadius: "9999px",
                  backgroundColor: "var(--bg-secondary, #171B35)",
                  border: "1px solid var(--border-color, #23284B)",
                  color: "var(--text-primary, #F8F4ED)",
                  boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
                  cursor: "grab",
                  userSelect: "none",
                  touchAction: "none",
                }
          }
          title={t("copilot.buttonTitle")}
        >
          <SparklesIcon
            size={15}
            style={{ color: "var(--accent-gold, #FFD166)" }}
          />
          <span style={{ fontSize: "0.75rem", fontWeight: 600 }}>
            {t("copilot.title")}
          </span>
          <span
            style={{
              fontSize: "0.65rem",
              padding: "0.1rem 0.35rem",
              borderRadius: "4px",
              backgroundColor: "#23284B",
              color: "#C7C5D3",
              fontFamily: "monospace",
            }}
          >
            {activeProvider}
          </span>
        </button>
      )}

      {/* Discrete Side Drawer / Pane */}
      {isOpen && (
        <aside
          style={{
            position: "fixed",
            bottom: 0,
            right: 0,
            zIndex: 50,
            display: "flex",
            flexDirection: "column",
            backgroundColor: "var(--bg-primary, #10111A)",
            borderLeft: "1px solid var(--border-color, #23284B)",
            borderTop: "1px solid var(--border-color, #23284B)",
            boxShadow: "-8px 0 32px rgba(0,0,0,0.6)",
            width: isExpanded ? "640px" : "420px",
            height: isExpanded ? "100vh" : "560px",
            borderTopLeftRadius: isExpanded ? 0 : "12px",
            transition: "all 0.2s ease-in-out",
          }}
          aria-label={t("copilot.title")}
        >
          {/* Header */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              padding: "0.75rem 1rem",
              backgroundColor: "var(--bg-secondary, #171B35)",
              borderBottom: "1px solid var(--border-color, #23284B)",
            }}
          >
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}
            >
              <div
                style={{
                  width: "28px",
                  height: "28px",
                  borderRadius: "6px",
                  backgroundColor: "#23284B",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  color: "#FFD166",
                }}
              >
                <SparklesIcon size={16} />
              </div>
              <div>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <span
                    style={{
                      fontSize: "0.85rem",
                      fontWeight: 700,
                      color: "var(--text-primary, #F8F4ED)",
                    }}
                  >
                    {t("copilot.title")}
                  </span>
                  <span
                    style={{
                      fontSize: "0.65rem",
                      padding: "0.1rem 0.35rem",
                      borderRadius: "4px",
                      backgroundColor: "rgba(52, 211, 153, 0.15)",
                      color: "#34d399",
                      fontFamily: "monospace",
                    }}
                  >
                    {t("dashboard.statusActive")}
                  </span>
                </div>
                <div
                  style={{
                    fontSize: "0.7rem",
                    color: "var(--text-muted, #C7C5D3)",
                  }}
                >
                  Engine:{" "}
                  <span style={{ color: "#FFD166", fontFamily: "monospace" }}>
                    {activeProvider}
                  </span>
                </div>
              </div>
            </div>

            <div
              style={{ display: "flex", alignItems: "center", gap: "0.3rem" }}
            >
              <button
                onClick={() => setIsExpanded(!isExpanded)}
                style={{
                  background: "transparent",
                  border: "none",
                  color: "#C7C5D3",
                  cursor: "pointer",
                  padding: "0.3rem",
                  borderRadius: "4px",
                }}
                title={isExpanded ? t("copilot.compact") : t("copilot.expand")}
              >
                {isExpanded ? "🗗" : "🗖"}
              </button>
              <button
                onClick={() => setIsOpen(false)}
                style={{
                  background: "transparent",
                  border: "none",
                  color: "#C7C5D3",
                  cursor: "pointer",
                  padding: "0.3rem",
                  borderRadius: "4px",
                }}
                title={t("copilot.minimize")}
              >
                <CloseIcon size={16} />
              </button>
            </div>
          </div>

          {/* Sub-tabs Navigation */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              padding: "0.4rem 0.75rem",
              backgroundColor: "rgba(23, 27, 53, 0.6)",
              borderBottom: "1px solid var(--border-color, #23284B)",
              fontSize: "0.75rem",
            }}
          >
            <button
              onClick={() => setActiveTab("chat")}
              style={{
                padding: "0.25rem 0.6rem",
                borderRadius: "5px",
                border: "none",
                backgroundColor:
                  activeTab === "chat" ? "#23284B" : "transparent",
                color: activeTab === "chat" ? "#FFD166" : "#C7C5D3",
                fontWeight: activeTab === "chat" ? 700 : 500,
                cursor: "pointer",
              }}
            >
              💬 {t("copilot.tabChat")}
            </button>
            <button
              onClick={() => setActiveTab("parse")}
              style={{
                padding: "0.25rem 0.6rem",
                borderRadius: "5px",
                border: "none",
                backgroundColor:
                  activeTab === "parse" ? "#23284B" : "transparent",
                color: activeTab === "parse" ? "#FFD166" : "#C7C5D3",
                fontWeight: activeTab === "parse" ? 700 : 500,
                cursor: "pointer",
              }}
            >
              🏷️ {t("copilot.tabParse")}
            </button>
            <button
              onClick={() => setActiveTab("security")}
              style={{
                padding: "0.25rem 0.6rem",
                borderRadius: "5px",
                border: "none",
                backgroundColor:
                  activeTab === "security" ? "#23284B" : "transparent",
                color: activeTab === "security" ? "#FFD166" : "#C7C5D3",
                fontWeight: activeTab === "security" ? 700 : 500,
                cursor: "pointer",
              }}
            >
              🛡️ {t("copilot.tabSecurity")}
            </button>
          </div>

          {/* Tab Content: Chat */}
          {activeTab === "chat" && (
            <div
              style={{
                flex: 1,
                display: "flex",
                flexDirection: "column",
                minHeight: 0,
                backgroundColor: "#10111A",
              }}
            >
              {/* Quick Actions Bar */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  padding: "0.4rem 0.75rem",
                  backgroundColor: "rgba(23, 27, 53, 0.3)",
                  borderBottom: "1px solid rgba(35, 40, 75, 0.5)",
                  overflowX: "auto",
                }}
              >
                <span
                  style={{
                    fontSize: "0.65rem",
                    textTransform: "uppercase",
                    fontWeight: 700,
                    color: "rgba(199, 197, 211, 0.6)",
                  }}
                >
                  {t("copilot.quick")}
                </span>
                <button
                  onClick={() =>
                    setInputMessage(
                      "How do I optimize my BitTorrent download speeds?",
                    )
                  }
                  style={{
                    fontSize: "0.7rem",
                    padding: "0.15rem 0.5rem",
                    borderRadius: "9999px",
                    backgroundColor: "rgba(35, 40, 75, 0.8)",
                    color: "#C7C5D3",
                    border: "none",
                    cursor: "pointer",
                    whiteSpace: "nowrap",
                  }}
                >
                  {t("copilot.speedTips")}
                </button>
                <button
                  onClick={() =>
                    setInputMessage(
                      "Explain what Endgame mode and Rarest-First piece picking do in Leecharr.",
                    )
                  }
                  style={{
                    fontSize: "0.7rem",
                    padding: "0.15rem 0.5rem",
                    borderRadius: "9999px",
                    backgroundColor: "rgba(35, 40, 75, 0.8)",
                    color: "#C7C5D3",
                    border: "none",
                    cursor: "pointer",
                    whiteSpace: "nowrap",
                  }}
                >
                  {t("copilot.piecePickers")}
                </button>
                <button
                  onClick={() =>
                    setInputMessage(
                      "How does VPN kill switch and interface binding work?",
                    )
                  }
                  style={{
                    fontSize: "0.7rem",
                    padding: "0.15rem 0.5rem",
                    borderRadius: "9999px",
                    backgroundColor: "rgba(35, 40, 75, 0.8)",
                    color: "#C7C5D3",
                    border: "none",
                    cursor: "pointer",
                    whiteSpace: "nowrap",
                  }}
                >
                  {t("copilot.vpnSecurity")}
                </button>
              </div>

              {/* Messages Container */}
              <div
                style={{
                  flex: 1,
                  padding: "0.75rem",
                  overflowY: "auto",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.6rem",
                }}
              >
                {messages.map((msg) => (
                  <div
                    key={msg.id}
                    style={{
                      display: "flex",
                      alignItems: "flex-start",
                      gap: "0.5rem",
                      flexDirection:
                        msg.sender === "user" ? "row-reverse" : "row",
                    }}
                  >
                    <div
                      style={{
                        width: "24px",
                        height: "24px",
                        borderRadius: "50%",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "center",
                        backgroundColor:
                          msg.sender === "user" ? "#FFD166" : "#23284B",
                        color: msg.sender === "user" ? "#10111A" : "#FFD166",
                        flexShrink: 0,
                      }}
                    >
                      {msg.sender === "user" ? (
                        <UserIcon size={13} />
                      ) : (
                        <BotIcon size={13} />
                      )}
                    </div>

                    <div
                      style={{
                        maxWidth: "85%",
                        padding: "0.5rem 0.75rem",
                        borderRadius: "8px",
                        fontSize: "0.75rem",
                        lineHeight: 1.4,
                        backgroundColor:
                          msg.sender === "user"
                            ? "#FFD166"
                            : "var(--bg-secondary, #171B35)",
                        color:
                          msg.sender === "user"
                            ? "#10111A"
                            : "var(--text-primary, #F8F4ED)",
                        border:
                          msg.sender === "user"
                            ? "none"
                            : "1px solid var(--border-color, #23284B)",
                        fontWeight: msg.sender === "user" ? 600 : 400,
                      }}
                    >
                      <p style={{ margin: 0, whiteSpace: "pre-wrap" }}>
                        {msg.text}
                      </p>
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "flex-end",
                          gap: "0.3rem",
                          marginTop: "0.25rem",
                          fontSize: "0.65rem",
                          opacity: 0.7,
                        }}
                      >
                        {msg.metadata?.provider && (
                          <span
                            style={{
                              fontFamily: "monospace",
                              padding: "0 0.2rem",
                              borderRadius: "3px",
                              backgroundColor: "#23284B",
                              color: "#C7C5D3",
                            }}
                          >
                            {msg.metadata.provider}
                          </span>
                        )}
                        <span>{msg.timestamp}</span>
                      </div>
                    </div>
                  </div>
                ))}

                {chatMutation.isPending && (
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                      padding: "0.5rem",
                      fontSize: "0.75rem",
                      color: "#C7C5D3",
                    }}
                  >
                    <RefreshIcon
                      size={14}
                      style={{
                        color: "#FFD166",
                        animation: "spin 1s linear infinite",
                      }}
                    />
                    <span>
                      {t("copilot.thinking", { provider: activeProvider })}
                    </span>
                  </div>
                )}
                <div ref={messagesEndRef} />
              </div>

              {/* Chat Input */}
              <form
                onSubmit={handleSendMessage}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  padding: "0.6rem 0.75rem",
                  backgroundColor: "var(--bg-secondary, #171B35)",
                  borderTop: "1px solid var(--border-color, #23284B)",
                }}
              >
                <input
                  type="text"
                  value={inputMessage}
                  onChange={(e) => setInputMessage(e.target.value)}
                  placeholder={t("copilot.inputPlaceholder")}
                  style={{
                    flex: 1,
                    backgroundColor: "#10111A",
                    border: "1px solid var(--border-color, #23284B)",
                    borderRadius: "6px",
                    padding: "0.4rem 0.6rem",
                    fontSize: "0.75rem",
                    color: "var(--text-primary, #F8F4ED)",
                    outline: "none",
                  }}
                />
                <button
                  type="submit"
                  disabled={!inputMessage.trim() || chatMutation.isPending}
                  style={{
                    padding: "0.4rem 0.6rem",
                    backgroundColor: "var(--accent-gold, #FFD166)",
                    color: "#10111A",
                    border: "none",
                    borderRadius: "6px",
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    opacity:
                      !inputMessage.trim() || chatMutation.isPending ? 0.5 : 1,
                  }}
                >
                  <SendIcon size={14} />
                </button>
              </form>
            </div>
          )}

          {/* Tab Content: Parse Release */}
          {activeTab === "parse" && (
            <div
              style={{
                flex: 1,
                padding: "0.75rem",
                overflowY: "auto",
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
                backgroundColor: "#10111A",
              }}
            >
              <div>
                <label
                  style={{
                    display: "block",
                    fontSize: "0.75rem",
                    fontWeight: 700,
                    color: "var(--text-primary, #F8F4ED)",
                    marginBottom: "0.3rem",
                  }}
                >
                  {t("copilot.rawSceneRelease")}
                </label>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <input
                    type="text"
                    value={releaseInput}
                    onChange={(e) => setReleaseInput(e.target.value)}
                    placeholder={t("copilot.rawScenePlaceholder")}
                    style={{
                      flex: 1,
                      backgroundColor: "var(--bg-secondary, #171B35)",
                      border: "1px solid var(--border-color, #23284B)",
                      borderRadius: "6px",
                      padding: "0.4rem 0.6rem",
                      fontSize: "0.75rem",
                      color: "var(--text-primary, #F8F4ED)",
                      outline: "none",
                    }}
                  />
                  <button
                    onClick={handleParseRelease}
                    disabled={!releaseInput.trim() || parseMutation.isPending}
                    style={{
                      padding: "0.4rem 0.75rem",
                      backgroundColor: "var(--accent-gold, #FFD166)",
                      color: "#10111A",
                      border: "none",
                      borderRadius: "6px",
                      fontSize: "0.75rem",
                      fontWeight: 700,
                      cursor: "pointer",
                      display: "flex",
                      alignItems: "center",
                      gap: "0.3rem",
                      opacity:
                        !releaseInput.trim() || parseMutation.isPending
                          ? 0.5
                          : 1,
                    }}
                  >
                    <span>{t("copilot.deobfuscate")}</span>
                  </button>
                </div>
              </div>

              {parsedResult && (
                <div
                  style={{
                    backgroundColor: "var(--bg-secondary, #171B35)",
                    border: "1px solid var(--border-color, #23284B)",
                    borderRadius: "8px",
                    padding: "0.75rem",
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.5rem",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      borderBottom: "1px solid var(--border-color, #23284B)",
                      paddingBottom: "0.4rem",
                    }}
                  >
                    <div>
                      <span
                        style={{
                          fontSize: "0.85rem",
                          fontWeight: 700,
                          color: "var(--text-primary, #F8F4ED)",
                        }}
                      >
                        {parsedResult.cleanTitle || "Unknown"}
                      </span>
                      {parsedResult.year && (
                        <span
                          style={{
                            fontSize: "0.75rem",
                            color: "#FFD166",
                            fontWeight: 600,
                            marginLeft: "0.3rem",
                          }}
                        >
                          ({parsedResult.year})
                        </span>
                      )}
                    </div>
                    <span
                      style={{
                        fontSize: "0.7rem",
                        fontFamily: "monospace",
                        color: "#34d399",
                        fontWeight: 700,
                      }}
                    >
                      Score: {Math.round(parsedResult.confidenceScore * 100)}%
                    </span>
                  </div>

                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns: "1fr 1fr",
                      gap: "0.4rem",
                      fontSize: "0.75rem",
                    }}
                  >
                    {parsedResult.resolution && (
                      <div
                        style={{
                          padding: "0.3rem 0.5rem",
                          borderRadius: "4px",
                          backgroundColor: "#23284B",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "0.65rem",
                            color: "#C7C5D3",
                            display: "block",
                          }}
                        >
                          Resolution
                        </span>
                        <strong>{parsedResult.resolution}</strong>
                      </div>
                    )}
                    {parsedResult.quality && (
                      <div
                        style={{
                          padding: "0.3rem 0.5rem",
                          borderRadius: "4px",
                          backgroundColor: "#23284B",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "0.65rem",
                            color: "#C7C5D3",
                            display: "block",
                          }}
                        >
                          Quality
                        </span>
                        <strong>{parsedResult.quality}</strong>
                      </div>
                    )}
                    {parsedResult.videoCodec && (
                      <div
                        style={{
                          padding: "0.3rem 0.5rem",
                          borderRadius: "4px",
                          backgroundColor: "#23284B",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "0.65rem",
                            color: "#C7C5D3",
                            display: "block",
                          }}
                        >
                          Video Codec
                        </span>
                        <strong>{parsedResult.videoCodec}</strong>
                      </div>
                    )}
                    {parsedResult.audioCodec && (
                      <div
                        style={{
                          padding: "0.3rem 0.5rem",
                          borderRadius: "4px",
                          backgroundColor: "#23284B",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "0.65rem",
                            color: "#C7C5D3",
                            display: "block",
                          }}
                        >
                          Audio
                        </span>
                        <strong>
                          {parsedResult.audioCodec} {parsedResult.audioChannels}
                        </strong>
                      </div>
                    )}
                    {parsedResult.dynamicRange && (
                      <div
                        style={{
                          padding: "0.3rem 0.5rem",
                          borderRadius: "4px",
                          backgroundColor: "#23284B",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "0.65rem",
                            color: "#C7C5D3",
                            display: "block",
                          }}
                        >
                          HDR
                        </span>
                        <strong style={{ color: "#FFD166" }}>
                          {parsedResult.dynamicRange}
                        </strong>
                      </div>
                    )}
                    {parsedResult.releaseGroup && (
                      <div
                        style={{
                          padding: "0.3rem 0.5rem",
                          borderRadius: "4px",
                          backgroundColor: "#23284B",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "0.65rem",
                            color: "#C7C5D3",
                            display: "block",
                          }}
                        >
                          Group
                        </span>
                        <strong>{parsedResult.releaseGroup}</strong>
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Tab Content: Risk Scanner */}
          {activeTab === "security" && (
            <div
              style={{
                flex: 1,
                padding: "0.75rem",
                overflowY: "auto",
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
                backgroundColor: "#10111A",
              }}
            >
              <div>
                <label
                  style={{
                    display: "block",
                    fontSize: "0.75rem",
                    fontWeight: 700,
                    color: "var(--text-primary, #F8F4ED)",
                    marginBottom: "0.3rem",
                  }}
                >
                  {t("copilot.fileListToInspect")}
                </label>
                <textarea
                  rows={4}
                  value={securityInput}
                  onChange={(e) => setSecurityInput(e.target.value)}
                  placeholder={t("copilot.fileListPlaceholder")}
                  style={{
                    width: "100%",
                    backgroundColor: "var(--bg-secondary, #171B35)",
                    border: "1px solid var(--border-color, #23284B)",
                    borderRadius: "6px",
                    padding: "0.4rem 0.6rem",
                    fontSize: "0.75rem",
                    fontFamily: "monospace",
                    color: "var(--text-primary, #F8F4ED)",
                    outline: "none",
                    boxSizing: "border-box",
                  }}
                />
                <button
                  onClick={handleCheckSecurity}
                  disabled={!securityInput.trim() || malwareMutation.isPending}
                  style={{
                    marginTop: "0.4rem",
                    width: "100%",
                    padding: "0.4rem 0.75rem",
                    backgroundColor: "var(--accent-gold, #FFD166)",
                    color: "#10111A",
                    border: "none",
                    borderRadius: "6px",
                    fontSize: "0.75rem",
                    fontWeight: 700,
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    gap: "0.3rem",
                    opacity:
                      !securityInput.trim() || malwareMutation.isPending
                        ? 0.5
                        : 1,
                  }}
                >
                  <ShieldCheckIcon size={14} />
                  <span>{t("copilot.scanTraps")}</span>
                </button>
              </div>

              {securityResult && (
                <div
                  style={{
                    backgroundColor: securityResult.isSuspicious
                      ? "rgba(225, 29, 72, 0.15)"
                      : "rgba(16, 185, 129, 0.15)",
                    border: securityResult.isSuspicious
                      ? "1px solid rgba(225, 29, 72, 0.4)"
                      : "1px solid rgba(16, 185, 129, 0.4)",
                    borderRadius: "8px",
                    padding: "0.75rem",
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.5rem",
                    fontSize: "0.75rem",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.4rem",
                      }}
                    >
                      {securityResult.isSuspicious ? (
                        <AlertIcon size={16} style={{ color: "#f87171" }} />
                      ) : (
                        <CheckCircleIcon
                          size={16}
                          style={{ color: "#34d399" }}
                        />
                      )}
                      <strong
                        style={{
                          color: securityResult.isSuspicious
                            ? "#fca5a5"
                            : "#6ee7b7",
                        }}
                      >
                        {securityResult.isSuspicious
                          ? t("copilot.threatDetected")
                          : t("copilot.cleanSafe")}
                      </strong>
                    </div>
                    <span
                      style={{
                        fontFamily: "monospace",
                        fontSize: "0.7rem",
                        fontWeight: 700,
                      }}
                    >
                      Risk: {securityResult.riskLevel} (
                      {Math.round(securityResult.riskScore * 100)}%)
                    </span>
                  </div>

                  {securityResult.threatReasons?.length > 0 && (
                    <ul
                      style={{
                        margin: 0,
                        paddingLeft: "1.2rem",
                        color: "#fecaca",
                      }}
                    >
                      {securityResult.threatReasons.map((tReason, i) => (
                        <li key={i}>{tReason}</li>
                      ))}
                    </ul>
                  )}

                  {securityResult.suspiciousFileNames?.length > 0 && (
                    <div
                      style={{
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.2rem",
                      }}
                    >
                      {securityResult.suspiciousFileNames.map((fn, i) => (
                        <span
                          key={i}
                          style={{
                            fontFamily: "monospace",
                            fontSize: "0.7rem",
                            padding: "0.2rem 0.4rem",
                            borderRadius: "3px",
                            backgroundColor: "rgba(225, 29, 72, 0.3)",
                            color: "#fecaca",
                          }}
                        >
                          {fn}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
        </aside>
      )}
    </>
  );
};
