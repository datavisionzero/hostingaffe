import { useEffect } from "react";
import type { SetURLSearchParams } from "react-router";

/**
 * Reading and writing the narrowing a list carries in its address, beside the
 * control that draws it (`Filters`).
 */

/**
 * One narrowing as the endpoint reads it: a word, or nothing.
 *
 * An empty value is nothing. That is what a field with a preset is left as when
 * it is switched off, and reading it as a word would ask the endpoint for
 * installations whose environment is the empty string.
 */
export function word(params: URLSearchParams, name: string): string | undefined {
  const value = params.get(name);

  return value === null || value === "" ? undefined : value;
}

/**
 * Whether a row answers what was typed — a part of any of its words, case
 * ignored, which is what "filter by name" means to somebody who remembers half
 * of one.
 */
export function found(needle: string, ...words: (string | null | undefined)[]): boolean {
  const wanted = needle.trim().toLowerCase();

  return wanted === "" || words.some((word) => (word ?? "").toLowerCase().includes(wanted));
}

/**
 * The narrowing a list starts with when the address carries none of its own,
 * and the address it then has.
 *
 * A preset is written into the URL rather than kept in the component, for the
 * reason every filter here is: what the screen shows has to be what a pasted
 * link shows. It is applied only to an address that names no narrowing at all —
 * a link from the machine screen carries `machine` and means every installation
 * there, not every active production one.
 */
export function usePreset(
  params: URLSearchParams,
  setParams: SetURLSearchParams,
  narrowers: string[],
  defaults: Record<string, string>,
): URLSearchParams {
  const bare = !narrowers.some((name) => params.has(name));

  useEffect(() => {
    if (bare) setParams(withPreset(params, defaults), { replace: true });
  }, [bare, params, setParams, defaults]);

  // The list is drawn with the preset in the same paint as the address is
  // rewritten, so that it never shows everything for one frame first.
  return bare ? withPreset(params, defaults) : params;
}

function withPreset(params: URLSearchParams, defaults: Record<string, string>): URLSearchParams {
  const kept = new URLSearchParams(params);

  for (const [name, value] of Object.entries(defaults)) kept.set(name, value);

  return kept;
}
