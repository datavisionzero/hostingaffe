import { useCallback, useEffect, useRef, useState } from "react";
import { describe, type Problem } from "@/api/client";

/** What a screen knows about one thing it asked the instance for. */
export type Asked<T> = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; value: T };

type Answer<T> = { data?: T; error?: Problem; response: Response };

const asking = { at: "asking" } as const;

/**
 * One question a screen asks the instance about the address it is on.
 *
 * The address is the first argument and not a dependency of the second,
 * because it is the answer's own label: a screen that walks from one machine to
 * the next would otherwise show the previous machine's answer under the new
 * key for as long as the request takes, and a slow answer that arrives after
 * the walk would overwrite the right one. An answer that is not about the
 * address being shown is not shown.
 *
 * A record screen asks several of these — the machine, its installations, its
 * files, its pages, its history — and each stands on its own: the fields are
 * readable while the history is still coming, and a section that fails says so
 * in its own place rather than taking the screen with it.
 */
export function useAsk<T>(at: string, ask: (signal: AbortSignal) => Promise<Answer<T>>): {
  asked: Asked<T>;
  /** Ask again — after a write, which is the only thing that makes an answer old. */
  again: () => void;
} {
  const [state, setState] = useState<{ at: string; round: number; asked: Asked<T> }>();
  const [round, setRound] = useState(0);

  // The caller writes the request inline, so it is a new function on every
  // render; `at` and the round are what actually decide when to ask again.
  const request = useEffectEvent(ask);

  useEffect(() => {
    const controller = new AbortController();
    let live = true;

    void (async () => {
      try {
        const { data, error, response } = await request(controller.signal);

        if (live) {
          setState({
            at,
            round,
            asked: data === undefined
              ? { at: "failed", why: describe(error, response.status) }
              : { at: "known", value: data },
          });
        }
      } catch {
        // An aborted request is a screen that walked on, not a failure to report.
        if (live && !controller.signal.aborted) {
          setState({ at, round, asked: { at: "failed", why: "The instance did not answer." } });
        }
      }
    })();

    return () => {
      live = false;
      controller.abort();
    };
    // `request` is deliberately not among them: it is the stable handle of
    // `useEffectEvent`, and listing it would say the effect depends on a
    // function that is new on every render — which is the whole reason the
    // handle exists.
  }, [at, round]);

  return {
    asked: state !== undefined && state.at === at && state.round === round ? state.asked : asking,
    again: useCallback(() => setRound((current) => current + 1), []),
  };
}

/**
 * The latest version of a callback, without it being a reason to run the effect
 * that calls it.
 *
 * React ships this as `useEffectEvent`, and while that is still experimental in
 * the version this application is on, it is these five lines — the same shape,
 * under the same name, so that the day it can be imported this function goes
 * and nothing that calls it changes.
 */
function useEffectEvent<A extends unknown[], R>(callback: (...args: A) => R): (...args: A) => R {
  const latest = useRef(callback);

  useEffect(() => {
    latest.current = callback;
  });

  return useCallback((...args: A) => latest.current(...args), []);
}
