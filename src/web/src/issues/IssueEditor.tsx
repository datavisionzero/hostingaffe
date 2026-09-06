import { useId, useState, type ReactNode } from "react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { api, codeOf, describe, type Issue, type Problem, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { LabelPicker } from "@/components/ui/label-picker";
import { useEpics } from "@/epics/useEpics";
import { useLabels } from "@/projects/useLabels";
import { MarkdownField } from "@/shared/MarkdownField";
import { useAbandon } from "@/shared/abandon";
import { PageHeader } from "@/shared/PageHeader";
import { stale } from "@/shared/stale";
import { keyPath } from "@/shell/views";
import { AssigneePicker, EpicPicker, IssuePicker } from "./pickers";
import { priorityLabel } from "./priorityLabel";
import { statusLabel } from "./statusLabel";

type NewIssue = Schemas["NewIssue"];

export function NewIssueView() {
  const { project } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  // The epic screen leads here with its own key in the address, so the bracket
  // an issue is started under is not typed a second time.
  const [search] = useSearchParams();
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<Refusal>();

  async function save(draft: IssueDraft) {
    setSaving(true); setRefused(undefined);
    try {
      const item: NewIssue = { ref: null, title: draft.title, description: draft.description, priority: draft.priority, ready: draft.ready, labels: draft.labels, epic: blank(draft.epic), parent: blank(draft.parent), assignee: blank(draft.assignee), blocked_by: draft.blockedBy, blocks: [], status: draft.status };
      const { data, error: problem, response } = await api.POST("/issues", { body: { project: project!, issues: [item] } });
      if (!data) { setRefused(refusal(problem, response.status)); return; }
      void navigate(keyPath(data.items[0].key), { replace: true });
    } catch { setRefused({ fields: {} , why: "The instance did not answer." }); } finally { setSaving(false); }
  }

  // Back where the form was opened from — the list `c` was pressed on, or the
  // epic whose key came along in the address — rather than at the issue list
  // whatever the way in was. A form opened by its own link has nothing behind
  // it, and falls back to the epic it names, or to the list.
  function cancel() {
    if (location.key !== "default") { void navigate(-1); return; }
    const epic = search.get("epic");
    void navigate(epic === null ? `/${project}/issues` : keyPath(epic));
  }

  return <><PageHeader title="Create issue" /><IssueForm epic={search.get("epic") ?? undefined} submit="Create issue" saving={saving} refused={refused} onSubmit={save} onCancel={cancel} /></>;
}

export function EditIssueForm({ issue, onSaved, onCancel }: { issue: Issue; onSaved: (issue: Issue) => void; onCancel: () => void }) {
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<Refusal>();
  // The version the next write is guarded with, and the issue a refusal handed
  // back. Keeping the version the form opened with is what left a person stuck:
  // every further save carried it and was refused for the same reason, and the
  // only way out was to throw away what had been typed.
  const [version, setVersion] = useState(issue.updated_at);
  const [conflict, setConflict] = useState<Issue>();
  async function save(draft: IssueDraft) {
    setSaving(true); setRefused(undefined); setConflict(undefined);
    const body = { title: draft.title, description: draft.description, priority: draft.priority, ready: draft.ready, labels: draft.labels, epic: blank(draft.epic), parent: blank(draft.parent), assignee: blank(draft.assignee), ...(parkable(issue) && draft.status !== issue.status ? { status: draft.status } : {}) };
    try {
      const answer = await api.PATCH("/issues/{key}", { params: { path: { key: issue.key } }, headers: { "If-Match": version }, body: body as never });
      const current = stale<Issue>(answer);
      if (current !== undefined) { setConflict(current); setVersion(current.updated_at); return; }
      if (!answer.data) { setRefused(refusal(answer.error, answer.response.status)); return; }
      onSaved(answer.data);
    } catch { setRefused({ fields: {}, why: "The instance did not answer." }); } finally { setSaving(false); }
  }
  return <IssueForm initial={issue} submit="Save changes" saving={saving} refused={refused} notice={conflict === undefined ? undefined : <Conflict opened={issue} current={conflict} />} onSubmit={save} onCancel={onCancel} />;
}

/** The fields of the issue a person edits here, in the order the form has them. */
const edited: Array<{ name: string; of: (issue: Issue) => string }> = [
  { name: "priority", of: (issue) => priorityLabel(issue.priority) },
  { name: "status", of: (issue) => issue.status },
  { name: "ready", of: (issue) => (issue.ready ? "yes" : "no") },
  { name: "labels", of: (issue) => issue.labels.map((label) => label.name).join(", ") },
  { name: "epic", of: (issue) => issue.epic?.key ?? "" },
  { name: "parent issue", of: (issue) => issue.parent?.key ?? "" },
  { name: "assignee", of: (issue) => issue.assignee?.name ?? "" },
];

