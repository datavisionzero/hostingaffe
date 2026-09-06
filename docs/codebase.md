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
```

## The four layers

Dependencies point inward, and a test reads the four project files and fails a
reference that points outward (planaffe ADR 0002):

```
Api ──────► Application ──────► Domain
 └────────► Infrastructure ──►
```

**Domain** carries the rules and no packages: `Identities` (user, agent, token,
browser session, one-time secret), `Machines`, `Pages`, `History`, and at the
root what belongs to more than one of them — `Key`, the handle an operator
chooses; `Status`, the lifecycle a machine and an installation share;
`Spelling`, which turns a closed set's value into the word the contract, the
column and the history all use; and `Refusal` with `RefusalCode`, the one list
of every way the product says no, which the CLI derives its exit code from.

**Application** is the use cases. `Acts/` holds one class per act, named for
what it does — `CreatePage`, `CreateUser`, `AuthenticateToken` — plus the
shapes the contract serves (`…Shape`, the suffix the OpenAPI document drops).
`Ports/` holds the interfaces the acts need and the Infrastructure implements:
`IMachines`, `IPages`, `IIdentities`, `ITokens`, `IHistory`, `ITransactions`,
`IIdempotency`, `IEmailSender`, and the settings records read from the
environment.

**Infrastructure** implements them. `Persistence/` is EF Core: the
`HostingaffeDbContext`, one `IEntityTypeConfiguration` per table under
`Configurations/`, one store per port beside it, `Transactions` with the
opportunistic purge, and `Migrations/` — one migration, and every schema
change from here arrives as another on top (planaffe ADR 0011). `Email/` is
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
internal/config     the two environment variables
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
src/pages       the wiki
src/session     sign-in, activation, recovery
src/settings    personal settings and instance administration
src/shared      Markdown, dialogs, the editor, test helpers
src/components  the owned UI primitives
src/api         the generated client and its wrapper
```

The frame is rendered before any data arrives and is never remounted by
navigation (planaffe ADR 0006). Its routes are the instance's own addresses —
`/pages`, `/settings`, `/admin` — because the API is out of the way under
`/api`; in development Vite forwards that one prefix to the API and serves
everything else itself.

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
