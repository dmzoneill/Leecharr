import { useAppStore } from "../store/app";
import type { CurrentUser } from "../api/types";

export interface Permissions {
  isAdmin: boolean;
  isOperator: boolean;
  isReadOnly: boolean;
  canManageSettings: boolean;
  canManageSystem: boolean;
  canManageBackups: boolean;
  canManageIndexers: boolean;
  canMutateTorrents: boolean;
  canAddTorrent: boolean;
  canDeleteTorrent: boolean;
  canPauseResumeTorrent: boolean;
  canModifyTorrent: boolean;
  canSaveSettings: boolean;
  canWipeData: boolean;
  hasRole: (role: string) => boolean;
}

export function getPermissions(
  user: CurrentUser | null | undefined,
): Permissions {
  const roles = user?.roles ?? [];
  const normalizedRoles = roles.map((r) => r.toLowerCase().trim());

  const isAuthenticated = user?.isAuthenticated ?? false;
  const isAdmin = isAuthenticated && normalizedRoles.includes("admin");
  const isOperator =
    isAuthenticated &&
    (isAdmin ||
      normalizedRoles.includes("user") ||
      normalizedRoles.includes("operator"));
  const isReadOnly = !isOperator;

  return {
    isAdmin,
    isOperator,
    isReadOnly,
    canManageSettings: isAdmin,
    canManageSystem: isAdmin,
    canManageBackups: isAdmin,
    canManageIndexers: isAdmin,
    canMutateTorrents: isOperator,
    canAddTorrent: isOperator,
    canDeleteTorrent: isOperator,
    canPauseResumeTorrent: isOperator,
    canModifyTorrent: isOperator,
    canSaveSettings: isAdmin,
    canWipeData: isOperator,
    hasRole: (role: string) =>
      normalizedRoles.includes(role.toLowerCase().trim()),
  };
}

export function usePermissions(explicitUser?: CurrentUser | null): Permissions {
  const storeUser = useAppStore((s) => s.currentUser);
  const user = explicitUser !== undefined ? explicitUser : storeUser;
  return getPermissions(user);
}

export default usePermissions;
