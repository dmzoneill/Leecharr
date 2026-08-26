import {
  createContext,
  useContext,
  useState,
  useCallback,
  useRef,
} from "react";
import type { ReactNode } from "react";

export type ToastType = "success" | "error" | "info";

interface Toast {
  id: number;
  message: string;
  type: ToastType;
}

interface ToastContextValue {
  toasts: Toast[];
  showToast: (message: string, type?: ToastType) => void;
  removeToast: (id: number) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(0);

  const removeToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const showToast = useCallback(
    (message: string, type: ToastType = "info") => {
      let durationMs = 4000;
      try {
        const stored = localStorage.getItem("leecharr-notification-settings");
        if (stored) {
          const settings = JSON.parse(stored);
          if (settings.enabled === false) return;
          if (type === "info" && settings.showInfo === false) return;
          if (type === "success" && settings.showSuccess === false) return;
          if (type === "error" && settings.showError === false) return;
          if (settings.autoDismissSeconds > 0) {
            durationMs = settings.autoDismissSeconds * 1000;
          }
        }
      } catch {
        // Fallback to default
      }

      const id = nextId.current++;
      setToasts((prev) => [...prev, { id, message, type }]);

      setTimeout(() => {
        removeToast(id);
      }, durationMs);
    },
    [removeToast],
  );

  return (
    <ToastContext.Provider value={{ toasts, showToast, removeToast }}>
      {children}
    </ToastContext.Provider>
  );
}

export function useToast(): {
  showToast: (message: string, type?: ToastType) => void;
} {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error("useToast must be used within a ToastProvider");
  }
  return { showToast: ctx.showToast };
}

export default ToastContext;
