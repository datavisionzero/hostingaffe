# The HTTP API

One API, for the web application, the CLI and whatever an agent writes. It
carries no version in its path: the contract is
[`api/openapi.json`](api/openapi.json), captured from a running instance and
checked in, and both clients are generated from it (planaffe ADRs 0005, 0011).

The endpoint tables below cover what the foundation serves. The objects of
the product — machine, software, installation, deployment, file — arrive with
their own sections.

## Conventions

- **JSON in, JSON out**, `snake_case` on the wire. An enum travels as the name
  the contract spells, never as a number, so a value outside the set is refused
  at the door rather than stored as a row nobody can read.
- **Every timestamp is RFC 3339** in UTC, to the microsecond.
- **A closed request object refuses a field it does not define** —
  `unknown-field`, rather than silently ignoring what somebody meant.
- **Authentication is a bearer token or the browser's session cookie.** The
  server tells a user token from an agent token; nothing else does. Only
  `GET /version` is outside the door.

## Errors

Every refusal is one `application/problem+json` document, written in one place:

```json
{
  "type": "/problems/not-found",
  "title": "Nothing by that key or id",
  "status": 404,
  "detail": "No page architecture.",
  "instance": "/pages/architecture"
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
| `transition` | 422 | the object's state does not allow the act |
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

## Concurrency on text fields

A text two writers share — a page's body — is guarded by `If-Match` carrying
the `updated_at` last read, quoted:

```
If-Match: "2026-09-06T09:12:44.518273Z"
```

A write over a version somebody else has moved is `stale`, and the refusal
carries the object as it now stands in `current`, so the caller can merge
rather than lose what it typed. Without the header the write goes through: the
guard is offered, not imposed.

## Who may do what

The line of [VISION 9](../Vision.md#9-users-and-permissions):

- Every identity reads everything and writes content. One instance holds one
  team's infrastructure, and there are no scopes inside it.
- **An agent administers no identities** (planaffe ADR 0015). `/users`,
  `/agents` and `/tokens` under an agent token are `forbidden`, and an agent
  token never carries the administrator role.
- An administrator invites users, grants and revokes the administrator role,
  deactivates and reactivates, and configures the instance.

## Endpoints

### The instance and the caller

| | |
|---|---|
| `GET /version` | the instance's version; the one endpoint outside the door |
| `GET /me` | who the token says you are |
| `PATCH /me`, `POST /me/password`, `POST /me/email` | your own name, password, address |
| `PATCH /me/metadata` | what an agent reports about itself |
| `GET /admin/smtp`, `POST /admin/smtp/test` | whether mail is configured, and one test mail |

### Sessions

| | |
|---|---|
| `POST /session` | sign in with name and password; sets the browser's cookie |
| `POST /session/bootstrap` | exchange the bootstrap token for a session, once |
| `DELETE /session` | sign out |
| `GET /sessions`, `DELETE /sessions/{id}` | your browser sessions, and ending one |

### Users, agents and tokens

| | |
|---|---|
| `GET /users`, `POST /users` | the humans; creating one sends an invitation |
| `PATCH /users/{id}` | the administrator role |
| `POST /users/{id}/invitation` | send the invitation again |
| `POST /users/{id}/deactivate`, `/reactivate` | identities are never deleted |
| `POST /invitations/accept` | set a password and become active |
| `POST /password-recovery`, `/password-recovery/complete` | the link, and what it leads to |
| `POST /email-changes/confirm` | confirm a new address |
| `GET /agents`, `POST /agents` | agents; creating one prints its token once |
| `PATCH /agents/{id}`, `DELETE /agents/{id}` | rename, and revoke the token |
| `GET /tokens`, `POST /tokens`, `DELETE /tokens/{id}` | your own user tokens |

### Pages

| | |
|---|---|
| `GET /pages` | every page, by slug, without the bodies; `q` is the full-text filter |
| `POST /pages` | the slug is given, never derived from the title (planaffe ADR 0021) |
| `GET /pages/{slug}` | the complete page |
| `PATCH /pages/{slug}` | title, body or slug; `If-Match` guards it |
| `DELETE /pages/{slug}`, `POST /pages/{slug}/restore` | soft, with the grace period |
| `GET /pages/{slug}/history` | who changed what, oldest first |

A page's slug is its address and may be renamed; nothing forwards afterwards,
and the rename stands in the history. A deleted page keeps its slug until the
purge, so a restore never lands on a name somebody else has taken.
