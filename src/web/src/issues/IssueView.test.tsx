import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { SessionProvider } from "@/session/Session";
import { IssueView } from "./IssueView";

// The screen lives under the shell, and it asks who is looking: only the
// author of a comment is offered the correction of it (ADR 0022).
const routedIssue = (
  <SessionProvider value={{ me: aUser, signOut: vi.fn() }}>
    <Routes><Route path="/:project/issues/:number" element={<IssueView />} /></Routes>
  </SessionProvider>
);

const person = { id: "0199a000-0000-7000-8000-000000000001", kind: "user", name: "maintainer" };
const agent = { ...person, id: "0199a000-0000-7000-8000-000000000002", kind: "agent", name: "codex" };
const issue = {
  key: "PLAN-9", project: "PLAN", title: "Human-first issue", description: "Long context.\n".repeat(10),
  result: "The delivered **result**.", status: "review", ready: true, priority: 2,
  labels: [{ name: "web", group: null, description: null }], epic: null, parent: { key: "PLAN-1", title: "Parent" }, release: null,
  sub_issues: [{ key: "PLAN-10", title: "Child" }], assignee: null,
  claim: { holder: agent, since: "2026-09-04T08:00:00Z", expires_at: "2026-09-04T09:00:00Z" }, author: person,
  blocked_by: [{ key: "PLAN-2", title: "Database", status: "todo", open: true }], blocks: [{ key: null, title: null, status: null, open: true }],
  open_questions: 1, open_blockers: 1, open_sub_issues: 1,
  comments: [{ id: "0199a000-0000-7000-8000-000000000003", author: person, body: "A comment.", created_at: "2026-09-04T10:00:00Z" }],
  questions: [{ id: "0199a000-0000-7000-8000-000000000004", question: "Which way?", asked_by: agent, asked_at: "2026-09-04T09:00:00Z", answer: null, answered_by: null, answered_at: null }],
  project_context: { key: "PLAN", name: "hostingaffe", triage_required: false, review_required: true, labels: [] },
  created_at: "2026-09-03T10:00:00Z", updated_at: "2026-09-04T10:00:00Z", closed_at: null,
};

/** The same issue, open and free: what the header offers there is Claim. */
const free = { ...issue, status: "todo", claim: null, result: null, questions: [] };

afterEach(() => vi.unstubAllGlobals());

