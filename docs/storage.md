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
create table device_login (
    id                  uuid        not null primary key,
    device_code_hash    bytea       not null,
    user_code           varchar(8)  not null,
    created_at          timestamptz not null,
    expires_at          timestamptz not null,
    approved_at         timestamptz,
    approved_by_user_id uuid        references identity (id),
    denied_at           timestamptz,
    redeemed_at         timestamptz,
    issued_token_id     uuid        references token (id)
);

create unique index device_login_code_hash on device_login (device_code_hash);
create unique index device_login_user_code on device_login (user_code)
    where approved_at is null and denied_at is null;
```

One `ha login` in flight (ADR 0005). The row keeps the **hash** of the long
device code the CLI polls with and never the code, the way a token row does;
what it can read back is the short user code a person types, which admits
nobody on its own. The unique index is on the ones still waiting, so that a code
a person types names exactly one login.

Where a login has got to is read from the four moments rather than stored as a
state: redeemed, then denied, then expired by `expires_at`, then approved, then
pending. That order is the order of what already happened — an approval nobody
collected in time reads as expired, so a device code left behind in a CI log is
worth nothing an hour later. `issued_token_id` says which token it produced, and
the poll that collects it claims the row in the same transaction: a device code
works once.

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

## Software

What an installation is an installation of (`CONTEXT.md`, Software). The word is
uncountable, so the table is `software` and the adapter over it is
`SoftwareRows` — there is no plural to name either of them with.

```sql
create table software (
    id          uuid          not null primary key,
    key         varchar(64)   not null,
    name        varchar(200)  not null,
    homepage    varchar(500),
    repository  varchar(500),
    image       varchar(500),
    description text          not null default '',
    created_by  uuid          not null references identity (id),
    created_at  timestamptz   not null,
    updated_by  uuid          not null references identity (id),
    updated_at  timestamptz   not null,
    deleted_at  timestamptz,
    deleted_by  uuid          references identity (id)
);

create unique index software_key on software (key);
```

**There is no version column, and there will not be one.** A software carries no
version — versions belong to deployments (VISION 7) — and a column repeating one
would be the second truth nobody keeps. `version` in a request body is
`unknown-field`, and the message says why.

**`image` is a container image name without a tag.** `caddy`,
`ghcr.io/datavisionzero/logaffe`. A `:tag` is refused rather than dropped,
because a caller who wrote one meant it, and what it meant belongs to a
deployment. A digest is refused for the same reason. `homepage` and `repository`
are absolute `http` or `https` addresses, checked where they are typed rather
than found broken by whoever clicks them.

`software_key` holds the key unique across the instance and is the order the
list is read in, and it covers deleted rows, so a key stays spent for the grace
period.

## Installations

One software installed once on one machine (`CONTEXT.md`, Installation), and the
second of the two relationships the model builds.

```sql
create table installation (
    id          uuid          not null primary key,
    key         varchar(64)   not null,
    name        varchar(200)  not null,
    machine_id  uuid          not null references machine (id),
    software_id uuid          not null references software (id),
    environment text          not null check (environment in ('production', 'staging', 'development')),
    role        text          not null check (role in ('application', 'platform')),
    status      text          not null check (status in ('planned', 'active', 'retired')),
    urls        text[]        not null default '{}',
    ports       -- a table of its own, below
    path        varchar(500),
    data        varchar(500),
    secrets     text[]        not null default '{}',
    backup      text          not null check (backup in ('none', 'planned', 'active')),
    monitoring  text          not null check (monitoring in ('none', 'external')),
    logging     text          not null check (logging in ('local', 'central')),
    description text          not null default '',
    created_by  uuid          not null references identity (id),
    created_at  timestamptz   not null,
    updated_by  uuid          not null references identity (id),
    updated_at  timestamptz   not null,
    deleted_at  timestamptz,
    deleted_by  uuid          references identity (id)
);

