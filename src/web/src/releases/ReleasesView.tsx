import { useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { api, describe, type ReleaseSummary } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/shared/PageHeader";
import { releasePath } from "@/shell/views";

type Loaded = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; items: ReleaseSummary[] };

/**
 * What shipped together: the open release first, then published ones newest
 * first — the order the instance answers in, which is the order this shows.
 * The open release has no name until publishing gives it one, and the instance
 * calls it `unreleased` until then.
 */
export function ReleasesView() {
  const { project } = useParams();
  const [known, setKnown] = useState<{ of: string | undefined; loaded: Loaded } | null>(null);
  const loaded: Loaded = known !== null && known.of === project ? known.loaded : { at: "asking" };

  useEffect(() => {
    let current = true;
    const setLoaded = (loaded: Loaded) => setKnown({ of: project, loaded });

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/projects/{key}/releases", {
          params: { path: { key: project! } },
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
  }, [project]);

  return (
    <>
      <PageHeader title="Releases" meta={loaded.at === "known" ? `${loaded.items.length}` : undefined} />
      {loaded.at === "asking" && (
        <div className="space-y-3 p-4">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      )}
      {loaded.at === "failed" && <p className="p-4 text-sm text-destructive">{loaded.why}</p>}
      {loaded.at === "known" && loaded.items.length === 0 && (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
          <p className="font-medium">No release yet.</p>
          <p className="text-sm text-muted-foreground">Closed issues collect in the open release, which is named when it is published.</p>
        </div>
      )}
      {loaded.at === "known" && loaded.items.length > 0 && (
        <ul className="divide-y">
          {loaded.items.map((release) => (
            <li key={release.name}>
              <Link
                to={releasePath(project!, release.name)}
                className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-3 hover:bg-accent"
              >
                <span className="font-medium">{release.name}</span>
                <Badge variant={release.status === "open" ? "outline" : "secondary"} className="font-normal">
                  {release.status === "open" ? "open" : "published"}
                </Badge>
                <span className="text-xs text-muted-foreground">
                  {release.issues} {release.issues === 1 ? "issue" : "issues"}
                </span>
                <span className="w-full text-xs text-muted-foreground sm:ml-auto sm:w-auto">
                  {release.published_at === null
                    ? "Fills itself as issues are done"
                    : `${new Date(release.published_at).toLocaleDateString()}${release.published_by === null ? "" : ` · ${release.published_by.name}`}`}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
