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

/**
 * How long ago something happened, as a person reads it: "12 minutes ago",
 * "6 days ago".
 *
 * There is no threshold in it and no word of judgement — not "stale", not
 * "silent". The relative timestamp carries the information and the reader
 * judges; a line the product drew would be the wrong one for the next host
 * (VISION 5).
 */
export function ago(value: string | null | undefined, now: Date = new Date()): string {
  if (value === null || value === undefined || value === "") return "";

  const seconds = Math.round((new Date(value).getTime() - now.getTime()) / 1000);
  const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ["year", 31_536_000], ["month", 2_592_000], ["day", 86_400],
    ["hour", 3_600], ["minute", 60],
  ];

  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return relative.format(Math.round(seconds / size), unit);
  }

  return relative.format(0, "minute");
}