create unique index installation_key      on installation (key);
create        index installation_machine  on installation (machine_id);
create        index installation_software on installation (software_id);
```

**There is no `version` column and no `depends_on` column.** The first is
derived from the deployments and arrives with them; the second is roadmap
(VISION 15.2), and a nullable column prepared in advance would be a decision
taken quietly. Both are `unknown-field` in a request body, and the message says
which of the two reasons applies.

**Two directories, and no constraint between them.** `path` is where the
installation lives — the directory it is deployed from and the one every file it
owns lies under — and `data` is where its persistent data lies: what a backup
has to take (ADR 0009). Both are absolute paths the write path checks and the
column only bounds, and neither is required to differ from the other: a host
that keeps configuration and state in one directory says the same thing twice,
and that is an answer rather than a contradiction.

**Six closed sets, six check constraints.** `environment` and `role` answer two
different questions — whom the installation serves, and what it is for the
machine — and the three decisions are columns rather than prose because "every
production installation without a backup" is a question the product answers in
one query. Every one of the eight filters on the list is an equality on one of
these columns or on one of the two foreign keys.

`installation_software` is read whenever a software is asked what still hangs on
it, which is what refuses its deletion.

`urls` and `secrets` are arrays of text: nothing is read by them, and a join per
URL would buy nothing the row does not already say. `secrets` holds **names**,
never values — a name is one word, and a value with an `=` or a space in it does
not fit the shape.

```sql
create table installation_port (
    installation_id uuid not null references installation (id) on delete cascade,
    port            int  not null check (port between 1 and 65535),
    protocol        text not null check (protocol in ('tcp', 'udp')),
    scope           text not null check (scope in ('public', 'private', 'internal')),

    primary key (installation_id, port, protocol)
);
```

**A port is a row, not a string and not a document.** `protocol` and `scope` are
closed sets like every other one in the model, and a closed set is a column with
a check constraint that lists the words. The primary key is what makes a port
the same port — the number and the transport — so a second row for `443/tcp`
with another scope is a contradiction the database will not hold. The spelling
`443/tcp:public` is what a person reads and what a history row carries; it is a
rendering, and it is nowhere in the schema.

The cascade here is the only one in the file, and it is the hard delete's: these
rows are part of what an installation *is*, so when the purge finally removes
the installation row they go with it. A soft delete leaves them alone, because
it leaves the installation alone.

## Files

The text a machine runs with, kept next to the thing it belongs to
(`CONTEXT.md`, File), and the one place the history keeps **content**.

```sql
create table file (
    id              uuid         not null primary key,
    machine_id      uuid         references machine (id),
    installation_id uuid         references installation (id),
    path            varchar(500) not null,
    directory       varchar(500),
    created_by      uuid         not null references identity (id),
    created_at      timestamptz  not null,
    deleted_at      timestamptz,
    deleted_by      uuid         references identity (id),

    check (num_nonnulls(machine_id, installation_id) = 1),
    check (directory is null or machine_id is not null)
);

create unique index file_on_machine      on file (machine_id, path)      where machine_id is not null;
create unique index file_on_installation on file (installation_id, path) where installation_id is not null;

create table file_revision (
    file_id    uuid        not null references file (id) on delete cascade,
    revision   int         not null,
    content    text        not null,
    executable boolean     not null,
    by         uuid        not null references identity (id),
    at         timestamptz not null,

    primary key (file_id, revision)
);
```

**Exactly one owner**, and the check constraint is what says "exactly": a
systemd unit belongs to the machine, a Compose file to the installation, and
nothing belongs to both or to neither. The two partial unique indexes are what
makes `path` unique *per owner* and are the order an owner's files are read in;
they cover deleted rows, so a path stays spent for the grace period.

**`directory` is where a machine's file lies on the machine**, absolute, and
the second check constraint is what keeps it a machine's alone: an installation
says once, in its own `path`, where all of its files lie, and a second answer
per file would be the one that drifts (ADR 0008). It is not `not null`, because
a machine file recorded before the column existed has no directory to invent —
the instance refuses a new one without it, and `ha files put --directory` fills
an old one in. It is on the file and not on the revision: moving a unit from one
root to another is a change the history names, and the content stays at the
revision it was at.

**Nothing on `file` says what the file contains.** The content, the mode bit,
the revision number, its size in bytes and who last wrote it are the newest
revision's, computed on read. A column repeating any of them would be the second
truth a write has to remember to refresh — the same rule the derived version
follows. The size in particular is counted off the newest revision's content in
bytes of UTF-8, the same count the megabyte cap is measured with, so that what a
list shows is the number a write is refused against.

**Every write is a revision, and every earlier content stays.** That is what
makes rolling back a Compose file possible, which is the whole reason files are
the exception to "a text records that it changed, not how". A write that changes
neither the content nor the mode bit makes no revision: a revision repeating its
predecessor byte for byte is not a version of the file, and `files sync` writing
the whole set would otherwise number the history up without saying anything.

The mode bit is on the revision rather than on the file, so that reading a
revision gives the file as it was — a script that was runnable stays runnable.

Content is UTF-8, capped at one megabyte, and refused rather than replaced when
it is not text: VISION 7 rules binary out, and a byte turned into a question
mark is a file that no longer runs.

**The refused paths are a rule of the Domain, not of a client.** `.env` and
every `.env.*` but `.env.example`; anything under `secrets/` at any depth; and
anything outside the owner's directory — a leading slash, a `..`, a `.`. The
list is in `FilePath` and nowhere else, because the API is as open as the CLI
is. `.envrc` is welcome: in the template it is one line and carries no value.

## Deployments

The record that an installation changed version (`CONTEXT.md`, Deployment), and
the source of the version it runs.

```sql
create table deployment (
    id              uuid          not null primary key,
    installation_id uuid          not null references installation (id),
    number          int           not null check (number >= 1),
    version         varchar(200)  not null,
    ref             varchar(500),
    at              timestamptz   not null,
    ticket          varchar(64),
    note            text          not null default '',
    created_by      uuid          not null references identity (id),   -- `by`
    created_at      timestamptz   not null,
    updated_by      uuid          not null references identity (id),
    updated_at      timestamptz   not null,
    deleted_at      timestamptz,
    deleted_by      uuid          references identity (id)
);

