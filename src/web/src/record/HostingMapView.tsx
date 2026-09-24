import { lazy, Suspense, Component, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Link } from "react-router";
import { Dialog as DialogPrimitive } from "@base-ui/react/dialog";
import { Maximize2Icon, Minimize2Icon, PanelRightCloseIcon, PanelRightOpenIcon } from "lucide-react";
import { api, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Failed, Waiting } from "@/shared/Detail";
import { installationsOnMachinePath, machinePath, providerPath } from "./addresses";
import { StatusBadge } from "./Parts";
import type { DiagramFocus } from "./Diagram";
import { findOnMap, mapDiagram, type MapMatch } from "./hostingMapData";
import { MachineAvatar } from "./avatars/MachineAvatar";

type HostingMap = Schemas["HostingMap"];
type Machine = Schemas["HostingMapMachine"];

const HostingDiagram = lazy(() => import("./HostingDiagram").then((module) => ({ default: module.HostingDiagram })));

/** The graph is supplementary; a rendering failure leaves the grouped list usable. */
class DiagramBoundary extends Component<{ children: ReactNode; onFail: () => void }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch() { this.props.onFail(); }
  render() {
    return this.state.failed
      ? <p role="status" className="rounded-lg border p-4 text-sm text-muted-foreground">The diagram is unavailable. The grouped list has every provider and machine.</p>
      : this.props.children;
  }
}

function MachineList({ machine }: { machine: Machine }) {
  const addresses = [machine.ipv4, machine.ipv6, machine.private_ip].filter((address) => address !== null);
  return <li className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t py-2 text-sm">
    <MachineAvatar machine={machine} size={28} />
    <Link className="min-w-0 font-mono text-xs text-brand hover:underline" to={machinePath(machine.key)}>{machine.key}</Link>
    <span className="min-w-0 flex-1 truncate">{machine.name}</span>
    <StatusBadge status={machine.status} />
    <span className="w-full break-all font-mono text-xs text-muted-foreground">{addresses.join(" · ") || "No addresses recorded"}</span>
    <Link className="text-xs text-brand hover:underline" to={installationsOnMachinePath(machine.key)}>Installations</Link>
  </li>;
}

/** Brings a provider or machine into view in the diagram, without narrowing the map or the list. */
function FindOnMap({ map, onFind }: { map: HostingMap; onFind: (match: MapMatch) => void }) {
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const results = useRef<HTMLUListElement>(null);
  const id = useId();
  const matches = findOnMap(map, query);
  const shown = open && query.trim() !== "";

  const choose = (match: MapMatch) => {
    onFind(match);
    setOpen(false);
  };
  const buttons = () => Array.from(results.current?.querySelectorAll("button") ?? []);
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === "Escape" && shown) {
      event.stopPropagation();
      setOpen(false);
      (event.currentTarget.querySelector("input") as HTMLInputElement | null)?.focus();
      return;
    }
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
    const all = buttons();
    if (all.length === 0) return;
    event.preventDefault();
    const at = all.indexOf(document.activeElement as HTMLButtonElement);
    const next = event.key === "ArrowDown" ? Math.min(at + 1, all.length - 1) : at - 1;
    if (next < 0) (event.currentTarget.querySelector("input") as HTMLInputElement | null)?.focus();
    else all[next].focus();
  };

  return <div role="search" className="relative w-full sm:w-72" onKeyDown={onKeyDown}>
    <form onSubmit={(event) => { event.preventDefault(); if (matches.length > 0) choose(matches[0]); }}>
      <label htmlFor={`${id}-query`} className="sr-only">Find a provider or machine</label>
      <Input id={`${id}-query`} type="search" autoComplete="off" placeholder="Find a provider or machine…" value={query}
        aria-controls={`${id}-results`} aria-expanded={shown}
        onChange={(event) => { setQuery(event.target.value); setOpen(true); }} onFocus={() => setOpen(true)} />
    </form>
    <div id={`${id}-results`} hidden={!shown}
      className="absolute inset-x-0 top-full z-20 mt-1 max-h-72 overflow-auto rounded-lg border bg-popover p-1 text-sm shadow-md">
      {shown && (matches.length === 0
        ? <p role="status" className="px-2 py-1.5 text-muted-foreground">No provider or machine matches “{query.trim()}”.</p>
        : <ul ref={results} aria-label="Matches">
          {matches.map((match) => <li key={match.id}>
            <button type="button" className="flex w-full items-baseline gap-2 rounded-md px-2 py-1.5 text-left hover:bg-muted focus-visible:bg-muted focus-visible:outline-2 focus-visible:outline-brand"
              onClick={() => choose(match)}>
              <span className="w-16 shrink-0 text-[0.65rem] font-semibold tracking-wide text-muted-foreground uppercase">{match.kind}</span>
              <span className="min-w-0 flex-1 truncate">{match.name}</span>
              <span className="shrink-0 font-mono text-xs text-muted-foreground">{match.key}</span>
            </button>
          </li>)}
        </ul>)}
    </div>
  </div>;
}

