import { describe, type Problem } from "@/api/client";

/**
 * A write, and the one sentence it is allowed to leave behind.
 *
 * What the instance refused is what the reader is owed: the last administrator
 * who cannot be demoted, a token that was already revoked, an invitation that
 * did not go out. A `.then(load)` that never looked at the answer said none of
 * it — the list reloaded unchanged and the screen stayed silent — and a notice
 * set from that same `.then()` said the opposite of what happened.
 *
 * The reload only follows a write that went through, because a refused one
 * changed nothing. The body of the answer is handed back for the caller that
 * needs it; a write answered with `204` carries none.
 */
export function reporting(setNotice: (notice: string) => void, reload: () => Promise<void>) {
  return async function report<T>(write: Promise<{ data?: T; error?: Problem; response: Response }>, said: string): Promise<T | undefined> {
    try {
      const { data, error, response } = await write;

      if (!response.ok) {
        setNotice(describe(error, response.status));
        return undefined;
      }

      setNotice(said);
      await reload();
      return data;
    } catch {
      setNotice("The instance did not answer.");
      return undefined;
    }
  };
}
