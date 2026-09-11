# The CLI

`ha` is hostingaffe from the console: the interface for agents and
console-minded humans (VISION 6.1), a client of the public API and nothing else
(planaffe ADR 0003), built as one static binary from `src/cli/`. Its shape is
`ha <object> <verb>`, like `gh` and `glab`.

## Getting it

One static binary per platform, on the
[release page](https://github.com/datavisionzero/hostingaffe/releases/latest).
It links against nothing and needs no runtime, so installing it is putting the
file on the `PATH`:

```sh
curl -fsSLo ha https://github.com/datavisionzero/hostingaffe/releases/latest/download/ha_linux_amd64
chmod +x ha && sudo mv ha /usr/local/bin/ha
```

The name is `ha_<os>_<arch>` — `linux` and `darwin` in both `amd64` and
`arm64`, and `windows` in `amd64` as `ha_windows_amd64.exe`. There is no
version in it, so the URL above keeps working; `latest/download/` resolves to
the newest stable release and never to a prerelease, and an installation that
wants a version that stands still names the tag instead of `latest`.

`checksums.txt` is beside them, because a download nobody can verify is a
download nobody should run:

```sh
curl -fsSLO https://github.com/datavisionzero/hostingaffe/releases/latest/download/checksums.txt
sha256sum --ignore-missing -c checksums.txt   # shasum -a 256 on macOS
```

`ha --version` says which version this binary is, and `ha version` says whether
it and the instance fit.

## Signing in

A person signs in once per machine, and never types a token into a shell
profile again ([ADR 0005](./adr/0005-ha-login-is-the-device-code-flow-and-the-session-lives-in-the-keychain.md)):

```sh
ha login --url https://hosting.example.com
```

`ha` prints a short code — `BCDF-GHJK`, eight consonants so that it is never a
word and carries no digit anybody retypes — and the address to enter it at. A
person opens that address in a browser **on whatever machine has one**, signs in
if they are not already, and approves. `ha` collects the token and puts it in
the operating system's keychain. That is the only sign-in that works over SSH,
in CI, in a container and in an agent's sandbox, where there is no browser on
the machine doing the asking.

What comes back is an ordinary **user token**: it appears in `ha token list` and
in the browser's settings, and is revoked in either. `ha logout` revokes the one
this machine holds and takes it out of the keychain.

**Where there is no keychain** — a headless Linux without a Secret Service,
most often — `ha` says so, writes nothing, and names the two ways on: a token in
`HOSTINGAFFE_TOKEN`, or `ha login --token-file <path>`, which writes it `0600`
and records the path. A token file others can read is refused on the way back
in, with the `chmod` that fixes it.

**An agent never runs `ha login`.** Its token arrives in `HOSTINGAFFE_TOKEN`,
set by whatever harness started it, and `ha logout` refuses to revoke a token it
did not put there.

## Which instance, and as whom

Two questions, each answered through a ladder, and `ha status` prints both with
the rung each answer came from.

**The instance** — `--url`, then `HOSTINGAFFE_URL`, then the instance this
machine signed in to.

**The token** — `HOSTINGAFFE_TOKEN`, then the token file if one was chosen, then
the keychain. The environment wins because that is how an agent receives its own
token and how CI holds one. `ha` never says which kind it holds: the server
tells a user token from an agent token (planaffe ADR 0015).

```
$ ha status
instance   https://hosting.example.com
version    ha 1.2.0, instance 1.2.0
token      user, from the keychain
acting as  maintainer, administrator
```

Everything on disk is one file — `$HOSTINGAFFE_CONFIG`, else
`$XDG_CONFIG_HOME/hostingaffe/config.json`, else
`~/.config/hostingaffe/config.json`, written `0600`. It holds the instance and,
where one was chosen, the *path* of the token file. **It holds no credential.**

There is still no project file, because there are no projects: one instance
holds one team's infrastructure and every token reads all of it (VISION 9). A
machine is named on the command line where a command needs one, and never
inferred from the directory `ha` happens to run in.

| | |
|---|---|
| `HOSTINGAFFE_URL` | the instance, scheme and host |
| `HOSTINGAFFE_TOKEN` | a user token or an agent token |
| `HOSTINGAFFE_INSECURE_HTTP` | `1` allows plain HTTP off loopback, like `--insecure-http` |
| `HOSTINGAFFE_CONFIG` | where the configuration file lives |

No instance at all is exit 2, and the message names both ways to say which one.
So is an address that is not an absolute `http` or `https` one, said before any
request goes out.

### Plain HTTP is refused off loopback

A token over plain HTTP is a token in somebody's network log
([ADR 0006](./adr/0006-a-token-never-travels-over-plain-http-off-loopback.md)).
`https://` is always fine, and `http://` to a loopback host — `localhost`,
`127.0.0.1`, `::1` — is always fine, so a development instance works out of the
box. Anything else is refused before the first request, and the override is
explicit: `--insecure-http`, or `HOSTINGAFFE_INSECURE_HTTP=1`.

## Commitments to agents

- **Data to stdout, errors to stderr**, always. `--json` prints the object as
  the API answered it, and nothing else on stdout.
- **Never interactive.** No prompt, no editor, no pager; stdin is read only
  where a flag says so — `--body-file -` and its like. There is nothing to
  answer, so a command in a pipeline behaves as it does at a terminal. `ha
  login` waits, which is not the same thing: it prints a code and polls, and
  reads nothing from stdin.
- **Every write carries an `Idempotency-Key`** `ha` generates itself, one per
  invocation and numbered per write — `<key>-1`, `<key>-2` — so a retry after a
  lost connection replays every request of the command rather than repeating
  it. Two invocations never share a key.
- **`User-Agent: ha/<version> (<os>/<arch>)`** on every request, and the
  instance's `Hostingaffe-Version` compared on every answer: an `ha` of another
  major, or older than the instance's minor, stops with exit 9 and says which
  of the two moves (planaffe ADR 0011). A development build of either side is
  not checked.
- **A success has to be the shape the contract promises.** The instance serves
  the web application from the same port and falls back to `index.html` for
  every path no endpoint took, so an endpoint this `ha` knows and that instance
  does not answers `200` with a page of HTML. That is exit 1 saying so, never a
  crash: a body that is not JSON is not a success, whatever the status says.
- **A guarded write sends `If-Match`** with what was last read, where a command
  offers the guard; the instance refuses a write over somebody else's with exit
  6 rather than letting it win silently. **What has revisions is guarded with
  the revision, and what has none with the stand**: a file takes `--revision`,
  everything else `--if-match`.

## Exit codes

Derived from the status and the problem document, so that a script branches on
a number rather than on a sentence:

| exit | meaning |
|---|---|
| 0 | success |
| 1 | unexpected: a 500, an answer `ha` cannot parse, a bug in `ha` |
| 2 | usage: bad arguments, no instance, no token, a malformed address, a token file others can read |
| 3 | not found, deleted included |
| 4 | refused: validation, and every 422 |
| 5 | conflict: `idempotency-mismatch`, `email-exists`, `last-administrator` |
| 6 | stale: `If-Match` did not match |
| 7 | denied: 401, 403, and a device login somebody refused or let expire |
| 9 | version skew |
| 10 | unreachable: DNS, connection refused, timeout, TLS |

`8` is not given away. It was `next` finding nothing, which is planaffe's and
not this product's, and it stays free for whatever answers "there is nothing"
here — so that no script has to relearn a number.

## Who may do what

The line of VISION 9, held by the instance and reported by `ha` as it comes:

- Every token reads everything and writes content. An agent creates pages, and
  so does a user.
- **An agent administers no identities** (planaffe ADR 0015). `ha user …`,
  `ha agent …` and `ha token …` under an agent token are `forbidden`,
  exit 7, and an agent token never carries the administrator role.
- An administrator invites users, grants and revokes the administrator role,
  and deactivates and reactivates.

`ha me` says which of the two the token in the environment is, and `ha version`
says whether this binary and that instance fit.

## Verbs

`ha --help` lists the objects, and `ha <object> --help` the verbs under each.
This is the complete surface; the handful of commands an agent actually runs is
the block of [`agents-md.md`](./agents-md.md), written to be copied into a
user's own repository.

| object | verbs |
|---|---|
| `ha machine` | `list`, `view`, `add`, `set`, `delete`, `restore`, `history`, `context` |
| `ha software` | `list`, `view`, `add`, `set`, `delete`, `restore`, `history` |
| `ha installation` | `list`, `view`, `add`, `set`, `delete`, `restore`, `history` |
| `ha deployment` | recording is the bare verb; then `list`, `view`, `set`, `delete`, `restore`, `history` |
| `ha files` | `list`, `get`, `put`, `diff`, `revisions`, `delete`, `restore`, `history`, `sync` |
| `ha search` | one call over every field, every Markdown body and every file |
| `ha export` | the whole record as a Markdown tree with the files in place, plus JSON |
| `ha page` | `list`, `view`, `add`, `set`, `rename`, `delete`, `restore`, `history`, `check` |
| `ha login`, `ha logout`, `ha status` | signing this machine in, out, and what it holds (ADR 0005) |
| `ha me`, `ha version`, `ha user`, `ha agent`, `ha token` | the foundation's, unchanged |

`ha inst` is `ha installation` and `ha deploy` is `ha deployment`; the objects
keep the glossary's words and the short forms are only short forms, and
`--inst` is `--installation` wherever that flag appears. There is no
`ha softwares`: the word is uncountable (`CONTEXT.md`, Software).

**`ha machine add --file FILE`** is the bulk write: a whole host — its software,
its installations, their files and their first deployments — in one transaction,
because documenting a host is one act and not thirty commands. The file is the
JSON `ha export` writes, so export and import go in a circle, and `-` reads it
from stdin. All or nothing: a refusal anywhere leaves nothing standing. `ha`
does not read the document; it hands it to the instance, which is the one place
that knows what a record may hold.

**`add` and `set` take the same flags**, so that what a record can be created
with is what it can be corrected with. A flag left off leaves the field alone;
**a flag given empty clears a text field** — `--location ""` empties it. `set`
refuses to send a request that changes nothing, and says so as exit 2 rather
than as a write that did not happen.

**The closed sets are flags that name their values in the help**: `--kind`,
`--arch` and `--status` on a machine, `--environment`, `--role`, `--status`,
`--backup`, `--monitoring` and `--logging` on an installation. A value outside
one is the instance's to refuse and arrives as exit 4 — `ha` keeps no second
copy of the model.

**An installation is written with two directories**: `--path`, where it lives on
the machine, and `--data`, where its persistent data lies — the one a backup has
to take (ADR 0009). `view`, the export tree and `ha machine context` print both.
`files sync` writes into the directory it is given — which is the installation's
`path` — and knows nothing of `data`: what lies there is the machine's, and the
record only says where it is.

**A list is replaced whole**, never patched entry by entry: `--url`, `--secret`
and `--port` are repeated, what is given is what the list becomes, and the lone
value `none` clears it. A port is written and read the way a person writes one,
`443/tcp:public`; the field itself is the object, and `ha` converts.

**A secret is written as `NAME@/the/file/it/lies/in`**, or as the bare name
where nobody has decided where the value goes — never as a value
(ADR 0011):

```sh
ha inst set logaffe-prod \
  --secret POSTGRES_PASSWORD@/opt/compose/logaffe/.env.runtime \
  --secret SMTP_PASSWORD
```

Moving one is a new list with the new file in it, and the history says what the
list became. `ha search POSTGRES_PASSWORD` and `ha search
/opt/compose/logaffe/.env.runtime` both find the installation.

**`--measured-at` takes a day** — `2026-09-05`, meaning midnight UTC — or a full
RFC 3339 timestamp. Anything else is exit 2, said before any request goes out.

**Every write takes `--note`**, and the note lands in the history next to the
change, so that "why" is recorded where "what" is (ADR 0004). One line; an empty
one is the same as none, and a change that touches three fields writes the note
on all three rows. `ha <object> history KEY` is where it is read back.

`--description` is one line on the command line; `--description-file` reads it
from a file or from `-`, and naming both is exit 2. `view` prints the fields
that are filled in and then the description as it is stored, so the output can
be piped straight back into `--description-file -`.

**Retiring is not deleting** (`CONTEXT.md`, Retired and deleted). The normal end
of a machine or an installation is `set KEY --status retired`, which keeps
everything and only leaves the default list; `--retired` on `list` puts them
back and `--status retired` asks for exactly them. `delete` is for mistakes, is
undone by `restore` for the grace period, and a key it burns is never given out
again.

## Deployments

Recording one is the **bare verb**, because that is what an agent types after
the work:

```sh
ha deploy logaffe-prod --version 1.4.0 \
   --ref ghcr.io/datavisionzero/logaffe@sha256:… --ticket LOG-42 --note-file -
```

Only `--version` is required. `--note` is the deployment's own field — why, what
was checked, what went wrong — and `--note-file` reads it from a file or from
`-`. It is not the history note of ADR 0004: a deployment already carries the
why, and one word would not mean two things on one command.

**`--at` is what makes history backfillable**, and everything derived is ordered
by it, never by the order of recording. An installation's `version` is the one
of its latest deployment by `at`, so recording an old deployment with an `--at`
in the past leaves the running version where it is, and only fills in what came
before. It takes a day or a full RFC 3339 timestamp, like `--measured-at`.

`ha deploy list --inst KEY` gives the history of one installation, newest by
`at` first. The installation is named by a flag here rather than by a position,
because the position belongs to the recording verb. `ha deploy view KEY NUMBER`
is the complete deployment, with `previous` and the file revisions that were
current when it went live, spelled `compose.yml@4`.

**`ha deploy set` is narrow, and the border is the instance's**: `--ref`,
`--at`, `--ticket` and `--note`. The version and the installation are what the
record *is* — a deployment with the wrong version is `ha deploy delete`d and
recorded again, and its number is not handed out a second time. `--version` is
on `set` all the same, and it is sent: the instance's refusal says the rule,
which an unknown flag would not.

## Exporting

```sh
ha export --dir ./hosting-export
```

The escape hatch that makes the product safe to adopt: the whole record as a
Markdown tree with the files at their own paths, plus one JSON document that
carries all of it machine-readably, the history included.

```
hosting-export/
  README.md                                      what this is, and a table of the machines
  export.json                                    all of it, the history included
  machines/<key>/README.md                       the machine, its fields, what is on it
  machines/<key>/history.md                      who changed what, oldest first
  machines/<key>/files/<path>                    its own files, byte for byte
  machines/<key>/installations/<key>/README.md   the installation and its fields
  machines/<key>/installations/<key>/history.md
  machines/<key>/installations/<key>/deployments.md   what ran here, newest by `at` first
  machines/<key>/installations/<key>/files/<path>
  software/<key>.md
  pages/<slug>.md
```

**Two exports of the same record are the same bytes.** Nothing in the tree says
when it was written, and everything is ordered by its address, so an export can
be kept in a repository and diffed — which is how a record that has drifted
shows itself.

**`export.json` is the shape the bulk write reads back**: export and import go
in a circle, which is what gives migrating an old repository a defined target.

**It is composed from the ordinary endpoints**, not from an endpoint of its own:
whoever may read an export may make the single reads it is built from, and one
endpoint for it would be a second place that has to learn every entity the model
grows. That makes it many requests — an export is complete and rare, and that is
what pays for it.

**`--dir` must be absent, empty, or an export.** A directory holding an export is
replaced, because re-exporting is the ordinary thing to do; anything else is
left alone and said so before a single request goes out, because `ha` does not
remove what it did not write.

## Searching

```sh
ha search "18502"        # the installation that listens on it
ha search logaffe        # the software, the installation, the files that name it
ha search /srv/caddy     # everything that touches the directory
```

One call over every field, every Markdown body and every file. A line per hit:
the kind, the address, which surface matched, what it belongs to, and what it is
called. `--limit` asks for fewer; the instance caps it whatever is asked, because
a word that occurs everywhere would otherwise answer with the whole record.

The words are matched the way Postgres splits text, and a whole path is one of
those words: `/opt/compose/logaffe` finds the installation it is the directory
of. **A piece of a path is not a word**, so a search term that is one word with a
slash or a dot in it — `/srv/caddy`, `.env.runtime`, `docker-compose.yml` — is
looked for as a fragment as well, over the same surfaces, from three characters
up ([ADR 0012](adr/0012-a-path-is-found-by-its-letters-not-by-its-words.md)).
Two words are two words: `caddy /srv/caddy` is a word search and nothing else.

A port is a number rather than a word and is looked up as one, which is what
makes the first example answer. A file answers for the revision it is at, and a
deleted row is not a hit.

## Pages, and what they link to

A Markdown body names another thing of the record with a scheme and an address
([ADR 0007](adr/0007-a-record-is-linked-from-markdown-as-a-scheme-and-a-key.md)):

```md
Restore it as [the runbook](page:backup-restore) says, on [ex44](machine:ex44),
where [caddy](software:caddy) runs as [app-1](installation:app-1).
```

The scheme carries the type because the address does not — a key is unique per
entity type, so the machine `caddy` and the software `caddy` coexist. The web
application follows those four schemes to its own screens; everything else in a
body is a foreign link or plain text, and a relative path to a file in some
repository is the latter: the instance has no tree to resolve it against.

**Nothing checks a body as it is written.** The instance stores Markdown and
does not parse it, so a reference to a page nobody wrote is stored like any
other text, and renaming a page breaks whatever pointed at it — which planaffe
ADR 0021 always said it would.

```sh
ha page check                 # every page: what points at nothing
ha page check backup-restore  # just the one
```

A line per dead reference: the page it stands in, what it points at, and the
words it was written as. `--json` gives the same as a list.

**It is a report and not a gate.** It exits 0 whether or not it found anything,
because every exit code `ha` gives is derived from how the instance answered
(Exit codes, above), and a number `ha` invented from what it read would be the
first that is not. A script branches on `--json` — an empty list is a wiki
whose pages all still find each other. It is composed from the ordinary
endpoints, the way the export is, so it costs one read per page.

**A migrated repository gets its links written for it.** The bulk write takes a
`path` on each page — where that page's Markdown sat before — and rewrites the
relative `.md` links in the bodies onto the slugs the pages arrive as. See
[`api.md`](api.md), Importing; a path no page in the document claims is left
exactly as it was.

## Context

`ha machine context KEY` is the one call an agent makes before it touches a
host: everything recorded about the machine, as Markdown on stdout, in the
order that brings first what is needed first — the machine, its installations
with their version, ports and file list, the last deployments, the software,
the pages that hang on any of it, and the instance's `decision` pages.

**File contents are not in it**, and that is the point: they are a
`ha files get` away, and they are what would fill a context window. What comes
back is what the instance assembled — one request, because the command is
measured by what an operation costs in round trips and context, not by server
latency (VISION 4.1, 16). `--json` gives the object the API answered with, the
document inside it.

## Files

A file has no key. Its address is its **owner and its path**, and the owner is
named the way a page names what it hangs on — `--machine KEY` or
`--installation KEY`, the kind included, because the machine `caddy` and the
software `caddy` are different things. Naming both, or neither, is exit 2 said
before any request goes out.

```sh
ha files put compose.override.yml --inst logaffe-prod --file ./compose.override.yml
ha files get compose.override.yml --inst logaffe-prod > compose.override.yml
ha files list --machine caddy
ha files diff sites/logaffe.caddy --machine caddy

# a machine's file says which directory on the machine it lies in
ha files put caddy-host-backup.service --machine caddy \
    --file ./caddy-host-backup.service --directory /etc/systemd/system
```

**`put` writes, and creates what is not there yet.** It writes first and
creates on a not-found, so nobody has to know which of the two it is — except
with `--revision`, where a not-found is a not-found, because nobody read a
revision of a file that does not exist. Every write prints the revision it
produced.

**`--directory` is where a machine's file lies on the machine**, absolute, and
a machine's file has one: a machine has no single directory its files lie
under, the way an installation has its own `path` (ADR 0008). An installation's
file is refused one. It is not part of a revision, so `put` without `--file`
moves a file and asks for no text back — and a move of a file that is not there
stays a not-found rather than creating an empty one. `ha files list --machine
KEY` prints the directory beside each path; for an installation there is no
column, because there would be nothing in it.

**`list` answers for the owner, not for whoever cares.** A shared proxy's site
fragment lies under the proxy installation's path and is its file, so
`ha files list --inst caddy` has it and `ha files list --inst app-1` does not.
That is the model working, not a gap: the installation the fragment fronts names
it in its description, with a link to the proxy
([ADR 0010](adr/0010-a-file-has-one-owner-and-what-else-it-concerns-is-prose.md)),
and `ha search app-1` finds the fragment by what it says.

**`--revision` is the write guard.** It carries the revision last read, and a
write against a newer one is exit 6 with the instance saying which revision the
file is at. Without it the write wins and the history says so. An agent that
read before it writes passes what it read; one that puts a new file has nothing
to pass.

**`list` says how big each file is**, in bytes of UTF-8 and exactly — the same
count the one-megabyte cap is measured with, so the number beside a path is the
one a write is refused against.

**`get` is the content, byte for byte**, so that `ha files get … > file` writes
what the machine runs and not one line more. The revision that a write hands
back is in `--json`, which prints the record instead, and in `ha files list`.
`get --revision N` reads the file as it was at that write; `ha files revisions`
says which writes there were.

**`diff` puts two revisions side by side** as a unified diff. Without `--from`
and `--to` it is the last change, which is the question somebody usually has.

**`ha files sync DIR` is the one command that touches a machine, and it
pulls.** It runs *on* the host, under the token of the SSH session — which is
why there is no configuration file to leave a token in (VISION 9) — writes an
installation's current files into `DIR`, and stops. **It executes nothing**: no
`docker compose up`, no reload, no check that anything came up. What to do
after it is in the runbook, and the agent does it.

**A machine is not an owner it syncs.** `--machine` is exit 2, said before a
single request goes out, and names `ha files list --machine KEY` as what
answers the question instead: a machine's files each lie in their own
directory, and sync writes one. `ha files get` is what puts one of them in
place, and rebuilding a machine is the agent reading the record and writing
files the way it writes everything else (ADR 0008).

It keeps a **manifest**, `.ha-sync.json`, beside the files. That is the only
piece of state outside the instance, and it is what makes the command usable at
all, because it is the only way to tell a file sync put there from one that was
always there:

- **What sync wrote, sync clears away** once it has left the record — unless the
  host changed it since, and then it stops being sync's and the manifest forgets
  it, rather than an edit being thrown away.
- **What sync never wrote, sync never touches.** A file of the record with
  something else already at its path is reported `in the way` and left exactly
  as it is; everything else is still written, and the command is **exit 5**,
  because the directory is not what the record says and a script has to be able
  to tell.
- A file sync wrote that was changed on the host is `restored` and says so: the
  record is what the machine runs. **The mode counts as part of it** — the
  record has one mode bit, executable or not, and it is put on the file on every
  run, so a script the record calls executable is executable after every sync
  and not only after the first. A run that says `unchanged` changed nothing,
  mode included.

`--dry-run` prints the same lines and touches nothing. A directory holding one
owner's files is not another owner's to sync into, and that is said before a
single request goes out.

**The refused paths are not rebuilt here.** `.env` and every `.env.*` but
`.env.example`, anything under `secrets/`, and anything outside the owner's
directory are refused by the instance, and `ha` passes the refusal through as
exit 4 — the boundary is at the API, not at the client (VISION 10).

**A page carries the same verbs as everything else**, and `create`, `edit` and
`put` are gone rather than left as a second spelling — nothing is published and
an alias from day one is a promise nobody gets rid of later. `put` was
considered and turned down: a file has a path and a content and nothing else,
so putting it is the whole of what can be done with it, while a page has a
title, Markdown, a kind and what it hangs on, and a write touches one of them.
`rename` stays a verb of its own whatever the others are called, because moving
a page's address is not editing its text: nothing forwards, and every link to
the old slug stops working (planaffe ADR 0021).

A page carries the two fields the record gives it. `--kind` is `runbook`,
`decision` or `note` on `add` and `set`; left off at creation it is the
instance that applies `note`, not `ha`. What the page hangs on is named by
`--machine KEY` or `--installation KEY` — the kind belongs to the anchor,
because the machine `caddy` and the software `caddy` are different things —
and naming both at once is exit 2, said before any request goes out, since
`attached_to` holds one anchor and no request says two. Leaving the flags off
lets the page hang where it hangs; `--detach` is what gives it to the instance
as a whole. `ha page list` narrows by the same three: `--kind`, `--machine`,
`--installation`. A page has no revisions, so its guard is `--if-match` with
the `updated_at` last read, not `--revision`.

## Working on it

```sh
cd src/cli
go generate ./...     # the client, from ../../docs/api/openapi.json (not committed)
go vet ./...
go test ./...
go build ./cmd/ha
```

The generated client is `internal/api/client.gen.go`, produced by
`oapi-codegen` as a Go tool dependency of the module; CI runs the same
commands. A release builds `cmd/ha` per platform with
`-ldflags "-X github.com/datavisionzero/hostingaffe/src/cli/internal/version.Version=<tag>"`.
