import { useEffect, useId, useState, type FormEvent, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api, describe, type Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { LabelPicker } from "@/components/ui/label-picker";
import { Skeleton } from "@/components/ui/skeleton";
import { MarkdownField } from "@/shared/MarkdownField";
import { useLabels } from "@/projects/useLabels";
import { useAbandon } from "@/shared/abandon";
import { ActionDialog, TextActionDialog } from "@/shared/ActionDialog";
import { Markdown } from "@/shared/Markdown";
import { PageHeader } from "@/shared/PageHeader";
import { stale } from "@/shared/stale";
import { pagePath } from "@/shell/views";

type Page = Schemas["Page"];
type Load<T> = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; value: T };

const asking = { at: "asking" } as const;

/**
 * A page of the project's wiki: the Markdown, rendered in the browser and
 * never as HTML (ADR 0007), with the editor in its place rather than beside
 * it. The address is the slug, which is why renaming is its own act here too —
 * nothing forwards, and every link written to the old one stops working
 * (ADR 0021).
 */
export function PageView() {
  const { project, slug } = useParams();
  const at = `${project}/${slug}`;
  const [state, setState] = useState<{ at: string; page: Load<Page> }>();
  // Both are about the page in the address, so both carry it: walking to
  // another page leaves neither the editor nor a deleted one's way back open.
  const [editingAt, setEditingAt] = useState<string>();
  const [removed, setRemoved] = useState<{ at: string; page: Page }>();
  const current = state !== undefined && state.at === at ? state.page : asking;
  const editing = editingAt === at;
  const deleted = removed !== undefined && removed.at === at ? removed.page : undefined;

  useEffect(() => {
    let live = true;

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/projects/{key}/pages/{slug}", {
          params: { path: { key: project!, slug: slug! } },
        });

        if (live) {
          setState({
            at,
            page: data === undefined ? { at: "failed", why: describe(error, response.status) } : { at: "known", value: data },
          });
        }
      } catch {
        if (live) {
          setState({ at, page: { at: "failed", why: "The instance did not answer." } });
        }
      }
    })();

    return () => {
      live = false;
    };
  }, [at, project, slug]);

  if (current.at === "asking") {
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

  if (current.at === "failed") {
    return (
      <>
        <PageHeader title={slug!} />
        <p className="p-4 text-sm text-destructive">{current.why}</p>
        <p className="px-4 text-sm">
          <Link className="text-brand hover:underline" to={`/${project}/pages`}>All pages</Link>
        </p>
      </>
    );
  }

  const page = current.value;
  const changed = (value: Page) => {
    setState({ at: `${project}/${value.slug}`, page: { at: "known", value } });
    setEditingAt(undefined);
  };

  if (deleted !== undefined) {
    return (
      <>
        <PageHeader title={deleted.slug} />
        <div className="m-auto grid max-w-md justify-items-center gap-3 p-8 text-center">
          <p>This page is deleted and hidden from the project. Its slug is held until the grace period is over.</p>
          <Restore
            page={deleted}
            project={project!}
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
        <PageHeader title={`Edit ${page.slug}`} />
        <EditPageForm project={project!} page={page} onSaved={changed} onCancel={() => setEditingAt(undefined)} />
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <span className="font-mono text-xs font-normal text-muted-foreground">{page.slug}</span>
            {page.title}
          </span>
        }
      >
        <Button variant="outline" size="sm" onClick={() => setEditingAt(at)}>Edit</Button>
      </PageHeader>

      <div className="max-w-3xl flex-1 p-4 md:p-6">
        {page.labels.length > 0 && (
          <div className="mb-5 flex flex-wrap gap-1">
            {page.labels.map((label) => (
              <Badge key={label.name} variant="secondary" className="font-normal">{label.name}</Badge>
            ))}
          </div>
        )}
        {page.body === "" ? (
          <p className="text-sm text-muted-foreground">
            This page is empty. It is the place for what the project knows and no ticket asks for.
          </p>
        ) : (
          <Markdown>{page.body}</Markdown>
        )}

        <Section title="Actions">
          <div className="flex flex-wrap gap-2">
            <Rename project={project!} page={page} onChanged={changed} />
            <ActionDialog
              trigger={<Button variant="destructive">Delete page</Button>}
              title={`Delete ${page.slug}?`}
              description="The page will be hidden from the project, but can be restored during the grace period. Its slug stays taken until then, so nothing else can move into the address."
              confirmLabel="Delete page"
              onConfirm={async () => {
                const result = await api.DELETE("/projects/{key}/pages/{slug}", {
                  params: { path: { key: project!, slug: page.slug } },
                });
                if (!result.response.ok) throw new Error(describe(result.error, result.response.status));
                setRemoved({ at, page });
              }}
            />
          </div>
        </Section>

        <Section title="About">
          <dl className="grid gap-1 text-sm sm:grid-cols-[8rem_1fr]">
            <dt className="text-muted-foreground">Author</dt><dd>{page.author.name}</dd>
            <dt className="text-muted-foreground">Created</dt><dd>{date(page.created_at)}</dd>
            <dt className="text-muted-foreground">Last change</dt><dd>{date(page.updated_at)} by {page.updated_by.name}</dd>
          </dl>
        </Section>
      </div>
    </>
  );
}

