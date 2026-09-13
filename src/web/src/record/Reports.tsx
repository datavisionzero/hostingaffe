import { useState } from "react";
import { api, describe, type Problem, type Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ActionDialog } from "@/shared/ActionDialog";
import { useAsk } from "@/shared/ask";
import { Nothing, Section } from "@/shared/Detail";
import { ago, moment } from "@/shared/when";
import { Asks, Line, Rows } from "./Parts";

type Report = Schemas["Report"];
type ReportPage = Schemas["ReportPage"];
type MachineToken = Schemas["MachineToken"];

/** How many of the series a page of it shows. */
const perPage = 20;

/**
 * Bytes as a person reads them. The instance carries numbers because it stores
 * numbers; this is the rendering, and it is here rather than beside a file's
 * `size` because the two answer different questions — a file's size is compared
 * against a cap and is exact, a disk's is read at a glance.
 */
function bytes(n: number | null | undefined): string {
  if (n === null || n === undefined) return "";
  const units = ["B", "kB", "MB", "GB", "TB"];
  let value = n;
  let unit = 0;
  while (value >= 1000 && unit < units.length - 1) {
    value /= 1000;
    unit += 1;
  }
  return `${unit >= 3 ? value.toFixed(1) : Math.round(value)}${units[unit]}`;
}

function uptime(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return "";
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  return days > 0 ? `${days}d ${hours}h` : `${hours}h ${Math.floor((seconds % 3600) / 60)}m`;
}

/**
 * What the machine last said about itself.
 *
 * It is a section like the others in `Parts.tsx`: it asks on its own, says so
 * on its own when it gets nothing, and never holds the fields up. The record is
 * written and the report is reported, and this is the second of the two
 * standing beside the first — nothing here writes anything back (ADR 0015).
 */
export function LastReport({ machine }: { machine: string }) {
  const { asked } = useAsk<Report>(`report:${machine}`, (signal) =>
    api.GET("/api/machines/{key}/reports/latest", { params: { path: { key: machine } }, signal }));

  // A machine that has never reported is the ordinary case, not a failure: it
  // gets a sentence that says what would make it report, rather than an empty
  // box or a red line.
  if (asked.at === "failed") {
    return (
      <Section title="Last report">
        <Nothing>
          This machine does not report. A host can hand in what it knows about itself every quarter of
          an hour — disk, memory, load, and what Docker runs — under a token that can do nothing else.
          Setting that up is three steps in <code className="font-mono text-xs">docs/operations.md</code>,
          and it starts with the token below.
        </Nothing>
      </Section>
    );
  }

  return (
    <Asks
      title="Last report"
      meta={asked.at === "known" ? ago(asked.value.received_at) : undefined}
      asked={asked}
    >
      {(report) => <ReportBody report={report} />}
    </Asks>
  );
}

