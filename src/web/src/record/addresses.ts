import type { Schemas } from "@/api/client";

type Anchor = Schemas["Anchor"];

/**
 * Where each thing of the record lives in the browser's address bar.
 *
 * Every object of VISION 7 is named by an immutable key, so the address is the
 * key and nothing derived from a name: renaming a machine never moves it, and a
 * link written down a year ago still leads to it. A file is the exception in
 * shape only — it is named by a path under the thing it belongs to, which is
 * why its address carries both.
 */
export function machinePath(key: string): string {
  return `/machines/${encodeURIComponent(key)}`;
}

export function softwarePath(key: string): string {
  return `/software/${encodeURIComponent(key)}`;
}

export function installationPath(key: string): string {
  return `/installations/${encodeURIComponent(key)}`;
}

/**
 * A file's address, under its owner. The path is escaped segment by segment:
 * it is the author's own, it carries slashes that are part of it, and an
 * escaped slash would be a different file than the one that was written.
 */
export function filePath(owner: Anchor, path: string): string {
  const under = owner.kind === "machine" ? machinePath(owner.key) : installationPath(owner.key);

  return `${under}/files/${path.split("/").map(encodeURIComponent).join("/")}`;
}

/** Where an anchor leads — what a file belongs to, what a page hangs on. */
export function anchorPath(anchor: Anchor): string {
  return anchor.kind === "machine" ? machinePath(anchor.key) : installationPath(anchor.key);
}

/**
 * A key, an enum value and a path are shown as the record spells them.
 *
 * There is deliberately no table of prettier words here: the closed field sets
 * of VISION 7 are the vocabulary of CONTEXT.md, they are what `ha` prints and
 * what the API carries, and a screen that title-cased `vps` into `Vps` would be
 * a second vocabulary for the same thing — the one thing the glossary exists to
 * prevent. `local` means one word under `kind` and another under `logging`
 * because the field it stands in says which, and a lookup table that flattened
 * them would have to guess.
 */
export const asRecorded = (value: string | null | undefined): string => value ?? "";
