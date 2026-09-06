import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/Session";
import { aProject, aUser, installInstance, renderAt } from "@/shared/testing";
import { Shell } from "./Shell";
import { drawn, shortcuts } from "./shortcuts";
import { views } from "./views";

const other = { ...aProject, key: "LOG", name: "logaffe" };

function aPage(slug: string, title: string) {
  return {
    slug,
    project: "PLAN",
    title,
    updated_by: { id: aUser.id, kind: "user", name: aUser.name },
    created_at: "2026-09-02T10:00:00Z",
    updated_at: "2026-09-02T10:00:00Z",
  };
}

function shell(path: string) {
  const instance = installInstance({
    "GET /projects": [aProject, other],
    "GET /projects/PLAN/pages": (request) =>
      new URL(request.url).searchParams.get("q") === "nothing" ? [] : [aPage("architecture", "The web shell")],
    "GET /projects/LOG/pages": [],
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
  it("shows the views of the project in the navigation", async () => {
    shell("/PLAN/pages");

    const navigation = await screen.findByRole("navigation");

    for (const view of views) {
      expect(within(navigation).getByRole("link", { name: view.label })).toHaveAttribute("href", `/PLAN/${view.path}`);
    }
  });

  it("switches the project and keeps the view", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Switch project" }));
    await user.click(await screen.findByRole("menuitem", { name: /logaffe/ }));

    await waitFor(() =>
      expect(screen.getByRole("navigation").querySelector('a[aria-current="page"]')).toHaveAttribute(
        "href",
        "/LOG/pages",
      ),
    );
    expect(window.localStorage.getItem("hostingaffe.project")).toBe("LOG");
  });

  // The frame used to flatten a failed list into an empty one, so the switcher
  // claimed there were no projects and the sidebar simply went dead.
  it("says the project list failed rather than that there are none", async () => {
    let answers = 0;
    installInstance({
      "GET /projects": () => (answers++ === 0 ? { status: 503, body: { detail: "no" } } : [aProject]),
      "GET /projects/PLAN/pages": [],
    });
    renderAt(
      "/PLAN/pages",
      <SessionProvider value={{ me: aUser, signOut: vi.fn() }}>
        <Shell />
      </SessionProvider>,
    );
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Switch project" }));

    expect(await screen.findByText("The projects could not be loaded.")).toBeInTheDocument();
    expect(screen.queryByText("No project yet.")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("menuitem", { name: /hostingaffe/ })).toBeInTheDocument();
  });

  it("offers project creation from the project switcher", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();

    await screen.findByText("The web shell");
    await user.click(screen.getByRole("button", { name: "Switch project" }));
    await user.click(await screen.findByRole("menuitem", { name: "Create project" }));

    expect(await screen.findByRole("heading", { name: "Create project" })).toBeInTheDocument();
  });

  it("offers what can be created from the palette", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "create");

    for (const what of ["Create page", "Create project"]) {
      expect(await screen.findByRole("option", { name: new RegExp(what) })).toBeInTheDocument();
    }

    await user.click(screen.getByRole("option", { name: /Create page/ }));

    expect(await screen.findByRole("heading", { name: "Create page" })).toBeInTheDocument();
  });

  // Creating belongs to the project, not to one of its screens: the key
  // answers wherever the frame stands in a project.
  it("creates from every screen of the project", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("c");

    expect(await screen.findByRole("heading", { name: "Create page" })).toBeInTheDocument();
  });

  it("leaves c alone where the frame stands in no project", async () => {
    shell("/settings");
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Personal settings" });

    await user.keyboard("c");

    expect(screen.queryByRole("heading", { name: "Create page" })).not.toBeInTheDocument();
  });

  it("leaves c alone while something is being typed", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("searchbox", { name: "Search" }), "class");

    expect(screen.queryByRole("heading", { name: "Create page" })).not.toBeInTheDocument();
  });

  it("renders the sidebar controls as focusable links", async () => {
    shell("/PLAN/pages");

    const navigation = await screen.findByRole("navigation");
    for (const link of within(navigation).getAllByRole("link")) {
      expect(link.tagName).toBe("A");
      expect(link).toHaveAttribute("data-sidebar", "menu-button");
      expect(link.tabIndex).toBe(0);
    }
  });

  it("lands on the remembered project from /", async () => {
    window.localStorage.setItem("hostingaffe.project", "LOG");
    shell("/");

    await waitFor(() =>
      expect(screen.getByRole("navigation").querySelector('a[aria-current="page"]')).toHaveAttribute(
        "href",
        "/LOG/pages",
      ),
    );
  });

  // The pages are flat because the search is what a hierarchy would have been,
  // so the palette is how one is found at all.
  it("finds pages for words, and says which page each hit is", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "shell");

    // The slug is the hint: a title alone does not say where a page lives.
    const found = await screen.findByRole("option", { name: /The web shell/ });
    expect(within(found).getByText("architecture")).toBeInTheDocument();
  });

  it("opens the project switcher on the key it advertises", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("p");

    const label = await screen.findByText("Projects");
    expect(within(label).getByText("P")).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /logaffe/ })).toBeInTheDocument();
  });

  it("leaves the key alone while something is being typed", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("searchbox", { name: "Search" }), "print");

    expect(screen.queryByRole("menuitem", { name: /logaffe/ })).not.toBeInTheDocument();
  });

  // The shell is not remounted by navigation (ADR 0006), so the frame kept the
  // list it had asked for once: on arrival at the project just created there
  // was no current project, and every link in the frame was drawn disabled.
  it("has the project a screen just created when it navigates there", async () => {
    const created = { ...aProject, key: "NEW", name: "the new one" };
    let made = false;
    installInstance({
      "GET /projects": () => (made ? [aProject, created] : [aProject]),
      "POST /projects": () => { made = true; return { status: 201, body: created }; },
      "GET /projects/NEW/pages": [],
    });
    renderAt(
      "/projects/new",
      <SessionProvider value={{ me: aUser, signOut: vi.fn() }}>
        <Shell />
      </SessionProvider>,
    );
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText("Key"), "NEW");
    await user.type(screen.getByLabelText("Name"), "the new one");
    await user.click(screen.getByRole("button", { name: "Create project" }));

    await waitFor(() =>
      expect(screen.getByRole("navigation").querySelector('a[aria-current="page"]')).toHaveAttribute(
        "href",
        "/NEW/pages",
      ),
    );
  });

  it("sends the project key the way it draws it: upper case", async () => {
    const created = { ...aProject, key: "NEW", name: "the new one" };
    const instance = installInstance({
      "GET /projects": [aProject],
      "POST /projects": { status: 201, body: created },
      "GET /projects/NEW/pages": [],
    });
    renderAt(
      "/projects/new",
      <SessionProvider value={{ me: aUser, signOut: vi.fn() }}>
        <Shell />
      </SessionProvider>,
    );
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText("Key"), "new");
    expect(screen.getByLabelText("Key")).toHaveValue("NEW");

    await user.type(screen.getByLabelText("Name"), "the new one");
    await user.click(screen.getByRole("button", { name: "Create project" }));

    const post = await vi.waitFor(() => instance.calls.find((call) => call.method === "POST")!);
    expect(await post.json()).toMatchObject({ key: "NEW", name: "the new one" });
  });

  it("shows who is signed in, top right", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Account: maintainer" }));

    expect(await screen.findByRole("menuitem", { name: "Sign out" })).toBeInTheDocument();
    expect(screen.getByText(/administrator/)).toBeInTheDocument();
  });

  // The application binds a dozen keys and used to explain exactly one of them,
  // on the palette button. The overview is the one place that says all of them,
  // and `shortcuts.ts` is the one place they are written down.
  it("opens the overview of the keys on ?, and draws every key it binds", async () => {
    shell("/PLAN/pages");
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
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("searchbox", { name: "Search" }), "why?");

    expect(screen.queryByRole("dialog", { name: "Keyboard shortcuts" })).not.toBeInTheDocument();
  });

  // A list of shortcuts reachable only by a shortcut helps nobody who has not
  // found one yet: the menu is the way in for a reader who never presses a key.
  it("offers the overview from the account menu", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Account: maintainer" }));
    await user.click(await screen.findByRole("menuitem", { name: /Keyboard shortcuts/ }));

    expect(await screen.findByRole("dialog", { name: "Keyboard shortcuts" })).toBeInTheDocument();
  });

  it("offers the overview from the palette, and steps aside for it", async () => {
    shell("/PLAN/pages");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "keyboard");
    await user.click(await screen.findByRole("option", { name: /Keyboard shortcuts/ }));

    expect(await screen.findByRole("dialog", { name: "Keyboard shortcuts" })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("combobox", { name: /command/i })).not.toBeInTheDocument());
  });
});
