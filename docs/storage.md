# Storage

Every table the instance holds, what each column is for, which rules the
database keeps and which the write path keeps, and the one value that is
derived on read rather than written. [`api.md`](api.md) is the surface over it;
[`CONTEXT.md`](../CONTEXT.md) spells every name used here, and
[Vision.md §7](../Vision.md#7-domain-model) is where the model was decided.

EF Core declares these tables and owns the migrations that apply themselves on
start ([`codebase.md`](codebase.md)). The SQL below is the shape, not the
migration: names and types are binding, the exact DDL is the migration's.

The foundation is a copy of planaffe with planaffe's domain cut out
([ADR 0001](adr/0001-the-foundation-is-a-copy-of-planaffe.md)), so identities,
tokens, sessions, the page and the history arrived whole and are described here
as they now stand rather than as planaffe has them.

## Conventions

- **Every table names its columns in `snake_case`**, and so does the API. A
  column and the field it answers as have the same name.
- **A closed set is text, not a number**, and a check constraint lists the
  words. `status in ('planned', 'active', 'retired')` can be read; `status in
  (1, 2, 3)` has to be looked up. The spelling is the contract's — the snake
  case of the name.
- **Every timestamp is `timestamptz`**, written in UTC.
- **Every index is declared**, none inferred. The convention that puts one on
  every foreign key is switched off, because nothing reads by `created_by`. The
  list below is the schema; a test compares it against a migrated database.
- **A soft delete is `deleted_at` and `deleted_by`**, and the row stays until
  the purge takes it ([planaffe ADR 0013](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0013-deleting-is-a-soft-delete-with-a-floor-and-identities-are-never-deleted.md)).
  A unique index over a handle covers deleted rows on purpose, so that a restore
  never lands on a name somebody else has taken.
- **`updated_at` is the version** a guarded write is compared against
  (`api.md`, Concurrency); no row carries a separate version column.

## Identities and tokens

Every record of who did something — the author of a page, who last touched a
machine, every history row — points at one table, so that a user and an agent
are the same kind of thing to everything that references them (`CONTEXT.md`,
Identity). EF Core maps this as one hierarchy in one table.

```sql
create table identity (
    id                     uuid        not null primary key,
    kind                   text        not null check (kind in ('user', 'agent')),
    name                   text        not null,
    administrator          boolean     not null default false,
    owner_id               uuid        references identity (id),  -- the agent's creator
    created_at             timestamptz not null,

    email                  text,                                  -- users only
    normalized_email       text,
    user_state             text,                                  -- invited, active, deactivated
    password_hash          text,
    bootstrap_exchanged_at timestamptz,

    metadata               jsonb,                                 -- what an agent says about itself
    metadata_reported_at   timestamptz,

    check (kind = 'user'  and owner_id is null
        or kind = 'agent' and owner_id is not null and not administrator)
);

create unique index identity_name  on identity (lower(name));
create unique index identity_email on identity (normalized_email) where kind = 'user';
```

**Names are unique across both kinds**, case-insensitively, because the API and
the CLI address identities by name and a name that could mean two things is no
address.

**An agent is never an administrator** and always has an owner — held by the
check constraint, not only by the write path
([planaffe ADR 0015](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0015-a-token-is-an-agent-or-a-users-key-and-an-agent-is-never-an-administrator.md)).

**Identities are never deleted.** There is no `deleted_at` here and never will
be; a user is deactivated by changing `user_state`.

```sql
create table token (
    id          uuid        not null primary key,
    identity_id uuid        not null references identity (id),
    kind        text        not null check (kind in ('user', 'agent')),
    prefix      text        not null,   -- the first characters, for recognising it in a list
    secret_hash bytea       not null,
    created_at  timestamptz not null,
    revoked_at  timestamptz
);

create unique index token_secret_hash on token (secret_hash);
create unique index token_agent       on token (identity_id) where kind = 'agent';
```

**An agent has exactly one token** — the partial unique index is what says so.
A user has as many as they create, each revocable on its own. The secret is
never stored, only its hash; `prefix` is what a person recognises a row by.

```sql
create table one_time_secret (
    id                       uuid        not null primary key,
    user_id                  uuid        not null references identity (id),
    purpose                  text        not null
        check (purpose in ('invitation', 'password_recovery', 'email_change')),
    secret_hash              bytea       not null,
    pending_email            text,
    pending_normalized_email text,
    created_at               timestamptz not null,
    expires_at               timestamptz not null,
    used_at                  timestamptz,

    check ((purpose = 'email_change') = (pending_email is not null))
);

create unique index one_time_secret_hash        on one_time_secret (secret_hash);
create unique index one_live_secret_per_purpose on one_time_secret (user_id, purpose) where used_at is null;
```

**One live secret per user per purpose**: asking for a second recovery link
replaces the first rather than leaving two doors open.

```sql
create table browser_session (
    id           uuid        not null primary key,
    user_id      uuid        not null references identity (id),
    secret_hash  bytea       not null,
    created_at   timestamptz not null,
    last_used_at timestamptz not null,
    expires_at   timestamptz not null,
    revoked_at   timestamptz
);

create unique index browser_session_hash on browser_session (secret_hash);
create        index browser_session_user on browser_session (user_id, created_at desc);
```

The cookie carries the secret and nothing else; the session list a person sees
is read by `browser_session_user`, newest first.

```sql
create table identity_metadata (
    id          uuid        not null primary key,
    identity_id uuid        not null references identity (id),
    reported_at timestamptz not null,
    metadata    jsonb       not null
);

create index identity_metadata_identity on identity_metadata (identity_id, reported_at);
```

What an agent reported about itself, kept as a trail beside the current one on
`identity`.

## Machines

The first record of the product itself (`CONTEXT.md`, Machine).

```sql
create table machine (
    id          uuid          not null primary key,
    key         varchar(64)   not null,
    name        varchar(200)  not null,
    hostname    varchar(200),
    kind        text          not null check (kind in ('vps', 'dedicated', 'vm', 'local')),
    host_id     uuid          references machine (id),
    provider    varchar(200),
    plan        varchar(200),
    location    varchar(200),
    os          varchar(200),
    arch        text          check (arch is null or arch in ('amd64', 'arm64')),
    cpu         varchar(200),
    memory      varchar(200),
    disk        varchar(200),
    ipv4        varchar(200),
    ipv6        varchar(200),
    private_ip  varchar(200),
    ssh         varchar(200),
    status      text          not null check (status in ('planned', 'active', 'retired')),
    measured_at timestamptz,
    description text          not null default '',
    created_by  uuid          not null references identity (id),
    created_at  timestamptz   not null,
    updated_by  uuid          not null references identity (id),
    updated_at  timestamptz   not null,
    deleted_at  timestamptz,
    deleted_by  uuid          references identity (id),

    check (host_id is null and kind <> 'vm' or kind = 'vm'),
    check (host_id is null or host_id <> id)
);

create unique index machine_key  on machine (key);
create        index machine_host on machine (host_id);
```

**The key is the address and never changes.** `machine_key` holds it unique
across the instance — not per anything, because a machine has no parent
(`CONTEXT.md`, Key) — and it covers deleted rows, so a key stays spent for the
grace period. That a key is never reused *after* the purge is a rule of
deleting and is not yet held here.

**Hardware facts are text, `arch` excepted.** Machines are compared by eye and
never summed, and `2×512G NVMe ZFS mirror` is a truer description of a disk
than a number. `arch` is closed because it is the one hardware fact an agent
compares: the image it pulls depends on it. Every free-text fact is one line of
at most 200 characters — enough for a description, too little for a paragraph,
which is what `description` and the pages are for.

**`host_id` is the one relationship this table has**, and it points at itself.
Two rules are the column's: only a `vm` names a host, and no machine is its own
host. That a *longer* chain does not close on itself is the write path's — the
database cannot see it without walking — and it is walked before every write
that sets a host.

**`measured_at` is the answer to the stale document** (VISION 2): a machine
nobody has looked at for a year says so itself.

`ipv4`, `ipv6` and `private_ip` are stored as the text of a parsed address, so
that what comes back out is what an address parser accepted going in.

## Pages

The instance's flat wiki, addressed by a slug rather than a key
([planaffe ADR 0021](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0021-a-pages-address-is-its-slug-not-a-key.md)).

```sql
create table page (
    id         uuid        not null primary key,
    slug       text        not null,
    title      text        not null,
    body       text        not null default '',
    created_by uuid        not null references identity (id),
    created_at timestamptz not null,
    updated_by uuid        not null references identity (id),
    updated_at timestamptz not null,
    deleted_at timestamptz,
    deleted_by uuid        references identity (id),
    search     tsvector generated always as (to_tsvector('simple', title || ' ' || body)) stored
);

create unique index page_slug   on page (slug);
create        index page_search on page using gin (search);
```

**The search is a column, not a job.** The wiki is flat because search replaces
the navigation a tree would have been (VISION 7), so the search has to be part
of the row. `simple` rather than a language configuration: the content is
German and English and code, and stemming one of them wrongly is worse than
stemming none.

`page_slug` is both the uniqueness and the order a flat wiki is listed in.

## The history

Every change to something that has a history: who, when, which field, from what
to what (`CONTEXT.md`, History).

```sql
create table history (
    id         bigint      not null primary key generated always as identity,
    subject    text        not null check (subject in ('page', 'machine')),
    subject_id uuid        not null,
    actor_id   uuid        not null references identity (id),
    at         timestamptz not null,
    field      text        not null,
    old_value  text,
    new_value  text,
    note       text
);

create index history_subject on history (subject, subject_id, id);
```

**A row names its subject rather than being a column of it.** One table for
every kind of subject, because the mechanism is the same for all of them and a
second copy would be the one that drifts. The pair carries **no foreign key**,
which is deliberate twice over: it points at more than one table, and a row is
meant to outlive what it describes — VISION 7 wants the history of a deleted
machine to still say that it existed and when it went. The purge therefore
leaves history rows where they are, and a row whose subject has been purged
points at an id nothing answers to.

**`generated always as identity`**: a caller cannot bring its own id, so the
order of the ids is the order the rows were written and nothing else. That is
what makes `order by id` the history's order.

**A text records that it changed, not how.** A page's body and a machine's
description write a row with both values empty; the text itself is one read
away, and a history that carried every draft would be a second copy of the
wiki. Files are the exception the model makes, and they do not exist yet.

**The field names are the API's.** `status`, `measured_at`, `private_ip` — a
history row can be read beside the object without a translation table. `created`
and `deleted` are the two names that belong to no field.

## Idempotency

What a replayed write is answered from for 24 hours (`api.md`, Idempotency).
Not a Domain type: nothing the vision states is a rule about it, and it exists
only for the HTTP adapter.

```sql
create table idempotency (
    identity_id  uuid        not null references identity (id),
    key          text        not null,
    request_hash bytea       not null,
    status       smallint    not null,
    body         jsonb,
    created_at   timestamptz not null,

    primary key (identity_id, key)
);
```

The key is scoped to the identity that used it, so two agents cannot collide on
the same word. `request_hash` is what makes a repeat with a different body an
`idempotency-mismatch` rather than a wrong answer.

## What is derived rather than stored

**Nothing yet, and one thing soon.** A machine's fields are all written. The
first derived value is an installation's `version` — the version of its latest
deployment *by `at`*, not by the order of recording — and it arrives with the
deployment. It is written down here in advance because the rule it follows is
the one that would be easy to get wrong twice: derived means computed on read,
at one place, never a column that a write has to remember to refresh.

## The purge

There is no scheduler. At the end of every write transaction, up to twenty
deleted pages whose grace period has passed are removed, plus up to twenty
idempotency rows older than a day. The batch is small so that no request pays
for a backlog, and the floor is a floor: an instance nobody writes to keeps its
deleted rows longer. The write that would have paid for a scheduler does the
work instead.

The history is not purged. See above.