describe("the human-first issue detail", () => {
  it("puts current attention before context and never folds the description away", async () => {
    installInstance({
      "GET /issues/PLAN-9": issue,
      "GET /issues/PLAN-9/history": [{ id: 1, actor: person, at: "2026-09-03T10:00:00Z", field: "created", old_value: null, new_value: null, note: null }],
    });
    renderAt("/PLAN/issues/9", routedIssue);

    const attention = await screen.findByLabelText("Needs attention");
    expect(within(attention).getByText("Answer needed")).toBeInTheDocument();
    expect(within(attention).getByText("Review needed")).toBeInTheDocument();
    expect(within(attention).getByText("Blocked")).toBeInTheDocument();
    expect(within(attention).getByText("In progress")).toBeInTheDocument();
    expect(attention.compareDocumentPosition(screen.getByText("Description")) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // Long, so it is capped and offers the rest — but it is on the page either way.
    const description = screen.getByText("Description").closest("section")!;
    expect(within(description).getByText(/Long context\./)).toBeInTheDocument();
    expect(within(description).getByRole("button", { name: "Show more" })).toBeInTheDocument();
  });

  // The three long lists used to stack, so acting on an issue meant scrolling
  // past all of them. Conversation is what an open issue is opened for.
  it("shares one tabbed area between conversation, relationships and history", async () => {
    installInstance({
      "GET /issues/PLAN-9": issue,
      "GET /issues/PLAN-9/history": [{ id: 1, actor: person, at: "2026-09-03T10:00:00Z", field: "created", old_value: null, new_value: null, note: null }],
    });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    expect(await screen.findByRole("tab", { name: "Conversation 2" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("A comment.")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Relationships 4" }));
    expect(await screen.findByRole("link", { name: /^PLAN-1 ·/ })).toHaveAttribute("href", "/PLAN/issues/1");
    expect(screen.getByRole("link", { name: /^PLAN-10 ·/ })).toBeInTheDocument();
    expect(screen.getByText("Issue outside your project access")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "History 1" }));
    expect(await screen.findByText((_, element) => element?.tagName === "LI" && element.textContent?.includes("maintainer created the issue") === true)).toBeInTheDocument();
  });

  // The address carries the number alone, so a link may not take the project
  // from the page it sits on: PLAN-9 blocked by OTHER-7 led to PLAN-7.
  it("sends a link to the project its key names, not to the project in the address", async () => {
    const across = {
      ...issue,
      epic: { key: "OTHER-E2", title: "Elsewhere", description: "", status: "open" },
      blocked_by: [{ key: "OTHER-7", title: "Elsewhere too", status: "todo", open: true }],
    };
    installInstance({ "GET /issues/PLAN-9": across, "GET /issues/PLAN-9/history": [] });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("tab", { name: /Relationships/ }));
    for (const link of await screen.findAllByRole("link", { name: /^OTHER-7 ·/ })) {
      expect(link).toHaveAttribute("href", "/OTHER/issues/7");
    }
    // The chip line and the metadata column both name the epic.
    for (const link of screen.getAllByRole("link", { name: "OTHER-E2" })) {
      expect(link).toHaveAttribute("href", "/OTHER/epics/E2");
    }
  });

  it("answers the action that needs attention without leaving the issue", async () => {
    const answered = { ...issue.questions[0], answer: "Use the browser path.", answered_by: person, answered_at: "2026-09-04T11:00:00Z" };
    const instance = installInstance({ "GET /issues/PLAN-9": issue, "GET /issues/PLAN-9/history": [], "POST /questions/0199a000-0000-7000-8000-000000000004/answer": { body: answered } });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText("Answer"), "Use the browser path.");
    await user.click(screen.getByRole("button", { name: "Answer" }));

    expect(await screen.findByText("Use the browser path.")).toBeInTheDocument();
    expect(await instance.calls.at(-1)!.json()).toEqual({ answer: "Use the browser path." });
  });

  // The action used to sit below description, relationships and conversation:
  // one had to scroll past everything the issue says in order to act on it.
  it("offers the one action the status calls for in the header", async () => {
    const instance = installInstance({ "GET /issues/PLAN-9": issue, "GET /issues/PLAN-9/history": [], "POST /issues/PLAN-9/close": { ...issue, status: "done" } });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    const header = (await screen.findByRole("heading", { name: /Human-first issue/ })).parentElement!.parentElement!;
    await user.click(within(header).getByRole("button", { name: "Accept as done" }));

    expect(await instance.calls.at(-1)!.json()).toEqual({ status: "done", result: issue.result });
  });

  it("offers Claim on a free issue and keeps the rest of the verbs in the overflow", async () => {
    const instance = installInstance({ "GET /issues/PLAN-9": free, "GET /issues/PLAN-9/history": [], "POST /issues/PLAN-9/claim": { ...free, claim: { holder: person, since: "2026-09-05T08:00:00Z", expires_at: null } } });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Claim" }));
    expect(await instance.calls.at(-1)!.json()).toEqual({ force: false });

    await user.click(screen.getByRole("button", { name: "More actions" }));
    const menu = await screen.findByRole("menu");
    expect(within(menu).getByRole("menuitem", { name: "Release claim" })).toBeInTheDocument();
    expect(within(menu).getByRole("menuitem", { name: "Clear ready" })).toBeInTheDocument();
    expect(within(menu).getByRole("menuitem", { name: "Delete issue" })).toBeInTheDocument();
  });

  // Nothing is typed at this button, so a stale refusal has nothing to merge —
  // but the version it carries is still what makes the next press possible.
  it("shows the version a stale refusal carried and lets the same press work", async () => {
    const current = { ...free, ready: false, priority: 0, updated_at: "2026-09-05T12:00:00Z" };
    let patched = 0;
    const instance = installInstance({
      "GET /issues/PLAN-9": free,
      "GET /issues/PLAN-9/history": [],
      "PATCH /issues/PLAN-9": () =>
        ++patched === 1
          ? { status: 412, body: { type: "/problems/stale", detail: "PLAN-9 changed at …", current } }
          : { body: { ...current, ready: true } },
    });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "More actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Clear ready" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("PLAN-9 changed while it was open.");
    // The screen took the version the refusal carried, so the same press is a
    // press that can work rather than one that is refused for ever.
    await user.click(screen.getByRole("button", { name: "More actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Set ready" }));

    const second = await vi.waitFor(() => {
      const calls = instance.calls.filter((call) => call.method === "PATCH");
      if (calls.length < 2) throw new Error("no second PATCH yet");
      return calls[1]!;
    });
    expect(second.headers.get("If-Match")).toBe("2026-09-05T12:00:00Z");
  });

  // ADR 0022: the author corrects their own comment in the field they wrote it
  // in, and the correction is visible. What the caller may not do is not
  // offered — the check itself is the instance's.
  it("corrects its author's own comment in place and says that it was edited", async () => {
    const corrected = { ...issue.comments[0], body: "A corrected comment.", edited_at: "2026-09-05T11:00:00Z" };
    const instance = installInstance({
      "GET /issues/PLAN-9": free,
      "GET /issues/PLAN-9/history": [],
      "PATCH /comments/0199a000-0000-7000-8000-000000000003": { body: corrected },
    });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    expect(await screen.findByText("A comment.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Actions on the comment by maintainer" }));
    await user.click(await screen.findByRole("menuitem", { name: "Edit comment" }));

    // The field opens in the text it is correcting rather than empty.
    const field = await screen.findByLabelText("Save comment");
    expect(field).toHaveValue("A comment.");
    await user.clear(field);
    await user.type(field, "A corrected comment.");
    await user.click(screen.getByRole("button", { name: "Save comment" }));

    expect(await screen.findByText("A corrected comment.")).toBeInTheDocument();
    expect(await instance.calls.find((call) => call.method === "PATCH")!.json()).toEqual({ body: "A corrected comment." });
    expect(screen.getByText("edited")).toBeInTheDocument();
  });

  it("takes a comment away for good, once it has been confirmed", async () => {
    const instance = installInstance({
      "GET /issues/PLAN-9": free,
      "GET /issues/PLAN-9/history": [],
      "DELETE /comments/0199a000-0000-7000-8000-000000000003": { status: 204 },
    });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    expect(await screen.findByText("A comment.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Actions on the comment by maintainer" }));
    await user.click(await screen.findByRole("menuitem", { name: "Delete comment" }));

    // No grace period, so the dialog says so before it happens.
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("gone for good");
    await user.click(within(dialog).getByRole("button", { name: "Delete comment" }));

    await waitFor(() => expect(screen.queryByText("A comment.")).toBeNull());
    expect(instance.calls.some((call) => call.method === "DELETE")).toBe(true);
  });

  // A comment somebody else wrote: a user may clear it up, and nobody but its
  // author may rewrite it.
  it("offers only the delete on somebody else's comment", async () => {
    installInstance({
      "GET /issues/PLAN-9": { ...free, comments: [{ ...issue.comments[0], author: agent }] },
      "GET /issues/PLAN-9/history": [],
    });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Actions on the comment by codex" }));
    const menu = await screen.findByRole("menu");
    expect(within(menu).getByRole("menuitem", { name: "Delete comment" })).toBeInTheDocument();
    expect(within(menu).queryByRole("menuitem", { name: "Edit comment" })).not.toBeInTheDocument();
  });

  // An always-open comment box invites the comment nobody needed, and it sat
  // under the actions rather than under the thread it belongs to.
  it("opens the comment field on a button, inside the conversation", async () => {
    const comment = { id: "0199a000-0000-7000-8000-000000000005", author: person, body: "Looked at it.", created_at: "2026-09-05T10:00:00Z" };
    const instance = installInstance({ "GET /issues/PLAN-9": free, "GET /issues/PLAN-9/history": [], "POST /issues/PLAN-9/comments": { body: comment } });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    expect(await screen.findByRole("button", { name: "Add comment" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Add comment")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Add comment" }));
    await user.type(await screen.findByLabelText("Add comment"), "Looked at it.");
    await user.click(screen.getByRole("button", { name: "Add comment" }));

    expect(await screen.findByText("Looked at it.")).toBeInTheDocument();
    expect(await instance.calls.at(-1)!.json()).toEqual({ body: "Looked at it." });
  });

  // `GET /issues/{key}` answers 404 `deleted` in the grace period and the view
  // used to print that as a red sentence: the way back existed for the few
  // seconds after a delete and nowhere else.
  it("offers the way back when the key names a deleted issue", async () => {
    const until = new Date(Date.now() + 84 * 3_600_000).toISOString();
    let restored = false;
    installInstance({
      "GET /issues/PLAN-9": () => restored
        ? issue
        : {
            status: 404,
            body: { type: "/problems/deleted", title: "deleted", status: 404, detail: "Issue PLAN-9 is deleted.", restorable_until: until },
          },
      "GET /issues/PLAN-9/history": [],
      "POST /issues/PLAN-9/restore": () => { restored = true; return issue; },
    });
    renderAt("/PLAN/issues/9", routedIssue);

    const restore = await screen.findByRole("button", { name: "Restore issue" });
    expect(screen.getByRole("status")).toHaveTextContent("deleted and hidden from the project");
    expect(screen.getByText(/It can be restored until .* — 3 days left\./)).toBeInTheDocument();
    // The focus is put there by an effect, which runs after the button is in
    // the document: waited for, not read once, or the assertion is a race that
    // only loses on a busy machine.
    await waitFor(() => expect(restore).toHaveFocus());

    await userEvent.setup().click(restore);

    expect(await screen.findByText("Human-first issue")).toBeInTheDocument();
  });

  it("puts the focus on Restore after a delete removed the control that started it", async () => {
    installInstance({ "GET /issues/PLAN-9": issue, "GET /issues/PLAN-9/history": [], "DELETE /issues/PLAN-9": { status: 204 } });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "More actions" }));
    await user.click(within(await screen.findByRole("menu")).getByRole("menuitem", { name: "Delete issue" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Delete issue" }));

    const restore = await screen.findByRole("button", { name: "Restore issue" });
    await waitFor(() => expect(restore).toHaveFocus());
  });

  // VISION 7 promises it in as many words: a ticket that has not shipped yet
  // simply does not belong, and moving one by hand still works.
  it("puts an issue into the open release by hand and takes it out again", async () => {
    let put = false;
    const instance = installInstance({
      "GET /issues/PLAN-9": () => (put ? { ...free, release: "unreleased" } : free),
      "GET /issues/PLAN-9/history": [],
      "PUT /projects/PLAN/releases/unreleased/issues/PLAN-9": () => { put = true; return { body: { name: "unreleased", status: "open", description: "", published_at: null, published_by: null, issues: [] } }; },
    });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "More actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Put into the open release" }));

    expect(await vi.waitFor(() => instance.calls.some((call) => call.method === "PUT"))).toBe(true);
    expect(await screen.findByText("unreleased")).toBeInTheDocument();
  });

  it("offers no release move on an issue that has shipped", async () => {
    installInstance({ "GET /issues/PLAN-9": { ...free, release: "0.4.0" }, "GET /issues/PLAN-9/history": [] });
    renderAt("/PLAN/issues/9", routedIssue);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "More actions" }));

    expect(screen.queryByRole("menuitem", { name: /open release/ })).not.toBeInTheDocument();
  });
});
