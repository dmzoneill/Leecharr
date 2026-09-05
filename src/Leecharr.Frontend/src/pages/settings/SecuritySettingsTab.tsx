import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { api } from "../../api/client";
import { useToast } from "../../context/ToastContext";
import { useConfirm } from "../../context/ConfirmContext";
import { useEscapeKey } from "../../hooks/useEscapeKey";
import { IdentityProviderDefinition, IdentityProviderType } from "../../api/types";
import { SaveBar, SectionCard, SelectInput, TextInput, Toggle } from "./shared";

const PROVIDER_TEMPLATES: Record<string, Partial<IdentityProviderDefinition>> = {
  authentik: {
    providerId: "authentik",
    name: "Authentik",
    providerType: 0, // OIDC
    issuerUrl: "https://auth.example.com/application/o/leecharr/",
    scopes: "openid profile email groups",
    buttonText: "Sign in with Authentik",
    roleMappingRules:
      '{"Admin":"^(admin|authentik Admins|infrastructure)$","Operator":"^(operators|media-managers)$"}',
  },
  keycloak: {
    providerId: "keycloak",
    name: "Keycloak",
    providerType: 0, // OIDC
    issuerUrl: "https://keycloak.example.com/realms/master",
    scopes: "openid profile email roles",
    buttonText: "Sign in with Keycloak",
    roleMappingRules:
      '{"Admin":"^(realm-admin|leecharr-admin)$","Operator":"^(leecharr-operator)$"}',
  },
  authelia: {
    providerId: "authelia",
    name: "Authelia",
    providerType: 0, // OIDC
    issuerUrl: "https://auth.example.com",
    scopes: "openid profile email groups",
    buttonText: "Sign in with Authelia",
    roleMappingRules: '{"Admin":"^(admins|devops)$"}',
  },
  google: {
    providerId: "google",
    name: "Google",
    providerType: 2, // Social
    issuerUrl: "https://accounts.google.com",
    scopes: "openid profile email",
    buttonText: "Sign in with Google",
  },
  github: {
    providerId: "github",
    name: "GitHub",
    providerType: 2, // Social
    issuerUrl: "https://github.com",
    scopes: "read:user user:email",
    buttonText: "Sign in with GitHub",
  },
  apple: {
    providerId: "apple",
    name: "Apple",
    providerType: 2, // Social
    issuerUrl: "https://appleid.apple.com",
    scopes: "name email",
    buttonText: "Sign in with Apple",
  },
  facebook: {
    providerId: "facebook",
    name: "Facebook",
    providerType: 2, // Social
    issuerUrl: "https://www.facebook.com",
    scopes: "email public_profile",
    buttonText: "Sign in with Facebook",
  },
  saml: {
    providerId: "enterprise-saml",
    name: "Enterprise SAML 2.0",
    providerType: 1, // SAML
    metadataUrl: "https://idp.example.com/metadata.xml",
    buttonText: "Single Sign-On (SAML)",
  },
};

