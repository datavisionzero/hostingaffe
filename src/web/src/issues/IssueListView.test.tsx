import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { views } from "@/shell/views";
import { IssueListView } from "./IssueListView";

afterEach(() => vi.unstubAllGlobals());

const all = views.find((view) => view.id === "all")!;

function anIssue(key: string, title: string, extra: Record<string, unknown> = {}) {
  return {
    key, project: "PLAN", title, status: "todo", ready: false, priority: 2, labels: [], epic: null, parent: null,
    release: null, assignee: null, claim: null, blocked_by: [], open_questions: 0, open_blockers: 0,
    open_sub_issues: 0, created_at: "2026-08-01T10:00:00Z", updated_at: "2026-08-02T10:00:00Z",
    closed_at: null, deleted_at: null, deleted_by: null, ...extra,
  };
}

const onePage = { items: [anIssue("PLAN-1", "The first one")], total: 1, has_more: false, next_cursor: null };

const everyStep = {
  items: [0, 1, 2, 3, 4].map((priority) => anIssue(`PLAN-${priority + 1}`, `Step ${priority}`, { priority })),
  total: 5, has_more: false, next_cursor: null,
};

const anAgent = {
  id: "0199a000-0000-7000-8000-000000000009",
  kind: "agent", name: "builder", owner: { id: "u", kind: "user", name: "maintainer" },
  created_at: "2026-09-02T10:00:00Z", token: { prefix: "ha_wxyz", created_at: "2026-09-02T10:00:00Z" },
  metadata: null, metadata_reported_at: null,
};

function renderList(routes: Record<string, unknown> = {}) {
  const instance = installInstance({
    "GET /issues": onePage,
    "GET /projects/PLAN/labels": [],
    "GET /epics": { items: [{ key: "PLAN-E1", project: "PLAN", title: "The shell", status: "open" }], total: 1, has_more: false, next_cursor: null },
    "GET /projects/PLAN/users": [{ id: "u", kind: "user", name: "maintainer", administrator: true, email: "maintainer@example.test" }],
    "GET /agents": [anAgent],
    ...routes,
  });

  renderAt(`/PLAN/${all.path}`, <Routes><Route path="/:project/:view" element={<IssueListView view={all} />} /></Routes>);

  return instance;
}

/** The filter bar, open, with the answers of the instance already in it. */
async function openFilters() {
  const user = userEvent.setup();
  renderList();

  await screen.findByText("The first one");
  await user.click(screen.getByRole("button", { name: "Filters" }));

  return { user, filters: screen.getByRole("group", { name: "Issue filters" }) };
}

/**
 * The three filters that were text fields: what exists already is chosen, and
 * the values the contract knows beside a name — Any, `me`, `none` — are rows of
 * the list rather than words to guess at.
 */
it("chooses the epic instead of typing it, and offers no epic as a row", async () => {
  const { user, filters } = await openFilters();

  for (const name of ["Epic", "Assignee", "Author"]) {
    expect(within(filters).getByRole("combobox", { name })).toBeInTheDocument();
  }

  await user.click(within(filters).getByRole("combobox", { name: "Epic" }));

  const epics = await screen.findByRole("listbox", { name: "Epic" });
  expect(within(epics).getByText("PLAN-E1")).toBeInTheDocument();
  expect(within(epics).getByText("No epic")).toBeInTheDocument();

  await user.click(within(epics).getByText("PLAN-E1"));
  expect(within(filters).getByText("PLAN-E1")).toBeInTheDocument();
});

/** The assignee filter narrows; it does not assign. Hence Any, not Nobody. */
it("offers Any, me and none where the contract does", async () => {
  const { user, filters } = await openFilters();

  await user.click(within(filters).getByRole("combobox", { name: "Assignee" }));

  const assignees = await screen.findByRole("listbox", { name: "Assignee" });
  expect(within(assignees).getByText("Any")).toBeInTheDocument();
  expect(within(assignees).getByText("Me")).toBeInTheDocument();
  expect(within(assignees).getByText("Nobody")).toBeInTheDocument();
  expect(within(assignees).getByText("maintainer")).toBeInTheDocument();
});

/** An author can be an agent, so the agents of the instance are offered too. */
it("offers users and agents as authors, and no nobody", async () => {
  const { user, filters } = await openFilters();

  await user.click(within(filters).getByRole("combobox", { name: "Author" }));

  const authors = await screen.findByRole("listbox", { name: "Author" });
  expect(within(authors).getByText("maintainer")).toBeInTheDocument();
  expect(within(authors).getByText("builder")).toBeInTheDocument();
  expect(within(authors).queryByText("Nobody")).not.toBeInTheDocument();
});

