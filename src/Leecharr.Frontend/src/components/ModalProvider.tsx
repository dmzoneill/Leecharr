import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useRef,
  useEffect,
} from "react";

export interface ModalInstance {
  id: string;
  onClose: () => void;
  modalRef?: React.RefObject<HTMLElement | null>;
  triggerElement?: HTMLElement | null;
}

export interface ModalContextValue {
  registerModal: (instance: ModalInstance) => () => void;
  unregisterModal: (id: string) => void;
  topModalId: string | null;
  isTopmost: (id: string) => boolean;
  modalCount: number;
}

const defaultContext: ModalContextValue = {
  registerModal: () => () => {},
  unregisterModal: () => {},
  topModalId: null,
  isTopmost: () => true,
  modalCount: 0,
};

const ModalContext = createContext<ModalContextValue>(defaultContext);

export function ModalProvider({ children }: { children: React.ReactNode }) {
  const [stack, setStack] = useState<ModalInstance[]>([]);
  const stackRef = useRef<ModalInstance[]>([]);
  stackRef.current = stack;

  const unregisterModal = useCallback((id: string) => {
    setStack((prev) => {
      const idx = prev.findIndex((m) => m.id === id);
      if (idx === -1) return prev;
      const removed = prev[idx];
      const next = prev.filter((m) => m.id !== id);
      stackRef.current = next;

      // Restore focus to triggerElement if this modal had one and was closed
      if (
        removed.triggerElement &&
        typeof removed.triggerElement.focus === "function" &&
        document.contains(removed.triggerElement)
      ) {
        requestAnimationFrame(() => {
          try {
            removed.triggerElement?.focus();
          } catch {
            // Ignore if element is no longer focusable
          }
        });
      }

      return next;
    });
  }, []);

  const registerModal = useCallback(
    (instance: ModalInstance) => {
      setStack((prev) => {
        // Remove existing instance with the same id if already present, then push to top
        const filtered = prev.filter((m) => m.id !== instance.id);
        const next = [...filtered, instance];
        stackRef.current = next;
        return next;
      });

      return () => {
        unregisterModal(instance.id);
      };
    },
    [unregisterModal],
  );

  const topModal = stack.length > 0 ? stack[stack.length - 1] : null;
  const topModalId = topModal ? topModal.id : null;

  const isTopmost = useCallback((id: string) => {
    return (
      stackRef.current.length > 0 &&
      stackRef.current[stackRef.current.length - 1].id === id
    );
  }, []);

  // Global KeyDown listener with capture to handle Escape hierarchy and Tab focus trapping
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const currentStack = stackRef.current;
      const hasRegisteredModals = currentStack.length > 0;
      const modalOverlays =
        typeof document !== "undefined"
          ? Array.from(
              document.querySelectorAll<HTMLElement>(
                'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay',
              ),
            )
          : [];
      const hasDomModals = modalOverlays.length > 0;

      if (!hasRegisteredModals && !hasDomModals) return;

      const top = hasRegisteredModals
        ? currentStack[currentStack.length - 1]
        : null;

      // Handle Escape: Dismiss ONLY the topmost modal
      if (e.key === "Escape") {
        if (top) {
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();
          top.onClose();
          return;
        }
      }

      // Handle Tab: Trap focus inside the topmost modal
      if (e.key === "Tab") {
        const topmostOverlay =
          modalOverlays.length > 0
            ? modalOverlays[modalOverlays.length - 1]
            : null;

        const container =
          top?.modalRef?.current ??
          topmostOverlay ??
          (document.querySelector(`[role="dialog"]`) as HTMLElement | null);

        if (!container) return;

        const focusableSelectors = [
          "a[href]",
          "area[href]",
          'input:not([disabled]):not([type="hidden"])',
          "select:not([disabled])",
          "textarea:not([disabled])",
          "button:not([disabled])",
          "iframe",
          "object",
          "embed",
          "[contenteditable]",
          '[tabindex]:not([tabindex="-1"])',
        ].join(",");

        const focusables = Array.from(
          container.querySelectorAll<HTMLElement>(focusableSelectors),
        ).filter((el) => {
          return (
            el.offsetWidth > 0 ||
            el.offsetHeight > 0 ||
            el.getClientRects().length > 0
          );
        });

        if (focusables.length === 0) {
          e.preventDefault();
          if (container.tabIndex < 0) {
            container.setAttribute("tabindex", "-1");
          }
          container.focus?.();
          return;
        }

        const first = focusables[0];
        const last = focusables[focusables.length - 1];
        const active = document.activeElement;

        if (e.shiftKey) {
          if (active === first || !container.contains(active)) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (active === last || !container.contains(active)) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, []);

  const value: ModalContextValue = {
    registerModal,
    unregisterModal,
    topModalId,
    isTopmost,
    modalCount: stack.length,
  };

  return (
    <ModalContext.Provider value={value}>{children}</ModalContext.Provider>
  );
}

export function useModalStack(): ModalContextValue {
  return useContext(ModalContext);
}

export interface UseModalOptions {
  id: string;
  isOpen?: boolean;
  onClose: () => void;
  modalRef?: React.RefObject<HTMLElement | null>;
  triggerElement?: HTMLElement | null;
}

export function useModalRegistration({
  id,
  isOpen = true,
  onClose,
  modalRef,
  triggerElement,
}: UseModalOptions) {
  const { registerModal, isTopmost } = useModalStack();
  const triggerRef = useRef<HTMLElement | null>(triggerElement ?? null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    if (!isOpen) return;

    // Capture the trigger element right when the modal opens if not explicitly passed
    const trigger =
      triggerRef.current ??
      triggerElement ??
      (document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null);

    const unregister = registerModal({
      id,
      onClose: () => onCloseRef.current(),
      modalRef,
      triggerElement: trigger,
    });

    return unregister;
  }, [id, isOpen, modalRef, triggerElement, registerModal]);

  useEffect(() => {
    if (!isOpen) return;

    const timer = setTimeout(() => {
      if (
        modalRef?.current &&
        !modalRef.current.contains(document.activeElement)
      ) {
        const firstFocusable = modalRef.current.querySelector<HTMLElement>(
          'input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])',
        );
        firstFocusable?.focus();
      }
    }, 50);

    return () => clearTimeout(timer);
  }, [isOpen, modalRef]);

  return {
    isTopmost: isTopmost(id),
  };
}

export const useModal = useModalRegistration;
export default ModalProvider;
