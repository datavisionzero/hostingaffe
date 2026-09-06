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

`ha me`, `ha version`, `ha user`, `ha agent`, `ha token` and `ha page` are what
the foundation has; `ha --help` lists them, and `ha <object> --help` the verbs
under each. The table of the product's own objects — machine, software,
installation, deployment, file — is written when those objects exist.

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
