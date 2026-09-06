/**
 * Every key the application binds, in one place.
 *
 * The bindings themselves used to sit in four files that knew nothing of each
 * other — the frame, the sidebar, the issue list and the palette — and the only
 * place they were written down was `docs/human-interface.md`. An overview
 * screen assembled by hand would have been the fifth copy and the first to
 * drift. So this list is the source: the handlers ask it what was pressed, and
 * the overview draws what it holds.
 */

/** The contexts the overview groups the keys into, in the order it shows them. */
export const groups = ["Global", "Issue lists", "Forms", "Command palette"] as const;

export type Group = (typeof groups)[number];

export type ShortcutId =
  | "global:palette"
  | "global:sidebar"
  | "global:projects"
  | "global:shortcuts"
  | "global:create"
  | "list:next"
  | "list:previous"
  | "list:open"
  | "list:search"
  | "list:close"
  | "form:cancel"
  | "form:submit"
  | "palette:next"
  | "palette:previous"
  | "palette:run"
  | "palette:close";

export type Shortcut = {
  id: ShortcutId;
  /** The `KeyboardEvent.key` this answers to; letters are compared without case. */
  key: string;
  /** True where ⌘ — or Ctrl away from a Mac — is held. */
  mod?: boolean;
  /** What it does, as the overview says it. */
  what: string;
  group: Group;
};

/**
 * ⌘ belongs to a Mac and Ctrl to everywhere else, and a list of shortcuts that
 * names the wrong one is worse than none. Read once: the keyboard does not
 * change under a running tab.
 */
export const modLabel =
  typeof navigator !== "undefined" && /Mac|iPhone|iPad|iPod/.test(navigator.userAgent) ? "⌘" : "Ctrl";

export const shortcuts: Shortcut[] = [
  { id: "global:palette", key: "k", mod: true, what: "Search or jump to anything", group: "Global" },
  { id: "global:projects", key: "p", what: "Switch project", group: "Global" },
  { id: "global:sidebar", key: "b", mod: true, what: "Fold the navigation", group: "Global" },
  { id: "global:shortcuts", key: "?", what: "Show this list", group: "Global" },
  // Creating belongs to the project, not to a list of it: the key answered on
  // three screens of seven and did nothing on the other four.
  { id: "global:create", key: "c", what: "New issue in this project", group: "Global" },

  { id: "list:next", key: "j", what: "Next issue", group: "Issue lists" },
  { id: "list:previous", key: "k", what: "Previous issue", group: "Issue lists" },
  { id: "list:open", key: "Enter", what: "Open the active issue", group: "Issue lists" },
  { id: "list:search", key: "/", what: "Search this list", group: "Issue lists" },
  { id: "list:close", key: "Escape", what: "Close the filters", group: "Issue lists" },

  // The same key the button beside it is: one behaviour, two ways to it.
  { id: "form:cancel", key: "Escape", what: "Leave the form, asking first if anything was written", group: "Forms" },
  // From inside the text, where leaving it first was the only way to the
  // button: a comment is written and sent without the hands moving.
  { id: "form:submit", key: "Enter", mod: true, what: "Save the text you are in", group: "Forms" },

  { id: "palette:next", key: "ArrowDown", what: "Next result", group: "Command palette" },
  { id: "palette:previous", key: "ArrowUp", what: "Previous result", group: "Command palette" },
  { id: "palette:run", key: "Enter", what: "Go where the row leads", group: "Command palette" },
  { id: "palette:close", key: "Escape", what: "Close the palette", group: "Command palette" },
];

const index = new Map(shortcuts.map((shortcut) => [shortcut.id, shortcut] as const));

export function shortcut(id: ShortcutId): Shortcut {
  // Every id in the union is in the list above; the fallback keeps a typo in a
  // handler from taking the screen down with it.
  return index.get(id) ?? { id, key: "", what: "", group: "Global" };
}

/** As much of a key event as `is` reads — a React one and a DOM one both fit. */
type Pressed = Pick<KeyboardEvent, "key" | "altKey" | "metaKey" | "ctrlKey">;

/**
 * Whether an event is that shortcut. Shift is deliberately not compared: `?`
 * needs it on an English keyboard and a different key on a German one, and what
 * arrived is the character either way.
 */
export function is(id: ShortcutId, event: Pressed): boolean {
  const wanted = shortcut(id);

  if (wanted.key === "" || event.altKey) {
    return false;
  }

  if ((event.metaKey || event.ctrlKey) !== (wanted.mod === true)) {
    return false;
  }

  return event.key.toLowerCase() === wanted.key.toLowerCase();
}

/** A bare key belongs to whatever is being typed into, and never to the frame. */
export function typing(event: KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null;
  return target?.matches("input, textarea, select, [contenteditable=true]") === true;
}

/** A menu or a dialog has the keyboard while it is open; those close with Escape. */
export function overlaid(event: KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null;
  return target?.closest('[role="menu"], [role="dialog"]') != null;
}

const drawnKeys: Record<string, string> = {
  Enter: "Enter",
  Escape: "Esc",
  ArrowUp: "↑",
  ArrowDown: "↓",
};

/** The caps a shortcut is drawn as: `["⌘", "K"]`, `["J"]`. */
export function drawn(id: ShortcutId): string[] {
  const wanted = shortcut(id);
  const key = drawnKeys[wanted.key] ?? wanted.key.toUpperCase();

  return wanted.mod === true ? [modLabel, key] : [key];
}
