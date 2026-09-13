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
| `HOSTINGAFFE_REPORT_RETENTION_DAYS` | `30` | how long a machine's reports are kept; the latest of each machine is never swept, and `0` keeps every one of them |

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

**Moving an instance is the same two commands** — a dump out of the old
database and a `psql` into the new one — and not `ha export` and
`ha machine add --file`. The export is the escape hatch: it hands the record
back as a Markdown tree and a JSON document that another instance can be
seeded from, and what it seeds begins there. The history, a file's earlier
revisions and the original timestamps are the instance's own and travel with
the database, not with the document ([`api.md`](api.md), Importing). An instance that
arrives somewhere else through an export is a new record of the same hosts;
one that arrives through a dump is the same instance.

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

**Sync writes where the installation lives, never where its data lies.** An
installation records two directories — `path`, the one it is deployed from, and
`data`, the one its persistent data lies in (ADR 0009) — and the second is
written down rather than written to. What lies there is the machine's, and a
command that rewrote it would be a restore nobody asked for.

**A machine is not an owner it syncs**, and `--machine` says so as exit 2. A
machine's files each say which directory on the machine they lie in, and sync
writes one; `ha files list --machine KEY` is where that question is answered,
and `ha files get` is what puts one of them in place (ADR 0008).

**No token that reads is on the host.** There is no configuration file to leave
one in — the two environment variables come from the SSH session and go with
it, which is why a host that is handed on carries no credentials of this
instance. The one token that does live on a host is the machine token below,
and it reads nothing at all
([ADR 0016](adr/0016-a-machine-token-posts-one-report-and-reads-nothing.md)).

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

## A machine that reports for itself

A host can hand in a **report** every quarter of an hour: a sign of life, plus
disk usage, memory, load and what Docker runs. The record stays written and the
report stays beside it — nothing here changes a field
([ADR 0015](adr/0015-a-machine-reports-and-the-record-stays-written.md)).

Setting it up is three steps, and none of them is automated: `ha` writes no
crontab entry and no systemd unit, because files end where execution begins
([VISION 13](../Vision.md#13-where-structure-stops)).

**1. See what would leave the host.** On the machine, before anything is sent
and without a token:

```sh
ha report collect
```

It prints the JSON `ha report send` would hand in. What it never carries: no
container environment, no process command lines, no file contents. That is the
same line that keeps secret values out of the record, and `collect` is how
anyone checks it without reading the code.

**2. Issue the machine its token**, from wherever you work — not on the host:

```sh
ha machine token issue ex44 > /tmp/ex44.token   # the secret is printed once
```

Put it on the host in a file of its own, owned by whoever runs the timer:

```sh
install -m 600 /dev/null /etc/hostingaffe.env
cat >> /etc/hostingaffe.env <<'EOF'
HOSTINGAFFE_URL=https://hosts.example.org
HOSTINGAFFE_TOKEN=ha_…
HOSTINGAFFE_MACHINE=ex44
EOF
```

The token belongs to that one machine and can do one thing: hand in a report for
it. It reads nothing — not an installation, not a file, not even its own machine
— which is why it may lie there at all. `HOSTINGAFFE_MACHINE` says which machine
this host is, because the token cannot: a token that could tell `ha` the key
would be reading something.

**3. Run it every quarter of an hour.** With cron:

```cron
*/15 * * * * . /etc/hostingaffe.env && /usr/local/bin/ha report send --quiet
```

Or with a systemd timer, on a host that has no cron:

```ini
# /etc/systemd/system/hostingaffe-report.service
[Unit]
Description=Hand in a report to hostingaffe

[Service]
Type=oneshot
EnvironmentFile=/etc/hostingaffe.env
ExecStart=/usr/local/bin/ha report send --quiet
```

```ini
# /etc/systemd/system/hostingaffe-report.timer
[Unit]
Description=Hand in a report to hostingaffe every quarter of an hour

[Timer]
OnBootSec=2min
OnUnitActiveSec=15min
RandomizedDelaySec=60

[Install]
WantedBy=timers.target
```

```sh
systemctl enable --now hostingaffe-report.timer
```

**Checking that it arrives.** From wherever you work:

```sh
ha machine list                  # the `last seen` column
ha report show ex44              # the last report, as a person reads it
ha machine token show ex44       # when the token was last used, if it was
```

**A run that fails is not caught up.** There is no buffer and no queue: the next
run is a quarter of an hour away and is the more current one anyway. `--quiet`
says nothing on success so that cron writes no mail every quarter of an hour;
failures go to stderr regardless, and the exit code says which half is wrong —
7 for a token the instance will not take, 10 for an instance it could not reach,
9 for a version skew.

**A section the collector could not determine is not a failure.** A host without
Docker reports no containers, says why, and exits 0 — the sign of life is the
point, and a cron that failed over a missing section would be switched off
within a fortnight.

**How long reports are kept.** Thirty days, and then the instance sweeps them
away — opportunistically, in the same stroke that already purges expired
deletions, with no scheduler and nothing new to operate. `HOSTINGAFFE_REPORT_RETENTION_DAYS`
moves the window and `0` switches the sweeping off. **The latest report of a
machine is never swept**, however old it is: a machine that has been silent for
six weeks must keep the one thing worth knowing about it, which is when it last
spoke and how it was doing then. There is no downsampling and no aggregate — if
thirty days is once too few, the answer is a larger number.

**Rotating and revoking.** `ha machine token issue ex44 --rotate` replaces the
token and revokes the old one at once, so the host fails visibly at its next run
until the new secret is in the file; `ha machine token revoke ex44` takes it back
for good. The reports a machine has already handed in stay.

## When something is wrong

**It does not come up.** `docker compose logs hostingaffe`. The two starts that
stop deliberately say so in one line: a migration that failed, and a bootstrap
secret the instance will not accept. Both leave the database untouched.

**It answers, but the form will not take the bootstrap token.** The form on `/`
is the ordinary sign-in and asks for an address and a password that do not exist
yet. The token belongs on `/activate`, which is a separate screen and is not
linked from the other one.

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
