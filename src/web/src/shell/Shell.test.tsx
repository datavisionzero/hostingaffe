import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/Session";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { Shell } from "./Shell";
import { drawn, shortcuts } from "./shortcuts";
import { views } from "./views";

function aPage(slug: string, title: string) {
  return {
    slug,
    title,
    updated_by: { id: aUser.id, kind: "user", name: aUser.name },
    created_at: "2026-09-02T10:00:00Z",
    updated_at: "2026-09-02T10:00:00Z",
  };
}

function shell(path: string) {
  const instance = installInstance({
    "GET /api/pages": (request) =>
      new URL(request.url).searchParams.get("q") === "nothing" ? [] : [aPage("architecture", "The web shell")],
  });

  renderAt(
    path,
    <SessionProvider value={{ me: aUser, signOut: vi.fn() }}>
      <Shell />
    </SessionProvider>,
  );

  return instance;
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

describe("the shell (ADR 0006)", () => {
  it("shows the views of the instance in the navigation", async () => {
    shell("/pages");

    const navigation = await screen.findByRole("navigation");

    for (const view of views) {
      expect(within(navigation).getByRole("link", { name: view.label })).toHaveAttribute("href", view.path);
    }
  });

  // A heading over an empty list is a promise the application does not keep.
  // The foundation fills one of the two groups, so only one is drawn.
  it("draws no group the instance has no view for", async () => {
    shell("/pages");

    const navigation = await screen.findByRole("navigation");
    const drawn = new Set<string>(views.map((view) => view.group));

    for (const group of ["Views", "Structure"]) {
      const shown = within(navigation).queryByText(group) !== null;
      expect(shown).toBe(drawn.has(group.toLowerCase()));
    }
  });

  // A typed address used to render the frame around nothing at all.
  it("answers an address it does not have, inside the frame", async () => {
    shell("/nowhere");

    expect(await screen.findByText("Nothing at this address.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to the wiki" })).toHaveAttribute("href", "/pages");
    // The frame is still the frame: the navigation did not go with the screen.
    expect(screen.getByRole("navigation")).toBeInTheDocument();
  });

  it("offers the instance administration from the palette, to an administrator", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "administration");

    await user.click(await screen.findByRole("option", { name: /Instance administration/ }));

    expect(await screen.findByRole("heading", { name: "Instance administration" })).toBeInTheDocument();
  });

  // One instance holds one team's infrastructure (VISION 9), so `/` is the
  // wiki and not a choice of where to stand.
  it("lands on the wiki from /", async () => {
    shell("/");

    await waitFor(() =>
      expect(screen.getByRole("navigation").querySelector('a[aria-current="page"]')).toHaveAttribute("href", "/pages"),
    );
  });

  it("offers what can be created from the palette", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "create");

    await user.click(await screen.findByRole("option", { name: /Create page/ }));

    expect(await screen.findByRole("heading", { name: "Create page" })).toBeInTheDocument();
  });

  it("creates from every screen", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("c");

    expect(await screen.findByRole("heading", { name: "Create page" })).toBeInTheDocument();
  });

  it("leaves c alone while something is being typed", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("searchbox", { name: "Search" }), "class");

    expect(screen.queryByRole("heading", { name: "Create page" })).not.toBeInTheDocument();
  });

  it("renders the sidebar controls as focusable links", async () => {
    shell("/pages");

    const navigation = await screen.findByRole("navigation");
    for (const link of within(navigation).getAllByRole("link")) {
      expect(link.tagName).toBe("A");
      expect(link).toHaveAttribute("data-sidebar", "menu-button");
      expect(link.tabIndex).toBe(0);
    }
  });

  // The pages are flat because the search is what a hierarchy would have been,
  // so the palette is how one is found at all.
  it("finds pages for words, and says which page each hit is", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "shell");

    // The slug is the hint: a title alone does not say where a page lives.
    const found = await screen.findByRole("option", { name: /The web shell/ });
    expect(within(found).getByText("architecture")).toBeInTheDocument();
  });

  it("shows who is signed in, top right", async () => {
    shell("/pages");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Account: maintainer" }));

    expect(await screen.findByRole("menuitem", { name: "Sign out" })).toBeInTheDocument();
    expect(screen.getByText(/administrator/)).toBeInTheDocument();
  });

  // The application binds a dozen keys and used to explain exactly one of them,
  // on the palette button. The overview is the one place that says all of them,
  // and `shortcuts.ts` is the one place they are written down.
  it("opens the overview of the keys on ?, and draws every key it binds", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("?");

    const overview = await screen.findByRole("dialog", { name: "Keyboard shortcuts" });
    for (const shortcut of shortcuts) {
      const row = within(overview).getByText(shortcut.what).closest("div")!;
      for (const cap of drawn(shortcut.id)) {
        expect(within(row).getByText(cap)).toBeInTheDocument();
      }
    }
  });

  it("leaves ? alone while something is being typed", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("searchbox", { name: "Search" }), "why?");

    expect(screen.queryByRole("dialog", { name: "Keyboard shortcuts" })).not.toBeInTheDocument();
  });

  // A list of shortcuts reachable only by a shortcut helps nobody who has not
  // found one yet: the menu is the way in for a reader who never presses a key.
  it("offers the overview from the account menu", async () => {
    shell("/pages");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Account: maintainer" }));
    await user.click(await screen.findByRole("menuitem", { name: /Keyboard shortcuts/ }));

    expect(await screen.findByRole("dialog", { name: "Keyboard shortcuts" })).toBeInTheDocument();
  });

  it("offers the overview from the palette, and steps aside for it", async () => {
    shell("/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "keyboard");
    await user.click(await screen.findByRole("option", { name: /Keyboard shortcuts/ }));

    expect(await screen.findByRole("dialog", { name: "Keyboard shortcuts" })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("combobox", { name: /command/i })).not.toBeInTheDocument());
  });
});
