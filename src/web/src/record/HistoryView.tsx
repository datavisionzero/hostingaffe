import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Filters, type Filter } from "@/shared/Filters";
import { word } from "@/shared/narrowing";
import { dayName, time } from "@/shared/when";
import { pagePath } from "@/shell/views";
import { filePath, installationPath, machinePath, softwarePath } from "./addresses";
import { values } from "./changes";

type HistoryEvent = Schemas["HistoryEvent"];
type MachineSummary = Schemas["MachineSummary"];

/** The kinds of subject the endpoint keeps apart, which are the model's own. */
const filters: Filter[] = [
  {
    name: "kind",
    label: "Kind",
    values: ["machine", "software", "installation", "deployment", "file", "page"],
  },
];

/** What one call brings, and what the walk continues by. */
const page = 50;

/**
 * What has been going on (VISION 6.2): every change to the record, newest
 * first, with the deployments among them.
 *
 * The detail screens answer what became of one thing. This is the other
 * question — the one somebody asks coming back on a Monday — and it is the
 * whole record at once, grouped by day, so that "nothing happened last week"
 * is as readable as a version that moved.
 */
export function HistoryView() {
  const [params, setParams] = useSearchParams();
  const machine = word(params, "machine");
  const kind = word(params, "kind");

  // The machines to narrow by, retired ones among them: what happened on a
  // machine that was taken out of service is exactly what somebody looks for
  // while it is being taken apart.
  const machines = useAsk<MachineSummary[]>("machines", (signal) =>
    api.GET("/api/machines", { params: { query: { retired: true } }, signal }));

  return (
    <>
      <PageHeader title="History" />

      <Filters
        filters={filters}
        params={params}
        setParams={setParams}
        pick={{
          name: "machine",
          label: "Machine",
          placeholder: "Every machine",
          empty: machines.asked.at === "known" ? "No machine of that name." : "Asking the instance…",
          choices: machines.asked.at === "known"
            ? machines.asked.value.map((one) => ({ id: one.key, name: one.key, hint: one.name }))
            : [],
        }}
      />

      {/* A new narrowing is a new walk, and the walk is what the feed holds:
          keyed by the question, it starts again rather than growing a second
          answer onto the first. */}
      <Feed key={`${machine ?? ""}|${kind ?? ""}`} machine={machine} kind={kind} />
    </>
  );
}

/**
 * The events under one narrowing, and the walk through them.
 *
 * The first page is the question this component was mounted with; every page
 * after it is a click, asked for in the handler and added to what is on the
 * screen. That is why the narrowing is this component's key: a walk belongs to
 * the question it started from, and the way to end it is to unmount it.
 */
function Feed({ machine, kind }: { machine?: string; kind?: string }) {
  const { asked } = useAsk<HistoryEvent[]>(`${machine ?? ""}|${kind ?? ""}`, (signal) =>
    api.GET("/api/history", { params: { query: { machine, kind, limit: page } }, signal }));

  const [added, setAdded] = useState<HistoryEvent[]>([]);
  const [walking, setWalking] = useState(false);
  const [end, setEnd] = useState(false);
  const [failed, setFailed] = useState<string>();

  const events = asked.at === "known" ? [...asked.value, ...added] : [];
  // A page that came back full is a page that may have a next one; a short
  // one is the end, and so is a walk that has been told so.
  const more = asked.at === "known" && !end && (added.length > 0 || asked.value.length === page);

  async function walk() {
    const last = events[events.length - 1];
    if (last === undefined) return;

    setWalking(true);
    setFailed(undefined);

    const { data, error, response } = await api.GET("/api/history", {
      params: { query: { machine, kind, before: last.cursor, limit: page } },
    });

    setWalking(false);

    if (data === undefined) {
      setFailed(describe(error, response.status));
      return;
    }

    setAdded((seen) => [...seen, ...data]);
    if (data.length < page) setEnd(true);
  }

  return (
    <>
      {machine !== undefined && (
        <p className="border-b px-4 py-2 text-xs text-muted-foreground">
          On <Link className="text-brand hover:underline" to={machinePath(machine)}>{machine}</Link>,
          its installations, their deployments, the files of both and the pages attached to either.
        </p>
      )}

      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="p-4 text-sm text-destructive">{asked.why}</p>}

      {asked.at === "known" && events.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">Nothing has happened here.</p>
          <p className="max-w-md text-sm text-muted-foreground">
            Every change to the record stands here — a field that was edited, a file that was written, a
            version that went live. Nothing yet does not mean nothing is recorded.
          </p>
        </div>
      )}

      {events.length > 0 && (
        <div className="min-h-0 flex-1 overflow-auto">
          {days(events).map(([day, ofThatDay]) => (
            <section key={day}>
              <h2 className="sticky top-0 border-b bg-background/95 px-4 py-1.5 text-xs font-medium text-muted-foreground backdrop-blur">
                {day}
              </h2>
              <ul className="divide-y">
                {ofThatDay.map((event) => (
                  <Event key={event.cursor} event={event} ofOneMachine={machine !== undefined} />
                ))}
              </ul>
            </section>
          ))}

          <div className="flex flex-col items-center gap-2 p-4">
            {failed !== undefined && <p className="text-sm text-destructive">{failed}</p>}
            {more
              ? (
                <Button variant="outline" size="sm" disabled={walking} onClick={() => void walk()}>
                  {walking ? "Loading…" : "Load more"}
                </Button>
              )
              : <p className="text-xs text-muted-foreground">That is everything.</p>}
          </div>
        </div>
      )}
    </>
  );
}

