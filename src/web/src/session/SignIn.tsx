import { useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router";
import { api, describe, type Me } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export function SignIn({ onSignedIn }: { onSignedIn: (me: Me) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [refusal, setRefusal] = useState<string | null>(null);
  const [asking, setAsking] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault(); setAsking(true); setRefusal(null);
    try {
      const signedIn = await api.POST("/session", { body: { email, password } });
      if (!signedIn.response.ok) { setRefusal(describe(signedIn.error, signedIn.response.status)); return; }
      const me = await api.GET("/me");
      if (me.data) onSignedIn(me.data); else setRefusal(describe(me.error, me.response.status));
    } catch { setRefusal("The instance did not answer."); }
    finally { setAsking(false); }
  }

  return <AuthFrame><h1 className="text-xl font-semibold">Sign in</h1>
  <form onSubmit={submit} className="space-y-5">
    <Field label="Email"><Input id="email" type="email" autoComplete="email" autoFocus value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
    <Field label="Password"><Input id="password" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
    {refusal && <p role="alert" className="text-destructive text-sm">{refusal}</p>}
    <Button type="submit" disabled={asking || !email || !password} className="w-full">Sign in</Button>
    <Link to="/recover" className="text-brand block text-center text-sm hover:underline">Forgot your password?</Link>
  </form></AuthFrame>;
}

export function AuthFrame({ children }: { children: ReactNode }) {
  return <main className="flex min-h-svh items-center justify-center p-6"><div className="w-full max-w-sm space-y-6">
    <div className="flex items-center gap-2 text-base font-semibold"><span aria-hidden className="size-4.5 rounded-sm bg-brand" />hostingaffe</div>{children}
  </div></main>;
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return <div className="space-y-2"><label htmlFor={label.toLowerCase()} className="block text-sm font-medium">{label}</label>{children}</div>;
}
