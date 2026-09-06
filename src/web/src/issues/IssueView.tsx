import { MoreHorizontalIcon } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";
import { Link, useParams } from "react-router";
import { api, codeOf, describe, type HistoryEntry, type Issue, type Problem } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsPanel, TabsTab } from "@/components/ui/tabs";
import { cn } from "@/lib/utils";
import { Markdown } from "@/shared/Markdown";
import { ActionDialog } from "@/shared/ActionDialog";
import { PageHeader } from "@/shared/PageHeader";
import { useSession } from "@/session/useSession";
import { stale } from "@/shared/stale";
import { keyPath, pathKey } from "@/shell/views";
import { PriorityMark } from "./priority";
import { StatusDot } from "./status";
import { MarkdownField } from "@/shared/MarkdownField";
import { EditIssueForm } from "./IssueEditor";

type Load<T> = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; value: T };
/** The issue alone can also be gone: deleted, and restorable until a moment. */
type IssueLoad = Load<Issue> | { at: "gone"; until: string | null };
const asking = { at: "asking" } as const;

/**
 * An issue in its grace period is `404 deleted` and not simply missing
 * (`docs/api.md`): the screen that says so is the only way back to it, and
 * `restorable_until` is how long that way stays open. The extension member is
 * read by name — the generated type knows only RFC 9457's own five.
 */
function refused(problem: Problem | undefined, status: number): IssueLoad {
  if (status === 404 && codeOf(problem) === "deleted") {
    const until = (problem as { restorable_until?: string } | undefined)?.restorable_until;
    return { at: "gone", until: until ?? null };
  }

  return { at: "failed", why: describe(problem, status) };
}

/**
 * The complete issue, weighted by what a human does with it: the action bar
 * rides along in the header, what needs attention and the description stand
 * open above the fold, and the three long lists — conversation, relationships,
 * history — share one tabbed area at the bottom instead of stacking.
 */
export function IssueView() {
  const { project, number } = useParams();
  const key = pathKey(project!, number!);
  const [state, setState] = useState<{ key: string; issue: IssueLoad; history: Load<HistoryEntry[]> }>();
  const [editing, setEditing] = useState(false);
  const [deleted, setDeleted] = useState<{ until: string | null }>();
  const current = state !== undefined && state.key === key ? state : { key, issue: asking, history: asking };

  useEffect(() => {
    let live = true;
    void api.GET("/issues/{key}", { params: { path: { key } } }).then(({ data, error, response }) => live && setState((old) => ({ key, history: old !== undefined && old.key === key ? old.history : asking, issue: data ? { at: "known", value: data } : refused(error, response.status) })), () => live && setState((old) => ({ key, history: old?.history ?? asking, issue: { at: "failed", why: "The instance did not answer." } })));
    void api.GET("/issues/{key}/history", { params: { path: { key } } }).then(({ data, error, response }) => live && setState((old) => ({ key, issue: old !== undefined && old.key === key ? old.issue : asking, history: data ? { at: "known", value: data } : { at: "failed", why: describe(error, response.status) } })), () => live && setState((old) => ({ key, issue: old?.issue ?? asking, history: { at: "failed", why: "The instance did not answer." } })));
    return () => { live = false; };
  }, [key]);

  const changed = (value: Issue) => { setState((old) => ({ key, issue: { at: "known", value }, history: old?.history ?? asking })); setEditing(false); };
  const restored = (value: Issue) => { setDeleted(undefined); changed(value); };

  if (current.issue.at === "asking") return <><PageHeader title={<Skeleton className="h-4 w-64" />} /><div className="space-y-3 p-4"><Skeleton className="h-3 w-full" /><Skeleton className="h-3 w-5/6" /></div></>;
  // Deleted just now, or deleted long before this browser asked for it: the
  // same screen either way, and the deadline whenever the instance named one.
  if (deleted !== undefined) return <Gone issueKey={key} until={deleted.until} onRestored={restored} />;
  if (current.issue.at === "gone") return <Gone issueKey={key} until={current.issue.until} onRestored={restored} />;
  if (current.issue.at === "failed") return <><PageHeader title={key} /><p className="p-4 text-sm text-destructive">{current.issue.why}</p></>;
  const issue = current.issue.value;
  if (editing) return <><PageHeader title={`Edit ${issue.key}`} /><EditIssueForm issue={issue} onSaved={changed} onCancel={() => setEditing(false)} /></>;

  return <><PageHeader className="sticky top-0 z-20 bg-background" title={<span className="flex items-center gap-2"><span className="font-mono text-xs font-normal text-muted-foreground">{issue.key}</span>{issue.title}</span>}><ActionBar issue={issue} onEdit={() => setEditing(true)} onChanged={changed} onDeleted={() => setDeleted({ until: null })} /></PageHeader>
    <div className="flex flex-1 flex-col md:flex-row">
      <main className="min-w-0 flex-1 p-4 md:p-6">
        <Chips issue={issue} />
        <Attention issue={issue} onChanged={changed} />
        <Section title="Description"><Long>{issue.description}</Long></Section>
        {issue.result !== null && <Section title="Result"><Long>{issue.result}</Long></Section>}
        <Panels issue={issue} history={current.history} onChanged={changed} />
      </main>
      <Metadata issue={issue} />
    </div>
  </>;
}

