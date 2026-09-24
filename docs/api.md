# The HTTP API

One API, for the web application, the CLI and whatever an agent writes. It
carries no version in its path: the contract is
[`api/openapi.json`](api/openapi.json), served at `/api/openapi/v1.json`,
captured from a running instance and checked in, and both clients are generated
from it (planaffe ADRs 0005, 0011).

The endpoint tables below cover what an instance serves.

## Where it is

Every endpoint is under `/api`, and every other address on the instance belongs
to the web application ([ADR 0002](adr/0002-the-api-lives-under-api.md)). The
two share a process and a port, and without the prefix they would share words:
`/pages` is a screen a person bookmarks and a collection a client reads, and
`machines`, `installations` and `deployments` are next. The paths below are
written out, prefix and all, because that is what an instance answers.

## Conventions

- **JSON in, JSON out**, `snake_case` on the wire. An enum travels as the name
  the contract spells, never as a number, so a value outside the set is refused
  at the door rather than stored as a row nobody can read.
- **Every timestamp is RFC 3339** in UTC, to the microsecond.
- **A closed request object refuses a field it does not define** —
  `unknown-field`, rather than silently ignoring what somebody meant.
- **Authentication is a bearer token or the browser's session cookie.** The
  server tells a user token from an agent token; nothing else does. Three
  endpoints are outside the door: `GET /api/version`, and the two halves of the
  device login, which exist to turn no credential into one.

## Errors

Every refusal is one `application/problem+json` document, written in one place:

```json
{
  "type": "/problems/not-found",
  "title": "Nothing by that key or id",
  "status": 404,
  "detail": "No page architecture.",
  "instance": "/api/pages/architecture"
}
```

`type` is what a client switches on. Some codes carry extra members —
`restorable_until` on `deleted`, `current` on `stale`, `errors` mapping field
to message on `validation`.

| code | status | |
|---|---|---|
| `validation` | 400 | a field is missing, malformed or over its limit |
| `unknown-field` | 400 | the request contains a field this object does not define |
| `cursor-invalid` | 400 | the cursor does not fit this request |
| `unauthenticated` | 401 | no token, an unknown token, or a revoked one |
| `csrf` | 403 | a browser write without its CSRF proof |
| `forbidden` | 403 | the identity may not do this |
| `not-found` | 404 | nothing by that key or id |
| `deleted` | 404 | it exists, in its grace period; `restorable_until` says how long |
| `idempotency-mismatch` | 409 | the key was used for a different request |
| `email-exists` | 409 | the address already belongs to a user |
| `last-administrator` | 409 | this would leave the instance with none |
| `device-pending` | 400 | nobody has approved that device login yet; keep polling |
| `device-denied` | 400 | a user refused that device login |
| `device-expired` | 400 | nobody approved it in time, or its token was already collected |
| `secret-expired` | 410 | a one-time link, spent or expired |
| `stale` | 412 | `If-Match` did not match; `current` carries the object |
| `transition` | 422 | the object's state does not allow the act — restoring what is not deleted, deleting a software that still has installations (`installations` says how many), deleting an installation or a machine that others depend on (`dependents` says how many) |
| `smtp-not-configured` | 422 | the act needs a mail and the instance sends none |
| `too-large` | 413 | the body is over what that endpoint takes; `limit` says what that was |
| `rate-limited` | 429 | that arrived again too soon; `retry_after` says in how many seconds |
| `internal` | 500 | a bug; the document carries nothing else |

### Exit codes of the CLI

`ha` derives its exit code from the status and the code above, so that a script
branches on a number. The table is in [`cli.md`](cli.md), and it is the same
table read from the other end.

## Idempotency

Every write may carry `Idempotency-Key`. The instance stores the answer for
twenty-four hours under the key and the identity that sent it: a repeat of the
same request replays that answer with `Idempotent-Replayed: true` and creates
nothing; the same key with a different body is `idempotency-mismatch`. Keys of
different identities never meet, and a refusal is stored and replayed like a
success — a client that retries a `validation` gets the same sentence, not a
second attempt.

`ha` generates one per invocation and numbers it per write, so retrying a
command that wrote several times replays each request rather than repeating
it.

## The note beside a change

Every write takes `note`, a **query parameter**, and what it says is written
into the history rows that write produces (ADR 0004):

```
POST   /api/machines?note=replacing%20the%20old%20ex44
PATCH  /api/installations/logaffe-prod?note=moved%20to%20the%20new%20proxy
DELETE /api/software/nginx?note=never%20actually%20installed
POST   /api/machines/ex44/restore?note=deleted%20by%20mistake
```

It is a query parameter and not a member of the request body because two of the
four writes have no body, and because the body objects are the record itself —
`CreateMachineRequest` is also the shape `ha export` writes and
`ha machine add --file` reads, and a note about an act does not belong in it.

A note is **one line of at most 500 characters**, trimmed; an empty one is the
same as none, and a longer one is `validation` naming `note`. A change that
touches three fields writes the note on all three rows: the note belongs to the
act, and the act wrote three rows. What a cascade writes keeps its own note —
deleting a machine says `with machine caddy` on everything it takes, and the
caller's note goes on the machine's own row.

## Guarding a write

A record two writers share is guarded by `If-Match`, carrying what the caller
last read, quoted. **What it carries depends on what the record keeps.**

A file is numbered — every write is a revision and every revision is still
readable — so its entity tag is that number:

```
PUT /api/installations/logaffe-prod/files/compose.override.yml
If-Match: "7"
```

Everything else keeps only the moment it last moved, so its entity tag is
`updated_at`:

```
PATCH /api/pages/backup-restore
If-Match: "2026-09-06T09:12:44.518273Z"
```

**What has revisions is guarded with the revision; what has none is guarded
with the stand.** A page is not numbered on purpose: a counter with nothing
behind it would let `?revision=2` be asked for and not answered, and the same
spelling with an unequal promise is worse than two spellings with a reason
(ADR 0004 is the note; this is the guard).

A write over a version somebody else has moved is `stale`, and the refusal
carries the object as it now stands in `current`, so the caller can merge
rather than lose what it typed. Without the header the write goes through: the
guard is offered, not imposed.

## Who may do what

