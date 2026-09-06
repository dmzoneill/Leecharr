import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
} from "react";
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
          title: "Unsaved Changes",
          message:
            "You have unsaved changes in settings. If you leave this page, your changes will be discarded. Are you sure you want to leave?",
          confirmText: "Discard and Leave",
          cancelText: "Stay on Page",
          danger: true,
        });
      } else {
        confirmed = window.confirm(
          "You have unsaved changes in settings. If you leave this page, your changes will be discarded. Are you sure you want to leave?",
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
