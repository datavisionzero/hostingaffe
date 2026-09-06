import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import type { Issue } from "@/api/client";
import { EditIssueForm, NewIssueView } from "./IssueEditor";

afterEach(() => vi.unstubAllGlobals());

it("creates one issue with its fields and opens it", async () => {
  const instance = installInstance({
    "GET /projects/PLAN/labels": [
      { name: "web", group: null, description: "Browser application" },
      { name: "cut-three", group: null, description: null },
    ],
    "POST /issues": { status: 201, body: { items: [{ key: "PLAN-10" }] } },
  });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /><Route path="/:project/issues/:number" element={<p>Created issue</p>} /></Routes>);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Title"), "Human action");
  await user.type(await screen.findByLabelText("Description"), "A **clear** description.");
  await user.click(screen.getByRole("button", { name: "Preview" }));
  expect(screen.getByText("clear").tagName).toBe("STRONG");
  await user.click(screen.getByRole("button", { name: "Edit" }));
  const labels = await screen.findByRole("combobox", { name: "Labels" });
  await user.type(labels, "web{Enter}");
  await user.type(labels, "cut-three{Enter}");
  await user.click(screen.getByRole("button", { name: "Create issue" }));

  expect(await screen.findByText("Created issue")).toBeInTheDocument();
  const sent = await instance.calls.find((call) => call.method === "POST")!.json();
  expect(sent).toMatchObject({ project: "PLAN", issues: [{ title: "Human action", labels: ["web", "cut-three"] }] });
});

it("does not send an unchanged parking status while editing fields", async () => {
  const person = { id: "0199a000-0000-7000-8000-000000000001", kind: "user" as const, name: "maintainer" };
  const issue: Issue = {
    key: "PLAN-10", project: "PLAN", title: "Before", description: "Context", result: null,
    status: "todo", ready: true, priority: 1, labels: [{ name: "web", group: null, description: null }],
    epic: null, parent: null, release: null, sub_issues: [], assignee: null, claim: null, author: person,
    blocked_by: [], blocks: [], open_questions: 0, open_blockers: 0, open_sub_issues: 0, comments: [], questions: [],
    project_context: { key: "PLAN", name: "hostingaffe", triage_required: false, review_required: false, labels: [], instructions: null },
    created_at: "2026-09-02T10:00:00Z", updated_at: "2026-09-02T10:00:00Z", closed_at: null,
  };
  const instance = installInstance({ "PATCH /issues/PLAN-10": { body: { ...issue, title: "After", priority: 3 } } });
  renderAt("/", <EditIssueForm issue={issue} onSaved={vi.fn()} onCancel={vi.fn()} />);
  const user = userEvent.setup();

  await user.clear(screen.getByLabelText("Title"));
  await user.type(screen.getByLabelText("Title"), "After");
  await user.selectOptions(screen.getByLabelText("Priority"), "3");
  await user.click(screen.getByRole("button", { name: "Save changes" }));

  await vi.waitFor(() => expect(instance.calls.some((call) => call.method === "PATCH")).toBe(true));
  const request = instance.calls.find((call) => call.method === "PATCH")!;
  const sent = await request.json();
  expect(sent).toMatchObject({ title: "After", priority: 3 });
  expect(sent).not.toHaveProperty("status");
});

// Parking is `todo` to `backlog` and back on an unclaimed issue (ADR 0016).
// The editor collapsed every other status to "Todo", so it showed the wrong
// one and offered a move the instance refuses — and the refusal took the
// title and the description edited beside it with it.
it("shows a status it cannot park and sends no status with the fields", async () => {
  const person = { id: "0199a000-0000-7000-8000-000000000001", kind: "user" as const, name: "maintainer" };
  const issue: Issue = {
    key: "PLAN-10", project: "PLAN", title: "Before", description: "Context", result: null,
    status: "in_progress", ready: true, priority: 1, labels: [],
    epic: null, parent: null, release: null, sub_issues: [], assignee: null,
    claim: { holder: person, since: "2026-09-02T10:00:00Z", expires_at: "2026-09-02T14:00:00Z" }, author: person,
    blocked_by: [], blocks: [], open_questions: 0, open_blockers: 0, open_sub_issues: 0, comments: [], questions: [],
    project_context: { key: "PLAN", name: "hostingaffe", triage_required: false, review_required: false, labels: [], instructions: null },
    created_at: "2026-09-02T10:00:00Z", updated_at: "2026-09-02T10:00:00Z", closed_at: null,
  };
  const instance = installInstance({ "PATCH /issues/PLAN-10": { body: { ...issue, title: "After" } } });
  renderAt("/", <EditIssueForm issue={issue} onSaved={vi.fn()} onCancel={vi.fn()} />);
  const user = userEvent.setup();

  const status = screen.getByLabelText("Status", { exact: false });
  expect(status).toBeDisabled();
  expect(status).toHaveValue("in_progress");
  expect(status).toHaveTextContent("in progress");

  await user.clear(screen.getByLabelText("Title"));
  await user.type(screen.getByLabelText("Title"), "After");
  await user.click(screen.getByRole("button", { name: "Save changes" }));

  await vi.waitFor(() => expect(instance.calls.some((call) => call.method === "PATCH")).toBe(true));
  const sent = await instance.calls.find((call) => call.method === "PATCH")!.json();
  expect(sent).toMatchObject({ title: "After" });
  expect(sent).not.toHaveProperty("status");
});

