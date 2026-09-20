import { useId, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { api, describe, type Problem, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { useAbandon } from "@/shared/abandon";
import { useAsk } from "@/shared/ask";
import { Nothing, Section } from "@/shared/Detail";
import { stale } from "@/shared/stale";
import { machinePath, providerPath } from "./addresses";

type Machine = Schemas["Machine"];
type ProviderSummary = Schemas["ProviderSummary"];

export function ProviderAssignment({ machine, onWritten }: { machine: Machine; onWritten: () => void }) {
  const [editing, setEditing] = useState(false);
  const provider = machine.provider === null ? null :
    <Link className="text-brand hover:underline" to={providerPath(machine.provider)}>{machine.provider}</Link>;

  if (machine.kind === "vm") return <Section title="Provider assignment">
    <p className="text-sm">
      This VM inherits {provider ?? "no provider"} from its host
      {machine.host !== null && <> <Link className="text-brand hover:underline" to={machinePath(machine.host)}>{machine.host}</Link></>}.
      Change the host's provider to change this value.
    </p>
    {machine.legacy_provider != null && machine.legacy_provider !== machine.provider &&
      <p className="mt-2 text-sm text-muted-foreground">Before providers were records, this VM said: {machine.legacy_provider}</p>}
  </Section>;

  return <Section title="Provider assignment" action={!editing &&
    <Button variant="outline" size="sm" onClick={() => setEditing(true)}>Change</Button>}>
    {editing ? <AssignmentForm machine={machine} onDone={() => { setEditing(false); onWritten(); }}
      onCancel={() => setEditing(false)} /> : provider ?? <Nothing>No external provider assigned.</Nothing>}
    {!editing && machine.legacy_provider != null && machine.legacy_provider !== machine.provider &&
      <p className="mt-2 text-sm text-muted-foreground">Original provider text: {machine.legacy_provider}</p>}
  </Section>;
}

function AssignmentForm({ machine, onDone, onCancel }: {
  machine: Machine; onDone: () => void; onCancel: () => void;
}) {
  const { asked } = useAsk<ProviderSummary[]>(`provider-options:${machine.key}`, (signal) =>
    api.GET("/api/providers", { signal }));
  const [provider, setProvider] = useState(machine.provider ?? "");
  const [version, setVersion] = useState(machine.updated_at);
  const [conflict, setConflict] = useState<Machine>();
  const [why, setWhy] = useState<string>();
  const [saving, setSaving] = useState(false);
  const selectId = useId();
  const { leave, dialog } = useAbandon(provider !== (machine.provider ?? ""), onCancel);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setWhy(undefined);
    setConflict(undefined);
    try {
      const answer = await api.PATCH("/api/machines/{key}", {
        params: { path: { key: machine.key } },
        headers: { "If-Match": version },
        body: { provider },
      });
      const current = stale<Machine>(answer);
      if (current !== undefined) {
        setConflict(current);
        setVersion(current.updated_at);
        return;
      }
      if (answer.data === undefined) {
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

  return <form className="grid gap-3 text-sm" onSubmit={(event) => void save(event)}>
    <label htmlFor={selectId} className="font-medium">Provider</label>
    {asked.at === "asking" && <p aria-busy className="text-muted-foreground">Loading providers…</p>}
    {asked.at === "failed" && <p role="alert" className="text-destructive">{asked.why}</p>}
    {asked.at === "known" && <select id={selectId} value={provider} onChange={(event) => setProvider(event.target.value)}
      className="h-9 w-full max-w-sm rounded-md border bg-background px-2">
      <option value="">No provider</option>
      {asked.value.map((option) => <option key={option.key} value={option.key}>{option.name} ({option.key})</option>)}
    </select>}
    {conflict !== undefined && <div role="alert" className="rounded-lg border border-amber-500/40 bg-amber-500/5 p-3">
      This machine changed while you were editing it. Its current provider is {conflict.provider ?? "none"}.
      Your choice is kept; saving now writes over that version.
    </div>}
    {why !== undefined && <p role="alert" className="text-destructive">{why}</p>}
    <div className="flex flex-wrap justify-end gap-2">
      <Button type="button" variant="outline" onClick={leave}>Cancel</Button>
      <Button type="submit" disabled={saving || asked.at !== "known"}>{saving ? "Saving…" : "Save provider"}</Button>
    </div>
    {dialog}
  </form>;
}
