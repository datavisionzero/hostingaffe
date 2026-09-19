import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { SoftwareListView } from "./SoftwareListView";

function aSoftware(key: string, name: string) {
  return { key, name, homepage: null, image: `${key}:latest`, updated_at: "2026-09-02T10:00:00Z" };
}

afterEach(() => vi.unstubAllGlobals());

it("lists the software with the image it is run from", async () => {
  installInstance({ "GET /api/software": [aSoftware("caddy", "Caddy")] });
  renderAt("/software", <SoftwareListView />);

  expect(await screen.findByRole("link", { name: /caddy/ })).toHaveAttribute("href", "/software/caddy");
  expect(screen.getByText("caddy:latest")).toBeInTheDocument();
});

// This list has no chips, because there is no closed set to draw them from. The
// word is the only narrowing it can have, and the endpoint takes none.
it("narrows by a typed word, and asks the instance nothing for it", async () => {
  const instance = installInstance({
    "GET /api/software": [aSoftware("caddy", "Caddy"), aSoftware("postgres", "The database")],
  });

  renderAt("/software", <SoftwareListView />);

  await screen.findByRole("link", { name: /caddy/ });
  const before = instance.calls.length;

  await userEvent.type(screen.getByRole("searchbox", { name: "Find" }), "datab");

  await waitFor(() => expect(screen.queryByRole("link", { name: /caddy/ })).not.toBeInTheDocument());
  expect(screen.getByRole("link", { name: /postgres/ })).toBeInTheDocument();
  expect(instance.calls).toHaveLength(before);
});

it("says which empty it is", async () => {
  installInstance({ "GET /api/software": [] });
  renderAt("/software?q=caddy", <SoftwareListView />);

  expect(await screen.findByText("Nothing matches.")).toBeInTheDocument();
});
