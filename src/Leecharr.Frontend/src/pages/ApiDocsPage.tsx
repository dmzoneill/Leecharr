import { useState } from "react";
import { useGeneralConfig } from "../api/hooks";
import { api, getUrlBase } from "../api/client";
import { useToast } from "../context/ToastContext";
import { useTranslation } from "../i18n";

export function ApiDocsPage() {
  const { t } = useTranslation();
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
        let copied = false;
        if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
          try {
            await navigator.clipboard.writeText(keyToCopy);
            copied = true;
          } catch {
            // Fallback below if navigator.clipboard write fails (e.g. non-secure context or permission denied)
          }
        }

        if (!copied && typeof document !== "undefined") {
          let textarea: HTMLTextAreaElement | null = null;
          try {
            textarea = document.createElement("textarea");
            textarea.value = keyToCopy;
            textarea.style.position = "fixed";
            textarea.style.left = "-9999px";
            textarea.style.top = "0";
            textarea.style.opacity = "0";
            textarea.setAttribute("readonly", "");

            document.body.appendChild(textarea);
            textarea.select();
            textarea.setSelectionRange(0, textarea.value.length);

            copied = document.execCommand("copy");
          } catch {
            copied = false;
          } finally {
            if (textarea && textarea.parentNode) {
              textarea.parentNode.removeChild(textarea);
            }
          }
        }

        if (copied) {
          setCopiedKey(true);
          setTimeout(() => setCopiedKey(false), 2000);
          toast.showToast(
            t("settings.apiKeyCopied", "API key copied to clipboard"),
            "success",
          );
        } else {
          toast.showToast(
            t("settings.failedToCopyApiKey", "Failed to copy API key to clipboard"),
            "error",
          );
        }
      }
    } catch {
      toast.showToast(
        t("settings.failedToCopyApiKey", "Failed to copy API key to clipboard"),
        "error",
      );
    } finally {
      setCopyingKey(false);
    }
  };

  return (
    <div
      className="content-area"
      style={{
        padding: "1.5rem",
        display: "flex",
        flexDirection: "column",
        gap: "1rem",
      }}
    >
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>📖</span> {t("apiDocs.title", "REST API & OpenAPI Explorer")}
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {t(
              "apiDocs.description",
              "Interactive OpenAPI v3 (Swagger) specification for Leecharr REST API v1. Test endpoints, inspect JSON schemas, and automate downloads.",
            )}
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
              title={t(
                "apiDocs.copyApiKeyTooltip",
                "Copy API Key to clipboard for Swagger Authorize header",
              )}
            >
              {copiedKey
                ? t("apiDocs.apiKeyCopied", "✓ API Key Copied")
                : copyingKey
                  ? t("apiDocs.copying", "⏳ Copying...")
                  : t("apiDocs.copyApiKey", "📋 Copy API Key")}
            </button>
          )}

          <a
            href={`${getUrlBase()}/swagger/v1/swagger.json`}
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-outline btn-small"
            title={t(
              "apiDocs.openRawJsonTooltip",
              "View raw OpenAPI 3.0 specification in JSON format",
            )}
          >
            {t("apiDocs.openApiJson", "📥 OpenAPI JSON")}
          </a>

          <a
            href={`${getUrlBase()}/swagger/index.html`}
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-primary btn-small"
            title={t(
              "apiDocs.openFullPageTooltip",
              "Open Swagger UI in a dedicated full browser tab",
            )}
          >
            {t("apiDocs.openFullPage", "↗ Open Full Page")}
          </a>
        </div>
      </div>

      {/* Embedded Swagger UI */}
      <div
        className="card"
        style={{
          padding: 0,
          borderRadius: "8px",
          border: "1px solid var(--border-light)",
          backgroundColor: "var(--bg-primary)",
          boxShadow: "0 8px 24px rgba(0, 0, 0, 0.4)",
          minHeight: "450px",
        }}
      >
        <iframe
          src={`${getUrlBase()}/swagger/index.html`}
          title={t(
            "apiDocs.swaggerDocTitle",
            "Leecharr REST API Swagger Documentation",
          )}
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