/** One event as a line: when, who, and what happened to what. */
function Event({ event, ofOneMachine }: { event: HistoryEvent; ofOneMachine: boolean }) {
  return (
    <li className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 px-4 py-1.5 text-sm">
      <span className="w-16 shrink-0 font-mono text-xs whitespace-nowrap text-muted-foreground">{time(event.at)}</span>
      <span className="w-28 shrink-0 truncate text-xs text-muted-foreground">{event.actor.name}</span>

      <span className="min-w-0 flex-1">
        <Subject event={event} />{" "}
        <Changes event={event} />
        {event.note !== null && event.note !== "" && (
          <span className="text-xs text-muted-foreground italic"> — {event.note}</span>
        )}
      </span>

      {/* Which machine it happened on. A reading that is already one machine's
          leaves the column out rather than printing the same key down the
          whole screen. */}
      {!ofOneMachine && (
        <span className="hidden w-32 shrink-0 truncate text-right font-mono text-xs text-muted-foreground sm:block">
          {event.machine ?? ""}
        </span>
      )}
    </li>
  );
}

/**
 * What it happened to, linked where the record still holds it.
 *
 * A subject the purge has taken is text: the event survives what it describes
 * (VISION 7), and a link to an address nothing answers to would be worse than
 * the plain word.
 */
function Subject({ event }: { event: HistoryEvent }) {
  const kind = <span className="text-xs text-muted-foreground">{event.subject_kind}</span>;

  if (event.subject === null) {
    return <>{kind} <span className="text-muted-foreground italic">gone</span></>;
  }

  const name = <span className="font-mono text-xs">{event.subject}</span>;
  const to = address(event);

  return (
    <>
      {kind}{" "}
      {to === undefined ? name : <Link className="text-brand hover:underline" to={to}>{name}</Link>}
      {event.number !== null && <span className="text-xs text-muted-foreground"> #{event.number}</span>}
    </>
  );
}

function address(event: HistoryEvent): string | undefined {
  const subject = event.subject;
  if (subject === null) return undefined;

  switch (event.subject_kind) {
    case "machine": return machinePath(subject);
    case "software": return softwarePath(subject);
    // A deployment is addressed by the installation it lives under: that is
    // where its version, its ref and its note are read.
    case "installation": case "deployment": return installationPath(subject);
    case "file": return event.owner === null ? undefined : filePath(event.owner, subject);
    case "page": return pagePath(subject);
    default: return undefined;
  }
}

/**
 * The fields one act changed, from what to what.
 *
 * A text records that it changed and not how (`docs/storage.md`), so a body
 * with no values says exactly that; a deployment's one change is its version,
 * and the version before it where there was one. The values are spelled by the
 * one helper the section of a detail page uses too, so a birth does not print
 * its own subject back and a moment is a date rather than a column's raw form.
 */
function Changes({ event }: { event: HistoryEvent }) {
  return (
    <>
      {event.changes.map((change, index) => {
        const value = values(change, event.subject);

        return (
          <span key={`${change.field}:${String(index)}`}>
            {index > 0 && <span className="text-muted-foreground">, </span>}
            <span className="font-mono text-xs">{change.field}</span>
            {value.old !== null && (
              <> <span className="text-muted-foreground line-through">{value.old}</span></>
            )}
            {value.new !== null && <> → {value.new}</>}
          </span>
        );
      })}
    </>
  );
}

/** The events by the day they happened on, in the order they came. */
function days(events: HistoryEvent[]): [string, HistoryEvent[]][] {
  const grouped: [string, HistoryEvent[]][] = [];

  for (const event of events) {
    const day = dayName(event.at);
    const last = grouped[grouped.length - 1];

    if (last !== undefined && last[0] === day) last[1].push(event);
    else grouped.push([day, [event]]);
  }

  return grouped;
}
