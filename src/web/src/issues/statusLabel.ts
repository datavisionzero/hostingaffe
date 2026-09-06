import type { IssueSummary } from "@/api/client";

/**
 * The status as the product spells it. Its own module beside `status.tsx`
 * because that file draws a component, and a file that exports both loses
 * fast refresh — the same reason `priority.ts` sits beside its callers.
 */
export type Status = IssueSummary["status"];

const labels: Record<Status, string> = {
  backlog: "backlog",
  todo: "todo",
  in_progress: "in progress",
  review: "review",
  done: "done",
  canceled: "canceled",
};

export function statusLabel(status: Status): string {
  return labels[status];
}
