import { useState, useEffect, useRef, useCallback } from "react";

export function InfoRow({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="detail-panel-row">
      <span className="detail-panel-label">{label}</span>
      <span className={`detail-panel-value${mono ? " mono" : ""}`}>
        {value}
      </span>
    </div>
  );
}

export function PanelLoading({ children }: { children: string }) {
  return <div className="detail-panel-loading">{children}</div>;
}

export function PanelEmpty({ children }: { children: string }) {
  return <div className="detail-panel-empty">{children}</div>;
}

export function usePanelHeight() {
  const [height, setHeight] = useState(() => {
    const stored = localStorage.getItem("seedarr-detail-height");
    return stored ? parseInt(stored, 10) : 280;
  });
  const panelRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ startY: number; startH: number } | null>(null);
  const dragListenersRef = useRef<{
    move: (e: MouseEvent) => void;
    up: (e: MouseEvent) => void;
  } | null>(null);

  useEffect(() => {
    return () => {
      if (dragListenersRef.current) {
        document.removeEventListener(
          "mousemove",
          dragListenersRef.current.move,
        );
        document.removeEventListener("mouseup", dragListenersRef.current.up);
        dragListenersRef.current = null;
      }
    };
  }, []);

  const onMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      dragRef.current = { startY: e.clientY, startH: height };

      const onMouseMove = (ev: MouseEvent) => {
        if (!dragRef.current) return;
        const delta = dragRef.current.startY - ev.clientY;
        const newH = Math.max(
          120,
          Math.min(window.innerHeight - 200, dragRef.current.startH + delta),
        );
        setHeight(newH);
      };

      const onMouseUp = () => {
        document.removeEventListener("mousemove", onMouseMove);
        document.removeEventListener("mouseup", onMouseUp);
        dragListenersRef.current = null;
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
        if (dragRef.current) {
          const finalH = panelRef.current?.offsetHeight ?? height;
          localStorage.setItem("seedarr-detail-height", String(finalH));
        }
        dragRef.current = null;
      };

      dragListenersRef.current = { move: onMouseMove, up: onMouseUp };
      document.body.style.cursor = "row-resize";
      document.body.style.userSelect = "none";
      document.addEventListener("mousemove", onMouseMove);
      document.addEventListener("mouseup", onMouseUp);
    },
    [height],
  );

  return { height, panelRef, onMouseDown };
}
