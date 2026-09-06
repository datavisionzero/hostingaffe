# The CLI

`ha` is hostingaffe from the console: the interface for agents and
console-minded humans (VISION 6.1), a client of the public API and nothing else
(planaffe ADR 0003), built as one static binary from `src/cli/`. Its shape is
`ha <object> <verb>`, like `gh` and `glab`.

## Configuration

Two environment variables, and nothing else — no file, nothing to log into:

| | |
|---|---|
| `HOSTINGAFFE_URL` | the instance, scheme and host |
| `HOSTINGAFFE_TOKEN` | a user token or an agent token; the server tells them apart, `ha` never says which it holds (planaffe ADR 0015) |

There is no project file, because there are no projects: one instance holds one
team's infrastructure and every token reads all of it (VISION 9). A machine is
named on the command line where a command needs one, and never inferred from
the directory `ha` happens to run in.

Either variable unset is exit 2, and the message names the one that is missing.
A `HOSTINGAFFE_URL` that is not an absolute `http` or `https` address is exit 2
as well, said before any request goes out.

## Commitments to agents

- **Data to stdout, errors to stderr**, always. `--json` prints the object as
  the API answered it, and nothing else on stdout.
- **Never interactive.** No prompt, no editor, no pager; stdin is read only
  where a flag says so — `--body-file -` and its like. There is nothing to
  answer, so a command in a pipeline behaves as it does at a terminal.
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
- **A guarded write sends `If-Match`** with the `updated_at` last read, where a
  command takes `--if-match`; the instance refuses a write over somebody else's
  with exit 6 rather than letting it win silently.

## Exit codes

Derived from the status and the problem document, so that a script branches on
a number rather than on a sentence:

| exit | meaning |
|---|---|
| 0 | success |
| 1 | unexpected: a 500, an answer `ha` cannot parse, a bug in `ha` |
| 2 | usage: bad arguments, `HOSTINGAFFE_URL` or `HOSTINGAFFE_TOKEN` unset or malformed |
| 3 | not found, deleted included |
| 4 | refused: validation, and every 422 |
| 5 | conflict: `idempotency-mismatch`, `email-exists`, `last-administrator` |
| 6 | stale: `If-Match` did not match |
| 7 | denied: 401, 403 |
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

| object | verbs |
|---|---|
| `ha machine` | `list`, `view`, `add`, `set`, `delete`, `restore`, `history` |
| `ha software` | `list`, `view`, `add`, `set`, `delete`, `restore`, `history` |
| `ha installation` | `list`, `view`, `add`, `set`, `delete`, `restore`, `history` |
| `ha page` | `list`, `view`, `create`, `edit`, `rename`, `delete`, `restore` |
| `ha me`, `ha version`, `ha user`, `ha agent`, `ha token` | the foundation's, unchanged |

`ha inst` is `ha installation`; the object keeps the glossary's word and the
short form is only a short form. There is no `ha softwares`: the word is
uncountable (`CONTEXT.md`, Software). Deployment and file are their own tickets
and are not here yet.

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

**A list is replaced whole**, never patched entry by entry: `--url`, `--secret`
and `--port` are repeated, what is given is what the list becomes, and the lone
value `none` clears it. A port is written and read the way a person writes one,
`443/tcp:public`; the field itself is the object, and `ha` converts.

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

A page carries the two fields the record gives it. `--kind` is `runbook`,
`decision` or `note` on `create` and `edit`; left off at creation it is the
instance that applies `note`, not `ha`. What the page hangs on is named by
`--machine KEY` or `--installation KEY` — the kind belongs to the anchor,
because the machine `caddy` and the software `caddy` are different things —
and naming both at once is exit 2, said before any request goes out, since
`attached_to` holds one anchor and no request says two. Leaving the flags off
lets the page hang where it hangs; `--detach` is what gives it to the instance
as a whole. `ha page list` narrows by the same three: `--kind`, `--machine`,
`--installation`.

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
