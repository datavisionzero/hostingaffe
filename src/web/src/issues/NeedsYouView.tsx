import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { api, describe, type IssueSummary, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/shared/PageHeader";
import { stale } from "@/shared/stale";
import { useAttention } from "@/shell/useAttention";
import { keyPath } from "@/shell/views";
import { StatusDot } from "./status";

type Because = Schemas["NeedsYouBecause"];
type Item = Schemas["NeedsYouItem"];

type Page =
  | { at: "asking" }
  | { at: "failed"; why: string }
  | { at: "known"; items: Item[]; total: number; nextCursor: string | null; agents: number };

const pageSize = 50;

/** What the CLI calls the four groups, so that one list is one list. */
const headings: Record<Because, string> = {
  question: "Open questions",
  review: "In review",
  unready: "Not ready",
  stuck: "Stuck",
};

const actions: Record<Because, string> = {
  question: "Answer",
  review: "Review",
  unready: "Set ready",
  stuck: "See blockers",
};

/**
 * "Needs you" is not a filtered issue list but its own read: `GET
 * /projects/{key}/needs-you` classifies each issue by the one thing that put
 * it there — an open question, a result in review, missing `ready` where the
 * project triages, or a chain of blockers that ends in a dead end (CONTEXT,
 * "Needs you" and "Stuck").
 *
 * The instance answers in human-action order and within a group by priority,
 * so the groups are cut where `because` changes rather than re-sorted here.
 * Every row says why it is on the list and carries the one action that takes
 * it off again.
 */
export function NeedsYouView() {
  const { project } = useParams();
  // The wake pulse of the frame, not a second connection: the number in the
  // navigation and this screen read the same list, and a screen that stood
  // still beside a number that moved would contradict it.
  const { pulse } = useAttention();
  const [known, setKnown] = useState<{ of: string | undefined; page: Page } | null>(null);
  const [again, setAgain] = useState(0);
  const [more, setMore] = useState<{ busy: boolean; why?: string }>({ busy: false });
  const page: Page = known !== null && known.of === project ? known.page : { at: "asking" };

  const ask = useCallback(
    async (cursor: string | undefined): Promise<Page> => {
      try {
        const { data, error, response } = await api.GET("/projects/{key}/needs-you", {
          params: { path: { key: project! }, query: { cursor, limit: pageSize } },
        });

        return data === undefined
          ? { at: "failed", why: describe(error, response.status) }
          : { at: "known", items: data.items, total: data.total, nextCursor: data.next_cursor, agents: data.agents };
      } catch {
        return { at: "failed", why: "The instance did not answer." };
      }
    },
    [project],
  );

  useEffect(() => {
    let current = true;

    void (async () => {
      const answer = await ask(undefined);

      if (current) {
        setMore({ busy: false });
        setKnown({ of: project, page: answer });
      }
    })();

    return () => {
      current = false;
    };
    // A pulse is read from the top: the change it announces may have moved
    // rows into and out of every group, and pages loaded under the old list
    // are pages of a list that is gone.
  }, [again, ask, project, pulse]);

  async function loadMore(cursor: string) {
    setMore({ busy: true });
    const answer = await ask(cursor);

    if (answer.at === "failed") {
      setMore({ busy: false, why: answer.why });
      return;
    }

    setMore({ busy: false });
    setKnown((current) =>
      current !== null && current.of === project && current.page.at === "known" && answer.at === "known"
        ? {
            of: project,
            page: { at: "known", items: [...current.page.items, ...answer.items], total: answer.total, nextCursor: answer.nextCursor, agents: answer.agents },
          }
        : current,
    );
  }

  const meta =
    page.at === "known" ? (page.items.length < page.total ? `${page.items.length} of ${page.total}` : `${page.total}`) : undefined;

  return (
    <>
      <PageHeader title="Needs you" meta={meta}>
        <Button size="sm" render={<Link to={`/${project}/issues/new`} />}>New issue</Button>
      </PageHeader>
      {page.at === "asking" && (
        <div className="space-y-3 p-4">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}
      {page.at === "failed" && <p className="p-4 text-sm text-destructive">{page.why}</p>}
      {/* A fact about the instance, said once beside the list rather than turned
          into entries on it: without an agent nothing here gets worked off, and
          the one thing to do about it is not an issue on this list. */}
      {page.at === "known" && page.agents === 0 && (
        <p className="border-b bg-amber-500/5 px-4 py-2 text-sm">
          No agent can pick work up on this instance, so nothing here will be worked off.{" "}
          <code className="font-mono text-xs">ha agent create &lt;name&gt;</code> makes one.
        </p>
      )}
      {page.at === "known" && page.items.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">Nothing needs you.</p>
          <p className="text-sm text-muted-foreground">
            No open question, nothing in review, nothing waiting for triage, and no blocker chain an agent cannot clear.
          </p>
        </div>
      )}
      {page.at === "known" && page.items.length > 0 && (
        <div>
          {group(page.items).map((section) => (
            <section key={section.because}>
              <h2 className="border-b bg-muted/40 px-4 py-1.5 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                {headings[section.because]} · {section.items.length}
              </h2>
              <ul className="divide-y">
                {section.items.map((item) => (
                  <Row key={item.issue.key} item={item} onResolved={() => setAgain((count) => count + 1)} />
                ))}
              </ul>
            </section>
          ))}
          <div className="p-4">
            {page.nextCursor !== null && (
              <Button variant="outline" size="sm" disabled={more.busy} onClick={() => void loadMore(page.nextCursor!)}>
                {more.busy ? "Loading…" : "Show more"}
              </Button>
            )}
            {more.why !== undefined && <p role="alert" className="mt-2 text-sm text-destructive">{more.why}</p>}
          </div>
        </div>
      )}
    </>
  );
}

function Row({ item, onResolved }: { item: Item; onResolved: () => void }) {
  const issue = item.issue;

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2">
      <StatusDot status={issue.status} />
      <Link className="font-mono text-xs text-brand hover:underline" to={keyPath(issue.key)}>
        {issue.key}
      </Link>
      <span className="min-w-0 flex-1 basis-40 truncate text-sm">{issue.title}</span>
      {/* The reason and the action wrap together onto the second line of a
          narrow row rather than out of it: they are what this screen is for. */}
      <span className="text-xs text-muted-foreground">{reason(item)}</span>
      <span className="ml-auto">
        {item.because === "unready" ? (
          <SetReady issue={issue} onDone={onResolved} />
        ) : (
          <Button variant="outline" size="sm" render={<Link to={keyPath(issue.key)} />}>
            {actions[item.because]}
          </Button>
        )}
      </span>
    </li>
  );
}

