import { useContext, useEffect, useState } from "react";
import { api } from "@/api/client";
import { AttentionContext, type Attention } from "./attention";
import { viewOf } from "./views";

/**
 * How long one held read waits to be told before it is answered `304` and
 * asked again. The instance allows up to an hour and an operator's proxy has
 * to allow that (`docs/operations.md`), but a shorter round is what a browser
 * on a laptop that sleeps and a phone that changes network want: a connection
 * that died without saying so is noticed within a minute rather than an hour.
 */
const round = 60;

/** How long the loop waits after a failed read, doubling, up to the second. */
const firstRetry = 1_000;
const longestRetry = 30_000;

/** The least a round may take, so that nothing can turn the loop into a spin. */
const floor = 1_000;

/**
 * Which issues the number beside "In progress" counts. It is the filter the
 * navigation entry already carries rather than a second copy of it: the number
 * and the screen it links to have to answer the same question, and two places
 * saying `in_progress` are two places to change it.
 */
const inProgressFilter = viewOf("in-progress").filter ?? {};

type Known = { of: string; needsYou: number | null; inProgress: number | null; pulse: number };

/**
 * The frame's reads of the two lists the navigation counts, and the wake pulse
 * the screen that lists the same work watches.
 *
 * They live once per project, above both the sidebar and the screens, because
 * a screen and the number read the same list and may not each hold a
 * connection for it: under HTTP/1.1 a browser has six per origin, and every
 * held one is a click that has to wait. That is one connection per list and
 * tab — two — and `limit=1` on both, because only `total` is wanted; the items
 * are the screen's.
 *
 * Each loop is one read after another: the first plain, so that a list that is
 * empty is known to be empty, then held ones carrying that list's last `ETag`,
 * each of which the instance answers the moment the list differs — the wake
 * channel of `docs/api.md`, Waiting, which the CLI has used since it existed.
 */
export function useAttentionState(project: string | undefined): Attention {
  const [known, setKnown] = useState<Known | null>(null);

  useEffect(() => {
    if (project === undefined) {
      return;
    }

    // One controller for both loops: a read for a project that is no longer
    // open is dropped rather than allowed to answer into the wrong navigation.
    const stop = new AbortController();

    // The two lists have validators of their own and change at their own
    // times, so they wait separately. Neither waits on the other's answer.
    void watch(
      reading((validator, signal) =>
        api.GET("/projects/{key}/needs-you", {
          params: { path: { key: project }, query: { limit: 1, wait: held(validator) } },
          headers: matching(validator),
          signal,
        }),
      ),
      (count, woken) =>
        setKnown((was) => {
          const now = carried(was, project);
          return { ...now, needsYou: count, pulse: now.pulse + (woken ? 1 : 0) };
        }),
      stop.signal,
    );

    void watch(
      reading((validator, signal) =>
        api.GET("/issues", {
          params: {
            // `wait` needs the project — the wake channel is a project's, and a
            // list across projects has none to hang on (`docs/api.md`).
            query: { ...inProgressFilter, status: inProgressFilter.status as never, project, limit: 1, wait: held(validator) },
          },
          headers: matching(validator),
          signal,
        }),
      ),
      // No pulse: nothing else reads this list through the frame. The screen
      // that lists it has its own read, and giving it a wake impulse is a
      // question for the epic and not a decision made in passing here.
      (count) => setKnown((was) => ({ ...carried(was, project), inProgress: count })),
      stop.signal,
    );

    return () => stop.abort();
  }, [project]);

  const mine = known !== null && known.of === project;

  return {
    needsYou: mine ? known.needsYou : null,
    inProgress: mine ? known.inProgress : null,
    pulse: mine ? known.pulse : 0,
  };
}

/** The same numbers and the same pulse, for the sidebar and for the screen. */
export function useAttention(): Attention {
  const attention = useContext(AttentionContext);

  if (attention === null) {
    throw new Error("useAttention is only for the frame and the screens under it.");
  }

  return attention;
}

/** What one loop knows so far, or an empty slate when the project just changed. */
function carried(was: Known | null, project: string): Known {
  return was !== null && was.of === project ? was : { of: project, needsYou: null, inProgress: null, pulse: 0 };
}

