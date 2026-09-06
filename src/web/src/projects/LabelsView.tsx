import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Link, useParams } from "react-router";
import { api, describe, type Problem, type Schemas } from "@/api/client";
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
import { ActionDialog, TextActionDialog } from "@/shared/ActionDialog";
import { PageHeader } from "@/shared/PageHeader";
import { keyPath } from "@/shell/views";
import { GroupPicker } from "./GroupPicker";
import { forgetLabels } from "./useLabels";

type Label = Schemas["Label"];
type Loaded = { at: "asking" } | { at: "failed"; why: string } | { at: "known"; labels: Label[] };

/** What a label is, as the form holds it. */
type Draft = { name: string; group: string; description: string };

/** A refused write, taken apart: what belongs at a field, and what is left over. */
type Refused = { why?: string; fields: Partial<Record<keyof Draft, ReactNode>> };

/** The labels of the project, grouped where they exclude one another. */
export function LabelsView() {
  const { project } = useParams();
  const [known, setKnown] = useState<{ of: string | undefined; loaded: Loaded } | null>(null);
  const [recentlyDeleted, setRecentlyDeleted] = useState<{ of: string | undefined; label: Label }>();
  const [restoreProblem, setRestoreProblem] = useState<{ of: string | undefined; why: string }>();
  const loaded: Loaded = known !== null && known.of === project ? known.loaded : { at: "asking" };
  const deleted = recentlyDeleted !== undefined && recentlyDeleted.of === project ? recentlyDeleted.label : undefined;
  const restoreError = restoreProblem !== undefined && restoreProblem.of === project ? restoreProblem.why : "";

  // Never throws. Every caller runs it after a write that already succeeded,
  // and a reload that cannot reach the instance would otherwise be reported as
  // the write having failed.
  async function reload() {
    // Every caller reloads after a write, which is also the moment the set the
    // pickers share stopped being what the project has.
    forgetLabels(project);
    try {
      const { data, error, response } = await api.GET("/projects/{key}/labels", { params: { path: { key: project! } } });
      setKnown({ of: project, loaded: data ? { at: "known", labels: data } : { at: "failed", why: describe(error, response.status) } });
    } catch {
      setKnown({ of: project, loaded: { at: "failed", why: "The instance did not answer." } });
    }
  }

  async function create(draft: Draft): Promise<Refused | undefined> {
    const { data, error, response } = await api.POST("/projects/{key}/labels", {
      params: { path: { key: project! } },
      body: { name: draft.name, group: draft.group || null, description: draft.description || null },
    });

    if (data === undefined) return refusalOf(error, response.status);

    await reload();
    return undefined;
  }

  async function change(label: Label, draft: Draft): Promise<Refused | undefined> {
    const { data, error, response } = await api.PATCH("/projects/{key}/labels/{name}", {
      params: { path: { key: project!, name: label.name } },
      body: { name: draft.name, group: draft.group || null, description: draft.description || null },
    });

    if (data === undefined) return refusalOf(error, response.status);

    await reload();
    return undefined;
  }

  /**
   * Renaming a group and dissolving one are the same move: every label in it
   * is written again with the group it should have from now on. The group is a
   * string on the label and not an entity of its own, so this is a row of
   * writes rather than one — which is why it says how far it got when one of
   * them is refused.
   */
  async function moveGroup(from: string, to: string) {
    const members = loaded.at === "known" ? loaded.labels.filter((label) => (label.group ?? "") === from) : [];
    const refused: string[] = [];

    for (const label of members) {
      const { data, error, response } = await api.PATCH("/projects/{key}/labels/{name}", {
        params: { path: { key: project!, name: label.name } },
        body: { name: null, group: to || null, description: label.description ?? null },
      });

      if (data === undefined) refused.push(`${label.name}: ${describe(error, response.status)}`);
    }

    await reload();

    if (refused.length > 0) {
      throw new Error(`${members.length - refused.length} of ${members.length} labels moved. ${refused.join(" ")}`);
    }
  }

  useEffect(() => {
    let current = true;
    const setLoaded = (loaded: Loaded) => setKnown({ of: project, loaded });

    void (async () => {
      try {
        const { data, error, response } = await api.GET("/projects/{key}/labels", {
          params: { path: { key: project! } },
        });

        if (current) {
          setLoaded(
            data === undefined ? { at: "failed", why: describe(error, response.status) } : { at: "known", labels: data },
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

  const restoreRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    restoreRef.current?.focus();
  }, [deleted?.name]);

  const groups =
    loaded.at === "known"
      ? [...new Set(loaded.labels.map((label) => label.group ?? ""))].sort((a, b) =>
          a === "" ? 1 : b === "" ? -1 : a.localeCompare(b),
        )
      : [];
  const named = groups.filter((group) => group !== "");

  return (
    <>
      <PageHeader title="Labels" meta={loaded.at === "known" ? `${loaded.labels.length}` : undefined} />
      {loaded.at === "failed" && <p className="p-4 text-sm text-destructive">{loaded.why}</p>}
      {/* Outside the loaded list on purpose. A reload that fails after the
          delete succeeded must not take the way back with it: the label is
          gone either way, and the grace period is the only thing that offers
          it again. */}
      {(deleted !== undefined || restoreError !== "") && (
        <div className="space-y-2 px-4 pt-4">
          {deleted && <div className="flex items-center gap-3 rounded-md border border-brand/40 bg-brand/5 p-3 text-sm"><p role="status" className="min-w-0 flex-1">Deleted <span className="font-mono">{deleted.name}</span>.</p><Button ref={restoreRef} type="button" size="sm" variant="outline" onClick={() => void (async () => { setRestoreProblem(undefined); const result = await api.POST("/projects/{key}/labels/{name}/restore", { params: { path: { key: project!, name: deleted.name } } }); if (!result.data) { setRestoreProblem({ of: project, why: describe(result.error, result.response.status) }); return; } setRecentlyDeleted(undefined); await reload(); })()}>Restore {deleted.name}</Button></div>}
          {restoreError && <p role="alert" className="text-sm text-destructive">{restoreError}</p>}
        </div>
      )}
      {loaded.at === "known" && (
        <div className="space-y-5 p-4">
          <LabelForm groups={named} submit="Create" layout="row" clear onSubmit={create} />
          {groups.map((group) => (
            <section key={group}>
              <div className="mb-1 flex min-h-8 flex-wrap items-center gap-x-2">
                <h2 className="text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                  {group === "" ? "Ungrouped" : `${group} · one of`}
                </h2>
                {group !== "" && (
                  <>
                    <TextActionDialog
                      trigger={<Button variant="ghost" size="sm">Rename group</Button>}
                      title={`Rename ${group}`}
                      description="Every label in the group is written again with the new name."
                      label="Group"
                      initialValue={group}
                      submitLabel="Rename group"
                      onSubmit={(to) => moveGroup(group, to)}
                    />
                    <ActionDialog
                      trigger={<Button variant="ghost" size="sm">Dissolve group</Button>}
                      title={`Dissolve ${group}?`}
                      description="Every label in it keeps its name and stops excluding the others."
                      confirmLabel="Dissolve group"
                      confirmVariant="default"
                      onConfirm={() => moveGroup(group, "")}
                    />
                  </>
                )}
              </div>
              <ul className="divide-y rounded-md border">
                {loaded.labels
                  .filter((label) => (label.group ?? "") === group)
                  .map((label) => (
                    // Narrow: the name and what it means stand above the two
                    // acts rather than beside them.
                    <li key={label.name} className="flex flex-col gap-1 px-3 py-2 sm:min-h-9 sm:flex-row sm:items-center sm:gap-3 sm:py-1">
                      <span className="font-mono text-xs">{label.name}</span>
                      <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">{label.description}</span>
                      <span className="flex shrink-0 items-center gap-1 self-end sm:self-auto">
                        <EditLabel label={label} groups={named} onSubmit={(draft) => change(label, draft)} />
                        <ActionDialog
                          trigger={<Button variant="ghost" size="sm">Delete</Button>}
                          title={`Delete ${label.name}?`}
                          description="The label will disappear from the project, but can be restored during the instance grace period."
                          confirmLabel="Delete label"
                          onConfirm={async () => {
                            const result = await api.DELETE("/projects/{key}/labels/{name}", { params: { path: { key: project!, name: label.name } } });
                            if (!result.response.ok) throw new Error(describe(result.error, result.response.status));
                            setRestoreProblem(undefined);
                            setRecentlyDeleted({ of: project, label });
                            await reload();
                          }}
                        />
                      </span>
                    </li>
                  ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </>
  );
}

/**
 * The whole label, not the one field a prompt could ask for. The name is not
 * the key — `Label` has its own id, and `PATCH` takes all three — so a name
 * typed once was never a reason to keep it forever.
 */
function EditLabel({ label, groups, onSubmit }: { label: Label; groups: string[]; onSubmit: (draft: Draft) => Promise<Refused | undefined> }) {
  const [open, setOpen] = useState(false);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button variant="ghost" size="sm" />}>Edit</DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit {label.name}</DialogTitle>
          <DialogDescription>The name, the group it excludes within, and what it means.</DialogDescription>
        </DialogHeader>
        <LabelForm
          initial={{ name: label.name, group: label.group ?? "", description: label.description ?? "" }}
          groups={groups}
          submit="Save label"
          layout="stacked"
          onSubmit={async (draft) => {
            const refused = await onSubmit(draft);
            if (refused === undefined) setOpen(false);
            return refused;
          }}
        />
      </DialogContent>
    </Dialog>
  );
}

const empty: Draft = { name: "", group: "", description: "" };

function LabelForm({
  initial = empty,
  groups,
  submit,
  layout,
  clear = false,
  onSubmit,
}: {
  initial?: Draft;
  groups: string[];
  submit: string;
  /** `row` is the create line at the top of the screen, `stacked` the dialog. */
  layout: "row" | "stacked";
  /** Empty the fields once the write went through — the create line does. */
  clear?: boolean;
  onSubmit: (draft: Draft) => Promise<Refused | undefined>;
}) {
  const [draft, setDraft] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [refused, setRefused] = useState<Refused>();
  const set = <K extends keyof Draft>(field: K, value: Draft[K]) => setDraft((old) => ({ ...old, [field]: value }));

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefused(undefined);

    try {
      const refusal = await onSubmit(draft);
      setRefused(refusal);
      if (refusal === undefined && clear) setDraft(initial);
    } catch {
      setRefused({ why: "The instance did not answer.", fields: {} });
    } finally {
      setBusy(false);
    }
  }

  const at = refused?.fields ?? {};

  return (
    <form
      className={
        layout === "row"
          ? "grid gap-3 rounded-md border p-3 sm:grid-cols-[1fr_1fr_2fr_auto] sm:items-start"
          : "grid gap-4"
      }
      onSubmit={(event) => void save(event)}
    >
      <Field label="Name" value={draft.name} onChange={(value) => set("name", value)} error={at.name} required />
      <GroupPicker groups={groups} value={draft.group} onChange={(group) => set("group", group)} error={at.group} />
      <Field
        label="Description"
        hint="What this label means, for whoever reads the set"
        value={draft.description}
        onChange={(value) => set("description", value)}
        error={at.description}
      />
      {refused?.why != null && <p role="alert" className="text-sm text-destructive">{refused.why}</p>}
      {layout === "row" ? (
        <Button type="submit" className="sm:mt-6" disabled={busy}>{busy ? "Saving…" : submit}</Button>
      ) : (
        <DialogFooter>
          <DialogClose render={<Button variant="outline" disabled={busy} />}>Cancel</DialogClose>
          <Button type="submit" disabled={busy}>{busy ? "Saving…" : submit}</Button>
        </DialogFooter>
      )}
    </form>
  );
}

function Field({
  label,
  hint,
  value,
  onChange,
  error,
  required,
}: {
  label: string;
  hint?: string;
  value: string;
  onChange: (value: string) => void;
  error?: ReactNode;
  required?: boolean;
}) {
  const id = useId();

  return (
    <div className="grid min-w-0 gap-1 text-sm font-medium">
      <label htmlFor={id}>{label}</label>
      {hint !== undefined && <span className="hidden text-xs font-normal text-muted-foreground sm:block">{hint}</span>}
      <Input id={id} value={value} required={required} aria-invalid={error != null || undefined} onChange={(event) => onChange(event.target.value)} />
      {error != null && <p role="alert" className="text-xs font-normal text-destructive">{error}</p>}
    </div>
  );
}

/**
 * `validation` carries `errors`, field to message; the group refusal carries
 * what stands in the way under `issues` and `epics` as well, and those are
 * worth following rather than reading (`docs/api.md`, Errors). All are
 * extension members, so the generated type does not know them.
 */
function refusalOf(problem: Problem | undefined, status: number): Refused {
  const errors = (problem as { errors?: Record<string, string | string[]> } | undefined)?.errors ?? {};
  const carrying = problem as { issues?: string[]; epics?: string[] } | undefined;
  const inTheWay = [...(carrying?.issues ?? []), ...(carrying?.epics ?? [])];
  const fields: Refused["fields"] = {};

  for (const field of ["name", "group", "description"] as const) {
    const said = errors[field];
    if (said === undefined) continue;

    const text = Array.isArray(said) ? said.join(" ") : said;
    fields[field] =
      field === "group" && inTheWay.length > 0 ? (
        <>
          {text}{" "}
          {inTheWay.map((key, index) => (
            <span key={key}>
              {index > 0 && ", "}
              <Link className="font-mono underline" to={keyPath(key)}>
                {key}
              </Link>
            </span>
          ))}
        </>
      ) : (
        text
      );
  }

  const rest = Object.keys(errors).filter((field) => !["name", "group", "description"].includes(field));

  return { fields, why: rest.length > 0 || Object.keys(fields).length === 0 ? describe(problem, status) : undefined };
}
