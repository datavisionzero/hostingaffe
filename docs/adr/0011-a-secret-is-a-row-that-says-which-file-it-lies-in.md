# A Secret Is a Row That Says Which File It Lies In

An installation's `secrets` stops being a list of words and becomes a list of
objects: **`name`**, and **`path`** — the file on the machine the value lies
in, absolute, optional. The value itself stays as far away as it ever was
([Vision §7](../../Vision.md#7-domain-model)): what is recorded is the name
somebody has to set and the place a restore has to bring back.

A secret is a row of `installation_secret` for the reason a port is a row of
`installation_port`: it is two facts now, and an entry of a `text[]` holds one.

## What forced it

The README promises that an installation "names the secrets it needs **and
where they live**". The second half was not in the model. `secrets` was
`text[]`, an entry was one word, and anything with whitespace or an `=` in it
was refused — which is the rule that keeps a value out, and also the rule
that kept the place out.

The first host migrated documents every secret in a table of four columns, and
the one that matters most operationally is the runtime location: it says which
file a restore needs, which file must be `0600`, and which file must never
reach a repository. It went into the description, where no filter, no search
and no line of `ha machine context` could find it — the record this product
replaces.

And [Vision 15.4](../../Vision.md#154-talking-to-the-siblings) wants an
installation's secrets resolved against a vaultaffe project one day. A list of
words has nothing to resolve *at*.

## The alternatives

**A second array beside the first**, `secret_paths`, aligned by position. Two
columns that are one fact, held together by an index nobody can see, and the
first write that sends lists of different lengths is a silent lie. A pair of
facts is a row.

**JSON in one column.** It would hold anything, which is the problem: no check
constraint, no key holding a name unique, no index the search can read, and a
generated client that sees an object with no shape. The model has no JSON
column and this is not the place to open one.

**The place as free text — a path, a vault reference, a password manager.**
Tempting, because a real host keeps some secrets in a vault and not in a file.
But `path` is the glossary's word for a place on the machine, and it is checked
everywhere it appears; a field that is a path on Monday and "Proton Pass, vault
`Agents`" on Tuesday is a field nothing can act on. Where the value is kept
*besides* the machine is prose in the runbook until 15.4 gives it a field of
its own, resolved against vaultaffe rather than typed.

**A `rotation` field beside the two.** The fourth column of the migrated host's
table — "in the mailbox, then `set-smtp-password`". It is a runbook sentence:
no query reads it, no filter selects by it, and the CLI would need a grammar
that carries a phrase with spaces in it beside two values that have none. The
description is the runbook, and that is where it stays.

**Nothing, and keep the place in the description.** The state of affairs the
ticket reported.

## Consequences

- `path` is optional and is never required: an installation may know that it
  needs `SMTP_PASSWORD` before anybody has decided where the value goes, and
  the bare name is what every secret recorded before this migration carries.
- **The name is the key.** An installation needs `POSTGRES_PASSWORD` once, so
  two entries naming it are a refusal and not two secrets — the same rule the
  port's key states, one level down.
- **It is searched on its own row.** A generated column reads the row it is in,
  so the names left the installation's `search` and `installation_secret` got
  one of its own: `ha search POSTGRES_PASSWORD` answers, and so does
  `ha search /opt/compose/logaffe/.env.runtime`. An installation whose own
  fields already answered is not listed a second time.
- **The spelling is `NAME@/the/file`**, and the bare name where there is no
  file. It is what `ha inst set --secret` takes, what `ha inst view` and
  `ha machine context` print, and what a history row carries — the same
  division of labour a port's `443/tcp:public` makes between the field and the
  rendering.
- The migration carries every name an instance already holds into a row of its
  own and drops the column; no one retypes anything, and nothing is lost
  forward. Backwards it is names only: a column of words has nowhere to put the
  half this adds.
- **The contract changed shape**, which is the cost. `secrets` was an array of
  strings and is an array of objects: a caller sending `["A_PASSWORD"]` is now
  `validation`. The version this ships in says so, and the export carries the
  objects, so export and import still go in a circle.