export function SecuritySettingsTab() {
  const navigate = useNavigate();
  const toast = useToast();
  const { showToast } = toast;
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [form, setForm] = useState({
    authenticationEnabled: false,
    apiKey: "",
    csrfProtectionEnabled: true,
    hostHeaderValidationEnabled: false,
    allowedHosts: "",
  });

  const [providers, setProviders] = useState<IdentityProviderDefinition[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(false);
  const [editingProvider, setEditingProvider] =
    useState<Partial<IdentityProviderDefinition> | null>(null);
  const confirm = useConfirm();
  useEscapeKey(() => {
    setEditingProvider(null);
    setShowSecret(false);
  }, Boolean(editingProvider));
  const [isNewProvider, setIsNewProvider] = useState(false);
  const [testResult, setTestResult] = useState<{
    success: boolean;
    message: string;
  } | null>(null);
  const [testing, setTesting] = useState(false);

  const [copied, setCopied] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [showSecret, setShowSecret] = useState(false);
  const [showApiKey, setShowApiKey] = useState(false);
  const [revealedApiKey, setRevealedApiKey] = useState<string | null>(null);
  const [loadingApiKey, setLoadingApiKey] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        authenticationEnabled: config.authenticationEnabled ?? false,
        apiKey: config.apiKey ?? "",
        csrfProtectionEnabled: config.csrfProtectionEnabled ?? true,
        hostHeaderValidationEnabled: config.hostHeaderValidationEnabled ?? false,
        allowedHosts: config.allowedHosts ?? "",
      });
      setDirty(false);
      setRevealedApiKey(null);
      setShowApiKey(false);
    }
  }, [config]);

  useEffect(() => {
    loadProviders();
  }, []);

  const loadProviders = async () => {
    try {
      setLoadingProviders(true);
      const list = await api.getIdProviders();
      setProviders(list || []);
    } catch {
      // Ignore
    } finally {
      setLoadingProviders(false);
    }
  };

  const update = <K extends keyof typeof form>(key: K, val: (typeof form)[K]) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const generateApiKey = () => {
    const bytes = new Uint8Array(16);
    window.crypto.getRandomValues(bytes);
    const key = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
    setRevealedApiKey(key);
    setShowApiKey(true);
    update("apiKey", key);
  };

  const handleToggleShowApiKey = async () => {
    if (showApiKey) {
      setShowApiKey(false);
      return;
    }

    if (revealedApiKey || !form.apiKey.includes("*")) {
      setShowApiKey(true);
      return;
    }

    try {
      setLoadingApiKey(true);
      const res = await api.getApiKey();
      setRevealedApiKey(res.apiKey);
      setShowApiKey(true);
    } catch (_err) {
      toast?.showToast("Failed to retrieve unmasked API key", "error");
    } finally {
      setLoadingApiKey(false);
    }
  };

  const handleCopyApiKey = async () => {
    if (!navigator.clipboard?.writeText) {
      toast?.showToast("Clipboard API not available in this browser context", "error");
      return;
    }

    try {
      let keyToCopy = form.apiKey;
      if (revealedApiKey && form.apiKey.includes("*")) {
        keyToCopy = revealedApiKey;
      } else if (!keyToCopy || keyToCopy.includes("*")) {
        const res = await api.getApiKey();
        keyToCopy = res.apiKey;
        setRevealedApiKey(res.apiKey);
      }

      if (!keyToCopy) {
        toast?.showToast("No API key available", "error");
        return;
      }

      await navigator.clipboard.writeText(keyToCopy);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
      toast?.showToast("API key copied to clipboard", "success");
    } catch (_err) {
      toast?.showToast("Failed to copy API key to clipboard", "error");
    }
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        authenticationEnabled: form.authenticationEnabled,
        apiKey: form.apiKey,
        csrfProtectionEnabled: form.csrfProtectionEnabled,
        hostHeaderValidationEnabled: form.hostHeaderValidationEnabled,
        allowedHosts: form.allowedHosts,
      },
      {
        onSuccess: () => setDirty(false),
      }
    );
  };

  const handleOpenAdd = (templateKey = "authentik") => {
    const template = PROVIDER_TEMPLATES[templateKey] || PROVIDER_TEMPLATES.authentik;
    setEditingProvider({
      ...template,
      isEnabled: true,
      clientId: "",
      clientSecret: "",
    });
    setIsNewProvider(true);
    setTestResult(null);
    setShowSecret(false);
  };

  const handleOpenEdit = (p: IdentityProviderDefinition) => {
    setEditingProvider({ ...p });
    setIsNewProvider(false);
    setTestResult(null);
    setShowSecret(false);
  };

  const handleSaveProvider = async () => {
    if (!editingProvider || !editingProvider.providerId || !editingProvider.name) return;

    try {
      if (isNewProvider) {
        await api.createIdProvider(editingProvider);
      } else if (editingProvider.id) {
        await api.updateIdProvider(editingProvider.id, editingProvider);
      }
      setEditingProvider(null);
      setShowSecret(false);
      await loadProviders();
      showToast("Identity provider saved successfully", "success");
    } catch (err: any) {
      showToast(err?.message || "Failed to save identity provider", "error");
    }
  };

  const handleDeleteProvider = async (id: number) => {
    const ok = await confirm({
      title: "Remove Identity Provider",
      message: "Are you sure you want to remove this identity provider?",
      danger: true,
      confirmText: "Remove",
    });
    if (!ok) return;

    try {
      await api.deleteIdProvider(id);
      await loadProviders();
      showToast("Identity provider removed", "success");
    } catch (err: any) {
      showToast(err?.message || "Failed to delete identity provider", "error");
    }
  };

  const handleTestConnection = async () => {
    if (!editingProvider) return;
    try {
      setTesting(true);
      setTestResult(null);
      const res = await api.testIdProvider(editingProvider);
      setTestResult(res);
    } catch (err: any) {
      setTestResult({
        success: false,
        message: err?.message || "Connection failed",
      });
    } finally {
      setTesting(false);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading security parameters...
      </div>
    );
  }

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={saveMutation.isPending}
        isError={saveMutation.isError}
        isSuccess={saveMutation.isSuccess}
        error={saveMutation.error as Error | null}
        onSave={handleSave}
      />

      <SectionCard
        title="Authentication & Access Gate"
        description="Configure user login protection for the Web UI and administrative controls."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Enable Web UI Authentication"
            checked={form.authenticationEnabled}
            onChange={(v) => update("authenticationEnabled", v)}
            hint="Require login before accessing Web UI"
          />

          {form.authenticationEnabled && (
            <div
              style={{
                backgroundColor: "var(--bg-primary)",
                padding: "1rem",
                borderRadius: "6px",
                border: "1px solid var(--border)",
              }}
            >
              <div style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}>
                Authentication is enabled. Local users accessing Leecharr over LAN or reverse proxy
                will authenticate using local credentials or configured SSO / Identity Providers
                below.
              </div>
            </div>
          )}
        </div>
      </SectionCard>

      <SectionCard
        title="WebUI Security & Request Origin Protection"
        description="Prevent Cross-Site Request Forgery (CSRF) and DNS rebinding attacks."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Enable Cross-Site Request Forgery (CSRF) Protection"
            checked={form.csrfProtectionEnabled}
            onChange={(v) => update("csrfProtectionEnabled", v)}
            hint="Enforces strict origin/referer checks and antiforgery token validation for all state-changing API and WebUI endpoints"
          />

          <Toggle
            label="Enable Strict Host Header Validation"
            checked={form.hostHeaderValidationEnabled}
            onChange={(v) => update("hostHeaderValidationEnabled", v)}
            hint="Prevents DNS rebinding attacks by validating the HTTP Host request header against allowed hostnames"
          />

          {form.hostHeaderValidationEnabled && (
            <TextInput
              label="Allowed Host Headers (Whitelist)"
              value={form.allowedHosts}
              onChange={(v) => update("allowedHosts", v)}
              hint="Comma-separated list of allowed hostnames/IPs (e.g. localhost, 127.0.0.1, leecharr.lan, torrents.mydomain.com)"
            />
          )}
        </div>
      </SectionCard>

      <SectionCard
        title="Identity Providers & Single Sign-On (SSO)"
        description="Integrate self-hosted IdPs (Authentik, Keycloak, Authelia), Social Logins (Google, GitHub, Apple, Facebook), or Enterprise SAML 2.0."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "0.5rem",
            }}
          >
            <div style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}>
              Configured Identity Providers ({providers.length})
            </div>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("authentik")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Authentik
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("keycloak")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Keycloak
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("authelia")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Authelia
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("google")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Google
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("github")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + GitHub
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("apple")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Apple
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("saml")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + SAML 2.0
              </button>
            </div>
          </div>

          {loadingProviders ? (
            <div style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}>
              Loading providers...
            </div>
          ) : providers.length === 0 ? (
            <div
              style={{
                backgroundColor: "var(--bg-primary)",
                padding: "1.5rem",
                borderRadius: "6px",
                border: "1px dashed var(--border)",
                textAlign: "center",
                color: "var(--text-secondary)",
                fontSize: "0.9rem",
              }}
            >
              No Identity Providers configured yet. Click one of the buttons above to add Authentik,
              Keycloak, Authelia, Google, GitHub, Apple, or SAML 2.0.
            </div>
          ) : (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
              }}
            >
              {providers.map((p) => (
                <div
                  key={p.id}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "12px 16px",
                    backgroundColor: "var(--bg-primary)",
                    borderRadius: "6px",
                    border: "1px solid var(--border)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "12px",
                    }}
                  >
                    <div
                      style={{
                        width: "10px",
                        height: "10px",
                        borderRadius: "50%",
                        backgroundColor: p.isEnabled ? "#10B981" : "#6B7280",
                      }}
                    />
                    <div>
                      <div
                        style={{
                          color: "var(--text-primary)",
                          fontWeight: 600,
                          fontSize: "0.95rem",
                        }}
                      >
                        {p.name}
                      </div>
                      <div
                        style={{
                          color: "var(--text-secondary)",
                          fontSize: "0.8rem",
                        }}
                      >
                        Type:{" "}
                        {p.providerType === 0
                          ? "OIDC"
                          : p.providerType === 1
                            ? "SAML 2.0"
                            : p.providerType === 2
                              ? "Social"
                              : "Forward-Auth"}{" "}
                        | ID: {p.providerId} {p.issuerUrl ? `| ${p.issuerUrl}` : ""}
                      </div>
                    </div>
                  </div>
                  <div style={{ display: "flex", gap: "0.5rem" }}>
                    <button
                      type="button"
                      className="btn btn-outline"
                      onClick={() => handleOpenEdit(p)}
                      style={{ fontSize: "0.8rem", padding: "4px 10px" }}
                    >
                      ✏️ Edit
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger"
                      onClick={() => handleDeleteProvider(p.id)}
                      style={{ fontSize: "0.8rem", padding: "4px 10px" }}
                    >
                      🗑️ Delete
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </SectionCard>

      <SectionCard
        title="REST API Key Security"
        description="Master API authentication token (X-Api-Key) required for external *arr connections and REST API access."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <div style={{ display: "flex", gap: "0.5rem", alignItems: "flex-end" }}>
            <div style={{ flex: 1 }}>
              <TextInput
                label="API Key (X-Api-Key)"
                type={showApiKey ? "text" : form.apiKey.includes("*") ? "text" : "password"}
                value={
                  showApiKey && revealedApiKey && form.apiKey.includes("*")
                    ? revealedApiKey
                    : form.apiKey
                }
                onChange={(v) => {
                  setRevealedApiKey(null);
                  update("apiKey", v);
                }}
                hint="Pass this key in the X-Api-Key HTTP header for programmatic REST API access"
                rightElement={
                  <button
                    type="button"
                    className="btn btn-outline"
                    onClick={handleToggleShowApiKey}
                    style={{ whiteSpace: "nowrap", height: "36px", padding: "0 0.75rem" }}
                    title={showApiKey ? "Hide API key" : "Show unmasked API key"}
                    aria-label={showApiKey ? "Hide API key" : "Show unmasked API key"}
                    disabled={loadingApiKey}
                  >
                    {loadingApiKey ? "..." : showApiKey ? "🙈 Hide" : "👁️ Show"}
                  </button>
                }
              />
            </div>
            <button
              type="button"
              className="btn btn-outline"
              onClick={handleCopyApiKey}
              style={{ marginBottom: "0.25rem", whiteSpace: "nowrap" }}
            >
              {copied ? "✓ Copied!" : "📋 Copy"}
            </button>
            <button
              type="button"
              className="btn btn-outline"
              onClick={generateApiKey}
              style={{ marginBottom: "0.25rem", whiteSpace: "nowrap" }}
            >
              🔄 Regenerate
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => navigate("/system/api")}
              style={{ marginBottom: "0.25rem", whiteSpace: "nowrap" }}
            >
              📖 API Docs (OpenAPI)
            </button>
          </div>
        </div>
      </SectionCard>

      {/* Provider Edit Modal */}
      {editingProvider && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "rgba(0, 0, 0, 0.75)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
            padding: "20px",
          }}
        >
          <div
            style={{
              width: "100%",
              maxWidth: "600px",
              maxHeight: "90vh",
              overflowY: "auto",
              backgroundColor: "#171B35",
              border: "1px solid #23284B",
              borderRadius: "12px",
              padding: "28px 24px",
              boxShadow: "0 20px 40px rgba(0, 0, 0, 0.5)",
            }}
          >
            <h2
              style={{
                color: "#F8F4ED",
                fontSize: "1.25rem",
                fontWeight: 700,
                margin: "0 0 16px 0",
              }}
            >
              {isNewProvider ? "Add Identity Provider" : `Edit ${editingProvider.name}`}
            </h2>

            <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <Toggle
                label="Enable Provider"
                checked={editingProvider.isEnabled ?? true}
                onChange={(v) => setEditingProvider((prev) => ({ ...prev, isEnabled: v }))}
                hint="Allow users to log in with this provider"
              />

              <TextInput
                label="Provider Name"
                value={editingProvider.name || ""}
                onChange={(v) => setEditingProvider((prev) => ({ ...prev, name: v }))}
                hint="Display name (e.g. Authentik, Keycloak, Google)"
              />

              <TextInput
                label="Provider Identifier"
                value={editingProvider.providerId || ""}
                onChange={(v) => setEditingProvider((prev) => ({ ...prev, providerId: v }))}
                hint="Unique URL-safe identifier (e.g. authentik, keycloak, google)"
              />

              <SelectInput
                label="Provider Type"
                value={String(editingProvider.providerType ?? 0)}
                options={[
                  { value: "0", label: "OpenID Connect (OIDC)" },
                  { value: "1", label: "Enterprise SAML 2.0" },
                  { value: "2", label: "Social OAuth 2.0" },
                  { value: "3", label: "Forward-Auth (Reverse Proxy)" },
                ]}
                onChange={(v) =>
                  setEditingProvider((prev) => ({
                    ...prev,
                    providerType: Number(v) as IdentityProviderType,
                  }))
                }
              />

              {editingProvider.providerType !== 1 && (
                <>
                  <TextInput
                    label="Issuer URL / Authority"
                    value={editingProvider.issuerUrl || ""}
                    onChange={(v) => setEditingProvider((prev) => ({ ...prev, issuerUrl: v }))}
                    hint="Base URL of IdP (e.g. https://auth.example.com/application/o/leecharr/)"
                  />

                  <TextInput
                    label="Client ID"
                    value={editingProvider.clientId || ""}
                    onChange={(v) => setEditingProvider((prev) => ({ ...prev, clientId: v }))}
                  />

                  <TextInput
                    label="Client Secret"
                    type={showSecret ? "text" : "password"}
                    value={editingProvider.clientSecret || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({
                        ...prev,
                        clientSecret: v,
                      }))
                    }
                    hint="Leave blank or masked to keep current secret"
                    rightElement={
                      <button
                        type="button"
                        className="btn btn-outline"
                        onClick={() => setShowSecret((prev) => !prev)}
                        style={{ whiteSpace: "nowrap", height: "36px", padding: "0 0.75rem" }}
                        title={showSecret ? "Hide client secret" : "Show client secret"}
                        aria-label={showSecret ? "Hide client secret" : "Show client secret"}
                      >
                        {showSecret ? "🙈 Hide" : "👁️ Show"}
                      </button>
                    }
                  />

                  <TextInput
                    label="OAuth Scopes"
                    value={editingProvider.scopes || "openid profile email"}
                    onChange={(v) => setEditingProvider((prev) => ({ ...prev, scopes: v }))}
                    hint="Space-separated list of scopes (e.g. openid profile email groups)"
                  />
                </>
              )}

              {editingProvider.providerType === 1 && (
                <TextInput
                  label="IdP Metadata URL / XML Endpoint"
                  value={editingProvider.metadataUrl || ""}
                  onChange={(v) => setEditingProvider((prev) => ({ ...prev, metadataUrl: v }))}
                  hint="SAML 2.0 IdP Federation Metadata URL"
                />
              )}

              <TextInput
                label="Role Mapping Rules (JSON)"
                value={editingProvider.roleMappingRules || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({
                    ...prev,
                    roleMappingRules: v,
                  }))
                }
                hint='Map IdP groups to Leecharr roles: {"Admin":"^(admin|devops)$","Operator":"^(operators)$"}'
              />

              <TextInput
                label="Button Text"
                value={editingProvider.buttonText || ""}
                onChange={(v) => setEditingProvider((prev) => ({ ...prev, buttonText: v }))}
                hint="Text rendered on login button"
              />

              {/* Test Result Message */}
              {testResult && (
                <div
                  style={{
                    padding: "10px 14px",
                    borderRadius: "6px",
                    fontSize: "13px",
                    backgroundColor: testResult.success
                      ? "rgba(16, 185, 129, 0.15)"
                      : "rgba(239, 68, 68, 0.15)",
                    border: `1px solid ${testResult.success ? "rgba(16, 185, 129, 0.3)" : "rgba(239, 68, 68, 0.3)"}`,
                    color: testResult.success ? "#6EE7B7" : "#FCA5A5",
                  }}
                >
                  {testResult.success ? "✓ " : "✕ "} {testResult.message}
                </div>
              )}
            </div>

            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "24px",
              }}
            >
              <button
                type="button"
                className="btn btn-outline"
                onClick={handleTestConnection}
                disabled={testing}
                style={{ fontSize: "0.85rem" }}
              >
                {testing ? "Testing..." : "🔍 Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "8px" }}>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => {
                    setEditingProvider(null);
                    setShowSecret(false);
                  }}
                >
                  Cancel
                </button>
                <button type="button" className="btn btn-primary" onClick={handleSaveProvider}>
                  Save Provider
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default SecuritySettingsTab;
