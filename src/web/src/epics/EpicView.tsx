import { useEffect, useId, useState, type FormEvent, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api, describe, type IssueSummary, type Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { LabelPicker } from "@/components/ui/label-picker";
import { Skeleton } from "@/components/ui/skeleton";
import { MarkdownField } from "@/shared/MarkdownField";
import { StatusDot } from "@/issues/status";
import { useLabels } from "@/projects/useLabels";
import { useAbandon } from "@/shared/abandon";
import { ActionDialog } from "@/shared/ActionDialog";
import { Markdown } from "@/shared/Markdown";
import { PageHeader } from "@/shared/PageHeader";
import { stale } from "@/shared/stale";
import { keyPath, pathKey } from "@/shell/views";

type Epic = Schemas["Epic"];
type Load<T> = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; value: T };

const asking = { at: "asking" } as const;

/**
 * The epic's own view (VISION 6.2): the living document on top, its issues
 * below. A bracket, not a unit of work — closing it gates nothing, which is
 * what the warning before closing says out loud.
 */
export function EpicView() {
  const { project, number } = useParams();
  const key = pathKey(project!, number!);
  const [state, setState] = useState<{ key: string; epic: Load<Epic>; issues: Load<IssueSummary[]> }>();
  // Both are about the epic in the address, so both carry its key: walking to
  // another epic leaves neither the editor nor a deleted one's way back open.
  const [editingKey, setEditingKey] = useState<string>();
  const [removed, setRemoved] = useState<{ key: string; epic: Epic }>();
  const current = state !== undefined && state.key === key ? state : { key, epic: asking, issues: asking };
  const editing = editingKey === key;
  const deleted = removed !== undefined && removed.key === key ? removed.epic : undefined;

  useEffect(() => {
    let live = true;

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/epics/{key}", { params: { path: { key } } });
        if (live) {
          setState((old) => ({
            key,
            issues: old !== undefined && old.key === key ? old.issues : asking,
            epic: data === undefined ? { at: "failed", why: describe(error, response.status) } : { at: "known", value: data },
          }));
        }
      } catch {
        if (live) {
          setState((old) => ({ key, issues: old?.issues ?? asking, epic: { at: "failed", why: "The instance did not answer." } }));
        }
      }
    })();

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/issues", { params: { query: { project, epic: key, limit: 100 } } });
        if (live) {
          setState((old) => ({
            key,
            epic: old !== undefined && old.key === key ? old.epic : asking,
            issues: data === undefined ? { at: "failed", why: describe(error, response.status) } : { at: "known", value: data.items },
          }));
        }
      } catch {
        if (live) {
          setState((old) => ({ key, epic: old?.epic ?? asking, issues: { at: "failed", why: "The instance did not answer." } }));
        }
      }
    })();

    return () => {
      live = false;
    };
  }, [key, project]);

  if (current.epic.at === "asking") {
    return (
      <>
        <PageHeader title={<Skeleton className="h-4 w-64" />} />
        <div className="space-y-3 p-4">
          <Skeleton className="h-3 w-full" />
          <Skeleton className="h-3 w-5/6" />
        </div>
      </>
    );
  }

  if (current.epic.at === "failed") {
    return (
      <>
        <PageHeader title={key} />
        <p className="p-4 text-sm text-destructive">{current.epic.why}</p>
        <p className="px-4 text-sm">
          <Link className="text-brand hover:underline" to={`/${project}/epics`}>All epics</Link>
        </p>
      </>
    );
  }

  const epic = current.epic.value;
  const changed = (value: Epic) => {
    setState((old) => ({ key, epic: { at: "known", value }, issues: old?.issues ?? asking }));
    setEditingKey(undefined);
  };

  if (deleted !== undefined) {
    return (
      <>
        <PageHeader title={deleted.key} />
        <div className="m-auto grid max-w-md justify-items-center gap-3 p-8 text-center">
          <p>This epic is deleted and hidden from the project.</p>
          <EpicAction
            label={`Restore ${deleted.key}`}
            path="/epics/{key}/restore"
            epic={deleted}
            onChanged={(value) => {
              setRemoved(undefined);
              changed(value);
            }}
          />
        </div>
      </>
    );
  }

  if (editing) {
    return (
      <>
        <PageHeader title={`Edit ${epic.key}`} />
        <EditEpicForm epic={epic} onSaved={changed} onCancel={() => setEditingKey(undefined)} />
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <span className="font-mono text-xs font-normal text-muted-foreground">{epic.key}</span>
            {epic.title}
            {epic.status === "closed" && <Badge variant="secondary" className="font-normal">closed</Badge>}
          </span>
        }
      >
        <Button variant="outline" size="sm" onClick={() => setEditingKey(key)}>Edit</Button>
      </PageHeader>

      <div className="max-w-3xl flex-1 p-4 md:p-6">
        <Progress epic={epic} />
        {epic.labels.length > 0 && (
          <div className="mb-5 flex flex-wrap gap-1">
            {epic.labels.map((label) => (
              <Badge key={label.name} variant="secondary" className="font-normal">{label.name}</Badge>
            ))}
          </div>
        )}
        <Section title="Description">
          {epic.description === "" ? (
            <p className="text-sm text-muted-foreground">
              No description yet. It is the shared context for whoever works under this epic.
            </p>
          ) : (
            <Markdown>{epic.description}</Markdown>
          )}
        </Section>
        <Issues loaded={current.issues} project={project!} epic={epic} />
        <Actions epic={epic} onChanged={changed} onDeleted={() => setRemoved({ key, epic })} />
        <Section title="About">
          <dl className="grid gap-1 text-sm sm:grid-cols-[8rem_1fr]">
            <dt className="text-muted-foreground">Author</dt><dd>{epic.author.name}</dd>
            <dt className="text-muted-foreground">Created</dt><dd>{date(epic.created_at)}</dd>
            <dt className="text-muted-foreground">Updated</dt><dd>{date(epic.updated_at)}</dd>
            {epic.closed_at !== null && (<><dt className="text-muted-foreground">Closed</dt><dd>{date(epic.closed_at)}</dd></>)}
          </dl>
        </Section>
      </div>
    </>
  );
}

