import { useCallback, useEffect, useId, useState, type FormEvent, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api, describe, type IssueSummary, type Release } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { MarkdownField } from "@/shared/MarkdownField";
import { StatusDot } from "@/issues/status";
import { ActionDialog } from "@/shared/ActionDialog";
import { Markdown } from "@/shared/Markdown";
import { PageHeader } from "@/shared/PageHeader";
import { keyPath, releasePath } from "@/shell/views";
import { releaseMarkdown } from "./notes";

type Loaded = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; release: Release };

/**
 * One release: its notes, the exact set of issues that shipped in it, and the
 * two things a human does here — annotate the notes and publish the open one.
 *
 * Publishing names and freezes what is open and opens the next release, so it
 * shows the name, the notes and the membership before it happens (the human
 * interface, "Action matrix"). Published notes stay editable; publication time,
 * publisher and membership do not change again.
 */
export function ReleaseView() {
  const { project, name } = useParams();
  const navigate = useNavigate();
  const [known, setKnown] = useState<{ of: string; loaded: Loaded } | null>(null);
  // Which publication is the newest, because only that one can be corrected
  // (VISION 7). The list answers it in one read; the release itself does not.
  const [newest, setNewest] = useState<{ of: string; name: string | null } | null>(null);
  const address = `${project}/${name}`;
  const loaded: Loaded = known !== null && known.of === address ? known.loaded : { at: "asking" };

  const reload = useCallback(async () => {
    const { data } = await api.GET("/projects/{key}/releases", { params: { path: { key: project! } } });
    setNewest({ of: address, name: data?.find((r) => r.status === "published")?.name ?? null });
  }, [address, project]);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/projects/{key}/releases/{name}", {
          params: { path: { key: project!, name: name! } },
        });

        if (current) {
          setKnown({
            of: address,
            loaded: data === undefined ? { at: "failed", why: describe(error, response.status) } : { at: "known", release: data },
          });
        }
      } catch {
        if (current) {
          setKnown({ of: address, loaded: { at: "failed", why: "The instance did not answer." } });
        }
      }
      if (current) await reload();
    })();

    return () => {
      current = false;
    };
  }, [address, name, project, reload]);

  if (loaded.at === "asking") {
    return (
      <>
        <PageHeader title={<Skeleton className="h-4 w-40" />} />
        <div className="space-y-3 p-4">
          <Skeleton className="h-3 w-full" />
          <Skeleton className="h-3 w-5/6" />
        </div>
      </>
    );
  }

  if (loaded.at === "failed") {
    return (
      <>
        <PageHeader title={name!} />
        <p className="p-4 text-sm text-destructive">{loaded.why}</p>
        <p className="px-4 text-sm">
          <Link className="text-brand hover:underline" to={`/${project}/releases`}>
            All releases
          </Link>
        </p>
      </>
    );
  }

  const release = loaded.release;
  const changed = (value: Release) => setKnown({ of: address, loaded: { at: "known", release: value } });
  const correctable = release.status === "published" && newest?.of === address && newest.name === release.name;

  return (
    <>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            {release.name}
            <Badge variant={release.status === "open" ? "outline" : "secondary"} className="font-normal">
              {release.status === "open" ? "open" : "published"}
            </Badge>
          </span>
        }
        meta={
          release.published_at === null
            ? "Fills itself as issues are done"
            : `Published ${new Date(release.published_at).toLocaleString()}${release.published_by === null ? "" : ` by ${release.published_by.name}`}`
        }
      >
        <CopyAsMarkdown release={release} />
        {/* Correcting the newest publication is not rewriting the record: a
            typo in the name it was just given, and a publication nobody meant
            to make (VISION 7). */}
        {correctable && <RenameDialog release={release} onRenamed={(value) => navigate(releasePath(project!, value.name), { replace: true })} />}
        {correctable && (
          <ActionDialog
            trigger={<Button variant="outline" size="sm">Take publication back</Button>}
            title={`Take ${release.name} back?`}
            description="It becomes the open release again with the same issues in it, and the empty open release goes. Refused once another release has followed it."
            confirmLabel="Take it back"
            confirmVariant="default"
            onConfirm={async () => {
              const result = await api.POST("/projects/{key}/releases/{name}/retract", { params: { path: { key: project!, name: release.name } } });
              if (!result.data) throw new Error(describe(result.error, result.response.status));
              void navigate(releasePath(project!, "unreleased"), { replace: true });
            }}
          />
        )}
        {release.status === "open" && (
          <PublishDialog
            release={release}
            onPublished={(published) => navigate(releasePath(project!, published.name), { replace: true })}
          />
        )}
      </PageHeader>

      <div className="max-w-3xl flex-1 p-4 md:p-6">
        <Notes release={release} onChanged={changed} />
        <Section title={`Issues · ${release.issues.length}`}>
          {release.issues.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {release.status === "open"
                ? "Nothing has been closed since the last release."
                : "This release was published with nothing in it."}
            </p>
          ) : (
            <Membership
              issues={release.issues}
              onRemove={release.status !== "open" ? undefined : async (key) => {
                const result = await api.DELETE("/projects/{key}/releases/{name}/issues/{issue}", { params: { path: { key: project!, name: release.name, issue: key } } });
                if (!result.data) throw new Error(describe(result.error, result.response.status));
                changed(result.data);
              }}
            />
          )}
        </Section>
      </div>
    </>
  );
}

