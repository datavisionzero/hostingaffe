# A Machine Token Posts One Report and Reads Nothing

A machine may hold a token, which is the exception to
[Vision §9](../../Vision.md#9-users-and-permissions)'s "no token lives on a
machine". It belongs to exactly one machine and can do exactly one thing: hand
in a report for that machine. It reads nothing — no installation, no file, no
page, no report, not even its own machine — and it is not an identity.

## What the rule actually said

§9 did not refuse a token on a host for the sake of the refusal. It gave a
reason: "a token stored on a machine would hand whoever takes that machine the
map of every other one, and a read-only token would not change that — it still
reads everything." The map is the thing being protected, and this product's
content *is* the map — configuration, ports, addresses, where every secret
lies ([Vision §10](../../Vision.md#10-reachable-from-the-internet-and-what-that-means-here)).

So the rule is tested against its own criterion rather than repeated. A token
that reads nothing hands over no map. What somebody who takes the machine gains
is the ability to lie about the machine they already hold — which they had
before the token existed, because they hold the machine.

The principle is kept and narrowed to what it was defending: **no token that
reads lives on a machine.**

## The decision

**One machine, one verb.** The token authenticates exactly
`POST /api/machines/{key}/reports`, and the `{key}` must be its own machine.
Every other endpoint refuses it, reads included, and the refusal does not
distinguish "there is no such machine" from "that one is not yours".

**Not an identity.** It is not a user and not an agent, it has no name of its
own beyond its machine, it carries no role, it appears in no `ha me`, and
nothing it does is attributed to a who. That follows from
[ADR 0015](./0015-a-machine-reports-and-the-record-stays-written.md): a report
changes nothing in the record, so there is nobody to hold responsible for a
change. A report is attributed to the **machine**, and a machine is not a who.

**Issued and revoked by a person.** An agent may read that a token exists, when
it was issued and when it was last used — none of which is a secret — and may
neither issue nor revoke one. That is the line hostingaffe inherited with
planaffe's
[ADR 0015](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0015-a-token-is-an-agent-or-a-users-key-and-an-agent-is-never-an-administrator.md):
an agent administers no identities, and keys least of all.

**One per machine, rotatable.** Issuing over an existing token is refused
unless the call says it is rotating, and then the old one is revoked in the
same move, so that the cron on the host fails visibly at its next run rather
than quietly continuing to work on a token somebody meant to replace. A revoked
token stays as a row, so that "there was one, and who took it back when" has an
answer.

**Stored hashed**, with the prefix convention the instance's other tokens
already use, and never travelling over plain HTTP off loopback
([ADR 0006](./0006-a-token-never-travels-over-plain-http-off-loopback.md)).

## The alternatives

**An ordinary agent token on the host, in a file.** It reads everything, so a
compromised host is the map of every other one. This is precisely what §9
refused, and nothing about a report makes it less true.

**A machine-scoped token that reads its own machine.** What
[Vision §17](../../Vision.md#17-open-points) sketched. It gives away more than
a report needs — the installations, their ports, the paths their secrets lie in
— for no gain at all, since a collector reads the host and not the record.
Refused here, and left open there for `files sync`, which is the case that
genuinely needs reading.

**No token, and the instance pulls over SSH.** Then hostingaffe holds
credentials for every machine and logs into them, which is a different product
with a much worse day when it is compromised, and it is discovery
([Vision §5](../../Vision.md#5-non-goals-deliberate-boundaries)).

**A shared token for all machines.** One secret on every host, and the first
compromised host lets an attacker report about all of them. The per-machine
binding is the whole point: what is stolen is scoped to what was already taken.

## Consequences

- **A stolen machine token can lie about its machine**, and that is the worst
  it can do: false disk figures, false containers, a sign of life for a machine
  that is not running. It is also the reason a report never touches the record
  ([ADR 0015](./0015-a-machine-reports-and-the-record-stays-written.md)) — the
  lie stays in the sample, where a person comparing the two sides can see it.
- **`last_used_at` is written on every delivery**, one write per report per
  machine, which is the only way to see afterwards whether a token is still in
  use.
- **Issuing and revoking are history rows on the machine.** The report is not
  (ADR 0015); the key that lets a machine report is, because a person changed
  the record.
- **The secret is shown exactly once**, in the answer to the issuing call.
  There is no endpoint that reads it back, because only the hash is kept.
- **`files sync` from the host is not solved by this**, and
  [Vision §17](../../Vision.md#17-open-points) still carries it.
