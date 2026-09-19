---
name: hostingaffe-new-installation
description: Record something new on a machine in hostingaffe — the software, the installation and its key, its files through the record, its dependencies, its secrets by name, its first deployment and its runbook. Use when a piece of software is being added to a host whose record lives in a hostingaffe instance. Not for installing software on a local development machine.
---

# Take a new installation into the record

Something new is going onto a machine. This workflow owns everything around
that — what has to be decided, in which order, and what has to be written down
so the next agent finds it — and nothing about the installing itself. How a
given software is installed is that software's business and the user's; what is
here is the part that is the same every time and gets forgotten in a different
place every time.

Read the `AGENTS.md` block of the repository you are in as the baseline; this
does not repeat it. Every write carries a `--note`.

## Check the tool first

Run `ha me`. Exit 2 means `HOSTINGAFFE_URL` or `HOSTINGAFFE_TOKEN` is missing:
report it and stop. Exit 9 is version skew and says which side moves, exit 10
is unreachable.

If the software is already installed somewhere and this is a version change,
this is the wrong workflow — `hostingaffe-update-installation` is that one.
`ha inst list --software KEY` settles it in one call.

## Is the software recorded at all

An installation is *of* a software, so the software comes first:

```sh
ha software list
ha software view app-1
```

If it is not there, record it once, for every machine that will ever run it:

```sh
ha software add app-1 --name "App One" \
   --repository https://github.com/example/app-1 \
   --image ghcr.io/example/app-1 \
   --note "first installation on ex44"
```

The image is recorded **without a tag** — the tag is a version, and a version
belongs to a deployment. The key is immutable and never handed out again, so
spell it the way every future installation will refer to it.

## Which machine, and what is already on it

```sh
ha machine list
ha machine context ex44
```

Read the whole document before adding to it. It says what is already installed,
which ports are taken, where installations keep their files and their data on
this machine, and which decision pages apply to everything on it. A new
installation that ignores the conventions of the six beside it is a new
convention nobody agreed to.

## Decide the key

The key is the address of this installation for as long as the record exists,
and it can never be reused, not even after a deletion. It is a decision, not a
formality.

Propose one that matches the keys already in use — if the machine holds
`logaffe-prod` and `logaffe-db`, then `app-1-prod` and not `app1_production` —
say which pattern you followed, and let the user object before you write it.
Where nothing establishes a pattern yet, `<software>-<environment>` is a
defensible default.

## Add the installation

```sh
ha inst add app-1-prod --machine ex44 --software app-1 \
   --environment production --role application \
   --path /srv/app-1 --data /srv/app-1/data \
   --url https://app-1.example.com --port 18502/tcp:private \
   --note "new installation, behind the shared proxy"
```

`--path` is where it is deployed from and where its files lie; `--data` is where
its persistent data lies — the directory a backup has to take and the one a
`docker compose down -v` does not bring back. Where a host keeps both in one
place, give both the same value rather than leaving one empty: a reader must not
have to guess which one was meant.

`--role` is what it is for the host: `application` if it is the point of the
machine, `platform` if other installations need it — a proxy, a database, a
log shipper. `--environment` is `production`, `staging` or `development`.

Record it while the work is still ahead with `--status planned`, and set it to
`active` when it runs. If you are writing this after the fact, `active` is the
default and correct.

`ha inst add` takes a `--version`, and it records the first deployment in the
same act. Use it when there is nothing more to say about that deployment. Where
you have the image digest and something worth writing down, leave it off here
and record the deployment properly at the end instead. Either way it is not a
field you correct afterwards — `ha inst set` has no `--version`, because an
installation's version is the version of its latest deployment.

## The files, record first and machine second

Never the other way round. A file on the machine that the record does not know
about is exactly the drift this product exists to prevent.

```sh
ha files put compose.yml --inst app-1-prod --file ./compose.yml \
   --note "first version"
ha files sync /srv/app-1 --inst app-1-prod --dry-run
ha files sync /srv/app-1 --inst app-1-prod
```

