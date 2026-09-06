import { useVirtualizer } from "@tanstack/react-virtual";
import { SearchIcon, SlidersHorizontalIcon } from "lucide-react";
import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { api, describe, type IssueSummary, type Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { LabelPicker, type PickableLabel } from "@/components/ui/label-picker";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { useEpics } from "@/epics/useEpics";
import { useIsMobile } from "@/hooks/use-mobile";
import { cn } from "@/lib/utils";
import { useLabels } from "@/projects/useLabels";
import { PageHeader } from "@/shared/PageHeader";
import { is, typing } from "@/shell/shortcuts";
import { keyPath, type View } from "@/shell/views";
import { AssigneeFilter, AuthorFilter, EpicFilter } from "./pickers";
import { PriorityMark } from "./priority";
import { priorityLabel } from "./priorityLabel";
import { StatusDot } from "./status";

type PageState =
  | { at: "asking"; items: IssueSummary[]; total?: number }
  | { at: "failed"; items: IssueSummary[]; total?: number; why: string }
  | { at: "known"; items: IssueSummary[]; total: number; nextCursor: string | null };

type ListQuery = {
  project?: string; status?: string[]; ready?: boolean; priority_min?: number; priority_max?: number;
  label?: string[]; epic?: string; assignee?: string; claimed?: string; author?: string;
  blocked?: boolean; has_open_question?: boolean; q?: string; deleted?: boolean; sort?: string; order?: string;
};

const pageSize = 50;

/** The shared, cursor-paginated issue list described by cut three. */
export function IssueListView({ view }: { view: View }) {
  const { project } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [search, setSearch] = useSearchParams();
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [active, setActive] = useState(0);
  const searchId = useId();
  const sortId = useId();
  const scrollElement = useRef<HTMLDivElement>(null);
  // The one control the filters belong to, whichever shape they take, and
  // where the focus goes back to when they close.
  const filtersButton = useRef<HTMLButtonElement>(null);
  const narrow = useIsMobile();
  const { labels } = useLabels(project);
  const epics = useEpics(project);
  const query = useMemo(() => readQuery(project, search, view), [project, search, view]);
  const fingerprint = JSON.stringify(query);
  const [loaded, setLoaded] = useState<{ of: string; page: PageState } | null>(null);
  const page: PageState = useMemo(
    () => loaded?.of === fingerprint ? loaded.page : { at: "asking", items: [] },
    [fingerprint, loaded],
  );

  const requestPage = useCallback(async (cursor?: string) => {
    setLoaded((current) => ({ of: fingerprint, page: { at: "asking", items: current?.of === fingerprint ? current.page.items : [], total: current?.of === fingerprint ? current.page.total : undefined } }));
    try {
      const { data, error, response } = await api.GET("/issues", { params: { query: { ...query, status: query.status as never, cursor, limit: pageSize } } });
      if (data === undefined) {
        setLoaded((current) => current?.of === fingerprint ? { of: fingerprint, page: { at: "failed", items: current.page.items, total: current.page.total, why: describe(error, response.status) } } : current);
        return;
      }
      setLoaded((current) => {
        if (current?.of !== fingerprint) return current;
        return { of: fingerprint, page: { at: "known", items: cursor === undefined ? data.items : [...current.page.items, ...data.items], total: data.total, nextCursor: data.next_cursor } };
      });
    } catch {
      setLoaded((current) => current?.of === fingerprint ? { of: fingerprint, page: { at: "failed", items: current.page.items, total: current.page.total, why: "The instance did not answer." } } : current);
    }
  }, [fingerprint, query]);

  useEffect(() => { void requestPage(); }, [requestPage]);
  // `sort=epic` makes the epic the first sort key, so a group is one unbroken
  // run of the list and stays one across page boundaries (`docs/api.md`). The
  // heads are rows of the same virtual window, of a height of their own.
  const rows = useMemo(() => {
    if (query.sort !== "epic") return page.items.map((_issue, index) => ({ head: null, index }));
    let open: string | null | undefined;
    return page.items.flatMap((issue, index) => {
      const opens = index === 0 || issue.epic !== open;
      open = issue.epic;
      return opens ? [{ head: issue.epic, index: -1 }, { head: null, index }] : [{ head: null, index }];
    });
  }, [page.items, query.sort]);

  // TanStack Virtual deliberately returns an imperative object; React Compiler
  // cannot memoize this hook, while the component itself remains safe.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollElement.current,
    estimateSize: (index) => rows[index]?.index === -1 ? 30 : 44,
    // Also gives server rendering and layout-less DOM tests a useful first
    // window; ResizeObserver replaces it with the real viewport immediately.
    initialRect: { width: 800, height: 600 },
    overscan: 8,
  });
  const virtualItems = virtualizer.getVirtualItems();
  const visibleItems = virtualItems.length > 0
    ? virtualItems
    : rows.slice(0, 14).map((row, index) => {
        const size = row.index === -1 ? 30 : 44;
        const start = rows.slice(0, index).reduce((total, before) => total + (before.index === -1 ? 30 : 44), 0);
        return { index, key: index, start, end: start + size, size, lane: 0 };
      });

  useEffect(() => {
    const last = virtualItems.at(-1)?.index;
    if (last !== undefined && last >= rows.length - 8 && page.at === "known" && page.nextCursor !== null) void requestPage(page.nextCursor);
  }, [page, requestPage, rows.length, virtualItems]);

  const storageKey = `hostingaffe.issue-list:${location.pathname}${location.search}`;
  useEffect(() => {
    const offset = Number(sessionStorage.getItem(storageKey));
    if (Number.isFinite(offset) && offset > 0) requestAnimationFrame(() => scrollElement.current?.scrollTo({ top: offset }));
  }, [storageKey]);

  useEffect(() => setActive((value) => Math.min(value, Math.max(0, page.items.length - 1))), [page.items.length]);
  useEffect(() => {
    // The keys of the list, as `shortcuts.ts` binds them and the ? overview
    // shows them. Escape is the exception that also answers while typing: it
    // closes the filters the search sits in.
    function onKeyDown(event: KeyboardEvent) {
      const editing = typing(event);
      if (is("list:search", event) && !editing) { event.preventDefault(); document.querySelector<HTMLInputElement>("[data-issue-search]")?.focus(); }
      else if (!editing && (is("list:next", event) || is("list:previous", event))) {
        event.preventDefault();
        const next = Math.max(0, Math.min(page.items.length - 1, active + (is("list:next", event) ? 1 : -1)));
        setActive(next); virtualizer.scrollToIndex(rows.findIndex((row) => row.index === next), { align: "auto" });
      } else if (!editing && is("list:open", event) && page.items[active]) void navigate(keyPath(page.items[active].key));
      // `c` is the frame's, not this list's: it creates in the project from
      // every screen of it.
      else if (is("list:close", event) && filtersOpen) { setFiltersOpen(false); filtersButton.current?.focus(); }
    }
    window.addEventListener("keydown", onKeyDown); return () => window.removeEventListener("keydown", onKeyDown);
  }, [active, filtersOpen, navigate, page.items, project, rows, virtualizer]);

  function change(name: string, value?: string) {
    const next = new URLSearchParams(search); next.delete(name);
    if (value) next.set(name, value);
    setSearch(next, { replace: true });
  }
  /** A filter the query carries several times over, `label` being the one. */
  function changeAll(name: string, values: string[]) {
    const next = new URLSearchParams(search); next.delete(name);
    for (const value of values) next.append(name, value);
    setSearch(next, { replace: true });
  }
  let explicit = false;
  search.forEach((_value, key) => { if (!["sort", "order"].includes(key)) explicit = true; });

  return <div className="flex min-h-0 flex-1 flex-col">
    <PageHeader title={view.label} meta={page.total === undefined ? "…" : `${page.total} ${page.total === 1 ? "issue" : "issues"}`}>
      <Button ref={filtersButton} variant={filtersOpen || explicit ? "secondary" : "outline"} size="sm" onClick={() => setFiltersOpen((open) => !open)} aria-expanded={filtersOpen}><SlidersHorizontalIcon /> Filters</Button>
      {/* The `c` of the frame, as a control: a key nobody has met yet is not
          an entry point, and the epic list has said it with a button all
          along. */}
      <Button size="sm" render={<Link to={`/${project}/issues/new`} />}>New issue</Button>
    </PageHeader>
    <div className="flex flex-wrap items-center gap-2 border-b p-2">
      <div className="relative min-w-48 flex-1 sm:max-w-sm"><SearchIcon className="pointer-events-none absolute left-2.5 top-2 size-4 text-muted-foreground" /><Input id={searchId} data-issue-search aria-label="Search issues" placeholder="Search issues…" value={search.get("q") ?? ""} onChange={(event) => change("q", event.target.value)} className="pl-8" /></div>
      <select id={sortId} aria-label="Sort issues" value={search.get("sort") ?? "updated"} onChange={(event) => change("sort", event.target.value === "updated" ? undefined : event.target.value)} className="h-8 rounded-lg border bg-background px-2 text-sm"><option value="updated">Recently updated</option><option value="created">Recently created</option><option value="priority">Priority</option><option value="epic">Epic</option></select>
      <Button variant="ghost" size="sm" onClick={() => change("order", (search.get("order") ?? "desc") === "desc" ? "asc" : undefined)} aria-label="Reverse sort order">{(search.get("order") ?? "desc") === "desc" ? "Descending" : "Ascending"}</Button>
    </div>
    {/* Wide: the bar stays in place above the list. Narrow: the same controls
        arrive as a sheet that dismisses itself and hands the focus back
        (`docs/human-interface.md`, the screen matrix). */}
    {filtersOpen && !narrow && <FilterBar project={project} search={search} change={change} changeAll={changeAll} labels={labels} epics={epics} clear={() => setSearch(new URLSearchParams(), { replace: true })} />}
    {narrow && <Sheet open={filtersOpen} onOpenChange={setFiltersOpen}>
      <SheetContent side="bottom" finalFocus={filtersButton} className="max-h-[85svh] overflow-y-auto pb-4">
        <SheetHeader className="pb-0">
          <SheetTitle>Filters</SheetTitle>
          <SheetDescription>What you choose is carried by the address of this list.</SheetDescription>
        </SheetHeader>
        <FilterBar project={project} search={search} change={change} changeAll={changeAll} labels={labels} epics={epics} clear={() => setSearch(new URLSearchParams(), { replace: true })} className="border-b-0 bg-transparent px-4 pt-0" />
      </SheetContent>
    </Sheet>}
    {page.at === "asking" && !page.items.length && <Loading />}
    {page.at === "failed" && !page.items.length && <p className="p-4 text-sm text-destructive">{page.why}</p>}
    {page.at === "known" && !page.items.length && <div className="flex flex-1 flex-col items-center justify-center gap-1 p-8 text-center"><p className="text-sm">{explicit ? "No issues match these filters." : "No issues yet."}</p><p className="text-xs text-muted-foreground">{view.hint}</p></div>}
    {!!page.items.length && <div ref={scrollElement} onScroll={(event) => sessionStorage.setItem(storageKey, String(event.currentTarget.scrollTop))} className="min-h-0 flex-1 overflow-auto" role="listbox" aria-label={`${view.label} issues`} aria-busy={page.at === "asking"}>
      <div className="relative w-full" style={{ height: virtualizer.getTotalSize() }}>{visibleItems.map((virtual) => {
        const row = rows[virtual.index];
        const style = { transform: `translateY(${virtual.start}px)`, height: virtual.size };
        return row.index === -1
          ? <GroupHead key={`epic:${row.head ?? "none"}`} epic={row.head} epics={epics} style={style} />
          : <IssueRow key={page.items[row.index].key} issue={page.items[row.index]} active={row.index === active} onActive={() => setActive(row.index)} style={style} />;
      })}</div>
      {page.at === "failed" && <p className="border-t p-3 text-center text-xs text-destructive">{page.why} <button className="underline" onClick={() => void requestPage()}>Try again</button></p>}
    </div>}
  </div>;
}

