import { useContext } from "react";
import ToastContext from "../context/ToastContext";
import { useTranslation } from "../i18n";

function ToastContainer() {
  const { t } = useTranslation();
  const ctx = useContext(ToastContext);
  if (!ctx || ctx.toasts.length === 0) return null;

  return (
    <div className="toast-container">
      {ctx.toasts.map((toast) => (
        <div key={toast.id} className={`toast toast-${toast.type}`}>
          <span className="toast-message">{toast.message}</span>
          <button
            className="toast-dismiss"
            onClick={() => ctx.removeToast(toast.id)}
            aria-label={t("alerts.dismiss")}
          >
            &times;
          </button>
        </div>
      ))}
    </div>
  );
}

export default ToastContainer;
