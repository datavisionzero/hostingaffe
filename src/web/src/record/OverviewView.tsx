import { Link, useSearchParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Filters, type Filter } from "@/shared/Filters";
import { word } from "@/shared/narrowing";
import { ago } from "@/shared/when";
import { historyPath, installationPath, machinePath } from "./addresses";
import { StatusBadge } from "./Parts";
import { MachineAvatar } from "./avatars/MachineAvatar";

type MachineSummary = Schemas["MachineSummary"];

/** What the tiles can be narrowed by, which is what the endpoint accepts. */
const filters: Filter[] = [
  { name: "window", label: "Since", values: ["24h", "7d", "30d"] },
  { name: "status", label: "Status", values: ["planned", "active", "retired"] },
  { name: "kind", label: "Kind", values: ["vps", "dedicated", "vm", "local"] },
];

/** How far back the counts reach when nobody said otherwise. */
const week = "7d";

/**
 * The machines as tiles, each saying what has lately happened on it, and a
 * click away from the whole story (ADR 0018).
 *
 * It is the instance's front page because it is the question somebody opens
 * the record with: which of these hosts moved, which one has said nothing, and
 * which one is waiting for a restart. Every number on it comes out of the
 * record and the reports already there — there is no time series, no graph and
 * no threshold, and no colour claims one (VISION 5).
 */
export function OverviewView() {
  const [params, setParams] = useSearchParams();
  const window = word(params, "window") ?? week;
  const status = word(params, "status");
  const kind = word(params, "kind");
  const retired = status === "retired";
  const at = `${window}|${status ?? ""}|${kind ?? ""}`;

  const { asked } = useAsk<MachineSummary[]>(at, (signal) =>
    api.GET("/api/machines", { params: { query: { status, kind, activity: window } }, signal }));

  const rows = asked.at === "known" ? asked.value : [];

  return (
    <>
      <PageHeader title="Overview" meta={asked.at === "known" ? `${rows.length}` : undefined} />

      <Filters filters={filters} params={params} setParams={setParams} defaults={{ window: week }} />

      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="p-4 text-sm text-destructive">{asked.why}</p>}

      {asked.at === "known" && rows.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">{retired || kind !== undefined ? "Nothing matches." : "No machines yet."}</p>
          <p className="max-w-md text-sm text-muted-foreground">
            A machine is a computer the team rents or owns. The first ones usually arrive from a repository
            rather than by hand: <code className="font-mono text-xs">ha machine add</code>.
          </p>
        </div>
      )}

      {rows.length > 0 && (
        <ul className="grid min-h-0 flex-1 content-start gap-3 overflow-auto p-4 sm:grid-cols-2 xl:grid-cols-3">
          {rows.map((machine) => <Tile key={machine.key} machine={machine} window={window} />)}
        </ul>
      )}
    </>
  );
}

/**
 * One machine, and what has lately happened on it.
 *
 * The tile as a whole leads to the history of that machine — what happened is
 * the question it raises — and the key leads to the machine itself. They are
 * two links and not one nested in the other: the one that covers the tile lies
 * under the header, and the header is what is clicked where a reader aims at
 * the name.
 */
function Tile({ machine, window }: { machine: MachineSummary; window: string }) {
  const activity = machine.activity;

  return (
    <li className="relative rounded-md border transition-colors hover:bg-accent/40">
      <Link
        to={historyPath({ machine: machine.key })}
        className="absolute inset-0 rounded-md"
        aria-label={`What has been going on on ${machine.key}`}
      />

      <div className="relative flex flex-col gap-2 p-4">
        <div className="flex items-center gap-2">
          <MachineAvatar machine={machine} size={28} />
          <Link
            to={machinePath(machine.key)}
            className="relative font-mono text-sm font-medium hover:underline"
          >
            {machine.key}
          </Link>
          <span className="ml-auto text-xs text-muted-foreground">{machine.kind}</span>
          <StatusBadge status={machine.status} />
        </div>

        <p className="truncate text-xs text-muted-foreground">
          {[machine.provider, machine.location, machine.arch, installations(activity)]
            .filter((word) => word !== null && word !== undefined && word !== "")
            .join(" · ")}
        </p>

        {/* The line the whole screen is for: whether anything was deployed
            here, and when. A machine with no deployment says so rather than
            leaving the row out, because "nothing was ever deployed here" is an
            answer. */}
        <p className="min-w-0 truncate text-sm">
          {activity?.latest == null
            ? <span className="text-muted-foreground">No deployment recorded.</span>
            : (
              <>
                <Link
                  to={installationPath(activity.latest.installation)}
                  className="relative font-mono text-xs hover:underline"
                >
                  {activity.latest.installation}
                </Link>{" "}
                {activity.latest.previous !== null && (
                  <span className="text-muted-foreground line-through">{activity.latest.previous}</span>
                )}{" "}
                → {activity.latest.version}{" "}
                <span className="text-xs text-muted-foreground">{ago(activity.latest.at)}</span>
              </>
            )}
        </p>

        <p className="flex flex-wrap gap-x-3 text-xs text-muted-foreground">
          <span>{changes(activity, window)}</span>
          <span>
            {machine.last_seen === null ? "Never reported" : `Reported ${ago(machine.last_seen)}`}
          </span>
          {activity !== null && activity !== undefined && activity.drift > 0 && (
            <span>{activity.drift === 1 ? "1 drift" : `${String(activity.drift)} drift`}</span>
          )}
          {machine.reboot_required === true && <span>Restart pending</span>}
        </p>
      </div>
    </li>
  );
}

/** How many installations are running there, or nothing where none are. */
function installations(activity: MachineSummary["activity"]): string {
  if (activity == null || activity.installations === 0) return "";

  return activity.installations === 1 ? "1 installation" : `${String(activity.installations)} installations`;
}

/** What happened in the window, counting a deployment as the change it is. */
function changes(activity: MachineSummary["activity"], window: string): string {
  if (activity == null) return "";

  const many = activity.changes + activity.deployments;

  return many === 0
    ? `Nothing in ${window}`
    : `${String(many)} ${many === 1 ? "change" : "changes"} in ${window}`;
}
