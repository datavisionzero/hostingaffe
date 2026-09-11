# An Installation Depends on an Installation, and the Reverse Is Derived

An installation carries `depends_on`, a list of the installations it needs, on
its machine or on another one. What needs *it* is `needed_by`: the same rows read
from the other end, derived on read the way a version is derived from the
deployments, and never written. Both are one hop and no closure. The model, which
[Vision §7](../../Vision.md#7-domain-model) closed at two relationships, now has
three, and that is all it has.

## What forced it

[Vision §15.2](../../Vision.md#152-an-installation-depends-on-an-installation)
named this relationship, called it "the one relationship worth adding", and
deferred it on a condition: the MVP should first show whether the two built-in
ones carry the everyday questions. The first real host answered.

On a host with a shared reverse proxy — the ordinary way to run one — nine of ten
installations hang on the same Caddy. It is the only container holding 80 and
443; every other service sits on an internal Docker network and is reachable
through it and nowhere else. In the record that shows up as nine installations
whose ports are all `internal` and one with two public ones. **That a restart of
Caddy takes nine services with it is nowhere** — except in prose, in each of the
ten descriptions separately.

"What falls out if I touch this" is the question asked before every intervention,
and it is the one `ha machine context` exists to answer before an agent touches
anything. Two relationships that say what an installation runs *on* and what it
is an installation *of* do not answer it.

## The decision

**The dependent names its dependency.** `logaffe-prod depends_on caddy`, which is
the direction the word has in a Compose file, where anyone reading it has read it
before. The list is replaced whole, never patched entry by entry, like every
other list an installation carries.

**The reverse is derived and is called `needed_by`.** It is on the complete
installation and not on the slim summary, it is refused in a request body as
`unknown-field` with the reason `version` is refused, and nothing can write it
into disagreement with the dependencies it is read from. One edge, one writable
end.

**Across machines**, because §15.2 says so in as many words — Caddy on
`ingress-01` routing to payaffe on `docker-stage-01` — and because a proxy on its
own host is what the relationship is for. `ha machine context` names a dependency
that lies on another machine with that machine (`caddy (on ingress-01)`) and
carries nothing else of it: a bare key from another host is one the reader of
that document cannot look up in it, and a shared proxy must not drag seven
foreign records in behind it.

**One hop, never a closure.** Nothing computes what a dependency itself depends
on. A closure would list an installation under one it never named, which is the
second truth [Vision §7](../../Vision.md#7-domain-model) exists to avoid, and
both real cases are one hop anyway: nine services on one proxy, one application
on the database beside it. Where a second hop matters, the first one leads to it.

**A row, not a `text[]` of keys.** The entry is a foreign key like every other
relationship in the model. "Which installations depend on this one" — the
question the whole edge exists for — is then an index read rather than a scan
over arrays, and the database holds on to what the keys name.

## The alternatives

**A field on the file, naming a second installation it concerns.** Decided
already, and against:
[ADR 0010](./0010-a-file-has-one-owner-and-what-else-it-concerns-is-prose.md)
refused it partly *because* of this decision — "it is Vision §15.2 entered
through the side door" — and said that when the deferred relation arrived it
would explain the fragment along with the port and what breaks when the proxy is
down. This is that arrival. What it does not carry is the fragment's **path**:
that stays the sentence in the description, because a path is not a
relationship.

**A `needed_by` that is also written.** Two lists for one edge, and the first
pair that disagreed would leave nobody able to say which was right. Deriving it
costs one query against an index that exists for it.

**A transitive `depends_on`.** Rejected above; the record says what somebody
wrote down.

**Refusing cycles.** A graph walk on every write, buying something the product
never computes: it orders no startup, resolves no closure, and is a record rather
than an orchestrator (VISION 13). Two services that need each other exist. Only
`A depends_on A` is refused, as `validation` — that is not a relationship, it is
a typo.

**Adding it to the slim `InstallationSummary`.** A list stays slim (ADR 0012),
and the edge is read where an installation or a machine is read. On the web the
machine screen lists installations and the edge is one click away on each; in
`ha machine context` it is inline, because an agent reading that document has no
click.

## Consequences

- **A third table.** `installation_depends_on (installation_id, depends_on_id)`,
  the pair as the key, indexed on the target — which is the direction that is
  actually read.
- **An installation others depend on is not deleted**, and neither is a machine
  carrying one that installations elsewhere depend on: `transition`, with
  `dependents` saying how many, the way a software carrying installations is
  refused. An edge is not a possession, so sweeping it away with a cascade would
  take a fact out of somebody else's record. **Retiring keeps every edge** and is
  the normal end anyway.
- **The purge takes the edges before it takes the row**, the way it takes a
  page's anchor: an edge can only reach the purge from a dependent that was
  itself deleted, and it cannot name something that is gone for good.
- **A dependency may be `retired`, and briefly `deleted`.** Retired is
  information — something still points at what nobody runs. Deleted can only
  happen the long way round: a dependent is deleted, its dependency is then
  deleted too, and a restore brings the dependent back still naming it. The key
  is still shown; the purge resolves it.
- **The history records what the list became, and not what it was.** The row
  holds ids and the Domain resolves no keys; a list is replaced whole, so the
  previous history row carries the previous list (`docs/storage.md`).
- **The edge is not searched.** `ha search caddy` would otherwise answer with
  every installation that depends on it, and the key belongs to the installation
  that carries it.
- **`depends_on` is not a filter.** It is read whole where an installation or a
  machine is read, never as a query across the record.
- **The import sets it in a third pass**, after every installation exists, for
  the reason a machine's host waits for the second: a dependency may lie further
  down the same document.
- **`needed_by` is in an export and is read past on import**, beside `version`.
- **A machine-level dependency is still prose.** Two installations sharing an
  update and backup mechanism that is a systemd timer on the machine depend on a
  *file*, not on an installation. That is ADR 0010's ground, and it has not
  moved.
