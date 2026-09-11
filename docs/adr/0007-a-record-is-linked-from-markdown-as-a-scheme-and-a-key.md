# A Record Is Linked from Markdown as a Scheme and a Key

A Markdown body links another thing of the record as an ordinary link with a
scheme of the instance's own — `[Restoring a backup](page:backup-restore)`,
`[ex44](machine:ex44)`, `[caddy](software:caddy)`,
`[app-1](installation:app-1)`. The web application resolves those four schemes
to its own addresses and navigates without leaving the page; every other scheme
stays what it was, a foreign link or nothing at all (planaffe ADR 0007).

## What forced it

A record has no cross-references at all. Moving a host out of a Markdown
repository carries bodies full of `[ADR](../decisions/2026-09-10-caddy.md)`,
and a relative path is a path into a tree the instance does not have: the
renderer drops the `href` and the text stays a sentence pointing nowhere. A
wiki whose pages cannot name each other is a pile of texts that have stopped
explaining one another, and every page written from here on has the same
problem.

## The alternatives

**`[[slug]]`, the wiki link.** Shorter, unmistakably internal, and what
Obsidian and MediaWiki taught everybody. It is not CommonMark, so it needs a
remark plugin of its own in the one pipeline planaffe ADR 0007 fixed — and
outside that pipeline it is not a link at all. `ha page view` prints the body
as it is stored, `ha export` writes a Markdown tree meant to be kept in a
repository and diffed, and in both of those `[[backup-restore]]` is literal
text. A second form would then be needed for machines, software and
installations, because a bare `[[caddy]]` cannot say whether it means the
machine or the software.

**Relative `.md` paths, resolved by the renderer.** It would need no new
spelling, which is its whole appeal. But a slug is one segment and the wiki is
flat (ADR 0003), so `../decisions/x.md` describes a tree that does not exist,
and resolving it means guessing which part of the path was meant to be the
address. A link that works only when guessed right is worse than one that says
what it means.

## Consequences

**The scheme carries the type, which is what `CONTEXT.md` already asked for.**
A key is unique per entity type, not across the instance, so the machine
`caddy` and the software `caddy` coexist — and the glossary says that where a
key stands alone, in a Markdown link among other places, it carries its type.
`machine:caddy` is that sentence written down.

**Nothing validates a body.** The instance stores Markdown and does not parse
it, so a link to a page that does not exist is written and stored like any
other text. What resolves it is the reader: the web application links it, and
`ha page check` says which references point at nothing. A renaming page breaks
its inbound links, as ADR 0021 always said it would — the check is how that
shows itself.

**The import writes these links**, because it is the only place that can. It
sees the source path of every page in the same document and maps a relative
`.md` link onto the slug of the page that arrives under that path; a path no
page in the document claims is left alone rather than guessed at.

**`file:` is not one of them.** A file is addressed by an anchor and a path, so
a scheme for it would have to carry two things, and `file:` is a registered URI
scheme with a meaning of its own. Files are linked when there is a spelling
that does not pretend to be the browser's.