create unique index deployment_number on deployment (installation_id, number);
create        index deployment_when   on deployment (installation_id, at desc, number desc);
```

**There is no status column.** A deployment is recorded when it is done. A
rollback is a deployment to the previous version with a note that says so; an
attempt that changed nothing is a note or a ticket, not a row here. Every row is
a version that really ran.

**A deployment has no key.** The instance numbers it per installation, counted
from one, and `deployment_number` is what makes that number an address. The next
number counts past deleted rows too: an address is not handed out twice.

**`at` is when the version went live and may be set**, so that history can be
backfilled. `created_by` — `by` on the wire — is who *recorded* it, which for a
backfilled deployment is not necessarily who deployed. `deployment_when` is the
index everything derived reads by.

`ticket` stays a string: it is a planaffe key like `LOG-42`, a reference to the
other product and not a word of this model.

## Pages

The instance's flat wiki, addressed by a slug rather than a key
([planaffe ADR 0021](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0021-a-pages-address-is-its-slug-not-a-key.md)).
A slug is one segment and carries no slash
([ADR 0003](adr/0003-a-pages-slug-is-one-segment-not-a-path.md)).

```sql
create table page (
    id              uuid   not null primary key,
    slug            text   not null,
    title           text   not null,
    kind            text   not null default 'note'
        check (kind in ('runbook', 'decision', 'note')),
    machine_id      uuid   references machine (id),
    installation_id uuid   references installation (id),
    body       text        not null default '',
    created_by uuid        not null references identity (id),
    created_at timestamptz not null,
    updated_by uuid        not null references identity (id),
    updated_at timestamptz not null,
    deleted_at timestamptz,
    deleted_by uuid        references identity (id),
    search     tsvector generated always as (to_tsvector('simple', title || ' ' || body)) stored
);

