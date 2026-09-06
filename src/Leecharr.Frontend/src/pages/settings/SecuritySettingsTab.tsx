import { useTranslation } from "../../i18n";
import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { api } from "../../api/client";
import { useToast } from "../../context/ToastContext";
import { useConfirm } from "../../context/ConfirmContext";
import { useEscapeKey } from "../../hooks/useEscapeKey";
import {
  IdentityProviderDefinition,
  IdentityProviderType,
} from "../../api/types";
import { SaveBar, SectionCard, SelectInput, TextInput, Toggle } from "./shared";

const PROVIDER_TEMPLATES: Record<
  string,
  Partial<IdentityProviderDefinition>
> = {
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
    // @ts-ignore
    name: t("settingsTabs.batch2.enterpriseSaml20"),
    providerType: 1, // SAML
    metadataUrl: "https://idp.example.com/metadata.xml",
    buttonText: "Single Sign-On (SAML)",
  },
};

export function SecuritySettingsTab() {
  const { t } = useTranslation();

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
        hostHeaderValidationEnabled:
          config.hostHeaderValidationEnabled ?? false,
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

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const generateApiKey = () => {
    const bytes = new Uint8Array(16);
    window.crypto.getRandomValues(bytes);
    const key = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join(
      "",
    );
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
      toast?.showToast(
        t("settingsTabs.batch2.failedToRetrieveUnmaskedApiKey"),
        "error",
      );
    } finally {
      setLoadingApiKey(false);
    }
  };

  const handleCopyApiKey = async () => {
    if (!navigator.clipboard?.writeText) {
      toast?.showToast(
        t("settingsTabs.batch2.clipboardApiNotAvailable"),
        "error",
      );
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
        toast?.showToast(t("settingsTabs.batch2.noApiKeyAvailable"), "error");
        return;
      }

      await navigator.clipboard.writeText(keyToCopy);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
      toast?.showToast(
        t("settingsTabs.batch2.apiKeyCopiedToClipboard"),
        "success",
      );
    } catch (_err) {
      toast?.showToast(t("settingsTabs.batch2.failedToCopyApiKey"), "error");
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
      },
    );
  };

  const handleOpenAdd = (templateKey = "authentik") => {
    const template =
      PROVIDER_TEMPLATES[templateKey] || PROVIDER_TEMPLATES.authentik;
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
    if (
      !editingProvider ||
      !editingProvider.providerId ||
      !editingProvider.name
    )
      return;

    try {
      if (isNewProvider) {
        await api.createIdProvider(editingProvider);
      } else if (editingProvider.id) {
        await api.updateIdProvider(editingProvider.id, editingProvider);
      }
      setEditingProvider(null);
      setShowSecret(false);
      await loadProviders();
      showToast(
        t("settingsTabs.batch2.identityProviderSavedSuccessfully"),
        "success",
      );
    } catch (err: any) {
      showToast(
        err?.message || t("settingsTabs.batch2.failedToSaveIdentityProvider"),
        "error",
      );
    }
  };

  const handleDeleteProvider = async (id: number) => {
    const ok = await confirm({
      title: t("settingsTabs.batch2.removeIdentityProvider"),
      message: t("settingsTabs.batch2.areYouSureRemoveIdp"),
      danger: true,
      confirmText: t("settingsTabs.batch2.remove"),
    });
    if (!ok) return;

    try {
      await api.deleteIdProvider(id);
      await loadProviders();
      showToast(t("settingsTabs.batch2.identityProviderRemoved"), "success");
    } catch (err: any) {
      showToast(
        err?.message || t("settingsTabs.batch2.failedToDeleteIdentityProvider"),
        "error",
      );
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
        message:
          err?.message || t("settingsTabs.notifications.connectionFailed"),
      });
    } finally {
      setTesting(false);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        {t("settingsTabs.batch2.loadingSecurityParameters")}
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
        title={t("settingsTabs.batch2.authenticationAndAccessGate")}
        description={t("settingsTabs.batch2.configureUserLoginProtection")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settingsTabs.batch2.enableWebUiAuthentication")}
            checked={form.authenticationEnabled}
            onChange={(v) => update("authenticationEnabled", v)}
            hint={t("settingsTabs.batch2.requireLoginBeforeAccessingWebUi")}
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
              <div
                style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}
              >
                Authentication is enabled. Local users accessing Leecharr over
                LAN or reverse proxy will authenticate using local credentials
                or configured SSO / Identity Providers below.
              </div>
            </div>
          )}
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.batch2.webUiSecurityAndRequestOriginProtection")}
        description={t("settingsTabs.batch2.preventCsrfAndDnsRebindingAttacks")}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label={t("settingsTabs.batch2.enableCsrfProtection")}
            checked={form.csrfProtectionEnabled}
            onChange={(v) => update("csrfProtectionEnabled", v)}
            hint={t("settingsTabs.batch2.enforcesStrictOriginRefererChecks")}
          />

          <Toggle
            label={t("settingsTabs.batch2.enableStrictHostHeaderValidation")}
            checked={form.hostHeaderValidationEnabled}
            onChange={(v) => update("hostHeaderValidationEnabled", v)}
            hint={t("settingsTabs.batch2.preventsDnsRebindingAttacks")}
          />

          {form.hostHeaderValidationEnabled && (
            <TextInput
              label={t("settingsTabs.batch2.allowedHostHeadersWhitelist")}
              value={form.allowedHosts}
              onChange={(v) => update("allowedHosts", v)}
              hint={t(
                "settingsTabs.batch2.commaSeparatedListOfAllowedHostnames",
              )}
            />
          )}
        </div>
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.batch2.identityProvidersAndSso")}
        description={t("settingsTabs.batch2.integrateSelfHostedIdps")}
      >
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "0.5rem",
            }}
          >
            <div
              style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}
            >
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
            <div
              style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}
            >
              {t("settingsTabs.batch2.loadingProviders")}
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
              No Identity Providers configured yet. Click one of the buttons
              above to add Authentik, Keycloak, Authelia, Google, GitHub, Apple,
              or SAML 2.0.
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
                          ? t("settingsTabs.batch2.oidc")
                          : p.providerType === 1
                            ? t("settingsTabs.batch2.saml20")
                            : p.providerType === 2
                              ? t("settingsTabs.batch2.social")
                              : t("settingsTabs.batch2.forwardAuth")}{" "}
                        | ID: {p.providerId}{" "}
                        {p.issuerUrl ? `| ${p.issuerUrl}` : ""}
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
        title={t("settingsTabs.batch2.restApiKeySecurity")}
        description={t(
          "settingsTabs.batch2.masterApiAuthenticationTokenRequired",
        )}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <div
            style={{ display: "flex", gap: "0.5rem", alignItems: "flex-end" }}
          >
            <div style={{ flex: 1 }}>
              <TextInput
                label={t("settingsTabs.batch2.apiKey")}
                type={
                  showApiKey
                    ? "text"
                    : form.apiKey.includes("*")
                      ? "text"
                      : "password"
                }
                value={
                  showApiKey && revealedApiKey && form.apiKey.includes("*")
                    ? revealedApiKey
                    : form.apiKey
                }
                onChange={(v) => {
                  setRevealedApiKey(null);
                  update("apiKey", v);
                }}
                hint={t("settingsTabs.batch2.passThisKeyInXApiKeyHttpHeader")}
                rightElement={
                  <button
                    type="button"
                    className="btn btn-outline"
                    onClick={handleToggleShowApiKey}
                    style={{
                      whiteSpace: "nowrap",
                      height: "36px",
                      padding: "0 0.75rem",
                    }}
                    title={
                      showApiKey
                        ? t("settingsTabs.batch2.hideApiKey")
                        : t("settingsTabs.batch2.showUnmaskedApiKey")
                    }
                    aria-label={
                      showApiKey
                        ? t("settingsTabs.batch2.hideApiKey")
                        : t("settingsTabs.batch2.showUnmaskedApiKey")
                    }
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
              {copied
                ? t("settingsTabs.batch2.copied")
                : t("settingsTabs.batch2.copy")}
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
              {isNewProvider
                ? t("settingsTabs.batch2.addIdentityProvider")
                : `Edit ${editingProvider.name}`}
            </h2>

            <div
              style={{ display: "flex", flexDirection: "column", gap: "14px" }}
            >
              <Toggle
                label={t("settingsTabs.batch2.enableProvider")}
                checked={editingProvider.isEnabled ?? true}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, isEnabled: v }))
                }
                hint={t(
                  "settingsTabs.batch2.allowUsersToLogInWithThisProvider",
                )}
              />

              <TextInput
                label={t("settingsTabs.batch2.providerName")}
                value={editingProvider.name || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, name: v }))
                }
                hint={t("settingsTabs.batch2.displayNameExample")}
              />

              <TextInput
                label={t("settingsTabs.batch2.providerIdentifier")}
                value={editingProvider.providerId || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, providerId: v }))
                }
                hint={t("settingsTabs.batch2.uniqueUrlSafeIdentifier")}
              />

              <SelectInput
                label={t("settingsTabs.batch2.providerType")}
                value={String(editingProvider.providerType ?? 0)}
                options={[
                  {
                    value: "0",
                    label: t("settingsTabs.batch2.openIdConnectOidc"),
                  },
                  {
                    value: "1",
                    label: t("settingsTabs.batch2.enterpriseSaml20"),
                  },
                  { value: "2", label: t("settingsTabs.batch2.socialOauth20") },
                  {
                    value: "3",
                    label: t("settingsTabs.batch2.forwardAuthReverseProxy"),
                  },
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
                    label={t("settingsTabs.batch2.issuerUrlAuthority")}
                    value={editingProvider.issuerUrl || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({ ...prev, issuerUrl: v }))
                    }
                    hint={t("settingsTabs.batch2.baseUrlOfIdp")}
                  />

                  <TextInput
                    label={t("settingsTabs.batch2.clientId")}
                    value={editingProvider.clientId || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({ ...prev, clientId: v }))
                    }
                  />

                  <TextInput
                    label={t("settingsTabs.batch2.clientSecret")}
                    type={showSecret ? "text" : "password"}
                    value={editingProvider.clientSecret || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({
                        ...prev,
                        clientSecret: v,
                      }))
                    }
                    hint={t(
                      "settingsTabs.batch2.leaveBlankOrMaskedToKeepCurrentSecret",
                    )}
                    rightElement={
                      <button
                        type="button"
                        className="btn btn-outline"
                        onClick={() => setShowSecret((prev) => !prev)}
                        style={{
                          whiteSpace: "nowrap",
                          height: "36px",
                          padding: "0 0.75rem",
                        }}
                        title={
                          showSecret
                            ? "Hide client secret"
                            : "Show client secret"
                        }
                        aria-label={
                          showSecret
                            ? "Hide client secret"
                            : "Show client secret"
                        }
                      >
                        {showSecret ? "🙈 Hide" : "👁️ Show"}
                      </button>
                    }
                  />

                  <TextInput
                    label={t("settings.oAuthScopes")}
                    value={editingProvider.scopes || "openid profile email"}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({ ...prev, scopes: v }))
                    }
                    hint="Space-separated list of scopes (e.g. openid profile email groups)"
                  />
                </>
              )}

              {editingProvider.providerType === 1 && (
                <TextInput
                  label={t("settings.idPMetadataURLXMLEndpoin")}
                  value={editingProvider.metadataUrl || ""}
                  onChange={(v) =>
                    setEditingProvider((prev) => ({ ...prev, metadataUrl: v }))
                  }
                  hint="SAML 2.0 IdP Federation Metadata URL"
                />
              )}

              <TextInput
                label={t("settings.roleMappingRulesJSON")}
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
                label={t("settings.buttonText")}
                value={editingProvider.buttonText || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, buttonText: v }))
                }
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
                {testing
                  ? t("settingsTabs.notifications.testing")
                  : "🔍 Test Connection"}
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
                  {t("settingsTabs.categories.modal.cancel")}
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={handleSaveProvider}
                >
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