/** A picker in the filter bar wears the label of the selects beside it. */
const picker = "w-44 text-xs font-normal text-muted-foreground";

function FilterBar({ project, search, change, changeAll, labels, epics, clear, className }: { project: string | undefined; search: URLSearchParams; change: (name: string, value?: string) => void; changeAll: (name: string, values: string[]) => void; labels: PickableLabel[]; epics: Schemas["EpicSummary"][]; clear: () => void; className?: string }) {
  return <div className={cn("flex flex-wrap items-end gap-2 border-b bg-muted/30 p-2", className)} role="group" aria-label="Issue filters">
    <Filter label="Status" name="status" value={search.get("status") ?? ""} change={change}><option value="">Any</option>{["backlog", "todo", "in_progress", "review", "done", "canceled"].map((value) => <option key={value}>{value}</option>)}</Filter>
    {/* A native option holds text and nothing else, so the choice carries the
        word the row's accessible name carries rather than the mark it draws. */}
    <Filter label="Priority" name="priority" value={search.get("priority") ?? ""} change={change}><option value="">Any</option>{[0, 1, 2, 3, 4].map((value) => <option key={value} value={value}>{priorityLabel(value)}</option>)}</Filter>
    {/* Several labels at once: the query has carried repeated `label` values
        all along, and one text field could only ever say one of them. */}
    <LabelPicker label="Label" labels={labels} value={search.getAll("label")} onChange={(names) => changeAll("label", names)} className="w-56 text-xs font-normal text-muted-foreground" />
    {/* What exists already is chosen, never typed — the filter's flavour of
        the three pickers the issue form uses (`docs/api.md`, Filters for the
        shared issue list). */}
    <EpicFilter epics={epics} value={search.get("epic") ?? ""} onChange={(key) => change("epic", key)} className={picker} />
    <AssigneeFilter project={project} value={search.get("assignee") ?? ""} onChange={(name) => change("assignee", name)} className={picker} />
    <AuthorFilter project={project} value={search.get("author") ?? ""} onChange={(name) => change("author", name)} className={picker} />
    <Filter label="Claim" name="claimed" value={search.get("claimed") ?? ""} change={change}><option value="">Any</option><option value="true">Claimed</option><option value="false">Unclaimed</option><option value="me">Mine</option></Filter>
    {/* The one read that sees deleted rows (ADR 0013). It was reachable only
        by typing the query parameter. */}
    <Filter label="Deleted" name="deleted" value={search.get("deleted") ?? ""} change={change}><option value="">Not deleted</option><option value="true">Deleted only</option></Filter>
    <Filter label="Blocked" name="blocked" value={search.get("blocked") ?? ""} change={change}><option value="">Any</option><option value="true">Blocked</option><option value="false">Not blocked</option></Filter>
    <Filter label="Ready" name="ready" value={search.get("ready") ?? ""} change={change}><option value="">Any</option><option value="true">Ready</option><option value="false">Not ready</option></Filter>
    <Button variant="ghost" size="sm" onClick={clear}>Clear</Button>
  </div>;
}

