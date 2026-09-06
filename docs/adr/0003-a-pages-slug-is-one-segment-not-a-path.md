# A Page's Slug Is One Segment, Not a Path

A slug matches `^[a-z0-9]+(-[a-z0-9]+)*$` and contains no slash, so a page is
`backup-restore` and never `caddy/backup-restore`. What a page belongs to is
said by `attached_to`, which names a machine or an installation, and pages are
listed and narrowed by that field rather than by a prefix in their address.

## What forced it

[Vision §7](../../Vision.md#7-domain-model) said both things at once. Its prose
had pages flat and listed folders and page hierarchy under what is deliberately
left out; the example column of the same table showed `caddy/backup-restore`
and `decisions/tailscale-for-management`. `Slug.PatternText` allowed no slash,
so the examples were the half that had never been built.

Segments are not a pattern change. `/api/pages/{slug}` would become the
catch-all `/api/pages/{**slug}`, and a catch-all in ASP.NET Core routing may
only be the last segment of a template. The two sub-resources that already
exist — `/api/pages/{slug}/history` and `/api/pages/{slug}/restore` — have
nowhere left to stand, and the collision is not only the router's: a page
slugged `caddy/history` and the history of the page `caddy` are one address,
and no ordering of routes makes them two.

## The alternatives

**Segments, with history and restore moved out of the path** — under a query
parameter, or a second prefix. It resolves the collision by rewriting the
contract far beyond pages, and it costs the shape that machine, software and
installation all follow, where `/{key}/history` is where a record's history
is. One record type addressed unlike every other is a worse answer than one
example being wrong.

**Segments capped at two, with the first reserved.** Cheaper to route and
worse to explain: the first segment would mean the anchor, unvalidated, free
to say `caddy` on a page whose `attached_to` says something else. Two fields
for one fact, one of them a string nobody checks.

## Consequences

**`attached_to` carries what the prefix was for.** It arrived in the same
ticket as this decision and does the job with a checked reference: the anchor
is a kind and a key that must exist, `?machine=` and `?installation=` filter by
it, and the address stays out of it.

**A name is taken instance-wide.** Two machines cannot both have a page
`backup-restore`; the second is `ex44-backup-restore` or similar. This is the
real cost, it was accepted knowingly, and it is what folders would have bought.
The Vision left folders out first.

**Two examples in §7 were wrong and are corrected.** They now read
`backup-restore`, attached to the machine `caddy`, and
`tailscale-for-management`, a `decision` with no anchor.

**planaffe [ADR 0021](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0021-a-pages-address-is-its-slug-not-a-key.md)
stands unchanged.** The slug is still given, never derived from the title, and
still renameable. This says what a slug may look like, not where it comes from.