/**
 * What a stale refusal means, in the words it means it. The typed text is
 * still in the fields above; this says what the other version holds, so it can
 * be merged by hand, and that the next save is now an overwrite of it.
 *
 * An issue is not an epic: beside the two fields somebody types into it
 * carries seven that are chosen from a list, and saving writes all of them.
 * The text is shown to be merged from, and the rest is named — not shown,
 * because a person about to overwrite a label somebody else set needs to know
 * that, and does not need a second copy of the form to read it in.
 */
function Conflict({ opened, current }: { opened: Issue; current: Issue }) {
  const also = edited.filter((field) => field.of(opened) !== field.of(current)).map((field) => field.name);

  return (
    <div role="alert" className="grid gap-2 rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
      <p>
        <span className="font-medium">{current.key} was changed while you were editing it.</span>{" "}
        Your text is kept. Saving now writes it over the version below.
      </p>
      {also.length > 0 && <p className="text-xs text-muted-foreground">Changed there as well, and overwritten too: {also.join(", ")}.</p>}
      <details className="text-xs">
        <summary className="cursor-pointer text-muted-foreground">The issue as it stands, saved {new Date(current.updated_at).toLocaleString()}</summary>
        <p className="mt-2 font-medium">{current.title}</p>
        <pre className="mt-1 max-h-64 overflow-auto rounded-md bg-muted p-2 font-mono whitespace-pre-wrap">{current.description}</pre>
      </details>
    </div>
  );
}

/**
 * Whether the editor may move the status at all. Parking is the one status
 * move that is a field write, `todo` to `backlog` and back on an open,
 * unclaimed issue (ADR 0016); every other move is an act of its own. Offering
 * the choice anywhere else sent a change the instance refuses, and the refusal
 * took the title and the description edited beside it with it.
 */
function parkable(issue: Issue | undefined): boolean {
  return issue === undefined || ((issue.status === "todo" || issue.status === "backlog") && issue.claim === null);
}

type IssueDraft = { title: string; description: string; priority: number; ready: boolean; labels: string[]; epic: string; parent: string; assignee: string; blockedBy: string[]; status: "backlog" | "todo" };

/**
 * A refused save, taken apart: what belongs at a field, and what is left for
 * the form. A refusal over the whole form for a key that was mistyped is what
 * the pickers exist to end, so a refusal that names a field is shown there.
 */
type Refusal = { why?: string; fields: Record<string, string> };

const atField = ["epic", "parent", "assignee", "blocked_by", "labels"];

/** Which refusals name a field, and which field that is. */
const codeField: Record<string, string> = {
  "one-level": "parent",
  "other-project": "parent",
  "epic-inherited": "epic",
  cycle: "blocked_by",
  "unknown-label": "labels",
};

function refusal(problem: Problem | undefined, status: number): Refusal {
  // `validation` carries `errors`, field to message (`docs/api.md`, Errors);
  // it is an extension member, so the generated type does not know it.
  const errors = (problem as { errors?: Record<string, string> } | undefined)?.errors ?? {};
  const named = codeField[codeOf(problem) ?? ""];
  const all = named === undefined ? errors : { ...errors, [named]: describe(problem, status) };
  const fields = Object.fromEntries(Object.entries(all).filter(([field]) => atField.includes(field)));
  const rest = Object.keys(all).filter((field) => !atField.includes(field));

  return { fields, why: rest.length > 0 || Object.keys(fields).length === 0 ? describe(problem, status) : undefined };
}

/** The form as it starts out, and what "changed" is measured against. */
function startingDraft(initial: Issue | undefined, epic: string | undefined): IssueDraft {
  return { title: initial?.title ?? "", description: initial?.description ?? "", priority: initial?.priority ?? 2, ready: initial?.ready ?? false, labels: initial?.labels.map((x) => x.name) ?? [], epic: initial?.epic?.key ?? epic ?? "", parent: initial?.parent?.key ?? "", assignee: initial?.assignee?.name ?? "", blockedBy: initial?.blocked_by.flatMap((x) => x.key ?? []) ?? [], status: initial?.status === "backlog" ? "backlog" : "todo" };
}

