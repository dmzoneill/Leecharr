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
  return {
    name: impl === "Webhook" ? "Generic Webhook" : impl,
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

    case "Apprise": {
      let resolvedUrl = form.url.trim();
      if (resolvedUrl) {
        const cleanUrl = resolvedUrl.replace(/\/+$/, "");
        if (!cleanUrl.endsWith("/notify") && !cleanUrl.includes("/notify?")) {
          resolvedUrl = `${cleanUrl}/notify`;
        }
      }
      settingsObj = {
        url: resolvedUrl,
      };
      break;
    }

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
  if (!form.name.trim()) {
    return "Notification Name is required";
  }

  switch (form.implementation) {
    case "Discord":
      if (!form.url.trim()) return "Discord Webhook URL is required";
      break;
    case "Telegram":
      if (!form.token.trim()) return "Telegram Bot Token is required";
      if (!form.chatId.trim()) return "Telegram Chat ID is required";
      break;
    case "Gotify":
      if (!form.url.trim()) return "Gotify Server URL is required";
      if (!form.token.trim()) return "Gotify App Token is required";
      break;
    case "Pushover":
      if (!form.userKey.trim()) return "Pushover User Key is required";
      if (!form.token.trim()) return "Pushover App Token is required";
      break;
    case "Apprise":
      if (!form.url.trim()) return "Apprise Target URL is required";
      break;
    case "Webhook":
      if (!form.url.trim()) return "Webhook URL is required";
      break;
    case "Email":
      if (!form.server.trim()) return "SMTP Server Host is required";
      if (!form.recipient.trim()) return "Recipient Email Address is required";
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
        : "Telegram Bot";
    }
    if (notif.implementation === "Pushover") {
      return s.user
        ? `User: ${String(s.user).substring(0, 6)}...`
        : "Pushover Alert";
    }
    if (notif.implementation === "Email") {
      return s.recipient
        ? `To: ${s.recipient}`
        : s.server
          ? `${s.server}:${s.port || 587}`
          : "SMTP Email";
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
      showToast("Toast notification preferences saved", "success");
    } catch (err: any) {
      showToast(
        err?.message || "Failed to save notification preferences",
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
              `Test notification for "${name}" failed: ${data.message || "Unknown error"}`,
              "error",
            );
          }
        },
        onError: (err: any) => {
          const msg = err?.message || "Failed to send test notification";
          setTestResults((prev) => ({
            ...prev,
            [id]: { success: false, message: msg },
          }));
          showToast(`Test notification for "${name}" failed: ${msg}`, "error");
        },
      });
    } catch (err: any) {
      const msg = err?.message || "Failed to send test notification";
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
            showToast("Test notification sent successfully", "success");
          } else {
            showToast(
              `Test notification failed: ${data.message || "Unknown error"}`,
              "error",
            );
          }
        },
        onError: (err: any) => {
          const msg = err?.message || "Failed to send test notification";
          setModalTestResult({ success: false, message: msg });
          showToast(`Test notification failed: ${msg}`, "error");
        },
      });
    } catch (err: any) {
      const msg = err?.message || "Failed to send test notification";
      setModalTestResult({ success: false, message: msg });
      showToast(`Test notification failed: ${msg}`, "error");
    }
  };

  const handleDelete = async (notif: NotificationResource) => {
    try {
      const ok = await confirm({
        title: "Delete Notification Connection",
        message: `Are you sure you want to delete the notification connection "${notif.name}"?`,
        danger: true,
        confirmText: "Delete",
      });
      if (!ok) return;

      deleteMutation.mutate(notif.id, {
        onSuccess: () => {
          showToast(`Notification "${notif.name}" deleted`, "info");
        },
        onError: (err: any) => {
          showToast(err?.message || "Failed to delete notification", "error");
        },
      });
    } catch (err: any) {
      showToast(err?.message || "Failed to delete notification", "error");
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
            showToast(err?.message || "Failed to update notification", "error");
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
            showToast(err?.message || "Failed to create notification", "error");
          },
        });
      }
    } catch (err: any) {
      showToast(err?.message || "Failed to save notification", "error");
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
        title="UI Toast Notifications"
        description="Configure in-browser popup toasts and alert positions"
      >
        <Toggle
          label="Enable Notifications"
          checked={form.enabled}
          onChange={(v) => setToastPref("enabled", v)}
          hint="Show toast notification popups on application events"
        />
        <SelectInput
          label="Position"
          value={form.position}
          onChange={(v) => setToastPref("position", v)}
          options={[
            { value: "top-right", label: "Top Right" },
            { value: "top-left", label: "Top Left" },
            { value: "bottom-right", label: "Bottom Right" },
            { value: "bottom-left", label: "Bottom Left" },
          ]}
          disabled={!form.enabled}
          hint="Screen corner where notification popups will dock"
        />
        <NumberInput
          label="Auto-Dismiss Timeout"
          value={form.autoDismissSeconds}
          onChange={(v) => setToastPref("autoDismissSeconds", v)}
          min={1}
          max={60}
          suffix="seconds"
          disabled={!form.enabled}
          hint="Duration before toasts automatically fade out"
        />
      </SectionCard>

      <SectionCard
        title="Notification Event Filters"
        description="Filter specific alert categories"
      >
        <Toggle
          label="Information"
          checked={form.showInfo}
          onChange={(v) => setToastPref("showInfo", v)}
          hint="Informational background event notifications"
        />
        <Toggle
          label="Success"
          checked={form.showSuccess}
          onChange={(v) => setToastPref("showSuccess", v)}
          hint="Successful operations, imports, and torrent actions"
        />
        <Toggle
          label="Warning"
          checked={form.showWarning}
          onChange={(v) => setToastPref("showWarning", v)}
          hint="Tracker timeouts, rate throttling, and disk threshold warnings"
        />
        <Toggle
          label="Error"
          checked={form.showError}
          onChange={(v) => setToastPref("showError", v)}
          hint="Critical failures and network disconnects"
        />
      </SectionCard>

      <SectionCard
        title="Outbound Webhooks & Notification Services"
        description="Configure outbound alerts for Discord, Telegram, Gotify, Pushover, Apprise, and Generic Webhooks"
      >
        {isLoadingNotifications ? (
          <div className="loading">Loading notifications...</div>
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
                      title="Test Notification"
                      onClick={(e) => {
                        e.stopPropagation();
                        handleTest(notif.id, notif.name);
                      }}
                    >
                      &#x2713;
                    </button>
                    <button
                      className="provider-card-action provider-card-action-danger"
                      title="Delete Notification"
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
                        Disabled
                      </span>
                    )}
                    {notif.onGrab && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        Grab
                      </span>
                    )}
                    {notif.onDownloadComplete && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        Complete
                      </span>
                    )}
                    {notif.onSeedGoalReached && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        Seed Goal
                      </span>
                    )}
                    {notif.onHealthIssue && (
                      <span className="provider-card-badge provider-card-badge-blue">
                        Health
                      </span>
                    )}
                  </div>
                  <div className="provider-card-info">{summary}</div>
                  {testResults[notif.id]?.success === true && (
                    <div className="provider-card-test provider-card-test-ok">
                      ✓ Connection passed
                    </div>
                  )}
                  {testResults[notif.id]?.success === false && (
                    <div
                      className="provider-card-test provider-card-test-fail"
                      title={testResults[notif.id]?.message}
                    >
                      ✕ Connection failed
                    </div>
                  )}
                  {testResults[notif.id] === null && (
                    <div className="provider-card-test provider-card-test-pending">
                      Testing...
                    </div>
                  )}
                </div>
              );
            })}
            <div
              className="provider-card-add"
              onClick={handleOpenAddModal}
              title="Add Notification Connection"
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
                ? "Edit Notification Connection"
                : "Add Notification Connection"}
            </div>

            <TextInput
              label="Name"
              value={editing.name}
              onChange={(v) => {
                setEditing({ ...editing, name: v });
                setModalTestResult(null);
              }}
              placeholder="e.g. My Discord Alerts"
            />

            <SelectInput
              label="Notification Template"
              value={editing.implementation}
              onChange={handleTypeChange}
              options={[
                { value: "Discord", label: "Discord" },
                { value: "Telegram", label: "Telegram" },
                { value: "Gotify", label: "Gotify" },
                { value: "Pushover", label: "Pushover" },
                { value: "Apprise", label: "Apprise" },
                { value: "Webhook", label: "Generic Webhook" },
                { value: "Email", label: "Email" },
              ]}
              hint="Select notification platform or webhook format"
            />

            <Toggle
              label="Enable Connection"
              checked={editing.enable}
              onChange={(v) => setEditing({ ...editing, enable: v })}
              hint="Enable or disable outbound alerts for this destination"
            />

            {/* Platform-specific configuration fields */}
            {editing.implementation === "Discord" && (
              <>
                <TextInput
                  label="Discord Webhook URL"
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder="https://discord.com/api/webhooks/..."
                  hint="Discord channel incoming webhook URL"
                />
                <TextInput
                  label="Bot Username"
                  value={editing.username}
                  onChange={(v) => setEditing({ ...editing, username: v })}
                  placeholder="Leecharr"
                  hint="Optional custom username for Discord bot"
                />
                <TextInput
                  label="Avatar URL"
                  value={editing.avatarUrl}
                  onChange={(v) => setEditing({ ...editing, avatarUrl: v })}
                  placeholder="https://..."
                  hint="Optional URL for Discord webhook avatar icon"
                />
              </>
            )}

            {editing.implementation === "Telegram" && (
              <>
                <TextInput
                  label="Bot Token"
                  value={editing.token}
                  onChange={(v) => {
                    setEditing({ ...editing, token: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder="123456789:ABCdefGhIJKlmNoPQRsTUVwxyZ"
                  hint="Telegram Bot token obtained from @BotFather"
                />
                <TextInput
                  label="Chat ID"
                  value={editing.chatId}
                  onChange={(v) => {
                    setEditing({ ...editing, chatId: v });
                    setModalTestResult(null);
                  }}
                  placeholder="123456789"
                  hint="Telegram Chat or Channel ID to dispatch messages to"
                />
              </>
            )}

            {editing.implementation === "Gotify" && (
              <>
                <TextInput
                  label="Server URL"
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder="http://gotify.example.com"
                  hint="Base URL of your Gotify instance"
                />
                <TextInput
                  label="Application Token"
                  value={editing.token}
                  onChange={(v) => {
                    setEditing({ ...editing, token: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder="A1b2c3d4e5"
                  hint="Application token generated in Gotify"
                />
                <NumberInput
                  label="Priority"
                  value={editing.priority}
                  onChange={(v) => setEditing({ ...editing, priority: v })}
                  min={1}
                  max={10}
                  hint="Notification priority (1-10, default 5)"
                />
              </>
            )}

            {editing.implementation === "Pushover" && (
              <>
                <TextInput
                  label="User Key"
                  value={editing.userKey}
                  onChange={(v) => {
                    setEditing({ ...editing, userKey: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder="Your 30-character User Key"
                  hint="Pushover user or group key"
                />
                <TextInput
                  label="Application API Token"
                  value={editing.token}
                  onChange={(v) => {
                    setEditing({ ...editing, token: v });
                    setModalTestResult(null);
                  }}
                  type="password"
                  placeholder="Your 30-character App Token"
                  hint="Pushover application API token"
                />
              </>
            )}

            {editing.implementation === "Apprise" && (
              <>
                <TextInput
                  label="Apprise Target URL"
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder="http://localhost:8000/notify"
                  hint="Apprise API server endpoint or service URL"
                />
              </>
            )}

            {editing.implementation === "Webhook" && (
              <>
                <TextInput
                  label="Webhook URL"
                  value={editing.url}
                  onChange={(v) => {
                    setEditing({ ...editing, url: v });
                    setModalTestResult(null);
                  }}
                  placeholder="https://example.com/webhook"
                  hint="Destination HTTP or HTTPS endpoint"
                />
                <SelectInput
                  label="HTTP Method"
                  value={editing.method}
                  onChange={(v) => setEditing({ ...editing, method: v })}
                  options={[
                    { value: "POST", label: "POST" },
                    { value: "PUT", label: "PUT" },
                  ]}
                />
                <TextInput
                  label="Custom Headers (JSON)"
                  value={editing.customHeaders}
                  onChange={(v) => setEditing({ ...editing, customHeaders: v })}
                  placeholder='{"Authorization": "Bearer token", "X-Custom": "val"}'
                  hint="Optional JSON key-value map of HTTP headers"
                />
                <TextInput
                  label="Basic Auth Username"
                  value={editing.username}
                  onChange={(v) => setEditing({ ...editing, username: v })}
                  placeholder="Optional HTTP basic auth username"
                />
                <TextInput
                  label="Basic Auth Password"
                  value={editing.password}
                  onChange={(v) => setEditing({ ...editing, password: v })}
                  type="password"
                  placeholder="Optional HTTP basic auth password"
                />
              </>
            )}

            {editing.implementation === "Email" && (
              <>
                <TextInput
                  label="SMTP Server"
                  value={editing.server}
                  onChange={(v) => {
                    setEditing({ ...editing, server: v });
                    setModalTestResult(null);
                  }}
                  placeholder="smtp.example.com"
                  hint="Outgoing mail server hostname"
                />
                <NumberInput
                  label="Port"
                  value={editing.port}
                  onChange={(v) => setEditing({ ...editing, port: v })}
                  min={1}
                  max={65535}
                  hint="SMTP port (e.g. 587, 465, or 25)"
                />
                <Toggle
                  label="Use SSL / TLS"
                  checked={editing.useSsl}
                  onChange={(v) => setEditing({ ...editing, useSsl: v })}
                />
                <TextInput
                  label="From Address"
                  value={editing.from}
                  onChange={(v) => setEditing({ ...editing, from: v })}
                  placeholder="leecharr@example.com"
                  hint="Sender email address"
                />
                <TextInput
                  label="Recipient Address"
                  value={editing.recipient}
                  onChange={(v) => {
                    setEditing({ ...editing, recipient: v });
                    setModalTestResult(null);
                  }}
                  placeholder="user@example.com"
                  hint="Destination email address for notifications"
                />
                <TextInput
                  label="SMTP Username"
                  value={editing.username}
                  onChange={(v) => setEditing({ ...editing, username: v })}
                  placeholder="Optional SMTP authentication username"
                />
                <TextInput
                  label="SMTP Password"
                  value={editing.password}
                  onChange={(v) => setEditing({ ...editing, password: v })}
                  type="password"
                  placeholder="Optional SMTP authentication password"
                />
              </>
            )}

            <SectionTitle>Notification Triggers</SectionTitle>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
                gap: "0.5rem",
              }}
            >
              <Toggle
                label="On Grab"
                checked={editing.onGrab}
                onChange={(v) => setEditing({ ...editing, onGrab: v })}
                hint="Torrent or magnet added"
              />
              <Toggle
                label="On Download Complete"
                checked={editing.onDownloadComplete}
                onChange={(v) =>
                  setEditing({ ...editing, onDownloadComplete: v })
                }
                hint="Piece check completes 100%"
              />
              <Toggle
                label="On Seed Goal Reached"
                checked={editing.onSeedGoalReached}
                onChange={(v) =>
                  setEditing({ ...editing, onSeedGoalReached: v })
                }
                hint="Target ratio or seed time met"
              />
              <Toggle
                label="On Health Issue"
                checked={editing.onHealthIssue}
                onChange={(v) => setEditing({ ...editing, onHealthIssue: v })}
                hint="Errors, tracker timeout, low disk"
              />
              <Toggle
                label="On Health Restored"
                checked={editing.onHealthRestored}
                onChange={(v) =>
                  setEditing({ ...editing, onHealthRestored: v })
                }
                hint="Health issue returns to normal"
              />
              <Toggle
                label="On Manual Action Required"
                checked={editing.onManualInteractionRequired}
                onChange={(v) =>
                  setEditing({ ...editing, onManualInteractionRequired: v })
                }
                hint="Stalled or action needed"
              />
              <Toggle
                label="On Media Inspected"
                checked={editing.onMediaInspected}
                onChange={(v) =>
                  setEditing({ ...editing, onMediaInspected: v })
                }
                hint="Codecs and stream info inspected"
              />
              <Toggle
                label="On Extract Complete"
                checked={editing.onExtractComplete}
                onChange={(v) =>
                  setEditing({ ...editing, onExtractComplete: v })
                }
                hint="Archive unpack finished"
              />
              <Toggle
                label="On Torrent Deleted"
                checked={editing.onTorrentDeleted}
                onChange={(v) =>
                  setEditing({ ...editing, onTorrentDeleted: v })
                }
                hint="Torrent removed from client"
              />
              <Toggle
                label="On Application Update"
                checked={editing.onApplicationUpdate}
                onChange={(v) =>
                  setEditing({ ...editing, onApplicationUpdate: v })
                }
                hint="Leecharr upgraded to new version"
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
                <span>Testing notification connection...</span>
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
                      ? "Notification Sent Successfully"
                      : "Notification Test Failed"}
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
                  ? "Testing..."
                  : "Test Notification"}
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
                  Cancel
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
                    ? "Saving..."
                    : "Save"}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
