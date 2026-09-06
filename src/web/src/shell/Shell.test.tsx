import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/Session";
import { aProject, aUser, installInstance, renderAt } from "@/shared/testing";
import { Shell } from "./Shell";
import { drawn, shortcuts } from "./shortcuts";
import { views } from "./views";

const other = { ...aProject, key: "LOG", name: "logaffe" };

function anIssue(key: string, title: string, status = "todo") {
  return {
    key,
    project: "PLAN",
    title,
    status,
    ready: true,
    priority: 3,
    labels: ["feature"],
    epic: null,
    assignee: null,
    claim: null,
    blocked_by: [],
    open_questions: 0,
    open_blockers: 0,
    open_sub_issues: 0,
    created_at: "2026-09-02T10:00:00Z",
    updated_at: "2026-09-02T10:00:00Z",
    closed_at: null,
    deleted_at: null,
    deleted_by: null,
  };
}

/**
 * A read of `/issues` that is the frame's count on the "In progress" link
 * rather than a screen's page: one item asked for, because only `total` is
 * wanted.
 */
function counting(call: Request) {
  return new URL(call.url).searchParams.get("limit") === "1";
}

function shell(path: string) {
  const instance = installInstance({
    "GET /projects": [aProject, other],
    "GET /issues": (request) => {
      const url = new URL(request.url);

      // The frame's count wants `total` and nothing else; a screen wants a
      // page. Answering them apart is what lets a test tell them apart.
      if (counting(request)) {
        const total = url.searchParams.get("status") === "in_progress" ? 3 : 0;
        return { items: [], total, has_more: false, next_cursor: null };
      }

      const items = url.searchParams.get("project") === "PLAN" ? [anIssue("PLAN-13", "The web shell")] : [];
      return { items, total: items.length, has_more: false, next_cursor: null };
    },
    "GET /epics": { items: [], total: 0, has_more: false, next_cursor: null },
    "GET /projects/PLAN/needs-you": {
      items: [{ issue: anIssue("PLAN-13", "The web shell", "review"), because: "review" }],
      total: 1,
      has_more: false,
      next_cursor: null,
    },
    "GET /projects/PLAN/labels": [{ name: "feature", group: "kind", description: null }],
    "GET /projects/PLAN/pages": (request) =>
      new URL(request.url).searchParams.get("q") === "shell"
        ? [{
            slug: "architecture",
            project: "PLAN",
            title: "The shell before it is a screen",
            labels: [],
            updated_by: { id: aUser.id, kind: "user", name: aUser.name },
            created_at: "2026-09-02T10:00:00Z",
            updated_at: "2026-09-02T10:00:00Z",
          }]
        : [],
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
  it("shows the seven views of the project in the navigation", async () => {
    shell("/PLAN/ready");

    const navigation = await screen.findByRole("navigation");

    for (const view of views) {
      // "Needs you" carries its count in the name of the link, so the label is
      // the beginning of the name rather than the whole of it.
      expect(within(navigation).getByRole("link", { name: new RegExp(`^${view.label}`) })).toHaveAttribute(
        "href",
        `/PLAN/${view.path}`,
      );
    }
  });

  // The number is a count on a link, not a notification: read from the very
  // list the screen shows, and gone again when there is nothing on it.
  it("counts Needs you in the navigation, in the name of the link", async () => {
    shell("/PLAN/ready");

    const navigation = await screen.findByRole("navigation");
    const link = await within(navigation).findByRole("link", { name: "Needs you, 1" });

    expect(link).toHaveAttribute("href", "/PLAN/needs-you");
    // Drawn beside the link and read as part of its name, not twice.
    expect(within(link.closest("li")!).getByText("1")).toHaveAttribute("aria-hidden", "true");
  });

  // The second number, on its own list and its own held read: the filter is
  // the one the link itself carries, so the count and the screen behind it
  // cannot come apart.
  it("counts In progress in the navigation as well", async () => {
    shell("/PLAN/ready");

    const navigation = await screen.findByRole("navigation");
    const link = await within(navigation).findByRole("link", { name: "In progress, 3" });

    expect(link).toHaveAttribute("href", "/PLAN/in-progress");
    expect(within(link.closest("li")!).getByText("3")).toHaveAttribute("aria-hidden", "true");
  });

  it("draws no count where nothing needs a human, and none where the instance did not answer", async () => {
    for (const answer of [
      { items: [], total: 0, has_more: false, next_cursor: null },
      { status: 503, body: { detail: "no" } },
    ]) {
      const { calls } = installInstance({
        "GET /projects": [aProject],
        "GET /issues": { items: [], total: 0, has_more: false, next_cursor: null },
        "GET /projects/PLAN/labels": [],
        "GET /projects/PLAN/needs-you": answer,
      });
      const { unmount } = renderAt(
        "/PLAN/ready",
        <SessionProvider value={{ me: aUser, signOut: vi.fn() }}>
          <Shell />
        </SessionProvider>,
      );

      const navigation = await screen.findByRole("navigation");
      await waitFor(() =>
        expect(calls.some((call) => new URL(call.url).pathname === "/projects/PLAN/needs-you")).toBe(true),
      );

      // The name of the link is the label alone: no badge, no "0", and no
      // error in the frame either.
      const link = within(navigation).getByRole("link", { name: "Needs you" });
      expect(within(link.closest("li")!).queryByText("0")).not.toBeInTheDocument();

      unmount();
    }
  });

  // Needs you is the one view of the four that is not a filtered issue list:
  // it has an endpoint of its own that says why each issue is on it.
  it("gives Needs you its own screen rather than a filtered issue list", async () => {
    const { calls } = shell("/PLAN/needs-you");

    expect(await screen.findByRole("heading", { name: "In review · 1", level: 2 })).toBeInTheDocument();
    expect(calls.some((call) => new URL(call.url).pathname === "/projects/PLAN/needs-you")).toBe(true);
    // The frame counts "In progress" over `/issues` whatever screen is open;
    // what the screen must not do is take its own items from there.
    expect(calls.filter((call) => new URL(call.url).pathname === "/issues").every(counting)).toBe(true);
  });

  it("frames the list of the view it was opened on, filtered by the view", async () => {
    const { calls } = shell("/PLAN/ready");

    expect(await screen.findByText("The web shell")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Ready for agents" })).toBeInTheDocument();

    const list = calls.find((call) => new URL(call.url).pathname === "/issues")!;
    const query = new URL(list.url).searchParams;
    expect(query.get("project")).toBe("PLAN");
    expect(query.getAll("status")).toEqual(["todo"]);
    expect(query.get("ready")).toBe("true");
  });

  it("lets the URL carry the filter", async () => {
    const { calls } = shell("/PLAN/issues?label=cut-3&status=backlog&status=todo");

    await screen.findByText("The web shell");

    const list = calls.find((call) => new URL(call.url).pathname === "/issues")!;
    const query = new URL(list.url).searchParams;
    expect(query.getAll("label")).toEqual(["cut-3"]);
    expect(query.getAll("status")).toEqual(["backlog", "todo"]);
  });

  // The screen matrix gives /:project/issues "filters open as a dismissible
  // sheet" on a narrow screen; they were an inline bar on every width.
  it("opens the filters as a sheet on a narrow screen and hands the focus back", async () => {
    vi.stubGlobal("matchMedia", (media: string) => ({
      matches: true, media, onchange: null,
      addEventListener: () => undefined, removeEventListener: () => undefined,
      addListener: () => undefined, removeListener: () => undefined, dispatchEvent: () => false,
    }));
    shell("/PLAN/issues");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    const filters = screen.getByRole("button", { name: "Filters" });
    await user.click(filters);

    const sheet = await screen.findByRole("dialog", { name: "Filters" });
    expect(within(sheet).getByRole("group", { name: "Issue filters" })).toBeInTheDocument();

    await user.click(within(sheet).getByRole("button", { name: "Close" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Filters" })).not.toBeInTheDocument());
    expect(filters).toHaveFocus();
  });

  it("keeps the filter bar in place on a wide screen, and Escape returns the focus", async () => {
    shell("/PLAN/issues");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    const filters = screen.getByRole("button", { name: "Filters" });
    await user.click(filters);

    expect(screen.getByRole("group", { name: "Issue filters" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Filters" })).not.toBeInTheDocument();

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("group", { name: "Issue filters" })).not.toBeInTheDocument();
    expect(filters).toHaveFocus();
  });

  // ADR 0013 calls the deleted list a real read; it was reachable only by
  // typing ?deleted=true into the address.
  it("offers the deleted list from the filters", async () => {
    const { calls } = shell("/PLAN/issues");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.click(screen.getByRole("button", { name: "Filters" }));
    await user.selectOptions(screen.getByLabelText("Deleted"), "true");

    await waitFor(() =>
      expect(calls.some((call) => new URL(call.url).searchParams.get("deleted") === "true")).toBe(true),
    );
  });

  it("switches the project and keeps the view", async () => {
    shell("/PLAN/in-progress");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Switch project" }));
    await user.click(await screen.findByRole("menuitem", { name: /logaffe/ }));

    await waitFor(() =>
      expect(screen.getByRole("navigation").querySelector('a[aria-current="page"]')).toHaveAttribute(
        "href",
        "/LOG/in-progress",
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
      "GET /issues": { items: [], total: 0, has_more: false, next_cursor: null },
      "GET /projects/PLAN/labels": [],
    });
    renderAt(
      "/PLAN/ready",
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
    shell("/PLAN/ready");
    const user = userEvent.setup();

    await screen.findByText("The web shell");
    await user.click(screen.getByRole("button", { name: "Switch project" }));
    await user.click(await screen.findByRole("menuitem", { name: "Create project" }));

    expect(await screen.findByRole("heading", { name: "Create project" })).toBeInTheDocument();
  });

  // `c` creates from a list, and nothing on the screen said so. The epic list
  // has had its button all along; the issue lists say it the same way now.
  it("offers issue creation from the header of every issue list", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.click(screen.getByRole("link", { name: "New issue" }));

    expect(await screen.findByRole("heading", { name: "Create issue" })).toBeInTheDocument();
  });

  it("offers issue creation from Needs you, which is a list of issues too", async () => {
    shell("/PLAN/needs-you");
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "In review · 1", level: 2 });

    await user.click(screen.getByRole("link", { name: "New issue" }));

    expect(await screen.findByRole("heading", { name: "Create issue" })).toBeInTheDocument();
  });

  it("offers the four things that can be created from the palette", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "create");

    for (const what of ["Create issue", "Create epic", "Create page", "Create project"]) {
      expect(await screen.findByRole("option", { name: new RegExp(what) })).toBeInTheDocument();
    }

    await user.click(screen.getByRole("option", { name: /Create epic/ }));

    expect(await screen.findByRole("heading", { name: "Create epic" })).toBeInTheDocument();
  });

  // `c` used to belong to the issue list component, so it answered on three
  // screens of seven and did nothing on the epics, the releases, the labels or
  // an issue itself. Creating belongs to the project.
  it("creates from every screen of the project, not only from an issue list", async () => {
    shell("/PLAN/epics");
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Epics" });

    await user.keyboard("c");

    expect(await screen.findByRole("heading", { name: "Create issue" })).toBeInTheDocument();
  });

  it("leaves c alone where the frame stands in no project", async () => {
    shell("/settings");
    const user = userEvent.setup();
    await screen.findByRole("heading", { name: "Personal settings" });

    await user.keyboard("c");

    expect(screen.queryByRole("heading", { name: "Create issue" })).not.toBeInTheDocument();
  });

  it("leaves c alone while something is being typed", async () => {
    shell("/PLAN/issues");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("textbox", { name: "Search issues" }), "class");

    expect(screen.queryByRole("heading", { name: "Create issue" })).not.toBeInTheDocument();
  });

  it("renders the sidebar controls as focusable links", async () => {
    shell("/PLAN/ready");

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
        "/LOG/ready",
      ),
    );
  });

  it("opens the palette on ⌘K and jumps to a key typed into it", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    const box = await screen.findByRole("combobox", { name: /command/i });
    await user.type(box, "plan-13{Enter}");

    expect(await screen.findByRole("heading", { name: /PLAN-13/ })).toBeInTheDocument();
  });

  it("offers what the instance finds for words, and the way to all of them", async () => {
    const { calls } = shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "shell");

    const match = await screen.findByRole("option", { name: /The web shell/ });
    expect(within(match).getByText("PLAN-13")).toBeInTheDocument();
    await waitFor(() => {
      const search = calls.map((call) => new URL(call.url)).find((url) => url.searchParams.get("q") === "shell")!;
      expect(search.searchParams.get("project")).toBe("PLAN");
      expect(search.searchParams.get("limit")).toBe("5");
    });

    await user.click(match);

    expect(await screen.findByRole("heading", { name: /PLAN-13/ })).toBeInTheDocument();
  });

  /**
   * The wiki is flat because the search is what a hierarchy would have been
   * (VISION 7), so this is how a page is found at all — and the heading says
   * it is a page, because a hit that does not say what kind of thing it is is
   * a poor hit.
   */
  it("finds pages as well as issues, under headings that say which is which", async () => {
    const { calls } = shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "shell");

    const page = await screen.findByRole("option", { name: /The shell before it is a screen/ });
    expect(within(page).getByText("architecture")).toBeInTheDocument();
    // Inside the palette, the one "Pages" that is not itself a row is the
    // heading over the hits; the other is the row that opens the wiki.
    const listbox = screen.getByRole("listbox");
    expect(within(listbox).getAllByText("Pages").filter((element) => element.closest('[role="option"]') === null)).toHaveLength(1);
    expect(within(listbox).getByText("Issues")).toBeInTheDocument();

    await waitFor(() => {
      const asked = calls.map((call) => new URL(call.url)).find((url) => url.pathname === "/projects/PLAN/pages" && url.searchParams.get("q") === "shell");
      expect(asked).toBeDefined();
    });

    await user.click(page);

    expect(await screen.findByRole("heading", { name: /architecture/ })).toBeInTheDocument();
  });

  it("lands on the filtered list from the last row of the matches", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "shell");
    await user.click(await screen.findByRole("option", { name: /All issues matching/ }));

    expect(await screen.findByRole("heading", { name: "All issues" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Search issues" })).toHaveValue("shell");
  });

  // PLAN-51: the dropdown advertised ⌘P and nothing was bound to it. ⌘P is the
  // browser's print, so the shortcut that got bound is the bare key the lists
  // already speak.
  it("opens the project switcher on the key it advertises", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("p");

    const label = await screen.findByText("Projects");
    expect(within(label).getByText("P")).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /logaffe/ })).toBeInTheDocument();
  });

  it("leaves the key alone while something is being typed", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("textbox", { name: "Search issues" }), "print");

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
      "GET /issues": { items: [], total: 0, has_more: false, next_cursor: null },
      "GET /projects/NEW/labels": [],
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

    const navigation = await screen.findByRole("navigation");
    await waitFor(() =>
      expect(within(navigation).getByRole("link", { name: views[0].label })).toHaveAttribute(
        "href",
        `/NEW/${views[0].path}`,
      ),
    );
    expect(screen.getByRole("button", { name: "Switch project" })).toHaveTextContent("the new one");
  });

  // The field drew what was typed in upper case and sent it as typed, so a key
  // typed in lower case looked accepted and came back refused: "a project key
  // is upper case" — about the very thing the screen had just drawn correctly.
  it("sends the project key the way it draws it: upper case", async () => {
    const created = { ...aProject, key: "NEW", name: "the new one" };
    const instance = installInstance({
      "GET /projects": [aProject],
      "POST /projects": { status: 201, body: created },
      "GET /issues": { items: [], total: 0, has_more: false, next_cursor: null },
      "GET /projects/NEW/labels": [],
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
    shell("/PLAN/ready");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Account: maintainer" }));

    expect(await screen.findByRole("menuitem", { name: "Sign out" })).toBeInTheDocument();
    expect(screen.getByText(/administrator/)).toBeInTheDocument();
  });
  // The application binds a dozen keys and used to explain exactly one of them,
  // on the palette button. The overview is the one place that says all of them,
  // and `shortcuts.ts` is the one place they are written down.
  it("opens the overview of the keys on ?, and draws every key it binds", async () => {
    shell("/PLAN/ready");
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
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.type(screen.getByRole("textbox", { name: "Search issues" }), "why?");

    expect(screen.queryByRole("dialog", { name: "Keyboard shortcuts" })).not.toBeInTheDocument();
  });

  // A list of shortcuts reachable only by a shortcut helps nobody who has not
  // found one yet: the menu is the way in for a reader who never presses a key.
  it("offers the overview from the account menu", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Account: maintainer" }));
    await user.click(await screen.findByRole("menuitem", { name: /Keyboard shortcuts/ }));

    expect(await screen.findByRole("dialog", { name: "Keyboard shortcuts" })).toBeInTheDocument();
  });

  it("offers the overview from the palette, and steps aside for it", async () => {
    shell("/PLAN/ready");
    const user = userEvent.setup();
    await screen.findByText("The web shell");

    await user.keyboard("{Meta>}k{/Meta}");
    await user.type(await screen.findByRole("combobox", { name: /command/i }), "keyboard");
    await user.click(await screen.findByRole("option", { name: /Keyboard shortcuts/ }));

    expect(await screen.findByRole("dialog", { name: "Keyboard shortcuts" })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("combobox", { name: /command/i })).not.toBeInTheDocument());
  });
});
