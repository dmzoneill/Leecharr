import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { HealthCheckResult } from "../api/types";
import { isHealthIssue } from "./StatusBar";
import { isHealthError } from "../pages/SystemStatus";
import { isHealthOk } from "./HealthAlerts";

describe("Health Check Serialization & Casing Support (#1011)", () => {
  describe("StatusBar health issue detection", () => {
    it("recognizes camelCase warning and error health check types", () => {
      const warningCheck: HealthCheckResult = {
        type: "warning",
        source: "NoIndexers",
        message: "No indexers are configured",
      };
      const errorCheck: HealthCheckResult = {
        type: "error",
        source: "Database",
        message: "Database connection failed",
      };
      const okCheck: HealthCheckResult = {
        type: "ok",
        source: "DiskSpace",
        message: "Disk space is healthy",
      };
      const noticeCheck: HealthCheckResult = {
        type: "notice",
        source: "Updates",
        message: "Update available",
      };

      assert.strictEqual(isHealthIssue(warningCheck), true);
      assert.strictEqual(isHealthIssue(errorCheck), true);
      assert.strictEqual(isHealthIssue(okCheck), false);
      assert.strictEqual(isHealthIssue(noticeCheck), false);
    });

    it("recognizes PascalCase Warning and Error health check types for backwards compatibility", () => {
      const warningCheck: HealthCheckResult = {
        type: "Warning",
        source: "NoIndexers",
        message: "No indexers configured",
      };
      const errorCheck: HealthCheckResult = {
        type: "Error",
        source: "DatabaseIntegrity",
        message: "Database corrupt",
      };
      const okCheck: HealthCheckResult = {
        type: "Ok",
        source: "DiskSpace",
        message: "Disk space ok",
      };
      const noticeCheck: HealthCheckResult = {
        type: "Notice",
        source: "Announcements",
        message: "Notice info",
      };

      assert.strictEqual(isHealthIssue(warningCheck), true);
      assert.strictEqual(isHealthIssue(errorCheck), true);
      assert.strictEqual(isHealthIssue(okCheck), false);
      assert.strictEqual(isHealthIssue(noticeCheck), false);
    });

    it("calculates issue count correctly from mixed camelCase and PascalCase checks", () => {
      const healthChecks: HealthCheckResult[] = [
        { type: "ok", source: "Disk", message: null },
        { type: "warning", source: "NoIndexers", message: "Warning issue" },
        { type: "Error", source: "Database", message: "Error issue" },
        { type: "notice", source: "NoticeCheck", message: "Notice info" },
        { type: "error", source: "Engine", message: "Engine down" },
        { type: "Ok", source: "Network", message: null },
      ];

      const hasIssues =
        healthChecks &&
        healthChecks.some((c) => {
          const type = c.type?.toLowerCase();
          return type === "warning" || type === "error";
        });

      const issueCount = hasIssues
        ? healthChecks.filter((c) => {
            const type = c.type?.toLowerCase();
            return type === "warning" || type === "error";
          }).length
        : 0;

      assert.strictEqual(hasIssues, true);
      assert.strictEqual(issueCount, 3);
    });
  });

  describe("SystemStatus issues filter and error indicator", () => {
    it("filters warning and error checks with camelCase serialization", () => {
      const checks: HealthCheckResult[] = [
        { type: "ok", source: "Check1", message: null },
        { type: "notice", source: "Check2", message: "Info" },
        { type: "warning", source: "Check3", message: "Warning" },
        { type: "error", source: "Check4", message: "Error" },
      ];

      const warningOrErrorChecks =
        checks.filter((c) => {
          const type = c.type?.toLowerCase();
          return type === "warning" || type === "error";
        }) ?? [];

      assert.strictEqual(warningOrErrorChecks.length, 2);
      assert.strictEqual(warningOrErrorChecks[0].source, "Check3");
      assert.strictEqual(warningOrErrorChecks[1].source, "Check4");
    });

    it("distinguishes error checks case-insensitively", () => {
      assert.strictEqual(isHealthError({ type: "error" }), true);
      assert.strictEqual(isHealthError({ type: "Error" }), true);
      assert.strictEqual(isHealthError({ type: "warning" }), false);
      assert.strictEqual(isHealthError({ type: "Warning" }), false);
      assert.strictEqual(isHealthError({ type: "ok" }), false);
    });
  });

  describe("HealthAlerts isOk check", () => {
    it("recognizes ok case-insensitively and numeric enum 0", () => {
      assert.strictEqual(isHealthOk({ type: "ok" }), true);
      assert.strictEqual(isHealthOk({ type: "Ok" }), true);
      assert.strictEqual(isHealthOk({ type: 0 }), true);
      assert.strictEqual(isHealthOk({ type: "warning" }), false);
      assert.strictEqual(isHealthOk({ type: "Warning" }), false);
      assert.strictEqual(isHealthOk({ type: "error" }), false);
      assert.strictEqual(isHealthOk({ type: "Error" }), false);
      assert.strictEqual(isHealthOk({ type: "notice" }), false);
    });
  });
});
