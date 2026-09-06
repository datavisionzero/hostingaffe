# A Write's Note Is a Query Parameter, Not a Field

Every write takes `?note=`, and what it carries is written into the history
rows that write produces. It is not a member of the request body, not a header,
and not a field of the record: `POST /api/machines?note=moved%20to%20fsn1`,
`PATCH /api/installations/logaffe-prod?note=…`,
`DELETE /api/software/nginx?note=…`, `POST /api/machines/ex44/restore?note=…`.
`ha` spells it `--note` on every command that writes.

## What forced it

[Vision §6.1](../../Vision.md#61-the-cli-ha) promises that every write can carry
a note and that the note lands in the history next to the change, so that "why"
is recorded where "what" is. The history row has held a `note` column since the
schema was written; what it had no route for was a note a caller sends. The
column was filled only by the instance itself — `with machine caddy` on the rows
a cascade writes.

Four writes per record type have to take it, and two of them have no body:
`DELETE /api/machines/{key}` and `POST /api/machines/{key}/restore`. Whatever
carries the note has to work for all four, or the promise is "every write except
the two that end one".

## The alternatives

**A `note` member of the request body.** It reads best on `POST` and `PATCH`,
and it is where a reader looks first. It fails twice. The request objects are
closed and describe the record — and `CreateMachineRequest` is more than a
request: it is the shape `ha export` writes and `ha machine add --file` reads
(VISION 13, 14). A note inside it would be exported as part of the record and
re-imported as a note about an import, which is not what it says. And it leaves
`DELETE` and restore needing a second mechanism anyway, which is the whole of
what this decision was trying to avoid.

**A `Hostingaffe-Note` request header.** The closest fit by kind: the act's
other two pieces of metadata, `Idempotency-Key` and `If-Match`, are headers, and
a note is metadata about the act rather than about the record. It was rejected
on the wire format. A header field is bytes, read as Latin-1 by every default
in the stack, and a note is free text somebody writes in their own language.
Making it work means either percent-encoding a header — a rule every client
would have to know and no tool would show correctly — or changing how the whole
server decodes request headers for the sake of one field.

## Consequences

**A note is one line of at most 500 characters**, trimmed, and an empty one is
the same as none. `Fields.Note` says so, and a longer one is `validation` naming
`note`. What needs more than a line is a `description` or a page; the note says
why, it does not tell the story.

**A change that touches three fields writes the note on all three rows.** The
note belongs to the act and the act wrote three rows; putting it on one of them
would mean choosing which, and there is no honest answer to that.

**A cascade keeps its own note.** Deleting a machine writes `with machine caddy`
on every row it takes with it, and the caller's note goes on the machine's own
row — the one that says the act happened. The two never compete for the column.

**Notes are in the URL, so they are in the access log.** Accepted: a note says
why a record changed, and this product's rule is already that no secret is ever
in the record (VISION 10). A note that would not belong in a log does not belong
in the history either.

**Every later write gets it the same way.** Files and pages take `?note=` when
their tickets arrive; nothing has to be re-decided per object. A deployment is
the exception, and not by omission: it carries a `note` of its own — a field of
the record, the why of that deployment — and a second one beside it would be two
things called the same word on one command.