function Progress({ epic }: { epic: Epic }) {
  const share = epic.progress.total === 0 ? 0 : epic.progress.closed / epic.progress.total;

  return (
    <div className="mb-5 flex items-center gap-3">
      <span className="h-1.5 w-40 overflow-hidden rounded-full bg-muted">
        <span className="block h-full bg-brand" style={{ width: `${Math.round(share * 100)}%` }} />
      </span>
      <span className="text-xs text-muted-foreground">
        {epic.progress.closed} of {epic.progress.total} closed
        {epic.progress.canceled > 0 && ` · ${epic.progress.canceled} canceled`}
      </span>
    </div>
  );
}

function Issues({ loaded, project, epic }: { loaded: Load<IssueSummary[]>; project: string; epic: Epic }) {
  return (
    <Section
      title="Issues"
      action={
        <span className="flex items-center gap-3">
          <Link className="text-xs text-brand hover:underline" to={`/${project}/issues?epic=${epic.key}`}>
            In the issue list
          </Link>
          {/* The bracket is where somebody decides that something else hangs
              under it, so the way to add it starts here and arrives at the
              form with the epic already filled in. */}
          <Link className="text-xs text-brand hover:underline" to={`/${project}/issues/new?epic=${epic.key}`}>
            New issue
          </Link>
        </span>
      }
    >
      {loaded.at === "asking" && <Skeleton className="h-16 w-full" />}
      {loaded.at === "failed" && <p className="text-sm text-destructive">{loaded.why}</p>}
      {loaded.at === "known" && loaded.value.length === 0 && (
        <p className="text-sm text-muted-foreground">Nothing hangs under this epic yet.</p>
      )}
      {loaded.at === "known" && loaded.value.length > 0 && (
        <ul className="divide-y rounded-md border">
          {loaded.value.map((issue) => (
            <li key={issue.key} className="flex min-h-9 items-center gap-3 px-3 py-1">
              <StatusDot status={issue.status} />
              <Link className="font-mono text-xs text-brand hover:underline" to={keyPath(issue.key)}>{issue.key}</Link>
              <span className="min-w-0 flex-1 truncate text-sm">{issue.title}</span>
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

/**
 * Close, reopen, delete, restore — the four moves of a bracket. Closing an epic
 * with open issues succeeds and warns first, because the warning is the whole
 * point: the issues stay workable, and whoever closes it should know that.
 */
function Actions({ epic, onChanged, onDeleted }: { epic: Epic; onChanged: (epic: Epic) => void; onDeleted: () => void }) {
  const open = epic.progress.total - epic.progress.closed;

  return (
    <Section title="Actions">
      <div className="flex flex-wrap gap-2">
        {epic.status === "open" ? (
          <ActionDialog
            trigger={<Button variant="outline">Close epic</Button>}
            title={`Close ${epic.key}?`}
            description={
              open > 0
                ? `${open} ${open === 1 ? "issue is" : "issues are"} still open. They stay workable — an epic is a bracket and gates nothing.`
                : "Everything under this epic is closed."
            }
            confirmLabel="Close epic"
            confirmVariant="default"
            onConfirm={async () => onChanged(await epicRequest("/epics/{key}/close", epic))}
          />
        ) : (
          <EpicAction label="Reopen epic" path="/epics/{key}/reopen" epic={epic} onChanged={onChanged} />
        )}
        <ActionDialog
          trigger={<Button variant="destructive">Delete epic</Button>}
          title={`Delete ${epic.key}?`}
          description="The epic will be hidden from the project, but can be restored during the grace period. An epic that issues still reference cannot be deleted."
          confirmLabel="Delete epic"
          onConfirm={async () => {
            const result = await api.DELETE("/epics/{key}", { params: { path: { key: epic.key } } });
            if (!result.response.ok) throw new Error(describe(result.error, result.response.status));
            onDeleted();
          }}
        />
      </div>
    </Section>
  );
}

type Act = "/epics/{key}/close" | "/epics/{key}/reopen" | "/epics/{key}/restore";

async function epicRequest(path: Act, epic: Epic): Promise<Epic> {
  const result = await api.POST(path as "/epics/{key}/close", { params: { path: { key: epic.key } } });
  if (!result.data) throw new Error(describe(result.error, result.response.status));
  return result.data;
}

function EpicAction({ label, path, epic, onChanged }: { label: string; path: Act; epic: Epic; onChanged: (epic: Epic) => void }) {
  const [busy, setBusy] = useState(false);
  const [why, setWhy] = useState<string>();

  async function run() {
    setBusy(true);
    setWhy(undefined);
    try {
      onChanged(await epicRequest(path, epic));
    } catch (reason) {
      setWhy(reason instanceof Error ? reason.message : "The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <span className="flex items-center gap-2">
      <Button variant="outline" disabled={busy} onClick={() => void run()}>{busy ? "Working…" : label}</Button>
      {why !== undefined && <span role="alert" className="text-xs text-destructive">{why}</span>}
    </span>
  );
}

function EditEpicForm({ epic, onSaved, onCancel }: { epic: Epic; onSaved: (epic: Epic) => void; onCancel: () => void }) {
  // The version the next write is guarded with, and the epic a refusal handed
  // back. Adopting both is what turns the refusal into a way forward: the typed
  // text stays in the form, the other version is there to merge from, and
  // saving again is a decision rather than a request that can only fail.
  const [version, setVersion] = useState(epic.updated_at);
  const [conflict, setConflict] = useState<Epic>();

  return (
    <EpicForm
      initial={epic}
      submit="Save changes"
      onCancel={onCancel}
      notice={conflict === undefined ? undefined : <Conflict epic={conflict} />}
      // `If-Match` with the `updated_at` last read: the description is a living
      // document, and two people keeping it current must not overwrite silently.
      write={async (draft) => {
        setConflict(undefined);
        const answer = await api.PATCH("/epics/{key}", {
          params: { path: { key: epic.key } },
          headers: { "If-Match": version },
          body: draft,
        });

        const current = stale<Epic>(answer);
        if (current !== undefined) {
          setConflict(current);
          setVersion(current.updated_at);
        }

        return answer;
      }}
      onWritten={onSaved}
    />
  );
}

/** Create an epic: a bracket, and the plan for what will hang under it. */
export function NewEpicView() {
  const { project } = useParams();
  const navigate = useNavigate();

  return (
    <>
      <PageHeader title="Create epic" />
      <EpicForm
        submit="Create epic"
        onCancel={() => void navigate(`/${project}/epics`)}
        write={(draft) => api.POST("/epics", { body: { project: project!, ...draft } })}
        onWritten={(epic) => void navigate(keyPath(epic.key), { replace: true })}
      />
    </>
  );
}

type Draft = { title: string; description: string; labels: string[] };
type Written = { data?: Epic; error?: unknown; response: Response };

/**
 * What a stale refusal means, in the words it means it. The typed text is
 * still in the fields above; this says what the other version holds, so it can
 * be merged by hand, and that the next save is now an overwrite of it.
 */
function Conflict({ epic }: { epic: Epic }) {
  return (
    <div role="alert" className="grid gap-2 rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
      <p>
        <span className="font-medium">{epic.key} was changed while you were editing it.</span>{" "}
        Your text is kept. Saving now writes it over the version below.
      </p>
      <details className="text-xs">
        <summary className="cursor-pointer text-muted-foreground">The description as it stands, saved {date(epic.updated_at)}</summary>
        <pre className="mt-2 max-h-64 overflow-auto rounded-md bg-muted p-2 font-mono whitespace-pre-wrap">{epic.description}</pre>
      </details>
    </div>
  );
}

function EpicForm({ initial, submit, write, onWritten, onCancel, notice }: {
  initial?: Epic;
  submit: string;
  write: (draft: Draft) => Promise<Written>;
  onWritten: (epic: Epic) => void;
  onCancel: () => void;
  /** What stands between the fields and the buttons — the conflict, where there is one. */
  notice?: ReactNode;
}) {
  const [title, setTitle] = useState(initial?.title ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [labels, setLabels] = useState(initial?.labels.map((label) => label.name) ?? []);
  const [saving, setSaving] = useState(false);
  const [why, setWhy] = useState<string>();
  const titleId = useId();
  const { project } = useParams();
  const known = useLabels(project);
  const start = { title: initial?.title ?? "", description: initial?.description ?? "", labels: initial?.labels.map((label) => label.name) ?? [] };
  const { leave, dialog } = useAbandon(JSON.stringify({ title, description, labels }) !== JSON.stringify(start), onCancel);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setWhy(undefined);

    try {
      const { data, error, response } = await write({
        title,
        description,
        labels,
      });

      if (data === undefined) {
        setWhy(describe(error as Parameters<typeof describe>[0], response.status));
        return;
      }

      onWritten(data);
    } catch {
      setWhy("The instance did not answer.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="mx-auto grid w-full max-w-3xl gap-4 p-4 md:p-6" onSubmit={(event) => void save(event)}>
      <label className="grid gap-1 text-sm font-medium">
        Title
        <Input id={titleId} required autoFocus value={title} onChange={(event) => setTitle(event.target.value)} />
      </label>
      <MarkdownField label="Description" value={description} onChange={setDescription} />
      <LabelPicker label="Labels" labels={known.labels} value={labels} onChange={setLabels} onCreate={known.create} />
      {/* A conflict says everything the refusal's own sentence says, and says
          what to do about it, so it stands in its place rather than beside it. */}
      {notice ?? (why !== undefined && <p role="alert" className="text-sm text-destructive">{why}</p>)}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={leave}>Cancel</Button>
        <Button type="submit" disabled={saving}>{saving ? "Saving…" : submit}</Button>
      </div>
      {dialog}
    </form>
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

function date(value: string) {
  return new Date(value).toLocaleString();
}
