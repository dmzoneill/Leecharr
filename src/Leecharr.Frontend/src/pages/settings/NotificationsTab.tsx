import { useTranslation, translate } from "../../i18n";
import { useState } from "react";
import type {
  NotificationSettings,
  NotificationResource,
  NotificationTestResult,
} from "../../api/types";
import {
  useNotifications,
  useCreateNotification,
  useUpdateNotification,
  useDeleteNotification,
  useTestNotification,
  useTestDirectNotification,
} from "../../api/hooks";
import {
  Toggle,
  SelectInput,
  TextInput,
  NumberInput,
  SaveBar,
  SectionCard,
  SectionTitle,
} from "./shared";
import { useToast } from "../../context/ToastContext";
import { useConfirm } from "../../context/ConfirmContext";
import { useEscapeKey } from "../../hooks/useEscapeKey";

const NOTIFICATION_SETTINGS_KEY = "leecharr-notification-settings";

const defaultNotificationSettings: NotificationSettings = {
  enabled: true,
  position: "top-right",
  autoDismissSeconds: 5,
  showInfo: true,
  showSuccess: true,
  showWarning: true,
  showError: true,
};

function useNotificationSettings(): [
  NotificationSettings,
  (settings: NotificationSettings) => void,
] {
  const [settings, setSettings] = useState<NotificationSettings>(() => {
    try {
      const stored = localStorage.getItem(NOTIFICATION_SETTINGS_KEY);
      return stored
        ? { ...defaultNotificationSettings, ...JSON.parse(stored) }
        : defaultNotificationSettings;
    } catch {
      return defaultNotificationSettings;
    }
  });

  const saveSettings = (newSettings: NotificationSettings) => {
    setSettings(newSettings);
    localStorage.setItem(
      NOTIFICATION_SETTINGS_KEY,
      JSON.stringify(newSettings),
    );
  };

  return [settings, saveSettings];
}

interface NotificationFormState {
  id?: number;
  name: string;
  implementation: string;
  configContract?: string;
  enable: boolean;
  onGrab: boolean;
  onDownloadComplete: boolean;
  onMediaInspected: boolean;
  onExtractComplete: boolean;
  onSeedGoalReached: boolean;
  onTorrentDeleted: boolean;
  onHealthIssue: boolean;
  onHealthRestored: boolean;
  onManualInteractionRequired: boolean;
  onApplicationUpdate: boolean;
  tags: number[];

  // Template / provider specific settings
  url: string;
  token: string;
  chatId: string;
  userKey: string;
  username: string;
  avatarUrl: string;
  method: string;
  customHeaders: string;
  server: string;
  port: number;
  useSsl: boolean;
  password: string;
  from: string;
  recipient: string;
  priority: number;
}

function getDefaultFormForImplementation(impl: string): NotificationFormState {
  const t = translate;
  return {
    // @ts-ignore
    name:
      impl === "Webhook"
        ? t("settingsTabs.notifications.genericWebhook")
        : impl,
    implementation: impl,
    configContract: `${impl}Settings`,
    enable: true,
    onGrab: true,
    onDownloadComplete: true,
    onMediaInspected: false,
    onExtractComplete: false,
    onSeedGoalReached: true,
    onTorrentDeleted: false,
    onHealthIssue: true,
    onHealthRestored: true,
    onManualInteractionRequired: true,
    onApplicationUpdate: false,
    tags: [],
    url: "",
    token: "",
    chatId: "",
    userKey: "",
    username: impl === "Discord" ? "Leecharr" : "",
    avatarUrl: "",
    method: "POST",
    customHeaders: "",
    server: "",
    port: 587,
    useSsl: true,
    password: "",
    from: "",
    recipient: "",
    priority: 5,
  };
}

function parseNotificationToForm(
  notif: NotificationResource,
): NotificationFormState {
  let parsed: Record<string, any> = {};
  if (notif.settings) {
    try {
      parsed = JSON.parse(notif.settings);
    } catch {
      parsed = { url: notif.settings };
    }
  }

  const impl = notif.implementation || "Webhook";

  return {
    id: notif.id,
    name: notif.name || impl,
    implementation: impl,
    configContract: notif.configContract || `${impl}Settings`,
    enable: notif.enable ?? true,
    onGrab: notif.onGrab ?? true,
    onDownloadComplete: notif.onDownloadComplete ?? true,
    onMediaInspected: notif.onMediaInspected ?? false,
    onExtractComplete: notif.onExtractComplete ?? false,
    onSeedGoalReached: notif.onSeedGoalReached ?? true,
    onTorrentDeleted: notif.onTorrentDeleted ?? false,
    onHealthIssue: notif.onHealthIssue ?? true,
    onHealthRestored: notif.onHealthRestored ?? true,
    onManualInteractionRequired: notif.onManualInteractionRequired ?? true,
    onApplicationUpdate: notif.onApplicationUpdate ?? false,
    tags: notif.tags || [],

    url:
      parsed.serverUrl ||
      parsed.url ||
      parsed.targetUrl ||
      parsed.webhookUrl ||
      "",
    token: parsed.token || parsed.botToken || parsed.apiKey || "",
    chatId: parsed.chat_id || parsed.chatId || "",
    userKey: parsed.user || parsed.userKey || "",
    username: parsed.username || parsed.user || "",
    avatarUrl: parsed.avatarUrl || parsed.avatar_url || "",
    method: parsed.method || "POST",
    customHeaders:
      typeof parsed.headers === "object"
        ? JSON.stringify(parsed.headers, null, 2)
        : parsed.headers || "",
    server: parsed.server || parsed.host || "",
    port: parsed.port ? Number(parsed.port) : 587,
    useSsl: parsed.useSsl ?? parsed.ssl ?? true,
    password: parsed.password || parsed.pass || "",
    from: parsed.from || "",
    recipient: parsed.recipient || parsed.to || "",
    priority: parsed.priority !== undefined ? Number(parsed.priority) : 5,
  };
}

