import { Link, useSearchParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { buttonVariants } from "@/components/ui/button";
import { Filters } from "@/shared/Filters";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { found } from "@/shared/narrowing";
import { day } from "@/shared/when";
import { providerPath } from "./addresses";

type ProviderSummary = Schemas["ProviderSummary"];

export function ProviderListView() {
  const [params, setParams] = useSearchParams();
  const find = params.get("q") ?? "";
  const { asked } = useAsk<ProviderSummary[]>("providers", (signal) => api.GET("/api/providers", { signal }));
  const rows = asked.at === "known" ? asked.value.filter((p) => found(find, p.key, p.name)) : [];

  return (
    <>
      <PageHeader title="Providers" meta={asked.at === "known" ? `${rows.length}` : undefined}>
        <Link className={buttonVariants({ size: "sm" })} to="/providers/new">Add provider</Link>
      </PageHeader>
      <Filters filters={[]} params={params} setParams={setParams}
        find={{ label: "Find", placeholder: "Part of a key or a name", value: find }} />
      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p role="alert" className="p-4 text-sm text-destructive">{asked.why}</p>}
      {asked.at === "known" && rows.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">{find === "" ? "No providers yet." : "Nothing matches."}</p>
          {find === "" && <p className="max-w-md text-sm text-muted-foreground">Add a provider to record who hosts a machine.</p>}
        </div>
      )}
      {asked.at === "known" && rows.length > 0 && (
        <ul className="divide-y">
          {rows.map((provider) => (
            <li key={provider.key}>
              <Link to={providerPath(provider.key)} className="flex min-h-11 items-center gap-3 px-4 py-1 hover:bg-accent focus-visible:outline-2 focus-visible:outline-brand">
                <span className="w-32 shrink-0 truncate font-mono text-xs text-muted-foreground sm:w-48">{provider.key}</span>
                <span className="min-w-0 flex-1 truncate">{provider.name}</span>
                <span className="hidden w-24 shrink-0 text-right text-xs text-muted-foreground sm:block">{day(provider.updated_at)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