export function HostingMapView() {
  const { asked } = useAsk<HostingMap>("hosting-map", (signal) => api.GET("/api/hosting-map", { signal }));
  if (asked.at === "asking") return <Waiting title="Loading the hosting map…" />;
  if (asked.at === "failed") return <Failed title="Hosting map" why={asked.why}
    back={<Link className="text-brand hover:underline" to="/machines">All machines</Link>} />;
  return <HostingMapContent map={asked.value} />;
}

function ListToggle({ shown, controls, onToggle }: { shown: boolean; controls: string; onToggle: () => void }) {
  return <Button variant="outline" size="sm" aria-expanded={shown} aria-controls={controls} onClick={onToggle}>
    {shown ? <PanelRightCloseIcon aria-hidden /> : <PanelRightOpenIcon aria-hidden />}
    {shown ? "Hide list" : "Show list"}
  </Button>;
}

function GroupedList({ map, id, shown, className }: { map: HostingMap; id: string; shown: boolean; className?: string }) {
  const under = new Map(map.providers.map((provider) => [provider.key, map.machines.filter((machine) => machine.provider === provider.key)]));
  const unassigned = map.machines.filter((machine) => machine.provider === null);
  return <section id={id} aria-label="Providers and machines" hidden={!shown} className={cn("min-w-0 space-y-4", className)}>
    <h2 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Providers and machines</h2>
    {map.providers.map((provider) => <div key={provider.key} className="rounded-lg border p-3">
      <h3 className="font-semibold"><Link className="text-brand hover:underline" to={providerPath(provider.key)}>{provider.name}</Link></h3>
      <p className="font-mono text-xs text-muted-foreground">{provider.key}</p>
      {(under.get(provider.key) ?? []).length === 0
        ? <p className="mt-2 text-sm text-muted-foreground">No machines assigned.</p>
        : <ul className="mt-2">{under.get(provider.key)!.map((machine) => <MachineList key={machine.key} machine={machine} />)}</ul>}
    </div>)}
    {unassigned.length > 0 && <div className="rounded-lg border p-3">
      <h3 className="font-semibold">Unassigned machines</h3>
      <ul className="mt-2">{unassigned.map((machine) => <MachineList key={machine.key} machine={machine} />)}</ul>
    </div>}
  </section>;
}

