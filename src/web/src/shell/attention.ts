import { createContext } from "react";

/**
 * How much of the current project is waiting for a human and how much of it is
 * under way, held by the frame so that the navigation can say it without
 * opening the screen (VISION 10, PLAN epic "Attention").
 *
 * They are numbers on links, not notifications: nothing is sent, nothing is
 * addressed at anybody, and there is no read state. Each is read from the list
 * it stands on and never from a counter of its own, so that the day a list
 * learns an addressee, its number learns it in the same move.
 *
 * They stay current by themselves, on the wake channel `ha needs-you --wait`
 * uses (`docs/api.md`, Waiting) — the web application is that mechanism's
 * second user and not a second mechanism. The connections are held here, one
 * per list and tab, so that the sidebar and the screen share them.
 */
export type Attention = {
  /**
   * How many issues only a human can resolve, or `null` while that is not
   * known — before the first answer, and after one that failed. Never `0` for
   * "unknown": a navigation that cannot fail cannot show an error either.
   */
  needsYou: number | null;
  /**
   * How many issues are being worked on, on the same terms. An expired claim
   * is not among them, because the number is what the list answers and the
   * status is derived when it is read (VISION 11).
   */
  inProgress: number | null;
  /**
   * Counts the times the instance said this project's work list changed. It
   * carries no state — like the channel it comes from, it only says "ask
   * again" — and a screen that reads the same list watches it to do so.
   */
  pulse: number;
};

export const AttentionContext = createContext<Attention | null>(null);