/**
 * `docs/api.md` ("Concurrency on text fields") has the `stale` refusal carry
 * the current object so a conflict is a way forward and not a dead end. The
 * mask kept the version it had opened with, so every further save was refused
 * for the same reason and the only way out was to throw away what was typed.
 */
it("keeps the typed text on a stale refusal and lets the next save through", async () => {
  const person = { id: "0199a000-0000-7000-8000-000000000001", kind: "user" as const, name: "maintainer" };
  const issue: Issue = {
    key: "PLAN-10", project: "PLAN", title: "Before", description: "Mine to rewrite.", result: null,
    status: "todo", ready: false, priority: 1, labels: [], epic: null, parent: null, release: null,
    sub_issues: [], assignee: null, claim: null, author: person, blocked_by: [], blocks: [],
    open_questions: 0, open_blockers: 0, open_sub_issues: 0, comments: [], questions: [],
    project_context: { key: "PLAN", name: "hostingaffe", triage_required: false, review_required: false, labels: [], instructions: null },
    created_at: "2026-09-02T10:00:00Z", updated_at: "2026-09-02T10:00:00Z", closed_at: null,
  };
  // Somebody else rewrote the description and raised the priority meanwhile.
  const current: Issue = { ...issue, description: "Somebody else wrote this.", priority: 0, updated_at: "2026-09-02T11:00:00Z" };
  let patched = 0;
  const instance = installInstance({
    "PATCH /issues/PLAN-10": () =>
      ++patched === 1
        ? { status: 412, body: { type: "/problems/stale", detail: "PLAN-10 changed at …", current } }
        : { body: { ...current, description: "My version." } },
  });
  const saved = vi.fn();
  renderAt("/", <EditIssueForm issue={issue} onSaved={saved} onCancel={vi.fn()} />);
  const user = userEvent.setup();

  await user.clear(await screen.findByLabelText("Description"));
  await user.type(await screen.findByLabelText("Description"), "My version.");
  await user.click(screen.getByRole("button", { name: "Save changes" }));

  const conflict = await screen.findByRole("alert");
  expect(conflict).toHaveTextContent("PLAN-10 was changed while you were editing it.");
  expect(conflict).toHaveTextContent("Somebody else wrote this.");
  // The fields that are not typed into are named rather than shown, because
  // saving overwrites them too and nobody can merge what they cannot see.
  expect(conflict).toHaveTextContent("Changed there as well, and overwritten too: priority.");
  // What was typed is still in the field, and the refusal's own sentence does
  // not stand beside a notice that already says it.
  expect(screen.getByLabelText("Description")).toHaveValue("My version.");
  expect(conflict).not.toHaveTextContent("PLAN-10 changed at");

  await user.click(screen.getByRole("button", { name: "Save changes" }));

  const second = await vi.waitFor(() => {
    const calls = instance.calls.filter((call) => call.method === "PATCH");
    if (calls.length < 2) throw new Error("no second PATCH yet");
    return calls[1]!;
  });
  expect(second.headers.get("If-Match")).toBe("2026-09-02T11:00:00Z");
  await vi.waitFor(() => expect(saved).toHaveBeenCalledWith(expect.objectContaining({ description: "My version." })));
});

