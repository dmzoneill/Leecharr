import React, { useState } from "react";
import { useGeneralConfig } from "../api/hooks";
import { api } from "../api/client";
import { useToast } from "../context/ToastContext";

export function ApiDocsPage() {
  const { data: config } = useGeneralConfig();
  const toast = useToast();
  const [copiedKey, setCopiedKey] = useState(false);
  const [copyingKey, setCopyingKey] = useState(false);

  const handleCopyKey = async () => {
    try {
      setCopyingKey(true);
      let keyToCopy = config?.apiKey;
      if (!keyToCopy || keyToCopy.includes("*")) {
        const res = await api.getApiKey();
        keyToCopy = res.apiKey;
      }

      if (keyToCopy) {
        await navigator.clipboard.writeText(keyToCopy);
        setCopiedKey(true);
        setTimeout(() => setCopiedKey(false), 2000);
        toast.showToast("API key copied to clipboard", "success");
      }
    } catch {
      toast.showToast("Failed to copy API key to clipboard", "error");
    } finally {
      setCopyingKey(false);
    }
  };

  return (
    <div className="content-area" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      {/* Header Banner */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          padding: "1rem 1.25rem",
          borderRadius: "8px",
          backgroundColor: "var(--bg-secondary)",
          border: "1px solid var(--border-light)",
        }}
      >
        <div>
          <h2
            style={{
              margin: 0,
              fontSize: "1.25rem",
              color: "var(--text-primary)",
            }}
          >
            REST API & OpenAPI Explorer
          </h2>
          <p
            style={{
              margin: "0.25rem 0 0 0",
              color: "var(--text-secondary)",
              fontSize: "0.85rem",
            }}
          >
            Interactive OpenAPI v3 (Swagger) specification for Leecharr REST API v1. Test endpoints,
            inspect JSON schemas, and automate downloads.
          </p>
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {config?.apiKey && (
            <button
              className="btn btn-outline btn-small"
              onClick={handleCopyKey}
              disabled={copyingKey}
              title="Copy API Key to clipboard for Swagger Authorize header"
            >
              {copiedKey ? "✓ API Key Copied" : copyingKey ? "⏳ Copying..." : "📋 Copy API Key"}
            </button>
          )}

          <a
            href="/swagger/v1/swagger.json"
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-outline btn-small"
            title="View raw OpenAPI 3.0 specification in JSON format"
          >
            📥 OpenAPI JSON
          </a>

          <a
            href="/swagger/index.html"
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-primary btn-small"
            title="Open Swagger UI in a dedicated full browser tab"
          >
            ↗ Open Full Page
          </a>
        </div>
      </div>

      {/* Embedded Swagger UI */}
      <div
        className="card"
        style={{
          padding: 0,
          overflow: "hidden",
          borderRadius: "8px",
          border: "1px solid var(--border-light)",
          backgroundColor: "var(--bg-primary)",
          boxShadow: "0 8px 24px rgba(0, 0, 0, 0.4)",
        }}
      >
        <iframe
          src="/swagger/index.html"
          title="Leecharr REST API Swagger Documentation"
          style={{
            width: "100%",
            height: "calc(100vh - 210px)",
            minHeight: "700px",
            border: "none",
            display: "block",
            backgroundColor: "#10111a",
          }}
        />
      </div>
    </div>
  );
}

export default ApiDocsPage;
