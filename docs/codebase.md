# The codebase

What is actually here, and where. Kept current: a file that lands somewhere
other than this describes means one of the two is wrong.

The foundation is a copy of planaffe with planaffe's domain cut out — copied,
not shared (VISION 12). The ADRs that chose the stack are planaffe's and are
referenced rather than rewritten.

```
src/Hostingaffe.Domain          the rules, no packages
src/Hostingaffe.Application     the use cases, and the ports they need
src/Hostingaffe.Infrastructure  the adapters: Postgres, SMTP, password hashing
src/Hostingaffe.Api             HTTP, and the composition root
src/cli                         `ha`, a Go module of its own
src/web                         the React application
tests/Hostingaffe.UnitTests     Domain and Application, no database
tests/Hostingaffe.IntegrationTests  the real thing against a real Postgres
deploy/                         Dockerfile, Compose, `.env.example`
docs/api/openapi.json           the contract, captured and checked in
docs/agents-md.md               the AGENTS.md block a user copies into their own repository
```

## The four layers

Dependencies point inward, and a test reads the four project files and fails a
reference that points outward (planaffe ADR 0002):

```
Api ──────► Application ──────► Domain
 └────────► Infrastructure ──►
```

**Domain** carries the rules and no packages: `Identities` (user, agent, token,
browser session, one-time secret, device login), `Machines`, `Installations`
(with the port and its two closed sets), `Files` (with the one list of refused
paths), `Deployments` (with `Derived`, which says what "latest" means), `Pages`,
`History`, and at the root what belongs to more than one of them — `Key`, the
handle an operator chooses; `AssignedKey`, the register that makes a key never
reusable; `Status`, the lifecycle a machine and an installation share; `Anchor`,
the machine-or-installation a file is owned by and a page attached to;
`Spelling`, which turns a closed set's value into the word the contract, the
column and the history all use; `Fields`, the shapes every editable field
shares; and `Refusal` with `RefusalCode`, the one list of every way the product
says no, which the CLI derives its exit code from.

`Software` is at the root too, and for a different reason: the word is
uncountable, so there is no plural to name a folder with, and a namespace
`Software` beside a type `Software` is an ambiguity every reference then has to
spell around.

Two type names collide with types the runtime imports everywhere —
`Installations.Environment` with `System.Environment`, `Files.File` with
`System.IO.File`. The files that need the model's say so with an alias: the
words are the glossary's, and a type named around a collision would be a word
`CONTEXT.md` does not have.

**Application** is the use cases. `Acts/` holds one class per act, named for
what it does — `CreatePage`, `CreateUser`, `AuthenticateToken` — plus the
shapes the contract serves (`…Shape`, the suffix the OpenAPI document drops).
`Ports/` holds the interfaces the acts need and the Infrastructure implements:
`IMachines`, `ISoftware`, `IInstallations`, `IFiles`, `IDeployments`, `IKeys`,
`IPages`, `IIdentities`, `ITokens`, `IDeviceLogins`, `IHistory`,
`ITransactions`, `IIdempotency`, `IEmailSender`, and the settings records read
from the environment.

**Infrastructure** implements them. `Persistence/` is EF Core: the
`HostingaffeDbContext`, one `IEntityTypeConfiguration` per table under
`Configurations/`, one store per port beside it, `Transactions` with the
opportunistic purge, and `Migrations/` — every schema change arrives
as another one on top, only ever forward (planaffe ADR 0011). `Email/` is
the SMTP sender, `Identity/` the Argon2id password hasher.

**Api** is HTTP and nothing else. `Http/` maps the endpoints, one file per
object, plus the cross-cutting pieces: `Problems` writes every refusal as one
problem document, `TokenAuthentication` and `BrowserSecurity` guard the door,
`IdempotencyMiddleware` replays a repeated write, `VersionHeader` puts the
instance's version on every answer, `Rfc3339` spells every timestamp.
`Hosting/` holds what runs before anything is served: the schema migration and
the bootstrap, in that order. `Program.cs` is the composition root and the only
place that knows all four layers — and the one that says where the API is:
every endpoint is mapped into the `/api` group, everything else is the web
application's, and `Routes` carries that prefix for what routing does not
write itself (ADR 0002).