`sync` writes the files into the installation's directory and **executes
nothing**; running Compose afterwards is yours. Exit 5 is `in the way` — the
directory already holds a file at that path that sync never wrote. Look at it
before you decide anything: on a first installation it usually means somebody
put the service there by hand already.

`.env` and anything under `secrets/` are refused; `.env.example` is welcome.

**A file belongs to whoever's directory it lies in.** This is the rule that is
most often got wrong, and a new installation behind a shared reverse proxy is
exactly where it bites: the site fragment lies under the proxy's path and is the
**proxy's** file, not this installation's.

```sh
ha files put sites/app-1.caddy --inst caddy --file ./app-1.caddy \
   --note "TLS endpoint for app-1"
```

Then name it once in the new installation's own description, so a reader finds
it from either side:

```
Reached at https://app-1.example.com. The TLS endpoint is
`sites/app-1.caddy` in [caddy](installation:caddy).
```

```sh
ha inst set app-1-prod --description-file - --note "named the TLS endpoint"
```

A machine's own files — a systemd unit, a script under `/usr/local/sbin` — are
not synced and carry the directory they belong in instead:

```sh
ha files put app-1-backup.service --machine ex44 \
   --file ./app-1-backup.service --directory /etc/systemd/system \
   --note "nightly backup unit"
```

## Say what it hangs on

This is its own step because it is the field the next agent reads before
restarting anything, and because it is easy to finish an installation without
ever writing it.

```sh
ha inst view app-1-prod                  # read the list before you change it
ha inst set app-1-prod --depends-on caddy --depends-on app-1-db \
   --note "behind the shared proxy, on its own postgres"
```

The list is **replaced whole** — what you give is what it becomes, and `none`
clears it — so read it first, even on an installation you just created. Name
every installation this one needs to work, on this machine or another. The other
end, `needed by`, is derived and nobody writes it; it is what warns the next
agent that restarting the proxy takes this installation with it.

## Secrets by name, and the rest of the fields

```sh
ha inst set app-1-prod \
   --secret POSTGRES_PASSWORD@/opt/compose/app-1/.env.runtime \
   --secret SMTP_PASSWORD \
   --backup planned --monitoring external --logging central \
   --note "secrets in the runtime env file, backup still to set up"
```

A secret is written as `NAME@/the/file/it/lies/in`, or as the bare name while
nobody has decided where the value goes. **Never a value**, not in a field, not
in a file, not in a page. Keep that file at `0600` and out of every repository.

`--backup`, `--monitoring` and `--logging` are decisions, and `planned` or
`none` is an honest answer on the first day. Leaving them unset is not.

## Record the first deployment

```sh
ha deploy app-1-prod --version 1.4.0 \
   --ref ghcr.io/example/app-1@sha256:… --note-file -
```

This is what gives the installation its version. Use the digest of the image
that is actually running:

```sh
docker inspect --format '{{index .RepoDigests 0}}' app-1
```

## Leave the runbook behind

You have just learned how this installation is operated. Write it down while it
is in front of you, because the next agent — and the update workflow — will read
it instead of asking:

```sh
ha page add app-1-operating --title "Operating app-1" --kind runbook \
   --installation app-1-prod --body-file -
```

A page names the rest of the record with a link: `[caddy](installation:caddy)`,
and likewise `machine:ex44`, `software:app-1` and `page:backup-restore`. A
relative path to a file in some repository is not a link here and renders as
plain text. `ha page check` says which references point at nothing.

## Read it back

```sh
ha machine context ex44
```

That is the acceptance: the document the next agent will read, with the new
installation in it. Check that it names a version, a path, a data directory,
what it depends on, the secrets it needs and where its runbook is. Anything
missing there is missing for everyone.

## What this does not do

It does not install the software, invent operating instructions, write a secret
value, change another installation beyond naming this one in its description, or
bring an existing installation to a new version — that is
`hostingaffe-update-installation`.