function buildNotificationPayload(
  form: NotificationFormState,
): NotificationResource {
  let settingsObj: Record<string, any> = {};

  switch (form.implementation) {
    case "Discord":
      settingsObj = {
        url: form.url.trim(),
        username: form.username.trim() || "Leecharr",
      };
      if (form.avatarUrl.trim()) {
        settingsObj.avatarUrl = form.avatarUrl.trim();
      }
      break;

    case "Telegram":
      settingsObj = {
        token: form.token.trim(),
        chat_id: form.chatId.trim(),
      };
      break;

    case "Gotify": {
      let resolvedUrl = form.url.trim();
      const token = form.token.trim();
      if (resolvedUrl) {
        const cleanUrl = resolvedUrl.replace(/\/+$/, "");
        if (!cleanUrl.endsWith("/message") && !cleanUrl.includes("/message?")) {
          resolvedUrl = `${cleanUrl}/message`;
        }
        if (token && !resolvedUrl.includes("token=")) {
          resolvedUrl += `?token=${encodeURIComponent(token)}`;
        }
      }
      settingsObj = {
        url: resolvedUrl,
        serverUrl: form.url.trim(),
        token,
        priority: form.priority || 5,
      };
      break;
    }

    case "Pushover":
      settingsObj = {
        token: form.token.trim(),
        user: form.userKey.trim(),
      };
      break;

    case "Apprise":
      settingsObj = {
        url: form.url.trim(),
      };
      break;

    case "Email":
      settingsObj = {
        server: form.server.trim(),
        port: form.port || 587,
        useSsl: form.useSsl,
        username: form.username.trim() || undefined,
        password: form.password || undefined,
        from: form.from.trim() || undefined,
        recipient: form.recipient.trim(),
      };
      break;

    case "Webhook":
    default: {
      let headers: any = undefined;
      if (form.customHeaders.trim()) {
        try {
          headers = JSON.parse(form.customHeaders);
        } catch {
          headers = form.customHeaders.trim();
        }
      }
      settingsObj = {
        url: form.url.trim(),
        method: form.method || "POST",
        headers,
        username: form.username.trim() || undefined,
        password: form.password || undefined,
      };
      break;
    }
  }

  return {
    id: form.id || 0,
    name: form.name.trim() || form.implementation,
    implementation: form.implementation,
    configContract: form.configContract || `${form.implementation}Settings`,
    settings: JSON.stringify(settingsObj),
    enable: form.enable,
    onGrab: form.onGrab,
    onDownloadComplete: form.onDownloadComplete,
    onMediaInspected: form.onMediaInspected,
    onExtractComplete: form.onExtractComplete,
    onSeedGoalReached: form.onSeedGoalReached,
    onTorrentDeleted: form.onTorrentDeleted,
    onHealthIssue: form.onHealthIssue,
    onHealthRestored: form.onHealthRestored,
    onManualInteractionRequired: form.onManualInteractionRequired,
    onApplicationUpdate: form.onApplicationUpdate,
    tags: form.tags || [],
  };
}

