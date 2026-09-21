/**
 * Safely copies text to the clipboard with fallback for non-secure HTTP contexts
 * (e.g. LAN access over http://) or environments where navigator.clipboard is unavailable.
 *
 * @param text The string to copy to the clipboard
 * @returns Promise resolving to true if copy succeeded, false otherwise
 */
export async function copyToClipboard(text: string): Promise<boolean> {
  if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Fallback below if navigator.clipboard write fails (e.g. permission denied)
    }
  }

  if (typeof document !== "undefined") {
    let textarea: HTMLTextAreaElement | null = null;
    try {
      textarea = document.createElement("textarea");
      textarea.value = text;
      // Prevent zooming or scrolling to bottom of screen in mobile / desktop browsers
      textarea.style.position = "fixed";
      textarea.style.left = "-9999px";
      textarea.style.top = "0";
      textarea.style.opacity = "0";
      textarea.setAttribute("readonly", "");

      document.body.appendChild(textarea);
      textarea.select();
      textarea.setSelectionRange(0, textarea.value.length);

      const successful = document.execCommand("copy");
      return successful;
    } catch {
      return false;
    } finally {
      if (textarea && textarea.parentNode) {
        textarea.parentNode.removeChild(textarea);
      }
    }
  }

  return false;
}
