import { useId, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api, describe, type Problem, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Markdown } from "@/shared/Markdown";
import { MarkdownField } from "@/shared/MarkdownField";
import { PageHeader } from "@/shared/PageHeader";
import { useAbandon } from "@/shared/abandon";
import { useAsk } from "@/shared/ask";
import { Failed, Nothing, Section, Waiting } from "@/shared/Detail";
import { stale } from "@/shared/stale";
import { About, Asks, History, Row, Rows, StatusBadge } from "./Parts";
import { machinePath, providerPath } from "./addresses";
import { EmblemChooser } from "./emblems/EmblemChooser";

type Provider = Schemas["Provider"];
type MachineSummary = Schemas["MachineSummary"];
type HistoryEntry = Schemas["HistoryEntry"];

export function ProviderView() {
  const { key } = useParams();
  const at = key ?? "";
  const [editingAt, setEditingAt] = useState<string>();
  const editing = editingAt === at;
  const { asked, again } = useAsk<Provider>(at, (signal) =>
    api.GET("/api/providers/{key}", { params: { path: { key: at } }, signal }));
  const machines = useAsk<MachineSummary[]>(at, (signal) =>
    api.GET("/api/machines", { params: { query: { provider: at, retired: true } }, signal }));
  const history = useAsk<HistoryEntry[]>(at, (signal) =>
    api.GET("/api/providers/{key}/history", { params: { path: { key: at } }, signal }));

  if (asked.at === "asking") return <Waiting title="Loading the provider…" />;
  if (asked.at === "failed") return <Failed title={at} why={asked.why}
    back={<Link className="text-brand hover:underline" to="/providers">All providers</Link>} />;

  const provider = asked.value;
  return (
    <>
      <PageHeader title={<span className="flex min-w-0 flex-wrap items-center gap-2">
        <EmblemChooser provider={provider} onWritten={() => { again(); history.again(); }} />
        <span className="font-mono text-xs font-normal text-muted-foreground">{provider.key}</span>
        {provider.name}
      </span>}>
        {!editing && <Button variant="outline" size="sm" onClick={() => setEditingAt(at)}>Edit provider</Button>}
      </PageHeader>
      <div className="max-w-3xl flex-1 p-4 md:p-6">
        {editing ? <ProviderForm key={provider.key} initial={provider} onSaved={() => { setEditingAt(undefined); again(); }}
          onCancel={() => setEditingAt(undefined)} /> : (
          <Section title="Description">
            {provider.description === "" ? <Nothing>Nothing is written about this provider yet.</Nothing>
              : <Markdown>{provider.description}</Markdown>}
          </Section>
        )}
        <Asks title="Machines" meta={machines.asked.at === "known" ? `${machines.asked.value.length}` : undefined}
          asked={machines.asked}>
          {(items) => items.length === 0 ? <Nothing>No machines are assigned to this provider.</Nothing> : (
            <Rows>{items.map((machine) => <Row key={machine.key} to={machinePath(machine.key)}>
              <span className="w-32 shrink-0 truncate font-mono text-xs sm:w-48">{machine.key}</span>
              <span className="min-w-0 flex-1 truncate">{machine.name}</span>
              <StatusBadge status={machine.status} />
            </Row>)}</Rows>
          )}
        </Asks>
        <History asked={history.asked} subject={provider.key} />
        <About of={provider} />
      </div>
    </>
  );
}

export function NewProviderView() {
  const navigate = useNavigate();
  return <>
    <PageHeader title="Add provider" />
    <div className="max-w-3xl p-4 md:p-6">
      <ProviderForm onSaved={(provider) => void navigate(providerPath(provider.key), { replace: true })}
        onCancel={() => void navigate("/providers")} />
    </div>
  </>;
}

function ProviderForm({ initial, onSaved, onCancel }: {
  initial?: Provider;
  onSaved: (provider: Provider) => void;
  onCancel: () => void;
}) {
  const [key, setKey] = useState(initial?.key ?? "");
  const [name, setName] = useState(initial?.name ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [version, setVersion] = useState(initial?.updated_at);
  const [conflict, setConflict] = useState<Provider>();
  const [why, setWhy] = useState<string>();
  const [saving, setSaving] = useState(false);
  const keyId = useId();
  const keyHintId = useId();
  const nameId = useId();
  const changed = key !== (initial?.key ?? "") || name !== (initial?.name ?? "") || description !== (initial?.description ?? "");
  const { leave, dialog } = useAbandon(changed, onCancel);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setWhy(undefined);
    setConflict(undefined);
    try {
      const answer = initial === undefined
        ? await api.POST("/api/providers", { body: { key, name, description } })
        : await api.PATCH("/api/providers/{key}", {
          params: { path: { key: initial.key } }, headers: { "If-Match": version ?? initial.updated_at },
          body: { name, description },
        });
      const current = stale<Provider>(answer);
      if (current !== undefined) {
        setConflict(current);
        setVersion(current.updated_at);
        return;
      }
      if (answer.data === undefined) {
        setWhy(describe(answer.error as Problem | undefined, answer.response.status));
        return;
      }
      onSaved(answer.data);
    } catch {
      setWhy("The instance did not answer.");
    } finally {
      setSaving(false);
    }
  }

  return <form className="grid gap-4" onSubmit={(event) => void save(event)}>
    {initial === undefined && <div className="grid gap-1 text-sm font-medium">
      <label htmlFor={keyId}>Key</label>
      <Input id={keyId} required autoFocus value={key} onChange={(event) => setKey(event.target.value)}
        pattern="[a-z0-9]+(-[a-z0-9]+)*" maxLength={64} aria-describedby={keyHintId} />
      <span id={keyHintId} className="text-xs font-normal text-muted-foreground">Lowercase letters, digits and hyphens. This key cannot change later.</span>
    </div>}
    <label className="grid gap-1 text-sm font-medium" htmlFor={nameId}>Name
      <Input id={nameId} required autoFocus={initial !== undefined} value={name}
        onChange={(event) => setName(event.target.value)} />
    </label>
    <MarkdownField label="Description" value={description} onChange={setDescription} />
    {conflict !== undefined && <div role="alert" className="grid gap-2 rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
      <p>This provider changed while you were editing it. Your text is kept; saving now writes over this version.</p>
      <p>Current name: {conflict.name}</p>
      <details><summary>Current description</summary><pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap">{conflict.description}</pre></details>
    </div>}
    {why !== undefined && <p role="alert" className="text-sm text-destructive">{why}</p>}
    <div className="flex flex-wrap justify-end gap-2">
      <Button type="button" variant="outline" onClick={leave}>Cancel</Button>
      <Button type="submit" disabled={saving}>{saving ? "Saving…" : initial === undefined ? "Add provider" : "Save provider"}</Button>
    </div>
    {dialog}
  </form>;
}
