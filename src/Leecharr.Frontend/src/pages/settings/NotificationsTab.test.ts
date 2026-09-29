import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  getDefaultFormForImplementation,
  parseNotificationToForm,
  buildNotificationPayload,
  validateNotificationForm,
  getNotificationSummary,
} from "./NotificationsTab";
import type { NotificationResource } from "../../api/types";

describe("NotificationsTab - CustomScript and Tags (#984)", () => {
  it("provides proper default form state for CustomScript", () => {
    const form = getDefaultFormForImplementation("CustomScript");
    assert.strictEqual(form.implementation, "CustomScript");
    assert.strictEqual(form.name, "Custom Script");
    assert.strictEqual(form.configContract, "CustomScriptSettings");
    assert.strictEqual(form.path, "");
    assert.strictEqual(form.arguments, "");
    assert.deepStrictEqual(form.tags, []);
  });

  it("parses CustomScript notification resource into form state with path and arguments", () => {
    const notif: NotificationResource = {
      id: 42,
      name: "My Notification Script",
      implementation: "CustomScript",
      configContract: "CustomScriptSettings",
      enable: true,
      settings: JSON.stringify({
        path: "/usr/local/bin/notify.sh",
        arguments: "--action complete --notify",
      }),
      tags: [1, 5, 9],
    };

    const form = parseNotificationToForm(notif);
    assert.strictEqual(form.id, 42);
    assert.strictEqual(form.name, "My Notification Script");
    assert.strictEqual(form.implementation, "CustomScript");
    assert.strictEqual(form.path, "/usr/local/bin/notify.sh");
    assert.strictEqual(form.arguments, "--action complete --notify");
    assert.deepStrictEqual(form.tags, [1, 5, 9]);
  });

  it("parses alternative property names for CustomScript settings", () => {
    const notif: NotificationResource = {
      id: 43,
      name: "Alias Script",
      implementation: "CustomScript",
      settings: JSON.stringify({
        scriptPath: "/opt/scripts/hook.py",
        args: "-d --json",
      }),
      tags: [3],
    };

    const form = parseNotificationToForm(notif);
    assert.strictEqual(form.path, "/opt/scripts/hook.py");
    assert.strictEqual(form.arguments, "-d --json");
    assert.deepStrictEqual(form.tags, [3]);
  });

  it("parses raw string settings for CustomScript if not formatted as JSON", () => {
    const notif: NotificationResource = {
      id: 44,
      name: "Raw Script Path",
      implementation: "CustomScript",
      settings: "/bin/sh /scripts/run.sh",
      tags: [],
    };

    const form = parseNotificationToForm(notif);
    assert.strictEqual(form.path, "/bin/sh /scripts/run.sh");
    assert.strictEqual(form.arguments, "");
  });

  it("builds correct payload for CustomScript without falling back to Webhook", () => {
    const form = getDefaultFormForImplementation("CustomScript");
    form.id = 10;
    form.name = "Production Script";
    form.path = "  /var/scripts/leecharr-alert.sh  ";
    form.arguments = "  --alert --quiet  ";
    form.tags = [2, 4];

    const payload = buildNotificationPayload(form);
    assert.strictEqual(payload.id, 10);
    assert.strictEqual(payload.name, "Production Script");
    assert.strictEqual(payload.implementation, "CustomScript");
    assert.deepStrictEqual(payload.tags, [2, 4]);

    const parsedSettings = JSON.parse(payload.settings || "{}");
    assert.strictEqual(parsedSettings.path, "/var/scripts/leecharr-alert.sh");
    assert.strictEqual(parsedSettings.arguments, "--alert --quiet");
    // Ensure Webhook fields are not present
    assert.strictEqual(parsedSettings.url, undefined);
    assert.strictEqual(parsedSettings.method, undefined);
  });

  it("validates that CustomScript requires path", () => {
    const form = getDefaultFormForImplementation("CustomScript");
    form.name = "My Script";
    form.path = "   ";

    const err = validateNotificationForm(form);
    assert.ok(err, "Expected validation error when path is empty");

    form.path = "/usr/bin/my-script";
    const ok = validateNotificationForm(form);
    assert.strictEqual(
      ok,
      null,
      "Expected validation to pass when path is provided",
    );
  });

  it("returns script path in getNotificationSummary for CustomScript", () => {
    const notif: NotificationResource = {
      name: "Custom Script",
      implementation: "CustomScript",
      settings: JSON.stringify({
        path: "/home/user/notify.sh",
        arguments: "--mode alert",
      }),
    };

    const summary = getNotificationSummary(notif);
    assert.strictEqual(summary, "/home/user/notify.sh");
  });
});
