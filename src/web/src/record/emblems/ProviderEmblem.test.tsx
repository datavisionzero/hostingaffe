import { render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
import { AVATARS } from "../avatars/avatars";
import { ProviderEmblem } from "./ProviderEmblem";
import { EMBLEMS, PALETTE_NAMES, drawingOf, emblemOf } from "./emblems";

it("draws every emblem of the set on a tile of its own", () => {
  for (const emblem of EMBLEMS) {
    expect(drawingOf(emblem)).toMatch(/^<svg .*var\(--emblem-ground\)/);
  }
  expect(EMBLEMS).toHaveLength(16);
  expect(PALETTE_NAMES).toHaveLength(10);
});

it("shares no word with the machine's avatars", () => {
  expect(EMBLEMS.filter((emblem) => (AVATARS as readonly string[]).includes(emblem))).toEqual([]);
});

it("derives the same emblem from the same key, every time", () => {
  const once = emblemOf({ key: "example-host" });
  expect(emblemOf({ key: "example-host", emblem: null, emblem_palette: null })).toEqual(once);
  expect(new Set(["example-host", "other-host", "cloud-a", "rack-b", "colo", "home-lab"]
    .map((key) => emblemOf({ key }).emblem)).size).toBeGreaterThan(1);
});

it("draws what was chosen over what the key derives", () => {
  expect(emblemOf({ key: "example-host", emblem: "orbit", emblem_palette: "lagoon" }))
    .toEqual({ emblem: "orbit", palette: "lagoon" });

  render(<ProviderEmblem provider={{ key: "example-host", emblem: "orbit", emblem_palette: "lagoon" }} />);

  const emblem = screen.getByRole("img", { name: "lagoon orbit" });
  expect(emblem.style.getPropertyValue("--emblem-ground")).toBe("#0f4c5c");
  expect(emblem.style.getPropertyValue("--emblem-a")).toBe("#5fd3c6");
});