## The CLI

`src/cli` is a Go module of its own and references nothing in `src/`: it is a
client of the public API and knows the instance only through the client
generated from `docs/api/openapi.json` (planaffe ADR 0003, 0005).

```
cmd/ha              the binary
internal/cmd        the command tree, one file per object
internal/client     the HTTP client, idempotency keys, version skew
internal/config     which instance, and as whom: the two ladders (ADR 0005)
internal/keychain   where a person's session lives, and nowhere else quietly
internal/exit       the exit codes
internal/problem    the problem document as `ha` reads it
internal/render     how it prints for a person
internal/version    what this build calls itself
internal/api        the generated client — not committed
```

`docs/cli.md` is what it promises.

## The web application

`src/web` is React on Vite, Tailwind and Base UI, `react-markdown` (planaffe
ADRs 0004, 0007, 0017). Its API layer is generated from the same
`docs/api/openapi.json` before every build, and is not committed either.

```
src/shell       the frame: sidebar, palette, shortcuts, routing
src/record      the machines, the software, the installations and the files
src/pages       the wiki
src/session     sign-in, activation, recovery, and approving a `ha login`
src/settings    personal settings and instance administration
src/shared      Markdown, dialogs, the editor, loading, test helpers
src/components  the owned UI primitives
src/api         the generated client and its wrapper
```

`src/record` is the product's own screens (VISION 6.2): a list and a detail for
each of the machines, the software and the installations, and one file screen
serving both of the things a file can hang on. `Parts.tsx` holds the sections
they share — a file list, the pages attached to something, the history, the
guarded description — because those screens are the same screen several times
over and a section that drifted on one of them would read like another product.
`addresses.ts` is where every address of the record is spelled, so that a key
is escaped the same way everywhere.

The frame is rendered before any data arrives and is never remounted by
navigation (planaffe ADR 0006). Its routes are the instance's own addresses —
`/machines`, `/software`, `/installations`, `/pages`, `/settings`, `/admin` —
because the API is out of the way under `/api`; in development Vite forwards
that one prefix to the API and serves everything else itself. A detail screen
arrives in a chunk of its own; the lists come with the frame.

A screen asks the instance through `useAsk` (`src/shared/ask.ts`), which is the
one place the rule lives that an answer belongs to the address it was asked
for: walking from one machine to the next never shows the previous one's
answer, and a slow answer that arrives after the walk is dropped rather than
rendered. Each section asks on its own, so the fields are readable while the
history is still coming and a section that fails says so in its own place.

## Storage

[`storage.md`](storage.md) is every table, what each column is for, which rules
the database holds and which the write path holds. EF Core declares them and
owns the migrations that apply themselves on start; that document is what the
declarations have to say, and a test compares its list of tables and indexes
against a migrated database.

## The contract

`docs/api/openapi.json` is served at `/api/openapi/v1.json`, captured from a
running instance and checked in.
Both clients are generated from it, and a test compares what an instance serves
against what is committed (planaffe ADR 0005). Regenerating it is that test
with `HOSTINGAFFE_CAPTURE_CONTRACT=1`, and the change is committed with the
change that caused it.

## The three toolchains

```sh
dotnet build Hostingaffe.slnx                  # the four projects
dotnet test tests/Hostingaffe.UnitTests        # seconds, nothing installed
dotnet test tests/Hostingaffe.IntegrationTests # Testcontainers brings up Postgres

cd src/cli && go generate ./... && go vet ./... && go test ./... && go build ./...

cd src/web && npm ci && npm run typecheck && npm run lint && npm run test && npm run build
```

CI runs the same commands, each toolchain in a job of its own, and builds the
image last.
