import { useEffect, useState } from "react";
import { api, type Schemas } from "@/api/client";

type EpicSummary = Schemas["EpicSummary"];

/**
 * The epics of the project, asked once — a project has few enough of them to
 * hold, and the issue form offers them as a choice rather than as a key to
 * type.
 */
export function useEpics(project: string | undefined): EpicSummary[] {
  const [epics, setEpics] = useState<EpicSummary[]>([]);

  useEffect(() => {
    if (project === undefined) return;

    let current = true;

    void (async () => {
      try {
        const { data } = await api.GET("/epics", { params: { query: { project, limit: 100 } } });
        if (current) setEpics(data?.items ?? []);
      } catch {
        if (current) setEpics([]);
      }
    })();

    return () => {
      current = false;
    };
  }, [project]);

  return epics;
}
