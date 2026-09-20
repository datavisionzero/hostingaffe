# The Front Page Is a Tile per Machine, and It Is Not Monitoring

[ADR 0020](0020-a-provider-is-a-record-and-a-vm-inherits-it.md) permits
read-only navigation diagrams on separate routes. The no-graph rule here
continues to apply to the front-page tiles and to monitoring.

The instance's `/` is an overview: one tile per machine, carrying what has
lately happened on it, and a click away from that machine's history. The
sentence in [VISION 6.2](../../Vision.md#62-web-interface-for-humans) that said
there is no dashboard is withdrawn; the non-goal in
[VISION 5](../../Vision.md#5-non-goals-deliberate-boundaries) that rules out
monitoring, metrics and alerts is not, and this screen stays inside it.

## The question this closes

"No dashboard, no diagram, no chart" was written to keep this product from
drifting into the thing it names in the same breath — a wall of graphs with
thresholds on them, which is Uptime Kuma's job and Beszel's, not a host
record's. As a rule about *graphs* it is right and stays.

As a rule about the front page it was answering a question nobody had asked
yet. `/` redirected to the machine list, and that list is a good answer to
"which machines do we have" and no answer at all to the question somebody
actually opens the record with after a week away: which of these hosts moved,
which one has said nothing, which one is waiting for a restart. The record held
every one of those answers already — in the history, in the deployments, in the
last report — and made a person open five screens to assemble them.

## The decision

**One tile per machine, and every number on it comes out of the record.** The
newest deployment with the version it went to and how long ago that was; how
many acts fall inside the window; when the machine last reported; whether it is
waiting for a restart; how many drift findings its latest report makes; how
many installations are active. The tile leads to `/history?machine=…`, which is
the whole story the numbers are a summary of, and the key leads to the machine.

**What VISION 5 excludes is excluded here.** No time series, because nothing is
kept as one: a report is a sample somebody may read (ADR 0015), and the sweep
takes it after thirty days. No graph, no sparkline, no gauge. **No threshold and
no colour that claims one** — "reported 4 minutes ago" and "reported 3 days ago"
are the same kind of sentence in the same grey, and which of them is a problem
is the reader's judgement about their own host, not a line this product drew.
No alert, no notification, nothing that watches.

That is the whole difference between this and monitoring, and it is why the
narrower sentence could go while the broader one stands: monitoring watches and
decides; this reads back what the record already says, in the order things
happened.

## What it cost, and why it is affordable

The numbers are an **opt-in** of the machine list, `activity=7d`
(`docs/api.md`, Machines). The counts and the newest deployment are one
statement each for the whole list; the drift is the one that costs, because it
reads the latest report of every machine against that machine's installations.
Fifty machines with an installation, a deployment and a report each answer in
about a quarter of a second, which is what a screen of tiles can pay — and why
the plain list, which somebody opens to find a key, is not made to pay it.

## What was considered instead

**Leaving `/` on the machine list and putting the tiles somewhere else.** That
keeps the withdrawn sentence intact and makes the screen nobody navigates to.
The front page is where a question like this one is asked or it is not asked at
all.

**A column in the machine list rather than a tile.** Six numbers do not fit a
row anyone can read down, and the list's own columns — key, name, kind,
provider, location, measured, last seen, restart, status — are already the
width of a window. The tile exists because the shape of the answer is a
paragraph per machine, not a cell.
