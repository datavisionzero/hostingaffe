/**
 * The object a `stale` refusal carries, or nothing.
 *
 * `docs/api.md` ("Concurrency on text fields") has the refusal hand the
 * current object back so the client can merge and try again — and a client
 * that drops it turns the guard into a dead end, because every further write
 * carries the same version and is refused for the same reason. Whoever sends
 * `If-Match` takes what comes back: the new version for the next write, and
 * the other side's text where there is typed text to merge.
 */
export function stale<T extends { updated_at: string }>(
  answer: { error?: unknown; response: Response },
): T | undefined {
  if (answer.response.status !== 412) {
    return undefined;
  }

  const current = (answer.error as { current?: T } | undefined)?.current;

  return current?.updated_at === undefined ? undefined : current;
}
