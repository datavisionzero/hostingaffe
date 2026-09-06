import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Link, Navigate, Route, Routes, useParams } from "react-router";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { DropdownMenuItem } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { useSession } from "@/session/useSession";
import { reporting } from "@/shared/report";
import { date, submitting } from "./forms";
import { Row, RowMenu, Rows, Said, Section, SettingsShell } from "./SettingsShell";

type User = Schemas["UserSummary"];
type AdminProject = Schemas["AdminProject"];
type Smtp = Schemas["SmtpStatus"];

/** The instance's own administration, one area per address. */
export function AdminView() {
  const { me } = useSession();

  if (!me.administrator) return <Navigate to="/" replace />;

  return (
    <SettingsShell
      title="Instance administration"
      areas={[
        { to: "users", label: "Users", element: <Users /> },
        { to: "projects", path: "projects/*", label: "Projects", element: <Projects /> },
        { to: "email", label: "Transactional email", element: <Email /> },
      ]}
    />
  );
}

function Users() {
  const [users, setUsers] = useState<User[]>([]);
  const [notice, setNotice] = useState("");
  async function load() { setUsers((await api.GET("/users")).data ?? []); }
  useEffect(() => { void (async () => { await load(); })(); }, []);
  const report = reporting(setNotice, load);

  // The form is taken before the await: React empties `currentTarget` once
  // the event has been dispatched, and reading it back threw where the list
  // was about to be reloaded, so an invited user did not appear until the
  // page was loaded again.
  async function invite(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    const invited = await report(api.POST("/users", { body: { name: String(data.get("name")), email: String(data.get("email")), administrator: data.has("administrator") } }), "Invitation sent.");
    if (invited) form.reset();
  }

  return (
    <Section title="Users" description="Invite users and manage their instance role and lifecycle.">
      <form className="mb-3 grid gap-2 sm:grid-cols-[1fr_1fr_auto]" onSubmit={(e) => void invite(e)}>
        <Input name="name" placeholder="Name" aria-label="Name" />
        <Input name="email" type="email" placeholder="Email" aria-label="Email" />
        <Button type="submit">Invite</Button>
        <label className="flex gap-2 text-sm sm:col-span-3"><input name="administrator" type="checkbox" /> Administrator</label>
      </form>
      <Rows empty="No users.">
        {users.map((u) => (
          <Row
            key={u.id}
            title={u.name}
            detail={`${u.email} · ${u.state}${u.administrator ? " · administrator" : ""}`}
            action={
              <RowMenu label={`Actions for ${u.name}`}>
                {u.state === "invited" && <DropdownMenuItem onClick={() => void report(api.POST("/users/{id}/invitation", { params: { path: { id: u.id } } }), "Invitation resent.")}>Resend invitation</DropdownMenuItem>}
                <DropdownMenuItem onClick={() => void report(api.PATCH("/users/{id}", { params: { path: { id: u.id } }, body: { administrator: !u.administrator } }), u.administrator ? `${u.name} is no longer an administrator.` : `${u.name} is now an administrator.`)}>{u.administrator ? "Demote" : "Make admin"}</DropdownMenuItem>
                <DropdownMenuItem onClick={() => void report(api.POST(u.state === "deactivated" ? "/users/{id}/reactivate" : "/users/{id}/deactivate", { params: { path: { id: u.id } } }), u.state === "deactivated" ? `${u.name} is active again.` : `${u.name} is deactivated.`)}>{u.state === "deactivated" ? "Reactivate" : "Deactivate"}</DropdownMenuItem>
              </RowMenu>
            }
          />
        ))}
      </Rows>
      <Said notice={notice} />
    </Section>
  );
}

/**
 * The projects of the instance, as a list with a detail behind each one. The
 * single box held a form and a button per permitted user for every project at
 * once — it grew with projects times users — and it asked every project for
 * its access list on opening, to show one of them.
 */
function Projects() {
  return (
    <Routes>
      <Route index element={<ProjectList />} />
      <Route path=":key" element={<ProjectAccess />} />
    </Routes>
  );
}

function useAdminProjects(): AdminProject[] {
  const [projects, setProjects] = useState<AdminProject[]>([]);

  useEffect(() => {
    let current = true;
    void (async () => {
      const { data } = await api.GET("/admin/projects", { params: { query: { deleted: "all" } } });
      if (current) setProjects(data ?? []);
    })();
    return () => { current = false; };
  }, []);

  return projects;
}

