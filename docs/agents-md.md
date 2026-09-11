# The AGENTS.md Block

An agent that is about to work on a host needs to know where the record of that
host is and how to reach it ([Vision §8](../Vision.md#8-what-an-agent-does-here)).
hostingaffe is one record across every machine, so that paragraph can be written
once instead of once per repository — and this is it.

The block below is meant to be **copied verbatim** into the `AGENTS.md` (or
`CLAUDE.md`, or whatever the harness reads) of a repository whose hosts are
recorded in a hostingaffe instance. Change nothing in it: it names no file that
only exists here, and it says only what an agent has to know to read a host,
change it, and write down what it did.

It is deliberately short. [`cli.md`](./cli.md) is the complete surface — every
object, every verb, every flag — and a block that listed all of that would not
be copied. This is the handful of commands an agent actually runs.

---

````markdown
## The hosts

What this repository runs on is recorded in hostingaffe, and that record is the
truth: the machines, what is installed on them, which versions run, which files
each installation runs with, and the runbooks and decisions that go with them.

`ha` needs `HOSTINGAFFE_URL` and `HOSTINGAFFE_TOKEN` in the environment and
nothing else. That token is yours, handed in by whatever started you; there is
nothing to log into and nothing to store. It is never interactive, writes data
to stdout and errors to stderr, and `--json` prints the object as the API
answered it. Exit codes say what happened: 2 usage, 3 not found, 4 refused,
5 conflict, 6 stale, 7 denied, 10 unreachable.

### Before touching a host

```sh
ha machine context caddy
```

One call, one Markdown document: the machine and its fields, its installations
with the version each runs, their ports, directories and file lists, the last
deployments, the software, and every page that applies — the runbooks of that
host and the decisions that hold on all of them. Read that and nothing else.

An installation has two directories, and the document says both: `path`, where
it is deployed from and where its files lie, and `data`, where its persistent
data lies — the one a backup has to take and the one a `docker compose down -v`
does not bring back. Where a host keeps both in one directory, both say it.

File *contents* are not in it. They are one call away:

```sh
ha files get compose.yml --installation logaffe-prod
ha files list --machine caddy
```

### Looking something up

```sh
ha search "18502"        # a port, an address, a name, a word in a page or a file
ha inst view logaffe-prod
ha machine list
```

`ha search` matches whole words the way Postgres splits text, so a fragment
inside a path is not a word — a port number is looked up as a number.

### Changing configuration

The record is written first, the host second:

```sh
ha files get compose.yml --installation logaffe-prod --json    # read it, and the revision with it
ha files put compose.yml --installation logaffe-prod --file ./compose.yml \
   --revision 7 --note "raised the memory limit"
```

`--revision` is what you last read. A write against a newer one is exit 6 and
nothing is lost; without it the write wins and the history says so. Every write
prints the revision it made.

Then, **on the host**, over SSH:

```sh
ha files sync /srv/logaffe --installation logaffe-prod --dry-run
ha files sync /srv/logaffe --installation logaffe-prod
```

`sync` writes the current files into place and stops. **It executes nothing** —
run whatever the runbook says afterwards (`docker compose up -d --wait`, a
reload) yourself. What sync wrote it clears away when it leaves the record; what
it never wrote it never touches, and reports `in the way` with exit 5 instead.

**A machine's files are not synced.** Each of them says which directory on the
machine it lies in — a unit under `/etc/systemd/system`, a script under
`/usr/local/sbin` — so there is no one directory to sync into, and
`sync --machine` refuses. Record it with its directory, and put it in place
yourself:

```sh
ha files put caddy-host-backup.service --machine caddy \
   --file ./caddy-host-backup.service --directory /etc/systemd/system --executable=false
ha files list --machine caddy                    # says where each of them belongs
ha files get caddy-host-backup.service --machine caddy | \
   sudo tee /etc/systemd/system/caddy-host-backup.service > /dev/null
```

### After the work

Record what you did, in the same session:

```sh
ha deploy logaffe-prod --version 1.4.0 \
   --ref ghcr.io/example/logaffe@sha256:… --ticket OPS-42 --note-file -
ha inst set logaffe-prod --backup active --data /srv/services/logaffe \
   --note "restic to the offsite bucket"
ha machine set caddy --os "Ubuntu 26.04 LTS" --measured-at 2026-09-05 --note "dist-upgrade"
ha page add logaffe-restore --title "Restoring logaffe" --kind runbook \
   --installation logaffe-prod --body-file -
```

**Every write takes `--note`**, and the note lands in the history beside the
change. A version change is a deployment; anything else is a `set` with a note
saying why. A runbook you wrote or corrected is a page.

**A page names the rest of the record with a link**: the target is a scheme and
an address — `[the runbook](page:backup-restore)`, and likewise `machine:ex44`,
`software:caddy` and `installation:app-1`. A relative path to a file in some
repository is not a link here — there is no tree to resolve it against, and it
renders as plain text. Nothing checks a body as it is written; `ha page check`
says which references point at nothing.

Never put a secret in the record — not in a file, not in a description, not in
a page. `secrets` on an installation holds the *names* of the secrets it needs;
the values live where secrets live. `.env` and anything under `secrets/` are
refused, and `.env.example` is welcome.

### Documenting a new host

One call from one file, rather than thirty commands:

```sh
ha machine add --file ./new-host.json --note "measured on 2026-09-05"
```

The file holds the machine, its software, its installations, their files and
their first deployments, and it is created in one transaction — all or nothing.
Its shape is what `ha export --dir` writes, so an export can be read back in.

### The commands you need

```sh
ha machine context KEY                  # everything about a host, before you touch it
ha search "…"                           # where was that again
ha files get PATH --installation KEY    # one file; --json gives the revision with it
ha files put PATH --installation KEY --file ./x --revision N --note "…"
ha files sync DIR --installation KEY    # on the host: write the files into place
ha deploy KEY --version 1.4.0 --note-file -
ha inst set KEY --note "…"              # and `ha machine set`, `ha software set`
ha page add SLUG --title "…" --kind runbook --installation KEY --body-file -
```

Everything else — deleting and restoring, the history of anything, comparing
two revisions of a file, exporting the whole record — is `ha <object> --help`.
````
