import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { api, describe } from "@/api/client";
import { useProjectList } from "./useProjects";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PageHeader } from "@/shared/PageHeader";

export function NewProjectView() {
  const navigate = useNavigate(); const { reload } = useProjectList(); const [error, setError] = useState("");
  // A project key is upper case (CONTEXT.md, Project key) and the instance
  // refuses anything else. The field drew what was typed in upper case and
  // sent it as typed, so a key typed in lower case looked accepted and came
  // back refused. What is shown is now what is sent.
  const [key, setKey] = useState("");
  async function create(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const data = new FormData(event.currentTarget); const result = await api.POST("/projects", { body: { key, name: String(data.get("name")), triage_required: data.has("triage"), review_required: data.has("review") } }); // The frame first, then the URL: the shell is not remounted by navigation,
    // so a shell still on the old list has no current project on arrival and
    // draws every one of its links disabled.
    if (result.data) { await reload(); void navigate(`/${result.data.key}/ready`); } else setError(describe(result.error, result.response.status)); }
  return <><PageHeader title="Create project" /><form className="mx-auto grid w-full max-w-lg gap-4 p-6" onSubmit={(e) => void create(e)}><label className="text-sm">Key<Input name="key" value={key} onChange={(e) => setKey(e.target.value.toUpperCase())} required pattern="[A-Z][A-Z0-9]{1,9}" title="Two to ten characters: a letter, then letters and digits. Never changed afterwards." className="mt-1" /></label><label className="text-sm">Name<Input name="name" required className="mt-1" /></label><label className="flex gap-2 text-sm"><input name="triage" type="checkbox" /> Require triage</label><label className="flex gap-2 text-sm"><input name="review" type="checkbox" /> Require review</label>{error && <p role="alert" className="text-sm text-destructive">{error}</p>}<Button type="submit">Create project</Button></form></>;
}
