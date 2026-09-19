import { useId } from "react";
import type { SetURLSearchParams } from "react-router";
import { Input } from "@/components/ui/input";
import { Picker, type Choice } from "@/components/ui/picker";
import { cn } from "@/lib/utils";
import { word } from "./narrowing";

/**
 * One field a list can be narrowed by, and the values it admits — which are the
 * closed sets of VISION 7 and are therefore known here rather than asked for.
 */
export type Filter = { name: string; label: string; values: string[] };

/** A switch beside the filters: one question with a yes and nothing else. */
export type Switch = { name: string; label: string; on: boolean };

/**
 * A word typed to narrow a list, written to `q`.
 *
 * It is the one narrowing whose values are not a set of anything: what is
 * looked for is the name somebody gave the thing, and the only way to say it is
 * to type it. What `q` reaches differs by list — the pages search the body of a
 * page at the endpoint, the record's lists match key and name in the browser —
 * so the field is drawn here and answered there.
 */
export type Find = { label: string; placeholder: string; value: string };

/**
 * One thing of the record chosen to narrow by — the machine, where the values
 * are open and as long as the record is, so there are no chips to draw.
 */
export type Pick = {
  name: string;
  label: string;
  placeholder?: string;
  choices: Choice[];
  /** What an empty list says: that nothing was found, or that it is still coming. */
  empty?: string;
};

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
 *
 * It is drawn in two rows, and they are two because they are read differently:
 * what is typed or chosen stands above, where a reader looks for a field, and
 * the closed sets stand below as the chips they are.
 */
export function Filters({ filters, params, setParams, find, pick, also, defaults }: {
  filters: Filter[];
  params: URLSearchParams;
  setParams: SetURLSearchParams;
  /** The word this list is narrowed by, where it has one. */
  find?: Find;
  /** The thing of the record this list is narrowed by, where it has one. */
  pick?: Pick;
  /**
   * The switches beside them, where the list has any: retired rows, an order
   * other than the default. One or several, because a list may want both.
   */
  also?: Switch | Switch[];
  /**
   * What the list narrows to when the address carries nothing of its own
   * (`usePreset`). A field that has one is never deleted but emptied, because
   * `?status=` is how a reader says "any status" in a way that survives a
   * reload and a pasted link — a deleted field would read as an address that
   * asked for nothing and get the preset back.
   */
  defaults?: Record<string, string>;
}) {
  const switches = also === undefined ? [] : Array.isArray(also) ? also : [also];
  const id = useId();
  const set = (name: string, value: string | undefined) => {
    const kept = new URLSearchParams(params);
    if (value !== undefined) kept.set(name, value);
    else if (defaults?.[name] !== undefined) kept.set(name, "");
    else kept.delete(name);
    setParams(kept, { replace: true });
  };

  return (
    <>
      {(find !== undefined || pick !== undefined) && (
        <div className="flex flex-col gap-2 border-b px-4 py-2 sm:flex-row sm:items-start">
          {find !== undefined && (
            <div className="grid min-w-0 flex-1 gap-1 text-sm font-medium">
              <label htmlFor={`${id}-find`}>{find.label}</label>
              <Input
                id={`${id}-find`}
                name="q"
                type="search"
                placeholder={find.placeholder}
                value={find.value}
                onChange={(event) => set("q", event.target.value === "" ? undefined : event.target.value)}
              />
            </div>
          )}
          {pick !== undefined && (
            <Picker
              className="sm:w-72"
              label={pick.label}
              placeholder={pick.placeholder}
              empty={pick.empty}
              choices={pick.choices}
              value={word(params, pick.name) === undefined ? [] : [params.get(pick.name)!]}
              onChange={(ids) => set(pick.name, ids[0])}
            />
          )}
        </div>
      )}

      {(filters.length > 0 || switches.length > 0) && (
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

          {switches.map((one) => (
            <label key={one.name} className="flex items-center gap-2 text-xs text-muted-foreground">
              <input
                type="checkbox"
                name={one.name}
                checked={one.on}
                onChange={(event) => set(one.name, event.target.checked ? "yes" : undefined)}
                className="size-3.5 accent-brand"
              />
              {one.label}
            </label>
          ))}
        </div>
      )}
    </>
  );
}
