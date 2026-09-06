import { act, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { AttentionContext } from "@/shell/attention";
import { NeedsYouView } from "./NeedsYouView";

afterEach(() => vi.unstubAllGlobals());

function anIssue(key: string, title: string, extra: Record<string, unknown> = {}) {
  return {
    key, project: "PLAN", title, status: "todo", ready: false, priority: 2, labels: [], epic: null, parent: null,
    release: null, assignee: null, claim: null, blocked_by: [], open_questions: 0, open_blockers: 0,
    open_sub_issues: 0, created_at: "2026-08-01T10:00:00Z", updated_at: "2026-08-02T10:00:00Z",
    closed_at: null, deleted_at: null, deleted_by: null, ...extra,
  };
}

// The instance answers in human-action order; the screen cuts the groups where
// `because` changes rather than sorting again.
const fourReasons = {
  items: [
    { issue: anIssue("PLAN-1", "Asked something", { open_questions: 1 }), because: "question" },
    { issue: anIssue("PLAN-2", "Handed in", { status: "review" }), because: "review" },
    { issue: anIssue("PLAN-3", "Never triaged"), because: "unready" },
    { issue: anIssue("PLAN-4", "Behind a parked blocker", { open_blockers: 1 }), because: "stuck" },
  ],
  total: 4,
  has_more: false,
  next_cursor: null,
  agents: 1,
};

/**
 * The frame around the screen: the number it draws, and the wake pulse both
 * share. The returned function is the instance saying the list changed.
 */
function renderNeedsYou() {
  let woken!: (pulse: number) => void;

  function Frame() {
    const [pulse, setPulse] = useState(0);
    woken = setPulse;

    return (
      <AttentionContext.Provider value={{ needsYou: null, inProgress: null, pulse }}>
        <Routes><Route path="/:project/needs-you" element={<NeedsYouView />} /></Routes>
      </AttentionContext.Provider>
    );
  }

  renderAt("/PLAN/needs-you", <Frame />);

  return (pulse: number) => act(() => woken(pulse));
}

it("shows the four groups, why each row is there, and the action that resolves it", async () => {
  installInstance({ "GET /projects/PLAN/needs-you": fourReasons });
  renderNeedsYou();

  const headings = await screen.findAllByRole("heading", { level: 2 });
  expect(headings.map((heading) => heading.textContent)).toEqual([
    "Open questions · 1",
    "In review · 1",
    "Not ready · 1",
    "Stuck · 1",
  ]);

  const question = screen.getByText("Asked something").closest("li")!;
  expect(question).toHaveTextContent("An open question waits for an answer.");
  expect(within(question).getByRole("link", { name: "Answer" })).toHaveAttribute("href", "/PLAN/issues/1");

  const review = screen.getByText("Handed in").closest("li")!;
  expect(review).toHaveTextContent("The result is handed in and waits for a decision.");
  expect(within(review).getByRole("link", { name: "Review" })).toHaveAttribute("href", "/PLAN/issues/2");

  const stuck = screen.getByText("Behind a parked blocker").closest("li")!;
  expect(stuck).toHaveTextContent("Its chain of blockers ends where no agent can go on.");
  expect(within(stuck).getByRole("link", { name: "See blockers" })).toHaveAttribute("href", "/PLAN/issues/4");
});

it("sets ready in place and reads the list again", async () => {
  let asked = 0;
  const instance = installInstance({
    "GET /projects/PLAN/needs-you": () => {
      asked += 1;
      return asked === 1 ? fourReasons : { ...fourReasons, items: fourReasons.items.filter((item) => item.because !== "unready"), total: 3 };
    },
    "PATCH /issues/PLAN-3": { body: anIssue("PLAN-3", "Never triaged", { ready: true }) },
  });
  renderNeedsYou();
  const user = userEvent.setup();

  const row = (await screen.findByText("Never triaged")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Set ready" }));

  const request = await vi.waitFor(() => instance.calls.find((call) => call.method === "PATCH")!);
  expect(request.headers.get("If-Match")).toBe("2026-08-02T10:00:00Z");
  // Only `ready` is on the wire, and this is the whole of the assertion: a
  // field present as `null` is not a field left alone, it is a field cleared
  // (`docs/api.md`). Naming the others here to satisfy the generated type sent
  // the description, the result, the assignee, the epic and the parent away
  // with one press — and this very expectation, written the same way, held the
  // door open for it.
  expect(await request.json()).toEqual({ ready: true });
  // The row leaves because the list was read again, not because it was removed here.
  await vi.waitFor(() => expect(screen.queryByText("Never triaged")).toBeNull());
  expect(screen.queryByRole("heading", { name: /Not ready/ })).not.toBeInTheDocument();
  expect(asked).toBe(2);
});

it("reads the list again when the frame's wake pulse says it changed", async () => {
  let asked = 0;
  installInstance({
    "GET /projects/PLAN/needs-you": () => {
      asked += 1;
      return asked === 1 ? fourReasons : { ...fourReasons, items: fourReasons.items.slice(0, 1), total: 1 };
    },
  });
  const pulse = renderNeedsYou();

  expect(await screen.findByText("Never triaged")).toBeInTheDocument();

  // The instance woke the frame; the screen asks its own question again rather
  // than being handed the frame's answer, which is one item long.
  pulse(1);

  await vi.waitFor(() => expect(screen.queryByText("Never triaged")).toBeNull());
  expect(asked).toBe(2);
  expect(screen.getByText("Asked something")).toBeInTheDocument();
});

// The row types nothing, so a stale refusal has nothing to lose — but it still
// may not be a dead end: without the version the refusal carried, every further
// press sends the version the list was read at and is refused for the same
// reason (PLAN-35).
it("takes the version a stale triage refusal carried and lets the next press through", async () => {
  const current = { ...anIssue("PLAN-3", "Never triaged"), updated_at: "2026-08-04T10:00:00Z" };
  let patched = 0;
  const instance = installInstance({
    "GET /projects/PLAN/needs-you": fourReasons,
    "PATCH /issues/PLAN-3": () =>
      ++patched === 1
        ? { status: 412, body: { type: "/problems/stale", detail: "PLAN-3 changed at …", current } }
        : { body: { ...current, ready: true } },
  });
  renderNeedsYou();
  const user = userEvent.setup();

  const row = (await screen.findByText("Never triaged")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Set ready" }));

  expect(await within(row).findByRole("alert")).toHaveTextContent("It changed since this list was read.");
  await user.click(within(row).getByRole("button", { name: "Set ready" }));

  const second = await vi.waitFor(() => {
    const calls = instance.calls.filter((call) => call.method === "PATCH");
    if (calls.length < 2) throw new Error("no second PATCH yet");
    return calls[1]!;
  });
  expect(second.headers.get("If-Match")).toBe("2026-08-04T10:00:00Z");
});

it("says why a refused triage did not happen and leaves the row where it is", async () => {
  installInstance({
    "GET /projects/PLAN/needs-you": fourReasons,
    "PATCH /issues/PLAN-3": { status: 412, body: { detail: "The issue changed since you read it." } },
  });
  renderNeedsYou();
  const user = userEvent.setup();

  const row = (await screen.findByText("Never triaged")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Set ready" }));

  expect(await within(row).findByRole("alert")).toHaveTextContent("The issue changed since you read it.");
  expect(screen.getByText("Never triaged")).toBeInTheDocument();
});

it("appends the next page and counts what is shown against the total", async () => {
  const first = {
    items: [{ issue: anIssue("PLAN-1", "Asked something", { open_questions: 1 }), because: "question" }],
    total: 2,
    has_more: true,
    next_cursor: "cursor-two",
  };
  const instance = installInstance({
    "GET /projects/PLAN/needs-you": (request: Request) =>
      new URL(request.url).searchParams.get("cursor") === "cursor-two"
        ? { items: [{ issue: anIssue("PLAN-4", "Behind a parked blocker"), because: "stuck" }], total: 2, has_more: false, next_cursor: null }
        : first,
  });
  renderNeedsYou();
  const user = userEvent.setup();

  expect(await screen.findByText("1 of 2")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Show more" }));

  expect(await screen.findByText("Behind a parked blocker")).toBeInTheDocument();
  expect(screen.getByText("Asked something")).toBeInTheDocument();
  expect(screen.getByText("2")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Show more" })).not.toBeInTheDocument();
  expect(instance.calls.filter((call) => call.method === "GET")).toHaveLength(2);
});

it("says nothing needs a human rather than showing an empty frame", async () => {
  installInstance({ "GET /projects/PLAN/needs-you": { items: [], total: 0, has_more: false, next_cursor: null } });
  renderNeedsYou();

  expect(await screen.findByText("Nothing needs you.")).toBeInTheDocument();
});

it("says what it could not load", async () => {
  installInstance({ "GET /projects/PLAN/needs-you": { status: 404, body: { detail: "No project PLAN." } } });
  renderNeedsYou();

  expect(await screen.findByText("No project PLAN.")).toBeInTheDocument();
});

/**
 * PLAN-29: an instance with no agent used to turn every blocked issue into an
 * entry of its own here. It is one fact about the instance, and the one thing
 * to do about it is not an issue on this list — so it is said once, beside it.
 */
it("says once that no agent can pick anything up, rather than per issue", async () => {
  installInstance({ "GET /projects/PLAN/needs-you": { ...fourReasons, agents: 0 } });
  renderNeedsYou();

  expect(await screen.findByText(/No agent can pick work up on this instance/)).toBeInTheDocument();
  expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(4);
});

it("says nothing about agents where there is one", async () => {
  installInstance({ "GET /projects/PLAN/needs-you": fourReasons });
  renderNeedsYou();

  await screen.findByText("Asked something");
  expect(screen.queryByText(/No agent can pick work up/)).not.toBeInTheDocument();
});