create unique index page_slug           on page (slug);
create        index page_on_machine      on page (machine_id)      where machine_id is not null;
create        index page_on_installation on page (installation_id) where installation_id is not null;
create        index page_search          on page using gin (search);
```

**`kind` says how a page is to be read, and nothing more**: no status, no
supersedes, no template enforced beyond it (VISION 7). Pages written before the
column existed are `note`, which is the kind that claims nothing, and the
default in the migration is what made them so.

**`attached_to` is one anchor or none**, and the check constraint says "one":
`machine_id` and `installation_id` are never both set. A page that hangs on
nothing belongs to the instance as a whole, which is a state the model has on
purpose. Neither foreign key cascades — a page does not follow its anchor into
deletion.

**The search is a column, not a job.** The wiki is flat because search replaces
the navigation a tree would have been (VISION 7), so the search has to be part
of the row. `simple` rather than a language configuration: the content is
German and English and code, and stemming one of them wrongly is worse than
stemming none. Every other searchable table carries the same column for the same
reason — see Searching, below.

`page_slug` is both the uniqueness and the order a flat wiki is listed in.

## Assigned keys

That a key was given out, once, and that it is therefore spent forever
(`CONTEXT.md`, Key).

```sql
create table assigned_key (
    kind text        not null check (kind in ('machine', 'software', 'installation')),
    key  varchar(64) not null,

    primary key (kind, key)
);
```

**Two columns, one question.** While a deleted row is in its grace period it
holds its own key and this table says nothing new; after the purge the row is
gone, and without something that remembers, the key would be free again — which
VISION 7 rules out. The primary key over both columns is what *enforces* the
rule rather than leaving it to a query somebody can forget.

It is not a bin and not a second history. The history was the other candidate —
it survives the purge and names the key — but it is there to be read, no index
could hold a uniqueness rule over it, and the rule would have become a query
somebody forgets. The migration that created this table filled it from the rows
that were already there: the rule is not "from now on".

Nothing points at these rows and they point at nothing. The purge does not touch
them.

## The history

Every change to something that has a history: who, when, which field, from what
to what (`CONTEXT.md`, History).

```sql
create table history (
    id         bigint      not null primary key generated always as identity,
    subject    text        not null
        check (subject in ('page', 'machine', 'software',
                           'installation', 'file', 'deployment')),
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

**A list records what it became.** `urls`, `secrets` and `ports` write their
entries as one line, separated by commas, and a port reads as `443/tcp:public`
there — a history row is text a person reads, which is exactly what that
spelling is for.

**A text records that it changed, not how.** A page's body and a machine's or a
software's description write a row with both values empty; the text itself is
one read away, and a history that carried every draft would be a second copy of
the wiki. Files are the exception the model makes, and they do not exist yet.

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

## Searching

Every table something is looked for in carries a stored `tsvector` and a GIN
index over it, generated from its own row:

| table | column | what goes into it |
|---|---|---|
| `machine` | `search` | the key, the name and every text field, description included |
| `software` | `search` | the key, the name, the image, the two URLs, the description |
| `installation` | `search` | the key, the name, the two directories, the urls, the secret names, the description |
| `deployment` | `search` | the version, the ref, the ticket, the note |
| `file` | `search` | the path |
| `file_revision` | `search` | what that revision said |
| `page` | `search` | the title and the body |

**A generated column, never a trigger.** The same reason the page's has always
been one: a job that fills it is a second write that can be forgotten, and a
column that is part of the row cannot fall behind it.

```sql
alter table machine add column search tsvector
    generated always as (to_tsvector('simple', key || ' ' || name || ' ' || … )) stored;

create index machine_search on machine using gin (search);
```

**`simple` everywhere**, for the reason the page gives: the content is German and
English and code, and stemming one of them wrongly is worse than stemming none.

**One function, and the schema owns it.**

```sql
create function words(value text[]) returns text
    language sql immutable parallel safe returns null on null input
    return array_to_string(value, ' ');
```

Postgres marks `array_to_string` **stable** rather than immutable, and a
generated column takes only immutable expressions — while for a `text[]` and a
constant separator the result depends on nothing at all. Without it an
installation's `urls` and `secrets` would be the two fields nobody could search
for. It is the only function in the schema.

**Ports are not in any of these columns.** They are rows of
`installation_port`, and `18502` inside the token `18502/tcp` is not something
anyone would find by typing the number. A query that *is* a port number is
looked up in that column instead, which is what makes VISION 5's own example
answer.

**A file is read at the revision it is at.** The query joins the revision whose
number is the highest for that file: what an older one said stopped being true
when the next was written.

**Deleted rows are not hits**, and neither is anything under a deleted owner —
a file of a deleted machine, a deployment of a deleted installation.

## What is derived rather than stored

Derived means **computed on read, at one place, never a column that a write has
to remember to refresh**. There is one such place so far and one more coming.

**A file's content, mode bit, revision number and last author** are the newest
revision's. `file` holds who put the file there and where it sits; everything it
*says* is in `file_revision`.

**An installation's `version`** is the version of its latest deployment *by
`at`*, and a deployment's **`previous`** is the version of the deployment before
it in the same order. **`files`** are the revisions of the installation's files
that were current at that deployment's `at`, and empty for one backfilled to
before the first file was put.

All three are ordered by `at` and never by the order of recording — counting by
recording order moves the present every time somebody backfills the past, which
is the mistake VISION 7 names. `at` can repeat, so the number breaks the tie and
is the only thing that does.

**The rule lives in one place**, `Derived`, and every read goes through it. That
is also why the deployment store hands back rows rather than an answer: a query
that said "the latest one" would be this rule written a second time, in SQL,
where nothing holds the two together.

## The purge

There is no scheduler. At the end of every write transaction, up to twenty rows
of each kind whose grace period has passed are removed, plus up to twenty
idempotency rows older than a day. The batch is small so that no request pays
for a backlog, and the floor is a floor: an instance nobody writes to keeps its
deleted rows longer. The write that would have paid for a scheduler does the
work instead.

**It runs from the leaves inward**: pages, then deployments and files, then
installations, then machines and software. Every step that removes a parent asks
that nothing still points at it, because the batch is capped and a child may be
waiting for the next write; what is left standing is picked up next time.

**A page loses its anchor here.** It does not follow a machine or an
installation into deletion — that is what keeps a restore able to bring the
whole picture back — but it cannot name one that is gone for good either, so the
purge sets `machine_id` and `installation_id` to null and the page becomes a
page of the instance.

**A device login is purged a day after it expired**, whatever became of it:
approved, refused or never answered, it is worth nothing ten minutes after it
was made, and the day's grace is so that somebody who ran `ha login` and walked
away still reads why it failed.

**The history is not purged, and neither are the assigned keys.** That is what
VISION 7 asks for twice over: the history of a deleted machine still says that
it existed and when it went, and the key it had is never given out again.
