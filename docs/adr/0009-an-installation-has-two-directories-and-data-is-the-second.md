# An Installation Has Two Directories, and `data` Is the Second

An installation carries **`data`** beside `path`: the absolute directory its
persistent data lies in, `/srv/services/logaffe`. `path` stays what it was —
where the installation lives, the directory it is deployed from and the one
every file it owns lies under — and `data` is the answer to the two questions
an operator actually asks: what a backup has to take, and what a
`docker compose down -v` does not bring back.

Nothing holds the two apart. Where a host keeps configuration and state in one
directory, both fields carry it; two directories is what the model allows, not
what it demands.

## What forced it

The first host migrated keeps every installation in two places, and does so on
purpose: the Compose file, the `.env.example` and the runtime environment under
`/opt/compose/<service>`, the database directories and everything else that
must survive under `/srv/services/<service>`. That is the usual separation of
configuration from state, and not one host's invention.

One of the two fitted in `path`. The other went into the description, where
nothing can read it: no filter, no search, no line in `ha machine context` at
the place an agent looks before it touches anything. The fact that decides
every backup and every restore was prose — which is the record this product
replaces.

`path` was also silent about which of the two it meant. A field that holds one
of two directories and does not say which is worse than a field that holds one.

## The alternatives

**Say in `CONTEXT.md` that `path` is the deployment directory, and put the data
directory in a page.** Free, and it settles the ambiguity. It leaves the one
fact a restore needs somewhere a reader has to find, in a document nothing
lists, filters or searches by structure. A page is where reasoning goes; a
directory is a value.

**A list of directories, each with a purpose.** More general, and it is a
grammar of its own: a closed set of purposes to argue about, a rendering to
invent, and clients that see a list they can read nothing out of. Two questions
were asked, and two fields answer them.

**Volumes as entities.** A Docker volume, its driver, its mount point. That is
measuring the machine, which this product does not do (VISION 5, 15.1): what is
wanted here is the one directory a human writes down.

**Nothing, and keep the second location in the description.** The state of
affairs the ticket reported. It is the one alternative the record itself argues
against.

## Consequences

- `data` is optional and is never required to differ from `path`. An
  installation recorded before this keeps none, and the column is nullable for
  the same reason every other optional text is.
- It is **searched like `path`**: the generated `search` column takes it, so
  `ha search /srv/services/logaffe` finds the installation whose data lies
  there — a whole path, because a fragment inside one is not a word. The
  migration drops and recreates the column and its index, which Postgres does
  in one statement each and the rows are rebuilt from what they already hold.
- `files sync` is untouched. It writes under `path`, and `data` is written
  down, never written to — the same rule a machine's file directory follows
  (ADR 0008): the agent acts on the machine and the record says what is there.
- `ha inst set KEY --data DIR` fills it in; `ha inst view`, the export tree,
  `ha machine context` and the web application print it beside the path.
- The API takes it on `POST` and `PATCH` like any other text field: the empty
  string clears it, leaving it out leaves it alone. A caller that sent `data`
  before got `unknown-field`; it now writes a directory.
