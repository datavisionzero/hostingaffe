# Sync Stays, Because a Directory Has an Owner

`ha files sync` keeps writing an installation's files into the directory that
installation names, and keeps refusing a machine. Nothing about the command
changes. What changes is that the reason is written down: sync writes where the
directory belongs to what is written into it, which is one rule about ownership
rather than two rules about direction.

## What forced it

[ADR 0008](0008-a-machines-file-says-where-it-lies-and-is-never-synced.md) gave
a machine's file a `directory` and said sync would never write it. The sentence
it left behind — "the directory is written down, not written to: the agent acts
on the machine and the record says what is there" — reads like a statement about
the product as a whole. Taken that way it condemns `files sync` too: if the
record does not write to machines, why does it write to `/srv/logaffe`?

Nobody could answer that from the documents. `Vision.md` 7 named the machine
case and gave the deployment-tool reason for it, and the deployment-tool reason
is exactly what a reader then applies to the installation case, where the
product does the thing anyway. An asymmetry nobody can look up reads as an
oversight, and the next person to touch it — or the next agent — removes the
wrong half.

## The decision

**The rule is about the directory, not about the direction.** An installation
owns a directory. `path` is one line in the record, the directory it is deployed
from, and what lies in it is that installation's files. Sync can write the whole
set there and clear away what has left the record, and the worst it can reach
belongs to the installation it is syncing. A machine owns no directory. Its
files lie under `/etc/systemd/system`, `/usr/local/sbin`, `/etc/docker` — roots
that belong to the operating system — and a sync that removed from them would be
removing somebody else's. That is the whole of it: sweeping is safe where the
thing being swept owns the floor.

**The record was never shy of the disk.** VISION 13 says it in the line that
draws the boundary: "hostingaffe writes files into a directory when asked and
stops there. It does not run them, does not template them, does not know whether
the container came up." Writing is inside the line; executing is outside it.
ADR 0008 did not narrow that, and its own alternatives say so — "one directory
per file, and `files sync` writes into each" was turned down for needing a
second manifest location and for writing in `/etc` as root, not for writing at
all.

**What the manifest buys has no substitute.** `.ha-sync.json` is what makes "a
file that has left the record leaves the host" possible: sync knows which files
it wrote, so it can remove those and only those. A loop of `ha files get` writes
every file the record still has and is blind to the one it no longer has, which
stays on the host until somebody notices. That is not a convenience the command
adds on top of `files get`; it is the only thing in the product that closes the
loop.

## The alternatives

**Drop `files sync` and let ADR 0008 hold for both owners.** The honest reading
of the sentence that caused this, and the one the ticket asked about. It costs
the manifest and with it the only mechanism that removes a file the record
dropped; it costs a published verb, which `docs/cli.md`, `docs/operations.md`
and the block in [`docs/agents-md.md`](../agents-md.md) all name — and a block
naming a verb `ha` no longer has is worse than no block; and it breaks every
instance already syncing, which since `v0.1.0` is not a hypothetical. All of
that to make a consistency that was never inconsistent.

**Keep the verb and take away the writing** — sync becomes the drift check
VISION 15.1 sketches as `--check`, reporting where the host differs from the
record and touching nothing. It would resolve the apparent conflict at the same
price as dropping the command, and leave the manifest knowing only what it
itself once wrote, which is worth nothing once it writes nothing. `--check` is
still wanted, *beside* the writing, and stays roadmap.

**Say nothing and leave it as it is.** The state this came from. The command is
right, so nothing breaks — until somebody reads ADR 0008's last sentence as the
rule it looks like.

## Consequences

- **No code changes.** `sync.go`, the manifest, the refusal of `--machine` and
  every document that names the command stay exactly as they are.
- **`Vision.md` 7 states the rule positively**, in place of the paragraph that
  only said what a machine is not.
- **The next owner is measured by the same question.** Anything that grows a
  directory of its own can be synced into it; anything whose files lie in
  somebody else's directory cannot. That is a test, not a precedent to argue
  from.
- **`files sync --check` is unaffected.** It is roadmap (VISION 15.1) and is a
  reading beside the writing, never a replacement for it.
