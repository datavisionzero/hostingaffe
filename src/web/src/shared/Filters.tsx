import type { SetURLSearchParams } from "react-router";
import { cn } from "@/lib/utils";

/**
 * One field a list can be narrowed by, and the values it admits — which are the
 * closed sets of VISION 7 and are therefore known here rather than asked for.
 */
export type Filter = { name: string; label: string; values: string[] };

/**
 * The narrowing above a list, written into the address bar.
 *
 * Filters live in the URL and not in component state because a filtered list is
 * something people send each other: "the retired VPSes" is a link, and a
 * reload, a bookmark and the back button all have to mean what the screen
 * showed. `replace` keeps the history free of one entry per click.
 *
 * One value per field, because that is what the endpoint accepts: `status` and
 * `kind` are read as a single word (`docs/api.md`). Clicking the value that is
 * already on takes it off again, which is the only way back to everything
 * without reaching for the address bar.
 */
export function Filters({ filters, params, setParams, also }: {
  filters: Filter[];
  params: URLSearchParams;
  setParams: SetURLSearchParams;
  /** A switch beside them, where the list has one: retired rows, deleted rows. */
  also?: { name: string; label: string; on: boolean };
}) {
  const set = (name: string, value: string | undefined) => {
    const kept = new URLSearchParams(params);
    if (value === undefined) kept.delete(name);
    else kept.set(name, value);
    setParams(kept, { replace: true });
  };

  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-2 border-b px-4 py-2">
      {filters.map((filter) => {
        const chosen = params.get(filter.name);

        return (
          <fieldset key={filter.name} className="flex flex-wrap items-center gap-2">
            <legend className="sr-only">{filter.label}</legend>
            <span aria-hidden className="text-xs font-medium text-muted-foreground">{filter.label}</span>
            {filter.values.map((value) => {
              const on = chosen === value;

              return (
                <button
                  key={value}
                  type="button"
                  aria-pressed={on}
                  onClick={() => set(filter.name, on ? undefined : value)}
                  className={cn(
                    "rounded-4xl border px-2 py-0.5 text-xs transition-colors",
                    on ? "border-transparent bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-accent",
                  )}
                >
                  {value}
                </button>
              );
            })}
          </fieldset>
        );
      })}

      {also !== undefined && (
        <label className="flex items-center gap-2 text-xs text-muted-foreground">
          <input
            type="checkbox"
            name={also.name}
            checked={also.on}
            onChange={(event) => set(also.name, event.target.checked ? "yes" : undefined)}
            className="size-3.5 accent-brand"
          />
          {also.label}
        </label>
      )}
    </div>
  );
}
