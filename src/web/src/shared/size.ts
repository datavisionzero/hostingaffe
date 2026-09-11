/**
 * How a file's size is spelled on a screen, in one place.
 *
 * Exactly, in bytes of UTF-8, because that is how the instance counts against
 * the one-megabyte cap (`docs/api.md`, Files): a rounded figure would answer
 * "does this still fit?" with a different number than the one a write is
 * refused against.
 */
export function size(bytes: number): string {
  return `${bytes} B`;
}
