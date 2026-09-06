import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router";
import { api, describe } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { AuthFrame, Field } from "./SignIn";

export function Recover() {
  const [parameters] = useSearchParams(); const secret = parameters.get("secret");
  const [value, setValue] = useState(""); const [message, setMessage] = useState<string | null>(null);
  async function submit(event: FormEvent) {
    event.preventDefault();
    const result = secret ? await api.POST("/password-recovery/complete", { body: { secret, password: value } }) : await api.POST("/password-recovery", { body: { email: value } });
    setMessage(result.response.ok ? (secret ? "Your password has been changed." : "If the address can recover an account, an email is on its way.") : describe(result.error, result.response.status));
  }
  return <AuthFrame><h1 className="text-xl font-semibold">{secret ? "Choose a new password" : "Recover your account"}</h1>
    <form onSubmit={submit} className="space-y-5"><Field label={secret ? "Password" : "Email"}><Input id={secret ? "password" : "email"} type={secret ? "password" : "email"} autoComplete={secret ? "new-password" : "email"} value={value} onChange={(e) => setValue(e.target.value)} /></Field>
      {message && <p role="status" className="text-sm">{message}</p>}<Button type="submit" className="w-full" disabled={!value || (!!secret && value.length < 12)}>Continue</Button><Link to="/login" className="text-brand block text-center text-sm hover:underline">Back to sign in</Link></form></AuthFrame>;
}