function Notes({ release, onChanged }: { release: Release; onChanged: (release: Release) => void }) {
  const { project } = useParams();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(release.description);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);

    try {
      const { data, error: refusal, response } = await api.PATCH("/projects/{key}/releases/{name}", {
        params: { path: { key: project!, name: release.name } },
        body: { name: null, description: draft },
      });

      if (data === undefined) {
        setError(describe(refusal, response.status));
        return;
      }

      onChanged(data);
      setEditing(false);
    } catch {
      setError("The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  if (editing) {
    return (
      <Section title="Notes">
        <form className="grid gap-3" onSubmit={(event) => void save(event)}>
          <MarkdownField label="Notes" value={draft} onChange={setDraft} />
          {error !== undefined && <p role="alert" className="text-sm text-destructive">{error}</p>}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setEditing(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? "Saving…" : "Save notes"}
            </Button>
          </div>
        </form>
      </Section>
    );
  }

  return (
    <Section
      title="Notes"
      action={
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            setDraft(release.description);
            setError(undefined);
            setEditing(true);
          }}
        >
          Edit notes
        </Button>
      }
    >
      {release.description === "" ? (
        <p className="text-sm text-muted-foreground">No notes yet.</p>
      ) : (
        <Markdown>{release.description}</Markdown>
      )}
    </Section>
  );
}

/**
 * The exact membership. The instance answers parent first, then that parent's
 * sub-issues, so a sub-issue is indented where it stands rather than moved.
 */
function Membership({ issues, onRemove }: { issues: IssueSummary[]; onRemove?: (key: string) => Promise<void> }) {
  const [why, setWhy] = useState<string>();

  return (
    <>
      <ul className="divide-y rounded-md border">
        {issues.map((issue) => (
          <li key={issue.key} className={`flex min-h-9 items-center gap-3 px-3 py-1 ${issue.parent === null ? "" : "pl-8"}`}>
            <StatusDot status={issue.status} />
            <Link className="font-mono text-xs text-brand hover:underline" to={keyPath(issue.key)}>
              {issue.key}
            </Link>
            <span className="min-w-0 flex-1 truncate text-sm">{issue.title}</span>
            {onRemove !== undefined && (
              <Button
                size="xs"
                variant="ghost"
                onClick={() => void onRemove(issue.key).then(() => setWhy(undefined), (reason: unknown) => setWhy(reason instanceof Error ? reason.message : "The instance did not answer."))}
              >
                Remove
              </Button>
            )}
          </li>
        ))}
      </ul>
      {why !== undefined && <p role="alert" className="mt-2 text-sm text-destructive">{why}</p>}
    </>
  );
}

