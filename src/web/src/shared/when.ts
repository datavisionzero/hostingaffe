/**
 * How a moment is spelled on a screen, in one place.
 *
 * The instance carries every timestamp as RFC 3339 in UTC (`docs/api.md`); what
 * a reader wants is their own clock, and two screens that disagreed about that
 * would be two products. `""` for nothing, so that a field list can leave the
 * row out rather than print a dash.
 */
/** The one spelling of a moment on a screen, so that two screens do not disagree. */
export function moment(value: string | null | undefined): string {
  return value === null || value === undefined || value === "" ? "" : new Date(value).toLocaleString();
}

/** The same, without the time, where a list has room for a date and not a timestamp. */
export function day(value: string | null | undefined): string {
  return value === null || value === undefined || value === "" ? "" : new Date(value).toLocaleDateString();
}
