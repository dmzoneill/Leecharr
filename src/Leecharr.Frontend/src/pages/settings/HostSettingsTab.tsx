import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { api } from "../../api/client";
import { SslCertificateValidationResult } from "../../api/types";
import { SaveBar, SectionCard, NumberInput, TextInput, Toggle } from "./shared";

export function HostSettingsTab() {
  const { t } = useTranslation();

  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [form, setForm] = useState({
    port: 7889,
    bindAddress: t("settingsTabs.batch2.defaultIp"),
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
        bindAddress: config.bindAddress ?? t("settingsTabs.batch2.defaultIp"),
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
            : t("settingsTabs.host.ssl.test.failedExecution"),
      });
    } finally {
      setTestingSsl(false);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.host.loading")}
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
        title={t("settingsTabs.host.webServer.title")}
        description={t("settingsTabs.host.webServer.description")}
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label={t("settingsTabs.host.webServer.port.label")}
            value={form.port}
            onChange={(v) => update("port", v)}
            min={1}
            max={65535}
            hint={t("settingsTabs.host.webServer.port.hint")}
          />

          <TextInput
            label={t("settingsTabs.host.webServer.bindAddress.label")}
            value={form.bindAddress}
            onChange={(v) => update("bindAddress", v)}
            hint={t("settingsTabs.host.webServer.bindAddress.hint")}
          />

          <TextInput
            label={t("settingsTabs.host.webServer.urlBase.label")}
            value={form.urlBase}
            onChange={(v) => update("urlBase", v)}
            hint={t("settingsTabs.host.webServer.urlBase.hint")}
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
            label={t("settingsTabs.host.webServer.autoStart.label")}
            checked={form.autoStart}
            onChange={(v) => update("autoStart", v)}
            hint={t("settingsTabs.host.webServer.autoStart.hint")}
          />
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.host.ssl.title")}
        description={t("settingsTabs.host.ssl.description")}
      >
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          <Toggle
            label={t("settingsTabs.host.ssl.enable.label")}
            checked={form.enableSsl}
            onChange={(v) => update("enableSsl", v)}
            hint={t("settingsTabs.host.ssl.enable.hint")}
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
                  label={t("settingsTabs.host.ssl.port.label")}
                  value={form.sslPort}
                  onChange={(v) => update("sslPort", v)}
                  min={1}
                  max={65535}
                  hint={t("settingsTabs.host.ssl.port.hint")}
                />

                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    paddingTop: "1.5rem",
                  }}
                >
                  <Toggle
                    label={t("settingsTabs.host.ssl.redirect.label")}
                    checked={form.redirectHttpToHttps}
                    onChange={(v) => update("redirectHttpToHttps", v)}
                    hint={t("settingsTabs.host.ssl.redirect.hint")}
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
                  label={t("settingsTabs.host.ssl.certPath.label")}
                  value={form.sslCertPath}
                  onChange={(v) => update("sslCertPath", v)}
                  hint={t("settingsTabs.host.ssl.certPath.hint")}
                />

                <TextInput
                  label={t("settingsTabs.host.ssl.keyPath.label")}
                  value={form.sslKeyPath}
                  onChange={(v) => update("sslKeyPath", v)}
                  hint={t("settingsTabs.host.ssl.keyPath.hint")}
                />

                <TextInput
                  label={t("settingsTabs.host.ssl.certPassword.label")}
                  value={form.sslCertPassword}
                  onChange={(v) => update("sslCertPassword", v)}
                  type="password"
                  hint={t("settingsTabs.host.ssl.certPassword.hint")}
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
                    ? t("settingsTabs.host.ssl.test.testing")
                    : t("settingsTabs.host.ssl.test.button")}
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
                        ? t("settingsTabs.host.ssl.test.validTitle")
                        : t("settingsTabs.host.ssl.test.invalidTitle")}
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
                          {t("settingsTabs.host.ssl.test.fields.subject")}
                        </span>
                        <span
                          style={{
                            color: "var(--text-primary)",
                            wordBreak: "break-all",
                          }}
                        >
                          {sslTestResult.subject ||
                            t("settingsTabs.host.ssl.test.fields.na")}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          {t("settingsTabs.host.ssl.test.fields.issuer")}
                        </span>
                        <span
                          style={{
                            color: "var(--text-primary)",
                            wordBreak: "break-all",
                          }}
                        >
                          {sslTestResult.issuer ||
                            t("settingsTabs.host.ssl.test.fields.na")}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          {t("settingsTabs.host.ssl.test.fields.validUntil")}
                        </span>
                        <span style={{ color: "var(--accent)" }}>
                          {sslTestResult.validTo
                            ? new Date(
                                sslTestResult.validTo,
                              ).toLocaleDateString()
                            : t("settingsTabs.host.ssl.test.fields.na")}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          {t("settingsTabs.host.ssl.test.fields.privateKey")}
                        </span>
                        <span
                          style={{
                            color: sslTestResult.hasPrivateKey
                              ? "var(--success, #2ecc71)"
                              : "var(--danger)",
                          }}
                        >
                          {sslTestResult.hasPrivateKey
                            ? t("settingsTabs.host.ssl.test.fields.keyPresent")
                            : t("settingsTabs.host.ssl.test.fields.keyMissing")}
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
                            {t("settingsTabs.host.ssl.test.fields.sans")}
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
                  {t("settingsTabs.host.ssl.note.title")}
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