/** The one thing a published release still gives up: the name it was just given. */
function RenameDialog({ release, onRenamed }: { release: Release; onRenamed: (release: Release) => void }) {
  const { project } = useParams();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(release.name);
  const nameId = useId();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  function changeOpen(next: boolean) {
    if (busy) return;
    if (next) setName(release.name);
    setError(undefined);
    setOpen(next);
  }

  async function rename(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const chosen = name.trim();
    if (chosen === "" || chosen === release.name) return;
    setBusy(true);
    setError(undefined);

    try {
      const { data, error: refusal, response } = await api.PATCH("/projects/{key}/releases/{name}", {
        params: { path: { key: project!, name: release.name } },
        body: { name: chosen, description: null },
      });
      if (data === undefined) {
        setError(describe(refusal, response.status));
        return;
      }
      setOpen(false);
      onRenamed(data);
    } catch {
      setError("The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      <DialogTrigger render={<Button variant="outline" size="sm" />}>Rename…</DialogTrigger>
      <DialogContent>
        <form className="grid gap-4" onSubmit={(event) => void rename(event)}>
          <DialogHeader>
            <DialogTitle>Rename {release.name}</DialogTitle>
            <DialogDescription>
              Only the newest publication can be renamed. What shipped in it does not change.
            </DialogDescription>
          </DialogHeader>
          <label className="grid gap-1 text-sm font-medium">
            Name
            <Input id={nameId} value={name} onChange={(event) => setName(event.target.value)} maxLength={100} required />
          </label>
          {error !== undefined && <p role="alert" className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <DialogClose render={<Button variant="outline" disabled={busy} />}>Cancel</DialogClose>
            <Button type="submit" disabled={busy || name.trim() === "" || name.trim() === release.name}>
              {busy ? "Renaming…" : "Rename release"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function CopyAsMarkdown({ release }: { release: Release }) {
  const [said, setSaid] = useState<{ text: string; failed: boolean }>();

  async function copy() {
    try {
      await navigator.clipboard.writeText(releaseMarkdown(release));
      setSaid({ text: `${release.name} copied as Markdown.`, failed: false });
    } catch {
      setSaid({ text: "The browser did not allow copying.", failed: true });
    }
  }

  return (
    <>
      <Button variant="outline" size="sm" onClick={() => void copy()}>
        Copy as Markdown
      </Button>
      {said !== undefined && (
        <span role="status" className={`text-xs ${said.failed ? "text-destructive" : "text-muted-foreground"}`}>
          {said.text}
        </span>
      )}
    </>
  );
}

function PublishDialog({ release, onPublished }: { release: Release; onPublished: (release: Release) => void }) {
  const { project } = useParams();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [notes, setNotes] = useState(release.description);
  const nameId = useId();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  function changeOpen(next: boolean) {
    if (busy) return;
    if (next) {
      setName("");
      setNotes(release.description);
    }
    setError(undefined);
    setOpen(next);
  }

  async function publish(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const chosen = name.trim();
    if (chosen === "") return;
    setBusy(true);
    setError(undefined);

    try {
      const { data, error: refusal, response } = await api.POST("/projects/{key}/releases/publish", {
        params: { path: { key: project! } },
        body: { name: chosen, description: notes },
      });

      if (data === undefined) {
        setError(describe(refusal, response.status));
        return;
      }

      setOpen(false);
      onPublished(data);
    } catch {
      setError("The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      <DialogTrigger render={<Button size="sm" />}>Publish…</DialogTrigger>
      <DialogContent className="sm:max-w-lg">
        <form className="grid gap-4" onSubmit={(event) => void publish(event)}>
          <DialogHeader>
            <DialogTitle>Publish the open release</DialogTitle>
            <DialogDescription>
              Naming it freezes these {release.issues.length} {release.issues.length === 1 ? "issue" : "issues"} as what
              shipped and opens the next release. The notes stay editable afterwards; the membership does not.
            </DialogDescription>
          </DialogHeader>
          <label className="grid gap-1 text-sm font-medium">
            Name
            <Input
              id={nameId}
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="1.4.0"
              maxLength={100}
              required
            />
          </label>
          <MarkdownField label="Notes" value={notes} onChange={setNotes} />
          <div className="grid gap-1">
            <span className="text-sm font-medium">What ships</span>
            <div className="max-h-48 overflow-y-auto">
              {release.issues.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nothing has been closed since the last release.</p>
              ) : (
                <Membership issues={release.issues} />
              )}
            </div>
          </div>
          {error !== undefined && <p role="alert" className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <DialogClose render={<Button variant="outline" disabled={busy} />}>Cancel</DialogClose>
            <Button type="submit" disabled={busy || name.trim() === ""}>
              {busy ? "Publishing…" : "Publish release"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function Section({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section className="border-t py-5 first:border-t-0 first:pt-0">
      <div className="mb-3 flex items-center gap-3">
        <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
        {action !== undefined && <span className="ml-auto">{action}</span>}
      </div>
      {children}
    </section>
  );
}
