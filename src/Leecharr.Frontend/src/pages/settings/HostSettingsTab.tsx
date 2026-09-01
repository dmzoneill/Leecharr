import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { api } from "../../api/client";
import { SslCertificateValidationResult } from "../../api/types";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function HostSettingsTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [form, setForm] = useState({
    port: 7889,
    bindAddress: "0.0.0.0",
    urlBase: "",
    autoStart: true,
    enableSsl: false,
    sslPort: 7890,
    sslCertPath: "",
    sslKeyPath: "",
    sslCertPassword: "",
    redirectHttpToHttps: false,
  });

  const [dirty, setDirty] = useState(false);
  const [testingSsl, setTestingSsl] = useState(false);
  const [sslTestResult, setSslTestResult] =
    useState<SslCertificateValidationResult | null>(null);

  useEffect(() => {
    if (config) {
      setForm({
        port: config.port ?? 7889,
        bindAddress: config.bindAddress ?? "0.0.0.0",
        urlBase: config.urlBase ?? "",
        autoStart: config.autoStart ?? true,
        enableSsl: config.enableSsl ?? false,
        sslPort: config.sslPort ?? 7890,
        sslCertPath: config.sslCertPath ?? "",
        sslKeyPath: config.sslKeyPath ?? "",
        sslCertPassword: config.sslCertPassword ?? "",
        redirectHttpToHttps: config.redirectHttpToHttps ?? false,
      });
      setDirty(false);
    }
  }, [config]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        port: form.port,
        bindAddress: form.bindAddress,
        urlBase: form.urlBase,
        autoStart: form.autoStart,
        enableSsl: form.enableSsl,
        sslPort: form.sslPort,
        sslCertPath: form.sslCertPath,
        sslKeyPath: form.sslKeyPath,
        sslCertPassword: form.sslCertPassword,
        redirectHttpToHttps: form.redirectHttpToHttps,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  const handleTestSsl = async () => {
    setTestingSsl(true);
    setSslTestResult(null);
    try {
      const res = await api.testSsl({
        enableSsl: form.enableSsl,
        sslPort: form.sslPort,
        sslCertPath: form.sslCertPath,
        sslKeyPath: form.sslKeyPath,
        sslCertPassword: form.sslCertPassword,
        bindAddress: form.bindAddress,
      });
      setSslTestResult(res);
    } catch (err) {
      setSslTestResult({
        isValid: false,
        subject: "",
        issuer: "",
        validFrom: "",
        validTo: "",
        thumbprint: "",
        hasPrivateKey: false,
        subjectAlternativeNames: [],
        handshakeSucceeded: false,
        message:
          err instanceof Error
            ? err.message
            : "Failed to execute SSL test connection",
      });
    } finally {
      setTestingSsl(false);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading host parameters...
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}>
      <SaveBar
        dirty={dirty}
        isPending={saveMutation.isPending}
        isError={saveMutation.isError}
        isSuccess={saveMutation.isSuccess}
        error={saveMutation.error as Error | null}
        onSave={handleSave}
      />

      <SectionCard
        title="Web Server Runtime & Ports"
        description="Configure Kestrel HTTP web server hosting parameters and reverse proxy routing."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="HTTP Port"
            value={form.port}
            onChange={(v) => update("port", v)}
            min={1}
            max={65535}
            hint="Port for plain HTTP Web UI & REST API traffic (default: 7889)"
          />

          <TextInput
            label="Bind Address"
            value={form.bindAddress}
            onChange={(v) => update("bindAddress", v)}
            hint="IP address/interface to bind web server (0.0.0.0 or * for all interfaces)"
          />

          <TextInput
            label="URL Base (Sub-path)"
            value={form.urlBase}
            onChange={(v) => update("urlBase", v)}
            hint="Prefix for reverse proxies (e.g. /leecharr, leave empty for root /)"
          />
        </div>

        <div
          style={{
            marginTop: "1rem",
            borderTop: "1px solid var(--border-light)",
            paddingTop: "1rem",
          }}
        >
          <Toggle
            label="Auto-Resume Torrents on Startup"
            checked={form.autoStart}
            onChange={(v) => update("autoStart", v)}
            hint="Automatically restart active torrent swarms upon Leecharr daemon initialization"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="SSL & HTTPS Encryption"
        description="Configure TLS/SSL certificate encryption, dual-listener HTTPS port, and connection verification."
      >
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          <Toggle
            label="Enable SSL (HTTPS)"
            checked={form.enableSsl}
            onChange={(v) => update("enableSsl", v)}
            hint="Activate secure HTTPS web server endpoint with TLS certificate encryption"
          />

          {form.enableSsl && (
            <>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                  gap: "1rem",
                }}
              >
                <NumberInput
                  label="SSL / HTTPS Port"
                  value={form.sslPort}
                  onChange={(v) => update("sslPort", v)}
                  min={1}
                  max={65535}
                  hint="Dedicated port for secure HTTPS traffic (default: 7890)"
                />

                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    paddingTop: "1.5rem",
                  }}
                >
                  <Toggle
                    label="Redirect HTTP to HTTPS"
                    checked={form.redirectHttpToHttps}
                    onChange={(v) => update("redirectHttpToHttps", v)}
                    hint="Automatically redirect plain HTTP requests to the HTTPS port"
                  />
                </div>
              </div>

              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                  gap: "1rem",
                }}
              >
                <TextInput
                  label="Certificate Path (.pfx, .crt, .pem)"
                  value={form.sslCertPath}
                  onChange={(v) => update("sslCertPath", v)}
                  hint="Path to PKCS#12 (.pfx/.p12) or PEM (.crt/.pem) certificate. Leave blank for auto-generated self-signed certificate."
                />

                <TextInput
                  label="Private Key Path (.key) (Optional for PEM)"
                  value={form.sslKeyPath}
                  onChange={(v) => update("sslKeyPath", v)}
                  hint="Path to separate PEM private key (.key). Leave blank if key is bundled in certificate or PFX."
                />

                <TextInput
                  label="Certificate Password (Optional for PFX)"
                  value={form.sslCertPassword}
                  onChange={(v) => update("sslCertPassword", v)}
                  type="password"
                  hint="Decryption passphrase if certificate file is password-protected"
                />
              </div>

              {/* Test Certificate Action & Live Verification Result */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "1rem",
                  flexWrap: "wrap",
                  paddingTop: "0.5rem",
                }}
              >
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={handleTestSsl}
                  disabled={testingSsl}
                  style={{ minWidth: "160px" }}
                >
                  {testingSsl
                    ? "Testing Certificate..."
                    : "🔒 Test SSL Connection"}
                </button>

                <span
                  style={{ fontSize: "0.82rem", color: "var(--text-muted)" }}
                >
                  Validates certificate cryptographic structure, private key,
                  SAN domains, and active HTTPS handshake.
                </span>
              </div>

              {sslTestResult && (
                <div
                  style={{
                    padding: "1rem 1.25rem",
                    borderRadius: "8px",
                    backgroundColor: sslTestResult.isValid
                      ? "rgba(46, 204, 113, 0.08)"
                      : "rgba(231, 76, 60, 0.08)",
                    border: sslTestResult.isValid
                      ? "1px solid rgba(46, 204, 113, 0.3)"
                      : "1px solid rgba(231, 76, 60, 0.3)",
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.5rem",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                    }}
                  >
                    <span style={{ fontSize: "1.1rem" }}>
                      {sslTestResult.isValid ? "✓" : "⚠️"}
                    </span>
                    <strong
                      style={{
                        color: sslTestResult.isValid
                          ? "var(--success, #2ecc71)"
                          : "var(--danger, #e74c3c)",
                        fontSize: "0.95rem",
                      }}
                    >
                      {sslTestResult.isValid
                        ? "SSL Certificate & Configuration Valid"
                        : "SSL Certificate Validation Issue"}
                    </strong>
                  </div>

                  <p
                    style={{
                      margin: 0,
                      fontSize: "0.86rem",
                      color: "var(--text-primary)",
                    }}
                  >
                    {sslTestResult.message}
                  </p>

                  {sslTestResult.isValid && (
                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns:
                          "repeat(auto-fit, minmax(220px, 1fr))",
                        gap: "0.75rem",
                        marginTop: "0.5rem",
                        fontSize: "0.8rem",
                        color: "var(--text-secondary)",
                        backgroundColor: "rgba(0, 0, 0, 0.2)",
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                      }}
                    >
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Subject:
                        </span>
                        <span
                          style={{
                            color: "var(--text-primary)",
                            wordBreak: "break-all",
                          }}
                        >
                          {sslTestResult.subject || "N/A"}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Issuer:
                        </span>
                        <span
                          style={{
                            color: "var(--text-primary)",
                            wordBreak: "break-all",
                          }}
                        >
                          {sslTestResult.issuer || "N/A"}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Valid Until:
                        </span>
                        <span style={{ color: "var(--accent)" }}>
                          {sslTestResult.validTo
                            ? new Date(
                                sslTestResult.validTo,
                              ).toLocaleDateString()
                            : "N/A"}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Private Key:
                        </span>
                        <span
                          style={{
                            color: sslTestResult.hasPrivateKey
                              ? "var(--success, #2ecc71)"
                              : "var(--danger)",
                          }}
                        >
                          {sslTestResult.hasPrivateKey
                            ? "Present (Verified)"
                            : "Missing"}
                        </span>
                      </div>
                      {sslTestResult.subjectAlternativeNames?.length > 0 && (
                        <div style={{ gridColumn: "1 / -1" }}>
                          <span
                            style={{
                              color: "var(--text-muted)",
                              display: "block",
                              marginBottom: "0.25rem",
                            }}
                          >
                            Subject Alternative Names (SANs):
                          </span>
                          <div
                            style={{
                              display: "flex",
                              gap: "0.4rem",
                              flexWrap: "wrap",
                            }}
                          >
                            {sslTestResult.subjectAlternativeNames.map(
                              (san) => (
                                <span
                                  key={san}
                                  style={{
                                    padding: "0.15rem 0.45rem",
                                    borderRadius: "4px",
                                    backgroundColor:
                                      "rgba(255, 209, 102, 0.12)",
                                    color: "var(--accent)",
                                    fontSize: "0.75rem",
                                  }}
                                >
                                  {san}
                                </span>
                              ),
                            )}
                          </div>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}

              <div
                style={{
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  backgroundColor: "rgba(255, 209, 102, 0.08)",
                  border: "1px solid rgba(255, 209, 102, 0.2)",
                  fontSize: "0.85rem",
                  color: "var(--text-secondary)",
                  lineHeight: "1.4",
                }}
              >
                <strong style={{ color: "var(--accent)" }}>
                  TLS Certificate Provisioning Note:
                </strong>{" "}
                When Certificate Path is left empty, Leecharr automatically
                generates, signs, and caches an internal 2048-bit RSA
                self-signed certificate in the application configuration
                directory. Web server port and SSL changes take effect upon
                restarting the application daemon or container.
              </div>
            </>
          )}
        </div>
      </SectionCard>
    </div>
  );
}

export default HostSettingsTab;