const closedEpic = {
  key: "PLAN-E1", project: "PLAN", title: "The first cut", description: "", status: "closed",
  author: { id: "0199a000-0000-7000-8000-000000000001", name: "maintainer" },
  labels: [], progress: { total: 4, closed: 4, done: 4, canceled: 0 },
  created_at: "2026-08-01T10:00:00Z", updated_at: "2026-08-02T10:00:00Z", closed_at: "2026-08-02T10:00:00Z",
};

const openEpic = {
  key: "PLAN-E4", project: "PLAN", title: "The web application", status: "open",
  labels: [], progress: { total: 4, closed: 1, done: 1, canceled: 0 },
  created_at: "2026-08-01T10:00:00Z", updated_at: "2026-08-02T10:00:00Z", closed_at: null,
};

const epicPage = { items: [closedEpic, openEpic], total: 2, next_cursor: null };

// The matrix: adding an issue to a closed epic warns that the epic reopens.
// The instance does reopen it, and says nothing about it in the answer.
it("says on the row which epics are closed, and warns once one is chosen", async () => {
  installInstance({ "GET /epics": epicPage });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /></Routes>);
  const user = userEvent.setup();

  await user.click(await screen.findByRole("combobox", { name: "Epic" }));
  expect(await screen.findByRole("option", { name: /PLAN-E1/ })).toHaveTextContent("closed");
  await user.click(screen.getByRole("option", { name: /PLAN-E1/ }));

  expect(await screen.findByRole("status")).toHaveTextContent(
    "PLAN-E1 is closed. Saving attaches this issue and reopens the epic.",
  );
});

// The epic screen leads here with the bracket already decided. Nothing is
// chosen, so the warning has to come from the list itself.
it("takes the epic from the address and warns about it unasked", async () => {
  installInstance({ "GET /epics": epicPage });
  renderAt("/PLAN/issues/new?epic=PLAN-E1", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /></Routes>);

  expect(await screen.findByRole("status")).toHaveTextContent(
    "PLAN-E1 is closed. Saving attaches this issue and reopens the epic.",
  );
});

it("says nothing about an open epic", async () => {
  installInstance({ "GET /epics": epicPage });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /></Routes>);
  const user = userEvent.setup();

  await user.click(await screen.findByRole("combobox", { name: "Epic" }));
  await user.click(await screen.findByRole("option", { name: /PLAN-E4/ }));

  expect(screen.queryByRole("status")).not.toBeInTheDocument();
});

const someIssues = {
  items: [
    { key: "PLAN-2", project: "PLAN", title: "The web application", status: "todo", labels: [] },
    { key: "PLAN-3", project: "PLAN", title: "The console", status: "in_progress", labels: [] },
  ],
  total: 2,
  next_cursor: null,
};

it("finds a parent by title and sends its key, never what was typed", async () => {
  const instance = installInstance({
    "GET /issues": someIssues,
    "POST /issues": { status: 201, body: { items: [{ key: "PLAN-10" }] } },
  });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /><Route path="/:project/issues/:number" element={<p>Created issue</p>} /></Routes>);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Title"), "Human action");
  await user.click(screen.getByRole("combobox", { name: "Parent issue" }));
  await user.click(await screen.findByRole("option", { name: /The web application/ }));
  await user.click(screen.getByRole("button", { name: "Create issue" }));

  expect(await screen.findByText("Created issue")).toBeInTheDocument();
  const sent = await instance.calls.find((call) => call.method === "POST")!.json();
  expect(sent.issues[0]).toMatchObject({ parent: "PLAN-2" });
});

it("holds several blockers as chips instead of a comma list", async () => {
  const instance = installInstance({
    "GET /issues": someIssues,
    "POST /issues": { status: 201, body: { items: [{ key: "PLAN-10" }] } },
  });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /><Route path="/:project/issues/:number" element={<p>Created issue</p>} /></Routes>);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Title"), "Human action");
  const blocked = screen.getByRole("combobox", { name: "Blocked by" });
  await user.click(blocked);
  await user.click(await screen.findByRole("option", { name: /PLAN-2/ }));
  await user.click(await screen.findByRole("option", { name: /PLAN-3/ }));
  expect(screen.getByRole("button", { name: "Remove PLAN-2" })).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Create issue" }));

  expect(await screen.findByText("Created issue")).toBeInTheDocument();
  const sent = await instance.calls.find((call) => call.method === "POST")!.json();
  expect(sent.issues[0]).toMatchObject({ blocked_by: ["PLAN-2", "PLAN-3"] });
});

