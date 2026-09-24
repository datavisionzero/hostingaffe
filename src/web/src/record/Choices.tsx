import { useRef, type KeyboardEvent, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * A grid of choices one Tab stop wide: the chosen cell takes the focus, and
 * the arrow keys walk the rest, a row at a time up and down.
 */
export function Cells({ label, columns, className, children }: {
  label: string; columns: number; className?: string; children: ReactNode;
}) {
  const grid = useRef<HTMLDivElement>(null);

  function walk(event: KeyboardEvent<HTMLDivElement>) {
    const cells = Array.from(grid.current?.querySelectorAll<HTMLButtonElement>("button") ?? []);
    const at = cells.indexOf(document.activeElement as HTMLButtonElement);
    const step = { ArrowRight: 1, ArrowLeft: -1, ArrowDown: columns, ArrowUp: -columns }[event.key];
    if (at < 0 || step === undefined) return;
    event.preventDefault();
    cells[Math.min(cells.length - 1, Math.max(0, at + step))].focus();
  }

  return (
    <div
      ref={grid}
      role="group"
      aria-label={label}
      onKeyDown={walk}
      className={cn("grid gap-1", className)}
      style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}
    >
      {children}
    </div>
  );
}

export function Cell({ pressed, disabled, label, onPick, children }: {
  pressed: boolean; disabled: boolean; label: string; onPick: () => void; children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      aria-label={label}
      title={label}
      disabled={disabled}
      tabIndex={pressed ? 0 : -1}
      onClick={onPick}
      className={cn(
        "flex items-center justify-center rounded-md p-1 outline-none hover:bg-muted focus-visible:ring-2 focus-visible:ring-brand disabled:opacity-60",
        pressed && "bg-muted ring-2 ring-brand",
      )}
    >
      {children}
    </button>
  );
}
