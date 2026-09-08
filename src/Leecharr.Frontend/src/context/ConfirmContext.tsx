import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useRef,
} from "react";
import { ConfirmModal } from "../components/ConfirmModal";
import { ErrorBoundary } from "../components/ErrorBoundary";

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

interface QueueItem {
  options: ConfirmOptions;
  resolve: (value: boolean) => void;
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

  const queueRef = useRef<QueueItem[]>([]);
  const activeItemRef = useRef<QueueItem | null>(null);

  const confirm: ConfirmDialogFn = useCallback((options) => {
    const parsedOptions: ConfirmOptions =
      typeof options === "string" ? { message: options } : options;

    return new Promise<boolean>((resolve) => {
      const item: QueueItem = { options: parsedOptions, resolve };
      if (!activeItemRef.current) {
        activeItemRef.current = item;
        setModalState({
          isOpen: true,
          options: parsedOptions,
        });
      } else {
        queueRef.current.push(item);
      }
    });
  }, []);

  const processNext = useCallback((confirmed: boolean) => {
    const current = activeItemRef.current;
    if (current) {
      current.resolve(confirmed);
      activeItemRef.current = null;
    }

    if (queueRef.current.length > 0) {
      const nextItem = queueRef.current.shift()!;
      activeItemRef.current = nextItem;
      setModalState({
        isOpen: true,
        options: nextItem.options,
      });
    } else {
      setModalState((prev) => ({ ...prev, isOpen: false }));
    }
  }, []);

  const handleConfirm = useCallback(() => {
    processNext(true);
  }, [processNext]);

  const handleCancel = useCallback(() => {
    processNext(false);
  }, [processNext]);

  return (
    <ConfirmContext.Provider value={{ confirm }}>
      {children}
      <ErrorBoundary title="Confirmation Dialog">
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
      </ErrorBoundary>
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
