import { moment } from "@/shared/when";

/**
 * One field of one act, as both the history of a detail page and the reading at
 * `/history` carry it.
 */
type Change = { field: string; old_value: string | null; new_value: string | null };

/**
 * An RFC 3339 moment and nothing looser.
 *
 * The instance writes every timestamp this way (`docs/api.md`), so a value that
 * matches is one — and a value that does not is left alone. A date is not
 * guessed out of something that merely starts with four digits.
 */
const rfc3339 = /^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})$/;

/**
 * What the two sides of a change read as on a screen: `null` where there is
 * nothing to print, and otherwise the value the way this product writes that
 * kind of value.
 *
 * The one place the rule lives, because the section of a detail page, the
 * reading and `ha history` are three views of the same line and one of them
 * saying it differently would read like another product.
 */
export function values(change: Change, subject?: string | null): { old: string | null; new: string | null } {
  return {
    old: said(change.old_value),
    new: repeats(change, subject) ? null : said(change.new_value),
  };
}

/**
 * A value as it is written, which for a moment is the local spelling every
 * other date on the screen uses. The stored row is untouched: this is the
 * reading of it and nothing else.
 */
function said(value: string | null): string | null {
  if (value === null || value === "") return null;
  if (!rfc3339.test(value)) return value;

  const spelled = moment(value);
  return spelled === "" || spelled === "Invalid Date" ? value : spelled;
}

/**
 * A birth whose new value is the address of the thing that was born: `file
 * compose.override.yml created → compose.override.yml` says the path twice, and
 * the repetition draws the eye away from the lines that say something.
 */
function repeats(change: Change, subject?: string | null): boolean {
  return change.field === "created"
    && subject !== undefined && subject !== null
    && change.new_value === subject;
}