function validateNotificationForm(form: NotificationFormState): string | null {
  const t = translate;
  if (!form.name.trim()) {
    // @ts-ignore
    return t("settingsTabs.notifications.nameRequired");
  }

  switch (form.implementation) {
    case "Discord":
      // @ts-ignore
      if (!form.url.trim())
        return t("settingsTabs.notifications.discordUrlRequired");
      break;
    case "Telegram":
      // @ts-ignore
      if (!form.token.trim())
        return t("settingsTabs.notifications.telegramTokenRequired");
      // @ts-ignore
      if (!form.chatId.trim())
        return t("settingsTabs.notifications.telegramChatIdRequired");
      break;
    case "Gotify":
      // @ts-ignore
      if (!form.url.trim())
        return t("settingsTabs.notifications.gotifyUrlRequired");
      // @ts-ignore
      if (!form.token.trim())
        return t("settingsTabs.notifications.gotifyTokenRequired");
      break;
    case "Pushover":
      // @ts-ignore
      if (!form.userKey.trim())
        return t("settingsTabs.notifications.pushoverUserKeyRequired");
      // @ts-ignore
      if (!form.token.trim())
        return t("settingsTabs.notifications.pushoverTokenRequired");
      break;
    case "Apprise":
      // @ts-ignore
      if (!form.url.trim())
        return t("settingsTabs.notifications.appriseUrlRequired");
      break;
    case "Webhook":
      // @ts-ignore
      if (!form.url.trim())
        return t("settingsTabs.notifications.webhookUrlRequired");
      break;
    case "Email":
      // @ts-ignore
      if (!form.server.trim())
        return t("settingsTabs.notifications.smtpServerRequired");
      // @ts-ignore
      if (!form.recipient.trim())
        return t("settingsTabs.notifications.recipientEmailRequired");
      break;
  }

  return null;
}

function getNotificationSummary(notif: NotificationResource): string {
  try {
    const s = JSON.parse(notif.settings || "{}");
    if (notif.implementation === "Telegram") {
      return s.chat_id || s.chatId
        ? `Chat ID: ${s.chat_id || s.chatId}`
        : // @ts-ignore
          t("settingsTabs.notifications.telegramBot");
    }
    if (notif.implementation === "Pushover") {
      return s.user
        ? `User: ${String(s.user).substring(0, 6)}...`
        : // @ts-ignore
          t("settingsTabs.notifications.pushoverAlert");
    }
    if (notif.implementation === "Email") {
      return s.recipient
        ? `To: ${s.recipient}`
        : s.server
          ? `${s.server}:${s.port || 587}`
          : // @ts-ignore
            t("settingsTabs.notifications.smtpEmail");
    }
    return s.serverUrl || s.url || notif.settings || notif.implementation;
  } catch {
    return notif.settings || notif.implementation;
  }
}

function getNotificationUrl(notif: NotificationResource): string | null {
  try {
    const s = JSON.parse(notif.settings || "{}");
    if (typeof s.serverUrl === "string" && s.serverUrl.startsWith("http"))
      return s.serverUrl;
    if (typeof s.url === "string" && s.url.startsWith("http")) return s.url;
    if (notif.settings && notif.settings.startsWith("http"))
      return notif.settings;
  } catch {
    if (notif.settings && notif.settings.startsWith("http"))
      return notif.settings;
  }
  return null;
}