function Filter({ label, name, value, change, children }: { label: string; name: string; value: string; change: (name: string, value?: string) => void; children: React.ReactNode }) {
  const id = useId();
  return <label className="grid gap-1 text-xs text-muted-foreground">{label}<select id={id} value={value} onChange={(event) => change(name, event.target.value)} className="h-8 rounded-lg border bg-background px-2 text-sm text-foreground">{children}</select></label>;
}

function IssueRow({ issue, active, onActive, style }: { issue: IssueSummary; active: boolean; onActive: () => void; style: React.CSSProperties }) {
  return <div role="option" aria-selected={active} className="absolute left-0 top-0 w-full border-b" style={style} onMouseMove={onActive}><Link to={keyPath(issue.key)} className={`grid h-full grid-cols-[auto_4.5rem_1fr_auto] items-center gap-x-2 px-3 hover:bg-accent focus-visible:bg-accent focus-visible:outline-hidden sm:grid-cols-[auto_5rem_minmax(8rem,1fr)_auto_auto_auto] ${active ? "bg-accent/70" : ""}`}><StatusDot status={issue.status} /><span className="font-mono text-xs text-muted-foreground">{issue.key}</span><span className="min-w-0 truncate"><span>{issue.title}</span>{issue.deleted_at != null && <span className="text-muted-foreground"> · deleted</span>}<span className="mt-0.5 block truncate text-xs text-muted-foreground sm:hidden">{issue.claim?.holder.name ?? issue.labels.join(" · ")}</span></span><span className="hidden items-center gap-1 md:flex">{issue.labels.slice(0, 3).map((name) => <Badge key={name} variant="secondary" className="font-normal">{name}</Badge>)}</span><span className="hidden max-w-32 truncate text-xs text-muted-foreground sm:inline">{issue.claim?.holder.name ?? issue.assignee?.name}</span><span className="flex w-6 justify-end"><PriorityMark priority={issue.priority} /></span></Link></div>;
}

