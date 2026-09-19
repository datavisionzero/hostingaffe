import { Link, useSearchParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { ago, day } from "@/shared/when";
import { machinePath } from "./addresses";
import { Filters, type Filter } from "@/shared/Filters";
import { found, word } from "@/shared/narrowing";
import { StatusBadge } from "./Parts";

type MachineSummary = Schemas["MachineSummary"];

/** What the list can be narrowed by, which is what the endpoint accepts and nothing beside it. */
const filters: Filter[] = [
  { name: "status", label: "Status", values: ["planned", "active", "retired"] },
  { name: "kind", label: "Kind", values: ["vps", "dedicated", "vm", "local"] },
];

/**
 * The machines — the central list of the record (VISION 6.2). A machine is a
 * computer somebody rents or owns, and this is the screen that answers "what do
 * we have".
 *
 * Retired machines are out of the list until they are asked for. They stay in
 * the record because the history of a machine that is gone is still worth
 * having (VISION 7), but a list that mixed them in would make every reader
 * check the status column of every row before believing it.
 */
export function MachinesView() {
  const [params, setParams] = useSearchParams();
  const status = word(params, "status");
  const kind = word(params, "kind");
  const retired = params.get("retired") === "yes";
  // "Which machine has said nothing for longest" is one click, and it is done
  // here rather than by the endpoint: one team's machines are a screenful, and
  // an order is not a filter.
  const byLastSeen = params.get("quietest") === "yes";
  // The word is not part of what the endpoint is asked either: a team's
  // machines are a screenful, and this narrows the screenful.
  const find = params.get("q") ?? "";
  const at = `${status ?? ""}|${kind ?? ""}|${retired}`;

  const { asked } = useAsk<MachineSummary[]>(at, (signal) =>
    api.GET("/api/machines", {
      // `status=retired` brings them in by itself, which is why the switch is
      // a separate question and not a fourth status.
      params: { query: { status, kind, retired: retired ? true : undefined } },
      signal,
    }));

  const rows = asked.at === "known"
    ? ordered(asked.value, byLastSeen).filter((machine) => found(find, machine.key, machine.name))
    : [];

  const narrowed = find !== "" || status !== undefined || kind !== undefined || retired;

  return (
    <>
      <PageHeader title="Machines" meta={asked.at === "known" ? `${rows.length}` : undefined} />

      <Filters
        filters={filters}
        params={params}
        setParams={setParams}
        find={{ label: "Find", placeholder: "Part of a key or a name", value: find }}
        also={[
          { name: "retired", label: "Include retired", on: retired },
          { name: "quietest", label: "Quietest first", on: byLastSeen },
        ]}
      />

      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="p-4 text-sm text-destructive">{asked.why}</p>}

      {asked.at === "known" && rows.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">{narrowed ? "Nothing matches." : "No machines yet."}</p>
          {!narrowed && (
            <p className="max-w-md text-sm text-muted-foreground">
              A machine is a computer the team rents or owns. The first ones usually arrive from a repository
              rather than by hand: <code className="font-mono text-xs">ha machine add</code>, or the bulk
              import an agent runs once.
            </p>
          )}
        </div>
      )}

      {asked.at === "known" && rows.length > 0 && (
        <ul className="divide-y">
          {rows.map((machine) => (
            <li key={machine.key}>
              <Link to={machinePath(machine.key)} className="flex min-h-10 items-center gap-3 px-4 py-1 hover:bg-accent">
                <span className="w-48 shrink-0 truncate font-mono text-xs text-muted-foreground">{machine.key}</span>
                <span className="min-w-0 flex-1 truncate">{machine.name}</span>
                <span className="hidden w-28 shrink-0 truncate text-xs text-muted-foreground sm:block">
                  {machine.kind}
                </span>
                <span className="hidden w-36 shrink-0 truncate text-xs text-muted-foreground md:block">
                  {machine.provider ?? ""}
                </span>
                <span className="hidden w-28 shrink-0 truncate text-xs text-muted-foreground lg:block">
                  {machine.location ?? ""}
                </span>
                <span className="hidden w-28 shrink-0 truncate text-right text-xs text-muted-foreground lg:block">
                  {/* When the fields were last confirmed against the machine
                      itself, which is not when the row was last edited. */}
                  {machine.measured_at === null ? "never measured" : day(machine.measured_at)}
                </span>
                <span className="hidden w-32 shrink-0 truncate text-right text-xs text-muted-foreground md:block">
                  {/* When the machine last spoke for itself. No threshold, no
                      colour, no badge: the reader judges (VISION 5). */}
                  {machine.last_seen === null ? "" : ago(machine.last_seen)}
                </span>
                <span className="hidden w-16 shrink-0 truncate text-right text-xs text-muted-foreground sm:block">
                  {/* The one column worth reading down ten machines on a Friday
                      afternoon. A word and no colour: whether it is restarted
                      now or on Monday is nobody's judgement but the reader's. */}
                  {machine.reboot_required === true ? "restart" : ""}
                </span>
                <StatusBadge status={machine.status} />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

/**
 * The order of the list: by key, as the instance answers, or the quietest
 * first — a machine that has never reported before one that reported a month
 * ago, because never is longer than a month.
 */
function ordered(machines: MachineSummary[], byLastSeen: boolean): MachineSummary[] {
  if (!byLastSeen) return machines;

  return [...machines].sort((a, b) => {
    if (a.last_seen === b.last_seen) return a.key.localeCompare(b.key);
    if (a.last_seen === null) return -1;
    if (b.last_seen === null) return 1;
    return a.last_seen.localeCompare(b.last_seen);
  });
}