/**
 * The row says the step twice, as the status dot beside it does: a mark that is
 * read at a glance, and the word that is read out. `P0` to `P4` did neither —
 * one character apart and equally loud, whichever step it was.
 */
it("marks the priority of a row with its word, not with a number", async () => {
  renderList({ "GET /issues": everyStep });

  await screen.findByText("Step 0");

  for (const [priority, word] of [[0, "none"], [1, "low"], [2, "medium"], [3, "high"], [4, "urgent"]] as const) {
    const row = screen.getByText(`Step ${priority}`).closest<HTMLElement>("[role=option]")!;
    expect(within(row).getByTitle(`Priority: ${word}`)).toBeInTheDocument();
    expect(row).toHaveTextContent(`Priority: ${word}`);
    expect(row).not.toHaveTextContent(`P${priority}`);
  }
});

/** Colour is spent once, and `none` is the step that must not draw the eye. */
it("lights as many bars as the step is high, and colours only urgent", async () => {
  renderList({ "GET /issues": everyStep });

  await screen.findByText("Step 0");

  for (const priority of [0, 1, 2, 3, 4]) {
    const row = screen.getByText(`Step ${priority}`).closest<HTMLElement>("[role=option]")!;
    const bars = Array.from(row.querySelectorAll("[title^='Priority'] span[aria-hidden] > span"));

    expect(bars).toHaveLength(4);
    expect(bars.filter((bar) => !bar.className.includes("bg-foreground/15"))).toHaveLength(priority);
    expect(bars.some((bar) => bar.className.includes("bg-destructive"))).toBe(priority === 4);
  }
});

/**
 * `sort=epic` makes the epic the first sort key, so a group is one unbroken run
 * of the answer and stays one across page boundaries (PLAN-19). The screen
 * draws the heads and does not re-sort: what comes back in that order is shown
 * in it.
 */
it("groups by epic when the epic is the sort key, and names the group without one", async () => {
  const grouped = {
    items: [
      anIssue("PLAN-3", "Under the shell", { epic: "PLAN-E1", priority: 4 }),
      anIssue("PLAN-1", "Also under the shell", { epic: "PLAN-E1", priority: 1 }),
      anIssue("PLAN-2", "Under nothing"),
    ],
    total: 3, has_more: false, next_cursor: null,
  };
  const instance = installInstance({
    "GET /issues": grouped,
    "GET /projects/PLAN/labels": [],
    "GET /epics": { items: [{ key: "PLAN-E1", project: "PLAN", title: "The shell", status: "open" }], total: 1, has_more: false, next_cursor: null },
  });
  renderAt("/PLAN/issues?sort=epic", <Routes><Route path="/:project/:view" element={<IssueListView view={all} />} /></Routes>);

  await screen.findByText("Under the shell");

  expect(new URL(instance.calls.find((call) => new URL(call.url).pathname === "/issues")!.url).searchParams.get("sort")).toBe("epic");
  // The head carries the key and the epic's own title, and the run that hangs
  // under no epic says so rather than trailing off the end of the list.
  expect(screen.getByText("PLAN-E1")).toBeInTheDocument();
  expect(screen.getByText("The shell")).toBeInTheDocument();
  expect(screen.getByText("No epic")).toBeInTheDocument();
  // Heads are not options: the listbox still holds only the three issues.
  expect(within(screen.getByRole("listbox", { name: "All issues issues" })).getAllByRole("option")).toHaveLength(3);
});

it("draws no group heads under any other sort", async () => {
  installInstance({
    "GET /issues": { items: [anIssue("PLAN-1", "Under the shell", { epic: "PLAN-E1" })], total: 1, has_more: false, next_cursor: null },
    "GET /projects/PLAN/labels": [],
    "GET /epics": { items: [], total: 0, has_more: false, next_cursor: null },
  });
  renderAt("/PLAN/issues", <Routes><Route path="/:project/:view" element={<IssueListView view={all} />} /></Routes>);

  await screen.findByText("Under the shell");

  expect(screen.queryByText("PLAN-E1")).not.toBeInTheDocument();
  expect(screen.queryByText("No epic")).not.toBeInTheDocument();
});
