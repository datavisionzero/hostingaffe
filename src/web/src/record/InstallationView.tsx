import { Link, useParams } from "react-router";
import { api, type Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Failed, Fields, Nothing, Section, Waiting } from "@/shared/Detail";
import { moment } from "@/shared/when";
import { machinePath, softwarePath } from "./addresses";
import { About, Asks, Attached, Description, Files, History, Line, Rows, StatusBadge } from "./Parts";

type Installation = Schemas["Installation"];
type DeploymentSummary = Schemas["DeploymentSummary"];
type HistoryEntry = Schemas["HistoryEntry"];

/**
 * One installation: what it is, where it runs, which version is on it and where
 * that version came from (VISION 6.2).
 *
 * The version is not a field somebody keeps up to date — it is the newest
 * deployment's, which is why the deployments stand under it rather than on a
 * screen of their own.
 */
export function InstallationView() {
  const { key } = useParams();
  const at = key ?? "";

  const { asked, again } = useAsk<Installation>(at, (signal) =>
    api.GET("/api/installations/{key}", { params: { path: { key: at } }, signal }));

  const deployments = useAsk<DeploymentSummary[]>(at, (signal) =>
    api.GET("/api/installations/{key}/deployments", { params: { path: { key: at } }, signal }));

  const history = useAsk<HistoryEntry[]>(at, (signal) =>
    api.GET("/api/installations/{key}/history", { params: { path: { key: at } }, signal }));

  if (asked.at === "asking") return <Waiting title="Loading the installation…" />;

  if (asked.at === "failed") {
    return (
      <Failed
        title={at}
        why={asked.why}
        back={<Link className="text-brand hover:underline" to="/installations">All installations</Link>}
      />
    );
  }

  const installation = asked.value;

  return (
    <>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <span className="font-mono text-xs font-normal text-muted-foreground">{installation.key}</span>
            {installation.name}
          </span>
        }
        meta={installation.environment}
      >
        <StatusBadge status={installation.status} />
      </PageHeader>

      <div className="max-w-3xl flex-1 p-4 md:p-6">
        <Section title="The installation">
          <Fields
            of={[
              ["Machine", (
                <Link className="text-brand hover:underline" to={machinePath(installation.machine)}>
                  {installation.machine}
                </Link>
              )],
              ["Software", (
                <Link className="text-brand hover:underline" to={softwarePath(installation.software)}>
                  {installation.software}
                </Link>
              )],
              ["Environment", installation.environment],
              ["Role", installation.role],
              ["Version", installation.version === null ? null : (
                <code className="font-mono text-xs">{installation.version}</code>
              )],
              ["Path", installation.path === null ? null : <code className="font-mono text-xs">{installation.path}</code>],
              ["Backup", installation.backup],
              ["Monitoring", installation.monitoring],
              ["Logging", installation.logging],
              ["URLs", installation.urls.length === 0 ? null : (
                <ul className="grid gap-0.5">
                  {installation.urls.map((url) => (
                    <li key={url}>
                      <a className="text-brand break-all hover:underline" href={url} target="_blank" rel="noreferrer noopener">
                        {url}
                      </a>
                    </li>
                  ))}
                </ul>
              )],
              ["Ports", installation.ports.length === 0 ? null : (
                <span className="flex flex-wrap gap-1">
                  {installation.ports.map((port) => (
                    <Badge key={`${port.port}/${port.protocol}`} variant="outline">
                      {port.port}/{port.protocol} {port.scope}
                    </Badge>
                  ))}
                </span>
              )],
              // The names of the secrets, never the secrets: this instance holds
              // where a secret lives, and vaultaffe holds the secret (VISION 11).
              ["Secrets", installation.secrets.length === 0 ? null : (
                <span className="flex flex-wrap gap-1">
                  {installation.secrets.map((secret) => (
                    <Badge key={secret} variant="outline"><code className="font-mono">{secret}</code></Badge>
                  ))}
                </span>
              )],
            ]}
          />
        </Section>

        <Description
          body={installation.description}
          empty="Nothing is written about this installation beyond its fields."
          version={installation.updated_at}
          onWritten={again}
          write={(description, version) =>
            api.PATCH("/api/installations/{key}", {
              params: { path: { key: installation.key } },
              headers: { "If-Match": version },
              body: { description },
            })}
        />

        <Deployments asked={deployments.asked} />
        <Files owner={{ kind: "installation", key: installation.key }} />
        <Attached to={{ kind: "installation", key: installation.key }} />
        <History asked={history.asked} />
        <About of={installation} />
      </div>
    </>
  );
}

/**
 * What ran here, newest first. The instance hands them back in its own order
 * and the newest is the one that decides the installation's version, so the
 * list is turned around here rather than being read from the bottom up.
 */
function Deployments({ asked }: { asked: ReturnType<typeof useAsk<DeploymentSummary[]>>["asked"] }) {
  return (
    <Asks title="Deployments" meta={asked.at === "known" ? `${asked.value.length}` : undefined} asked={asked}>
      {(items) => items.length === 0 ? (
        <Nothing>Nothing has been deployed here yet — or nothing was written down when it was.</Nothing>
      ) : (
        <Rows>
          {[...items]
            .sort((one, other) => other.number - one.number)
            .map((deployment) => (
              <Line key={deployment.number}>
                <span className="w-12 shrink-0 text-xs text-muted-foreground">#{deployment.number}</span>
                <span className="min-w-0 flex-1 truncate font-mono text-xs">{deployment.version}</span>
                {deployment.previous !== null && (
                  <span className="shrink-0 truncate font-mono text-xs text-muted-foreground">
                    from {deployment.previous}
                  </span>
                )}
                {deployment.ticket !== null && (
                  <span className="shrink-0 truncate text-xs text-muted-foreground">{deployment.ticket}</span>
                )}
                <span className="ml-auto shrink-0 text-right text-xs text-muted-foreground">
                  {moment(deployment.at)} · {deployment.by.name}
                </span>
              </Line>
            ))}
        </Rows>
      )}
    </Asks>
  );
}
