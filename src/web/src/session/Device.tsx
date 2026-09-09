import { useEffect, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { AuthFrame, Field } from "./SignIn";

type Waiting = Schemas["WaitingDeviceLogin"];

/**
 * The other half of `ha login` (ADR 0005): a person reads a code out of a
 * terminal that has no browser and approves it here, on a machine that has one.
 *
 * The screen asks for the code and nothing else. Approving is a write under
 * this browser's session, so what the machine at the other end collects is this
 * user's own token — which is why the page says whose it will be before the
 * button is pressed.
 */
export function Device({ name }: { name: string }) {
  const [parameters] = useSearchParams();
  const [code, setCode] = useState(parameters.get("code") ?? "");
  const [waiting, setWaiting] = useState<Waiting | null>(null);
  const [decided, setDecided] = useState<"approved" | "denied" | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);

  // A code in the address is one the person did not type, so it is read back
  // before anything is offered: a code that names no waiting login is worth
  // saying so about now rather than after a button.
  const given = parameters.get("code");
  useEffect(() => {
    if (!given) return;
    let current = true;
    void (async () => {
      const result = await api.GET("/api/device/logins/{code}", { params: { path: { code: given } } });
      if (!current) return;
      if (result.data) setWaiting(result.data);
      else setRefusal(describe(result.error, result.response.status));
    })();
    return () => { current = false; };
  }, [given]);

  async function decide(event: FormEvent, approve: boolean) {
    event.preventDefault();
    setRefusal(null);
    const result = approve
      ? await api.POST("/api/device/approvals", { body: { user_code: code } })
      : await api.POST("/api/device/refusals", { body: { user_code: code } });
    if (!result.response.ok) { setRefusal(describe(result.error, result.response.status)); return; }
    setDecided(approve ? "approved" : "denied");
  }

  if (decided) {
    return <AuthFrame>
      <h1 className="text-xl font-semibold">{decided === "approved" ? "That machine is signed in" : "That login was refused"}</h1>
      <p className="text-muted-foreground text-sm">{decided === "approved"
        ? <>It holds a token of yours now, and <code>ha token list</code> revokes it. You can close this page.</>
        : "Nothing was handed over. You can close this page."}</p>
    </AuthFrame>;
  }

  return <AuthFrame>
    <div>
      <h1 className="text-xl font-semibold">Sign a machine in</h1>
      <p className="text-muted-foreground mt-1 text-sm">
        Type the code <code>ha login</code> printed. Approving hands that machine a token of yours, as {name}.
      </p>
    </div>
    <form onSubmit={(event) => decide(event, true)} className="space-y-5">
      <Field label="Code"><Input id="code" autoComplete="off" autoFocus spellCheck={false} value={code} onChange={(e) => setCode(e.target.value)} /></Field>
      {waiting && <p role="status" className="text-muted-foreground text-sm">Begun {new Date(waiting.requested_at).toLocaleString()}, good until {new Date(waiting.expires_at).toLocaleTimeString()}.</p>}
      {refusal && <p role="alert" className="text-destructive text-sm">{refusal}</p>}
      <div className="flex gap-2">
        <Button type="submit" className="flex-1" disabled={!code}>Approve</Button>
        <Button type="button" variant="outline" className="flex-1" disabled={!code} onClick={(event) => void decide(event, false)}>I did not start this</Button>
      </div>
    </form>
  </AuthFrame>;
}
