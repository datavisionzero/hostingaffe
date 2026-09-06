import { useEffect, useId, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PageHeader } from "@/shared/PageHeader";
import { pagePath } from "@/shell/views";

type PageSummary = Schemas["PageSummary"];
type Loaded = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; items: PageSummary[] };

/**
 * The project's wiki, flat and by slug — no tree, no table of contents. What
 * replaces the navigation a hierarchy would give is the search, which the
 * command palette already reaches (VISION 7).
 */
export function PagesView() {
  const { project } = useParams();
  const [params, setParams] = useSearchParams();
  // The filter lives in the URL: a pasted link says what it shows.
  const query = params.get("q") ?? "";
  const [asked, setAsked] = useState<{ of: string; loaded: Loaded } | null>(null);
  const searchId = useId();
  const at = `${project}/${query}`;
  const loaded: Loaded = asked !== null && asked.of === at ? asked.loaded : { at: "asking" };

  const filtered = query !== "";

  const set = (name: string, values: string[]) => {
    const kept = new URLSearchParams(params);
    kept.delete(name);
    for (const value of values) kept.append(name, value);
    setParams(kept, { replace: true });
  };

  useEffect(() => {
    let current = true;
    const setLoaded = (loaded: Loaded) => setAsked({ of: at, loaded });

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/projects/{key}/pages", {
          params: {
            path: { key: project! },
            query: { q: query === "" ? undefined : query },
          },
        });

        if (current) {
          setLoaded(data === undefined ? { at: "failed", why: describe(error, response.status) } : { at: "known", items: data });
        }
      } catch {
        if (current) {
          setLoaded({ at: "failed", why: "The instance did not answer." });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [at, project, query]);

  return (
    <>
      <PageHeader title="Pages" meta={loaded.at === "known" ? `${loaded.items.length}` : undefined}>
        <Button size="sm" render={<Link to={`/${project}/pages/new`} />}>New page</Button>
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
            onChange={(event) => set("q", event.target.value === "" ? [] : [event.target.value])}
          />
        </div>
      </div>
      {loaded.at === "failed" && <p className="p-4 text-sm text-destructive">{loaded.why}</p>}
      {/* An empty project and an empty filtered result are different states:
          one is a wiki nobody has written in yet, the other is a filter that
          matched nothing. */}
      {loaded.at === "known" && loaded.items.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">{filtered ? "Nothing matches." : "No pages yet."}</p>
          {!filtered && (
            <p className="max-w-md text-sm text-muted-foreground">
              A page is what a project knows and no ticket asks for: the architecture, the conventions, what an
              operator has to know — and the plan tickets are cut from later.
            </p>
          )}
        </div>
      )}
      {loaded.at === "known" && loaded.items.length > 0 && (
        <ul className="divide-y">
          {loaded.items.map((page) => (
            <li key={page.slug}>
              <Link to={pagePath(project!, page.slug)} className="flex min-h-10 items-center gap-3 px-4 py-1 hover:bg-accent">
                <span className="w-40 shrink-0 truncate font-mono text-xs text-muted-foreground">{page.slug}</span>
                <span className="min-w-0 flex-1 truncate">{page.title}</span>
                <span className="hidden w-44 shrink-0 truncate text-right text-xs text-muted-foreground md:block">
                  {new Date(page.updated_at).toLocaleDateString()} · {page.updated_by.name}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