function ProjectList() {
  const projects = useAdminProjects();

  return (
    <Section title="Projects" description="Every project of the instance, deleted ones included.">
      <Rows empty="No projects.">
        {projects.map((p) => (
          <Row
            key={p.key}
            title={<Link className="hover:underline" to={p.key}>{p.key} · {p.name}</Link>}
            detail={p.deleted_at ? `Deleted ${date(p.deleted_at)}` : undefined}
          />
        ))}
      </Rows>
    </Section>
  );
}

function ProjectAccess() {
  const { key } = useParams();
  const [project, setProject] = useState<AdminProject>();
  const [users, setUsers] = useState<User[]>([]);
  const [permitted, setPermitted] = useState<User[]>([]);
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    const [all, everybody, projects] = await Promise.all([
      api.GET("/projects/{key}/users", { params: { path: { key: key! } } }),
      api.GET("/users"),
      api.GET("/admin/projects", { params: { query: { deleted: "all" } } }),
    ]);
    setPermitted(all.data ?? []);
    setUsers(everybody.data ?? []);
    setProject((projects.data ?? []).find((candidate) => candidate.key === key));
  }, [key]);

  useEffect(() => { void (async () => { await load(); })(); }, [load]);
  const report = reporting(setNotice, load);

  // Everyone who does not have access to this project yet. With nobody left
  // the select has no options, contributes no form entry, and the id read
  // back out of it was the string "null".
  const grantable = users.filter((candidate) => !permitted.some((x) => x.id === candidate.id));

  return (
    <Section title={project === undefined ? key! : `${project.key} · ${project.name}`} description="Who may see and write in this project.">
      <p className="mb-3 text-sm"><Link className="text-brand hover:underline" to="..">All projects</Link></p>
      {project?.deleted_at != null ? (
        <>
          <p className="mb-3 text-sm text-muted-foreground">Deleted {date(project.deleted_at)}.</p>
          <Button size="sm" variant="outline" onClick={() => void report(api.POST("/projects/{key}/restore", { params: { path: { key: key! } } }), `${key} restored.`)}>Restore</Button>
        </>
      ) : (
        <>
          <form className="mb-3 flex gap-2" onSubmit={(e) => void submitting(e, setNotice, async (data) => { if (grantable.length === 0) return; const id = String(data.get("user")); const r = await api.PUT("/projects/{key}/users/{id}", { params: { path: { key: key!, id } } }); if (!r.response.ok) throw new Error(describe(r.error, r.response.status)); await load(); })}>
            <select name="user" aria-label={`User for ${key}`} className="h-8 flex-1 rounded-md border bg-background px-2 text-sm">
              {grantable.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
            </select>
            <Button type="submit" size="sm" variant="outline" disabled={grantable.length === 0}>Grant access</Button>
          </form>
          <Rows empty="Nobody has access.">
            {permitted.map((u) => (
              <Row
                key={u.id}
                title={u.name}
                detail={`${u.email} · ${u.state}${u.administrator ? " · administrator" : ""}`}
                action={
                  <RowMenu label={`Actions for ${u.name}`}>
                    <DropdownMenuItem onClick={() => void report(api.DELETE("/projects/{key}/users/{id}", { params: { path: { key: key!, id: u.id } } }), `${u.name} no longer has access to ${key}.`)}>Remove access</DropdownMenuItem>
                  </RowMenu>
                }
              />
            ))}
          </Rows>
        </>
      )}
      <Said notice={notice} />
    </Section>
  );
}

function Email() {
  const [smtp, setSmtp] = useState<Smtp>();
  const [notice, setNotice] = useState("");
  useEffect(() => { void (async () => { setSmtp((await api.GET("/admin/smtp")).data); })(); }, []);

  return (
    <Section title="Transactional email" description="Credentials remain in environment variables.">
      {smtp && <p className="mb-3 text-sm">{smtp.configured ? `${smtp.host}:${smtp.port} · ${smtp.security} · ${smtp.sender}` : "Not configured"}</p>}
      <form className="flex max-w-lg gap-2" onSubmit={(e) => { e.preventDefault(); const email = String(new FormData(e.currentTarget).get("email")); void sendTest(email, setNotice); }}>
        <Input name="email" type="email" placeholder="Test recipient" aria-label="Test recipient" />
        <Button type="submit" disabled={!smtp?.configured}>Send test</Button>
      </form>
      <Said notice={notice} />
    </Section>
  );
}

/**
 * The only write on this screen that changes nothing, so it reports what the
 * instance answered without reloading anything behind it.
 */
async function sendTest(email: string, setNotice: (notice: string) => void) {
  try {
    const { error, response } = await api.POST("/admin/smtp/test", { body: { email } });
    setNotice(response.ok ? "Test email sent." : describe(error, response.status));
  } catch {
    setNotice("The instance did not answer.");
  }
}
