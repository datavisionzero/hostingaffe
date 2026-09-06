import { useEffect, useState } from "react";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useSession } from "@/session/useSession";
import { TextActionDialog } from "@/shared/ActionDialog";
import { reporting } from "@/shared/report";
import { date, submitting } from "./forms";
import { Row, Rows, Said, Secret, Section, SettingsShell } from "./SettingsShell";

type Session = Schemas["BrowserSessionSummary"];
type Token = Schemas["TokenSummary"];
type Agent = Schemas["AgentSummary"];

/** The identity's own settings, one area per address. */
export function SettingsView() {
  return (
    <SettingsShell
      title="Personal settings"
      areas={[
        { to: "profile", label: "Profile", element: <Profile /> },
        { to: "security", label: "Security", element: <Security /> },
        { to: "tokens", label: "User tokens", element: <Tokens /> },
        { to: "agents", label: "Agents", element: <Agents /> },
      ]}
    />
  );
}

function Profile() {
  const { me } = useSession();
  const [notice, setNotice] = useState("");

  return (
    <Section title="Profile" description="Your name is used on comments and history.">
      <form className="flex max-w-lg gap-2" onSubmit={(e) => void submitting(e, setNotice, async (data) => { const r = await api.PATCH("/me", { body: { name: String(data.get("name")) } }); if (!r.data) throw new Error(describe(r.error, r.response.status)); window.location.reload(); })}>
        <Input name="name" defaultValue={me.name} aria-label="Name" />
        <Button type="submit">Save name</Button>
      </form>
      <form className="mt-3 flex max-w-lg gap-2" onSubmit={(e) => void submitting(e, setNotice, async (data) => { const r = await api.POST("/me/email", { body: { email: String(data.get("email")) } }); if (!r.response.ok) throw new Error(describe(r.error, r.response.status)); setNotice("Check the new address to confirm the change."); })}>
        <Input name="email" type="email" defaultValue={me.email ?? ""} aria-label="Email" />
        <Button type="submit" variant="outline">Change email</Button>
      </form>
      <Said notice={notice} />
    </Section>
  );
}

/** The password and the browsers it signs in: one subject, one address. */
function Security() {
  const [sessions, setSessions] = useState<Session[]>([]);
  const [passwordNotice, setPasswordNotice] = useState("");
  const [notice, setNotice] = useState("");
  async function load() { setSessions((await api.GET("/sessions")).data ?? []); }
  useEffect(() => { void (async () => { await load(); })(); }, []);
  const report = reporting(setNotice, load);

  return (
    <>
      <Section title="Password" description="Changing it signs every other browser session out.">
        <form className="grid max-w-lg gap-2" onSubmit={(e) => void submitting(e, setPasswordNotice, async (data, form) => { const r = await api.POST("/me/password", { body: { current_password: String(data.get("current")), password: String(data.get("password")) } }); if (!r.response.ok) throw new Error(describe(r.error, r.response.status)); form.reset(); })}>
          <Input name="current" type="password" placeholder="Current password" aria-label="Current password" />
          <Input name="password" type="password" placeholder="New password (12 characters or more)" minLength={12} aria-label="New password" />
          <Button type="submit" className="w-fit">Change password</Button>
        </form>
        <Said notice={passwordNotice} />
      </Section>
      <Section title="Browser sessions" description="Revoke browsers you no longer use.">
        <Rows empty="No browser sessions.">
          {sessions.map((s) => <Row key={s.id} title={s.current ? "This browser" : `Used ${date(s.last_used_at)}`} detail={`Created ${date(s.created_at)} · expires ${date(s.expires_at)}`} action={!s.current && <Button variant="outline" size="sm" onClick={() => void report(api.DELETE("/sessions/{id}", { params: { path: { id: s.id } } }), "Session revoked.")}>Revoke</Button>} />)}
        </Rows>
        <Button className="mt-3" variant="outline" onClick={() => void report(api.DELETE("/sessions"), "Every other session revoked.")}>Revoke all other sessions</Button>
        <Said notice={notice} />
      </Section>
    </>
  );
}

function Tokens() {
  const [tokens, setTokens] = useState<Token[]>([]);
  const [secret, setSecret] = useState("");
  const [notice, setNotice] = useState("");
  async function load() { setTokens((await api.GET("/tokens")).data ?? []); }
  useEffect(() => { void (async () => { await load(); })(); }, []);
  const report = reporting(setNotice, load);

  return (
    <Section title="User tokens" description="For the CLI and direct API use. A new secret is shown once.">
      {secret && <Secret value={secret} />}
      <Button className="mb-3" onClick={() => void report(api.POST("/tokens"), "Token created.").then((created) => { if (created) setSecret(created.secret); })}>Create token</Button>
      <Rows empty="No user tokens.">
        {tokens.map((t) => <Row key={t.id} title={`${t.prefix}…`} detail={t.revoked_at ? `Revoked ${date(t.revoked_at)}` : `Created ${date(t.created_at)}`} action={!t.revoked_at && <Button variant="outline" size="sm" onClick={() => void report(api.DELETE("/tokens/{id}", { params: { path: { id: t.id } } }), "Token revoked.")}>Revoke</Button>} />)}
      </Rows>
      <Said notice={notice} />
    </Section>
  );
}

function Agents() {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [secret, setSecret] = useState("");
  const [notice, setNotice] = useState("");
  async function load() { setAgents((await api.GET("/agents")).data ?? []); }
  useEffect(() => { void (async () => { await load(); })(); }, []);
  const report = reporting(setNotice, load);

  return (
    <Section title="Agents" description="Each agent has one token and inherits your project access.">
      {secret && <Secret value={secret} />}
      <form className="mb-3 flex max-w-lg gap-2" onSubmit={(e) => void submitting(e, setNotice, async (data, form) => { const r = await api.POST("/agents", { body: { name: String(data.get("name")) || null } }); if (!r.data) throw new Error(describe(r.error, r.response.status)); setSecret(r.data.token.secret); await load(); form.reset(); })}>
        <Input name="name" placeholder="Optional agent name" aria-label="Agent name" />
        <Button type="submit">Create agent</Button>
      </form>
      <Rows empty="No agents.">
        {agents.map((a) => (
          <Row
            key={a.id}
            title={a.name}
            detail={`${a.token.prefix}… · ${a.token.revoked_at ? "revoked" : "active"}`}
            action={
              <div className="flex gap-1">
                <TextActionDialog trigger={<Button variant="outline" size="sm">Rename</Button>} title="Rename agent" description="Change the name used to identify this agent." label="Agent name" initialValue={a.name} submitLabel="Save name" onSubmit={async (name) => { const result = await api.PATCH("/agents/{id}", { params: { path: { id: a.id } }, body: { name } }); if (!result.data) throw new Error(describe(result.error, result.response.status)); await load(); }} />
                {!a.token.revoked_at && <Button variant="outline" size="sm" onClick={() => void report(api.DELETE("/agents/{id}", { params: { path: { id: a.id } } }), "Agent revoked.")}>Revoke</Button>}
              </div>
            }
          />
        ))}
      </Rows>
      <Said notice={notice} />
    </Section>
  );
}
