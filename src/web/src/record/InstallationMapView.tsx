import { Component, lazy, Suspense, useState, type ReactNode } from "react";
import { Link, useParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Failed, Waiting } from "@/shared/Detail";
import { moment } from "@/shared/when";
import { installationPath, machinePath } from "./addresses";
import { installationDiagram, domainsOf } from "./installationMapData";
import { StatusBadge } from "./Parts";

type InstallationMap = Schemas["InstallationMap"];
type Entry = Schemas["InstallationMapEntry"];

const InstallationDiagram = lazy(() => import("./InstallationDiagram").then((module) => ({ default: module.InstallationDiagram })));

class DiagramBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() {
    return this.state.failed
      ? <p role="status" className="rounded-lg border p-4 text-sm text-muted-foreground">The diagram is unavailable. The installation list remains complete.</p>
      : this.props.children;
  }
}

function InstallationRow({ entry }: { entry: Entry }) {
  const domains = domainsOf(entry.urls);
  return <li className="border-t py-3 text-sm">
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
      <Link className="font-semibold text-brand hover:underline" to={installationPath(entry.key)}>{entry.name}</Link>
      <span className="font-mono text-xs text-muted-foreground">{entry.key}</span>
      <span className="text-xs text-muted-foreground">{entry.role} · {entry.software}</span>
      <StatusBadge status={entry.status} />
    </div>
    {domains.length > 0 && <ul aria-label={`Domains for ${entry.name}`} className="mt-1 flex flex-wrap gap-x-3 font-mono text-xs">
      {domains.map((domain) => <li key={domain} className="break-all">{domain}</li>)}
    </ul>}
    <p className="mt-1 text-xs text-muted-foreground">
      {entry.latest_deployment_at === null ? "No deployment recorded" : `Latest deployment ${moment(entry.latest_deployment_at)}`}
    </p>
  </li>;
}

export function InstallationMapView() {
  const { key } = useParams();
  const at = key ?? "";
  const { asked } = useAsk<InstallationMap>(at, (signal) => api.GET("/api/machines/{key}/installation-map", {
    params: { path: { key: at } }, signal,
  }));

  if (asked.at === "asking") return <Waiting title="Loading the installation map…" />;
  if (asked.at === "failed") return <Failed title="Installation map" why={asked.why}
    back={<Link className="text-brand hover:underline" to={machinePath(at)}>Machine details</Link>} />;

  return <InstallationMapContent key={asked.value.machine} map={asked.value} />;
}

function InstallationMapContent({ map }: { map: InstallationMap }) {
  const [showPlatform, setShowPlatform] = useState(false);
  const platformCount = map.installations.filter((entry) => entry.role === "platform").length;
  const { items, edges } = installationDiagram(map, showPlatform);

  return <>
    <PageHeader title={`${map.name} · installations`} meta={`${map.installations.length} installations`}>
      <Link className="text-sm text-brand hover:underline" to={machinePath(map.machine)}>Machine details</Link>
    </PageHeader>
    {map.installations.length === 0 ? <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <p className="font-medium">No installations recorded on this machine.</p>
      <Link className="text-sm text-brand hover:underline" to={machinePath(map.machine)}>Machine details</Link>
    </div> : <div className="grid min-w-0 gap-5 p-4 xl:grid-cols-[minmax(0,2fr)_minmax(19rem,1fr)] md:p-6">
      <section aria-label="Installation diagram" className="min-w-0 space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Installation map</h2>
          {platformCount > 0 && <button type="button" className="rounded-md border px-3 py-1.5 text-xs text-brand hover:bg-muted focus-visible:outline-2 focus-visible:outline-brand"
            aria-expanded={showPlatform} onClick={() => setShowPlatform((current) => !current)}>
            {showPlatform ? `Hide platform installations (${platformCount})` : `Show platform installations (${platformCount})`}
          </button>}
        </div>
        <DiagramBoundary>
          <Suspense fallback={<p aria-busy className="rounded-lg border p-4 text-sm text-muted-foreground">Loading the diagram…</p>}>
            <InstallationDiagram key={showPlatform ? "all" : "applications"} items={items} edges={edges} />
          </Suspense>
        </DiagramBoundary>
        {!showPlatform && platformCount > 0 && <p className="text-xs text-muted-foreground">Platform installations are grouped out of the diagram. Every installation is listed alongside it.</p>}
      </section>
      <section aria-label="All installations" className="min-w-0">
        <h2 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">All installations</h2>
        <p className="mt-1 mb-2 text-xs text-muted-foreground">Latest deployment first; entries without deployments follow.</p>
        <ul>{map.installations.map((entry) => <InstallationRow key={entry.key} entry={entry} />)}</ul>
      </section>
    </div>}
  </>;
}
