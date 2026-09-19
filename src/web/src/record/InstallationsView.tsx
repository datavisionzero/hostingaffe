import { Link, useSearchParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { installationPath, machinePath, softwarePath } from "./addresses";
import { Filters, type Filter } from "@/shared/Filters";
import { found, usePreset, word } from "@/shared/narrowing";
import { StatusBadge } from "./Parts";

type InstallationSummary = Schemas["InstallationSummary"];
type MachineSummary = Schemas["MachineSummary"];

/** The closed sets of VISION 7, which is what the endpoint reads and all it reads. */
const filters: Filter[] = [
  { name: "environment", label: "Environment", values: ["production", "staging", "development"] },
  { name: "role", label: "Role", values: ["application", "platform"] },
  { name: "status", label: "Status", values: ["planned", "active", "retired"] },
  { name: "backup", label: "Backup", values: ["none", "planned", "active"] },
];

/** Every way this list can be narrowed, which is what an address is read as bare for. */
const narrowers = ["q", "machine", "software", "environment", "role", "status", "backup", "retired"];

/**
 * What the list narrows to when nobody said otherwise: what is running now, in
 * production.
 *
 * It is the question people come to this screen with — the planned and the
 * retired are read on purpose, the running ones are read to work. The chips say
 * so and one click takes either off, because a narrowing nobody can see is a
 * record that looks like it lost something.
 */
const defaults = { environment: "production", status: "active" };

/**
 * The installations: one software installed once on one machine (VISION 7).
 * This is the list that crosses the record — every row names both ends — and
 * the one that answers questions the two other lists cannot, like which
 * production installations have no backup.
 */
export function InstallationsView() {
  const [address, setParams] = useSearchParams();
  const params = usePreset(address, setParams, narrowers, defaults);
  const query = {
    machine: word(params, "machine"),
    software: word(params, "software"),
    environment: word(params, "environment"),
    role: word(params, "role"),
    status: word(params, "status"),
    backup: word(params, "backup"),
    retired: params.get("retired") === "yes" ? true : undefined,
  };
  const at = JSON.stringify(query);
  // Not part of the question the endpoint is asked: the word narrows the answer
  // it gave, here, as one screenful of installations can be narrowed.
  const find = params.get("q") ?? "";

  const { asked } = useAsk<InstallationSummary[]>(at, (signal) =>
    api.GET("/api/installations", { params: { query }, signal }));

  // The machines to choose from, retired ones among them: an installation on a
  // machine that was taken out of service is exactly what somebody looks for
  // while it is being moved off.
  const machines = useAsk<MachineSummary[]>("machines", (signal) =>
    api.GET("/api/machines", { params: { query: { retired: true } }, signal }));

  const rows = asked.at === "known"
    ? asked.value.filter((installation) => found(find, installation.key, installation.name))
    : [];

  const narrowed = find !== "" || Object.values(query).some((value) => value !== undefined);

  return (
    <>
      <PageHeader title="Installations" meta={asked.at === "known" ? `${rows.length}` : undefined} />

      <Filters
        filters={filters}
        params={params}
        setParams={setParams}
        defaults={defaults}
        find={{ label: "Find", placeholder: "Part of a key or a name", value: find }}
        pick={{
          name: "machine",
          label: "Machine",
          placeholder: "Any machine",
          empty: machines.asked.at === "known" ? "No machine of that name." : "Asking the instance…",
          choices: machines.asked.at === "known"
            ? machines.asked.value.map((machine) => ({ id: machine.key, name: machine.key, hint: machine.name }))
            : [],
        }}
        also={{ name: "retired", label: "Include retired", on: query.retired === true }}
      />

      {/* A list narrowed to one software arrived from that screen. Saying so,
          with the way back, is what keeps the address bar from being the only
          place that knows; the machine says it in the field it is chosen in. */}
      {(query.machine !== undefined || query.software !== undefined) && (
        <p className="border-b px-4 py-2 text-xs text-muted-foreground">
          {query.machine !== undefined && (
            <>On <Link className="text-brand hover:underline" to={machinePath(query.machine)}>{query.machine}</Link>. </>
          )}
          {query.software !== undefined && (
            <>Of <Link className="text-brand hover:underline" to={softwarePath(query.software)}>{query.software}</Link>. </>
          )}
        </p>
      )}

      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="p-4 text-sm text-destructive">{asked.why}</p>}

      {asked.at === "known" && rows.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">{narrowed ? "Nothing matches." : "No installations yet."}</p>
          {!narrowed && (
            <p className="max-w-md text-sm text-muted-foreground">
              An installation is one software on one machine: what it is called there, where it lives, which
              ports it holds, and which version is on it now.
            </p>
          )}
        </div>
      )}

      {asked.at === "known" && rows.length > 0 && (
        <ul className="divide-y">
          {rows.map((installation) => (
            <li key={installation.key}>
              <Link
                to={installationPath(installation.key)}
                className="flex min-h-10 items-center gap-3 px-4 py-1 hover:bg-accent"
              >
                <span className="w-48 shrink-0 truncate font-mono text-xs text-muted-foreground">
                  {installation.key}
                </span>
                <span className="min-w-0 flex-1 truncate">{installation.name}</span>
                <span className="hidden w-40 shrink-0 truncate text-xs text-muted-foreground md:block">
                  {installation.machine}
                </span>
                <span className="hidden w-28 shrink-0 truncate text-xs text-muted-foreground lg:block">
                  {installation.environment}
                </span>
                <span className="hidden w-32 shrink-0 truncate font-mono text-xs sm:block">
                  {installation.version ?? ""}
                </span>
                <StatusBadge status={installation.status} />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
