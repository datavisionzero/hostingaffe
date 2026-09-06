/**
 * What the toolbar of a Markdown field does to a piece of text, as functions
 * over a selection rather than as commands of an editor.
 *
 * They live apart from the editor on purpose. The editor arrives behind a
 * dynamic import (ADR 0006: the shell ships first), so for the moment before
 * it is there the field is a plain text area — and the same button has to do
 * the same thing in both. Written this way, it is one implementation and one
 * set of tests, and neither depends on CodeMirror being loaded.
 */

/** A piece of text with something selected in it; `from === to` is a caret. */
export type Selected = { text: string; from: number; to: number };

/**
 * Wrap the selection in a mark, or take the mark off when it is already there
 * — the same button both ways, which is what a person pressing ⌘B twice means.
 * With nothing selected it opens the pair and puts the caret between them.
 */
export function wrapped(at: Selected, mark: string): Selected {
  const inside = at.text.slice(at.from, at.to);
  const before = at.text.slice(0, at.from);
  const after = at.text.slice(at.to);

  if (inside.startsWith(mark) && inside.endsWith(mark) && inside.length >= 2 * mark.length) {
    const bare = inside.slice(mark.length, inside.length - mark.length);
    return { text: before + bare + after, from: at.from, to: at.from + bare.length };
  }

  // The mark may sit just outside the selection — a word selected by
  // double-click inside `**bold**` is the usual way there.
  if (before.endsWith(mark) && after.startsWith(mark)) {
    const from = at.from - mark.length;
    return {
      text: before.slice(0, from) + inside + after.slice(mark.length),
      from,
      to: from + inside.length,
    };
  }

  return {
    text: before + mark + inside + mark + after,
    from: at.from + mark.length,
    to: at.to + mark.length,
  };
}

/**
 * Put a mark in front of every line the selection touches, or take it off
 * every line when they all carry it already. Quote, list and heading are the
 * same act with a different word.
 */
export function prefixed(at: Selected, mark: string): Selected {
  const from = lineStart(at.text, at.from);
  const to = lineEnd(at.text, at.to);
  const lines = at.text.slice(from, to).split("\n");
  const carried = lines.every((line) => line.startsWith(mark));
  const changed = lines.map((line) => (carried ? line.slice(mark.length) : mark + line));
  const moved = (carried ? -1 : 1) * mark.length;

  return {
    text: at.text.slice(0, from) + changed.join("\n") + at.text.slice(to),
    // The first line moves the caret with it; the rest move the end of the
    // selection, so what was selected stays selected.
    from: Math.max(from, at.from + moved),
    to: at.to + moved * lines.length,
  };
}

/**
 * A link out of what is selected. Selected text becomes the label and the
 * caret lands where the address goes, which is the way round a person means
 * it: they marked the words first. A selection that is itself a URL becomes
 * the address instead, with the caret in the empty label.
 */
export function linked(at: Selected): Selected {
  const inside = at.text.slice(at.from, at.to);
  const before = at.text.slice(0, at.from);
  const after = at.text.slice(at.to);

  if (isUrl(inside)) {
    const made = `[](${inside})`;
    return { text: before + made + after, from: at.from + 1, to: at.from + 1 };
  }

  const made = `[${inside}]()`;
  const caret = at.from + made.length - 1;
  return { text: before + made + after, from: caret, to: caret };
}

/**
 * Only the two schemes a link in this product may carry anyway (ADR 0007), and
 * only when it is one word.
 */
function isUrl(text: string): boolean {
  return /^https?:\/\/\S+$/.test(text.trim()) && !/\s/.test(text.trim());
}

function lineStart(text: string, at: number): number {
  return text.lastIndexOf("\n", Math.max(0, at - 1)) + 1;
}

function lineEnd(text: string, at: number): number {
  const next = text.indexOf("\n", at);
  return next === -1 ? text.length : next;
}
