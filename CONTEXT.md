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
│   └── Installation   one software installed once on one machine
│       ├── Deployment one version change
│       └── File       what the installation runs with
├── Software           what an installation is an installation of
├── Page               Markdown, on a machine, an installation or the instance
├── History            every change, written by the instance
└── Identity           user or agent
```

Two relationships are built in and no others: an installation is on a machine,
and a virtual machine is on a host machine.

## Key

The handle an operator chooses for an entity: short, lowercase, immutable —
`caddy`, `ex44`, `docker-prod-01`, `logaffe-prod`. Keys are what appear in
commands, URLs and Markdown links.

A key is unique **per entity type across the instance**, not per parent. There
is one machine `caddy`, and naming it needs no parent. A machine `caddy` and a
software `caddy` may coexist, and usually will, because every command names the
type before the key. Where a key stands alone — a Markdown link, a search
result, a path in an export — it carries its type.

A deleted key is never reused.

## Machine

A computer the operator pays for or owns and can log into. It exists whether or
not anything is installed on it.

| closed set | values |
|---|---|
| `kind` | `vps` · `dedicated` · `vm` · `local` |
| `arch` | `amd64` · `arm64` |
| `status` | `planned` · `active` · `retired` |

`host` is set only on a `vm`, and names the machine it runs on. Hardware facts
are text rather than numbers, `arch` excepted: machines are compared by eye,
and "2×512G NVMe ZFS mirror" is truer than a number. `measured_at` says when
the facts were last verified — a machine nobody has looked at for a year says
so.

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
| `monitoring` | `none` · `external` |
| `logging` | `local` · `central` |
| `protocol` (of a port) | `tcp` · `udp` |
| `scope` (of a port) | `public` · `private` · `internal` |

`environment` and `role` answer two different questions: whom an installation
serves, and what it is for the host. A Caddy fronting production and staging is
both `platform` and `production`.

`ports` is a list of objects — `{ "port": 443, "protocol": "tcp", "scope":
"public" }`. The spelling `443/tcp:public` is what a person reads and types, and
what a history row carries; it is a rendering, not the field. `secrets` is a
list of secret **names**, never values. `urls` is a list of URLs.

**An installation has two directories.** `path` is where it lives on the
machine — the one it is deployed from, `/opt/compose/logaffe`, and the one every
file it owns lies under. `data` is where its persistent data lies,
`/srv/services/logaffe`: what a backup has to take and what a `docker compose
down -v` does not bring back ([ADR 0009](docs/adr/0009-an-installation-has-two-directories-and-data-is-the-second.md)).
Where a host keeps configuration and state in one directory, both say the same
thing, and nothing holds them apart.

`version` is derived: the version of the installation's latest deployment.

## Deployment

The record that an installation changed version. It has no status: a deployment
is recorded when it is done, and a rollback is a deployment to the previous
version with a note that says so.

`previous` and `files` are derived — the version before it, and the file
revisions that were current when the version went live. Everything derived is
ordered by `at`, never by the order of recording, so backfilling history never
moves the present. A deployment has no key: the instance numbers it per
installation.

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
`machine:ex44`, `software:caddy` and `installation:app-1`
([ADR 0007](docs/adr/0007-a-record-is-linked-from-markdown-as-a-scheme-and-a-key.md)).
That is the Key rule above written down: where a key stands alone it carries
its type. Nothing validates a body, so a reference that points at nothing is
stored like any other text.

## History

Every change to a machine, software, installation, file or page: who, when,
which field, from which value to which, and the note that came with it. Written
by the instance, never edited, never deleted — not even with the thing it
describes.

Deployments are not history entries. They are records of their own, because a
deployment is what an operator wants to *read*, while the history is what they
consult when something looks wrong.

## Identity

A **user** is a human; an **agent** is what work runs under. Both are
identities, and everything written is bound to one, so the history and every
deployment say who.

A **token** is a user's key to the CLI, or the agent itself. An agent token has
a name, is never an administrator, and administers no identities. Every token
reads everything and writes what it is told to: one instance holds one team's
infrastructure, and there is no role beyond the administrator who manages
users, agents and tokens.

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
| machine | its files, its installations (with theirs), the vms it hosts | its pages stay |
| installation | its files, its deployments | its pages stay |
| software | nothing | it is **refused** while installations still hang on it, with a count |
| file | its revisions | |
| deployment | nothing | |

**A restore brings back what that deletion took, and nothing else.** Every row a
cascade touches carries the moment of the deletion, and a restore brings back
exactly the rows carrying it; a file deleted on its own the week before stays
deleted.

**A page does not follow its anchor.** It survives the deletion still naming
what it hung on, so that restoring the machine restores the whole picture. Only
the purge unhooks it, and it becomes a page of the instance.

**The history survives everything, the purge included** — the history of a
deleted machine still says that it existed and when it went. So does the key:
it is written into a register when it is given out, and a key is never given out
a second time, not even after the purge has taken the row that held it.

## Words we do not use

| not this | this, and why |
|---|---|
| **service** | **installation**. systemd and Compose both mean something else by "service", and a runbook that says "restart the service" would be ambiguous in a product that also has a systemd unit as a file. |
| **project** | nothing. There are no projects. One instance holds one team's infrastructure and every user sees all of it. |
| **host** | **machine**, except in one place: the `host` of a `vm` is the machine it runs on. "Host" as a synonym for machine is what the word does everywhere else, and that is exactly why it needs the narrow sense here. |
| **server** | **machine**. A machine may be a VPS, a dedicated box, a VM or the computer under a desk, and "server" reads as only the first two. |
| **issue**, **epic**, **release**, **label**, **claim** | nothing. They are planaffe's ticket model, which this product was cut free of. A type of one of those names here means something was copied that should not have been. |

`ticket` survives as a field on a deployment, and it is a planaffe key like
`LOG-42` — a reference to the other product, not a concept of this one.
