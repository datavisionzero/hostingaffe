import type { CSSProperties } from "react";
import type { Schemas } from "@/api/client";
import { hash } from "../avatars/avatars";

/** A word of the provider's `emblem` set, as the contract spells it. */
export type Emblem = NonNullable<Schemas["Provider"]["emblem"]>;

/** A word of the provider's `emblem_palette` set. */
export type EmblemPalette = NonNullable<Schemas["Provider"]["emblem_palette"]>;

/**
 * The compositions, inline. Their three colours come from `--emblem-ground`,
 * `--emblem-a` and `--emblem-b`, which an `<img>` would not inherit, so every
 * one is a string put into the page as it is (ADR 0022). They are the
 * repository's own files, never anything a person or the instance wrote.
 */
const drawings = import.meta.glob<string>("./drawings/*.svg", { query: "?raw", import: "default", eager: true });

const DRAWINGS = new Map(Object.entries(drawings).map(([path, svg]) => [path.replace(/^.*\/(.*)\.svg$/, "$1"), svg]));

/** Every composition, in the order of `CONTEXT.md`, which is also the order of the set. */
export const EMBLEMS: readonly Emblem[] = [
  "orbit", "arch", "peak", "split", "quarter", "stack", "wave", "grid",
  "target", "bloom", "eclipse", "chevron", "bridge", "tiles", "beam", "steps",
];

/**
 * The ten palettes: the tile the shapes stand on, then the two colours of the
 * shapes. The tile is part of the emblem, which is what keeps every one
 * legible in the light and the dark theme alike.
 */
export const EMBLEM_PALETTES: Readonly<Record<EmblemPalette, readonly [string, string, string]>> = {
  bauhaus: ["#1f3a93", "#f2c230", "#e0452b"],
  ember: ["#3a1f1a", "#f26b38", "#ffc15e"],
  meadow: ["#2f5d3a", "#a8d672", "#f4e285"],
  lagoon: ["#0f4c5c", "#5fd3c6", "#f7b267"],
  dusk: ["#3d2c5e", "#f28482", "#f6bd60"],
  citrus: ["#f6d34a", "#e8572a", "#2d6a4f"],
  orchid: ["#6a2c70", "#f08a9f", "#f9d5e5"],
  granite: ["#3b4252", "#88c0d0", "#ebcb8b"],
  coral: ["#ff7f6b", "#1d3557", "#fbe8d3"],
  glacier: ["#cfe8ef", "#1b4965", "#62b6cb"],
};

export const PALETTE_NAMES = Object.keys(EMBLEM_PALETTES) as EmblemPalette[];

/**
 * The emblem a provider is drawn with: what was chosen, and where nothing was,
 * one derived from its key — the same on every screen and never written back
 * (ADR 0022). The seeds differ from the machine's, so that a provider and a
 * machine sharing a key do not share a choice.
 */
export function emblemOf(provider: { key: string; emblem?: Emblem | null; emblem_palette?: EmblemPalette | null }) {
  return {
    emblem: provider.emblem ?? EMBLEMS[hash(provider.key, 0x3c6ef372) % EMBLEMS.length],
    palette: provider.emblem_palette ?? PALETTE_NAMES[hash(provider.key, 0xa54ff53a) % PALETTE_NAMES.length],
  };
}

/** The drawing to put into the page. */
export const drawingOf = (emblem: Emblem) => DRAWINGS.get(emblem) ?? "";

/** The three variables a drawing takes its colours from. */
export function paletteStyle(palette: EmblemPalette): CSSProperties {
  const [ground, a, b] = EMBLEM_PALETTES[palette];
  return { "--emblem-ground": ground, "--emblem-a": a, "--emblem-b": b } as CSSProperties;
}