/**
 * A deleted issue, and the way back. The focus lands on Restore, which is what
 * the accessibility floor asks for where the confirmed action removed the
 * control that started it.
 */
function Gone({ issueKey, until, onRestored }: { issueKey: string; until: string | null; onRestored: (issue: Issue) => void }) {
  const restore = useRef<HTMLButtonElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  useEffect(() => { restore.current?.focus(); }, []);

  async function run() {
    setBusy(true); setError(undefined);
    try {
      const result = await api.POST("/issues/{key}/restore", { params: { path: { key: issueKey } } });
      if (!result.data) throw new Error(describe(result.error, result.response.status));
      onRestored(result.data);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return <><PageHeader title={issueKey} /><div className="m-auto grid max-w-md justify-items-center gap-3 p-8 text-center">
    <p role="status">This issue is deleted and hidden from the project.</p>
    {until !== null && <p className="text-sm text-muted-foreground">It can be restored until {new Date(until).toLocaleString()} — {timeLeft(until)}.</p>}
    <Button ref={restore} disabled={busy} onClick={() => void run()}>{busy ? "Working…" : "Restore issue"}</Button>
    {error !== undefined && <p role="alert" className="text-sm text-destructive">{error}</p>}
  </div></>;
}

/**
 * The header's action bar: the one move this status makes sense of, `Edit`
 * beside it, everything else behind the overflow — including the delete, whose
 * dialog is held here rather than under the menu item that opens it, because
 * the menu closes on that click and would take its own trigger down with it.
 */
function ActionBar({ issue, onEdit, onChanged, onDeleted }: { issue: Issue; onEdit: () => void; onChanged: (issue: Issue) => void; onDeleted: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [deleting, setDeleting] = useState(false);
  const open = !["review", "done", "canceled"].includes(issue.status);

  async function run(act: () => Promise<Issue>) {
    setBusy(true); setError(undefined);
    try { onChanged(await act()); } catch (reason) { setError(reason instanceof Error ? reason.message : "The instance did not answer."); } finally { setBusy(false); }
  }

  const close = (status: "done" | "canceled") => () => run(() => issueRequest("/issues/{key}/close", issue, { status, result: issue.result }));
  const hand = () => run(() => issueRequest("/issues/{key}/review", issue, { result: issue.result }));
  // Nothing is typed here, so a stale refusal has nothing to merge — but it
  // still may not be a dead end. The screen takes the version the refusal
  // carried, says what happened, and the same press writes against that one.
  const ready = () => run(async () => {
    const result = await api.PATCH("/issues/{key}", { params: { path: { key: issue.key } }, headers: { "If-Match": issue.updated_at }, body: { ready: !issue.ready } as never });
    const current = stale<Issue>(result);
    if (current !== undefined) { onChanged(current); throw new Error(`${issue.key} changed while it was open. It is shown as it is now; set it again to write against that version.`); }
    if (!result.data) throw new Error(describe(result.error, result.response.status));
    return result.data;
  });
  // "Moving a ticket by hand still works — a ticket that has not shipped yet
  // simply does not belong" (VISION 7). The act is the release's; the answer is
  // the release, so the issue is read again.
  const shipped = issue.release !== null && issue.release !== "unreleased";
  const moveRelease = (into: boolean) => run(async () => {
    const path = { key: issue.project, name: "unreleased", issue: issue.key };
    const result = into
      ? await api.PUT("/projects/{key}/releases/{name}/issues/{issue}", { params: { path } })
      : await api.DELETE("/projects/{key}/releases/{name}/issues/{issue}", { params: { path } });
    if (!result.data) throw new Error(describe(result.error, result.response.status));
    const read = await api.GET("/issues/{key}", { params: { path: { key: issue.key } } });
    if (!read.data) throw new Error(describe(read.error, read.response.status));
    return read.data;
  });

  // One primary per status, and only one: accept what was handed in, hand in
  // what is being worked on, take what is free, reopen what is closed.
  const primary = issue.status === "review" ? { label: "Accept as done", act: close("done") }
    : ["done", "canceled"].includes(issue.status) ? { label: "Reopen", act: () => run(() => issueRequest("/issues/{key}/reopen", issue, { comment: null })) }
    : issue.claim === null ? { label: "Claim", act: () => run(() => issueRequest("/issues/{key}/claim", issue, { force: false })) }
    : { label: "Hand in for review", act: hand };

  return <>
    {error !== undefined && <span role="alert" className="text-xs text-destructive">{error}</span>}
    <Button size="sm" disabled={busy} onClick={() => void primary.act()}>{busy ? "Working…" : primary.label}</Button>
    <Button variant="outline" size="sm" onClick={onEdit}>Edit</Button>
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label="More actions" />}><MoreHorizontalIcon /></DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-48">
        {issue.claim !== null && <DropdownMenuItem onClick={() => void run(() => issueRequest("/issues/{key}/release", issue))}>Release claim</DropdownMenuItem>}
        {open && issue.claim === null && <DropdownMenuItem onClick={() => void hand()}>Hand in for review</DropdownMenuItem>}
        {open && <DropdownMenuItem onClick={() => void close("done")()}>Close as done</DropdownMenuItem>}
        {open && <DropdownMenuItem onClick={() => void close("canceled")()}>Close as canceled</DropdownMenuItem>}
        <DropdownMenuItem onClick={() => void ready()}>{issue.ready ? "Clear ready" : "Set ready"}</DropdownMenuItem>
        {!shipped && <DropdownMenuItem onClick={() => void moveRelease(issue.release === null)}>{issue.release === null ? "Put into the open release" : "Take out of the open release"}</DropdownMenuItem>}
        <DropdownMenuSeparator />
        <DropdownMenuItem variant="destructive" onClick={() => setDeleting(true)}>Delete issue</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
    <ActionDialog open={deleting} onOpenChange={setDeleting} title={`Delete ${issue.key}?`} description="The issue will be hidden from the project, but can be restored during the grace period." confirmLabel="Delete issue" onConfirm={async () => { const result = await api.DELETE("/issues/{key}", { params: { path: { key: issue.key } } }); if (!result.response.ok) throw new Error(describe(result.error, result.response.status)); onDeleted(); }} />
  </>;
}

/**
 * What the metadata column says, on a phone, where a column at the far end of
 * the page is a column nobody reads: status, priority, `ready` and the epic as
 * one line of chips under the title. The column itself starts at `md`.
 */
function Chips({ issue }: { issue: Issue }) {
  return <div className="mb-5 flex flex-wrap items-center gap-2 md:hidden" aria-label="At a glance">
    <Badge variant="outline" className="font-normal"><StatusDot status={issue.status} withLabel /></Badge>
    <Badge variant="outline" className="font-normal"><PriorityMark priority={issue.priority} withLabel /></Badge>
    <Badge variant={issue.ready ? "secondary" : "outline"} className="font-normal">{issue.ready ? "ready" : "not ready"}</Badge>
    {issue.epic && <Badge variant="outline" className="font-normal"><Link className="text-brand hover:underline" to={keyPath(issue.epic.key)}>{issue.epic.key}</Link></Badge>}
  </div>;
}

function Attention({ issue, onChanged }: { issue: Issue; onChanged: (issue: Issue) => void }) {
  return <div className="mb-6 space-y-3" aria-label="Needs attention">
    {issue.questions.filter((q) => q.answer === null).map((q) => <aside key={q.id} className="rounded-lg border border-brand bg-accent p-4"><Eyebrow>Answer needed</Eyebrow><Markdown className="mt-2">{q.question}</Markdown><Byline name={q.asked_by.name} at={q.asked_at} /><TextAction label="Answer" onRun={async (text) => { const result = await api.POST("/questions/{id}/answer", { params: { path: { id: q.id } }, body: { answer: text } }); if (!result.data) throw new Error(describe(result.error, result.response.status)); return { ...issue, questions: issue.questions.map((x) => x.id === q.id ? result.data! : x), open_questions: issue.open_questions - 1 }; }} onChanged={onChanged} /></aside>)}
    {/* Accepting is the header's primary in this status, so this box carries
        the result and the two decisions that are not it. */}
    {issue.status === "review" && <aside className="rounded-lg border border-brand bg-accent p-4"><Eyebrow>Review needed</Eyebrow><p className="mt-1 text-sm">Decide whether this work is done, canceled, or should return to todo.</p>{issue.result !== null && <Markdown className="mt-3">{issue.result}</Markdown>}<div className="mt-3 flex flex-wrap gap-2"><IssueAction label="Accept as canceled" variant="outline" path="/issues/{key}/close" issue={issue} body={{ status: "canceled", result: issue.result }} onChanged={onChanged} /></div><TextAction label="Return to todo" placeholder="What needs to change?" onRun={(comment) => issueRequest("/issues/{key}/reopen", issue, { comment })} onChanged={onChanged} /></aside>}
    {issue.open_blockers > 0 && <aside className="rounded-lg border bg-muted p-4"><Eyebrow>Blocked</Eyebrow><p className="mt-1 text-sm">Waiting for:</p><IssueLinks links={issue.blocked_by.filter((x) => x.open)} /></aside>}
    {issue.claim !== null && <aside className="rounded-lg border bg-muted p-4"><Eyebrow>In progress</Eyebrow><p className="mt-1 text-sm"><strong>{issue.claim.holder.name}</strong> claimed this {relativeTime(issue.claim.since)}.</p></aside>}
  </div>;
}

/**
 * The three lists nobody reads at the same time, in one place: the
 * conversation is what an open issue is usually opened for, so it is the one
 * that stands open. Each tab carries its count, so the tab nobody chose still
 * says whether there is anything behind it.
 */
function Panels({ issue, history, onChanged }: { issue: Issue; history: Load<HistoryEntry[]>; onChanged: (issue: Issue) => void }) {
  const relations = (issue.parent ? 1 : 0) + issue.sub_issues.length + issue.blocked_by.length + issue.blocks.length;
  return <Tabs defaultValue="conversation" className="border-t pt-5">
    <TabsList>
      <TabsTab value="conversation">Conversation <Count of={issue.comments.length + issue.questions.length} /></TabsTab>
      <TabsTab value="relationships">Relationships <Count of={relations} /></TabsTab>
      <TabsTab value="history">History <Count of={history.at === "known" ? history.value.length : undefined} /></TabsTab>
    </TabsList>
    <TabsPanel value="conversation"><Conversation issue={issue} onChanged={onChanged} /></TabsPanel>
    <TabsPanel value="relationships"><Relationships issue={issue} onChanged={onChanged} /></TabsPanel>
    <TabsPanel value="history"><History loaded={history} /></TabsPanel>
  </Tabs>;
}

function Count({ of }: { of: number | undefined }) { return of === undefined ? null : <span className="rounded-full bg-muted px-1.5 text-xs tabular-nums">{of}</span>; }

function Relationships({ issue, onChanged }: { issue: Issue; onChanged: (issue: Issue) => void }) {
  const rows: Array<[string, Array<{ key: string | null; title?: string | null; open: boolean }>]> = [];
  if (issue.parent) rows.push(["Parent", [{ ...issue.parent, open: true }]]);
  if (issue.sub_issues.length) rows.push(["Sub-issues", issue.sub_issues.map((x) => ({ ...x, open: true }))]);
  if (issue.blocked_by.length) rows.push(["Blocked by", issue.blocked_by]);
  if (issue.blocks.length) rows.push(["Blocks", issue.blocks]);
  return <div className="space-y-5 text-sm">
    {rows.length === 0 ? <p className="text-muted-foreground">This issue stands on its own — no parent, no sub-issues, nothing blocking or blocked.</p> : rows.map(([name, links]) => <div key={name}><h3 className="font-medium">{name}</h3><IssueLinks links={links} /></div>)}
    <EdgeAction issue={issue} onChanged={onChanged} />
  </div>;
}

function IssueLinks({ links }: { links: Array<{ key: string | null; title?: string | null; open: boolean }> }) {
  return <ul className="mt-2 space-y-1">{links.map((x, i) => <li key={x.key ?? i}>{x.key === null ? <span className="text-muted-foreground">Issue outside your project access</span> : <Link className="text-brand hover:underline" to={keyPath(x.key)}><span className="font-mono text-xs">{x.key}</span>{x.title ? ` · ${x.title}` : ""}</Link>}{!x.open && <span className="text-muted-foreground"> · closed</span>}</li>)}</ul>;
}

/**
 * The conversation, and the two ways to add to it. The fields open on a
 * button: a comment box that stands open on every issue invites the comment
 * nobody needed, and it sat under the actions rather than under the thread it
 * belongs to.
 */
function Conversation({ issue, onChanged }: { issue: Issue; onChanged: (issue: Issue) => void }) {
  const [writing, setWriting] = useState<"comment" | "question">();
  const entries = [...issue.questions.map((value) => ({ kind: "question" as const, at: value.asked_at, value })), ...issue.comments.map((value) => ({ kind: "comment" as const, at: value.created_at, value }))].sort((a, b) => a.at.localeCompare(b.at));
  const added = (issue: Issue) => { setWriting(undefined); onChanged(issue); };
  return <div className="space-y-5">
    {entries.length === 0 ? <p className="text-sm text-muted-foreground">Nothing has been said on this issue yet.</p> : entries.map((entry) => entry.kind === "comment" ? <CommentEntry key={entry.value.id} issue={issue} comment={entry.value} onChanged={onChanged} /> : <article key={entry.value.id}><Eyebrow>{entry.value.answer === null ? "Open question" : "Question"}</Eyebrow><Markdown className="mt-1">{entry.value.question}</Markdown><Byline name={entry.value.asked_by.name} at={entry.value.asked_at} />{entry.value.answer !== null && <div className="mt-3 border-l-2 pl-3"><Markdown>{entry.value.answer}</Markdown><Byline name={entry.value.answered_by?.name ?? "Unknown"} at={entry.value.answered_at!} /></div>}</article>)}
    {writing === undefined && <div className="flex flex-wrap gap-2"><Button variant="outline" size="sm" onClick={() => setWriting("comment")}>Add comment</Button><Button variant="outline" size="sm" onClick={() => setWriting("question")}>Ask question</Button></div>}
    {writing === "comment" && <TextAction label="Add comment" multiline onCancel={() => setWriting(undefined)} onRun={async (body) => { const result = await api.POST("/issues/{key}/comments", { params: { path: { key: issue.key } }, body: { body } }); if (!result.data) throw new Error(describe(result.error, result.response.status)); return { ...issue, comments: [...issue.comments, result.data] }; }} onChanged={added} />}
    {writing === "question" && <TextAction label="Ask question" multiline onCancel={() => setWriting(undefined)} onRun={async (question) => { const result = await api.POST("/issues/{key}/questions", { params: { path: { key: issue.key } }, body: { question } }); if (!result.data) throw new Error(describe(result.error, result.response.status)); return { ...issue, questions: [...issue.questions, result.data], open_questions: issue.open_questions + 1 }; }} onChanged={added} />}
  </div>;
}

/**
 * One comment, with the two things its author can do to it (ADR 0022). The
 * acts sit in the same overflow menu the issue header uses rather than as two
 * text links under every paragraph: a conversation is read, and a row of verbs
 * under each entry is read too.
 *
 * Shown only where they are allowed, because a menu that offers what the
 * instance refuses teaches the reader nothing. Hiding is not the check — the
 * instance makes it, and it is `forbidden` there.
 */
function CommentEntry({ issue, comment, onChanged }: { issue: Issue; comment: Issue["comments"][number]; onChanged: (issue: Issue) => void }) {
  const { me } = useSession();
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const mine = comment.author.id === me.id;
  // The author rewrites; the author or any user clears up, on anybody's.
  const removable = mine || me.kind === "user";
  const without = (issue: Issue) => ({ ...issue, comments: issue.comments.filter((x) => x.id !== comment.id) });

  return <article>
    <div className="flex items-start justify-between gap-2">
      <Byline name={comment.author.name} at={comment.created_at} edited={comment.edited_at} />
      {(mine || removable) && <DropdownMenu>
        <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label={`Actions on the comment by ${comment.author.name}`} />}><MoreHorizontalIcon /></DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {mine && <DropdownMenuItem onClick={() => setEditing(true)}>Edit comment</DropdownMenuItem>}
          {removable && <DropdownMenuItem variant="destructive" onClick={() => setDeleting(true)}>Delete comment</DropdownMenuItem>}
        </DropdownMenuContent>
      </DropdownMenu>}
    </div>
    {editing
      ? <TextAction label="Save comment" multiline initial={comment.body} onCancel={() => setEditing(false)} onRun={async (body) => {
          const result = await api.PATCH("/comments/{id}", { params: { path: { id: comment.id } }, body: { body } });
          if (!result.data) throw new Error(describe(result.error, result.response.status));
          return { ...issue, comments: issue.comments.map((x) => x.id === comment.id ? result.data! : x) };
        }} onChanged={(next) => { setEditing(false); onChanged(next); }} />
      : <Markdown className="mt-1">{comment.body}</Markdown>}
    <ActionDialog open={deleting} onOpenChange={setDeleting} title="Delete this comment?" description="It is gone for good — there is no grace period for a comment. The history keeps that it was taken away." confirmLabel="Delete comment" onConfirm={async () => {
      const result = await api.DELETE("/comments/{id}", { params: { path: { id: comment.id } } });
      if (!result.response.ok) throw new Error(describe(result.error, result.response.status));
      onChanged(without(issue));
    }} />
  </article>;
}

function EdgeAction({ issue, onChanged }: { issue: Issue; onChanged: (issue: Issue) => void }) {
  const [key, setKey] = useState(""); const [error, setError] = useState<string>();
  const id = useId();
  async function add() { const value = key.trim(); if (!value) return; const result = await api.POST("/issues/{key}/blocked-by/{blockerKey}", { params: { path: { key: issue.key, blockerKey: value } } }); if (!result.response.ok) { setError(describe(result.error, result.response.status)); return; } const read = await api.GET("/issues/{key}", { params: { path: { key: issue.key } } }); if (read.data) { setKey(""); onChanged(read.data); } }
  return <div className="grid gap-2 border-t pt-5"><label htmlFor={id} className="text-sm font-medium">Add blocker</label><div className="flex max-w-sm gap-2"><Input id={id} aria-label="Blocker issue key" placeholder="PLAN-42" value={key} onChange={(e) => setKey(e.target.value)} /><Button variant="outline" onClick={() => void add()}>Add</Button></div>{error && <p className="text-sm text-destructive">{error}</p>}<div className="flex flex-wrap gap-2">{issue.blocked_by.filter((x) => x.key !== null).map((x) => <Button key={x.key} size="xs" variant="ghost" onClick={async () => { const result = await api.DELETE("/issues/{key}/blocked-by/{blockerKey}", { params: { path: { key: issue.key, blockerKey: x.key! } } }); if (result.response.ok) onChanged({ ...issue, blocked_by: issue.blocked_by.filter((edge) => edge !== x), open_blockers: issue.open_blockers - Number(x.open) }); }}>Remove {x.key}</Button>)}</div></div>;
}

type ActPath = "/issues/{key}/claim" | "/issues/{key}/release" | "/issues/{key}/close" | "/issues/{key}/review" | "/issues/{key}/reopen" | "/issues/{key}/restore";
async function issueRequest(path: ActPath, issue: Issue, body?: object): Promise<Issue> {
  const result = await api.POST(path as "/issues/{key}/claim", { params: { path: { key: issue.key } }, body: body as never });
  if (!result.data) throw new Error(describe(result.error, result.response.status));
  return result.data;
}

function IssueAction({ label, path, issue, body, onChanged, variant = "default" }: { label: string; path: ActPath; issue: Issue; body?: object; onChanged: (issue: Issue) => void; variant?: "default" | "outline" }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  async function run() { setBusy(true); setError(undefined); try { onChanged(await issueRequest(path, issue, body)); } catch (reason) { setError(reason instanceof Error ? reason.message : "The instance did not answer."); } finally { setBusy(false); } }
  return <span><Button variant={variant} disabled={busy} onClick={() => void run()}>{busy ? "Working…" : label}</Button>{error && <span role="alert" className="ml-2 text-xs text-destructive">{error}</span>}</span>;
}

function TextAction({ label, placeholder, multiline, initial, onRun, onChanged, onCancel }: { label: string; placeholder?: string; multiline?: boolean; initial?: string; onRun: (text: string) => Promise<Issue>; onChanged: (issue: Issue) => void; onCancel?: () => void }) {
  // A correction opens in the text it is correcting, in the same field it was
  // written in — not in an empty box that makes the author type it again.
  const [text, setText] = useState(initial ?? ""); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const id = useId();
  async function run() { if (!text.trim()) return; setBusy(true); setError(undefined); try { onChanged(await onRun(text)); setText(""); } catch (reason) { setError(reason instanceof Error ? reason.message : "The instance did not answer."); } finally { setBusy(false); } }
  return <div className="mt-3 grid max-w-xl gap-2">{multiline ? <MarkdownField label={label} value={text} onChange={setText} size="compact" hint={placeholder} onSubmit={() => void run()} /> : <label className="grid gap-1 text-sm font-medium">{label}<Input id={id} placeholder={placeholder} value={text} onChange={(e) => setText(e.target.value)} /></label>}<div className="flex gap-2"><Button size="sm" disabled={busy || !text.trim()} onClick={() => void run()}>{busy ? "Saving…" : label}</Button>{onCancel && <Button size="sm" variant="ghost" disabled={busy} onClick={onCancel}>Cancel</Button>}</div>{error && <p role="alert" className="text-sm text-destructive">{error}</p>}</div>;
}

function History({ loaded }: { loaded: Load<HistoryEntry[]> }) {
  return loaded.at === "asking" ? <Skeleton className="h-12 w-full" /> : loaded.at === "failed" ? <p className="text-sm text-destructive">{loaded.why}</p> : <ol className="space-y-3 text-sm">{loaded.value.map((x) => <li key={x.id}><span className="font-medium">{x.actor.name}</span> {historyText(x)}<Byline at={x.at} /></li>)}</ol>;
}

function historyText(x: HistoryEntry) {
  if (x.field === "created") return "created the issue";
  // A comment entry names the fact and which comment, never the text (ADR
  // 0022): a history that carried what somebody withdrew would keep the one
  // thing the withdrawal was for.
  if (x.field === "comment") return x.note === "withdrawn" ? "took a comment away" : "edited a comment";
  const from = value(x.old_value), to = value(x.new_value);
  if (from === null) return <>set <strong>{x.field}</strong> to {to}</>;
  if (to === null) return <>cleared <strong>{x.field}</strong> from {from}</>;
  return <>changed <strong>{x.field}</strong> from {from} to {to}</>;
}
function value(x: unknown) { if (x == null) return null; if (typeof x === "object" && "name" in x && typeof x.name === "string") return x.name; return String(x); }

function Metadata({ issue }: { issue: Issue }) {
  return <aside className="shrink-0 space-y-3 border-t p-4 text-sm md:w-64 md:border-t-0 md:border-l"><Field name="Status" className="max-md:hidden"><StatusDot status={issue.status} withLabel /></Field><Field name="Priority" className="max-md:hidden"><PriorityMark priority={issue.priority} withLabel /></Field><Field name="Ready" className="max-md:hidden">{issue.ready ? "yes" : "no"}</Field>{issue.epic && <Field name="Epic" className="max-md:hidden"><Link to={keyPath(issue.epic.key)} className="text-brand hover:underline">{issue.epic.key}</Link> <span className="text-muted-foreground">{issue.epic.title}</span></Field>}{issue.claim && <Field name="Claimed by">{issue.claim.holder.name}<span className="text-muted-foreground">{issue.claim.expires_at === null ? " · does not expire" : ` · until ${date(issue.claim.expires_at)}`}</span></Field>}{issue.assignee && <Field name="Assignee">{issue.assignee.name}</Field>}{issue.labels.length > 0 && <Field name="Labels"><span className="flex flex-wrap gap-1">{issue.labels.map((x) => <Badge key={x.name} variant="secondary" className="font-normal">{x.name}</Badge>)}</span></Field>}<Field name="Author">{issue.author.name}</Field><Field name="Created">{date(issue.created_at)}</Field><Field name="Updated">{date(issue.updated_at)}</Field><Field name="Release">{issue.release === null ? <span className="text-muted-foreground">not in a release</span> : issue.release}</Field></aside>;
}

/**
 * A body that is allowed to be long. Nothing is folded away — a description is
 * hidden exactly when it has something to say — but a very long one is cut off
 * at a readable height behind a fade until somebody asks for the rest.
 */
function Long({ children }: { children: string }) {
  const [whole, setWhole] = useState(false);
  const long = isLong(children);
  return <>
    <div className={cn("relative", long && !whole && "max-h-72 overflow-hidden")}>
      <Markdown>{children}</Markdown>
      {long && !whole && <div aria-hidden className="pointer-events-none absolute inset-x-0 bottom-0 h-16 bg-linear-to-b from-transparent to-background" />}
    </div>
    {long && <button type="button" className="mt-2 text-sm font-medium text-brand hover:underline" onClick={() => setWhole((x) => !x)}>{whole ? "Show less" : "Show more"}</button>}
  </>;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) { return <section className="border-t py-5 first:border-t-0 first:pt-0"><h2 className="mb-3 text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>{children}</section>; }
function Field({ name, className, children }: { name: string; className?: string; children: React.ReactNode }) { return <div className={className}><div className="text-[11px] font-medium tracking-wide text-muted-foreground uppercase">{name}</div><div className="mt-0.5">{children}</div></div>; }
function Eyebrow({ children }: { children: React.ReactNode }) { return <h2 className="text-xs font-semibold tracking-wide uppercase">{children}</h2>; }
function Byline({ name, at, edited }: { name?: string; at: string; edited?: string | null }) { return <p className="mt-1 text-xs text-muted-foreground">{name && <>{name} · </>}<time dateTime={at}>{date(at)}</time>{edited != null && <> · <span title={date(edited)}>edited</span></>}</p>; }
function date(x: string) { return new Date(x).toLocaleString(); }
function relativeTime(x: string) { const hours = Math.max(0, Math.floor((Date.now() - new Date(x).getTime()) / 3_600_000)); return hours < 24 ? `${hours} hour${hours === 1 ? "" : "s"} ago` : `${Math.floor(hours / 24)} days ago`; }
/** How much of the grace period is left, in the words the deadline is read in. */
function timeLeft(x: string) { const hours = Math.floor((new Date(x).getTime() - Date.now()) / 3_600_000); if (hours < 1) return "less than an hour left"; return hours < 48 ? `${hours} hour${hours === 1 ? "" : "s"} left` : `${Math.floor(hours / 24)} days left`; }
function isLong(x: string) { return x.length > 600 || x.split("\n").length > 8; }
