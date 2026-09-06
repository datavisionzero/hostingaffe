import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { EpicView, NewEpicView } from "./EpicView";

afterEach(() => vi.unstubAllGlobals());

const epic = {
  key: "PLAN-E4",
  project: "PLAN",
  title: "The web application",
  description: "React on its own toolchain.",
  status: "open",
  author: { id: "0199a000-0000-7000-8000-000000000001", name: "maintainer" },
  labels: [{ name: "cut-3", group: null, description: null }],
  progress: { total: 3, closed: 1, done: 1, canceled: 0 },
  created_at: "2026-08-01T10:00:00Z",
  updated_at: "2026-08-02T10:00:00Z",
  closed_at: null,
};

function anIssue(key: string, title: string, status = "todo") {
  return {
    key, project: "PLAN", title, status, ready: true, priority: 2, labels: [], epic: "PLAN-E4", parent: null,
    release: null, assignee: null, claim: null, blocked_by: [], open_questions: 0, open_blockers: 0,
    open_sub_issues: 0, created_at: "2026-08-01T10:00:00Z", updated_at: "2026-08-02T10:00:00Z",
    closed_at: null, deleted_at: null, deleted_by: null,
  };
}

const issues = { items: [anIssue("PLAN-43", "The release screens"), anIssue("PLAN-45", "The epic's own view")], total: 2, has_more: false, next_cursor: null };

function renderEpic(routes: Parameters<typeof installInstance>[0] = {}) {
  const instance = installInstance({ "GET /epics/PLAN-E4": { body: epic }, "GET /issues": issues, ...routes });
  renderAt("/PLAN/epics/E4", <Routes><Route path="/:project/epics/:number" element={<EpicView />} /></Routes>);
  return instance;
}

it("opens the epic itself: the living document, the progress and its issues", async () => {
  const instance = renderEpic();

  expect(await screen.findByText("React on its own toolchain.")).toBeInTheDocument();
  expect(screen.getByText("1 of 3 closed")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "PLAN-43" })).toHaveAttribute("href", "/PLAN/issues/43");
  expect(screen.getByRole("link", { name: "In the issue list" })).toHaveAttribute("href", "/PLAN/issues?epic=PLAN-E4");
  // Deciding that something hangs under this bracket starts here, and the
  // form is reached with the bracket already filled in.
  expect(screen.getByRole("link", { name: "New issue" })).toHaveAttribute("href", "/PLAN/issues/new?epic=PLAN-E4");

  const list = instance.calls.find((call) => new URL(call.url).pathname === "/issues")!;
  expect(new URL(list.url).searchParams.get("epic")).toBe("PLAN-E4");
});

// The matrix: closing an epic with open issues warns but succeeds.
it("warns that the open issues stay workable before closing the epic", async () => {
  const instance = renderEpic({ "POST /epics/PLAN-E4/close": { body: { ...epic, status: "closed", closed_at: "2026-09-04T10:00:00Z" } } });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Close epic" }));
  const dialog = screen.getByRole("dialog", { name: "Close PLAN-E4?" });
  expect(dialog).toHaveTextContent("2 issues are still open. They stay workable — an epic is a bracket and gates nothing.");

  await user.click(within(dialog).getByRole("button", { name: "Close epic" }));

  expect(await vi.waitFor(() => instance.calls.some((call) => call.method === "POST"))).toBe(true);
  expect(await screen.findByRole("button", { name: "Reopen epic" })).toBeInTheDocument();
  expect(screen.getByText("closed")).toBeInTheDocument();
});

it("edits the living document under If-Match", async () => {
  const instance = renderEpic({ "PATCH /epics/PLAN-E4": { body: { ...epic, description: "Rewritten." } } });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Edit" }));
  await user.clear(await screen.findByLabelText("Description"));
  await user.type(await screen.findByLabelText("Description"), "Rewritten.");
  await user.click(screen.getByRole("button", { name: "Save changes" }));

  const request = await vi.waitFor(() => {
    const call = instance.calls.find((entry) => entry.method === "PATCH");
    if (call === undefined) throw new Error("no PATCH yet");
    return call;
  });
  expect(request.headers.get("If-Match")).toBe("2026-08-02T10:00:00Z");
  expect(await request.json()).toEqual({ title: "The web application", description: "Rewritten.", labels: ["cut-3"] });
  expect(await screen.findByText("Rewritten.")).toBeInTheDocument();
});