/**
 * The head of an epic's group. It is `presentation` inside the listbox, which
 * takes only options: the run below it is the ordering the instance answered
 * with, and every row still says which issue it is.
 */
function GroupHead({ epic, epics, style }: { epic: string | null; epics: Schemas["EpicSummary"][]; style: React.CSSProperties }) {
  const title = epics.find((candidate) => candidate.key === epic)?.title;
  return <div role="presentation" className="absolute left-0 top-0 flex w-full items-center gap-2 border-b bg-muted/40 px-3 text-xs text-muted-foreground" style={style}>
    {epic === null
      ? <span className="font-medium">No epic</span>
      : <><span className="font-mono">{epic}</span><span className="min-w-0 truncate">{title}</span></>}
  </div>;
}

function Loading() { return <div className="divide-y" aria-busy>{Array.from({ length: 8 }, (_, i) => <div key={i} className="flex h-11 items-center gap-3 px-4"><Skeleton className="h-3 w-16" /><Skeleton className="h-3 flex-1" /></div>)}</div>; }

function readQuery(project: string | undefined, search: URLSearchParams, view: View): ListQuery {
  const bool = (name: string, fallback?: boolean) => search.has(name) ? search.get(name) === "true" : fallback;
  const number = (name: string) => search.has(name) ? Number(search.get(name)) : undefined;
  const priority = number("priority");
  return { project, status: search.getAll("status").length ? search.getAll("status") : view.filter?.status, ready: bool("ready", view.filter?.ready), priority_min: priority, priority_max: priority, label: search.getAll("label").length ? search.getAll("label") : undefined, epic: search.get("epic") ?? undefined, assignee: search.get("assignee") ?? undefined, claimed: search.get("claimed") ?? view.filter?.claimed, author: search.get("author") ?? undefined, blocked: bool("blocked"), has_open_question: bool("has_open_question", view.filter?.has_open_question), q: search.get("q") ?? undefined, deleted: bool("deleted"), sort: search.get("sort") ?? undefined, order: search.get("order") ?? undefined };
}
