import { screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderAt } from "@/shared/testing";
import { App } from "./App";

afterEach(() => vi.unstubAllGlobals());

describe("the first paint", () => {
  // `docs/human-interface.md`: loading is a designed state, not a blank page.
  it("says what it is waiting for while it asks who the browser is", async () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => undefined)));

    renderAt("/", <App />);

    expect(await screen.findByRole("status")).toHaveTextContent("Checking your session");
    expect(screen.getByRole("main")).toHaveAttribute("aria-busy");
  });
});