/**
 * `docs/api.md` has the `stale` refusal carry the current object so that a
 * conflict is a way forward and not a dead end. Before this the form kept the
 * version it had opened with, so every further save was refused too and the
 * only way out was to throw away what had been typed.
 */
it("keeps the typed text on a stale refusal and lets the next save through", async () => {
  const current = { ...epic, description: "Somebody else wrote this.", updated_at: "2026-08-03T09:00:00Z" };
  let patched = 0;
  const instance = renderEpic({
    "PATCH /epics/PLAN-E4": () =>
      ++patched === 1
        ? { status: 412, body: { type: "/problems/stale", detail: "PLAN-E4 changed at …", current } }
        : { body: { ...current, description: "Mine." } },
  });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Edit" }));
  await user.clear(await screen.findByLabelText("Description"));
  await user.type(await screen.findByLabelText("Description"), "Mine.");
  await user.click(screen.getByRole("button", { name: "Save changes" }));

  const conflict = await screen.findByRole("alert");
  expect(conflict).toHaveTextContent("PLAN-E4 was changed while you were editing it.");
  expect(conflict).toHaveTextContent("Somebody else wrote this.");
  // What was typed is still in the field, and the refusal's own sentence does
  // not stand beside a notice that already says it.
  expect(screen.getByLabelText("Description")).toHaveValue("Mine.");
  expect(conflict).not.toHaveTextContent("PLAN-E4 changed at");

  await user.click(screen.getByRole("button", { name: "Save changes" }));

  // The second write is guarded with the version the refusal handed back.
  const second = await vi.waitFor(() => {
    const calls = instance.calls.filter((call) => call.method === "PATCH");
    if (calls.length < 2) throw new Error("no second PATCH yet");
    return calls[1]!;
  });
  expect(second.headers.get("If-Match")).toBe("2026-08-03T09:00:00Z");
  expect(await screen.findByText("Mine.")).toBeInTheDocument();
});

it("says why an epic its issues still reference cannot be deleted", async () => {
  renderEpic({ "DELETE /epics/PLAN-E4": { status: 422, body: { detail: "2 issues still reference PLAN-E4." } } });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Delete epic" }));
  const dialog = screen.getByRole("dialog", { name: "Delete PLAN-E4?" });
  await user.click(within(dialog).getByRole("button", { name: "Delete epic" }));

  expect(await within(dialog).findByRole("alert")).toHaveTextContent("2 issues still reference PLAN-E4.");
});

it("offers the way back after a delete", async () => {
  renderEpic({ "DELETE /epics/PLAN-E4": { status: 204 }, "POST /epics/PLAN-E4/restore": { body: epic } });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Delete epic" }));
  await user.click(within(screen.getByRole("dialog", { name: "Delete PLAN-E4?" })).getByRole("button", { name: "Delete epic" }));

  await user.click(await screen.findByRole("button", { name: "Restore PLAN-E4" }));
  expect(await screen.findByText("React on its own toolchain.")).toBeInTheDocument();
});

it("says what it could not read, and offers the way back to the list", async () => {
  installInstance({ "GET /epics/PLAN-E4": { status: 404, body: { detail: "No epic PLAN-E4." } }, "GET /issues": issues });
  renderAt("/PLAN/epics/E4", <Routes><Route path="/:project/epics/:number" element={<EpicView />} /></Routes>);

  expect(await screen.findByText("No epic PLAN-E4.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "All epics" })).toHaveAttribute("href", "/PLAN/epics");
});

it("creates an epic and opens it", async () => {
  const instance = installInstance({
    "GET /projects/PLAN/labels": [{ name: "cut-3", group: null, description: null }],
    "POST /epics": { status: 201, body: epic },
  });
  renderAt(
    "/PLAN/epics/new",
    <Routes>
      <Route path="/:project/epics/new" element={<NewEpicView />} />
      <Route path="/:project/epics/:number" element={<p>The epic itself</p>} />
    </Routes>,
  );
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Title"), "The web application");
  await user.type(await screen.findByLabelText("Description"), "React on its own toolchain.");
  await user.type(await screen.findByRole("combobox", { name: "Labels" }), "cut-3{Enter}");
  await user.click(screen.getByRole("button", { name: "Create epic" }));

  expect(await screen.findByText("The epic itself")).toBeInTheDocument();
  const request = instance.calls.find((call) => call.method === "POST")!;
  expect(await request.json()).toEqual({
    project: "PLAN",
    title: "The web application",
    description: "React on its own toolchain.",
    labels: ["cut-3"],
  });
});
