import { expect, it } from "vitest";
import { linked, prefixed, wrapped } from "./markdownCommands";

/**
 * What the toolbar of a Markdown field does, tested where it is decided. The
 * editor these run inside is CodeMirror and needs a browser; the rules
 * themselves are text in and text out, and belong under a unit test rather
 * than under a rendered one.
 */

/** `a [b] c` — the brackets say what is selected and are taken out again. */
function selection(marked: string) {
  const from = marked.indexOf("[");
  const to = marked.indexOf("]") - 1;
  return { text: marked.replace("[", "").replace("]", ""), from, to };
}

/** The same shape back, so a result reads as the sentence it makes. */
function shown(made: { text: string; from: number; to: number }) {
  return `${made.text.slice(0, made.from)}[${made.text.slice(made.from, made.to)}]${made.text.slice(made.to)}`;
}

it("wraps what is selected, and unwraps it when the mark is already there", () => {
  expect(shown(wrapped(selection("say [this] out loud"), "**"))).toBe("say **[this]** out loud");

  // The same button again takes it off, which is what pressing it twice means.
  expect(shown(wrapped(selection("say [**this**] out loud"), "**"))).toBe("say [this] out loud");

  // A word selected by double-click inside `**bold**` has the marks just
  // outside it, and the button has to see them there too.
  expect(shown(wrapped(selection("say **[this]** out loud"), "**"))).toBe("say [this] out loud");
});

it("opens the pair around the caret when nothing is selected", () => {
  const made = wrapped({ text: "say ", from: 4, to: 4 }, "`");

  expect(made.text).toBe("say ``");
  expect([made.from, made.to]).toEqual([5, 5]);
});

it("marks every line the selection touches, and unmarks them when they all carry it", () => {
  expect(shown(prefixed(selection("one\nt[w]o\nthree"), "- "))).toBe("one\n- t[w]o\nthree");

  const both = prefixed(selection("[one\ntwo]\nthree"), "> ");
  expect(both.text).toBe("> one\n> two\nthree");
  expect(shown(prefixed({ ...both, from: 0, to: both.text.indexOf("\nthree") }, "> "))).toBe("[one\ntwo]\nthree");
});

it("marks the line the caret is on even where nothing is selected", () => {
  expect(prefixed({ text: "a title", from: 3, to: 3 }, "## ").text).toBe("## a title");
});

it("makes the selected words the label of a link and leaves the caret in the address", () => {
  const made = linked(selection("read [the vision] first"));

  expect(made.text).toBe("read [the vision]() first");
  expect(made.from).toBe(made.text.indexOf("()") + 1);
  expect(made.to).toBe(made.from);
});

it("makes a selected URL the address instead, with the caret in the empty label", () => {
  const made = linked(selection("[https://example.org/x]"));

  expect(made.text).toBe("[](https://example.org/x)");
  expect([made.from, made.to]).toEqual([1, 1]);
});

it("is not fooled by prose that begins with a scheme", () => {
  expect(linked(selection("[https://example.org and more]")).text).toBe(
    "[https://example.org and more]()",
  );
});
