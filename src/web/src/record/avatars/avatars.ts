import { useSyncExternalStore } from "react";
import type { Schemas } from "@/api/client";

/** A word of the machine's `avatar` set, as the contract spells it. */
export type Avatar = NonNullable<Schemas["Machine"]["avatar"]>;

/** A word of the machine's `avatar_color` set. */
export type AvatarColor = NonNullable<Schemas["Machine"]["avatar_color"]>;

/**
 * The drawings, inline. Their main colour comes from `--avatar-color`, and an
 * `<img>` would not inherit the variable, so every one is a string that is
 * put into the page as it is (ADR 0021). They are the repository's own files,
 * never anything a person or the instance wrote.
 */
const faces = import.meta.glob<string>("./faces/*.svg", { query: "?raw", import: "default", eager: true });
const symbols = import.meta.glob<string>("./symbols/*.svg", { query: "?raw", import: "default", eager: true });

const byName = (files: Record<string, string>) =>
  new Map(Object.entries(files).map(([path, svg]) => [path.replace(/^.*\/(.*)\.svg$/, "$1"), svg]));

const FACES = byName(faces);
const SYMBOLS = byName(symbols);

/**
 * Every drawing, in the order a chooser shows them: animals first, then the
 * devices. The order of `CONTEXT.md`, which is also the order of the set.
 */
export const AVATARS: readonly Avatar[] = [
  "monkey", "gorilla", "sloth", "raccoon", "fox", "owl", "penguin", "octopus", "cat", "frog",
  "bear", "wolf", "lion", "puma", "robot", "rack", "turbo", "tower", "minipc", "minimac",
  "desktop", "laptop", "devbook", "aibox", "monitor", "router", "proxy", "signpost", "firewall",
  "cloud", "container", "database", "harddrive", "bucket", "floppy", "tape", "logbook", "gauge",
];

/** The ten colours and what each one is drawn with, in light and dark alike. */
export const AVATAR_COLORS: Readonly<Record<AvatarColor, string>> = {
  brown: "#9a6b45",
  slate: "#5b6b7a",
  teal: "#2f8f8a",
  orange: "#e0843a",
  berry: "#a14a7a",
  sage: "#7c9a5a",
  blue: "#4a7bd0",
  red: "#d0584a",
  mustard: "#d4a02f",
  lavender: "#8a6fc4",
};

export const COLOR_NAMES = Object.keys(AVATAR_COLORS) as AvatarColor[];

/**
 * A stable number for a key — FNV-1a, because it is short, spreads short
 * strings well and gives the same answer in every browser. Two hashes, one
 * per choice, so that the drawing and the colour vary independently.
 */
export function hash(text: string, seed: number) {
  let value = seed >>> 0;
  for (let i = 0; i < text.length; i++) {
    value ^= text.charCodeAt(i);
    value = Math.imul(value, 0x01000193) >>> 0;
  }
  return value;
}

/**
 * The picture a machine is drawn with: what was chosen, and where nothing was,
 * one derived from its key. The derived one is the same on every screen and
 * in every browser, and it is never written back (ADR 0021).
 */
export function pictureOf(machine: { key: string; avatar?: Avatar | null; avatar_color?: AvatarColor | null }) {
  return {
    avatar: machine.avatar ?? AVATARS[hash(machine.key, 0x811c9dc5) % AVATARS.length],
    color: machine.avatar_color ?? COLOR_NAMES[hash(machine.key, 0x9e3779b9) % COLOR_NAMES.length],
  };
}

/**
 * The drawing to put into the page. Without faces a device is its plain
 * symbol; an animal and the robot have no symbol and keep their face.
 */
export function drawingOf(avatar: Avatar, withFaces: boolean) {
  return (!withFaces && SYMBOLS.get(avatar)) || FACES.get(avatar) || "";
}

/** Whether a drawing exists without a face. */
export const hasSymbol = (avatar: Avatar) => SYMBOLS.has(avatar);

const FACES_KEY = "avatar-faces";
const CHANGED = "avatar-faces-changed";

function readFaces() {
  try {
    return localStorage.getItem(FACES_KEY) !== "off";
  } catch {
    // A browser that keeps nothing still draws faces: it is the default.
    return true;
  }
}

function subscribe(changed: () => void) {
  const onStorage = (event: StorageEvent) => { if (event.key === FACES_KEY) changed(); };
  window.addEventListener("storage", onStorage);
  window.addEventListener(CHANGED, changed);
  return () => {
    window.removeEventListener("storage", onStorage);
    window.removeEventListener(CHANGED, changed);
  };
}

/**
 * The person's own choice whether devices are drawn with a face. It lives in
 * the browser like the theme, because it is taste and not a fact about any
 * machine; every avatar on the page follows a change at once, and so do the
 * other tabs.
 */
export function useFaces(): [boolean, (on: boolean) => void] {
  const on = useSyncExternalStore(subscribe, readFaces, () => true);
  return [on, (next) => {
    try {
      localStorage.setItem(FACES_KEY, next ? "on" : "off");
    } catch {
      // Nothing is kept; the page still follows for as long as it is open.
    }
    window.dispatchEvent(new Event(CHANGED));
  }];
}
