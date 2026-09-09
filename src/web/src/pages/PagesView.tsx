import { useId } from "react";
import { Link, useSearchParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Filters, type Filter } from "@/shared/Filters";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { pagePath } from "@/shell/views";

type PageSummary = Schemas["PageSummary"];
type Anchor = Schemas["Anchor"];
type MachineSummary = Schemas["MachineSummary"];
type InstallationSummary = Schemas["InstallationSummary"];

const filters: Filter[] = [{ name: "kind", label: "Kind", values: ["runbook", "decision", "note"] }];

/**
 * The wiki, under what its pages hang on.
 *
 * A page is attached to a machine, to an installation, or to the instance as a
 * whole, and that anchor is the only structure this wiki has — there are no
 * folders and a slug is one segment (ADR 0003). One flat list hides exactly
 * that: it reads as though every page held for every machine, which is untrue
 * of most of them. So the list is grouped by the anchor, and the heading of a
 * group links to the record the pages belong to.
 *
 * Grouping is not narrowing. The search still reaches every page and is what a
 * reader navigates by, since it is what the product put in a folder tree's
 * place (VISION 7); the groups only say where a match lives.
 */
export function PagesView() {
  const [params, setParams] = useSearchParams();
  // The filter lives in the URL: a pasted link says what it shows.
  const query = params.get("q") ?? "";
  const kind = params.get("kind") ?? undefined;
  const searchId = useId();
  const at = `${query}|${kind ?? ""}`;

  const { asked } = useAsk<PageSummary[]>(at, (signal) =>
    api.GET("/api/pages", { params: { query: { q: query === "" ? undefined : query, kind } }, signal }));

  // An anchor carries a key and no name, so the names are read once for the
  // whole screen — retired machines among them, since a page outlives what it
  // hangs on being taken out of service. They are decoration: the headings
  // stand on the key while these are still coming, and a lookup that failed
  // costs a word, not the list.
  const machines = useAsk<MachineSummary[]>("machines", (signal) =>
    api.GET("/api/machines", { params: { query: { retired: true } }, signal }));

  const installations = useAsk<InstallationSummary[]>("installations", (signal) =>
    api.GET("/api/installations", { params: { query: { retired: true } }, signal }));

  const names = new Map<string, string>();

  if (machines.asked.at === "known") {
    for (const machine of machines.asked.value) names.set(`machine:${machine.key}`, machine.name);
  }

  if (installations.asked.at === "known") {
    for (const installation of installations.asked.value) names.set(`installation:${installation.key}`, installation.name);
  }

  const narrowed = query !== "" || kind !== undefined;

  const set = (name: string, value: string | undefined) => {
    const kept = new URLSearchParams(params);
    if (value === undefined) kept.delete(name);
    else kept.set(name, value);
    setParams(kept, { replace: true });
  };

  return (
    <>
      <PageHeader title="Pages" meta={asked.at === "known" ? `${asked.value.length}` : undefined}>
        <Button size="sm" render={<Link to="/pages/new" />}>New page</Button>
      </PageHeader>
      <div className="grid gap-2 border-b px-4 py-2">
        {/* The search is what this wiki has instead of a tree, so it stands
            above the list rather than behind a filter sheet. */}
        <div className="grid gap-1 text-sm font-medium">
          <label htmlFor={searchId}>Search</label>
          <Input
            id={searchId}
            name="q"
            type="search"
            placeholder="Words in the title or the body"
            value={query}
            onChange={(event) => set("q", event.target.value === "" ? undefined : event.target.value)}
          />
        </div>
      </div>

      <Filters filters={filters} params={params} setParams={setParams} />

      {asked.at === "asking" && <p aria-busy className="p-4 text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="p-4 text-sm text-destructive">{asked.why}</p>}
      {/* An empty wiki and an empty filtered result are different states: one
          is a wiki nobody has written in yet, the other is a filter that
          matched nothing. */}
      {asked.at === "known" && asked.value.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">{narrowed ? "Nothing matches." : "No pages yet."}</p>
          {!narrowed && (
            <p className="max-w-md text-sm text-muted-foreground">
              A page is what the team knows and no record holds: the architecture, the conventions, what an
              operator has to know.
            </p>
          )}
        </div>
      )}
      {asked.at === "known" && asked.value.length > 0 && (
        <div className="divide-y">
          {grouped(asked.value, names).map((group) => (
            <section key={group.id}>
              <h2 className="flex items-center gap-2 bg-muted/40 px-4 py-1.5 text-xs">
                {group.to === null
                  ? <span className="font-medium">{group.label}</span>
                  : (
                    // Key and name in one flow with a space between them, so
                    // that the heading is read as the two words it shows.
                    <Link className="min-w-0 truncate font-medium hover:underline" to={group.to}>
                      <span className="font-mono">{group.label}</span>
                      {group.name !== null && <>{" "}{group.name}</>}
                    </Link>
                  )}
                <span className="text-muted-foreground">{group.of}</span>
                <span className="text-muted-foreground tabular-nums">{group.pages.length}</span>
              </h2>
              <ul className="divide-y">
                {group.pages.map((page) => (
                  <li key={page.slug}>
                    <Link to={pagePath(page.slug)} className="flex min-h-10 items-center gap-3 px-4 py-1 hover:bg-accent">
                      <span className="w-40 shrink-0 truncate font-mono text-xs text-muted-foreground">{page.slug}</span>
                      <span className="min-w-0 flex-1 truncate">{page.title}</span>
                      <span className="hidden w-20 shrink-0 truncate text-xs text-muted-foreground sm:block">{page.kind}</span>
                      <span className="hidden w-44 shrink-0 truncate text-right text-xs text-muted-foreground md:block">
                        {new Date(page.updated_at).toLocaleDateString()} · {page.updated_by.name}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </>
  );
}

/** The pages of one anchor, and where that anchor is read. */
type Group = { id: string; label: string; name: string | null; of: string; to: string | null; pages: PageSummary[] };

function anchorPath(anchor: Anchor): string {
  return anchor.kind === "machine"
    ? `/machines/${encodeURIComponent(anchor.key)}`
    : `/installations/${encodeURIComponent(anchor.key)}`;
}

/**
 * The pages under their anchors: the instance first, because what holds for
 * everything is not a footnote to the machine list, then the machines and then
 * the installations, each by key.
 *
 * A heading names the record the way its own screen does: the key it is
 * addressed by, and the name beside it where `names` has one. The key is what
 * survives a name that has not arrived or a record that is no longer listed.
 */
function grouped(pages: PageSummary[], names: Map<string, string>): Group[] {
  const groups = new Map<string, Group>();

  for (const page of pages) {
    const anchor = page.attached_to;
    const id = anchor === null ? "" : `${anchor.kind}:${anchor.key}`;
    const group = groups.get(id) ?? {
      id,
      label: anchor === null ? "The instance" : anchor.key,
      name: names.get(id) ?? null,
      of: anchor === null ? "pages of no single machine" : anchor.kind,
      to: anchor === null ? null : anchorPath(anchor),
      pages: [],
    };

    group.pages.push(page);
    groups.set(id, group);
  }

  const rank = (group: Group) => (group.id === "" ? 0 : group.id.startsWith("machine:") ? 1 : 2);

  return [...groups.values()].sort((a, b) => rank(a) - rank(b) || a.label.localeCompare(b.label));
}
