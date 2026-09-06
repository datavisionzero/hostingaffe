import { act, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { useAttentionState } from "./useAttention";

/**
 * The frame's held reads of "Needs you" and "In progress". The instance is
 * driven by hand here rather than answering at once, because what these loops
 * are for is the time between a question and its answer: the connections they
 * hold, when they let go of them, and what the navigation says while it has
 * none.
 */
type Round = { request: Request; answer: (response: Response) => void; refuse: () => void };

/** Which list a round belongs to — the two loops run side by side. */
type List = "needs-you" | "in-progress";

const rounds: Round[] = [];

function installHeldInstance() {
  rounds.length = 0;

  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const request = input instanceof Request ? input : new Request(input, init);

      return new Promise<Response>((answer, reject) => {
        const refuse = () => reject(new Error("the instance did not answer"));
        request.signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
        rounds.push({ request, answer, refuse });
      });
    }),
  );
}

function page(total: number, etag: string): Response {
  return new Response(JSON.stringify({ items: [], total, has_more: false, next_cursor: null, agents: 1 }), {
    status: 200,
    headers: { "content-type": "application/json", etag },
  });
}

/** What the navigation would draw, and how often it was told to look again. */
function Probe({ project }: { project: string | undefined }) {
  const { needsYou, inProgress, pulse } = useAttentionState(project);

  return <p>{`${drawn(needsYou)} · ${drawn(inProgress)} · ${pulse}`}</p>;
}

function drawn(count: number | null): string {
  return count === null ? "nothing known" : String(count);
}

function shown(): string {
  return screen.getByText(/·/).textContent!;
}

function of(request: Request): List {
  return new URL(request.url).pathname.endsWith("/needs-you") ? "needs-you" : "in-progress";
}

function all(list: List): Round[] {
  return rounds.filter((held) => of(held.request) === list);
}

// Generous, because a round the instance answered `304` waits out the loop's
// floor before it asks again — the one thing that keeps a `304` nobody waited
// for from becoming a spin.
async function round(list: List, at: number): Promise<Round> {
  await waitFor(() => expect(all(list).length).toBeGreaterThan(at), { timeout: 3_000 });
  return all(list)[at];
}

function query(request: Request) {
  return new URL(request.url).searchParams;
}

let hidden = false;

Object.defineProperty(document, "hidden", { configurable: true, get: () => hidden });

function look(away: boolean) {
  hidden = away;
  act(() => document.dispatchEvent(new Event("visibilitychange")));
}

afterEach(() => {
  vi.unstubAllGlobals();
  hidden = false;
});

it("reads once plainly, then holds a read against the last validator", async () => {
  installHeldInstance();
  render(<Probe project="PLAN" />);

  // The first read is a plain one. A held read with no validator would wait
  // for a first item, and a list that is empty is exactly what the navigation
  // has to be able to say.
  const first = await round("needs-you", 0);
  expect(query(first.request).get("limit")).toBe("1");
  expect(query(first.request).get("wait")).toBeNull();
  expect(first.request.headers.get("If-None-Match")).toBeNull();

  // Nothing changed yet — the first answer is what a screen reading the same
  // list has just read for itself, so no pulse goes with it.
  await act(async () => first.answer(page(2, '"a"')));
  expect(shown()).toBe("2 · nothing known · 0");

  const second = await round("needs-you", 1);
  expect(query(second.request).get("wait")).toBe("60");
  expect(second.request.headers.get("If-None-Match")).toBe('"a"');

  // The instance said the list changed, so whoever else reads it asks again.
  await act(async () => second.answer(page(3, '"b"')));
  await waitFor(() => expect(shown()).toBe("3 · nothing known · 1"));

  // A round that reached its deadline changes nothing and is asked again with
  // the validator it went out with.
  const third = await round("needs-you", 2);
  expect(third.request.headers.get("If-None-Match")).toBe('"b"');
  await act(async () => third.answer(new Response(null, { status: 304, headers: { etag: '"b"' } })));

  const fourth = await round("needs-you", 3);
  expect(fourth.request.headers.get("If-None-Match")).toBe('"b"');
  expect(shown()).toBe("3 · nothing known · 1");
});