function IssueForm({ initial, epic, submit, saving, refused, notice, onSubmit, onCancel }: { initial?: Issue; epic?: string; submit: string; saving: boolean; refused?: Refusal; notice?: ReactNode; onSubmit: (draft: IssueDraft) => void; onCancel: () => void }) {
  const [start] = useState(() => startingDraft(initial, epic));
  const [draft, setDraft] = useState<IssueDraft>(start);
  const { leave, dialog } = useAbandon(JSON.stringify(draft) !== JSON.stringify(start), onCancel);
  const titleId = useId();
  const readyId = useId();
  const set = <K extends keyof IssueDraft>(key: K, value: IssueDraft[K]) => setDraft((old) => ({ ...old, [key]: value }));
  const { project } = useParams();
  const { labels, create } = useLabels(project);
  const epics = useEpics(project);
  const at = refused?.fields ?? {};

  // Attaching an issue to a closed epic reopens the epic, silently as far as
  // the HTTP call goes. The choice says which epics are closed on their own
  // rows; this is the same fact once one of them is chosen, said as what
  // saving will do.
  const chosenEpic = epics.find((known) => known.key === draft.epic);
  const reopens = chosenEpic?.status === "closed" && chosenEpic.key !== initial?.epic?.key
    ? `${chosenEpic.key} is closed. Saving attaches this issue and reopens the epic.`
    : undefined;

  // Two columns where there is room: what is written stays wide, and the eight
  // controls around it stand beside it rather than crowding in above and below
  // (`docs/human-interface.md`). One column on a phone, in the order below.
  return <form className="mx-auto grid w-full max-w-6xl gap-4 p-4 md:p-6 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start lg:gap-6" onSubmit={(event) => { event.preventDefault(); void onSubmit(draft); }}>
    <div className="grid content-start gap-4">
    <label className="grid gap-1 text-sm font-medium">Title<Input id={titleId} required autoFocus value={draft.title} onChange={(e) => set("title", e.target.value)} /></label>
    <MarkdownField label="Description" value={draft.description} onChange={(value) => set("description", value)} hint="What has to be true when this is done?" />
    </div>
    <div className="grid content-start gap-4">
    <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-1"><Select label="Priority" value={draft.priority} onChange={(value) => set("priority", Number(value))}>{[0,1,2,3,4].map((x) => <option key={x} value={x}>{priorityLabel(x)}</option>)}</Select>{parkable(initial) ? <Select label="Status" value={draft.status} onChange={(value) => set("status", value as "backlog" | "todo")}><option value="todo">Todo</option><option value="backlog">Backlog</option></Select> : <Select label="Status" hint="Changed through the issue's own actions" value={initial!.status} disabled onChange={() => undefined}><option value={initial!.status}>{statusLabel(initial!.status)}</option></Select>}<label className="flex items-end gap-2 pb-2 text-sm"><input id={readyId} type="checkbox" checked={draft.ready} onChange={(e) => set("ready", e.target.checked)} /> Ready</label></div>
    <LabelPicker label="Labels" labels={labels} value={draft.labels} onChange={(names) => set("labels", names)} onCreate={create} error={at.labels} />
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-1"><EpicPicker epics={epics} value={draft.epic} onChange={(key) => set("epic", key)} error={at.epic} /><IssuePicker label="Parent issue" project={project} exclude={initial ? [initial.key] : []} value={draft.parent === "" ? [] : [draft.parent]} onChange={(keys) => set("parent", keys[0] ?? "")} error={at.parent} /><AssigneePicker project={project} value={draft.assignee} onChange={(name) => set("assignee", name)} error={at.assignee} />{!initial && <IssuePicker label="Blocked by" project={project} multiple value={draft.blockedBy} onChange={(keys) => set("blockedBy", keys)} error={at.blocked_by} />}</div>
    {reopens && <p role="status" className="text-sm text-brand">{reopens}</p>}
    {/* A conflict says everything the refusal's own sentence says, and says
        what to do about it, so it stands in its place rather than beside it. */}
    {notice ?? (refused?.why && <p role="alert" className="text-sm text-destructive">{refused.why}</p>)}
    <div className="flex justify-end gap-2"><Button type="button" variant="outline" onClick={leave}>Cancel</Button><Button type="submit" disabled={saving}>{saving ? "Saving…" : submit}</Button></div>
    </div>
    {dialog}
  </form>;
}

function Select({ label, hint, value, disabled, onChange, children }: { label: string; hint?: string; value: string | number; disabled?: boolean; onChange: (value: string) => void; children: React.ReactNode }) {
  const id = useId();
  return <label className="grid gap-1 text-sm font-medium">{label}{hint && <span className="text-xs font-normal text-muted-foreground">{hint}</span>}<select id={id} className="h-8 rounded-lg border bg-background px-2 disabled:text-muted-foreground" value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)}>{children}</select></label>;
}
function blank(value: string) { return value.trim() || null; }
