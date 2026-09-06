import { cn } from "@/lib/utils";
import { priorityLabel } from "./priorityLabel";

/*
 * Four bars of rising height, of which as many are lit as the step is high:
 * none leaves them all dark, urgent lights all four. A quantity reads as a
 * scale without the digit being deciphered, which `P0` to `P4` never did — one
 * character apart, equally loud, and `none` is the default and so has the most
 * rows.
 *
 * Colour is spent once, on urgent, and it is `--destructive` rather than a
 * token of its own: the vocabulary is closed (`index.css`), red is not one of
 * the status hues two columns to the left, and what it means there — something
 * wants attention — is what it means here.
 */
const steps = ["h-1", "h-1.5", "h-2", "h-2.5"];

/** The priority as a mark and a word, the same everywhere an issue is listed. */
export function PriorityMark({ priority, withLabel = false }: { priority: number; withLabel?: boolean }) {
  const said = `Priority: ${priorityLabel(priority)}`;
  const lit = priority >= 4 ? "bg-destructive" : "bg-foreground/55";

  return (
    <span className="inline-flex items-center gap-1.5" title={said}>
      <span aria-hidden className="inline-flex h-2.5 items-end gap-px">
        {steps.map((height, step) => (
          <span key={height} className={cn("w-0.5 rounded-xs", height, step < priority ? lit : "bg-foreground/15")} />
        ))}
      </span>
      {withLabel && <span className="text-xs text-muted-foreground">{priorityLabel(priority)}</span>}
      {!withLabel && <span className="sr-only">{said}</span>}
    </span>
  );
}
