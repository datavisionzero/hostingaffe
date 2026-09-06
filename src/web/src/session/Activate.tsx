import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { api, describe, type Me } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { AuthFrame, Field } from "./SignIn";

export function Activate({ onActivated }: { onActivated: (me: Me) => void }) {
  const [parameters] = useSearchParams(); const invitation = parameters.get("secret");
  const [token, setToken] = useState(""); const [password, setPassword] = useState("");
  const [refusal, setRefusal] = useState<string | null>(null);
  async function submit(event: FormEvent) {
    event.preventDefault();
    const result = invitation ? await api.POST("/invitations/accept", { body: { secret: invitation, password } }) : await api.POST("/session/bootstrap", { body: { token, password } });
    if (!result.response.ok) { setRefusal(describe(result.error, result.response.status)); return; }
    const me = await api.GET("/me");
    if (me.data) onActivated(me.data); else setRefusal(describe(me.error, me.response.status));
  }
  return <AuthFrame><div><h1 className="text-xl font-semibold">Set your password</h1><p className="text-muted-foreground mt-1 text-sm">Use at least 12 characters.</p></div>
    <form onSubmit={submit} className="space-y-5">
      {!invitation && <Field label="Bootstrap token"><Input id="bootstrap token" type="password" autoComplete="off" value={token} onChange={(e) => setToken(e.target.value)} /></Field>}
      <Field label="Password"><Input id="password" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
      {refusal && <p role="alert" className="text-destructive text-sm">{refusal}</p>}<Button type="submit" className="w-full" disabled={password.length < 12 || (!invitation && !token)}>Continue</Button>
    </form></AuthFrame>;
}
