import { Link, useSearchParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { day } from "@/shared/when";
import { machinePath } from "./addresses";
import { Filters, type Filter } from "./Filters";
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
  const status = params.get("status") ?? undefined;
  const kind = params.get("kind") ?? undefined;
  const retired = params.get("retired") === "yes";
  const at = `${status ?? ""}|${kind ?? ""}|${retired}`;

  const { asked } = useAsk<MachineSummary[]>(at, (signal) =>
    api.GET("/api/machines", {
      // `status=retired` brings them in by itself, which is why the switch is
      // a separate question and not a fourth status.
      params: { query: { status, kind, retired: retired ? true : undefined } },
      signal,
    }));

  const narrowed = status !== undefined || kind !== undefined || retired;

  return (
    <>
      <PageHeader title="Machines" meta={asked.at === "known" ? `${asked.value.length}` : undefined} />

      <Filters
        filters={filters}
        params={params}
        setParams={setParams}
        also={{ name: "retired", label: "Include retired", on: retired }}
      />

      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="p-4 text-sm text-destructive">{asked.why}</p>}

      {asked.at === "known" && asked.value.length === 0 && (
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

      {asked.at === "known" && asked.value.length > 0 && (
        <ul className="divide-y">
          {asked.value.map((machine) => (
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
                <StatusBadge status={machine.status} />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