// Nobody is the normal case (VISION 8), so it is a row rather than an empty
// field somebody has to think to clear.
it("offers the project's members and nobody among them", async () => {
  installInstance({
    "GET /projects/PLAN/users": [{ id: "0199a000-0000-7000-8000-000000000002", kind: "user", name: "maintainer", email: "maintainer@example.test", state: "active", administrator: true, created_at: "2026-08-01T10:00:00Z" }],
  });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /></Routes>);
  const user = userEvent.setup();

  await user.click(screen.getByRole("combobox", { name: "Assignee" }));

  expect(await screen.findByRole("option", { name: /maintainer/ })).toBeInTheDocument();
  expect(screen.getByRole("option", { name: /Nobody/ })).toBeInTheDocument();
});

it("shows a refusal that names a field at that field", async () => {
  installInstance({
    "GET /issues": someIssues,
    "POST /issues": { status: 409, body: { type: "/problems/one-level", title: "refused", status: 409, detail: "PLAN-2 is a sub-issue already." } },
  });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /></Routes>);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Title"), "Human action");
  await user.click(screen.getByRole("combobox", { name: "Parent issue" }));
  await user.click(await screen.findByRole("option", { name: /PLAN-2/ }));
  await user.click(screen.getByRole("button", { name: "Create issue" }));

  expect(await screen.findByRole("alert")).toHaveTextContent("PLAN-2 is a sub-issue already.");
  expect(screen.getByRole("combobox", { name: "Parent issue" })).toHaveAttribute("aria-invalid", "true");
});

const quietInstance = { "GET /epics": { items: [], total: 0, next_cursor: null }, "GET /issues": { items: [], total: 0, next_cursor: null }, "GET /projects/PLAN/users": [] };

it("leaves an untouched form at once, by the button and by Escape alike", async () => {
  installInstance(quietInstance);
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /><Route path="/:project/issues" element={<p>The list</p>} /></Routes>);
  const user = userEvent.setup();

  await user.keyboard("{Escape}");
  expect(await screen.findByText("The list")).toBeInTheDocument();
});

// The epic screen leads here with its own key in the address; cancelling
// belongs back there and not at the issue list.
it("goes back to the epic it was started from", async () => {
  installInstance(quietInstance);
  renderAt("/PLAN/issues/new?epic=PLAN-E1", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /><Route path="/:project/epics/:number" element={<p>The epic</p>} /></Routes>);
  const user = userEvent.setup();

  await user.click(screen.getByRole("button", { name: "Cancel" }));
  expect(await screen.findByText("The epic")).toBeInTheDocument();
});

it("asks before throwing away what was written, and stays when told to", async () => {
  installInstance(quietInstance);
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /><Route path="/:project/issues" element={<p>The list</p>} /></Routes>);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Title"), "Human action");
  await user.keyboard("{Escape}");

  const asking = await screen.findByRole("dialog", { name: "Discard what you wrote?" });
  await user.click(within(asking).getByRole("button", { name: "Keep writing" }));
  expect(screen.getByLabelText("Title")).toHaveValue("Human action");

  // The button is the same act as the key, not a second behaviour beside it.
  await user.click(screen.getByRole("button", { name: "Cancel" }));
  await user.click(within(await screen.findByRole("dialog", { name: "Discard what you wrote?" })).getByRole("button", { name: "Discard" }));
  expect(await screen.findByText("The list")).toBeInTheDocument();
});

// Escape belongs to whatever is nearest the keyboard: an open list of
// suggestions closes before the form it stands in does.
it("lets a picker's list have Escape before the form does", async () => {
  installInstance({ ...quietInstance, "GET /projects/PLAN/labels": [{ name: "web", group: null, description: null }] });
  renderAt("/PLAN/issues/new", <Routes><Route path="/:project/issues/new" element={<NewIssueView />} /><Route path="/:project/issues" element={<p>The list</p>} /></Routes>);
  const user = userEvent.setup();

  await user.click(await screen.findByRole("combobox", { name: "Labels" }));
  const list = await screen.findByRole("listbox", { name: "Labels" });
  expect(within(list).getByRole("option", { name: /web/ })).toBeInTheDocument();
  await user.keyboard("{Escape}");

  expect(screen.queryByRole("listbox", { name: "Labels" })).not.toBeInTheDocument();
  expect(screen.queryByText("The list")).not.toBeInTheDocument();
});
