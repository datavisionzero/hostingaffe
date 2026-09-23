import { act, render, screen } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import { MachineAvatar } from "./MachineAvatar";
import { AVATARS, drawingOf, hasSymbol, pictureOf, useFaces } from "./avatars";

afterEach(() => localStorage.removeItem("avatar-faces"));

it("draws every avatar of the set, with a face and without", () => {
  for (const avatar of AVATARS) {
    expect(drawingOf(avatar, true)).toMatch(/^<svg /);
    expect(drawingOf(avatar, false)).toMatch(/^<svg /);
  }
  expect(AVATARS).toHaveLength(38);
});

it("derives the same picture from the same key, every time", () => {
  const once = pictureOf({ key: "ex44" });
  expect(pictureOf({ key: "ex44", avatar: null, avatar_color: null })).toEqual(once);
  expect(new Set(["ex44", "cx22", "caddy", "db-01", "nas", "pi"].map((key) => pictureOf({ key }).avatar)).size).toBeGreaterThan(1);
});

it("draws what was chosen over what the key derives", () => {
  expect(pictureOf({ key: "ex44", avatar: "owl", avatar_color: "red" })).toEqual({ avatar: "owl", color: "red" });

  render(<MachineAvatar machine={{ key: "ex44", avatar: "owl", avatar_color: "red" }} />);

  const picture = screen.getByRole("img", { name: "red owl" });
  expect(picture.style.getPropertyValue("--avatar-color")).toBe("#d0584a");
});

/** A drawing as the page holds it once parsed, which is how it reads back. */
function parsed(svg: string) {
  const holder = document.createElement("span");
  holder.innerHTML = svg;
  return holder.innerHTML;
}

function Switch() {
  const [, setFaces] = useFaces();
  return <button onClick={() => setFaces(false)}>plain</button>;
}

it("draws a device as its symbol and an animal with its face once faces are off", () => {
  render(<><MachineAvatar machine={{ key: "a", avatar: "rack", avatar_color: "teal" }} /><MachineAvatar machine={{ key: "b", avatar: "fox", avatar_color: "teal" }} /><Switch /></>);
  const rack = screen.getByRole("img", { name: "teal rack" });
  const fox = screen.getByRole("img", { name: "teal fox" });
  expect(rack.innerHTML).toBe(parsed(drawingOf("rack", true)));

  act(() => screen.getByRole("button", { name: "plain" }).click());

  expect(hasSymbol("rack")).toBe(true);
  expect(hasSymbol("fox")).toBe(false);
  expect(rack.innerHTML).toBe(parsed(drawingOf("rack", false)));
  expect(rack.innerHTML).not.toBe(parsed(drawingOf("rack", true)));
  expect(fox.innerHTML).toBe(parsed(drawingOf("fox", true)));
});
