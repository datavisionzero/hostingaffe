import { useEffect, useId, useRef } from "react";
import { is } from "@/shell/shortcuts";
import type { Selected } from "./markdownCommands";

/** The plain text area every test writes into in place of CodeMirror. */
export default function StandInEditor({ value, onChange, onSubmit, onReady, label, hint, autoFocus, minHeight }: {
  value: string;
  onChange: (value: string) => void;
  onSubmit?: () => void;
  onReady?: (handle: { apply: (command: (at: Selected) => Selected) => void; focus: () => void } | null) => void;
  label: string;
  hint?: string;
  autoFocus?: boolean;
  minHeight: string;
}) {
  const id = useId();
  const area = useRef<HTMLTextAreaElement>(null);
  const latest = useRef(onChange);

  useEffect(() => {
    latest.current = onChange;
  });

  useEffect(() => {
    onReady?.({
      apply: (command) => {
        const it = area.current!;
        latest.current(command({ text: it.value, from: it.selectionStart, to: it.selectionEnd }).text);
      },
      focus: () => area.current?.focus(),
    });

    return () => onReady?.(null);
  }, [onReady]);

  return (
    <textarea
      id={id}
      ref={area}
      aria-label={label}
      placeholder={hint}
      autoFocus={autoFocus}
      value={value}
      style={{ minHeight }}
      onChange={(event) => onChange(event.target.value)}
      onKeyDown={(event) => {
        if (onSubmit !== undefined && is("form:submit", event)) {
          event.preventDefault();
          onSubmit();
        }
      }}
    />
  );
}