/** One report, set out: the sections under each other, in what a person reads. */
export function ReportBody({ report }: { report: Report }) {
  const containers = report.containers ?? [];
  const running = containers.filter((one) => one.state === "running").length;

  return (
    <div className="grid gap-4 text-sm">
      <p className="text-xs text-muted-foreground">
        Report {report.number}, received <time title={moment(report.received_at)}>{ago(report.received_at)}</time>
        {report.agent !== null && <> · collected by ha {report.agent}</>}
      </p>

      {report.host !== null && (
        <dl className="grid gap-x-4 gap-y-1 sm:grid-cols-[10rem_1fr]">
          {report.host.hostname !== null && <><dt className="text-muted-foreground">Hostname</dt><dd>{report.host.hostname}</dd></>}
          {report.host.os !== null && <><dt className="text-muted-foreground">Operating system</dt><dd>{report.host.os}</dd></>}
          {report.host.kernel !== null && <><dt className="text-muted-foreground">Kernel</dt><dd className="font-mono text-xs">{report.host.kernel}</dd></>}
          {report.host.arch !== null && <><dt className="text-muted-foreground">Architecture</dt><dd>{report.host.arch}</dd></>}
          {report.host.uptime_seconds !== null && <><dt className="text-muted-foreground">Up</dt><dd>{uptime(report.host.uptime_seconds)}</dd></>}
          {report.host.load1 !== null && (
            <>
              <dt className="text-muted-foreground">Load</dt>
              <dd className="tabular-nums">
                {report.host.load1?.toFixed(2)} {report.host.load5?.toFixed(2)} {report.host.load15?.toFixed(2)}
              </dd>
            </>
          )}
          {report.memory !== null && report.memory.total_bytes !== null && (
            <>
              <dt className="text-muted-foreground">Memory</dt>
              <dd>
                <span>{`${bytes(report.memory.used_bytes)} of ${bytes(report.memory.total_bytes)} used`}</span>
                {(report.memory.swap_total_bytes ?? 0) > 0 && (
                  <span>{` · swap ${bytes(report.memory.swap_used_bytes)} of ${bytes(report.memory.swap_total_bytes)}`}</span>
                )}
              </dd>
            </>
          )}
        </dl>
      )}

      {report.disks !== null && report.disks.length > 0 && (
        <ul className="grid gap-1.5">
          {report.disks.map((disk) => (
            <li key={disk.mount} className="grid gap-1">
              <div className="flex items-baseline gap-2">
                <span className="min-w-0 flex-1 truncate font-mono text-xs">{disk.mount}</span>
                <span className="text-xs text-muted-foreground">
                  {`${bytes(disk.used_bytes)} of ${bytes(disk.size_bytes)}`}
                </span>
                <span className="w-10 text-right text-xs tabular-nums">{`${disk.percent ?? "–"}%`}</span>
              </div>
              {/* A bar, and no colour that decides for the reader what a number
                  means: 91 per cent is a number a person reads (VISION 5). */}
              <div className="h-1.5 overflow-hidden rounded-full bg-muted">
                <div className="h-full bg-foreground/40" style={{ width: `${disk.percent ?? 0}%` }} />
              </div>
            </li>
          ))}
        </ul>
      )}

      {report.containers !== null && (
        <div className="grid gap-2">
          <p className="text-xs text-muted-foreground">{`${running} of ${containers.length} containers running`}</p>
          {containers.length > 0 && (
            <Rows>
              {containers.map((container) => (
                <Line key={container.name}>
                  <span className="w-40 shrink-0 truncate font-mono text-xs">{container.name}</span>
                  <span className="min-w-0 flex-1 truncate font-mono text-xs text-muted-foreground">{container.image}</span>
                  <Badge variant={container.state === "running" ? "secondary" : "outline"}>
                    {container.state}{container.health !== null && container.health !== "" ? ` · ${container.health}` : ""}
                  </Badge>
                  {container.started_at !== null && (
                    <span className="hidden w-28 shrink-0 text-right text-xs text-muted-foreground sm:block">
                      {ago(container.started_at)}
                    </span>
                  )}
                  <span className="w-24 shrink-0 text-right text-xs text-muted-foreground">
                    {`${container.restarts ?? 0} restarts`}
                  </span>
                </Line>
              ))}
            </Rows>
          )}
        </div>
      )}

      {report.missing.length > 0 && (
        <ul className="grid gap-1 text-xs text-muted-foreground">
          {report.missing.map((missing) => (
            <li key={missing.section}>
              <span className="font-mono">{missing.section}</span>: not determined ({missing.reason})
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * The series, as a plain table. **No chart, not even a small one**: VISION 5
 * leaves graphs out, and the moment a curve is on the screen the next question
 * is about the second one, and the one after that is about the threshold.
 */
export function ReportHistory({ machine }: { machine: string }) {
  const [offset, setOffset] = useState(0);
  const [open, setOpen] = useState<number>();

  const { asked } = useAsk<ReportPage>(`reports:${machine}:${offset}`, (signal) =>
    api.GET("/api/machines/{key}/reports", {
      params: { path: { key: machine }, query: { limit: perPage, offset } },
      signal,
    }));

  return (
    <>
      <Asks
        title="Reports"
        meta={asked.at === "known" ? `${asked.value.total}` : undefined}
        asked={asked}
      >
        {(page) => page.total === 0 ? (
          <Nothing>Nothing has been handed in yet.</Nothing>
        ) : (
          <div className="grid gap-3">
            <Rows>
              {page.reports.map((report) => (
                <li key={report.number}>
                  <button
                    type="button"
                    onClick={() => setOpen(report.number)}
                    className="flex min-h-10 w-full items-center gap-3 px-3 py-1.5 text-left text-sm hover:bg-accent"
                  >
                    <span className="w-16 shrink-0 font-mono text-xs text-muted-foreground">{report.number}</span>
                    <span className="min-w-0 flex-1 truncate">
                      <time title={moment(report.received_at)}>{ago(report.received_at)}</time>
                    </span>
                    <span className="hidden w-24 shrink-0 text-right text-xs tabular-nums text-muted-foreground sm:block">
                      {`${report.containers_running ?? "–"}/${report.containers_total ?? "–"}`}
                    </span>
                    <span className="w-16 shrink-0 text-right text-xs tabular-nums text-muted-foreground">
                      {report.disk_percent === null ? "–" : `${report.disk_percent}%`}
                    </span>
                    <span className="hidden w-16 shrink-0 text-right text-xs tabular-nums text-muted-foreground sm:block">
                      {report.load1 === null ? "–" : report.load1.toFixed(2)}
                    </span>
                  </button>
                </li>
              ))}
            </Rows>

            {page.total > perPage && (
              <div className="flex items-center justify-between text-xs text-muted-foreground">
                <Button variant="outline" size="sm" disabled={offset === 0} onClick={() => setOffset(Math.max(offset - perPage, 0))}>
                  Newer
                </Button>
                <span>{offset + 1}–{Math.min(offset + perPage, page.total)} of {page.total}</span>
                <Button variant="outline" size="sm" disabled={offset + perPage >= page.total} onClick={() => setOffset(offset + perPage)}>
                  Older
                </Button>
              </div>
            )}
          </div>
        )}
      </Asks>

      {open !== undefined && <OneReport machine={machine} number={open} onClose={() => setOpen(undefined)} />}
    </>
  );
}

/** One report of the series, opened from its row. */
function OneReport({ machine, number, onClose }: { machine: string; number: number; onClose: () => void }) {
  const { asked } = useAsk<Report>(`report:${machine}:${number}`, (signal) =>
    api.GET("/api/machines/{key}/reports/{number}", {
      params: { path: { key: machine, number } },
      signal,
    }));

  return (
    <Dialog open onOpenChange={(next) => { if (!next) onClose(); }}>
      <DialogContent className="max-h-[85vh] overflow-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Report {number}</DialogTitle>
          <DialogDescription>What {machine} said about itself at that moment.</DialogDescription>
        </DialogHeader>
        {asked.at === "asking" && <p aria-busy className="text-sm text-muted-foreground">Loading…</p>}
        {asked.at === "failed" && <p className="text-sm text-destructive">{asked.why}</p>}
        {asked.at === "known" && <ReportBody report={asked.value} />}
        <DialogFooter>
          <DialogClose render={<Button variant="outline">Close</Button>} />
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The key the machine reports under, and the three things a person does with
 * it (ADR 0016). The secret appears once, in a dialog that says so, with the
 * cron line beside it to copy.
 */
export function ReportingToken({ machine }: { machine: string }) {
  const { asked, again } = useAsk<MachineToken>(`token:${machine}`, (signal) =>
    api.GET("/api/machines/{key}/token", { params: { path: { key: machine } }, signal }));

  const [issued, setIssued] = useState<{ secret: string } | undefined>();
  const [why, setWhy] = useState<string>();

  async function issue(rotate: boolean) {
    setWhy(undefined);
    const answer = await api.POST("/api/machines/{key}/token", {
      params: { path: { key: machine }, query: rotate ? { rotate: true } : {} },
    });

    if (!answer.response.ok || answer.data === undefined) {
      setWhy(describe(answer.error as Problem | undefined, answer.response.status));
      throw new Error(why);
    }

    setIssued({ secret: answer.data.secret });
    again();
  }

  async function revoke() {
    const answer = await api.DELETE("/api/machines/{key}/token", { params: { path: { key: machine } } });
    if (!answer.response.ok) {
      throw new Error(describe(answer.error as Problem | undefined, answer.response.status));
    }
    again();
  }

  const token = asked.at === "known" ? asked.value : undefined;

  return (
    <Section
      title="Reporting token"
      action={
        <span className="flex gap-2">
          {token?.present === true ? (
            <>
              <ActionDialog
                trigger={<Button variant="outline" size="sm">Rotate</Button>}
                title="Rotate this machine's token"
                description="A new secret is issued and the current one is revoked at once. The host stops reporting until the new secret is in place on it."
                confirmLabel="Rotate"
                confirmVariant="default"
                onConfirm={() => issue(true)}
              />
              <ActionDialog
                trigger={<Button variant="outline" size="sm">Revoke</Button>}
                title="Revoke this machine's token"
                description="The machine stops reporting at its next run. The reports it has already handed in stay."
                confirmLabel="Revoke"
                onConfirm={revoke}
              />
            </>
          ) : (
            <ActionDialog
              trigger={<Button variant="outline" size="sm">Issue</Button>}
              title="Issue a token for this machine"
              description="It belongs to this machine and can do one thing: hand in a report for it. It reads nothing at all — not an installation, not a file, not even this machine."
              confirmLabel="Issue"
              confirmVariant="default"
              onConfirm={() => issue(false)}
            />
          )}
        </span>
      }
    >
      {asked.at === "asking" && <p aria-busy className="text-sm text-muted-foreground">Loading…</p>}
      {asked.at === "failed" && <p className="text-sm text-destructive">{asked.why}</p>}
      {why !== undefined && <p role="alert" className="text-sm text-destructive">{why}</p>}

      {token !== undefined && token.present && (
        <dl className="grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[10rem_1fr]">
          <dt className="text-muted-foreground">Token</dt>
          <dd className="font-mono text-xs">{token.prefix}…</dd>
          <dt className="text-muted-foreground">Issued</dt>
          <dd>{moment(token.issued_at)}{token.issued_by !== null && <> by {token.issued_by.name}</>}</dd>
          <dt className="text-muted-foreground">Last used</dt>
          <dd>{token.last_used_at === null ? "never" : <time title={moment(token.last_used_at)}>{ago(token.last_used_at)}</time>}</dd>
        </dl>
      )}

      {token !== undefined && !token.present && (
        <Nothing>
          {token.revoked_at === null
            ? "This machine has no token, so it cannot report. Issuing one is the second of the three steps in docs/operations.md."
            : `The token ${token.prefix}… was revoked ${moment(token.revoked_at)}${token.revoked_by === null ? "" : ` by ${token.revoked_by.name}`}. This machine cannot report until a new one is issued.`}
        </Nothing>
      )}

      {issued !== undefined && (
        <TheSecret machine={machine} secret={issued.secret} onClose={() => setIssued(undefined)} />
      )}
    </Section>
  );
}

/**
 * The one moment the secret exists outside the host. Only the hash is kept, so
 * this dialog says plainly that it does not come back, and puts the line the
 * host needs beside it rather than leaving somebody to assemble it.
 */
function TheSecret({ machine, secret, onClose }: { machine: string; secret: string; onClose: () => void }) {
  return (
    <Dialog open onOpenChange={(next) => { if (!next) onClose(); }}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>The token for {machine}</DialogTitle>
          <DialogDescription>
            It is shown once and does not come back: only its hash is kept. Losing it means issuing a new one.
          </DialogDescription>
        </DialogHeader>

        <pre className="overflow-x-auto rounded-md bg-muted p-3 font-mono text-xs select-all">{secret}</pre>

        <p className="text-sm text-muted-foreground">
          On the host, in a file of its own with mode 0600, owned by whoever runs the timer:
        </p>
        <pre className="overflow-x-auto rounded-md bg-muted p-3 font-mono text-xs select-all">
{`HOSTINGAFFE_URL=${window.location.origin}
HOSTINGAFFE_TOKEN=${secret}
HOSTINGAFFE_MACHINE=${machine}`}
        </pre>
        <p className="text-sm text-muted-foreground">And the line that hands in a report every quarter of an hour:</p>
        <pre className="overflow-x-auto rounded-md bg-muted p-3 font-mono text-xs select-all">
          {"*/15 * * * * . /etc/hostingaffe.env && /usr/local/bin/ha report send --quiet"}
        </pre>

        <DialogFooter>
          <DialogClose render={<Button>Done</Button>} />
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