it("counts what is in progress on the filter the navigation entry carries", async () => {
  installHeldInstance();
  render(<Probe project="PLAN" />);

  const first = await round("in-progress", 0);
  // The filter of the "In progress" entry of `views.ts`, and the project the
  // wake channel belongs to — without it the instance refuses to wait.
  expect(query(first.request).getAll("status")).toEqual(["in_progress"]);
  expect(query(first.request).get("project")).toBe("PLAN");
  expect(query(first.request).get("limit")).toBe("1");
  expect(query(first.request).get("wait")).toBeNull();

  await act(async () => first.answer(page(5, '"p"')));
  expect(shown()).toBe("nothing known · 5 · 0");

  // A list of its own, a validator of its own: it waits on the change of the
  // page it asked for and not on whatever "Needs you" is doing.
  const second = await round("in-progress", 1);
  expect(second.request.headers.get("If-None-Match")).toBe('"p"');
  expect(query(second.request).get("wait")).toBe("60");

  // The second number is an observation and wakes nobody: the pulse belongs to
  // "Needs you", whose screen shares this frame's read of it.
  await act(async () => second.answer(page(4, '"q"')));
  await waitFor(() => expect(shown()).toBe("nothing known · 4 · 0"));
});

it("holds one connection per list and neither waits on the other", async () => {
  installHeldInstance();
  render(<Probe project="PLAN" />);

  await act(async () => (await round("needs-you", 0)).answer(page(1, '"a"')));
  await act(async () => (await round("in-progress", 0)).answer(page(7, '"p"')));

  await round("needs-you", 1);
  await round("in-progress", 1);

  // Two lists, two held reads, and not one more: under HTTP/1.1 the browser
  // has six connections per origin and the clicks need the rest.
  expect(all("needs-you")).toHaveLength(2);
  expect(all("in-progress")).toHaveLength(2);
  expect(shown()).toBe("1 · 7 · 0");
});

it("keeps the number it last knew when the instance stops answering", async () => {
  installHeldInstance();
  render(<Probe project="PLAN" />);

  await act(async () => (await round("needs-you", 0)).answer(page(4, '"a"')));
  expect(shown()).toBe("4 · nothing known · 0");

  await act(async () => (await round("needs-you", 1)).refuse());

  // A navigation carries no error banner, and an instance that is restarting
  // is not a reason to forget what was true a second ago.
  expect(shown()).toBe("4 · nothing known · 0");
});

it("holds nothing while nobody is looking, and reads at once on coming back", async () => {
  installHeldInstance();
  render(<Probe project="PLAN" />);

  await act(async () => (await round("needs-you", 0)).answer(page(1, '"a"')));
  await act(async () => (await round("in-progress", 0)).answer(page(2, '"p"')));
  const waiting = await round("needs-you", 1);
  const other = await round("in-progress", 1);

  look(true);

  // Under HTTP/1.1 the browser has six connections per origin; a tab nobody is
  // looking at may not spend one of them on a number nobody can see.
  await waitFor(() => expect(waiting.request.signal.aborted).toBe(true));
  await waitFor(() => expect(other.request.signal.aborted).toBe(true));
  expect(rounds).toHaveLength(4);

  look(false);

  const back = await round("needs-you", 2);
  expect(back.request.headers.get("If-None-Match")).toBe('"a"');
  expect((await round("in-progress", 2)).request.headers.get("If-None-Match")).toBe('"p"');
  expect(shown()).toBe("1 · 2 · 0");
});

it("drops the reads of a project that is no longer open", async () => {
  installHeldInstance();
  const { unmount } = render(<Probe project="PLAN" />);

  const first = await round("needs-you", 0);
  const second = await round("in-progress", 0);
  unmount();

  await waitFor(() => expect(first.request.signal.aborted).toBe(true));
  await waitFor(() => expect(second.request.signal.aborted).toBe(true));
});
