import { defaultKeymap, history, historyKeymap } from "@codemirror/commands";
import { markdown, markdownKeymap, markdownLanguage, pasteURLAsLink } from "@codemirror/lang-markdown";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { EditorState, Prec, type Extension } from "@codemirror/state";
import { EditorView, drawSelection, keymap, placeholder } from "@codemirror/view";
import { tags } from "@lezer/highlight";
import { useEffect, useRef } from "react";
import type { Selected } from "./markdownCommands";

/**
 * The Markdown editor, and the only module in the application that knows
 * CodeMirror.
 *
 * It is loaded on demand and never with the shell (ADR 0006, ADR 0023): it
 * weighs several times what the render pipeline does, and it is wanted only on
 * the screens where somebody writes. `MarkdownField` is what everything else
 * imports; until this module has arrived, that field is a plain text area over
 * the same value.
 *
 * It edits Markdown as source. What is stored is Markdown and what is edited is
 * Markdown — ADR 0007 — so this shows the structure of the text while it is
 * typed and never becomes a WYSIWYG surface over a document model of its own.
 */

/** What the field's toolbar may ask of a mounted editor. */
export type EditorHandle = {
  /** Apply one of `markdownCommands` to the selection and keep the focus. */
  apply: (command: (at: Selected) => Selected) => void;
  focus: () => void;
};

export default function Editor({
  value,
  onChange,
  onSubmit,
  onReady,
  label,
  hint,
  autoFocus,
  minHeight,
  maxHeight,
}: {
  value: string;
  onChange: (value: string) => void;
  /** ⌘/Ctrl+Enter, so a comment can be sent without leaving the text. */
  onSubmit?: () => void;
  onReady?: (handle: EditorHandle | null) => void;
  /** The accessible name; CodeMirror writes on a `contenteditable`, which has none of its own. */
  label: string;
  hint?: string;
  autoFocus?: boolean;
  /** How tall the field starts, as a CSS length. It grows from there. */
  minHeight: string;
  /** Where the growing stops and the field scrolls instead. */
  maxHeight: string;
}) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView>(null);

  // The props the editor's own callbacks read. They are held in a ref because
  // the editor is built once: rebuilding it on every render would throw the
  // undo history and the cursor away with it.
  const now = useRef({ onChange, onSubmit });

  useEffect(() => {
    now.current = { onChange, onSubmit };
  });

  useEffect(() => {
    const state = EditorState.create({
      doc: value,
      extensions: [
        // Ours before CodeMirror's, so that a key this application binds means
        // here what it means everywhere else in it.
        Prec.highest(
          keymap.of([
            {
              key: "Mod-Enter",
              run: () => {
                if (now.current.onSubmit === undefined) {
                  return false;
                }
                now.current.onSubmit();
                return true;
              },
            },
          ]),
        ),
        history(),
        drawSelection(),
        EditorView.lineWrapping,
        markdown({ base: markdownLanguage, codeLanguages: [] }),
        syntaxHighlighting(structure),
        keymap.of([...markdownKeymap, ...historyKeymap, ...defaultKeymap]),
        pasteURLAsLink,
        placeholder(hint ?? ""),
        EditorView.contentAttributes.of({ "aria-label": label }),
        look(minHeight, maxHeight),
        EditorView.updateListener.of((update) => {
          if (update.docChanged) {
            now.current.onChange(update.state.doc.toString());
          }
        }),
      ],
    });

    const mounted = new EditorView({ state, parent: host.current! });
    view.current = mounted;

    if (autoFocus === true) {
      mounted.focus();
    }

    onReady?.({
      apply: (command) => {
        const range = mounted.state.selection.main;
        const was: Selected = { text: mounted.state.doc.toString(), from: range.from, to: range.to };
        const made = command(was);

        mounted.dispatch({
          changes: { from: 0, to: was.text.length, insert: made.text },
          selection: { anchor: made.from, head: made.to },
        });
        mounted.focus();
      },
      focus: () => mounted.focus(),
    });

    return () => {
      onReady?.(null);
      mounted.destroy();
      view.current = null;
    };
    // Built once. `value` is followed below, and the callbacks through `now`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // A value set from outside — a form reset, a draft loaded, the same text
  // opened in the full-screen twin — is written in. A value that is only what
  // was just typed is not, or the cursor would jump on every keystroke.
  useEffect(() => {
    const mounted = view.current;

    if (mounted !== null && value !== mounted.state.doc.toString()) {
      mounted.dispatch({ changes: { from: 0, to: mounted.state.doc.length, insert: value } });
    }
  }, [value]);

  return <div ref={host} className="min-w-0" />;
}

/**
 * What the structure of the text looks like while it is written: heading,
 * emphasis, link, quote and code, in the application's own tokens. It is not
 * syntax colouring of the code inside a fenced block — ADR 0017 refused that
 * for the rendered side and it is refused here too, which is why the editor
 * carries no code languages.
 */
const structure = HighlightStyle.define([
  { tag: tags.heading, class: "font-semibold text-foreground" },
  { tag: tags.strong, class: "font-semibold" },
  { tag: tags.emphasis, class: "italic" },
  { tag: tags.strikethrough, class: "line-through" },
  { tag: tags.link, class: "text-brand" },
  { tag: tags.url, class: "text-brand underline underline-offset-2" },
  { tag: tags.quote, class: "text-muted-foreground" },
  { tag: tags.monospace, class: "text-muted-foreground" },
  { tag: tags.list, class: "text-muted-foreground" },
  { tag: tags.processingInstruction, class: "text-muted-foreground" },
]);

/**
 * The editor drawn in the application's tokens rather than in a theme of its
 * own (ADR 0017): the field looks like every other field, in both colour
 * schemes, because it takes the same variables the rest of the interface does.
 */
function look(minHeight: string, maxHeight: string): Extension {
  return EditorView.theme({
    "&": {
      fontSize: "0.875rem",
      backgroundColor: "transparent",
      color: "var(--color-foreground)",
    },
    "&.cm-focused": { outline: "none" },
    ".cm-content": {
      fontFamily: "var(--font-mono)",
      padding: "0.5rem 0.75rem",
      caretColor: "var(--color-foreground)",
      minHeight,
    },
    ".cm-scroller": { lineHeight: "1.5", overflow: "auto", maxHeight },
    ".cm-line": { padding: "0" },
    ".cm-placeholder": { color: "var(--color-muted-foreground)" },
    "&.cm-focused .cm-cursor": { borderLeftColor: "var(--color-foreground)" },
    "&.cm-focused .cm-selectionBackground, ::selection": {
      backgroundColor: "color-mix(in oklab, var(--color-ring) 30%, transparent)",
    },
    ".cm-selectionBackground": {
      backgroundColor: "color-mix(in oklab, var(--color-ring) 20%, transparent)",
    },
  });
}