/** The first read of a list is a plain one; every one after it is held. */
function held(validator: string | undefined): number | undefined {
  return validator === undefined ? undefined : round;
}

function matching(validator: string | undefined): { "If-None-Match": string } | undefined {
  return validator === undefined ? undefined : { "If-None-Match": validator };
}

type Answer =
  | { at: "count"; total: number; validator: string | undefined }
  | { at: "same" }
  | { at: "interrupted" }
  | { at: "failed" };

type Read = (validator: string | undefined, signal: AbortSignal) => Promise<Answer>;

type Call = (
  validator: string | undefined,
  signal: AbortSignal,
) => Promise<{ data?: { total: number } | undefined; response: Response }>;

/**
 * One read of a list, held when there is a validator to hold it against. It is
 * given up when the tab goes away as well as when the project does: nobody is
 * looking, and the connection is worth more to the tab that is.
 */
function reading(call: Call): Read {
  return async (validator, outer) => {
    const request = new AbortController();
    const give = () => request.abort();
    const hide = () => {
      if (document.hidden) {
        request.abort();
      }
    };

    outer.addEventListener("abort", give, { once: true });
    document.addEventListener("visibilitychange", hide);

    try {
      const { data, response } = await call(validator, request.signal);

      if (response.status === 304) {
        return { at: "same" };
      }

      return data === undefined
        ? { at: "failed" }
        : { at: "count", total: data.total, validator: response.headers.get("ETag") ?? undefined };
    } catch {
      return request.signal.aborted ? { at: "interrupted" } : { at: "failed" };
    } finally {
      outer.removeEventListener("abort", give);
      document.removeEventListener("visibilitychange", hide);
    }
  };
}

/**
 * The loop over one list: read, keep what came back, read again. `woken` is
 * false for the first answer, which is what a screen reading the same list has
 * just read for itself, and true for every one the instance handed over
 * because the list had changed.
 */
async function watch(read: Read, landed: (count: number, woken: boolean) => void, stop: AbortSignal): Promise<void> {
  let validator: string | undefined;
  let failures = 0;

  while (!stop.aborted) {
    // A hidden tab holds nothing. Coming back reads at once, so that the
    // number is right before the human has looked at it.
    await whileVisible(stop);
    if (stop.aborted) {
      return;
    }

    const answer = await read(validator, stop);

    if (answer.at === "interrupted") {
      continue;
    }

    if (answer.at === "failed") {
      // The number stays as it is. An instance that is restarting is not a
      // reason for the navigation to forget what it last knew, and not a
      // reason to keep asking it either.
      failures += 1;
      await pause(Math.min(firstRetry * 2 ** (failures - 1), longestRetry), stop);
      continue;
    }

    failures = 0;

    if (answer.at === "same") {
      // The instance only answers `304` at its deadline, so this has already
      // waited a round. Something in between that answered it out of a cache
      // would not have, and a loop that trusted that would spin against the
      // instance for as long as the tab is open.
      await pause(floor, stop);
      continue;
    }

    const first = validator === undefined;
    validator = answer.validator;
    landed(answer.total, !first);

    // No validator, no wake channel — something between here and the instance
    // dropped the header. Reading again at once would be a tight loop, so this
    // falls back to asking once a round.
    if (validator === undefined) {
      await pause(round * 1_000, stop);
    }
  }
}

/** Resolves as soon as somebody could be looking at this tab, or never mind. */
function whileVisible(signal: AbortSignal): Promise<void> {
  if (!document.hidden || signal.aborted) {
    return Promise.resolve();
  }

  return new Promise((go) => {
    const done = () => {
      document.removeEventListener("visibilitychange", shown);
      signal.removeEventListener("abort", done);
      go();
    };
    const shown = () => {
      if (!document.hidden) {
        done();
      }
    };

    document.addEventListener("visibilitychange", shown);
    signal.addEventListener("abort", done);
  });
}

function pause(milliseconds: number, signal: AbortSignal): Promise<void> {
  if (signal.aborted) {
    return Promise.resolve();
  }

  return new Promise((go) => {
    const done = () => {
      clearTimeout(timer);
      signal.removeEventListener("abort", done);
      go();
    };
    const timer = setTimeout(done, milliseconds);

    signal.addEventListener("abort", done);
  });
}
