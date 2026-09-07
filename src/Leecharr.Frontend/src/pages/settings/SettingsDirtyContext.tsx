import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
} from "react";
import { useTranslation } from "../../i18n";
import ConfirmContext from "../../context/ConfirmContext";

export interface SettingsDirtyContextType {
  isDirty: boolean;
  setDirty: (dirty: boolean) => void;
  confirmIfDirty: (navigateFn: () => void) => Promise<boolean>;
}

export const defaultSettingsDirtyContext: SettingsDirtyContextType = {
  isDirty: false,
  setDirty: () => {},
  confirmIfDirty: async (navigateFn) => {
    navigateFn();
    return true;
  },
};

export const SettingsDirtyContext = createContext<SettingsDirtyContextType>(
  defaultSettingsDirtyContext,
);

export function useSettingsDirty(): SettingsDirtyContextType {
  return useContext(SettingsDirtyContext);
}

export function SettingsDirtyProvider({
  children,
}: {
  children: React.ReactNode;
}) {
  const { t } = useTranslation();
  const [isDirty, setIsDirty] = useState(false);
  const confirmCtx = useContext(ConfirmContext);

  // Handle browser tab close or reload when changes are unsaved
  useEffect(() => {
    if (!isDirty) return;
    const handleBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => window.removeEventListener("beforeunload", handleBeforeUnload);
  }, [isDirty]);

  const confirmIfDirty = useCallback(
    async (navigateFn: () => void): Promise<boolean> => {
      if (!isDirty) {
        navigateFn();
        return true;
      }

      let confirmed = false;
      if (confirmCtx?.confirm) {
        confirmed = await confirmCtx.confirm({
          title: t("settingsTabs.shared.unsavedChangesTitle"),
          message: t("settingsTabs.shared.unsavedChangesPrompt"),
          confirmText: t("settingsTabs.shared.discardAndLeave"),
          cancelText: t("settingsTabs.shared.stayOnPage"),
          danger: true,
        });
      } else {
        confirmed = window.confirm(
          t("settingsTabs.shared.unsavedChangesPrompt"),
        );
      }

      if (confirmed) {
        setIsDirty(false);
        navigateFn();
        return true;
      }

      return false;
    },
    [isDirty, confirmCtx],
  );

  return (
    <SettingsDirtyContext.Provider
      value={{
        isDirty,
        setDirty: setIsDirty,
        confirmIfDirty,
      }}
    >
      {children}
    </SettingsDirtyContext.Provider>
  );
}