/**
 * Triage, in place: this is the whole of what the row asks for, and the issue
 * screen would only offer the same switch. The list is read again afterwards
 * rather than patched here — what else the change moved, or brought onto the
 * list behind it, is the instance's answer and not ours to guess.
 */
function SetReady({ issue, onDone }: { issue: IssueSummary; onDone: () => void }) {
  const [busy, setBusy] = useState(false);
  const [why, setWhy] = useState<string>();
  // What a refusal handed back, against the row it was refused for. Nothing is
  // typed here, so there is nothing to merge — but pressing again has to be
  // able to work, and it cannot while the row keeps sending the version the
  // list was read at. A row that has since been read again invalidates it.
  const [adopted, setAdopted] = useState<{ from: string; version: string }>();
  const version = adopted?.from === issue.updated_at ? adopted.version : issue.updated_at;

  async function run() {
    setBusy(true);
    setWhy(undefined);

    try {
      const answer = await api.PATCH("/issues/{key}", {
        params: { path: { key: issue.key } },
        headers: { "If-Match": version },
        // Only `ready`, and nothing beside it. A `PATCH` reads three states
        // and not two — absent, present as `null`, present with a value — and
        // present as `null` is what clears a field (`docs/api.md`, "Change the
        // fields present; `null` clears"). Naming the other fields as `null`
        // to satisfy the generated type, which marks every property of the
        // body required, wrote the description, the result, the assignee, the
        // epic and the parent away with one press. The cast is the way past
        // that type, as at the same switch on the issue screen.
        body: { ready: true } as never,
      });

      const current = stale<{ updated_at: string }>(answer);
      if (current !== undefined) {
        setAdopted({ from: issue.updated_at, version: current.updated_at });
        setWhy("It changed since this list was read. Set it again.");
        return;
      }

      if (answer.data === undefined) {
        setWhy(describe(answer.error, answer.response.status));
        return;
      }

      onDone();
    } catch {
      setWhy("The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <span className="flex items-center gap-2">
      {why !== undefined && <span role="alert" className="text-xs text-destructive">{why}</span>}
      <Button variant="outline" size="sm" disabled={busy} onClick={() => void run()}>
        {busy ? "Working…" : actions.unready}
      </Button>
    </span>
  );
}

function reason(item: Item): string {
  switch (item.because) {
    case "question":
      return item.issue.open_questions > 1
        ? `${item.issue.open_questions} open questions wait for an answer.`
        : "An open question waits for an answer.";
    case "review":
      return "The result is handed in and waits for a decision.";
    case "unready":
      return "Not ready, and this project triages before an agent may take it.";
    case "stuck":
      return "Its chain of blockers ends where no agent can go on.";
  }
}

/** The groups the instance's order already describes, cut where it changes. */
function group(items: Item[]): Array<{ because: Because; items: Item[] }> {
  const sections: Array<{ because: Because; items: Item[] }> = [];

  for (const item of items) {
    const last = sections.at(-1);

    if (last !== undefined && last.because === item.because) {
      last.items.push(item);
    } else {
      sections.push({ because: item.because, items: [item] });
    }
  }

  return sections;
}
