import { useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { api, describe, type Project, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Picker, type Choice } from "@/components/ui/picker";
import { useSession } from "@/session/useSession";
import { ActionDialog } from "@/shared/ActionDialog";
import { submitting } from "./forms";
import { Row, Rows, Said, Section, SettingsShell } from "./SettingsShell";

type User = Schemas["UserSummary"];

/**
 * The project's own settings. Labels used to be a section here that was only a
 * link to another screen — a seam that explained nothing but that the split
 * had not been thought through. They are a daily working screen of the
 * project's navigation and not a setting, so the pseudo-section is gone.
 */
export function ProjectSettingsView() {
  const { project: key } = useParams();

  return (
    <SettingsShell
      title={`${key} settings`}
      areas={[
        { to: "general", label: "General", element: <General /> },
        { to: "instructions", label: "Instructions", element: <Instructions /> },
        { to: "members", label: "Members", element: <Members /> },
      ]}
    />
  );
}

function General() {
  const { project: key } = useParams();
  const { me } = useSession();
  const [project, setProject] = useState<Project>();
  const [notice, setNotice] = useState("");

  useEffect(() => {
    let current = true;
    void (async () => {
      const { data } = await api.GET("/projects/{key}", { params: { path: { key: key! } } });
      if (current) setProject(data);
    })();
    return () => { current = false; };
  }, [key]);

  return (
    <>
      <Section title="Project" description="The key is permanent; the name can change.">
        {project && (
          <form className="grid max-w-lg gap-3" onSubmit={(e) => void submitting(e, setNotice, async (data) => { const r = await api.PATCH("/projects/{key}", { params: { path: { key: key! } }, body: { name: String(data.get("name")) } as never }); if (!r.data) throw new Error(describe(r.error, r.response.status)); setProject(r.data); })}>
            <label className="text-sm">Name<Input name="name" defaultValue={project.name} /></label>
            <Button type="submit" className="w-fit">Save project</Button>
          </form>
        )}
        <Said notice={notice} />
      </Section>
      {me.administrator && (
        <Section title="Project lifecycle" description="Deletion is reversible during the instance grace period.">
          <ActionDialog
            trigger={<Button variant="destructive">Delete project</Button>}
            title={`Delete project ${key}?`}
            description="The project and its content will disappear, but can be restored during the instance grace period."
            confirmLabel="Delete project"
            onConfirm={async () => {
              const result = await api.DELETE("/projects/{key}", { params: { path: { key: key! } } });
              if (!result.response.ok) throw new Error(describe(result.error, result.response.status));
              window.location.assign("/");
            }}
          />
        </Section>
      )}
    </>
  );
}

/**
 * Which page of the wiki every agent is handed with every ticket
 * (`CONTEXT.md`, Instructions). One page and not a mark on any number of them:
 * three marked pages would be three pages of context on every ticket, and that
 * budget is the point. The text is not edited here — it is an ordinary page,
 * written where every other page is written, and this screen only says which
 * one it is.
 */
function Instructions() {
  const { project: key } = useParams();
  const [project, setProject] = useState<Project>();
  const [pages, setPages] = useState<Schemas["PageSummary"][]>([]);
  const [notice, setNotice] = useState("");

  useEffect(() => {
    let current = true;
    void (async () => {
      const [read, listed] = await Promise.all([
        api.GET("/projects/{key}", { params: { path: { key: key! } } }),
        api.GET("/projects/{key}/pages", { params: { path: { key: key! } } }),
      ]);
      if (!current) return;
      setProject(read.data);
      setPages(listed.data ?? []);
    })();
    return () => { current = false; };
  }, [key]);

  const chosen = project?.instructions_page ?? "";
  const choices: Choice[] = [
    { id: "", name: "None", hint: "Agents are handed the ticket and nothing else" },
    ...pages.map((page) => ({ id: page.slug, name: page.slug, hint: page.title })),
  ];

  async function designate(slug: string) {
    setNotice("");
    const r = await api.PATCH("/projects/{key}", {
      params: { path: { key: key! } },
      // Only this field: every property of the change is required in the
      // contract, and a change that names the others would write them too.
      body: { instructions_page: slug === "" ? null : slug } as never,
    });
    if (!r.data) { setNotice(describe(r.error, r.response.status)); return; }
    setProject(r.data);
    setNotice("Saved.");
  }

  return (
    <Section
      title="Instructions"
      description="One page of this project's wiki, delivered to every agent with every ticket — what holds for all work here, whoever does it."
    >
      {project && (
        <div className="grid max-w-lg gap-3">
          <Picker
            label="Instructions page"
            placeholder="None"
            empty={pages.length === 0 ? "This project has no pages yet." : "No page of this project matches."}
            choices={choices}
            value={chosen === "" ? [] : [chosen]}
            onChange={(slugs) => void designate(slugs[0] ?? "")}
          />
          {chosen !== "" && (
            <Link className="w-fit text-sm underline underline-offset-4" to={`/${key}/pages/${chosen}`}>
              Open {chosen}
            </Link>
          )}
        </div>
      )}
      <Said notice={notice} />
    </Section>
  );
}

function Members() {
  const { project: key } = useParams();
  const [users, setUsers] = useState<User[]>([]);

  useEffect(() => {
    let current = true;
    void (async () => {
      const { data } = await api.GET("/projects/{key}/users", { params: { path: { key: key! } } });
      if (current) setUsers(data ?? []);
    })();
    return () => { current = false; };
  }, [key]);

  return (
    <Section title="Members" description="Project access is managed by administrators.">
      <Rows empty="Nobody has access.">
        {users.map((u) => <Row key={u.id} title={u.name} detail={`${u.email} · ${u.state}${u.administrator ? " · administrator" : ""}`} />)}
      </Rows>
    </Section>
  );
}
