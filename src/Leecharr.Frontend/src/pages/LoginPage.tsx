import React, { useState, useEffect } from "react";
import { api } from "../api/client";
import { AuthProvider } from "../api/types";

interface LoginPageProps {
  onLoginSuccess: () => void;
}

export const LoginPage: React.FC<LoginPageProps> = ({ onLoginSuccess }) => {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(true);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [providers, setProviders] = useState<AuthProvider[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(true);

  useEffect(() => {
    loadProviders();
  }, []);

  const loadProviders = async () => {
    try {
      setLoadingProviders(true);
      const data = await api.getAuthProviders();
      setProviders(data || []);
    } catch {
      // Ignore if providers cannot be loaded
    } finally {
      setLoadingProviders(false);
    }
  };

  const handleLocalSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) {
      setError("Please enter your username and password.");
      return;
    }

    try {
      setLoading(true);
      setError(null);
      await api.login({ username: username.trim(), password, rememberMe });
      onLoginSuccess();
    } catch (err: any) {
      setError(err?.message || "Invalid username or password");
    } finally {
      setLoading(false);
    }
  };

  const getProviderIcon = (provider: AuthProvider) => {
    const id = provider.providerId.toLowerCase();
    const name = provider.name.toLowerCase();

    if (id.includes("google") || name.includes("google")) {
      return (
        <svg width="20" height="20" viewBox="0 0 24 24">
          <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" />
          <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
          <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.06H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.94l2.85-2.22.81-.63z" />
          <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52z" />
        </svg>
      );
    }
    if (id.includes("github") || name.includes("github")) {
      return (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
          <path fillRule="evenodd" clipRule="evenodd" d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.53 1.032 1.53 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z" />
        </svg>
      );
    }
    if (id.includes("apple") || name.includes("apple")) {
      return (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
          <path d="M18.71 19.5c-.83 1.24-1.71 2.45-3.05 2.47-1.34.03-1.77-.79-3.29-.79-1.53 0-2 .77-3.27.82-1.31.05-2.3-1.32-3.14-2.53C4.25 17 2.94 12.45 4.7 9.39c.87-1.52 2.43-2.48 4.12-2.51 1.28-.02 2.5.87 3.29.87.78 0 2.26-1.07 3.81-.91.65.03 2.47.26 3.64 1.98-.09.06-2.17 1.28-2.15 3.81.03 3.02 2.65 4.03 2.68 4.04-.03.07-.42 1.44-1.38 2.83M15.97 6.37c.61-.75 1.04-1.8 1.01-2.87-.96.04-2.13.65-2.77 1.4-.56.65-.99 1.7-1.01 2.77.96.08 2.16-.55 2.77-1.3z" />
        </svg>
      );
    }
    if (id.includes("facebook") || name.includes("facebook")) {
      return (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="#1877F2">
          <path d="M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z" />
        </svg>
      );
    }
    if (id.includes("authentik") || name.includes("authentik")) {
      return (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="#FD4B2D">
          <path d="M12 2L2 7v10l10 5 10-5V7L12 2zm0 2.8l7.2 3.6-7.2 3.6-7.2-3.6L12 4.8zM4 9.1l7 3.5v7.3l-7-3.5V9.1zm16 0v7.3l-7 3.5v-7.3l7-3.5z" />
        </svg>
      );
    }
    if (id.includes("keycloak") || name.includes("keycloak")) {
      return (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="#0088CE">
          <path d="M12 2a10 10 0 1010 10A10 10 0 0012 2zm1 14.93V17h-2v-2.07A4.004 4.004 0 0110 8a4 4 0 014 4 4.004 4.004 0 01-1 2.93zM12 6a2 2 0 102 2 2 2 0 00-2-2z" />
        </svg>
      );
    }
    if (id.includes("authelia") || name.includes("authelia")) {
      return (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="#1E88E5">
          <path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z" />
        </svg>
      );
    }

    // Default SSO / SAML Shield icon
    return (
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
        <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
      </svg>
    );
  };

  return (
    <div style={{
      minHeight: "100vh",
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      backgroundColor: "#10111A",
      backgroundImage: "radial-gradient(ellipse at 50% 20%, #1c2242 0%, #10111A 70%)",
      padding: "24px",
      fontFamily: "inherit"
    }}>
      <div style={{
        width: "100%",
        maxWidth: "420px",
        backgroundColor: "#171B35",
        border: "1px solid #23284B",
        borderRadius: "12px",
        padding: "36px 32px",
        boxShadow: "0 20px 40px rgba(0, 0, 0, 0.45)"
      }}>
        {/* Brand Header */}
        <div style={{ textAlign: "center", marginBottom: "28px" }}>
          <div style={{
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            width: "56px",
            height: "56px",
            borderRadius: "14px",
            backgroundColor: "rgba(255, 209, 102, 0.12)",
            color: "#FFD166",
            marginBottom: "14px",
            border: "1px solid rgba(255, 209, 102, 0.25)"
          }}>
            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="7 10 12 15 17 10" />
              <line x1="12" y1="15" x2="12" y2="3" />
            </svg>
          </div>
          <h1 style={{ color: "#F8F4ED", fontSize: "24px", fontWeight: 700, margin: 0 }}>
            Leecharr
          </h1>
          <p style={{ color: "#C7C5D3", fontSize: "14px", marginTop: "6px", marginBottom: 0 }}>
            Sign in to access your media downloader
          </p>
        </div>

        {/* Error Alert */}
        {error && (
          <div style={{
            backgroundColor: "rgba(239, 68, 68, 0.15)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            color: "#FCA5A5",
            padding: "10px 14px",
            borderRadius: "6px",
            fontSize: "13px",
            marginBottom: "20px",
            display: "flex",
            alignItems: "center",
            gap: "8px"
          }}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="12" cy="12" r="10" />
              <line x1="12" y1="8" x2="12" y2="12" />
              <line x1="12" y1="16" x2="12.01" y2="16" />
            </svg>
            <span>{error}</span>
          </div>
        )}

        {/* Local Login Form */}
        <form onSubmit={handleLocalSubmit}>
          <div style={{ marginBottom: "16px" }}>
            <label style={{ display: "block", color: "#F8F4ED", fontSize: "13px", fontWeight: 500, marginBottom: "6px" }}>
              Username
            </label>
            <input
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="admin"
              autoFocus
              style={{
                width: "100%",
                padding: "10px 12px",
                backgroundColor: "#10111A",
                border: "1px solid #2A305B",
                borderRadius: "6px",
                color: "#F8F4ED",
                fontSize: "14px",
                outline: "none",
                boxSizing: "border-box"
              }}
            />
          </div>

          <div style={{ marginBottom: "18px" }}>
            <label style={{ display: "block", color: "#F8F4ED", fontSize: "13px", fontWeight: 500, marginBottom: "6px" }}>
              Password
            </label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              style={{
                width: "100%",
                padding: "10px 12px",
                backgroundColor: "#10111A",
                border: "1px solid #2A305B",
                borderRadius: "6px",
                color: "#F8F4ED",
                fontSize: "14px",
                outline: "none",
                boxSizing: "border-box"
              }}
            />
          </div>

          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: "20px" }}>
            <label style={{ display: "flex", alignItems: "center", gap: "8px", color: "#C7C5D3", fontSize: "13px", cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={rememberMe}
                onChange={(e) => setRememberMe(e.target.checked)}
                style={{ accentColor: "#FFD166", width: "16px", height: "16px", cursor: "pointer" }}
              />
              Remember me
            </label>
          </div>

          <button
            type="submit"
            disabled={loading}
            style={{
              width: "100%",
              padding: "11px",
              backgroundColor: "#FFD166",
              color: "#10111A",
              border: "none",
              borderRadius: "6px",
              fontSize: "14px",
              fontWeight: 600,
              cursor: loading ? "not-allowed" : "pointer",
              transition: "opacity 0.2s ease",
              opacity: loading ? 0.7 : 1
            }}
          >
            {loading ? "Signing in..." : "Sign In"}
          </button>
        </form>

        {/* SSO / Identity Providers Section */}
        {providers.length > 0 && (
          <div style={{ marginTop: "24px" }}>
            <div style={{
              display: "flex",
              alignItems: "center",
              margin: "20px 0 16px",
              color: "#6B7280",
              fontSize: "12px",
              textTransform: "uppercase",
              letterSpacing: "0.05em"
            }}>
              <div style={{ flex: 1, height: "1px", backgroundColor: "#23284B" }} />
              <span style={{ padding: "0 12px", color: "#8E92B2" }}>Or sign in with</span>
              <div style={{ flex: 1, height: "1px", backgroundColor: "#23284B" }} />
            </div>

            <div style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              {providers.map((p) => (
                <a
                  key={p.id}
                  href={p.loginUrl}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    gap: "10px",
                    width: "100%",
                    padding: "10px 14px",
                    backgroundColor: "#10111A",
                    border: "1px solid #23284B",
                    borderRadius: "6px",
                    color: "#F8F4ED",
                    fontSize: "13px",
                    fontWeight: 500,
                    textDecoration: "none",
                    transition: "all 0.15s ease",
                    boxSizing: "border-box"
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.backgroundColor = "#23284B";
                    e.currentTarget.style.borderColor = "#FFD166";
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.backgroundColor = "#10111A";
                    e.currentTarget.style.borderColor = "#23284B";
                  }}
                >
                  {getProviderIcon(p)}
                  <span>{p.buttonText || `Sign in with ${p.name}`}</span>
                </a>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
