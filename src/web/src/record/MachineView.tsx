import { Link, useParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Failed, Fields, Section, Waiting } from "@/shared/Detail";
import { moment } from "@/shared/when";
import { About, Attached, Description, Files, History, Installations, StatusBadge } from "./Parts";

type Machine = Schemas["Machine"];
type HistoryEntry = Schemas["HistoryEntry"];

/**
 * One machine, whole: the fields, what is installed on it, the text it runs
 * with, the pages hanging on it, and everything that ever changed about it
 * (VISION 6.2).
 *
 * The sections load beside each other rather than one after the next. The
 * fields are the answer somebody came for, and a history of four hundred
 * entries must not be what holds them up.
 */
export function MachineView() {
  const { key } = useParams();
  const at = key ?? "";

  const { asked, again } = useAsk<Machine>(at, (signal) =>
    api.GET("/api/machines/{key}", { params: { path: { key: at } }, signal }));

  const history = useAsk<HistoryEntry[]>(at, (signal) =>
    api.GET("/api/machines/{key}/history", { params: { path: { key: at } }, signal }));

  if (asked.at === "asking") return <Waiting title="Loading the machine…" />;

  if (asked.at === "failed") {
    return (
      <Failed
        title={at}
        why={asked.why}
        back={<Link className="text-brand hover:underline" to="/machines">All machines</Link>}
      />
    );
  }

  const machine = asked.value;

  return (
    <>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <span className="font-mono text-xs font-normal text-muted-foreground">{machine.key}</span>
            {machine.name}
          </span>
        }
        meta={machine.kind}
      >
        <StatusBadge status={machine.status} />
      </PageHeader>

      <div className="max-w-3xl flex-1 p-4 md:p-6">
        <Section title="The machine">
          <Fields
            of={[
              ["Hostname", machine.hostname],
              ["Kind", machine.kind],
              // Only a vm has one, and it is the machine it runs on.
              ["Host", machine.host === null ? null : (
                <Link className="text-brand hover:underline" to={`/machines/${encodeURIComponent(machine.host)}`}>
                  {machine.host}
                </Link>
              )],
              ["Provider", machine.provider],
              ["Plan", machine.plan],
              ["Location", machine.location],
              ["Operating system", machine.os],
              ["Architecture", machine.arch],
              ["CPU", machine.cpu],
              ["Memory", machine.memory],
              ["Disk", machine.disk],
              ["SSH", machine.ssh === null ? null : <code className="font-mono text-xs">{machine.ssh}</code>],
            ]}
          />
        </Section>

        <Section title="Addresses">
          <Fields
            of={[
              ["IPv4", machine.ipv4 === null ? null : <code className="font-mono text-xs">{machine.ipv4}</code>],
              ["IPv6", machine.ipv6 === null ? null : <code className="font-mono text-xs">{machine.ipv6}</code>],
              ["Private", machine.private_ip === null ? null : <code className="font-mono text-xs">{machine.private_ip}</code>],
            ]}
          />
        </Section>

        <Description
          body={machine.description}
          empty="Nothing is written about this machine beyond its fields."
          version={machine.updated_at}
          onWritten={again}
          write={(description, version) =>
            api.PATCH("/api/machines/{key}", {
              params: { path: { key: machine.key } },
              headers: { "If-Match": version },
              body: { description },
            })}
        />

        <Installations of={{ machine: machine.key }} />
        <Files owner={{ kind: "machine", key: machine.key }} />
        <Attached to={{ kind: "machine", key: machine.key }} />
        <History asked={history.asked} />

        <Section title="Measured">
          {/* The one field about the fields: when they were last confirmed
              against the machine itself rather than against somebody's memory. */}
          <p className="text-sm">
            {machine.measured_at === null
              ? "These fields have never been measured against the machine itself."
              : `Last measured ${moment(machine.measured_at)}.`}
          </p>
        </Section>

        <About of={machine} />
      </div>
    </>
  );
}
