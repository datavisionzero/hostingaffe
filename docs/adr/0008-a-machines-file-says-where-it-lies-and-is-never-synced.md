# A Machine's File Says Where It Lies, and Is Never Synced

A file owned by a machine carries a **`directory`**: the absolute directory on
the machine it lies in, `/etc/systemd/system`, `/usr/local/sbin`,
`/etc/docker`. A file owned by an installation carries none, because the
installation's own `path` is the one directory all of its files lie under. And
`ha files sync` takes an installation and refuses a machine: the directory is
written down, not written to.

## What forced it

A file's path is relative and has no leading slash, and a machine — unlike an
installation — has no `path` of its own. So the record could hold a systemd
unit and its content, and not the one fact that makes the content usable: where
it goes. Migrating the first host found 28 machine-level files under three
different roots — sixteen units and timers under `/etc/systemd/system`, eleven
backup and update scripts under `/usr/local/sbin` and `/usr/local/libexec`, and
`/etc/docker/daemon.json` — and nothing in the record able to tell them apart.
The repository they came from had the answer in prose, as a table in a runbook
next to an `scp` command. A fact every reader needs is a field, not a sentence
somebody has to find.

Vision 17 listed this as open and said it would be decided when the first host
was migrated. This is that decision.

## The alternatives

**Write the root into the path.** `etc/systemd/system/caddy-host-backup.service`
breaks no rule the Domain has, and needs no field at all. It makes `files sync`
meaningful only with `/` as its directory, and a command that writes — and
removes — under `/` on a production host is not one this product should own. It
also puts two different things in one string: the address the record is read by
and the place the machine expects.

**Put the location in the description.** Free, and unreadable to everything
that needs it. A record whose most important fact is prose is the repository
this product replaces.

**Keep only installation files.** It leaves the whole backup and restore system
of the machine outside the record — which is exactly the context an agent needs
before the first move.

**One directory per file, and `files sync` writes into each.** The field is the
same one; only the verb differs. It would need a second manifest location,
because `.ha-sync.json` lives *beside* the files it is about and scattered
files have no shared "beside", and it would make the one command that touches a
machine into a command that writes and deletes in `/etc` as root. That is a
deployment tool, and hostingaffe is not one (VISION 5, 13): the agent acts on
the machine, and the record says what is there. It stays available later
without a model change — sync could group a machine's files by directory and
keep a manifest per group — and is not taken now.

## Consequences

- A machine's file **must** name a directory when it is created; an
  installation's file is refused one. Both are `validation` on `directory`. A
  machine file recorded before this — there is no directory to invent for one —
  keeps none, and the check constraint allows that;
  `ha files put PATH --machine KEY --directory /etc/systemd/system` fills it in
  without handing the content back.
- The directory is **not part of a revision**. Moving a unit from one root to
  another is a change the history names, and the content stays at the revision
  it was at: where a file lies is not what it says.
- `ha files sync DIR --machine KEY` is **exit 2** and names
  `ha files list --machine KEY` as what answers the question instead. `ha files
  get` is what puts one file in place, and an agent that rebuilds a machine
  reads the record and writes the files the way it writes everything else.
- The export tree lists a machine's files with the directory each belongs in,
  because the export is what somebody reads while rebuilding a host.
