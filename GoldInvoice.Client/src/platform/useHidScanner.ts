import { useEffect, useRef } from "react";

export interface HidScannerOptions {
  enabled?: boolean;
  minLength?: number;
  maxLength?: number;
  maxInterKeyDelayMs?: number;
  resetAfterMs?: number;
  onScan(code: string): void;
}

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;

  const tagName = target.tagName.toLowerCase();
  return tagName === "input" || tagName === "textarea" || tagName === "select";
}

/**
 * Captures USB QR/barcode scanners configured as HID Keyboard Wedge devices.
 *
 * Expected scanner setup: decoded text followed by Enter or Tab. Human typing
 * in editable controls is ignored and slow key sequences are discarded.
 */
export function useHidScanner({
  enabled = true,
  minLength = 3,
  maxLength = 128,
  maxInterKeyDelayMs = 80,
  resetAfterMs = 250,
  onScan,
}: HidScannerOptions): void {
  const callbackRef = useRef(onScan);

  useEffect(() => {
    callbackRef.current = onScan;
  }, [onScan]);

  useEffect(() => {
    if (!enabled) return undefined;

    let buffer = "";
    let lastKeyAt = 0;
    let resetTimer: number | undefined;

    const reset = () => {
      buffer = "";
      lastKeyAt = 0;
      if (resetTimer !== undefined) {
        window.clearTimeout(resetTimer);
        resetTimer = undefined;
      }
    };

    const scheduleReset = () => {
      if (resetTimer !== undefined) window.clearTimeout(resetTimer);
      resetTimer = window.setTimeout(reset, resetAfterMs);
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (
        event.defaultPrevented ||
        event.repeat ||
        event.ctrlKey ||
        event.altKey ||
        event.metaKey ||
        isEditableTarget(event.target)
      ) {
        return;
      }

      const now = performance.now();

      if (event.key === "Enter" || event.key === "Tab") {
        const code = buffer.trim();
        reset();

        if (code.length < minLength) return;

        event.preventDefault();
        callbackRef.current(code);
        return;
      }

      if (event.key.length !== 1) return;

      if (lastKeyAt > 0 && now - lastKeyAt > maxInterKeyDelayMs) {
        buffer = "";
      }

      buffer += event.key;
      if (buffer.length > maxLength) {
        buffer = buffer.slice(-maxLength);
      }

      lastKeyAt = now;
      scheduleReset();
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => {
      window.removeEventListener("keydown", handleKeyDown, true);
      reset();
    };
  }, [enabled, maxInterKeyDelayMs, maxLength, minLength, resetAfterMs]);
}