function HostingMapContent({ map }: { map: HostingMap }) {
  const [listHidden, setListHidden] = useState(false);
  // The focus view is there for the diagram, so it starts without the list and keeps the ordinary layout's choice apart.
  const [focused, setFocused] = useState(false);
  const [focusListHidden, setFocusListHidden] = useState(true);
  const [diagramFailed, setDiagramFailed] = useState(false);
  const [focus, setFocus] = useState<DiagramFocus | null>(null);
  const { items, edges } = mapDiagram(map);
  const meta = `${map.providers.length} providers · ${map.machines.length} machines`;

  if (items.length === 0) return <>
    <PageHeader title="Hosting map" meta={meta} />
    <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <p className="font-medium">No providers or machines yet.</p>
      <Link className="text-sm text-brand hover:underline" to="/providers">Browse providers</Link>
    </div>
  </>;

  // Without the diagram the list is the only way to the records, so it cannot stay hidden.
  const listShown = !listHidden || diagramFailed;
  const focusListShown = !focusListHidden || diagramFailed;
  const find = !diagramFailed && <FindOnMap map={map} onFind={(match) => setFocus((current) => ({ id: match.id, at: (current?.at ?? 0) + 1 }))} />;
  const diagram = (shown: boolean, className?: string) => <DiagramBoundary onFail={() => setDiagramFailed(true)}>
    <Suspense fallback={<p aria-busy className="rounded-lg border p-4 text-sm text-muted-foreground">Loading the diagram…</p>}>
      <HostingDiagram items={items} edges={edges} focus={focus} frame={shown ? "with list" : "without list"} className={className} />
    </Suspense>
  </DiagramBoundary>;

  return <DialogPrimitive.Root open={focused} onOpenChange={setFocused}>
    <PageHeader title="Hosting map" meta={meta}>
      {!diagramFailed && <>
        <ListToggle shown={listShown} controls="hosting-map-list" onToggle={() => setListHidden((hidden) => !hidden)} />
        <DialogPrimitive.Trigger render={<Button variant="outline" size="sm" />}>
          <Maximize2Icon aria-hidden />Focus map
        </DialogPrimitive.Trigger>
      </>}
    </PageHeader>
    {!focused && <div data-list={listShown ? "shown" : "hidden"}
      className={cn("grid min-w-0 gap-5 p-4 md:p-6", listShown && "xl:grid-cols-[minmax(0,2fr)_minmax(19rem,1fr)]")}>
      <div className="min-w-0 space-y-3">
        {find}
        {diagram(listShown)}
      </div>
      <GroupedList map={map} id="hosting-map-list" shown={listShown} />
    </div>}
    <DialogPrimitive.Portal>
      <DialogPrimitive.Popup className="fixed inset-0 z-50 flex flex-col overflow-auto bg-background text-sm outline-none">
        <div className="sticky top-0 z-10 flex min-h-12 flex-wrap items-center gap-x-3 gap-y-2 border-b bg-background px-4 py-2">
          <DialogPrimitive.Title className="text-sm font-semibold">Hosting map</DialogPrimitive.Title>
          <span className="text-xs text-muted-foreground">{meta}</span>
          <div className="ml-auto flex items-center gap-2">
            {!diagramFailed && <ListToggle shown={focusListShown} controls="hosting-map-focus-list" onToggle={() => setFocusListHidden((hidden) => !hidden)} />}
            <DialogPrimitive.Close render={<Button variant="outline" size="sm" />}>
              <Minimize2Icon aria-hidden />Exit focus
            </DialogPrimitive.Close>
          </div>
        </div>
        <div data-list={focusListShown ? "shown" : "hidden"} className={cn("grid flex-1 gap-4 p-3 md:p-4 lg:min-h-0 lg:grid-rows-[minmax(0,1fr)]",
          focusListShown && "lg:grid-cols-[minmax(0,1fr)_minmax(19rem,24rem)]")}>
          <div className="flex min-h-[max(20rem,calc(100dvh-8rem))] min-w-0 flex-col gap-3 lg:min-h-0">
            {find}
            {diagram(focusListShown, "h-auto min-h-80 flex-1")}
          </div>
          <GroupedList map={map} id="hosting-map-focus-list" shown={focusListShown} className="lg:min-h-0 lg:overflow-auto" />
        </div>
      </DialogPrimitive.Popup>
    </DialogPrimitive.Portal>
  </DialogPrimitive.Root>;
}
