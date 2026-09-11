# A Path Is Found by Its Letters, Not by Its Words

`ha search` gains a **fragment search beside the word search**: a query that is
one word with a slash or a dot in it — `/srv/caddy`, `.env.runtime`,
`docker-compose.yml` — is looked for as a piece of text as well, over the same
surfaces the words are looked for on. Postgres `pg_trgm` provides the index; the
`tsvector` columns stay exactly as they are and are never replaced.

## What forced it

Postgres splits text into words, and a path is one word. `to_tsvector('simple',
'/srv/caddy/caddy.env')` is a single token of type `file`, so `/srv/caddy`
matched nothing, however often the path occurred — and `docs/cli.md` said so
itself, as a fact of life with no way out beside it.

On a record of machines that is the wrong way round. `/srv/caddy/caddy.env`
occurs 28 times in the first host's documentation, `/srv/caddy/docker-compose.yml`
26 times, `/etc/caddy/Caddyfile` 19 times. The questions somebody actually asks
are "who touches `/srv/caddy`", "which installation writes to `/srv/services`",
"where does `.env.runtime` turn up". The search answered none of them — not
"nothing found, because there is nothing", but "nothing found, because that is
not how I look".

## The decision

**Both, and not one instead of the other.** The word search is what answers
`logaffe`, `LOG-42`, `tailscale`, and it answers them ranked by nothing and fast.
The fragment search is what answers a path. A query that is one word with a
slash or a dot in it runs both; everything else runs the words alone, exactly as
before.

**Over every surface, not over the files alone.** The smaller step — only file
contents and an installation's directories — would have covered most of the
occurrences and left a second, differently shaped promise behind: a path in a
runbook, or in a deployment's note, is where somebody wrote down what to edit,
and a search that finds it in the file but not in the page telling you to open
the file is worse than one rule stated once.

**A concatenation gets a `letters` column; a column that is already the text is
indexed as it stands.** `machine`, `software`, `installation`,
`installation_secret` and `deployment` each gain a stored generated `letters`,
the same text their `search` is made of; `file_revision.content`, `page.title`
and `page.body` are texts already and carry the trigram index directly. One
expression per table feeds both columns, so a field added to the row reaches the
words and the letters together or reaches neither.

**A file is the exception, and it is the case the ticket named.** Its words stay
its `path` under its owner — Postgres makes one token of a path, so folding the
directory in would have cost `ha search Caddyfile` the hit it has today. Its
letters are `directory` and `path` together, the absolute place a machine's file
lies, which until now was in no column the search read at all: "who touches
`/srv/caddy`" is a question about the directory, and the directory was the one
field that could not answer it.

## The alternatives

**Prefix matching on the existing `tsvector`**, `to_tsquery('/srv/caddy:*')`. No
new index, no extension, and it answers two of the three questions — but not
`.env.runtime`, which is the end of a token and not its beginning. Half the
question answered by a mechanism nobody could predict the shape of is worse than
a rule that holds.

**`ilike` with no index at all.** It is what the query does anyway; the index is
what keeps it from reading every revision of every file. On a small record the
difference is invisible, which is exactly why it would be found too late.

**A second search engine.** Ruled out where the stack was chosen: no second
index to operate is part of the promise that an instance is a Compose file
([Vision §12](../../Vision.md#12-technical-guard-rails)). `pg_trgm` ships with
Postgres and is a *trusted* extension, so the database owner creates it and no
instance needs a superuser to migrate.

**Saying so in the documentation and stopping there.** The state of affairs the
ticket reported, and the honest fallback if this had been refused.

## Consequences

- **A query is a fragment or it is not, whole.** `caddy /srv/caddy` is two words
  and stays a word search — the same rule the port already follows, where a
  query *is* a port number or is not one.
- **Three characters at least.** A trigram is three characters; below that the
  index narrows nothing and the search would read the whole record to answer.
- **A hit still says which surface answered**, and the vocabulary does not
  change: `fields`, `description`, `note`, `title`, `body`, `path`, `content`,
  `ports`, `secrets`. A fragment found in a description is a `description` hit,
  the way a word is.
- **The letters are a second copy of the text**, for the six tables that have
  one, which is the price. It is the same price the `tsvector` already pays, and
  the large surfaces — a file's content, a page's body — pay it only once,
  because their index sits on the column itself.
- **The instance creates an extension on migration.** An operator who runs
  Postgres themselves needs `pg_trgm` available in the image; the official one
  carries it, and the migration asks for it with `create extension if not
  exists`.
