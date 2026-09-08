import { useEffect, useRef } from "react";

export interface UseFocusTrapOptions {
  isOpen?: boolean;
  onClose?: () => void;
  initialFocusRef?: React.RefObject<HTMLElement | null>;
  restoreFocus?: boolean;
}

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Custom hook to trap keyboard focus within a modal or dialog container.
 * Keeps Tab / Shift+Tab navigation cycling within the container and handles Escape key to close.
 * Restores focus to the previously active element upon closing.
 */
export function useFocusTrap<T extends HTMLElement = HTMLDivElement>(
  options: boolean | UseFocusTrapOptions = true,
  onCloseCallback?: () => void,
) {
  const containerRef = useRef<T | null>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);

  const normalizedOptions: UseFocusTrapOptions =
    typeof options === "boolean"
      ? { isOpen: options, onClose: onCloseCallback }
      : options;

  const {
    isOpen = true,
    onClose = onCloseCallback,
    initialFocusRef,
    restoreFocus = true,
  } = normalizedOptions;

  useEffect(() => {
    if (!isOpen) return;

    // Save previous active element to restore focus when closing
    previousFocusRef.current = document.activeElement as HTMLElement | null;

    const container = containerRef.current;
    if (!container) return;

    // Initial focus placement
    const timer = setTimeout(() => {
      if (initialFocusRef?.current) {
        initialFocusRef.current.focus();
      } else {
        const focusableElements = container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR);
        if (focusableElements.length > 0) {
          focusableElements[0].focus();
        } else if (container.getAttribute("tabIndex") !== null) {
          container.focus();
        }
      }
    }, 10);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" || event.key === "Esc") {
        if (onClose) {
          event.stopPropagation();
          onClose();
        }
        return;
      }

      if (event.key !== "Tab") return;

      const currentContainer = containerRef.current;
      if (!currentContainer) return;

      const focusable = Array.from(
        currentContainer.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((el) => {
        // Only include elements that are visible and not hidden
        return el.offsetParent !== null && !el.hasAttribute("disabled") && el.getAttribute("aria-hidden") !== "true";
      });

      if (focusable.length === 0) {
        event.preventDefault();
        return;
      }

      const firstElement = focusable[0];
      const lastElement = focusable[focusable.length - 1];

      if (event.shiftKey) {
        if (
          document.activeElement === firstElement ||
          !currentContainer.contains(document.activeElement)
        ) {
          event.preventDefault();
          lastElement.focus();
        }
      } else {
        if (
          document.activeElement === lastElement ||
          !currentContainer.contains(document.activeElement)
        ) {
          event.preventDefault();
          firstElement.focus();
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown);

    return () => {
      clearTimeout(timer);
      window.removeEventListener("keydown", handleKeyDown);
      if (restoreFocus && previousFocusRef.current && typeof previousFocusRef.current.focus === "function") {
        try {
          previousFocusRef.current.focus();
        } catch {
          // Ignore if previous element is no longer attached
        }
      }
    };
  }, [isOpen, onClose, initialFocusRef, restoreFocus]);

  return containerRef;
}

export default useFocusTrap;
