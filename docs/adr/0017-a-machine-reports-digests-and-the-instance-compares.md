# A Machine Reports Digests, and the Instance Compares

The drift check for configuration is a section of the report: the machine sends
a digest per file, and the instance compares it against the record. There is no
`ha files sync --check` that reads the record from a host, and no token that
would let one.

## The question this closes

[VISION 15.1](../../Vision.md#151-measuring-instead-of-typing) has ended on the
same sentence since the MVP: "Still open: the same idea applied to files. `ha
files sync --check`, reporting where the directory on the host differs from the
record, is the drift check for configuration, and it needs a token that reads."

The principle is built for facts, containers, ports and the restart. For
**files** there is nothing, and the question a person actually has about a host
— "is the `compose.override.yml` on the disk still the one in the record" — has
no answer in the product.

What held it up was never the arithmetic. `files sync` already knows: the
manifest `.ha-sync.json` beside the files carries an owner and, per path, the
digest of what sync wrote. What held it up is that `--check` on a cron would
have to **read the record** — the files of an installation, the installations of
a machine — and a machine token reads nothing at all
([ADR 0016](./0016-a-machine-token-posts-one-report-and-reads-nothing.md)).

## The decision

**The report carries digests, and the comparison happens on the instance.** A
machine sends, per directory it was given, the installation the manifest names,
where the directory lies, and for every path the manifest claims the SHA-256 of
what lies there now — or nothing, where nothing does. The instance hashes what
the record holds and computes a fifth drift kind, `file`, exactly as it computes
the other four.

**The token model is untouched.** A machine token still posts one report and
reads nothing, so ADR 0016 stands as written and
[VISION 17](../../Vision.md#17-open-points) loses its open point rather than
answering it. The narrower token kind is not built, and the reason is ADR 0016's
own: this product's content *is* the map, and a token that reads one machine and
its installations hands whoever takes that machine more than the disk does — the
runbook pages, where every secret lies, and through `depends_on` the names of
the neighbouring machines.

**A digest is not a content**, and this is what keeps the section on the right
side of "no secrets, ever". What a digest can be compared against is what the
record already holds, and the record refuses the paths that bear secrets — `.env`
and every `.env.*` but `.env.example`, anything under `secrets/`. The report
refuses them at the same door, so a collector cannot widen the boundary by
naming one.

**The manifest is the whole of what is reported.** A file `files sync` never
wrote is not in the manifest, is not hashed and is not named — which is the same
rule sync itself keeps, and what stops a report from carrying the file names of
whatever else lies in a compose directory. Every path that leaves the machine
came out of the record in the first place.

**The directories are named on the command line**, because the collector cannot
be told by the instance: `ha report send --sync-dir /srv/logaffe --sync-dir
/srv/caddy`. It is a small duplication of what the record already says, and it is
the price of the token model. It is explicit, it is one line in the cron, and it
is visible to whoever set the cron up.

**A report that names no directory hears nothing about files.** That is the rule
the machine's own ports already keep (ADR 0015, VISION 15.1): a comparison
nobody asked for is not made rather than answered with noise, because a drift
nobody can clear teaches people to stop reading the list. What it is not is a
suppression list — no single path and no single finding is silenced, and a
directory that is named is compared whole, an empty one included.

## What a finding is

Three, and one non-finding:

- A file the record has whose digest on the host is another one — changed on the
  host, or the record moved and sync has not run.
- A file the record has that lies nowhere on the host: the record's side is
  named, the machine's is nothing.
- A file that has left the record and is still lying there: the record says
  nothing, and that is the whole finding.
- A file sync never wrote is **no finding**. It is what sync never touches, and
  the report never named it.

Both sides are named as digests, shortened to twelve characters the way a commit
is, because that is the one value the two sides have in common: a file on a host
carries no revision, and a revision is not something a host can be asked for.
**Only the content is compared** and never the mode bit — the manifest hashes
bytes, and `files sync` puts the record's mode on the file on every run anyway.

## The alternatives

**`ha files sync --check`, run where an agent already sits.** Rejected, because
it is very nearly there already: `ha files sync --dry-run` prints the same lines
and touches nothing. A `--check` would be a second name for it plus an exit code,
and it answers only while an agent is on the machine — which is exactly when that
agent could type `--dry-run`. It buys an ergonomic and none of what the section
was wanted for.

**A machine-scoped token that reads.** Rejected above, and the reason is the map.

**Nothing at all.** Rejected: the bar VISION 13 sets is a question one can name,
and this one is the question VISION 15.1 already asked for facts — what runs on
the host differently from what the record says, for configuration rather than
for versions. Every finding it makes is clearable: either the record is brought
up to date, or the disk is.

**Contents rather than digests.** Never considered seriously, and named here so
that nobody considers it later: a machine that reports file contents is the thing
"no secrets, ever" exists to exclude, and it would make the report a second copy
of the record maintained in the wrong direction.
