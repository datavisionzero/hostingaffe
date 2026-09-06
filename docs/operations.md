# Operating an instance

What an operator needs after `docs/install.md`: the variables, the upgrade, the
backup, and where to look when something is wrong.

## The variables

All of them are read from the environment, and `deploy/.env` is where Compose
picks them up. Everything not listed as required has a default, and an instance
that sets none of the optional ones is a working instance.

### Required

| | |
|---|---|
| `POSTGRES_PASSWORD` | shared by the two services |
| `HOSTINGAFFE_BOOTSTRAP_ADMIN` | the first administrator's name |
| `HOSTINGAFFE_BOOTSTRAP_EMAIL` | their address |
| `HOSTINGAFFE_BOOTSTRAP_TOKEN` | their first user token, at least 32 characters |

The three bootstrap variables are read on the first start only. Missing, they
stop Compose before anything runs, and the message names the one that is
missing.

### The instance

| | default | |
|---|---|---|
| `HOSTINGAFFE_PORT` | `8080` | the whole left half of the published port, so `127.0.0.1:8080` binds there and nowhere else |
| `HOSTINGAFFE_IMAGE` | `ghcr.io/…/hostingaffe:latest` | `:main` follows the trunk, a `sha-<commit>` or a version stands still |
| `HOSTINGAFFE_TRUSTED_PROXY` | unset | an address, a CIDR network, or `all`; unset, every request looks as if it came from the proxy |
| `HOSTINGAFFE_DELETION_GRACE_DAYS` | `7` | how long a deleted row can be restored before the purge may take it |

A value the instance will not accept stops the start with one line naming the
variable, rather than being rounded to something nobody asked for or leaving a
stack trace in a container that restarts every few seconds.

### Transactional email

Optional, and off when `HOSTINGAFFE_SMTP_HOST` is unset (VISION 9). Without it
the instance runs and everything but the four acts that need a mail works;
those refuse with `smtp-not-configured` and say so.

| | |
|---|---|
| `HOSTINGAFFE_PUBLIC_URL` | the address the links in the mails have to lead to; **https outside Development** |
| `HOSTINGAFFE_SMTP_HOST`, `_PORT` | the server |
| `HOSTINGAFFE_SMTP_USERNAME`, `_PASSWORD` | credentials, where it wants them; both or neither |
| `HOSTINGAFFE_SMTP_SECURITY` | `starttls` (default) or `tls`; **`none` only in Development** |
| `HOSTINGAFFE_SMTP_FROM_ADDRESS`, `_FROM_NAME` | the sender |

An invitation link is a secret in an email, which is why the last two are
rules and not suggestions: a plaintext hop or an `http` link would hand it to
whoever is listening. Both stop the start rather than degrading quietly, and
the line the instance writes names the variable.

`GET /api/admin/smtp` says whether it is configured, and the administration
screen sends a test mail to an address you name.

### Logs

| | default | |
|---|---|---|
| `HOSTINGAFFE_LOG_ENDPOINT` | unset | a logaffe instance; unset, the log goes to the console and a rolling file under `/app/logs` |
| `HOSTINGAFFE_LOG_TOKEN` | unset | the token for it |
| `HOSTINGAFFE_LOG_LEVEL` | `Information` | the floor |

The request log carries method, path, status and duration, and nothing an agent
wrote (VISION 13).

## Upgrading

```sh
docker compose exec db pg_dump -U hostingaffe hostingaffe > backup.sql
docker compose -f deploy/docker-compose.yml pull
docker compose -f deploy/docker-compose.yml up -d
```

The backup first, and not out of caution: migrations apply themselves on
startup and only ever run forward (ADR 0011). There is no downgrade path, so
going back a version means restoring the dump taken before the upgrade —
which is why it is the first line and not the last.

An older image in front of a database a newer one migrated refuses to serve
rather than serving against a shape it misunderstands. It says which migrations
it does not know about; start the version that wrote them, or restore.

## Backup

Everything the product stores is in Postgres — the pages, the identities, the
history, all of it. One dump is the whole backup:

```sh
docker compose exec db pg_dump -U hostingaffe hostingaffe > hostingaffe-$(date +%F).sql
```

Restoring is `psql` into an empty database, then starting the instance against
it. Take one before every upgrade, and on a schedule that matches how much work
you are willing to lose.

## On a host, not on the instance

`ha files sync` is the one command that touches a machine, and it belongs to
the host rather than to the instance. It **pulls**: it runs over SSH on the
machine itself, under the token of that session, and writes the installation's
current files into a directory.

```sh
ha files sync /srv/logaffe --inst logaffe-prod --dry-run   # what it would do
ha files sync /srv/logaffe --inst logaffe-prod             # and then do it
```

**It executes nothing.** No `docker compose up`, no reload, no check that
anything came up. It writes files and stops; what to do after it is in the
runbook of that installation.

**Nothing on the host holds a token.** There is no configuration file to leave
one in — the two environment variables come from the SSH session and go with
it, which is why a host that is handed on carries no credentials of this
instance.

It keeps `.ha-sync.json` beside the files. That file is the only state outside
Postgres, and it is what tells a file sync wrote from one that was always
there: **what sync wrote, sync clears away** when it leaves the record, and
**what sync never wrote, sync never touches**. Do not delete it lightly —
without it, sync takes every file in the directory for somebody else's and
stops clearing anything away.

A run that reports `in the way` exits 5 and has left something as it found it:
the record and the disk disagree about that path, and a person decides which is
right. Take the file into the record with `ha files put`, or take it out of the
directory.

## When something is wrong

**It does not come up.** `docker compose logs hostingaffe`. The two starts that
stop deliberately say so in one line: a migration that failed, and a bootstrap
secret the instance will not accept. Both leave the database untouched.

**It answers, but nobody can sign in.** The bootstrap runs once, and the log of
the first start says whether it created the administrator or found identities
already there. If the token was lost, a second administrator cannot be made
from the environment — it is a `psql` job or a restore.

**An invitation never arrives.** `GET /api/admin/smtp` first: unset is not
misconfigured. Then the test mail from the administration screen, which reports
what the server said.

**A version disagreement.** `ha version` prints both sides and whether they
fit. A CLI of another major, or older than the instance's minor, exits 9 and
says which of the two moves.