function Restore({ project, page, onChanged }: { project: string; page: Page; onChanged: (page: Page) => void }) {
  const [busy, setBusy] = useState(false);
  const [why, setWhy] = useState<string>();

  async function run() {
    setBusy(true);
    setWhy(undefined);
    try {
      const { data, error, response } = await api.POST("/projects/{key}/pages/{slug}/restore", {
        params: { path: { key: project, slug: page.slug } },
      });
      if (data === undefined) throw new Error(describe(error, response.status));
      onChanged(data);
    } catch (reason) {
      setWhy(reason instanceof Error ? reason.message : "The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <span className="flex items-center gap-2">
      <Button variant="outline" disabled={busy} onClick={() => void run()}>
        {busy ? "Working…" : `Restore ${page.slug}`}
      </Button>
      {why !== undefined && <span role="alert" className="text-xs text-destructive">{why}</span>}
    </span>
  );
}

/**
 * Renaming is an act with a warning rather than a field in the form: the old
 * address leads nowhere afterwards, nothing forwards, and every reference
 * written to it stops working (ADR 0021). That is worth being told once.
 */
function Rename({ project, page, onChanged }: { project: string; page: Page; onChanged: (page: Page) => void }) {
  const navigate = useNavigate();

  return (
    <TextActionDialog
      trigger={<Button variant="outline">Rename page</Button>}
      title={`Rename ${page.slug}?`}
      description="The page moves to the new address. Nothing forwards: the old slug leads nowhere afterwards, and links written to it stop working. The rename stands in the history."
      label="New slug"
      initialValue={page.slug}
      submitLabel="Rename page"
      onSubmit={async (slug) => {
        const { data, error, response } = await api.PATCH("/projects/{key}/pages/{slug}", {
          params: { path: { key: project, slug: page.slug } },
          body: { slug },
        });
        if (data === undefined) throw new Error(describe(error, response.status));
        onChanged(data);
        void navigate(pagePath(project, data.slug), { replace: true });
      }}
    />
  );
}

function EditPageForm({ project, page, onSaved, onCancel }: {
  project: string;
  page: Page;
  onSaved: (page: Page) => void;
  onCancel: () => void;
}) {
  // The version the next write is guarded with, and the page a refusal handed
  // back. Adopting both is what turns the refusal into a way forward: the typed
  // text stays in the form, the other version is there to merge from, and
  // saving again is a decision rather than a request that can only fail.
  const [version, setVersion] = useState(page.updated_at);
  const [conflict, setConflict] = useState<Page>();

  return (
    <PageForm
      project={project}
      initial={page}
      submit="Save changes"
      onCancel={onCancel}
      notice={conflict === undefined ? undefined : <Conflict page={conflict} />}
      // `If-Match` with the `updated_at` last read: a page is a text a human
      // and an agent both edit, and neither may overwrite the other silently.
      write={async (draft) => {
        setConflict(undefined);
        const answer = await api.PATCH("/projects/{key}/pages/{slug}", {
          params: { path: { key: project, slug: page.slug } },
          headers: { "If-Match": version },
          body: { title: draft.title, body: draft.body, labels: draft.labels },
        });

        const current = stale<Page>(answer);
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

/** Create a page: the slug is given here and never derived from the title (ADR 0021). */
export function NewPageView() {
  const { project } = useParams();
  const navigate = useNavigate();

  return (
    <>
      <PageHeader title="Create page" />
      <PageForm
        project={project!}
        slugField
        submit="Create page"
        onCancel={() => void navigate(`/${project}/pages`)}
        write={(draft) => api.POST("/projects/{key}/pages", { params: { path: { key: project! } }, body: draft })}
        onWritten={(page) => void navigate(pagePath(project!, page.slug), { replace: true })}
      />
    </>
  );
}

type Draft = { slug: string; title: string; body: string; labels: string[] };
type Written = { data?: Page; error?: unknown; response: Response };

/**
 * What a stale refusal means, in the words it means it. The typed text is
 * still in the fields above; this says what the other version holds, so it can
 * be merged by hand, and that the next save is now an overwrite of it.
 */
function Conflict({ page }: { page: Page }) {
  return (
    <div role="alert" className="grid gap-2 rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
      <p>
        <span className="font-medium">{page.slug} was changed while you were editing it.</span>{" "}
        Your text is kept. Saving now writes it over the version below.
      </p>
      <details className="text-xs">
        <summary className="cursor-pointer text-muted-foreground">The page as it stands, saved {date(page.updated_at)}</summary>
        <pre className="mt-2 max-h-64 overflow-auto rounded-md bg-muted p-2 font-mono whitespace-pre-wrap">{page.body}</pre>
      </details>
    </div>
  );
}

function PageForm({ project, initial, slugField, submit, write, onWritten, onCancel, notice }: {
  project: string;
  initial?: Page;
  /** Only where the address is being decided: renaming is its own act. */
  slugField?: boolean;
  submit: string;
  write: (draft: Draft) => Promise<Written>;
  onWritten: (page: Page) => void;
  onCancel: () => void;
  /** What stands between the fields and the buttons — the conflict, where there is one. */
  notice?: ReactNode;
}) {
  const [slug, setSlug] = useState(initial?.slug ?? "");
  const [title, setTitle] = useState(initial?.title ?? "");
  const [body, setBody] = useState(initial?.body ?? "");
  const [labels, setLabels] = useState(initial?.labels.map((label) => label.name) ?? []);
  const [saving, setSaving] = useState(false);
  const [why, setWhy] = useState<string>();
  const slugId = useId();
  const titleId = useId();
  const known = useLabels(project);
  const start = {
    slug: initial?.slug ?? "",
    title: initial?.title ?? "",
    body: initial?.body ?? "",
    labels: initial?.labels.map((label) => label.name) ?? [],
  };
  const { leave, dialog } = useAbandon(JSON.stringify({ slug, title, body, labels }) !== JSON.stringify(start), onCancel);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setWhy(undefined);

    try {
      const { data, error, response } = await write({ slug, title, body, labels });

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
      {slugField === true && (
        // The hint stands beside the field rather than inside its label: a
        // label that carries a paragraph is what a screen reader reads out
        // before every keystroke.
        <div className="grid gap-1 text-sm font-medium">
          <label htmlFor={slugId}>Slug</label>
          <Input
            id={slugId}
            name="slug"
            required
            autoFocus
            value={slug}
            onChange={(event) => setSlug(event.target.value)}
          />
          <span className="text-xs font-normal text-muted-foreground">
            The address of the page: lower case letters and digits, hyphens between the words. It is not derived
            from the title, and renaming it later leaves nothing behind at the old one.
          </span>
        </div>
      )}
      <label className="grid gap-1 text-sm font-medium" htmlFor={titleId}>
        Title
        <Input
          id={titleId}
          name="title"
          required
          autoFocus={slugField !== true}
          value={title}
          onChange={(event) => setTitle(event.target.value)}
        />
      </label>
      <MarkdownField label="Body" value={body} onChange={setBody} />
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

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mt-6 border-t py-5">
      <h2 className="mb-3 text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
      {children}
    </section>
  );
}

function date(value: string) {
  return new Date(value).toLocaleString();
}
