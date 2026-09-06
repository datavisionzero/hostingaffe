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
  server tells a user token from an agent token; nothing else does. Only
  `GET /api/version` is outside the door.

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
| `secret-expired` | 410 | a one-time link, spent or expired |
| `stale` | 412 | `If-Match` did not match; `current` carries the object |
| `transition` | 422 | the object's state does not allow the act — restoring what is not deleted, deleting a software that still has installations (`installations` says how many) |
| `smtp-not-configured` | 422 | the act needs a mail and the instance sends none |
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

**The history survives everything, the purge included**, and so does the key. A
key is written into a register when it is given out and is never given out
again — creating a machine under the key of one that was purged is `validation`,
and the message says why.

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
| `GET /api/machines` | every machine as a slim `MachineSummary`, by key; `status` and `kind` filter |
| `POST /api/machines` | `key` and `kind` are required, everything else may arrive later |
| `GET /api/machines/{key}` | the complete machine |
| `PATCH /api/machines/{key}` | any field but the key; `If-Match` guards it |
| `GET /api/machines/{key}/context` | everything recorded about it, as one Markdown document |
| `GET /api/machines/{key}/history` | who changed what, oldest first |
| `DELETE /api/machines/{key}`, `POST /api/machines/{key}/restore` | soft, with the cascade above |

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

`status` is `planned`, `active` or `retired` and defaults to `active`: a record
is usually made for a machine that already exists, and `planned` is the case a
caller states. What retiring does and what deleting takes is above, under
Retiring, and deleting.

Hardware facts are text, `arch` excepted, and each is one line of at most 200
characters. What is longer than that is the `description`, or a page.

**`context` is the one call an agent makes before it touches a host.** It
answers `{ "key", "document" }`, and the document is Markdown, in this order:
the machine and its fields; its installations, each with the version it runs,
its ports, its file list and the last five deployments; the software those are
installations of; the machine's own files; the pages that hang on the machine or
on any of its installations; and the instance's `decision` pages — the rules
that hold on every host.

**File contents are not in it.** They are one read of a file away, and they are
what would fill a context window. The measure is VISION 16: well under ten
thousand tokens for a machine with five installations, which the integration
tests hold to by counting characters rather than by estimating.

The document is assembled here rather than by the caller, and that is a
deliberate exception to what this document otherwise avoids — describing
presentation. A client that built it would ask about thirty times for a host of
that size, and the web application's machine screen is the same assembly; one of
them is what keeps the two saying the same thing.

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
what was deployed belongs to a deployment. `homepage` and `repository` are
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
object. `protocol` is `tcp` or `udp`, `scope` is `public`, `private` or
`internal`, and two entries for the same port and protocol are refused.

**A list is replaced whole**, never patched entry by entry: an entry has no
address, and a caller who sends two of them means both. `[]` clears a list,
leaving it out leaves it alone.

`secrets` holds the **names** of the secrets the installation needs and never
their values — the values live in vaultaffe or on the host. A name is one word;
anything with a space or an `=` in it is `validation`, which is what keeps a
line of an `.env` file from arriving here whole.

`version` is derived from the deployments and is read-only on an installation —
`PATCH` refuses it as `unknown-field`. `POST` takes it once, for the first
deployment. `depends_on` is roadmap (VISION 15.2), not an omission.

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
