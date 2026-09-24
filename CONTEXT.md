# The words

The glossary the code is named after. Every type, endpoint, CLI verb, column
and screen uses the words below, in the sense given here, without exception.
[Vision.md §7](Vision.md#7-domain-model) is where the model is decided; this
file is where it is written down for whoever is about to name something.

A concept that needs a word this file does not have gets settled in the vision
first. A type named after a word under
[Words we do not use](#words-we-do-not-use) is a naming error, not a matter of
taste.

## The model

Flat, and closed:

```
Instance
├── Machine            the computer
│   ├── File           host-level configuration
│   ├── Installation   one software installed once on one machine
│   │   ├── Deployment one version change
│   │   └── File       what the installation runs with
│   └── Report         what the machine said about itself, at a moment
├── Software           what an installation is an installation of
├── Provider           who supplies a machine's external hosting
├── Page               Markdown, on a machine, an installation or the instance
├── History            every change, written by the instance
└── Identity           user or agent
```

Four relationships are built in and no others: an installation is on a
machine, a virtual machine is on a host machine, an installation depends on
another installation, and a non-VM machine may be assigned to a provider.
The fourth replaces the former free-text `machine.provider` deliberately
([ADR 0020](docs/adr/0020-a-provider-is-a-record-and-a-vm-inherits-it.md)).

## Key

The handle an operator chooses for an entity: short, lowercase, immutable —
`caddy`, `ex44`, `docker-prod-01`, `logaffe-prod`. Keys are what appear in
commands, URLs and Markdown links.

A key is unique **per entity type across the instance**, not per parent. There
is one machine `caddy`, and naming it needs no parent. A machine `caddy` and a
software `caddy` may coexist, and usually will, because every command names the
type before the key. Where a key stands alone — a Markdown link, a search
result, a path in an export — it carries its type.

A deleted key stays reserved. An installation key can be released by the
separate, irreversible purge after deletion ([ADR 0019](docs/adr/0019-an-installation-key-can-be-released-explicitly.md));
machine, software and provider keys are never reused.

## Machine

A computer the operator pays for or owns and can log into. It exists whether or
not anything is installed on it.

| closed set | values |
|---|---|
| `kind` | `vps` · `dedicated` · `vm` · `local` |
| `arch` | `amd64` · `arm64` |
| `status` | `planned` · `active` · `retired` |
| `protocol`, `scope` (of a port) | see Port |
| `avatar` | `monkey` · `gorilla` · `sloth` · `raccoon` · `fox` · `owl` · `penguin` · `octopus` · `cat` · `frog` · `bear` · `wolf` · `lion` · `puma` · `robot` · `rack` · `turbo` · `tower` · `minipc` · `minimac` · `desktop` · `laptop` · `devbook` · `aibox` · `monitor` · `router` · `proxy` · `signpost` · `firewall` · `cloud` · `container` · `database` · `harddrive` · `bucket` · `floppy` · `tape` · `logbook` · `gauge` |
| `avatar_color` | `brown` · `slate` · `teal` · `orange` · `berry` · `sage` · `blue` · `red` · `mustard` · `lavender` |

`host` is set only on a `vm`, and names the machine it runs on. Hardware facts
are text rather than numbers, `arch` excepted: machines are compared by eye,
and "2×512G NVMe ZFS mirror" is truer than a number. `measured_at` says when
the facts were last verified — a machine nobody has looked at for a year says
so.

`provider` is an optional provider key on a non-VM machine. A `vm` has no
assignment of its own: its effective provider is derived through its `host`
chain. A host change therefore changes what the VM reports. `local` does not
imply a provider, and an unassigned machine remains a valid record.
`legacy_provider` is the exact pre-migration free-text value, read-only and
visible even when it differs from a VM's inherited provider. It is preserved
through later edits, deletion and restoration.

`ports` is the machine's own: what it listens on and no installation of it
answers to — SSH, a Wireguard endpoint, a provider's agent. It is the same field
an installation has, in the same shape (see Port). **An empty list says nothing
rather than "none":** the record holds no ports for this machine, and the drift
that reads it is simply not computed. Whoever writes one down switches that
comparison on and can clear every finding it makes.

`avatar` and `avatar_color` are the machine's picture: one of the drawings the
product ships with, in one of its colours. Both are optional, and a machine
without them is shown with a picture derived from its key — derived on every
read, never stored. **A picture is not a kind:** a `rack` avatar on a `local`
machine says nothing about the machine, it only makes it recognisable. The
avatar of a rack-mounted computer is called `rack` because "server" is a word
this product does not use (below). Whether a device is drawn with a face is a
person's setting in the web interface, not a field.

`last_seen` is derived and never written: the moment the instance received the
machine's latest report. It stands beside `measured_at` and answers a different
question — `measured_at` is when a person last checked the facts, `last_seen`
is when the machine last spoke for itself. A machine that has never reported
has none.

## Provider

The record of who supplies external hosting. Its immutable key identifies it;
`name` is its display name and `description` is editable Markdown. It carries
identity and change timestamps and history like a machine or software record.
Its machine list includes directly assigned machines and VMs whose host chain
leads to it. A provider used by any machine, including a deleted but restorable
one, cannot be deleted. A provider may be restored; deletion does not make its
key available again.

`emblem` and `emblem_palette` are the provider's picture (ADR 0022): one of the
geometric compositions the product ships with, in one of its palettes of three
colours. They behave like a machine's `avatar` and `avatar_color` — optional,
cleared by the empty word, and derived from the key on every read where they
are missing, never stored. **An emblem is not an avatar:** the two sets share
no word, and a machine is never drawn with an emblem nor a provider with an
avatar.

| closed set | values |
|---|---|
| `emblem` | `orbit` · `arch` · `peak` · `split` · `quarter` · `stack` · `wave` · `grid` · `target` · `bloom` · `eclipse` · `chevron` · `bridge` · `tiles` · `beam` · `steps` |
| `emblem_palette` | `bauhaus` · `ember` · `meadow` · `lagoon` · `dusk` · `citrus` · `orchid` · `granite` · `coral` · `glacier` |

## Software

What an installation is an installation of: `caddy`, `postgres`, `logaffe`. It
exists once per instance, so that "where is this running, and in which
versions?" has a screen. It carries no version — versions belong to
deployments.

`homepage` and `repository` are URLs. `image` is the name of a container image
**without a tag** — `caddy`, `ghcr.io/datavisionzero/logaffe` — and a `:tag` in
it is refused, because the tag belongs to the deployment.

**The word is uncountable.** The collection is `/api/software`, never
`/api/softwares`; the type is `Software`; and the plural is circumscribed
wherever it is needed — "the software rows", "every software". A `Softwares`
anywhere is a naming error.

## Installation

One software installed once on one machine. Two logaffe installations on one
host are two installations; Caddy on three hosts is three installations of one
software.

| closed set | values |
|---|---|
| `environment` | `production` · `staging` · `development` |
| `role` | `application` · `platform` |
| `status` | `planned` · `active` · `retired` |
| `backup` | `none` · `planned` · `active` |
| `monitoring` | `none` · `planned` · `external` |
| `logging` | `local` · `central` |

`environment` and `role` answer two different questions: whom an installation
serves, and what it is for the host. A Caddy fronting production and staging is
both `platform` and `production`.

`ports` is a list of Ports — see below; it is the machine's field too, in the
same shape. `urls` is a list of URLs.

`secrets` is a list of objects too — `{ "name": "POSTGRES_PASSWORD", "path":
"/opt/compose/logaffe/.env.runtime" }`. The **name** is a name and never a
value; `path` is the file on the machine the value lies in, absolute, and is
empty where nobody has decided yet. It reads and is typed as
`POSTGRES_PASSWORD@/opt/compose/logaffe/.env.runtime`, or as the bare name
([ADR 0011](docs/adr/0011-a-secret-is-a-row-that-says-which-file-it-lies-in.md)).
Where a value is kept *besides* the machine — a vault, a password manager —
is prose in the runbook. There is no field for how a secret is rotated.

**An installation has two directories.** `path` is where it lives on the
machine — the one it is deployed from, `/opt/compose/logaffe`, and the one every
file it owns lies under. `data` is where its persistent data lies,
`/srv/services/logaffe`: what a backup has to take and what a `docker compose
down -v` does not bring back ([ADR 0009](docs/adr/0009-an-installation-has-two-directories-and-data-is-the-second.md)).
Where a host keeps configuration and state in one directory, both say the same
thing, and nothing holds them apart.

**`depends_on` is what an installation needs** — a list of installation keys,
on this machine or on another one: the reverse proxy in front of it, the
database beside it. It is written from one end only. What needs *this*
installation is `needed_by`, derived on read from the installations that name it
and never written, so the two can never disagree
([ADR 0014](docs/adr/0014-an-installation-depends-on-an-installation-and-the-reverse-is-derived.md)).

It is **one hop**: nothing computes what a dependency itself depends on, and no
list ever carries an installation that did not name it. An installation does not
depend on itself; a longer cycle is not refused, because nothing here computes a
closure or a startup order. An installation others depend on is not deleted, and
neither is a machine carrying one they depend on — retiring is the normal end and
keeps every edge.

What is depended on is always an **installation**. Two installations sharing a
backup timer that lives on the machine depend on a file, and what a file concerns
is prose ([ADR 0010](docs/adr/0010-a-file-has-one-owner-and-what-else-it-concerns-is-prose.md)).

`version` is derived: the version of the installation's latest deployment.

## Port

One port something listens on, and how far it is reachable. It belongs to an
installation and to a machine alike — what an installation listens on is the
installation's, what belongs to the machine and to no installation of it is the
machine's — and it is the same field on both.

| closed set | values |
|---|---|
| `protocol` | `tcp` · `udp` |
| `scope` | `public` · `private` (the operator's network) · `loopback` (this machine only) · `internal` (never reaches the host) |

It is an object — `{ "port": 443, "protocol": "tcp", "scope": "public" }`. The
spelling `443/tcp:public` is what a person reads and types, and what a history
row carries; it is a rendering, not the field.

**A `scope` is not a Binding** (see Report). A scope is what an operator decided
a port is *for*: `public` and `private` reach beyond the machine, `loopback`
reaches this machine only, and `internal` never reaches the host. A binding is
what the socket says. The two are compared, never equated.

## Deployment

The record that an installation changed version. It has no status: a deployment
is recorded when it is done, and a rollback is a deployment to the previous
version with a note that says so.

`previous` and `files` are derived — the version before it, and the file
revisions that were current when the version went live. Everything derived is
ordered by `at`, never by the order of recording, so backfilling history never
moves the present. A deployment has no key: the instance numbers it per
installation.

## Report

What a machine said about itself at a moment. A collector on the host gathers
it and hands it in; the instance never reaches out to a machine.

A report **belongs to exactly one machine, has no key** — the instance numbers
it per machine, as it does a deployment — **and is never edited**. It is a
sample beside the record, never a field of it: a report sets nothing, and
whatever it says that the record contradicts is drift for a person or an agent
to resolve
([ADR 0015](docs/adr/0015-a-machine-reports-and-the-record-stays-written.md)).

What a report carries is a closed set of sections, each of which may be
missing:

| section | what is in it |
|---|---|
| `host` | hostname, os, kernel, arch, boot time, load 1·5·15 |
| `memory` | total, used, available, swap |
| `disks` | per real mount: mount, device, size, used, percent |
| `containers` | per container: name, image **with its tag**, state, status, health, restarts, started_at, ports, and how many of how many run |
| `listening` | per port and protocol: the port, `tcp` or `udp`, and the **binding** — and never a process |
| `updates` | whether the machine is waiting for a restart |
| `files` | per directory `files sync` wrote into: the installation, the directory, and a **digest** per path the manifest beside them claims — never a content |

Beside them: `collected_at`, the host's clock as it came; `received_at`, the
instance's clock, which is what the order and `last_seen` are read from; the
version of the `ha` that collected it; and `missing`, the sections the
collector could not determine, each with its reason. A host without Docker
reports no containers and says why.

A container's `image` carries its **tag**, where a software's `image` carries
none: the tag is what the report is compared against, and the software's
belongs to the deployment.

**Binding** is how far a socket is bound, and it has two values: `public`,
bound to a wildcard or to an address other machines can reach, and `loopback`,
reachable from this machine and nowhere else. It is deliberately **not**
`scope`, which is what an operator decided a port is *for*: `private` means
"from my own network", and that is a firewall's doing and invisible in a
listening socket; `loopback` means the host alone, while `internal` means no
host socket exists. One entry per port and protocol, and where a port is bound
to several addresses the widest binding wins.

The `listening` section carries **no process** — not the name, not the command
line, not the arguments. Another user's process is visible only to root, and
the collector needs none; what the section exists for is the comparison against
the `ports` of an installation and of the machine, and those are ports rather
than processes.

The `files` section is the one place a report is about the record rather than
about the machine alone, and it carries **digests and never contents**. What it
names is what the manifest `.ha-sync.json` claims — the paths `files sync` wrote,
which came out of the record in the first place — and nothing else that lies in
the directory. The directories are named on the cron's own command line, because
a machine token reads nothing and cannot be told which they are
([ADR 0017](docs/adr/0017-a-machine-reports-digests-and-the-instance-compares.md)).

The `updates` section says one thing: whether the machine is waiting for a
restart. How many packages have an update is not in it — counting them makes
the collector distribution-dependent, and a host whose package lists are weeks
old would report nothing pending and lie in the most comforting way there is.

**No secrets, ever** — no container environment, no process command lines, no
file contents. It is the rule the record already follows, and it holds here
because the material is the machine's own.

**Drift** is where the two sides disagree, computed on read and stored nowhere:
the tag of a container's image against the version of the installation's latest
deployment (`version`), an installation the record calls active whose container
is not running (`container`), `os` and `arch` against the machine's own fields
(`fact`), a port against the socket the machine has for it (`port`), and a file
of the record against the digest reported for the path sync wrote it to
(`file`). Every drift names both sides, what kind of thing the subject is —
`machine` or `installation` — and how old each side is; which of them is right
the product does not say. Where the assignment of a container to an installation is
ambiguous, nothing is claimed.

A `file` drift is the drift check for configuration: three findings, and one
non-finding. A file of the record whose digest on the host is another one; one
the record has that lies nowhere on the host; one that has left the record and is
still lying there. **A file sync never wrote is no finding** — it is what sync
never touches, and the report never named it. Both sides are named as digests,
because a file on a host carries no revision, and only the content is compared
and never the mode bit. **A report that names no directory hears nothing about
files**, the same way a machine that keeps no ports hears nothing about
undocumented ones.

A `port` drift reads both sides of the record — an active installation's `ports`
and the machine's own — against the `listening` section. **A port bound in
public that neither of them claims is a drift only where the machine keeps its
own ports**; where it keeps none, the record says nothing about them and the
comparison is not made, because a drift nobody can clear teaches people to stop
reading the list. That is the semantics of the field and not a suppression list:
no single port and no single finding is ever silenced.

Reports are swept after thirty days, except the latest of a machine, which is
kept however old it is.

## File

A UTF-8 text file a machine runs with, owned by exactly one installation or one
machine: a `compose.override.yml`, a Caddy fragment, a systemd unit, a `bin/`
script. `path` is relative and unique per owner.

`directory` is where the file lies on the machine — `/etc/systemd/system`,
absolute. A machine's file has one and an installation's has none: an
installation says once, in its own `path`, where all of its files lie, and a
machine has no single answer to give (ADR 0008). It is written down, not
written to: `files sync` takes an installation and refuses a machine.

Files are the one place the history keeps **content** rather than the fact of a
change, because rolling back a Compose file needs the previous Compose file.
Each write makes a `revision`, and two revisions can be diffed.

**A file has one owner even where two installations care about it.** On a host
with a shared reverse proxy, `sites/hostingaffe.caddy` lies under the proxy
installation's `path`, so it is the proxy's file and nothing else. The
installation it fronts names it in its **description**, which is the runbook —
the path, and a link to the proxy:

```md
The TLS endpoint is `sites/hostingaffe.caddy` in [caddy](installation:caddy).
```

There is no field for a second installation a file concerns. A relationship
between installations is `depends_on` on the installations, not a column here
([ADR 0010](docs/adr/0010-a-file-has-one-owner-and-what-else-it-concerns-is-prose.md),
[ADR 0014](docs/adr/0014-an-installation-depends-on-an-installation-and-the-reverse-is-derived.md)).
The edge carries the relationship; the fragment's **path** is still the sentence
above.

An owner is named by its kind and its key together — `{"kind": "machine", "key":
"ex44"}` — because a key is unique per entity type and not across them, and a
machine `caddy` and an installation `caddy` both exist. A page's `attached_to`
takes the same shape; the two field names stay what they are, because a file is
owned and a page is attached.

Paths that carry secrets are refused: `.env` and any `.env.*` but
`.env.example`, and anything under `secrets/`. So is any path outside the
owner's directory.

## Page

Markdown with a slug, attached to a machine, an installation, or to the
instance as a whole. Where everything goes that is longer than a description.

| closed set | values |
|---|---|
| `kind` | `runbook` · `decision` · `note` |

Pages are flat and addressed by their slug, which is their address and may be
renamed; nothing forwards afterwards. A slug is one segment and carries no
slash ([ADR 0003](docs/adr/0003-a-pages-slug-is-one-segment-not-a-path.md)).
`kind` says how a page is to be read and nothing more — a `decision` has no
status, no supersedes and no enforced template. `attached_to` names a machine
or an installation, or is empty for a page of the instance, and a page does not
follow its anchor into deletion.

A body names another thing of the record as a Markdown link whose target is a
scheme and an address — `[the runbook](page:backup-restore)`, and likewise
`machine:ex44`, `software:caddy`, `provider:example-host` and `installation:app-1`
([ADR 0007](docs/adr/0007-a-record-is-linked-from-markdown-as-a-scheme-and-a-key.md)).
That is the Key rule above written down: where a key stands alone it carries
its type. Nothing validates a body, so a reference that points at nothing is
stored like any other text.

## History

Every change to a machine, provider, software, installation, file or page: who, when,
which field, from which value to which, and the note that came with it. Written
by the instance, never edited, never deleted — not even with the thing it
describes.

Deployments are not history entries. They are records of their own, because a
deployment is what an operator wants to *read*, while the history is what they
consult when something looks wrong.

**Read across every subject, the two are one list.** "What happened lately" is
the history of everything at once, and it is served with the deployments mixed
in — a version that changed is the event somebody asking that question is
looking for. It is a reading and nothing else: no deployment becomes a history
row, nothing is written, and the rows one act wrote are folded into one event on
the way out (`docs/api.md`, The history).

**A report is not a history entry either.** The history is who changed the
record; a cron reporting every quarter of an hour has changed nothing, and a
report that wrote a row would bury every real change under ninety-six of them
a day. What *is* a history entry is issuing or revoking a machine's token —
that is a person changing the record.

## Identity

A **user** is a human; an **agent** is what work runs under. Both are
identities, and everything written is bound to one, so the history and every
deployment say who.

A **token** is a user's key to the CLI, or the agent itself. An agent token has
a name, is never an administrator, and administers no identities. Every token
reads everything and writes what it is told to: one instance holds one team's
infrastructure, and there is no role beyond the administrator who manages
users, agents and tokens.

A **machine token** is none of that. It belongs to one machine and can do one
thing: hand in a report for that machine. It reads nothing — no installation,
no file, no page, no report, not even its own machine — and it is **not an
identity**: no user, no agent, no role, absent from `ha me` and from every
history row. A report is attributed to the machine, and a machine is not a who
([ADR 0016](docs/adr/0016-a-machine-token-posts-one-report-and-reads-nothing.md)).
One per machine, hashed like every other token, issued and revoked by a person;
an agent may see that one exists and never issue or revoke one. Issuing and
revoking are history rows on the machine, because that is a person changing the
record.

A **device login** is one `ha login` in flight: the machine with no browser
holds a **device code** and polls with it, and the person reads out a **user
code** and approves it in a browser somewhere else
([ADR 0005](docs/adr/0005-ha-login-is-the-device-code-flow-and-the-session-lives-in-the-keychain.md)).
It is not a third kind of identity and produces no new kind of key: what it
hands over is the approving user's own token. "Session" stays the browser's —
the cookie a person signs in with — and is never what `ha` holds.

## Retired, and deleted

**Retired** is the normal end of a machine or an installation. A retired thing
keeps everything it had, so that "what did we run in 2026" stays answerable; it
leaves the default lists and stays reachable by key and in search.

**Deleted** is for mistakes — a machine created twice, a deployment recorded
with the wrong version. A soft delete, invisible everywhere at once, restorable
for a grace period, gone afterwards. Identities are deactivated and revoked,
never deleted.

What a deletion takes:

| deleting a | takes | and |
|---|---|---|
| machine | its files, its installations (with theirs), the vms it hosts, its reports and its token | its pages stay |
| provider | nothing | deletion is refused while any machine still refers to it |
| installation | its files, its deployments | its pages stay |
| software | nothing | it is **refused** while installations still hang on it, with a count |
| file | its revisions | |
| deployment | nothing | |

Reports and a machine's token are the one thing a cascade does not stamp: they
are reached only through the machine, so they are invisible while it is
deleted, come back with it, and are taken by the purge with the row
(`docs/storage.md`, Reports).

**A restore brings back what that deletion took, and nothing else.** Every row a
cascade touches carries the moment of the deletion, and a restore brings back
exactly the rows carrying it; a file deleted on its own the week before stays
deleted.

**A page does not follow its anchor.** It survives the deletion still naming
what it hung on, so that restoring the machine restores the whole picture. Only
the purge unhooks it, and it becomes a page of the instance.

**The history survives everything, the purge included** — the history of a
deleted machine still says that it existed and when it went. A key is written
into a register when given out. A normal delete and the automatic purge keep it
reserved; only the explicit purge of a deleted installation releases its key.

## Words we do not use

| not this | this, and why |
|---|---|
| **service** | **installation**. systemd and Compose both mean something else by "service", and a runbook that says "restart the service" would be ambiguous in a product that also has a systemd unit as a file. |
| **project** | nothing. There are no projects. One instance holds one team's infrastructure and every user sees all of it. |
| **host** | **machine**, except in one place: the `host` of a `vm` is the machine it runs on. "Host" as a synonym for machine is what the word does everywhere else, and that is exactly why it needs the narrow sense here. |
| **server** | **machine**. A machine may be a VPS, a dedicated box, a VM or the computer under a desk, and "server" reads as only the first two. |
| **metric** | **report**. A report is a sample somebody may read, not a number kept for a graph, and the moment there are metrics there are thresholds and alerts, which this product does not have. |
| **heartbeat** | **report**. The sign of life is a consequence of a report arriving, not a thing of its own, and there is no second, smaller message beside it. |
| **monitoring** | the name of an installation's decision field, `none · planned · external`, and nothing else. It never becomes the name of the area reports live in, and no `internal` is added to it. |
| **issue**, **epic**, **release**, **label**, **claim** | nothing. They are planaffe's ticket model, which this product was cut free of. A type of one of those names here means something was copied that should not have been. |

`ticket` survives as a field on a deployment, and it is a planaffe key like
`LOG-42` — a reference to the other product, not a concept of this one.
