/**
 * The five steps of the fixed scale (VISION 8), as the word each one is. `ha`
 * prints `P0` to `P4` because a column of two characters is what a terminal
 * has; a screen has room for the word, and the word is what means something
 * read out.
 */
const words = ["none", "low", "medium", "high", "urgent"] as const;

export function priorityLabel(priority: number): string {
  return words[priority] ?? `P${priority}`;
}
