import React from "react";
import { NavLink } from "react-router";

interface DeveloperNavProps {
  currentTab?: string;
  isSeedarr?: boolean;
}

export const DeveloperNav: React.FC<DeveloperNavProps> = ({ isSeedarr = false }) => {
  const tabs = [
    { to: "/developer/database", label: "Database Explorer", icon: "🗄️" },
    { to: "/developer/events", label: "Event Bus", icon: "📡" },
    { to: "/developer/commands", label: "Command Console", icon: "⚡" },
    { to: "/developer/network", label: "Network Wiretap", icon: "🌐" },
    { to: "/developer/webhooks", label: "Webhook Sandbox", icon: "🪝" },
    ...(isSeedarr ? [{ to: "/developer/simulation", label: "Simulation Lab", icon: "🧪" }] : []),
    { to: "/developer/config", label: "Config & Env", icon: "⚙️" },
    { to: "/developer/diagnostics", label: "Diagnostics", icon: "📊" },
    { to: "/developer/terminal", label: "Terminal CLI", icon: "💻" },
    { to: "/developer/api", label: "API Reference", icon: "📖" },
  ];

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: "4px",
        overflowX: "auto",
        padding: "6px 8px",
        backgroundColor: "var(--bg-surface, #1e293b)",
        borderRadius: "8px",
        border: "1px solid var(--border, #334155)",
        marginBottom: "16px",
        scrollbarWidth: "thin",
      }}
    >
      {tabs.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          style={({ isActive }) => ({
            display: "inline-flex",
            alignItems: "center",
            gap: "6px",
            padding: "6px 12px",
            borderRadius: "6px",
            fontSize: "0.82rem",
            fontWeight: isActive ? 600 : 500,
            color: isActive ? "#ffffff" : "var(--text-secondary, #94a3b8)",
            backgroundColor: isActive ? "var(--accent, #3b82f6)" : "transparent",
            textDecoration: "none",
            whiteSpace: "nowrap",
            transition: "all 0.15s ease",
          })}
        >
          <span>{tab.icon}</span>
          <span>{tab.label}</span>
        </NavLink>
      ))}
    </div>
  );
};