The line of [VISION 9](../Vision.md#9-users-and-permissions):

- Every identity reads everything and writes content. One instance holds one
  team's infrastructure, and there are no scopes inside it.
- **An agent administers no identities** (planaffe ADR 0015). `/api/users`,
  `/api/agents` and `/api/tokens` under an agent token are `forbidden`, and an agent
  token never carries the administrator role.
- An administrator invites users, grants and revokes the administrator role,
  deactivates and reactivates, and configures the instance.
- **A machine token is not an identity and reads nothing.** It authenticates
  one call — handing in a report for its own machine — and every other endpoint
  refuses it, reads included. A user token and an agent token are refused at
  that one call in turn, so the two doors never fall through to each other
  ([ADR 0016](adr/0016-a-machine-token-posts-one-report-and-reads-nothing.md)).

## Retiring, and deleting

Two different ends, and telling them apart is the point (VISION 7).

**Retiring is the normal end.** `status=retired` on a machine or an installation
keeps everything it had — its installations, files, deployments and history are
untouched — and it leaves the default list while staying reachable by its key:

```
GET /api/machines                  # what is still there
GET /api/machines?retired=true     # and what has been retired as well
GET /api/machines?status=retired   # exactly the retired ones
GET /api/machines/ex44             # a retired machine, by its key, as always
```

**Deleting is for mistakes**, and follows planaffe ADR 0013: a soft delete,
invisible everywhere at once, restorable for a grace period, removed for good
afterwards, and open to agents because the grace period is the safety net.
`DELETE` answers `204`, `POST …/restore` answers the object, and reading a
deleted one is `deleted` with `restorable_until`.

| deleting a | takes with it | and |
|---|---|---|
| machine | its files, its installations (with theirs), the vms it hosts | its pages stay |
| installation | its files, its deployments | its pages stay |
| software | nothing | **refused** while installations hang on it; `installations` counts them |
| file | its revisions | |
| deployment | nothing | its number is not handed out again |

**A restore brings back what that deletion took**, and nothing else: every row a
cascade touches carries the moment of the deletion, and a file deleted on its
own the week before stays deleted.

**A page does not follow its anchor.** It survives still naming what it hung on,
so that restoring a machine restores the whole picture; only the purge unhooks
it, and the page becomes a page of the instance.

**The history survives everything, the purge included.** A key is written into
a register when it is given out. Machine, software and provider keys stay reserved; an
installation key stays reserved unless its owner is explicitly purged after
deletion ([ADR 0019](adr/0019-an-installation-key-can-be-released-explicitly.md)).

## Endpoints

### The instance and the caller

| | |
|---|---|
| `GET /api/version` | the instance's version; the one endpoint outside the door |
| `GET /api/me` | who the token says you are |
| `PATCH /api/me`, `POST /api/me/password`, `POST /api/me/email` | your own name, password, address |
| `PATCH /api/me/metadata` | what an agent reports about itself |
| `GET /api/admin/smtp`, `POST /api/admin/smtp/test` | whether mail is configured, and one test mail |

### Sessions

| | |
|---|---|
| `POST /api/session` | sign in with name and password; sets the browser's cookie |
| `POST /api/session/bootstrap` | exchange the bootstrap token for a session, once |
| `DELETE /api/session` | sign out |
| `GET /api/sessions`, `DELETE /api/sessions/{id}` | your browser sessions, and ending one |

### The device login

How `ha login` signs a person in on a machine with no browser
([ADR 0005](./adr/0005-ha-login-is-the-device-code-flow-and-the-session-lives-in-the-keychain.md)):
an SSH session on a rented box, a CI job, a container, an agent's sandbox.

| | |
|---|---|
| `POST /api/device/logins` | begin one; no token. A device code for the client, a user code for the person |
| `POST /api/device/tokens` | poll; no token. The user token once somebody approved, a code that says why not until then |
| `GET /api/device/logins/{code}` | what is waiting behind a code, for the screen about to approve it |
| `POST /api/device/approvals`, `/api/device/refusals` | a signed-in user approves one, or says they did not start it |

1. `POST /api/device/logins` answers a **device code** the client keeps and a
   **user code** it prints, with `verification_uri`, `expires_in_seconds` and
   `interval_seconds`. The user code is eight consonants as `XXXX-XXXX`: no
   vowel, so it is never a word, and no digit, so none of `0/O`, `1/I`, `5/S`
   or `2/Z` has a second half to be confused with. The credential is the device
   code and it is 256 bits; the row keeps its hash and never the code.

   **`verification_uri` and `verification_uri_complete` are relative to the
   instance** — `/device` and `/device?code=XXXX-XXXX`. A client resolves them
   against the address it just called, which it has; the instance does not,
   because it stands behind a proxy and would have to be told its own public
   name to build one. A client that prints one to a person joins the two halves
   first: a path with no host is not something anybody can open.

2. A user opens `/device` in a browser on any machine, types the code and
   approves. That is a write under the browser's session, so the token the
   waiting machine collects is that user's own — and an agent is `forbidden`
   there, because an agent administers no identities (planaffe ADR 0015).

3. `POST /api/device/tokens` answers the token once somebody has approved.
   Until then it refuses, and **which refusal it is, is the whole protocol**:
   `device-pending` means keep polling; `device-denied`, `device-expired` and
   `not-found` mean stop.

A device code hands over one token and never a second: the poll that collects it
claims the row in the same transaction, so one left behind in a CI log is worth
nothing to whoever finds it. A login lives ten minutes, and an approval nobody
collected in time is expired rather than approved.

### Users, agents and tokens

| | |
|---|---|
| `GET /api/users`, `POST /api/users` | the humans; creating one sends an invitation |
| `PATCH /api/users/{id}` | the administrator role |
| `POST /api/users/{id}/invitation` | send the invitation again |
| `POST /api/users/{id}/deactivate`, `/reactivate` | identities are never deleted |
| `POST /api/invitations/accept` | set a password and become active |
| `POST /api/password-recovery`, `/api/password-recovery/complete` | the link, and what it leads to |
| `POST /api/email-changes/confirm` | confirm a new address |
| `GET /api/agents`, `POST /api/agents` | agents; creating one prints its token once |
| `PATCH /api/agents/{id}`, `DELETE /api/agents/{id}` | rename, and revoke the token |
| `GET /api/tokens`, `POST /api/tokens`, `DELETE /api/tokens/{id}` | your own user tokens |

### Machines

| | |
|---|---|
| `GET /api/machines` | every machine as a slim `MachineSummary`, by key; `status`, `kind`, effective `provider` and `activity` |
| `POST /api/machines` | `key` and `kind` are required, everything else may arrive later |
| `GET /api/machines/{key}` | the complete machine |
| `PATCH /api/machines/{key}` | any field but the key; `If-Match` guards it |
| `GET /api/machines/{key}/context` | everything recorded about it, as one Markdown document |
| `GET /api/machines/{key}/history` | who changed what, oldest first |
| `DELETE /api/machines/{key}`, `POST /api/machines/{key}/restore` | soft, with the cascade above |
| `GET /api/machines/{key}/token` | whether it has a machine token, and what became of the last one |
| `POST /api/machines/{key}/token` | issue one; `rotate=true` replaces an existing one |
| `DELETE /api/machines/{key}/token` | revoke it |

**`activity` says what has been going on, per row.** It is a window — a count
of hours or days, `24h`, `7d`, at most `90d` — and every summary then carries an
`activity` beside its fields:

```json
{"window": "7d", "changes": 4, "deployments": 1,
 "latest": {"installation": "logaffe-prod", "number": 7, "version": "0.5.0",
            "previous": "0.4.1", "at": "2026-09-18T19:12:04.118231Z"},
 "installations": 7, "drift": 2}
```

`changes` and `deployments` are the events of The history below, counted over
the same reading a screen would show — what hangs on that machine, not what
names it — so a count and the list beside it can never disagree. `latest` is the
newest deployment on the machine **whenever it was**: a window that hid it would
answer "nothing" where the truth is "nothing for six months", which is the more
useful sentence. `installations` counts the active ones, and `drift` is how many
findings the machine's latest report makes against the record (Drift, below); a
machine that has never reported has none.

**It is asked for and never served by default.** Every number in it costs
something the plain list does not pay, the drift most of all: it reads the
latest report of every machine against that machine's installations. Fifty
machines with an installation, a deployment and a report each answer in about a
quarter of a second, which is what a screen of tiles can pay and a list somebody
opens to find a key should not.

The key is the address and is **immutable**: `key` in a change body is
`unknown-field`, not a rename. Both request objects are closed — a field they
do not define is refused rather than ignored.

**A change says what changes.** A field left out of `PATCH` stays as it is, and
**the empty string clears a text field**: `{"location": ""}` empties it,
`{"location": "fsn1-dc14"}` sets it, and a body without `location` leaves it
alone. `kind`, `arch`, `status` and `measured_at` are set, never cleared — a
closed set has no empty value to send, and a measurement is corrected by
measuring again.

`host` is a `vm`'s only, and names the machine it runs on; on any other kind it
is refused. A machine is not its own host, and a chain of hosts that would close
on itself is refused. A machine that stops being a `vm` loses its host, and the
history says so.

`provider` names a live provider key on a non-VM machine. An empty string
clears the assignment; a missing or deleted key is `validation`. A VM cannot
receive a direct assignment. Its `provider` in complete and summary reads is
derived through its host chain, so changing the host or its provider changes
the VM's effective value. `GET /api/machines?provider=KEY` filters on that
effective value and therefore includes VMs. `legacy_provider` on a complete
machine is the exact read-only value carried over from the former free-text
field, including any VM value that disagrees with its host. It is visible in
the machine context document too. Writes cannot set it.

`status` is `planned`, `active` or `retired` and defaults to `active`: a record
is usually made for a machine that already exists, and `planned` is the case a
caller states. What retiring does and what deleting takes is above, under
Retiring, and deleting.

Hardware facts are text, `arch` excepted, and each is one line of at most 200
characters. What is longer than that is the `description`, or a page.

**`avatar` and `avatar_color` are the machine's picture** (ADR 0021): a word of
each closed set, listed in `CONTEXT.md` and in the contract's `Avatar` and
`AvatarColor` schemas. Unlike `kind` and `arch` they can be cleared, so a write
sends them as words and the empty string clears them, like a text field; a word
outside the set is `validation` on the field it was sent in. Both are optional,
and a machine without them reads `null` — the picture a screen shows instead is
derived from the key there and never written back. Every machine read carries
them: the complete machine, the list, the hosting map and the installation map.

**`ports` is what the machine itself listens on** and no installation of it
answers to: SSH, a Wireguard endpoint, a provider's agent. It is the same
`Port` an installation carries — `{ "port": 22, "protocol": "tcp", "scope":
"public" }` — and the same rules hold: a list left out stays as it is, an empty
list clears it, and two entries with the same number and protocol are refused
whatever their scopes say. **An empty list says nothing rather than "none"**:
the record holds no ports for this machine, and the drift that reads it is not
computed. Under Drift below is what writing one down switches on.

**The machine token is the key a host reports under** and can do nothing else
([ADR 0016](adr/0016-a-machine-token-posts-one-report-and-reads-nothing.md)).
`POST` answers `{ "machine", "prefix", "secret", "issued_at" }`, and **the
secret is in that answer and nowhere afterwards** — only the hash is kept, so
whoever loses it issues a new one. A machine that already has a token is
`transition` unless the call says `rotate=true`; rotating revokes the old one
in the same move, so the cron on the host fails visibly at its next run instead
of quietly carrying on under a key somebody meant to replace.

**Issuing and revoking are a user's acts**, `forbidden` for an agent, which
administers no keys. Reading is not: `GET` carries no secret, only `present`,
the prefix, who issued it and when, and when it was last used — and where there
is no live token it describes the last one there was, so that "is this machine
still reporting, and is that the token's doing" has one answer. Both writes
**make a history row on the machine**, which is the one thing around reports
that belongs in the history: a person changed what the machine may do. The
report itself stays out
([ADR 0015](adr/0015-a-machine-reports-and-the-record-stays-written.md)).

**`context` is the one call an agent makes before it touches a host.** It
answers `{ "key", "document" }`, and the document is Markdown, in this order:
the machine and its fields; **what it last reported** and every drift; its
installations, each with the version it runs, its ports, its file list and the
last five deployments; the software those are installations of; the machine's
own files; the pages that hang on the machine or on any of its installations;
and the instance's `decision` pages — the rules that hold on every host.

**The report comes second, and short.** An agent about to type `docker compose
up` has to read it before, not after: when the machine last reported, the disks
in one line, how many containers run of how many, every container that is *not*
running, and the drift. What stays out is the full container table, memory and
load in detail, and every older report — one `ha report show` away, and exactly
the sort of content that fills a context window without changing a decision. A
report **older than a day** is given with its age and said not to be the
present, because an agent concluding from a three-week-old report what runs now
is worse off than one that knows nothing. A machine that has never reported gets
a sentence rather than an empty section.

**File contents are not in it.** They are one read of a file away, and they are
what would fill a context window. The measure is VISION 16: well under ten
thousand tokens for a machine with five installations, which the integration
tests hold to by counting characters rather than by estimating.

The document is assembled here rather than by the caller, and that is a
deliberate exception to what this document otherwise avoids — describing
presentation. A client that built it would ask about thirty times for a host of
that size, and the web application's machine screen is the same assembly; one of
them is what keeps the two saying the same thing.

### Providers

| | |
|---|---|
| `GET /api/providers` | live providers as slim `ProviderSummary` rows, by key |
| `POST /api/providers` | create a provider; `key` is required |
| `GET /api/providers/{key}` | name, Markdown description, emblem, authors and timestamps |
| `PATCH /api/providers/{key}` | change name, description or emblem; `If-Match` guards it |
| `GET /api/providers/{key}/history` | its changes, oldest first |
| `DELETE /api/providers/{key}`, `POST /api/providers/{key}/restore` | soft delete and restore |

A provider key is immutable and remains reserved after deletion. Unknown
fields are refused, as on other records. Changes and lifecycle acts write
history, and description changes record that the text changed without copying
its content into history. `GET /api/machines?provider=KEY` lists its machines,
including VMs by inherited provider. A provider cannot be deleted while any
machine still refers to it, even if that machine is deleted but restorable.
An agent may read and write providers with the same authorization as machines.

**`emblem` and `emblem_palette` are the provider's picture** (ADR 0022): a word
of each closed set, listed in `CONTEXT.md` and in the contract's `Emblem` and
`EmblemPalette` schemas, written and cleared exactly like a machine's `avatar`
and `avatar_color` — the empty string clears them, and a word outside the set is
`validation` on the field it was sent in. A provider without them reads `null`.
Every provider read carries them: the complete provider, the list and the
hosting map.

### Hosting map

`GET /api/hosting-map` returns a `HostingMap` with providers (`key`, `name`,
`emblem`, `emblem_palette`)
and every live machine (`key`, `name`, `kind`, `status`, effective `provider`,
`ipv4`, `ipv6`, `private_ip`, `avatar`, `avatar_color`). Retired machines remain in it; deleted machines
do not. A VM's provider comes from its host. This one authenticated read feeds
the read-only `/hosting-map` diagram and its grouped list without a request per
machine. It records only provider-to-machine relationships; addresses are
machine fields. The same facts are readable with `ha provider list`,
`ha machine list` and `ha machine view KEY`.

### Installation map

`GET /api/machines/{key}/installation-map` returns one `InstallationMap` for a
live machine, including a retired one. It contains the machine's key, name, status,
`avatar` and `avatar_color`, plus every non-deleted installation on it, including retired
installations. Each entry has `key`, `name`, `software`, `role`, `status`, all
recorded `urls`, and `latest_deployment_at` (null where no deployment is
recorded). The latest time is derived from deployment `at`, including backfilled
and corrected records. Entries with deployments are sorted newest first; entries
without one follow in key order. A missing or deleted machine follows the same
read refusal as `GET /api/machines/{key}`.

This authenticated read feeds `/machines/KEY/installation-map` without a
request per installation. The read-only diagram draws only
machine-to-installation edges. Application installations are shown directly;
platform installations can be expanded, and the adjacent list always shows
all entries. Domains displayed on nodes come from distinct hostnames in the
recorded URLs. The same facts remain available through `ha machine view KEY`,
`ha installation list --machine KEY --retired`, `ha installation view KEY`, and
`ha deployment list --installation KEY`.

### Software

| | |
|---|---|
| `GET /api/software` | every software as a slim `SoftwareSummary`, by key |
| `POST /api/software` | only `key` is required; everything else may arrive later |
| `GET /api/software/{key}` | the complete software |
| `PATCH /api/software/{key}` | any field but the key; `If-Match` guards it |
| `GET /api/software/{key}/history` | who changed what, oldest first |
| `DELETE /api/software/{key}`, `POST /api/software/{key}/restore` | refused while installations hang on it |

**The collection is `/api/software`.** The word is uncountable, and there is no
`/api/softwares` (`CONTEXT.md`, Software).

The key is the address and is **immutable**, and both request objects are closed,
exactly as for a machine. `version` is a field neither of them has, and the
refusal says why: a software carries no version, because versions belong to
deployments.

`image` is a container image name **without a tag** — `caddy`,
`ghcr.io/datavisionzero/logaffe`. A `:tag` is `validation`, and so is a digest:
what was deployed belongs to a deployment. The short form a Compose file writes
is the right one to keep: drift matches a container to an installation through
Docker's own reading of the name, so `caddy` and `docker.io/library/caddy` are
the same image. `homepage` and `repository` are
absolute `http` or `https` addresses.

Deleting is not here yet: a software with installations is not deleted at all,
and that refusal needs the installation.

### Installations

| | |
|---|---|
| `GET /api/installations` | every installation as a slim `InstallationSummary`, by key |
| `POST /api/installations` | `key`, `machine`, `software`, `environment` and `role` are required |
| `GET /api/installations/{key}` | the complete installation |
| `PATCH /api/installations/{key}` | any field but the key; `If-Match` guards it |
| `GET /api/installations/{key}/history` | who changed what, oldest first |
| `DELETE /api/installations/{key}`, `POST /api/installations/{key}/restore` | soft, with its files and deployments |
| `POST /api/installations/{key}/purge?confirm={key}` | permanently remove a deleted installation and free its key |

The explicit purge requires the key again in `confirm`. It refuses pages
attached to the installation, dependencies on it, and Markdown links to
`installation:KEY` in pages or other descriptions; the `transition` problem
lists the references to clear. If its machine is deleted, restore the machine
first. It removes the installation's files, revisions
and deployments. If the automatic sweep already removed the row, the same act
releases its still-reserved key. Old history remains; a purge event names the
key, and the machine's history records it while the row is still available.

The list filters by `machine`, `software`, `environment`, `role`, `status`,
`backup`, `monitoring` and `logging`, each by one value. That is what makes the
question VISION 7 names one call:

```
GET /api/installations?environment=production&backup=none
```

**Five fields are required and the rest have defaults that are true.** An
installation is a software on a machine, so `machine` and `software` name
existing keys; `environment` and `role` answer the two questions every
installation answers, and neither has a default that would not be a guess. The
three decisions start at `none`, `none` and `local` — nothing decided yet is no
backup, which is the honest state and the one the filter above is meant to find.
`status` starts `active`.

**`ports` is a list of objects**, not of strings:

```json
{"ports": [{"port": 443, "protocol": "tcp", "scope": "public"},
           {"port": 5432, "protocol": "tcp", "scope": "private"}]}
```

`443/tcp:public` is how a person writes and reads one — the CLI and the
interface convert, and the history writes it that way — but the field is the
object. `protocol` is `tcp` or `udp`; `scope` is `public`, `private`,
`loopback` or `internal`, and two entries for the same port and protocol are
refused.

**A list is replaced whole**, never patched entry by entry: an entry has no
address, and a caller who sends two of them means both. `[]` clears a list,
leaving it out leaves it alone.

**An installation has two directories.** `path` is where it lives on the machine
— the directory it is deployed from, and the one every file it owns lies under —
and `data` is where its persistent data lies: what a backup has to take and what
a `docker compose down -v` does not bring back
([ADR 0009](adr/0009-an-installation-has-two-directories-and-data-is-the-second.md)).
Both are absolute, both may be left out, and nothing holds them apart: where a
host keeps configuration and state in one directory, both carry it. `data` is
searched like `path`.

**`secrets` is a list of objects too**, and never a list of values:

```json
{"secrets": [{"name": "POSTGRES_PASSWORD",
              "path": "/opt/compose/logaffe/.env.runtime"},
             {"name": "SMTP_PASSWORD"}]}
```

`name` is the name the installation needs it under — one word; anything with
a space or an `=` in it is `validation`, which is what keeps a line of an
`.env` file from arriving here whole. `path` is the file on the machine the
value lies in, absolute like every other path, and it may be left out: an
installation may need a secret before anybody has decided where it goes. The
value itself lives in vaultaffe or on the host and never here
([ADR 0011](adr/0011-a-secret-is-a-row-that-says-which-file-it-lies-in.md)).

A secret is the same secret by its **name**, so two entries naming one are
`validation` — the rule a port's number and protocol state one level up. Both
halves are searched: `GET /api/search?q=POSTGRES_PASSWORD` and the file it
lies in answer with the installation.

**`depends_on` is a list of installation keys** — what this installation needs
to do its job, on its machine or on another one:

```json
{"depends_on": ["caddy", "logaffe-db"]}
```

A key nothing live answers to is `validation` on the field, the way an unknown
`machine` is; an installation naming itself is `validation` too. It is replaced
whole like every other list, `[]` clears it, and the order does not matter — the
instance holds it in key order, so sending the same set another way round changes
nothing and writes no history.

**`needed_by` is the same edge read from the other end** and is on the complete
installation only, never on a summary. It is derived, like `version`: `PATCH`
refuses it as `unknown-field`, and nothing can write it into disagreement with
the dependencies it is read from. Both lists are one hop — nothing computes what
a dependency itself depends on — and a longer cycle is held rather than refused,
because the product orders no startup and resolves no closure
([ADR 0014](adr/0014-an-installation-depends-on-an-installation-and-the-reverse-is-derived.md)).

**An installation others depend on is not deleted**, and neither is a machine
carrying one that installations elsewhere depend on: `transition`, with
`dependents` saying how many. Retiring is the normal end and keeps every edge.

`version` is derived from the deployments and is read-only on an installation —
`PATCH` refuses it as `unknown-field`. `POST` takes it once, for the first
deployment.

### Files

A file has no key: its address is its owner and its path. Both owners get the
same six endpoints, once under `/api/machines/{key}` and once under
`/api/installations/{key}`:

| | |
|---|---|
| `GET …/files` | every file of the owner, by path, as a slim `FileSummary` |
| `POST …/files` | put one there; the content is its first revision |
| `GET …/files/{path}` | the file with its content; `revision` reads it as it was |
| `PUT …/files/{path}` | write it: a new revision |
| `GET …/file-revisions/{path}` | every write, newest first, without the contents |
| `GET …/file-history/{path}` | who changed what, oldest first |
| `DELETE …/files/{path}`, `POST …/file-restore/{path}` | soft, with every revision |

A path has slashes in it, so it is the last thing in an address — which is why
the revisions and the history sit under a word of their own beside `files`
rather than after the path, where nothing could tell a sub-resource from a
directory.

**Every write is a revision, and every earlier content stays.** Reading revision
2 is `GET …/files/compose.override.yml?revision=2`, and the answer is the same
`File` shape: "the file as it was" is the file. Comparing two of them is the
CLI's and the interface's; nothing is diffed on the server.

**A write that changes nothing makes no revision.** Sending the content the file
already has, with the mode bit it already has, answers the file unchanged. That
is what keeps `files sync` — which writes the whole set — from numbering the
history up without saying anything.

**`directory` is where a machine's file lies on the machine** —
`/etc/systemd/system`, absolute. A file created under a machine names one or is
refused as `validation` on `directory`; a file created under an installation is
refused *for* naming one, because the installation's own `path` already says
where all of its files lie (ADR 0008). It is a field of the file and not of a
revision: sending it alone moves the file and answers it at the revision it was
at, and the history names the move.

**The refused paths** (VISION 7, 10), one list, in the Domain, because the API
is as open as the CLI is:

- `.env`, and every `.env.*` but `.env.example`
- anything under `secrets/`, at any depth
- anything outside the owner's directory: a leading slash, a `..`, a `.`

`.envrc` is welcome — in the template it is one line and carries no value. The
warning about content that *looks* like a private key is the CLI's, and VISION 7
says itself that it is a guard against accidents rather than a boundary.

Content is UTF-8 and capped at one megabyte; what is not text is `validation`,
not a replaced byte. `executable` is the only mode bit there is, and it belongs
to the revision, so reading an old one gives the file as it was.

**`size` is the newest revision's content in bytes of UTF-8**, derived on read
like the content, the mode bit and the revision number, and the one thing about
the text a `FileSummary` says without carrying it. Bytes and not characters,
because that is what the megabyte is measured with: a size a list shows answers
"does this still fit?" with the number a write is refused against.

`path`, `revision` and `owner` are `unknown-field` in a write body. A file does
not move — it is put at the new path and the old one deleted — a revision is
made by writing, and the owner is the address it was written to. The revision a
caller *does* send is the guard, and it goes in `If-Match`: see Guarding a
write.

### Deployments

A deployment has no key: the instance numbers it per installation, and it lives
under that installation.

| | |
|---|---|
| `GET /api/installations/{key}/deployments` | every one, newest by `at` first |
| `POST /api/installations/{key}/deployments` | record one; only `version` is required |
| `GET /api/installations/{key}/deployments/{number}` | the complete deployment |
| `PATCH /api/installations/{key}/deployments/{number}` | `ref`, `at`, `ticket`, `note` |
| `GET /api/installations/{key}/deployments/{number}/history` | the corrections made to it |
| `DELETE …/deployments/{number}`, `POST …/deployments/{number}/restore` | the other half of the correction rule |

**There is no status.** A deployment is recorded when it is done. A rollback is
a deployment to the previous version with a note that says so; a failed attempt
that changed nothing is a note or a ticket, not a row here.

**`at` may be set**, so that history can be backfilled, and everything derived
is ordered by it:

- an installation's `version` is the one of its latest deployment **by `at`**
- a deployment's `previous` is the version of the deployment before it, same order
- its `files` are the revisions of the installation's files that were current at
  its `at` — empty for one backfilled to before the first file was put

Backfilling a deployment with an older `at` therefore does **not** move the
current version, and one with a newer `at` does. `at` can repeat, and the number
breaks the tie.

**The first deployment is created with the installation.** `POST
/api/installations` takes a `version`, and records both in one transaction, so
an installation never has a version without a record of when it appeared.
Without a `version` there is no deployment yet and no version — which is what a
`planned` installation is.

**The correction rule is fixed**, because an agent will record the wrong thing:
`ref`, `at`, `ticket` and `note` change and the history says so. `version` and
`installation` are `unknown-field`, and the refusal says why — they are what the
record *is*, and a deployment with the wrong version is deleted and recorded
again. So are `number`, `previous`, `files` and `status`.

`by` is who **recorded** it, written by the instance, which for a backfilled
deployment is not necessarily who deployed. `ticket` is a planaffe key like
`LOG-42` and stays a string: a reference to the other product, not a word of
this model.

### Reports

What a machine said about itself at a moment: a sample beside the record, and
never a field of it
([ADR 0015](adr/0015-a-machine-reports-and-the-record-stays-written.md)).

| | |
|---|---|
| `POST /api/machines/{key}/reports` | hand one in; **the machine's token and nothing else** |
| `GET /api/machines/{key}/reports` | the series, newest first, as slim `ReportSummary`; `limit` defaults to 50 and never exceeds 200, `offset` walks back |
| `GET /api/machines/{key}/reports/latest` | the latest one, whole |
| `GET /api/machines/{key}/reports/{number}` | one by its number, whole |

**The one write of the feature, and the narrowest door in the API.** It is
authenticated with the machine token of that machine: a user token and an agent
token are `unauthenticated` here, because somebody who could post a report by
hand could forge the drift comparison with nothing showing anywhere. A machine
token for another machine is refused too, and the answer does not distinguish
"there is no such machine" from "that one is not yours" — a token that could
enumerate keys would read something, and this one reads nothing.

```json
{
  "collected_at": "2026-09-13T08:00:07Z",
  "agent": "0.4.0",
  "host": { "hostname": "ex44", "os": "Ubuntu 26.04 LTS", "kernel": "6.14.0-27-generic",
            "arch": "x86_64", "uptime_seconds": 1893244, "load1": 0.14, "load5": 0.2, "load15": 0.18 },
  "memory": { "total_bytes": 67430400000, "used_bytes": 19204000000,
              "available_bytes": 46900000000, "swap_total_bytes": 0, "swap_used_bytes": 0 },
  "disks": [ { "mount": "/", "device": "/dev/nvme0n1p2",
               "size_bytes": 502000000000, "used_bytes": 301000000000, "percent": 60 } ],
  "containers": [ { "name": "logaffe", "image": "ghcr.io/datavisionzero/logaffe:1.4.0",
                    "state": "running", "status": "Up 3 days", "health": "healthy",
                    "restarts": 0, "started_at": "2026-09-10T09:12:00Z",
                    "ports": ["127.0.0.1:18502->8080/tcp"] } ],
  "listening": [ { "port": 22, "protocol": "tcp", "binding": "public" },
                 { "port": 18502, "protocol": "tcp", "binding": "loopback" } ],
  "updates": { "reboot_required": false },
  "files": [ { "installation": "logaffe-prod", "directory": "/srv/logaffe",
               "files": [ { "path": "compose.yml",
                            "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08" },
                          { "path": "Caddyfile", "sha256": null } ] } ],
  "missing": []
}
```

**Every section may be left out**, and a host without Docker reports no
containers instead of failing — it says so in `missing`, as
`{"section": "containers", "reason": "docker is not installed"}`. The shape is
**closed, section by section**: a field the contract does not define is
`unknown-field` rather than ignored, which is what keeps the body from becoming
a collecting bin.

**Numbers are numbers** — bytes, never `42G` — and times are RFC 3339 like
everywhere else. A container's `image` carries its **tag**, where a software's
`image` carries none: the tag is the thing worth comparing.

**`listening` carries no process.** Not the name, not the command line, not the
arguments — there is no field for one, and a body that names one is
`unknown-field`. `ss -tulpn` shows another user's process only as root, and the
collector's promise is that it needs none; a section whole on the machine whose
cron runs as root and half empty on the next would be worse than one that says
the same everywhere. What the section exists for is the comparison against an
installation's `ports`, and those are ports rather than processes.

`binding` is `public` — bound to a wildcard or to an address other machines can
reach — or `loopback`. It is **not** `scope`: a scope is what an operator
decided a port is for, and `private` means "from my own network", which is a
firewall's doing and invisible in a listening socket. The instance keeps **one
entry per port and protocol** and the widest binding wins, so a collector that
named a port twice, once on the wildcard and once on loopback, gets one
`public` entry back.

**`files` is the drift check for configuration, and it carries digests.** One
entry per directory `ha files sync` wrote into: the `installation` the manifest
`.ha-sync.json` beside the files names, the `directory` it lies in, and for
every path that manifest claims the SHA-256 of what lies there now — lower-case
hexadecimal, 64 characters, or `null` where nothing lies there any more
([ADR 0017](adr/0017-a-machine-reports-digests-and-the-instance-compares.md)).

**Never a content.** That is what lets a host say whether its configuration
still matches the record without handing the configuration over, and it is why
this section needs no token that reads. The paths it may carry are the record's
own and are checked against the same rules: `.env` and every `.env.*` but
`.env.example`, anything under `secrets/`, and anything that climbs out of the
directory are `validation` here as they are there.

**The manifest is the whole of what a report names.** A file sync never wrote is
not in it, is not hashed and is not named — the same rule sync keeps, and what
stops a report from carrying the file names of whatever else lies in a compose
directory. An empty `files` on a directory is not nothing: it says the machine
holds that installation's files there and has none of them.

**One installation, one directory, and one entry per path in it.** An
installation has a single `path` in the record, so a body claiming two
directories for it says two things that cannot both be answered; both that and a
path named twice are `validation` rather than deduplicated. `ha` says so on the
host instead and reports the first directory, so that a cron given the same
`--sync-dir` twice keeps its sign of life.

A report carries at most **32 directories** and **128 files each**, and the
64 KB below is the real ceiling. An `installation` this machine does not have is
stored as it came and compared against nothing: that is a cron pointed at the
wrong directory, which is a mistake on the host and not a disagreement between
two sides.

**`updates` says one thing: `reboot_required`.** How many packages have an
update is deliberately not there. Counting them makes the collector
distribution-dependent for the first time — apt, dnf, apk, pacman, each with
its own command — and a host on which nothing ran `apt update` for weeks would
report nothing pending and lie in the most comforting way there is. Where the
restart cannot be told, the section is **absent** and `missing` says why: a
`false` from a machine nobody could ask is the worst of the three answers.

**The instance sets `received_at` itself** and does not trust the host's clock.
`collected_at` is kept as it came, and the order of reports and a machine's
`last_seen` are read from `received_at`. A `collected_at` far in the future is
stored rather than refused: denying a machine with a wrong clock its sign of
life would be the worse answer.

**At the door:** a body over **64 KB** is `too-large`, with `limit` saying what
it was, and more than **one report per machine per minute** is `rate-limited`,
with `retry_after` saying how long. Neither guards against an attacker holding a
valid token — it can lie about its own machine whatever it does — they guard
against a cron running amok and a collector that appended something large.

**Nothing else happens.** No field of the machine is set, no history row is
written, no deployment appears, nothing on an installation is touched. The
answer is `{ "number", "received_at" }`, which is all a cron has any use for.

**Reading is ordinary.** The three `GET`s take a user or agent token like
everything else, because everyone in the instance sees everything
([VISION 9](../Vision.md#9-users-and-permissions)). A **machine token reaches
none of them**, its own machine's reports included.

The series answers `{ "total", "reports" }`, and a `ReportSummary` is what a
list makes a line of: `number`, `received_at`, `collected_at`, how many
containers run of how many, the highest disk percentage, `load1`, and
`reboot_required`. The `files` section is not in it: what it says is either a
drift, which is read on the whole report, or a count of paths that agree. All of it
is counted from the body on read; none of it is stored beside the body it is
counted from. That is enough for "on the 3rd the disk went from 60 to 91 per
cent" without fetching two hundred whole bodies to see it.

A machine that has never reported answers `not-found` on `latest` and an
**empty list** on the series. That is not an error: it is the ordinary state of
a machine on which no cron has been set up.

**`last_seen` is on the machine**, in `GET /api/machines/{key}` and in the
`MachineSummary` of the list, so that an overview does not ask once per row. It
is the `received_at` of the latest report, derived and never written, and it is
absent where a machine has never reported. It stands beside `measured_at` and
means something else — `measured_at` is when a person last checked the facts.

**`reboot_required` is on the machine too**, in the same two places and derived
the same way, because the list worth having is ten machines in a column and the
two that are waiting for a restart. It is `null` where the machine has never
reported and where the collector could not tell.

### Drift

**Because both sides are there, they can be compared** — and that is why a
report stands beside the record instead of in it
([ADR 0015](adr/0015-a-machine-reports-and-the-record-stays-written.md)).

`drift` comes with the whole report (`latest` and one by number) and with
`GET /api/machines/{key}`, where it is computed from that machine's latest
report. It is served here rather than assembled by each client, which is what
keeps the web application and `ha` from saying different things about one host.

```json
{"kind": "version", "subject_kind": "installation", "subject": "logaffe-prod",
 "field": "version", "record": "1.4.0", "record_at": "2026-09-08T19:12:00Z",
 "reported": "1.3.2", "reported_at": "2026-09-13T08:00:09Z"}
```

**`subject_kind` says what the subject is** — `machine` or `installation` —
because a key is unique per entity type and not across them, and a machine
`caddy` and an installation `caddy` both exist. It is what lets a client link
the subject to the thing it names rather than guess from the kind.

`kind` is one of five:

- **`version`** — the tag of a container's image against the version of the
  installation's latest deployment. The most valuable line of the whole
  feature: "the record says logaffe-prod runs 1.4.0, the machine reports
  1.3.2."
- **`container`** — an installation the record calls `active` whose container
  the machine reports as `exited`. A statement, not an alarm.
- **`fact`** — `os` or `arch` against the machine's own fields, with
  `record_at` the `measured_at` beside them. This is
  [VISION 15.1](../Vision.md#151-measuring-instead-of-typing) word for word.
- **`port`** — a port an active installation or the machine itself says it
  listens on against what the machine has a socket for, and a port bound in
  public that neither of them claims. `field` names the port — `port
  18502/tcp` — `record` is the recorded `scope` and `reported` the `binding`;
  `record` is `null` where nothing in the record claims the port, and
  `reported` is `null` where nothing listens there at all.
- **`file`** — a file of the record against the digest the machine reported for
  the path `files sync` wrote it to. `field` names the path — `file
  compose.yml` — and both sides are **digests**, shortened to twelve characters
  the way a commit is, because that is the one value the two sides have in
  common: a file on a host carries no revision.

A `scope` and a `binding` are not compared as if they were the same word.
`public` and `private` both need a socket bound past loopback — how much
further is a firewall's doing, which no listening socket shows — so both are
read as "reachable from off this machine", and a `loopback` binding under
either is drift. A `loopback` scope needs a `loopback` binding; silence or a
`public` binding is drift. `internal` means the port never reaches the host, so
silence is the agreement and a `public` binding is the disagreement. An
installation the record does not call `active` is passed over: a planned one
is not supposed to be listening.

A `file` drift has three shapes and one silence. The record's digest against
another one is a file changed on the host, or a record that moved with no sync
since. `reported` is `null` where the record has the file and nothing lies at
that path. `record` is `null`, `record_at` with it, where sync wrote it once,
the record has let it go, and it is still lying there. **A path neither side has
any more makes no drift** — the manifest still names it, nothing lies there, and
there is nothing to clear. **Only the content is compared and never the mode
bit**: the manifest hashes bytes, and `files sync` puts the record's mode on the
file on every run anyway.

**The comparison runs for exactly the directories the report names.** A machine
whose cron was given no `--sync-dir` reports no `files` and hears nothing about
them, however much the record holds for it — the same rule the machine's own
ports keep, and for the same reason: a drift nobody can clear teaches people to
stop reading the list. It is not a suppression list; a directory that is named
is compared whole.

**A port bound in public that stands in no record is drift only where the
machine keeps its own ports.** It is the question the section was wanted for —
what is reachable from outside that nobody wrote down — and it is answerable
because `ports` at the machine is where SSH goes. Where a machine keeps none,
the record says nothing about its ports and the comparison is not made: every
port it has would otherwise be reported as undocumented for ever, and a drift
nobody can clear teaches people to stop reading the list. Writing one port down
switches the comparison on, and every finding it then makes can be cleared —
either the port is written down or it is closed.

That is the semantics of the field and **not a suppression list**: no single
port and no single finding is silenced, there is no ignore flag, and the three
comparisons above run whatever the machine's `ports` say. A socket bound to
loopback alone is not in it either — it reaches nothing off the machine, and a
record of what an operator rents is not a process list. A port a `planned`
installation wrote down counts as claimed: somebody put it in the record,
whatever the installation's state says.

**Which side is right the product does not say.** Every drift names both sides
and how old each is, and the decision is a person's or an agent's. No field is
set, nothing is "reconciled", and there is no call that pulls the record after
the report — that would be discovery through the back door, and it is
deliberately not here.

**Where the assignment is ambiguous, nothing is claimed.** A container is
matched to an installation by the image name *without* its tag, which the
software already carries, plus the machine it lies on. The name is read the way
Docker itself writes it, so the two sides match on the image rather than on the
spelling: `caddy`, `library/caddy` and `docker.io/library/caddy` are one name,
and so are `acme/widget` and `docker.io/acme/widget`. That matters because the
Docker API is inconsistent about the prefix — it reports one container with
`docker.io/` and the next without — while a record keeps the short form that
stands in every Compose file. Another registry is left as it is:
`ghcr.io/library/caddy` is not `caddy`. Two installations of the
same software on one machine, or two containers out of one image, produce no
drift at all: the report is shown and the reader compares the two rows. A wrong
sentence is worse than none. So does a software whose `image` is empty, an
installation with no container, and a container with no installation.

**Disk, memory and load make no drift**, and neither does a pending restart.
They have no other side in the record,
so they are shown and not compared, and "91 per cent full" is a number a person
reads rather than a disagreement.

**What is not here:** no filter over the contents of a body, no aggregate, no
time window beyond the ordinary paging, and no search in reports. `GET
/api/search` goes over the record, and a report is not the record; whoever wants
an evaluation has a monitoring tool for it
([VISION 5](../Vision.md#5-non-goals-deliberate-boundaries)).

### Importing

| | |
|---|---|
| `POST /api/import` | a whole record from one document, in one transaction |

Documenting a host is one act and not thirty calls. The body is the document
`ha export` writes — providers, machines with their installations, files and deployments,
the software they are of, and pages — and everything in it is created in **one
transaction**:

```json
{"providers": [{"key": "example-host", "name": "Example Host", "description": "Support notes."}],
 "software": [{"key": "caddy", "image": "caddy"}],
 "machines": [{"key": "ex44", "kind": "dedicated", "provider": "example-host",
               "files": [{"path": "sites/app.caddy", "content": "…"}],
               "installations": [{"key": "app-1", "software": "caddy",
                                  "environment": "production", "role": "application",
                                  "files": [{"path": "compose.yml", "content": "…"}],
                                  "deployments": [{"version": "1.4.0", "at": "2026-09-05T12:00:00Z"}]}]}],
 "pages": [{"slug": "backup-restore", "title": "Restoring a backup", "kind": "runbook",
            "path": "docs/operations/backup-restore.md"}]}
```

**All or nothing.** It is the ordinary acts run inside one transaction, so every
rule they hold still holds — the keys, the closed sets, the refused paths, the
history each of them writes — and an installation whose software neither exists
nor arrives with it fails the whole thing, leaving nothing standing and no key
spent.

**Providers are created before machines.** Current exports always contain a
`providers` array, even when empty. Each provider carries its description and
history; import applies the description and starts new history with the caller.
Machine `provider` values in current exports are keys. A VM's effective
`provider` appears in an export but is inherited again on import, never
assigned directly. Its read-only `legacy_provider` text, when present, is
preserved exactly.

An older export has no `providers` array and its machine `provider` values are
free text. Import creates one provider for each distinct nonblank value in
ordinal order, deriving a lowercase hyphenated key of at most 64 characters.
Colliding keys receive numeric suffixes (`-2`, `-3`, …). The original text is
kept in each machine's `legacy_provider` field. A VM inherits its host's
provider; any old VM value that disagrees remains visible in `legacy_provider`.
Provider creation, machine creation and everything nested under them share the
same transaction, so a later refusal rolls all of them back.

**What only the instance writes is read past.** The document is an export, so it
carries `created_by`, `updated_by`, `created_at`, `updated_at`, the `history`, a
file's `revision` and `owner`, an installation's `version` and `needed_by`, a
deployment's `number`, `previous`, `files` and `by`, and a page's `author`. Those
are accepted by name and dropped; anything else is `unknown-field`, as
everywhere. **The closed sets arrive as their words**, spelled as the contract
spells them.

**`depends_on` is set after every installation is there.** An installation may
depend on one that is further down the same document, under a machine the import
has not read yet, so the dependencies are written in a pass of their own — the
way a machine's `host` is, for the same reason. Within one transaction, so a
dependency naming nothing at all still fails the whole thing.

**So the circle carries the record and not the account of how it got there.**
An export read back in is the providers, the machines, the software, the installations, the
files at the content they are at, the deployments and the pages — beginning
here, written by whoever ran the import, at the moment they ran it. The
history of the source instance, the revisions its files went through, the
timestamps and the identities behind them do not come along, and nothing here
invents them: this is the ordinary acts inside one transaction, and the
history is the instance's to write and never to be written to (`CONTEXT.md`,
History). What that is good for is adopting a record — a migrated repository,
a second instance seeded from an export, a record lifted out of one place and
put down in another. **Moving an instance with its history intact is `pg_dump`
and `psql`** and not this endpoint ([`operations.md`](operations.md), Backup).

**A file arrives at the content it is at**, as its first revision: what it said
before is in the source's history, and a record that invented revisions it never
had would be a worse copy than one that says where it began. **An installation's
`version` is not read**, because the deployments are in the document and they
are what a version is derived from. **A vm finds its host anywhere in the same
document**: every machine is created first and the hosts are set afterwards.

**A page says where it came from, and its links are rewritten.** `path` is the
file the page's Markdown sat in before — `docs/setup/README.md` — and it is
read for one thing and never stored: a relative `.md` link in any body is
resolved against it and turned into `page:<slug>`, the address of the page that
arrives in the same document under that path (ADR 0007). A path no page in the
document claims is left exactly as it was, because a dead link the reader can
see beats an address the import invented. `links` in the answer counts what was
rewritten, which is what says whether the paths in the document were the ones
the bodies actually used.

**Two pages that want one slug are named.** The slug space is flat and
instance-wide (ADR 0003) and a repository of Markdown is not, so five
directories each holding a `README.md` are ordinary there and one name here.
The document is read for that before anything is written, and the refusal says
which two entries collided rather than only which slug:

```json
{"type": "…/validation", "status": 400,
 "errors": {"slug": ["docs/setup/README.md and docs/operations/README.md both want the slug readme, and a slug names one page in the whole instance; one of them is given another."]}}
```

`note` goes into the history beside every change the import makes.

### Searching

| | |
|---|---|
| `GET /api/search?q=…` | every live record the words match, capped; `limit` defaults to 50 and never exceeds 200 |

"Where was that again" is the question a host record is asked most often, and it
is one call over every field, every Markdown body and every file. A hit says
what was found and where:

```json
{"kind": "installation", "key": "logaffe-prod", "name": "logaffe",
 "number": null, "directory": null, "owner": null, "where": "ports"}
```

`kind` is `machine`, `provider`, `software`, `installation`, `deployment`, `file` or `page`,
and the hits come in that order. `key` is the address — a key, a page's slug, a
file's path, or the installation a deployment lives under; `number` is the
deployment's number and nothing else has one; `directory` is where a machine's
file lies on the machine and nothing else has one either; `owner` is what a file
belongs to or a page hangs on. `where` names the surface that matched: `fields`,
`description`, `ports`, `secrets`, `note`, `path`, `content`, `title` or `body`.

**A file's directory comes with the hit, because the search reads it.** Where a
machine's file lies is one of the surfaces a fragment is looked for on
([ADR 0012](adr/0012-a-path-is-found-by-its-letters-not-by-its-words.md)), so a
search for `/etc/systemd/system` answers with files whose `key` is a bare name —
and `directory` is the half of the address that says what it answered with. It
is `null` for an installation's file, whose directory is the installation's own,
once for all of them ([ADR 0008](adr/0008-a-machines-file-says-where-it-lies-and-is-never-synced.md)).

**Postgres and nothing beside it.** No second index and nothing to operate,
which is part of the promise that an instance starts from a Compose file. Three
things follow from that, and all three are the honest shape of it rather than an
omission:

- **The words are matched the way Postgres splits text**, and a whole path is
  one of those words: `/opt/compose/logaffe` finds the installation whose
  directory it is.
- **A piece of a path is not a word.** `/srv/caddy/caddy.env` is one token, so a
  search term that is one word with a slash or a dot in it, three characters or
  longer, is looked for as a fragment as well — over the same surfaces, through
  a `pg_trgm` index, and never instead of the words
  ([ADR 0012](adr/0012-a-path-is-found-by-its-letters-not-by-its-words.md)).
  `caddy /srv/caddy` is two words and stays a word search.
- **A port is a number, not a word.** `18502` inside `18502/tcp` is not a token
  anyone would find by typing the number, so a query that *is* a port number is
  looked up in the ports as well — an installation's and a machine's alike, so
  that `22` finds the host SSH is written down on. That is what makes `ha
  search "18502"` answer.

**A file is searched at the revision it is at.** What an older revision said
stopped being true when the next one was written, and a hit in it would send
somebody to a line that is not there. Deleted rows are not hits, and neither is
anything a deletion took with it.

**Not paginated, capped instead.** It is not a list of one thing and there is no
order a cursor could walk; a word that occurs in every file would otherwise
answer with the whole record, which is not an answer.

### The history

| | |
|---|---|
| `GET /api/history` | every change to the record, newest first, with the deployments mixed in |

A subject's own `…/history` answers what became of that one thing, oldest
first. This is the other question — what has been going on — and it is asked of
the whole record at once:

```json
{"at": "2026-09-18T19:12:04.118231Z",
 "actor": {"id": "…", "kind": "user", "name": "alex"},
 "subject_kind": "installation", "subject": "logaffe-prod", "number": null,
 "machine": "ex44", "owner": null,
 "changes": [{"field": "status", "old_value": "planned", "new_value": "active"},
             {"field": "backup", "old_value": "planned", "new_value": "active"}],
 "note": "the box is live", "cursor": "MjAyNi0wOS0xOFQxOToxMjowNC4x…"}
```

**One act is one event.** A history row is one field, and a `PATCH` over three
of them wrote three rows carrying the same actor, the same moment and the same
note; here they are one event with three changes, in the order they were
written. Nothing in the table moves — the folding is a reading, and
`…/history` on the subject still answers row by row.

**A deployment is an event, and it is not a history row.** It stays what
`CONTEXT.md` says it is, a record of its own, and this reading puts it beside
the changes because "logaffe-prod went from 0.4.1 to 0.5.0" is the answer
somebody asking what happened is looking for. Its `subject_kind` is
`deployment`, its `subject` is the installation it lives under, its `number` is
the deployment's, and its one change is the version — `old_value` the version
before it in the order every derived value uses, `new_value` its own. What the
recording of a deployment wrote into the history beside it is left out here,
because that is the same event a second time; a **correction** to a deployment
is a change like any other and appears as one.

**A file and a page carry their `owner`**, the way a search hit does: a path is
an address only under the thing it belongs to, and a slug is read beside what it
hangs on. Everything else has none.

**`machine` is what hangs on a machine**, not what names it: the machine's own
changes, its installations', the deployments of those, the files of both, and
the pages attached to either. A software belongs to no machine and is in no
machine's reading; a page of the instance is in none either. `kind` keeps one
kind of subject — `machine`, `software`, `installation`, `file`, `page` or
`deployment` — and a word outside that set is `validation`.

**A deleted subject keeps its events.** The history survives the deletion of
what it describes and the purge with it (VISION 7), and a reading of what
happened that dropped the deletions would answer the opposite of what it was
asked: the last thing that happened to a machine somebody removed is that
somebody removed it. Where the purge has taken the row, `subject` is `null` and
the event still says its kind, its moment and who.

**There is no notion of an interesting event.** Every act is one line and the
reader decides. A filter that hid the dull ones would be a rule nobody can see,
and a short list that is quietly wrong is worse than a long one. Reports are
not in it at all, for the reason the glossary already gives: a cron reporting
every quarter of an hour has changed nothing.

**Walked with a cursor, not an offset.** `limit` defaults to 50 and never
exceeds 200, and every event carries the `cursor` that continues the reading
after it: the next page is `before=` the last one's. It is opaque — what it
holds is this reading's sort key, which is the moment plus the side and the row
it came from, so that two events sharing a moment cannot hide each other. A
cursor that is not one this reading handed out is `cursor-invalid`.

### Pages

| | |
|---|---|
| `GET /api/pages` | every page, by slug, without the bodies; `q`, `kind`, `machine`, `installation` filter |
| `POST /api/pages` | the slug is given, never derived from the title (planaffe ADR 0021) |
| `GET /api/pages/{slug}` | the complete page |
| `PATCH /api/pages/{slug}` | title, body, slug, `kind` or `attached_to`; `If-Match` guards it |
| `DELETE /api/pages/{slug}`, `POST /api/pages/{slug}/restore` | soft, with the grace period |
| `GET /api/pages/{slug}/history` | who changed what, oldest first |

A page's slug is its address and may be renamed; nothing forwards afterwards,
and the rename stands in the history. It is one segment and carries no slash
([ADR 0003](adr/0003-a-pages-slug-is-one-segment-not-a-path.md)), so
`/api/pages/{slug}/history` is never itself a page. A deleted page keeps its
slug until the purge, so a restore never lands on a name somebody else has
taken.

**`kind` is `runbook`, `decision` or `note`**, and it says how the page is to be
read and nothing more: no status, no supersedes, no template enforced beyond it.
It defaults to `note`, which is the kind that claims nothing.

**`attached_to` names a machine or an installation, or is left out** — and then
the page belongs to the instance as a whole:

```json
{"attached_to": {"kind": "machine", "key": "ex44"}}
```

The kind is part of it because a key alone would name two things: a machine
`caddy` and an installation `caddy` both exist. In `PATCH`, `"attached_to":
null` unhooks the page and leaving the field out leaves the anchor where it is —
the same rule the body follows.

Filtering is `?kind=runbook`, `?machine=ex44` and `?installation=logaffe-prod`.
Asking for both a machine and an installation is `validation`: a page hangs on
one thing.

**A body names another thing of the record with a scheme and an address**:
`[Restoring a backup](page:backup-restore)`, `[ex44](machine:ex44)`,
`[caddy](software:caddy)`, `[Example Host](provider:example-host)`,
`[app-1](installation:app-1)`
([ADR 0007](adr/0007-a-record-is-linked-from-markdown-as-a-scheme-and-a-key.md)).
The scheme carries the type because the address does not — a key is unique per
entity type, so the machine `caddy` and the software `caddy` coexist.

Nothing validates it. The instance stores Markdown and does not parse it, so a
link to a page that does not exist is stored like any other text; the web
application resolves these schemes to its own addresses and `ha page check`
says which references point at nothing. Renaming a page therefore breaks its
inbound links, as it always did — the check is how that shows itself.
