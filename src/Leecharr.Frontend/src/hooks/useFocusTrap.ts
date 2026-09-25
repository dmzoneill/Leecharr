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
  const wasOpenRef = useRef<boolean>(false);

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

  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  const restoreFocusRef = useRef(restoreFocus);
  restoreFocusRef.current = restoreFocus;

  const initialFocusRefRef = useRef(initialFocusRef);
  initialFocusRefRef.current = initialFocusRef;

  useEffect(() => {
    if (!isOpen) {
      if (wasOpenRef.current) {
        wasOpenRef.current = false;
        if (
          restoreFocusRef.current &&
          previousFocusRef.current &&
          typeof previousFocusRef.current.focus === "function"
        ) {
          try {
            previousFocusRef.current.focus();
          } catch {
            // Ignore if previous element is no longer attached
          }
          previousFocusRef.current = null;
        }
      }
      return;
    }

    // Modal is opening: save previous active element once
    if (!wasOpenRef.current) {
      wasOpenRef.current = true;
      if (
        typeof document !== "undefined" &&
        document.activeElement instanceof HTMLElement
      ) {
        previousFocusRef.current = document.activeElement;
      }
    }

    const container = containerRef.current;
    if (!container) return;

    // Initial focus placement only when opening, not when focus is already inside container
    const timer = setTimeout(() => {
      if (!containerRef.current) return;
      const currentContainer = containerRef.current;

      // If document.activeElement is ALREADY inside the container, DO NOT steal focus
      if (
        document.activeElement &&
        currentContainer.contains(document.activeElement) &&
        document.activeElement !== currentContainer &&
        document.activeElement !== document.body
      ) {
        return;
      }

      if (initialFocusRefRef.current?.current) {
        initialFocusRefRef.current.current.focus();
        return;
      }

      const autoFocusEl =
        currentContainer.querySelector<HTMLElement>("[autofocus]");
      if (autoFocusEl && typeof autoFocusEl.focus === "function") {
        autoFocusEl.focus();
        return;
      }

      const focusableElements = Array.from(
        currentContainer.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((el) => {
        return (
          el.offsetParent !== null &&
          !el.hasAttribute("disabled") &&
          el.getAttribute("aria-hidden") !== "true"
        );
      });

      if (focusableElements.length > 0) {
        focusableElements[0].focus();
      } else if (currentContainer.getAttribute("tabIndex") !== null) {
        currentContainer.focus();
      }
    }, 10);

    const handleKeyDown = (event: KeyboardEvent) => {
      const currentContainer = containerRef.current;
      if (!currentContainer) return;

      const myModal =
        currentContainer.closest<HTMLElement>(
          'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay, .modal-backdrop',
        ) || currentContainer;

      const activeEl = document.activeElement as HTMLElement | null;
      const activeModal = activeEl?.closest<HTMLElement>(
        'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay, .modal-backdrop',
      );

      const openModals = Array.from(
        document.querySelectorAll<HTMLElement>(
          'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay, .modal-backdrop',
        ),
      );

      if (event.key === "Escape" || event.key === "Esc") {
        // If active element is inside another child modal, let that modal handle Escape
        if (activeModal && activeModal !== myModal) {
          return;
        }
        // If this modal is not the topmost modal in DOM, do not intercept
        if (openModals.length > 0) {
          const topModal = openModals[openModals.length - 1];
          if (
            topModal &&
            topModal !== myModal &&
            !topModal.contains(currentContainer)
          ) {
            return;
          }
        }
        if (onCloseRef.current) {
          event.stopPropagation();
          event.stopImmediatePropagation();
          onCloseRef.current();
        }
        return;
      }

      if (event.key !== "Tab") return;

      // Ensure Tab does not trap if activeElement is inside another child/topmost modal
      if (activeModal && activeModal !== myModal) {
        return;
      }
      if (openModals.length > 0) {
        const topModal = openModals[openModals.length - 1];
        if (
          topModal &&
          topModal !== myModal &&
          !topModal.contains(currentContainer)
        ) {
          return;
        }
      }

      const focusable = Array.from(
        currentContainer.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((el) => {
        // Only include elements that are visible and not hidden
        return (
          el.offsetParent !== null &&
          !el.hasAttribute("disabled") &&
          el.getAttribute("aria-hidden") !== "true"
        );
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
    };
  }, [isOpen]);

  // Unmount cleanup: restore focus if modal is unmounted while still open
  useEffect(() => {
    return () => {
      if (
        wasOpenRef.current &&
        restoreFocusRef.current &&
        previousFocusRef.current &&
        typeof previousFocusRef.current.focus === "function"
      ) {
        try {
          previousFocusRef.current.focus();
        } catch {
          // Ignore if previous element is no longer attached
        }
        previousFocusRef.current = null;
      }
    };
  }, []);

  return containerRef;
}

export default useFocusTrap;
