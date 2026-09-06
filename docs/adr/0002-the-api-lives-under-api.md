# The API Lives Under /api

Every endpoint the instance serves is addressed under `/api`, and every other
path belongs to the web application. `GET /api/version`, `GET /api/pages`,
`GET /api/openapi/v1.json`; `/pages`, `/settings`, `/admin` are the
application's own addresses, and the SPA fallback answers all of them.

## What forced it

One process serves both the API and the web application, and the fallback rule
is "every path no endpoint took is the SPA's". In planaffe the two never met:
the application was at `/:project/pages`, the API at
`/projects/{key}/pages`. hostingaffe has no project dimension
([0001](./0001-the-foundation-is-a-copy-of-planaffe.md)), so both went flat and
landed on the same word.

The result was not subtle. `GET /pages` with `Accept: text/html` — a reload, a
bookmark, a pasted link — answered `401 application/problem+json` instead of
the application. Inside a running application nothing was wrong, because the
router navigates client-side and asks for no HTML; broken was exactly what a
person does daily.

`/pages` was the first collision, not the last one. `machines`,
`installations`, `software`, `deployments` are all words both worlds want, and
each of them would have arrived as the same bug in a different screen.

## The alternatives

**A prefix for the application** — `/app/pages` — leaves the contract
untouched, and makes every address a person shares carry a segment that means
nothing to them. The addresses humans read and send are the ones worth
protecting; the ones a generated client assembles are not.

**A fallback that reads `Accept`** is the smallest change and the worst rule.
"Every unclaimed path is the SPA's" becomes a rule about a header, two
endpoints answer at one address depending on who asks, and any client that
omits `Accept` still lands in JSON. It buys a fix for browsers by making the
contract depend on something the contract does not describe.

Both leave the namespace shared and the next collision unresolved. The prefix
is the only one that ends the class of bug rather than the instance of it.

## Consequences

**It is a departure from planaffe, and deliberately so.** planaffe's API
carries no prefix because it has no conflict to solve. The stack is adopted,
not the address layout.

**The cost was paid once.** Every path in the contract, both generated
clients, the endpoint tables in `docs/api.md`, the integration tests. Nothing
had been released and nobody was programming against the addresses, which is
the only reason this was cheap — it would not have been six months later.

**The document says the prefix out loud.** `/api` is written into every path
in `docs/api/openapi.json` rather than hidden in a `servers` entry, so the
generated clients need no special handling and the document keeps describing
the URLs an instance actually answers. `servers` stays empty for the reason it
always was: whoever captured the document was at some address, and nobody else
is.

**The development proxy became a single line.** Vite forwarded thirteen first
segments to the API; it forwards `/api` now, and a new endpoint no longer
needs a second place to be registered.

**One rule, stated in one place.** `app.MapGroup("/api")` in the composition
root, and `MapFallbackToFile` underneath it. An endpoint that wants to live
outside `/api` has to be written outside that group on purpose, which is the
review the next collision would have needed anyway.
