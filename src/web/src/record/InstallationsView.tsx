import { Link, useSearchParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { installationPath, machinePath, softwarePath } from "./addresses";
import { Filters, type Filter } from "@/shared/Filters";
import { StatusBadge } from "./Parts";

type InstallationSummary = Schemas["InstallationSummary"];

/** The closed sets of VISION 7, which is what the endpoint reads and all it reads. */
const filters: Filter[] = [
  { name: "environment", label: "Environment", values: ["production", "staging", "development"] },
  { name: "role", label: "Role", values: ["application", "platform"] },
  { name: "status", label: "Status", values: ["planned", "active", "retired"] },
  { name: "backup", label: "Backup", values: ["none", "planned", "active"] },
];

/**
 * The installations: one software installed once on one machine (VISION 7).
 * This is the list that crosses the record — every row names both ends — and
 * the one that answers questions the two other lists cannot, like which
 * production installations have no backup.
 */
export function InstallationsView() {
  const [params, setParams] = useSearchParams();
  const query = {
    machine: params.get("machine") ?? undefined,
    software: params.get("software") ?? undefined,
    environment: params.get("environment") ?? undefined,
    role: params.get("role") ?? undefined,
    status: params.get("status") ?? undefined,
    backup: params.get("backup") ?? undefined,
    retired: params.get("retired") === "yes" ? true : undefined,
  };
  const at = JSON.stringify(query);

  const { asked } = useAsk<InstallationSummary[]>(at, (signal) =>
    api.GET("/api/installations", { params: { query }, signal }));

  const narrowed = Object.values(query).some((value) => value !== undefined);

  return (
    <>
      <PageHeader title="Installations" meta={asked.at === "known" ? `${asked.value.length}` : undefined} />

      <Filters
        filters={filters}
        params={params}
        setParams={setParams}
        also={{ name: "retired", label: "Include retired", on: query.retired === true }}
      />

      {/* A list narrowed to one machine or one software arrived from that
          screen. Saying so, with the way back, is what keeps the address bar
          from being the only place that knows. */}
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

      {asked.at === "known" && asked.value.length === 0 && (
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

      {asked.at === "known" && asked.value.length > 0 && (
        <ul className="divide-y">
          {asked.value.map((installation) => (
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
