import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useRef,
} from "react";
import { ConfirmModal } from "../components/ConfirmModal";

export interface ConfirmOptions {
  title?: string;
  message: string | React.ReactNode;
  confirmText?: string;
  cancelText?: string;
  danger?: boolean;
}

export type ConfirmDialogFn = (
  options: ConfirmOptions | string,
) => Promise<boolean>;

interface ConfirmContextType {
  confirm: ConfirmDialogFn;
}

const ConfirmContext = createContext<ConfirmContextType | undefined>(undefined);

export const ConfirmProvider: React.FC<{ children: React.ReactNode }> = ({
  children,
}) => {
  const [modalState, setModalState] = useState<{
    isOpen: boolean;
    options: ConfirmOptions;
  }>({
    isOpen: false,
    options: { message: "" },
  });

  const resolverRef = useRef<((value: boolean) => void) | null>(null);

  const confirm: ConfirmDialogFn = useCallback((options) => {
    const parsedOptions: ConfirmOptions =
      typeof options === "string" ? { message: options } : options;

    return new Promise<boolean>((resolve) => {
      resolverRef.current = resolve;
      setModalState({
        isOpen: true,
        options: parsedOptions,
      });
    });
  }, []);

  const handleConfirm = useCallback(() => {
    setModalState((prev) => ({ ...prev, isOpen: false }));
    if (resolverRef.current) {
      resolverRef.current(true);
      resolverRef.current = null;
    }
  }, []);

  const handleCancel = useCallback(() => {
    setModalState((prev) => ({ ...prev, isOpen: false }));
    if (resolverRef.current) {
      resolverRef.current(false);
      resolverRef.current = null;
    }
  }, []);

  return (
    <ConfirmContext.Provider value={{ confirm }}>
      {children}
      <ConfirmModal
        isOpen={modalState.isOpen}
        title={modalState.options.title}
        message={modalState.options.message}
        confirmText={modalState.options.confirmText}
        cancelText={modalState.options.cancelText}
        danger={modalState.options.danger}
        onConfirm={handleConfirm}
        onCancel={handleCancel}
      />
    </ConfirmContext.Provider>
  );
};

export function useConfirm(): ConfirmDialogFn {
  const context = useContext(ConfirmContext);
  if (!context) {
    throw new Error("useConfirm must be used within a ConfirmProvider");
  }
  return context.confirm;
}

export function useConfirmDialog(): ConfirmDialogFn {
  return useConfirm();
}

export default ConfirmContext;
