---
name: hostingaffe-update-installation
description: Bring a piece of software recorded in hostingaffe to a new version — resolve which installations run it, check what depends on them, change their files through the record, do what the runbook says, and record the deployment. Use when the user asks to update or upgrade something on a host. Do not use to add software that is not in the record yet.
---

# Update an installation

The user names a software — "update logaffe" — and this workflow carries it
through to a recorded deployment. It owns the procedure, not the software:
**how** a given installation is updated is in the record, in its description
and in its runbook pages. hostingaffe is not a deployment engine, and a
workflow that carried its own installation instructions would be wrong about
half the hosts and stale on the rest.

Read the `AGENTS.md` block of the repository you are in as the baseline; this
does not repeat it. Run every write with a `--note` saying why.

## Check the tool first

Run `ha me` before anything else. Exit 2 means `HOSTINGAFFE_URL` or
`HOSTINGAFFE_TOKEN` is missing from the environment: report that and stop —
there is no instance to guess and nothing to log into. Exit 9 is version skew
between this `ha` and that instance, and says which of the two moves. Exit 10
is unreachable, which is a fact about the network, not about the record.

## Resolve what is affected

The user says a name; the record holds a key. Map one to the other, then list
what runs it:

```sh
ha software list
ha inst list --software logaffe --json
```

Retired installations are out of that list already. Branch on what comes back,
and do not widen the search on your own:

- **Nothing.** Either the software is recorded and nowhere installed, or the
  key is spelled differently. Try `ha search logaffe` once, report what you
  found, and stop. Something that is not in the record is not updated here —
  that is `hostingaffe-new-installation`.
- **Exactly one.** Proceed with it, without asking. The user asked for the
  update; there is nothing to choose.
- **More than one.** Ask. List them with machine, environment and the version
  each runs now, and offer: one named installation, several, or all of them.
  Do not pick the production one because it looks most important, and do not
  quietly do all of them.

For "all", go **one at a time**, development and staging before production, and
**stop the remaining ones when one fails**. Half a fleet on a new version and
half on the old one is a state somebody has to be told about, not a run that
continues.

## Read the host before touching it

For each installation, in this order:

```sh
ha machine context ex44
ha history --machine ex44 --since 30d
```

`context` gives the version running now, the `path` the files lie in, the
`data` directory a backup has to take, the ports, the secrets and the file each
lies in, and every page that applies. `history` says what somebody did there
lately — a failed attempt three days ago changes what you are about to do.

**Read `needed by` and act on it.** It is the line that says what breaks when
this installation restarts. On a machine with a shared reverse proxy, restarting
the proxy takes down everything behind it; a database restart takes the
applications with it. If `needed by` is not empty and the runbook restarts the
installation, **name those installations to the user before you start** and say
what will be briefly unreachable. If any of them serves production and the user
did not ask for an outage, ask before proceeding. A dependency on another
machine is named with that machine — `caddy (on ingress-01)` — and it is one
hop: what a dependency itself depends on is one `ha inst view` away.

**The runbook is the update procedure.** Read the pages `context` lists, and the
installation's description. If none of them says how this installation is
updated, do not invent it: ask the user how it is done, do it their way, and
offer to leave a runbook page behind at the end so the next agent does not have
to ask.

## Change the files through the record

The record is written first and the machine second, always in this order:

```sh
ha files list --inst logaffe-prod
ha files get compose.yml --inst logaffe-prod --json           # the revision
ha files get compose.yml --inst logaffe-prod > ./compose.yml  # the content
```

`--json` prints the file's record and not its text, so it is two calls: one for
the revision to write against, one for the bytes to change. Change the image tag
in the working copy, then write it back with the revision you read:

```sh
ha files put compose.yml --inst logaffe-prod --file ./compose.yml \
   --revision 7 --note "logaffe 1.4.0"
```

Exit 6 means somebody wrote that file after you read it. Read it again, apply
your change to what is there now, and write again with the new revision. Never
drop `--revision` to make the refusal go away — that is the guard doing its job.

Then, **on the host**, over SSH:

```sh
ha files sync /srv/logaffe --inst logaffe-prod --dry-run
ha files sync /srv/logaffe --inst logaffe-prod
```

`sync` writes the installation's current files into its directory and stops. It
executes nothing. Exit 5 is `in the way`: a file of the record has something
else at its path that sync never wrote. Show the user which one and leave it
alone — it may be the thing somebody is missing.

A version lives in the file the record holds. A secret value does not: `.env`
and anything under `secrets/` are refused, and the record names a secret and the
file its value lies in, never the value.

## Do what the runbook says, then check

Run the runbook's commands — `docker compose up -d --wait`, a reload, whatever
it is — and verify the way it says to verify. Where it says nothing about
checking, at minimum confirm that the new version is the one actually running
and note how you confirmed it.

Get the digest of what is running, because that is what goes in the record:

```sh
docker inspect --format '{{index .RepoDigests 0}}' logaffe
```

## Record it, in the same session

```sh
ha deploy logaffe-prod --version 1.4.0 \
   --ref ghcr.io/example/logaffe@sha256:… --ticket OPS-42 --note-file -
```

Only `--version` is required; give `--ref` when you have the digest, and
`--ticket` when the user named one. The deployment's note is its own field —
what you checked, what went wrong on the way, what the next person should know.

Anything else the update changed is a `set` with a note: a new port, a data
directory that moved, a secret that now lies in another file.

```sh
ha inst set logaffe-prod --port 18502/tcp:private \
   --note "moved off the public interface"
```

Every list on an installation — `--port`, `--url`, `--secret`, `--depends-on` —
is **replaced whole** by what you give it, so read `ha inst view` first and pass
the entries that stay along with the one that changed.

A runbook you corrected while following it is `ha page set`; one you wrote
because there was none is `ha page add SLUG --kind runbook --installation KEY`.

**A run that changed the host and recorded no deployment is not finished.** If
the write fails, say so plainly and prominently: the machine and the record now
disagree, and only a person can decide which one is wrong.

## When the new version does not come up

The previous file is in the record, whole:

```sh
ha files revisions compose.yml --inst logaffe-prod
ha files get compose.yml --inst logaffe-prod --revision 7 > ./compose.yml
```

Put it back the same way it went out — `put` with the revision you last read,
`sync`, then the runbook — and record a deployment for the version that is
running at the end, with a note saying the newer one failed and how. A rollback
is a deployment like any other; the installation's version is whatever its
latest deployment says, so leaving it unrecorded makes the record claim a
version that is not there.

## What this does not do

It does not add software that is not recorded, retire or delete anything, write
a secret value into the record, or update a second installation because the
first one depends on it. Where another installation has to move too, say so and
let the user decide; then run this workflow again for that one.
