import { useState } from "react";
import { Link } from "react-router";
import { api, describe, type Problem, type Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Markdown } from "@/shared/Markdown";
import { MarkdownField } from "@/shared/MarkdownField";
import { useAbandon } from "@/shared/abandon";
import { stale } from "@/shared/stale";
import { useAsk, type Asked } from "@/shared/ask";
import { Nothing, Section } from "@/shared/Detail";
import { size } from "@/shared/size";
import { day, moment } from "@/shared/when";
import { filePath, installationPath } from "./addresses";

type Anchor = Schemas["Anchor"];
type FileSummary = Schemas["FileSummary"];
type HistoryEntry = Schemas["HistoryEntry"];
type InstallationSummary = Schemas["InstallationSummary"];
type PageSummary = Schemas["PageSummary"];
type Status = Schemas["Status"];

/**
 * What a row's status looks like. `retired` is the one that has to be visible
 * at a glance: a retired machine stays in the record on purpose (VISION 7), and
 * a reader who mistakes one for a live machine acts on a machine that is gone.
 */
export function StatusBadge({ status }: { status: Status }) {
  return (
    <Badge variant={status === "active" ? "secondary" : status === "retired" ? "destructive" : "outline"}>
      {status}
    </Badge>
  );
}

/** A section that says what its own request said, rather than emptying the screen. */
export function Asks<T>({ title, meta, asked, children }: {
  title: string;
  meta?: string;
  asked: Asked<T>;
  children: (value: T) => React.ReactNode;
}) {
  return (
    <Section title={title} meta={asked.at === "known" ? meta : undefined}>
      {asked.at === "asking" && <p aria-busy className="text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="text-sm text-destructive">{asked.why}</p>}
      {asked.at === "known" && children(asked.value)}
    </Section>
  );
}

/** The rows of a list, in the density the wiki already uses. */
export function Rows({ children }: { children: React.ReactNode }) {
  return <ul className="-mx-2 divide-y rounded-md border">{children}</ul>;
}

/** A row with nowhere to go: what it says is all there is of it. */
export function Line({ children }: { children: React.ReactNode }) {
  return <li className="flex min-h-10 flex-wrap items-center gap-x-3 gap-y-1 px-3 py-1.5 text-sm">{children}</li>;
}

export function Row({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <li>
      <Link to={to} className="flex min-h-10 items-center gap-3 px-3 py-1.5 text-sm hover:bg-accent">
        {children}
      </Link>
    </li>
  );
}

/**
 * The installations on a machine, or of a software — the same section from
 * either side, because it is the same relation read in two directions
 * (VISION 7): one software installed once on one machine.
 */
export function Installations({ of }: { of: { machine: string } | { software: string } }) {
  const filter = "machine" in of ? { machine: of.machine } : { software: of.software };
  const at = JSON.stringify(filter);
  const { asked } = useAsk<InstallationSummary[]>(at, (signal) =>
    api.GET("/api/installations", { params: { query: filter }, signal }));

  return (
    <Asks title="Installations" meta={asked.at === "known" ? `${asked.value.length}` : undefined} asked={asked}>
      {(items) => items.length === 0 ? (
        <Nothing>
          {"machine" in of
            ? "Nothing is installed on this machine."
            : "This software is not installed anywhere."}
        </Nothing>
      ) : (
        <Rows>
          {items.map((installation) => (
            <Row key={installation.key} to={installationPath(installation.key)}>
              <span className="w-48 shrink-0 truncate font-mono text-xs">{installation.key}</span>
              <span className="min-w-0 flex-1 truncate">
                {"machine" in of ? installation.software : installation.machine}
              </span>
              <span className="hidden w-28 shrink-0 truncate text-xs text-muted-foreground sm:block">
                {installation.environment}
              </span>
              <span className="hidden w-32 shrink-0 truncate font-mono text-xs md:block">
                {installation.version ?? ""}
              </span>
              <StatusBadge status={installation.status} />
            </Row>
          ))}
        </Rows>
      )}
    </Asks>
  );
}

/** The files a machine or an installation carries, with the size and the revision each is at. */
export function Files({ owner }: { owner: Anchor }) {
  const { asked } = useAsk<FileSummary[]>(`files:${owner.kind}:${owner.key}`, (signal) =>
    owner.kind === "machine"
      ? api.GET("/api/machines/{key}/files", { params: { path: { key: owner.key } }, signal })
      : api.GET("/api/installations/{key}/files", { params: { path: { key: owner.key } }, signal }));

  return (
    <Asks title="Files" meta={asked.at === "known" ? `${asked.value.length}` : undefined} asked={asked}>
      {(items) => items.length === 0 ? (
        <Nothing>
          No files here yet. A file is the text this thing runs with — a unit, a compose file, a
          configuration — and every write of it is a revision.
        </Nothing>
      ) : (
        <Rows>
          {items.map((file) => (
            <Row key={file.path} to={filePath(file.owner, file.path)}>
              <span className="min-w-0 flex-1 truncate font-mono text-xs">{file.path}</span>
              {file.executable && <Badge variant="outline">executable</Badge>}
              <span className="w-24 shrink-0 text-right text-xs tabular-nums text-muted-foreground">{size(file.size)}</span>
              <span className="w-20 shrink-0 text-right text-xs text-muted-foreground">rev {file.revision}</span>
              <span className="hidden w-40 shrink-0 truncate text-right text-xs text-muted-foreground md:block">
                {day(file.updated_at)} · {file.updated_by.name}
              </span>
            </Row>
          ))}
        </Rows>
      )}
    </Asks>
  );
}

/**
 * The pages hanging on this thing. A page does not follow it into deletion and
 * is not part of it — it is what the team knows and no record holds, attached
 * to where it is needed (VISION 7).
 */
export function Attached({ to }: { to: Anchor }) {
  const filter = to.kind === "machine" ? { machine: to.key } : { installation: to.key };
  const { asked } = useAsk<PageSummary[]>(`pages:${to.kind}:${to.key}`, (signal) =>
    api.GET("/api/pages", { params: { query: filter }, signal }));

  return (
    <Asks title="Pages" meta={asked.at === "known" ? `${asked.value.length}` : undefined} asked={asked}>
      {(items) => items.length === 0 ? (
        <Nothing>No page is attached to this.</Nothing>
      ) : (
        <Rows>
          {items.map((page) => (
            <Row key={page.slug} to={`/pages/${encodeURIComponent(page.slug)}`}>
              <span className="w-40 shrink-0 truncate font-mono text-xs text-muted-foreground">{page.slug}</span>
              <span className="min-w-0 flex-1 truncate">{page.title}</span>
              <span className="hidden w-24 shrink-0 truncate text-xs text-muted-foreground sm:block">{page.kind}</span>
            </Row>
          ))}
        </Rows>
      )}
    </Asks>
  );
}

/**
 * Who changed what, oldest first, as the instance keeps it. The history
 * survives the deletion of its subject and the purge (VISION 7), which is why
 * it is a section of the screen and not a property of the object.
 */
export function History({ asked }: { asked: Asked<HistoryEntry[]> }) {
  return (
    <Asks title="History" meta={asked.at === "known" ? `${asked.value.length}` : undefined} asked={asked}>
      {(entries) => entries.length === 0 ? (
        <Nothing>Nothing has changed since this was written down.</Nothing>
      ) : (
        <ol className="grid gap-2 text-sm">
          {[...entries].reverse().map((entry) => (
            <li key={entry.id} className="grid gap-0.5 border-l-2 pl-3">
              <span className="text-xs text-muted-foreground">
                {moment(entry.at)} · {entry.actor.name}
              </span>
              <span className="min-w-0 break-words">
                <span className="font-mono text-xs">{entry.field}</span>
                {entry.old_value !== null && entry.old_value !== "" && (
                  <> <span className="text-muted-foreground line-through">{entry.old_value}</span></>
                )}
                {entry.new_value !== null && entry.new_value !== "" && <> → {entry.new_value}</>}
              </span>
              {entry.note !== null && entry.note !== "" && (
                <span className="text-xs text-muted-foreground italic">{entry.note}</span>
              )}
            </li>
          ))}
        </ol>
      )}
    </Asks>
  );
}

/**
 * The description, which is Markdown everywhere it appears (ADR 0007), and the
 * one field of the record a person edits from the screen rather than from `ha`.
 *
 * It is guarded with the version last read (`docs/api.md`, Guarding a write): a
 * machine's description is a text an operator and an agent both write, and
 * neither may overwrite the other silently. What comes back from a refusal is
 * kept, so the second attempt is a decision and not the same request again.
 */
export function Description({ body, empty, write, version, onWritten }: {
  body: string | null | undefined;
  empty: string;
  /** Where there is no writer, the description is read and nothing more. */
  write?: (description: string, version: string) => Promise<{ data?: unknown; error?: unknown; response: Response }>;
  version?: string;
  onWritten?: () => void;
}) {
  const [editing, setEditing] = useState(false);

  return (
    <Section
      title="Description"
      action={write !== undefined && !editing && (
        <Button variant="outline" size="sm" onClick={() => setEditing(true)}>Edit</Button>
      )}
    >
      {editing && write !== undefined && version !== undefined ? (
        <DescriptionForm
          body={body ?? ""}
          version={version}
          write={write}
          onDone={() => { setEditing(false); onWritten?.(); }}
          onCancel={() => setEditing(false)}
        />
      ) : body === null || body === undefined || body === "" ? (
        <Nothing>{empty}</Nothing>
      ) : (
        <Markdown>{body}</Markdown>
      )}
    </Section>
  );
}

function DescriptionForm({ body, version, write, onDone, onCancel }: {
  body: string;
  version: string;
  write: (description: string, version: string) => Promise<{ data?: unknown; error?: unknown; response: Response }>;
  onDone: () => void;
  onCancel: () => void;
}) {
  const [draft, setDraft] = useState(body);
  const [guard, setGuard] = useState(version);
  const [conflict, setConflict] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [why, setWhy] = useState<string>();
  const { leave, dialog } = useAbandon(draft !== body, onCancel);

  async function save() {
    setSaving(true);
    setWhy(undefined);
    setConflict(undefined);

    try {
      const answer = await write(draft, guard);
      const current = stale<{ updated_at: string; description?: string | null }>(answer);

      if (current !== undefined) {
        // The guard moves to what is there now, and the other side's text is
        // shown rather than swallowed: without both, every further save is
        // refused for the same reason and there is nothing to merge from.
        setGuard(current.updated_at);
        setConflict(current.description ?? "");
        return;
      }

      if (!answer.response.ok) {
        setWhy(describe(answer.error as Problem | undefined, answer.response.status));
        return;
      }

      onDone();
    } catch {
      setWhy("The instance did not answer.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="grid gap-3">
      <MarkdownField label="Description" value={draft} onChange={setDraft} onSubmit={() => void save()} />
      {conflict !== undefined && (
        <div role="alert" className="grid gap-2 rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
          <p>
            <span className="font-medium">This was changed while you were editing it.</span>{" "}
            Your text is kept. Saving now writes it over the version below.
          </p>
          <details className="text-xs">
            <summary className="cursor-pointer text-muted-foreground">The description as it stands</summary>
            <pre className="mt-2 max-h-64 overflow-auto rounded-md bg-muted p-2 font-mono whitespace-pre-wrap">{conflict}</pre>
          </details>
        </div>
      )}
      {why !== undefined && <p role="alert" className="text-sm text-destructive">{why}</p>}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={leave}>Cancel</Button>
        <Button type="button" disabled={saving} onClick={() => void save()}>{saving ? "Saving…" : "Save description"}</Button>
      </div>
      {dialog}
    </div>
  );
}

/** Who wrote this down and who touched it last — the same last section on every screen. */
export function About({ of }: {
  of: { created_by: Schemas["IdentityRef"]; updated_by: Schemas["IdentityRef"]; created_at: string; updated_at: string };
}) {
  return (
    <Section title="About">
      <dl className="grid gap-1 text-sm sm:grid-cols-[10rem_1fr]">
        <dt className="text-muted-foreground">Written down</dt>
        <dd>{moment(of.created_at)} by {of.created_by.name}</dd>
        <dt className="text-muted-foreground">Last change</dt>
        <dd>{moment(of.updated_at)} by {of.updated_by.name}</dd>
      </dl>
    </Section>
  );
}
