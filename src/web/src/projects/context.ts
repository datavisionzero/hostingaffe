import { createContext } from "react";
import type { Projects } from "./useProjects";

/**
 * The project list the shell holds, and the way a screen that changed it asks
 * for it again. The shell is never remounted by navigation (ADR 0006), so a
 * screen that creates a project has no other way to make the frame agree with
 * the URL it is about to navigate to.
 */
export type ProjectList = { projects: Projects; reload: () => Promise<void> };

export const ProjectsContext = createContext<ProjectList | null>(null);
