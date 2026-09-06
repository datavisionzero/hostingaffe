import { useEffect, useState, type FormEvent } from "react";
import { Navigate } from "react-router";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { DropdownMenuItem } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { useSession } from "@/session/useSession";
import { reporting } from "@/shared/report";
import { Row, RowMenu, Rows, Said, Section, SettingsShell } from "./SettingsShell";

type User = Schemas["UserSummary"];
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
        { to: "email", label: "Transactional email", element: <Email /> },
      ]}
    />
  );
}

function Users() {
  const [users, setUsers] = useState<User[]>([]);
  const [notice, setNotice] = useState("");
  async function load() { setUsers((await api.GET("/api/users")).data ?? []); }
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
    const invited = await report(api.POST("/api/users", { body: { name: String(data.get("name")), email: String(data.get("email")), administrator: data.has("administrator") } }), "Invitation sent.");
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
                {u.state === "invited" && <DropdownMenuItem onClick={() => void report(api.POST("/api/users/{id}/invitation", { params: { path: { id: u.id } } }), "Invitation resent.")}>Resend invitation</DropdownMenuItem>}
                <DropdownMenuItem onClick={() => void report(api.PATCH("/api/users/{id}", { params: { path: { id: u.id } }, body: { administrator: !u.administrator } }), u.administrator ? `${u.name} is no longer an administrator.` : `${u.name} is now an administrator.`)}>{u.administrator ? "Demote" : "Make admin"}</DropdownMenuItem>
                <DropdownMenuItem onClick={() => void report(api.POST(u.state === "deactivated" ? "/api/users/{id}/reactivate" : "/api/users/{id}/deactivate", { params: { path: { id: u.id } } }), u.state === "deactivated" ? `${u.name} is active again.` : `${u.name} is deactivated.`)}>{u.state === "deactivated" ? "Reactivate" : "Deactivate"}</DropdownMenuItem>
              </RowMenu>
            }
          />
        ))}
      </Rows>
      <Said notice={notice} />
    </Section>
  );
}

function Email() {
  const [smtp, setSmtp] = useState<Smtp>();
  const [notice, setNotice] = useState("");
  useEffect(() => { void (async () => { setSmtp((await api.GET("/api/admin/smtp")).data); })(); }, []);

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
    const { error, response } = await api.POST("/api/admin/smtp/test", { body: { email } });
    setNotice(response.ok ? "Test email sent." : describe(error, response.status));
  } catch {
    setNotice("The instance did not answer.");
  }
}