export function NotificationsTab() {
  const { t } = useTranslation();

  const { showToast } = useToast();
  const confirm = useConfirm();

  // In-browser toast preferences
  const [settings, saveSettings] = useNotificationSettings();
  const [form, setForm] = useState<NotificationSettings>(settings);
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState(false);

  // Backend outbound notifications
  const { data: notifications, isLoading: isLoadingNotifications } =
    useNotifications();
  const createMutation = useCreateNotification();
  const updateMutation = useUpdateNotification();
  const deleteMutation = useDeleteNotification();
  const testMutation = useTestNotification();
  const testDirectMutation = useTestDirectNotification();

  const [editing, setEditing] = useState<NotificationFormState | null>(null);
  const [testResults, setTestResults] = useState<
    Record<number, NotificationTestResult | null>
  >({});
  const [modalTestResult, setModalTestResult] =
    useState<NotificationTestResult | null>(null);

  useEscapeKey(() => {
    setEditing(null);
    setModalTestResult(null);
  }, Boolean(editing));

  const setToastPref = <K extends keyof NotificationSettings>(
    key: K,
    value: NotificationSettings[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
    setSaved(false);
  };

  const handleSaveToastPrefs = () => {
    try {
      saveSettings(form);
      setDirty(false);
      setSaved(true);
      showToast(t("settingsTabs.notifications.toastSaved"), "success");
    } catch (err: any) {
      showToast(
        err?.message || t("settingsTabs.notifications.toastSaveFailed"),
        "error",
      );
    }
  };

  const handleOpenAddModal = () => {
    setModalTestResult(null);
    setEditing(getDefaultFormForImplementation("Discord"));
  };

  const handleOpenModal = (notif: NotificationResource) => {
    setModalTestResult(null);
    setEditing(parseNotificationToForm(notif));
  };

  const handleTypeChange = (newType: string) => {
    if (!editing) return;
    const currentDefaults = getDefaultFormForImplementation(
      editing.implementation,
    );
    const newDefaults = getDefaultFormForImplementation(newType);
    const isDefaultName =
      !editing.name || editing.name === currentDefaults.name;

    setEditing({
      ...editing,
      implementation: newType,
      name: isDefaultName ? newDefaults.name : editing.name,
      configContract: `${newType}Settings`,
      username:
        newType === "Discord" && !editing.username
          ? "Leecharr"
          : editing.username,
    });
    setModalTestResult(null);
  };

  const handleTest = async (id: number, name: string) => {
    try {
      setTestResults((prev) => ({ ...prev, [id]: null }));
      testMutation.mutate(id, {
        onSuccess: (data) => {
          setTestResults((prev) => ({ ...prev, [id]: data }));
          if (data.success) {
            showToast(
              `Test notification for "${name}" sent successfully`,
              "success",
            );
          } else {
            showToast(
              `Test notification for "${name}" failed: ${data.message || t("settingsTabs.notifications.unknownError")}`,
              "error",
            );
          }
        },
        onError: (err: any) => {
          const msg =
            err?.message || t("settingsTabs.notifications.testFailed");
          setTestResults((prev) => ({
            ...prev,
            [id]: { success: false, message: msg },
          }));
          showToast(`Test notification for "${name}" failed: ${msg}`, "error");
        },
      });
    } catch (err: any) {
      const msg = err?.message || t("settingsTabs.notifications.testFailed");
      setTestResults((prev) => ({
        ...prev,
        [id]: { success: false, message: msg },
      }));
      showToast(`Test notification for "${name}" failed: ${msg}`, "error");
    }
  };

  const handleModalTest = async () => {
    if (!editing) return;
    const validationError = validateNotificationForm(editing);
    if (validationError) {
      showToast(validationError, "error");
      return;
    }

    try {
      setModalTestResult(null);
      const payload = buildNotificationPayload(editing);
      testDirectMutation.mutate(payload, {
        onSuccess: (data) => {
          setModalTestResult(data);
          if (data.success) {
            showToast(t("settingsTabs.notifications.testSuccess"), "success");
          } else {
            showToast(
              `Test notification failed: ${data.message || t("settingsTabs.notifications.unknownError")}`,
              "error",
            );
          }
        },
        onError: (err: any) => {
          const msg =
            err?.message || t("settingsTabs.notifications.testFailed");
          setModalTestResult({ success: false, message: msg });
          showToast(`Test notification failed: ${msg}`, "error");
        },
      });
    } catch (err: any) {
      const msg = err?.message || t("settingsTabs.notifications.testFailed");
      setModalTestResult({ success: false, message: msg });
      showToast(`Test notification failed: ${msg}`, "error");
    }
  };

  const handleDelete = async (notif: NotificationResource) => {
    try {
      const ok = await confirm({
        title: t("settingsTabs.notifications.deleteTitle"),
        message: `Are you sure you want to delete the notification connection "${notif.name}"?`,
        danger: true,
        confirmText: t("settingsTabs.categories.deleteConfirm"),
      });
      if (!ok) return;

      deleteMutation.mutate(notif.id, {
        onSuccess: () => {
          showToast(`Notification "${notif.name}" deleted`, "info");
        },
        onError: (err: any) => {
          showToast(
            err?.message || t("settingsTabs.notifications.deleteFailed"),
            "error",
          );
        },
      });
    } catch (err: any) {
      showToast(
        err?.message || t("settingsTabs.notifications.deleteFailed"),
        "error",
      );
    }
  };

  const handleSave = async () => {
    if (!editing) return;
    const validationError = validateNotificationForm(editing);
    if (validationError) {
      showToast(validationError, "error");
      return;
    }

    try {
      const payload = buildNotificationPayload(editing);
      if (editing.id) {
        updateMutation.mutate(payload, {
          onSuccess: () => {
            showToast(`Notification "${payload.name}" updated`, "success");
            setEditing(null);
            setModalTestResult(null);
          },
          onError: (err: any) => {
            showToast(
              err?.message || t("settingsTabs.notifications.updateFailed"),
              "error",
            );
          },
        });
      } else {
        createMutation.mutate(payload, {
          onSuccess: () => {
            showToast(`Notification "${payload.name}" created`, "success");
            setEditing(null);
            setModalTestResult(null);
          },
          onError: (err: any) => {
            showToast(
              err?.message || t("settingsTabs.notifications.createFailed"),
              "error",
            );
          },
        });
      }
    } catch (err: any) {
      showToast(
        err?.message || t("settingsTabs.notifications.saveFailed"),
        "error",
      );
    }
  };

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={false}
        isError={false}
        isSuccess={saved}
        error={null}
        onSave={handleSaveToastPrefs}
      />

      <SectionCard
        title={t("settingsTabs.notifications.toastTitle")}
        description={t("settingsTabs.notifications.toastDescription")}
      >
        <Toggle
          label={t("settingsTabs.notifications.enableToast")}
          checked={form.enabled}
          onChange={(v) => setToastPref("enabled", v)}
          hint={t("settingsTabs.notifications.enableToastHint")}
        />
        <SelectInput
          label={t("settingsTabs.notifications.position")}
          value={form.position}
          onChange={(v) => setToastPref("position", v)}
          options={[
            {
              value: "top-right",
              label: t("settingsTabs.notifications.positionTopRight"),
            },
            {
              value: "top-left",
              label: t("settingsTabs.notifications.positionTopLeft"),
            },
            {
              value: "bottom-right",
              label: t("settingsTabs.notifications.positionBottomRight"),
            },
            {
              value: "bottom-left",
              label: t("settingsTabs.notifications.positionBottomLeft"),
            },
          ]}
          disabled={!form.enabled}
          hint={t("settingsTabs.notifications.positionHint")}
        />
        <NumberInput
          label={t("settingsTabs.notifications.timeout")}
          value={form.autoDismissSeconds}
          onChange={(v) => setToastPref("autoDismissSeconds", v)}
          min={1}
          max={60}
          suffix={t("settingsTabs.notifications.timeoutSuffix")}
          disabled={!form.enabled}
          hint={t("settingsTabs.notifications.timeoutHint")}
        />
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.notifications.filtersTitle")}
        description={t("settingsTabs.notifications.filtersDescription")}
      >
        <Toggle
          label={t("settingsTabs.notifications.info")}
          checked={form.showInfo}
          onChange={(v) => setToastPref("showInfo", v)}
          hint={t("settingsTabs.notifications.infoHint")}
        />
        <Toggle
          label={t("settingsTabs.notifications.success")}
          checked={form.showSuccess}
          onChange={(v) => setToastPref("showSuccess", v)}
          hint={t("settingsTabs.notifications.successHint")}
        />
        <Toggle
          label={t("settingsTabs.notifications.warning")}
          checked={form.showWarning}
          onChange={(v) => setToastPref("showWarning", v)}
          hint={t("settingsTabs.notifications.warningHint")}
        />
        <Toggle
          label={t("settingsTabs.notifications.error")}
          checked={form.showError}
          onChange={(v) => setToastPref("showError", v)}
          hint={t("settingsTabs.notifications.errorHint")}
        />
      </SectionCard>

      <SectionCard
        title={t("settingsTabs.notifications.outboundTitle")}
        description={t("settingsTabs.notifications.outboundDescription")}
      >
        {isLoadingNotifications ? (
          <div className="loading">
            {t("settingsTabs.notifications.loading")}
          </div>
        ) : (
          <div className="provider-cards">
            {notifications?.map((notif) => {
              const externalUrl = getNotificationUrl(notif);
              const summary = getNotificationSummary(notif);
              return (
                <div
                  key={notif.id}
                  className="provider-card"
                  onClick={() => handleOpenModal(notif)}
                >
                  <div className="provider-card-actions">
                    {externalUrl && (
                      <a
                        href={externalUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="provider-card-action"
                        title={`Open endpoint (${externalUrl})`}
                        onClick={(e) => e.stopPropagation()}
                        style={{ textDecoration: "none", color: "inherit" }}
                      >
                        ↗
                      </a>
                    )}
                    <button
                      className="provider-card-action"
                      title={t("settingsTabs.notifications.testBtnTitle")}
                      onClick={(e) => {
                        e.stopPropagation();
                        handleTest(notif.id, notif.name);
                      }}
                    >
                      &#x2713;
                    </button>
                    <button
                      className="provider-card-action provider-card-action-danger"
                      title={t("settingsTabs.notifications.deleteBtnTitle")}
                      onClick={(e) => {
                        e.stopPropagation();
                        handleDelete(notif);
                      }}
                    >
                      &#x2715;
                    </button>
                  </div>
                  <div className="provider-card-name">{notif.name}</div>
                  <div className="provider-card-badges">
                    <span className="provider-card-badge provider-card-badge-green">
                      {notif.implementation}
                    </span>
                    {notif.enable === false && (
                      <span className="provider-card-badge provider-card-badge-gray">
                        {t("settingsTabs.categories.table.disabled")}
                      </span>
                    )}
                    {notif.onGrab && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        {t("settingsTabs.notifications.badgeGrab")}
                      </span>
                    )}
                    {notif.onDownloadComplete && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        {t("settingsTabs.notifications.badgeComplete")}
                      </span>
                    )}
                    {notif.onSeedGoalReached && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        {t("settingsTabs.notifications.badgeSeedGoal")}
                      </span>
                    )}
                    {notif.onHealthIssue && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        {t("settingsTabs.notifications.badgeHealth")}
                      </span>
                    )}
                  </div>
                  <div className="provider-card-info">{summary}</div>
                  {testResults[notif.id]?.success === true && (
                    <div className="provider-card-test provider-card-test-ok">
                      {t("settingsTabs.indexers.connectionPassed")}
                    </div>
                  )}
                  {testResults[notif.id]?.success === false && (
                    <div
                      className="provider-card-test provider-card-test-fail"
                      title={testResults[notif.id]?.message}
                    >
                      {t("settingsTabs.indexers.connectionFailed")}
                    </div>
                  )}
                  {testResults[notif.id] === null && (
                    <div className="provider-card-test provider-card-test-pending">
                      {t("settingsTabs.notifications.testing")}
                    </div>
                  )}
                </div>
              );
            })}
            <div
              className="provider-card-add"
              onClick={handleOpenAddModal}
              title={t("settingsTabs.notifications.addConnection")}
            >
              <span className="provider-card-add-icon">+</span>
            </div>
          </div>
        )}
      </SectionCard>

      {editing && (
        <div
          className="modal-overlay"
          onClick={() => {
            setEditing(null);
            setModalTestResult(null);
          }}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 580,
              maxHeight: "90vh",
              overflowY: "auto",
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid rgba(255, 255, 255, 0.12)",
            }}
          >
            <div
              className="modal-title"
              style={{ fontSize: "1.2rem", marginBottom: "1rem" }}
            >
              {editing.id
                ? t("settingsTabs.notifications.editConnection")
                : t("settingsTabs.notifications.addConnection")}
            </div>

            <TextInput
              label={t("settingsTabs.categories.table.name")}
              value={editing.name}
              onChange={(v) => {
                setEditing({ ...editing, name: v });
                setModalTestResult(null);
              }}
              placeholder={t("settingsTabs.notifications.namePlaceholder")}
            />

            <SelectInput
              label={t("settingsTabs.notifications.template")}
              value={editing.implementation}
              onChange={handleTypeChange}
              options={[
                { value: "Discord", label: "Discord" },
                { value: "Telegram", label: "Telegram" },
                { value: "Gotify", label: "Gotify" },
                { value: "Pushover", label: "Pushover" },
                { value: "Apprise", label: "Apprise" },
                {
                  value: "Webhook",
                  label: t("settingsTabs.notifications.genericWebhook"),
                },
                { value: "Email", label: "Email" },
              ]}
              hint={t("settingsTabs.notifications.templateHint")}
            />

            <Toggle
              label={t("settingsTabs.notifications.enableConnection")}
              checked={editing.enable}
              onChange={(v) => setEditing({ ...editing, enable: v })}
              hint={t("settingsTabs.notifications.enableConnectionHint")}
            />

            {/* Platform-specific configuration fields */}
            {editing.implementation === "Discord" && (
              <>
                <TextInput
                  label={t("settingsTabs.notifications.discordUrl")}
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder={t("settings.httpsDiscordComApiWebhoo")}
                  hint={t("settingsTabs.notifications.discordUrlHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.botUsername")}
                  value={editing.username}
                  onChange={(v) => setEditing({ ...editing, username: v })}
                  placeholder={t("settings.leecharr2")}
                  hint={t("settingsTabs.notifications.discordBotUsernameHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.avatarUrl")}
                  value={editing.avatarUrl}
                  onChange={(v) => setEditing({ ...editing, avatarUrl: v })}
                  placeholder={t("settings.https")}
                  hint={t("settingsTabs.notifications.avatarUrlHint")}
                />
              </>
            )}

            {editing.implementation === "Telegram" && (
              <>
                <TextInput
                  label={t("settingsTabs.notifications.botToken")}
                  value={editing.token}
                  onChange={(v) => {
                    setEditing({ ...editing, token: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder={t("settings.123456789ABCdefGhIJKlmNoPQRsT")}
                  hint={t("settingsTabs.notifications.telegramTokenHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.chatId")}
                  value={editing.chatId}
                  onChange={(v) => {
                    setEditing({ ...editing, chatId: v });
                    setModalTestResult(null);
                  }}
                  placeholder={t("settings.123456789")}
                  hint={t("settingsTabs.notifications.chatIdHint")}
                />
              </>
            )}

            {editing.implementation === "Gotify" && (
              <>
                <TextInput
                  label={t("settingsTabs.notifications.serverUrl")}
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder={t("settings.httpGotifyExampleCom")}
                  hint={t("settingsTabs.notifications.gotifyUrlHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.appToken")}
                  value={editing.token}
                  onChange={(v) => {
                    setEditing({ ...editing, token: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder={t("settings.a1b2c3d4e5")}
                  hint={t("settingsTabs.notifications.gotifyTokenHint")}
                />
                <NumberInput
                  label={t("settingsTabs.notifications.priority")}
                  value={editing.priority}
                  onChange={(v) => setEditing({ ...editing, priority: v })}
                  min={1}
                  max={10}
                  hint={t("settingsTabs.notifications.priorityHint")}
                />
              </>
            )}

            {editing.implementation === "Pushover" && (
              <>
                <TextInput
                  label={t("settingsTabs.notifications.userKey")}
                  value={editing.userKey}
                  onChange={(v) => {
                    setEditing({ ...editing, userKey: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder={t(
                    "settingsTabs.notifications.userKeyPlaceholder",
                  )}
                  hint={t("settingsTabs.notifications.pushoverUserKeyHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.appApiToken")}
                  value={editing.token}
                  onChange={(v) => {
                    setEditing({ ...editing, token: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder={t(
                    "settingsTabs.notifications.appTokenPlaceholder",
                  )}
                  hint={t("settingsTabs.notifications.pushoverAppTokenHint")}
                />
              </>
            )}

            {editing.implementation === "Apprise" && (
              <>
                <TextInput
                  label={t("settingsTabs.notifications.appriseUrl")}
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder={t("settings.httpLocalhost8000Notify")}
                  hint={t("settingsTabs.notifications.appriseUrlHint")}
                />
              </>
            )}

            {editing.implementation === "Webhook" && (
              <>
                <TextInput
                  label={t("settingsTabs.notifications.webhookUrl")}
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder={t("settings.httpsExampleComWebhook")}
                  hint={t("settingsTabs.notifications.webhookUrlHint")}
                />
                <SelectInput
                  label={t("settingsTabs.notifications.httpMethod")}
                  value={editing.method}
                  onChange={(v) => setEditing({ ...editing, method: v })}
                  options={[
                    { value: "POST", label: "POST" },
                    { value: "PUT", label: "PUT" },
                  ]}
                />
                <TextInput
                  label={t("settingsTabs.notifications.customHeaders")}
                  value={editing.customHeaders}
                  onChange={(v) => setEditing({ ...editing, customHeaders: v })}
                  placeholder='{"Authorization": "Bearer token", "X-Custom": "val"}'
                  hint={t("settingsTabs.notifications.customHeadersHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.basicAuthUsername")}
                  value={editing.username}
                  onChange={(v) => setEditing({ ...editing, username: v })}
                  placeholder={t(
                    "settingsTabs.notifications.basicAuthUsernamePlaceholder",
                  )}
                />
                <TextInput
                  label={t("settingsTabs.notifications.basicAuthPassword")}
                  value={editing.password}
                  onChange={(v) => setEditing({ ...editing, password: v })}
                  type="password"
                  placeholder={t(
                    "settingsTabs.notifications.basicAuthPasswordPlaceholder",
                  )}
                />
              </>
            )}

            {editing.implementation === "Email" && (
              <>
                <TextInput
                  label={t("settingsTabs.notifications.smtpServer")}
                  value={editing.server}
                  onChange={(v) => {
                    setEditing({ ...editing, server: v });
                    setModalTestResult(null);
                  }}
                  placeholder={t("settings.smtpExampleCom")}
                  hint={t("settingsTabs.notifications.smtpServerHint")}
                />
                <NumberInput
                  label={t("settingsTabs.notifications.port")}
                  value={editing.port}
                  onChange={(v) => setEditing({ ...editing, port: v })}
                  min={1}
                  max={65535}
                  hint={t("settingsTabs.notifications.portHint")}
                />
                <Toggle
                  label={t("settingsTabs.notifications.useSsl")}
                  checked={editing.useSsl}
                  onChange={(v) => setEditing({ ...editing, useSsl: v })}
                />
                <TextInput
                  label={t("settingsTabs.notifications.fromAddress")}
                  value={editing.from}
                  onChange={(v) => setEditing({ ...editing, from: v })}
                  placeholder={t("settings.leecharrExampleCom")}
                  hint={t("settingsTabs.notifications.fromAddressHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.recipientAddress")}
                  value={editing.recipient}
                  onChange={(v) => {
                    setEditing({ ...editing, recipient: v });
                    setModalTestResult(null);
                  }}
                  placeholder={t("settings.userExampleCom")}
                  hint={t("settingsTabs.notifications.recipientAddressHint")}
                />
                <TextInput
                  label={t("settingsTabs.notifications.smtpUsername")}
                  value={editing.username}
                  onChange={(v) => setEditing({ ...editing, username: v })}
                  placeholder={t(
                    "settingsTabs.notifications.smtpUsernamePlaceholder",
                  )}
                />
                <TextInput
                  label={t("settingsTabs.notifications.smtpPassword")}
                  value={editing.password}
                  onChange={(v) => setEditing({ ...editing, password: v })}
                  type="password"
                  placeholder={t(
                    "settingsTabs.notifications.smtpPasswordPlaceholder",
                  )}
                />
              </>
            )}

            <SectionTitle>
              {t("settingsTabs.notifications.triggers")}
            </SectionTitle>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
                gap: "0.5rem",
              }}
            >
              <Toggle
                label={t("settingsTabs.notifications.onGrab")}
                checked={editing.onGrab}
                onChange={(v) => setEditing({ ...editing, onGrab: v })}
                hint={t("settingsTabs.notifications.onGrabHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onDownloadComplete")}
                checked={editing.onDownloadComplete}
                onChange={(v) =>
                  setEditing({ ...editing, onDownloadComplete: v })
                }
                hint={t("settingsTabs.notifications.onDownloadCompleteHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onSeedGoalReached")}
                checked={editing.onSeedGoalReached}
                onChange={(v) =>
                  setEditing({ ...editing, onSeedGoalReached: v })
                }
                hint={t("settingsTabs.notifications.onSeedGoalReachedHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onHealthIssue")}
                checked={editing.onHealthIssue}
                onChange={(v) => setEditing({ ...editing, onHealthIssue: v })}
                hint={t("settingsTabs.notifications.onHealthIssueHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onHealthRestored")}
                checked={editing.onHealthRestored}
                onChange={(v) =>
                  setEditing({ ...editing, onHealthRestored: v })
                }
                hint={t("settingsTabs.notifications.onHealthRestoredHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onManualActionRequired")}
                checked={editing.onManualInteractionRequired}
                onChange={(v) =>
                  setEditing({ ...editing, onManualInteractionRequired: v })
                }
                hint={t(
                  "settingsTabs.notifications.onManualActionRequiredHint",
                )}
              />
              <Toggle
                label={t("settingsTabs.notifications.onMediaInspected")}
                checked={editing.onMediaInspected}
                onChange={(v) =>
                  setEditing({ ...editing, onMediaInspected: v })
                }
                hint={t("settingsTabs.notifications.onMediaInspectedHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onExtractComplete")}
                checked={editing.onExtractComplete}
                onChange={(v) =>
                  setEditing({ ...editing, onExtractComplete: v })
                }
                hint={t("settingsTabs.notifications.onExtractCompleteHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onTorrentDeleted")}
                checked={editing.onTorrentDeleted}
                onChange={(v) =>
                  setEditing({ ...editing, onTorrentDeleted: v })
                }
                hint={t("settingsTabs.notifications.onTorrentDeletedHint")}
              />
              <Toggle
                label={t("settingsTabs.notifications.onApplicationUpdate")}
                checked={editing.onApplicationUpdate}
                onChange={(v) =>
                  setEditing({ ...editing, onApplicationUpdate: v })
                }
                hint={t("settingsTabs.notifications.onApplicationUpdateHint")}
              />
            </div>

            {testDirectMutation.isPending && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  fontSize: "0.875rem",
                  backgroundColor: "rgba(200, 168, 78, 0.12)",
                  color: "var(--accent, #c8a84e)",
                  border: "1px solid rgba(200, 168, 78, 0.35)",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                }}
              >
                <span>{t("settingsTabs.notifications.testingConnection")}</span>
              </div>
            )}

            {modalTestResult && !testDirectMutation.isPending && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  fontSize: "0.875rem",
                  lineHeight: "1.4",
                  display: "flex",
                  alignItems: "flex-start",
                  gap: "0.65rem",
                  backgroundColor: modalTestResult.success
                    ? "rgba(40, 167, 69, 0.15)"
                    : "rgba(220, 53, 69, 0.15)",
                  color: modalTestResult.success
                    ? "var(--success, #28a745)"
                    : "var(--danger, #dc3545)",
                  border: `1px solid ${
                    modalTestResult.success
                      ? "rgba(40, 167, 69, 0.35)"
                      : "rgba(220, 53, 69, 0.35)"
                  }`,
                }}
              >
                <span
                  style={{
                    fontWeight: "bold",
                    fontSize: "1.1rem",
                    lineHeight: "1",
                  }}
                >
                  {modalTestResult.success ? "✓" : "✕"}
                </span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600 }}>
                    {modalTestResult.success
                      ? t("settingsTabs.notifications.sentSuccessfully")
                      : t("settingsTabs.notifications.testFailedMsg")}
                  </div>
                  {modalTestResult.message && (
                    <div
                      style={{
                        marginTop: "0.25rem",
                        opacity: 0.95,
                        wordBreak: "break-word",
                      }}
                    >
                      {modalTestResult.message}
                    </div>
                  )}
                </div>
              </div>
            )}

            {(createMutation.isError || updateMutation.isError) && (
              <div className="modal-error" style={{ marginTop: "1rem" }}>
                {(createMutation.error || updateMutation.error)?.message}
              </div>
            )}

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
                gap: "0.75rem",
                flexWrap: "wrap",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={handleModalTest}
                disabled={testDirectMutation.isPending}
              >
                {testDirectMutation.isPending
                  ? t("settingsTabs.notifications.testing")
                  : t("settingsTabs.notifications.testBtnTitle")}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={() => {
                    setEditing(null);
                    setModalTestResult(null);
                  }}
                >
                  {t("settingsTabs.categories.modal.cancel")}
                </button>
                <button
                  type="button"
                  className="btn btn-primary btn-small"
                  onClick={handleSave}
                  disabled={
                    createMutation.isPending || updateMutation.isPending
                  }
                >
                  {createMutation.isPending || updateMutation.isPending
                    ? t("settingsTabs.categories.modal.saving")
                    : t("settingsTabs.notifications.save")}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
