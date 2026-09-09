import { Link } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { day } from "@/shared/when";
import { softwarePath } from "./addresses";

type SoftwareSummary = Schemas["SoftwareSummary"];

/**
 * The software — what an installation is an installation *of* (VISION 7). It is
 * the thing itself and not a copy of it: Caddy is one row however many machines
 * run it, which is what makes "where do I have to update Caddy?" a question
 * with one screen for an answer.
 *
 * No filters, because the endpoint takes none: the list is short by nature — a
 * team runs a few dozen pieces of software, not a few thousand.
 */
export function SoftwareListView() {
  const { asked } = useAsk<SoftwareSummary[]>("software", (signal) => api.GET("/api/software", { signal }));

  return (
    <>
      <PageHeader title="Software" meta={asked.at === "known" ? `${asked.value.length}` : undefined} />

      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="p-4 text-sm text-destructive">{asked.why}</p>}

      {asked.at === "known" && asked.value.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">No software yet.</p>
          <p className="max-w-md text-sm text-muted-foreground">
            A software is what runs on the machines — a web server, a database, one of the team's own
            services. It is written down once and installed as often as it is installed.
          </p>
        </div>
      )}

      {asked.at === "known" && asked.value.length > 0 && (
        <ul className="divide-y">
          {asked.value.map((software) => (
            <li key={software.key}>
              <Link to={softwarePath(software.key)} className="flex min-h-10 items-center gap-3 px-4 py-1 hover:bg-accent">
                <span className="w-48 shrink-0 truncate font-mono text-xs text-muted-foreground">{software.key}</span>
                <span className="min-w-0 flex-1 truncate">{software.name}</span>
                <span className="hidden w-64 shrink-0 truncate font-mono text-xs text-muted-foreground md:block">
                  {software.image ?? ""}
                </span>
                <span className="hidden w-24 shrink-0 truncate text-right text-xs text-muted-foreground sm:block">
                  {day(software.updated_at)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
