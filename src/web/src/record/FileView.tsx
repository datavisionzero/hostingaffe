import { useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { api, describe, type Problem, type Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/shared/PageHeader";
import { useAbandon } from "@/shared/abandon";
import { useAsk } from "@/shared/ask";
import { Failed, Nothing, Section, Waiting } from "@/shared/Detail";
import { moment } from "@/shared/when";
import { anchorPath } from "./addresses";
import { Asks, Line, Rows } from "./Parts";

type Anchor = Schemas["Anchor"];
type FileContent = Schemas["File"];
type FileRevision = Schemas["FileRevision"];

/**
 * A file of the record: the text a machine or an installation runs with, at the
 * revision the address asks for, with every revision beside it and a difference
 * between any two (VISION 6.2).
 *
 * The whole of it is one component for both owners. A file on a machine and a
 * file on an installation are the same object hanging from a different anchor
 * (VISION 7), and two screens for it would be two places to fix one bug.
 */
export function FileView({ owner: kind }: { owner: Anchor["kind"] }) {
  const params = useParams();
  const [search, setSearch] = useSearchParams();
  const key = params.key ?? "";
  // The path is the splat: it carries slashes, so it is the rest of the address
  // rather than one segment of it.
  const path = params["*"] ?? "";
  const owner: Anchor = { kind, key };

  // Which revision is being read, and which one it is being compared against.
  // Both are in the address: a difference somebody found is a link.
  const at = search.get("revision");
  const against = search.get("against");
  const revision = at === null ? undefined : Number(at);

  const { asked, again } = useAsk<FileContent>(`${kind}:${key}:${path}:${at ?? "head"}`, (signal) =>
    kind === "machine"
      ? api.GET("/api/machines/{key}/files/{path}", { params: { path: { key, path }, query: { revision } }, signal })
      : api.GET("/api/installations/{key}/files/{path}", { params: { path: { key, path }, query: { revision } }, signal }));

  const revisions = useAsk<FileRevision[]>(`${kind}:${key}:${path}`, (signal) =>
    kind === "machine"
      ? api.GET("/api/machines/{key}/file-revisions/{path}", { params: { path: { key, path } }, signal })
      : api.GET("/api/installations/{key}/file-revisions/{path}", { params: { path: { key, path } }, signal }));

  // The revision being compared against, where the address names one. `null` is
  // an answer and not a failure: nothing is being compared, which is the usual
  // case and must not colour the screen red.
  const other = useAsk<FileContent | null>(
    against === null ? "" : `${kind}:${key}:${path}:${against}`,
    (signal) => against === null
      ? Promise.resolve({ data: null, response: new Response(null, { status: 204 }) })
      : kind === "machine"
        ? api.GET("/api/machines/{key}/files/{path}", { params: { path: { key, path }, query: { revision: Number(against) } }, signal })
        : api.GET("/api/installations/{key}/files/{path}", { params: { path: { key, path }, query: { revision: Number(against) } }, signal }));

  const [editing, setEditing] = useState(false);

  if (asked.at === "asking") return <Waiting title="Loading the file…" />;

  if (asked.at === "failed") {
    return (
      <Failed
        title={path}
        why={asked.why}
        back={<Link className="text-brand hover:underline" to={anchorPath(owner)}>Back to {key}</Link>}
      />
    );
  }

  const file = asked.value;
  const head = revision === undefined;
  const compared = other.asked.at === "known" && other.asked.value !== null ? other.asked.value : undefined;

  const set = (name: string, value: string | undefined) => {
    const kept = new URLSearchParams(search);
    if (value === undefined) kept.delete(name);
    else kept.set(name, value);
    setSearch(kept, { replace: true });
  };

  return (
    <>
      <PageHeader
        title={
          <span className="flex flex-wrap items-center gap-2">
            <Link className="text-xs font-normal text-muted-foreground hover:underline" to={anchorPath(owner)}>
              {key}
            </Link>
            <span className="font-mono">{file.path}</span>
          </span>
        }
        meta={file.directory ? `revision ${file.revision} · lies in ${file.directory}` : `revision ${file.revision}`}
      >
        {file.executable && <Badge variant="outline">executable</Badge>}
        {/* An old revision is read-only. Writing from one would take the file
            back to what it said then, silently, and the way to do that on
            purpose is to copy the text into a new write. */}
        {head && !editing && <Button variant="outline" size="sm" onClick={() => setEditing(true)}>Edit</Button>}
      </PageHeader>

      {!head && (
        <p className="border-b bg-amber-500/5 px-4 py-2 text-sm">
          Revision {file.revision} of {file.path}, as it was.{" "}
          <button type="button" className="text-brand underline-offset-4 hover:underline" onClick={() => set("revision", undefined)}>
            Read the current one
          </button>
        </p>
      )}

      <div className="max-w-5xl flex-1 p-4 md:p-6">
        {editing ? (
          <WriteFile
            owner={owner}
            file={file}
            onDone={() => { setEditing(false); again(); revisions.again(); }}
            onCancel={() => setEditing(false)}
          />
        ) : compared !== undefined ? (
          <Section
            title={`Difference against revision ${compared.revision}`}
            action={
              <Button variant="ghost" size="sm" onClick={() => set("against", undefined)}>Stop comparing</Button>
            }
          >
            <Diff from={compared.content} to={file.content} />
          </Section>
        ) : (
          <Section title="Content">
            {file.content === "" ? (
              <Nothing>This revision of the file is empty.</Nothing>
            ) : (
              // The text as it is: no highlighting that could colour something
              // into meaning it does not have, and no wrapping that would break
              // a line somebody is about to copy onto a machine.
              <pre className="overflow-x-auto rounded-md border bg-muted p-3 font-mono text-xs">{file.content}</pre>
            )}
          </Section>
        )}

        <Asks
          title="Revisions"
          meta={revisions.asked.at === "known" ? `${revisions.asked.value.length}` : undefined}
          asked={revisions.asked}
        >
          {(all) => (
            <Rows>
              {all.map((one) => (
                <Line key={one.revision}>
                  <span className="w-16 shrink-0 text-xs">
                    {one.revision === file.revision ? <strong>rev {one.revision}</strong> : `rev ${one.revision}`}
                  </span>
                  <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">
                    {moment(one.at)} · {one.by.name}
                  </span>
                  {one.revision !== file.revision && (
                    <>
                      <button
                        type="button"
                        className="text-brand text-xs underline-offset-4 hover:underline"
                        onClick={() => set("revision", String(one.revision))}
                      >
                        Read
                      </button>
                      <button
                        type="button"
                        className="text-brand text-xs underline-offset-4 hover:underline"
                        onClick={() => set("against", String(one.revision))}
                      >
                        Compare
                      </button>
                    </>
                  )}
                </Line>
              ))}
            </Rows>
          )}
        </Asks>

        <Section title="About">
          <dl className="grid gap-1 text-sm sm:grid-cols-[10rem_1fr]">
            <dt className="text-muted-foreground">Belongs to</dt>
            <dd><Link className="text-brand hover:underline" to={anchorPath(file.owner)}>{file.owner.key}</Link></dd>
            <dt className="text-muted-foreground">First written</dt>
            <dd>{moment(file.created_at)} by {file.created_by.name}</dd>
            <dt className="text-muted-foreground">This revision</dt>
            <dd>{moment(file.updated_at)} by {file.updated_by.name}</dd>
          </dl>
        </Section>
      </div>
    </>
  );
}

/**
 * Writing a file: a new revision, guarded by the one that was read.
 *
 * A file is guarded with its revision and not with a timestamp
 * (`docs/api.md`, Guarding a write), which is the difference that matters here:
 * two people editing the same unit file is the normal case, not the exception.
 */
function WriteFile({ owner, file, onDone, onCancel }: {
  owner: Anchor;
  file: FileContent;
  onDone: () => void;
  onCancel: () => void;
}) {
  const [content, setContent] = useState(file.content);
  // Only a machine's file says where on the machine it lies; an installation's
  // lie under the installation's own path, which it says once for all of them.
  const places = owner.kind === "machine";
  const [directory, setDirectory] = useState(file.directory ?? "");
  const [guard, setGuard] = useState(file.revision);
  const [conflict, setConflict] = useState<FileContent>();
  const [saving, setSaving] = useState(false);
  const [why, setWhy] = useState<string>();
  const moved = places && directory !== (file.directory ?? "");
  const { leave, dialog } = useAbandon(content !== file.content || moved, onCancel);

  async function save() {
    setSaving(true);
    setWhy(undefined);
    setConflict(undefined);

    try {
      // The directory goes along only where it changed: left out, it stays
      // what it is, and it is never part of a revision.
      const body = moved ? { content, executable: file.executable, directory } : { content, executable: file.executable };
      const headers = { "If-Match": String(guard) };
      const path = { key: owner.key, path: file.path };
      const answer = owner.kind === "machine"
        ? await api.PUT("/api/machines/{key}/files/{path}", { params: { path }, headers, body })
        : await api.PUT("/api/installations/{key}/files/{path}", { params: { path }, headers, body });

      if (answer.response.status === 412) {
        const current = (answer.error as { current?: FileContent } | undefined)?.current;
        if (current !== undefined) {
          setGuard(current.revision);
          setConflict(current);
          return;
        }
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
    <Section title={`Write ${file.path}`}>
      <div className="grid gap-3">
        {places && (
          <label className="grid gap-1 text-sm">
            <span className="text-muted-foreground">Directory on the machine</span>
            <input
              name="directory"
              aria-label="Directory on the machine"
              spellCheck={false}
              placeholder="/etc/systemd/system"
              value={directory}
              onChange={(event) => setDirectory(event.target.value)}
              className="w-full rounded-md border bg-transparent px-3 py-2 font-mono text-xs outline-hidden focus-visible:ring-[3px] focus-visible:ring-ring/50"
            />
          </label>
        )}
        <textarea
          name="content"
          aria-label="File content"
          spellCheck={false}
          value={content}
          onChange={(event) => setContent(event.target.value)}
          className="min-h-96 w-full rounded-md border bg-transparent p-3 font-mono text-xs outline-hidden focus-visible:ring-[3px] focus-visible:ring-ring/50"
        />
        {conflict !== undefined && (
          <div role="alert" className="grid gap-2 rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
            <p>
              <span className="font-medium">
                {file.path} was written while you were editing it — it is at revision {conflict.revision} now.
              </span>{" "}
              Your text is kept. Saving now writes it over the revision below.
            </p>
            <details className="text-xs">
              <summary className="cursor-pointer text-muted-foreground">
                Revision {conflict.revision}, written {moment(conflict.updated_at)} by {conflict.updated_by.name}
              </summary>
              <pre className="mt-2 max-h-64 overflow-auto rounded-md bg-muted p-2 font-mono whitespace-pre-wrap">
                {conflict.content}
              </pre>
            </details>
          </div>
        )}
        {why !== undefined && <p role="alert" className="text-sm text-destructive">{why}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={leave}>Cancel</Button>
          <Button type="button" disabled={saving} onClick={() => void save()}>
            {saving ? "Writing…" : "Write a new revision"}
          </Button>
        </div>
        {dialog}
      </div>
    </Section>
  );
}

/**
 * The difference between two revisions, line by line.
 *
 * A longest-common-subsequence over lines, computed here rather than pulled in:
 * a file of the record is a unit file or a compose file, the comparison is
 * between two revisions of it, and that is a table small enough to fill without
 * a library — which is one dependency fewer in the chunk the whole application
 * pays for (ADR 0017).
 */
function Diff({ from, to }: { from: string; to: string }) {
  const before = from.split("\n");
  const after = to.split("\n");
  const lines = walk(before, after);

  if (lines.every((line) => line.mark === " ")) {
    return <Nothing>These two revisions say the same thing.</Nothing>;
  }

  return (
    <pre className="overflow-x-auto rounded-md border bg-muted p-3 font-mono text-xs">
      {lines.map((line, index) => (
        <div
          key={index}
          className={
            line.mark === "+"
              ? "bg-emerald-500/15 text-emerald-900 dark:text-emerald-200"
              : line.mark === "-"
                ? "bg-red-500/15 text-red-900 dark:text-red-200"
                : undefined
          }
        >
          <span aria-hidden className="select-none text-muted-foreground">{line.mark} </span>
          {line.text === "" ? " " : line.text}
        </div>
      ))}
    </pre>
  );
}

type Marked = { mark: "+" | "-" | " "; text: string };

function walk(before: string[], after: string[]): Marked[] {
  // The classic table: `common[i][j]` is the length of the longest common run of
  // lines from `before[i…]` and `after[j…]`.
  const common: number[][] = Array.from({ length: before.length + 1 }, () => new Array<number>(after.length + 1).fill(0));

  for (let i = before.length - 1; i >= 0; i -= 1) {
    for (let j = after.length - 1; j >= 0; j -= 1) {
      common[i][j] = before[i] === after[j]
        ? common[i + 1][j + 1] + 1
        : Math.max(common[i + 1][j], common[i][j + 1]);
    }
  }

  const lines: Marked[] = [];
  let i = 0;
  let j = 0;

  while (i < before.length && j < after.length) {
    if (before[i] === after[j]) {
      lines.push({ mark: " ", text: before[i] });
      i += 1;
      j += 1;
    } else if (common[i + 1][j] >= common[i][j + 1]) {
      lines.push({ mark: "-", text: before[i] });
      i += 1;
    } else {
      lines.push({ mark: "+", text: after[j] });
      j += 1;
    }
  }

  while (i < before.length) { lines.push({ mark: "-", text: before[i] }); i += 1; }
  while (j < after.length) { lines.push({ mark: "+", text: after[j] }); j += 1; }

  return lines;
}
