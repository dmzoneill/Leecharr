import { useEffect } from "react";

/**
 * Custom hook to invoke an onClose callback when the Escape key is pressed.
 *
 * @param onClose Callback function invoked on Escape press.
 * @param isOpen Whether the dialog/modal is currently open. Defaults to true.
 */
export function useEscapeKey(onClose?: () => void, isOpen: boolean = true) {
  useEffect(() => {
    if (!isOpen || !onClose) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" || event.key === "Esc") {
        event.stopPropagation();
        onClose();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [onClose, isOpen]);
}

export default useEscapeKey;
