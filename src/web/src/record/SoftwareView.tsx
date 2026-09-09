import { Link, useParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Failed, Fields, Section, Waiting } from "@/shared/Detail";
import { About, Description, History, Installations } from "./Parts";

type Software = Schemas["Software"];
type HistoryEntry = Schemas["HistoryEntry"];

/**
 * One software, and every installation of it with its version — the screen that
 * answers "where do I have to update Caddy?" (VISION 6.2).
 *
 * It has no files and no pages of its own. A file is the text a *machine* or an
 * *installation* runs with, and a page hangs on one of those two; the software
 * is the thing being installed, not a place where anything runs.
 */
export function SoftwareView() {
  const { key } = useParams();
  const at = key ?? "";

  const { asked, again } = useAsk<Software>(at, (signal) =>
    api.GET("/api/software/{key}", { params: { path: { key: at } }, signal }));

  const history = useAsk<HistoryEntry[]>(at, (signal) =>
    api.GET("/api/software/{key}/history", { params: { path: { key: at } }, signal }));

  if (asked.at === "asking") return <Waiting title="Loading the software…" />;

  if (asked.at === "failed") {
    return (
      <Failed
        title={at}
        why={asked.why}
        back={<Link className="text-brand hover:underline" to="/software">All software</Link>}
      />
    );
  }

  const software = asked.value;
  const link = (url: string | null) =>
    url === null ? null : (
      // A homepage and a repository are foreign addresses (ADR 0007): they open
      // away from the instance and carry no referrer with them.
      <a className="text-brand break-all hover:underline" href={url} target="_blank" rel="noreferrer noopener">
        {url}
      </a>
    );

  return (
    <>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <span className="font-mono text-xs font-normal text-muted-foreground">{software.key}</span>
            {software.name}
          </span>
        }
      />

      <div className="max-w-3xl flex-1 p-4 md:p-6">
        <Section title="The software">
          <Fields
            of={[
              ["Homepage", link(software.homepage)],
              ["Repository", link(software.repository)],
              ["Image", software.image === null ? null : <code className="font-mono text-xs">{software.image}</code>],
            ]}
          />
        </Section>

        <Description
          body={software.description}
          empty="Nothing is written about this software beyond its fields."
          version={software.updated_at}
          onWritten={again}
          write={(description, version) =>
            api.PATCH("/api/software/{key}", {
              params: { path: { key: software.key } },
              headers: { "If-Match": version },
              body: { description },
            })}
        />

        <Installations of={{ software: software.key }} />
        <History asked={history.asked} />
        <About of={software} />
      </div>
    </>
  );
}
