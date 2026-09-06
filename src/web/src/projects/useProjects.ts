import { useCallback, useContext, useEffect, useRef, useState } from "react";
import { api, type Project } from "@/api/client";
import { ProjectsContext, type ProjectList } from "./context";

/**
 * The projects the signed-in identity can see, asked once per load and
 * handed to the switcher and the landing route. Multiple projects are the
 * normal case (VISION 6.2), so this is part of the frame, not of a screen.
 */
export type Projects =
  | { at: "asking" }
  | { at: "failed" }
  | { at: "known"; projects: Project[] };

export function useProjects(): ProjectList {
  const [projects, setProjects] = useState<Projects>({ at: "asking" });
  const live = useRef(true);

  const reload = useCallback(async () => {
    try {
      const { data } = await api.GET("/projects");

      if (live.current) {
        setProjects(data === undefined ? { at: "failed" } : { at: "known", projects: data });
      }
    } catch {
      if (live.current) {
        setProjects({ at: "failed" });
      }
    }
  }, []);

  useEffect(() => {
    live.current = true;
    void (async () => {
      await reload();
    })();

    return () => {
      live.current = false;
    };
  }, [reload]);

  return { projects, reload };
}

/**
 * The same list, for a screen under the shell. A screen that adds a project
 * calls `reload` before it navigates there, or the frame keeps a list the new
 * project is not in and every link in it goes dead.
 */
export function useProjectList(): ProjectList {
  const list = useContext(ProjectsContext);

  if (list === null) {
    throw new Error("useProjectList is only for screens under the shell.");
  }

  return list;
}

/**
 * The project the user was in last, so that `/` lands there. A convenience of
 * this browser, nothing the instance knows.
 */
const lastProjectKey = "hostingaffe.project";

export function rememberProject(key: string): void {
  try {
    window.localStorage.setItem(lastProjectKey, key);
  } catch {
    // A browser that keeps nothing lands on the first project instead.
  }
}

export function lastProject(): string | null {
  try {
    return window.localStorage.getItem(lastProjectKey);
  } catch {
    return null;
  }
}
