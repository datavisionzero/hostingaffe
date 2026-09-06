import { useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { api, describe, type EpicSummary } from "@/api/client";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/shared/PageHeader";
import { keyPath } from "@/shell/views";

type Loaded = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; items: EpicSummary[] };

/**
 * The epics of the project with their progress, counted at read time by the
 * instance. Each row is the way into the epic's own view.
 */
export function EpicsView() {
  const { project } = useParams();
  const [known, setKnown] = useState<{ of: string | undefined; loaded: Loaded } | null>(null);
  const loaded: Loaded = known !== null && known.of === project ? known.loaded : { at: "asking" };

  useEffect(() => {
    let current = true;
    const setLoaded = (loaded: Loaded) => setKnown({ of: project, loaded });

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/epics", { params: { query: { project, limit: 100 } } });

        if (current) {
          setLoaded(
            data === undefined ? { at: "failed", why: describe(error, response.status) } : { at: "known", items: data.items },
          );
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
  }, [project]);

  return (
    <>
      <PageHeader title="Epics" meta={loaded.at === "known" ? `${loaded.items.length}` : undefined}>
        <Button size="sm" render={<Link to={`/${project}/epics/new`} />}>New epic</Button>
      </PageHeader>
      {loaded.at === "failed" && <p className="p-4 text-sm text-destructive">{loaded.why}</p>}
      {loaded.at === "known" && (
        <ul className="divide-y">
          {loaded.items.map((epic) => {
            const share = epic.progress.total === 0 ? 0 : epic.progress.closed / epic.progress.total;

            return (
              <li key={epic.key}>
                <Link to={keyPath(epic.key)} className="flex h-10 items-center gap-3 px-4 hover:bg-accent">
                  <span className="w-20 shrink-0 font-mono text-xs text-muted-foreground">{epic.key}</span>
                  <span className="min-w-0 flex-1 truncate">{epic.title}</span>
                  <span className="hidden w-32 items-center gap-2 sm:flex">
                    <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted">
                      <span className="block h-full bg-brand" style={{ width: `${Math.round(share * 100)}%` }} />
                    </span>
                    <span className="w-12 text-right font-mono text-xs text-muted-foreground">
                      {epic.progress.closed}/{epic.progress.total}
                    </span>
                  </span>
                  <span className="text-xs text-muted-foreground">{epic.status}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </>
  );
}
