import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { getPermissions } from "./usePermissions";
import type { CurrentUser } from "../api/types";

describe("usePermissions / getPermissions", () => {
  it("defaults to ReadOnly when user is null or undefined", () => {
    const nullPerms = getPermissions(null);
    assert.strictEqual(nullPerms.isAdmin, false);
    assert.strictEqual(nullPerms.isOperator, false);
    assert.strictEqual(nullPerms.isReadOnly, true);
    assert.strictEqual(nullPerms.canSaveSettings, false);
    assert.strictEqual(nullPerms.canManageSystem, false);
    assert.strictEqual(nullPerms.canManageBackups, false);
    assert.strictEqual(nullPerms.canMutateTorrents, false);
    assert.strictEqual(nullPerms.canAddTorrent, false);
    assert.strictEqual(nullPerms.canDeleteTorrent, false);
    assert.strictEqual(nullPerms.hasRole("Admin"), false);

    const undefPerms = getPermissions(undefined);
    assert.strictEqual(undefPerms.isAdmin, false);
    assert.strictEqual(undefPerms.isOperator, false);
    assert.strictEqual(undefPerms.isReadOnly, true);
  });

  it("treats unauthenticated users as ReadOnly even if roles claim Admin", () => {
    const unauthUser: CurrentUser = {
      isAuthenticated: false,
      username: "anonymous",
      roles: ["Admin"],
    };

    const perms = getPermissions(unauthUser);
    assert.strictEqual(perms.isAdmin, false);
    assert.strictEqual(perms.isOperator, false);
    assert.strictEqual(perms.isReadOnly, true);
    assert.strictEqual(perms.canSaveSettings, false);
    assert.strictEqual(perms.canMutateTorrents, false);
  });

  it("grants full permissions to authenticated Admin user", () => {
    const adminUser: CurrentUser = {
      isAuthenticated: true,
      username: "admin",
      roles: ["Admin"],
    };

    const perms = getPermissions(adminUser);
    assert.strictEqual(perms.isAdmin, true);
    assert.strictEqual(perms.isOperator, true);
    assert.strictEqual(perms.isReadOnly, false);
    assert.strictEqual(perms.canManageSettings, true);
    assert.strictEqual(perms.canManageSystem, true);
    assert.strictEqual(perms.canManageBackups, true);
    assert.strictEqual(perms.canManageIndexers, true);
    assert.strictEqual(perms.canSaveSettings, true);
    assert.strictEqual(perms.canMutateTorrents, true);
    assert.strictEqual(perms.canAddTorrent, true);
    assert.strictEqual(perms.canDeleteTorrent, true);
    assert.strictEqual(perms.canPauseResumeTorrent, true);
    assert.strictEqual(perms.canModifyTorrent, true);
    assert.strictEqual(perms.canWipeData, true);
    assert.strictEqual(perms.hasRole("admin"), true);
    assert.strictEqual(perms.hasRole("Admin"), true);
    assert.strictEqual(perms.hasRole("User"), false);
  });

  it("grants mutation permissions to authenticated Operator / User without admin privileges", () => {
    const user: CurrentUser = {
      isAuthenticated: true,
      username: "operator1",
      roles: ["User"],
    };

    const perms = getPermissions(user);
    assert.strictEqual(perms.isAdmin, false);
    assert.strictEqual(perms.isOperator, true);
    assert.strictEqual(perms.isReadOnly, false);
    assert.strictEqual(perms.canManageSettings, false);
    assert.strictEqual(perms.canManageSystem, false);
    assert.strictEqual(perms.canManageBackups, false);
    assert.strictEqual(perms.canManageIndexers, false);
    assert.strictEqual(perms.canSaveSettings, false);
    assert.strictEqual(perms.canMutateTorrents, true);
    assert.strictEqual(perms.canAddTorrent, true);
    assert.strictEqual(perms.canDeleteTorrent, true);
    assert.strictEqual(perms.canPauseResumeTorrent, true);
    assert.strictEqual(perms.canModifyTorrent, true);
    assert.strictEqual(perms.canWipeData, true);
    assert.strictEqual(perms.hasRole("User"), true);
    assert.strictEqual(perms.hasRole("user"), true);
    assert.strictEqual(perms.hasRole("Admin"), false);
  });

  it("treats authenticated ReadOnly user with only read permissions", () => {
    const readOnlyUser: CurrentUser = {
      isAuthenticated: true,
      username: "viewer",
      roles: ["ReadOnly"],
    };

    const perms = getPermissions(readOnlyUser);
    assert.strictEqual(perms.isAdmin, false);
    assert.strictEqual(perms.isOperator, false);
    assert.strictEqual(perms.isReadOnly, true);
    assert.strictEqual(perms.canManageSettings, false);
    assert.strictEqual(perms.canManageSystem, false);
    assert.strictEqual(perms.canManageBackups, false);
    assert.strictEqual(perms.canManageIndexers, false);
    assert.strictEqual(perms.canSaveSettings, false);
    assert.strictEqual(perms.canMutateTorrents, false);
    assert.strictEqual(perms.canAddTorrent, false);
    assert.strictEqual(perms.canDeleteTorrent, false);
    assert.strictEqual(perms.canPauseResumeTorrent, false);
    assert.strictEqual(perms.canModifyTorrent, false);
    assert.strictEqual(perms.canWipeData, false);
    assert.strictEqual(perms.hasRole("ReadOnly"), true);
    assert.strictEqual(perms.hasRole("readonly"), true);
  });

  it("defaults authenticated user with empty roles to ReadOnly", () => {
    const noRoleUser: CurrentUser = {
      isAuthenticated: true,
      username: "unknown",
      roles: [],
    };

    const perms = getPermissions(noRoleUser);
    assert.strictEqual(perms.isAdmin, false);
    assert.strictEqual(perms.isOperator, false);
    assert.strictEqual(perms.isReadOnly, true);
    assert.strictEqual(perms.canSaveSettings, false);
    assert.strictEqual(perms.canMutateTorrents, false);
  });
});
