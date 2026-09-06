import { CheckIcon, ChevronsUpDownIcon, PlusIcon } from "lucide-react";
import { useNavigate } from "react-router";
import type { Project } from "@/api/client";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Kbd } from "@/components/ui/kbd";
import { rememberProject, type Projects } from "./useProjects";

/**
 * The project switcher of the header (ADR 0006). Switching keeps the view:
 * whoever is in "In progress" of PLAN lands in "In progress" of the next
 * project, because the question was about the view, not about the project.
 *
 * The shell owns whether it is open, because `p` opens it from anywhere; the
 * menu closes itself the way every menu does.
 *
 * It is handed the list as it stands rather than the projects in it: a list
 * that could not be loaded is not a list of none, and saying "No project yet."
 * to somebody whose instance did not answer is the wrong sentence.
 */
export function ProjectSwitcher({
  projects,
  current,
  viewPath,
  open,
  onOpenChange,
  reload,
}: {
  projects: Projects;
  current: Project | undefined;
  viewPath: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  reload: () => Promise<void>;
}) {
  const navigate = useNavigate();
  const known = projects.at === "known" ? projects.projects : [];

  return (
    <DropdownMenu open={open} onOpenChange={onOpenChange}>
      <DropdownMenuTrigger
        render={
          <Button variant="ghost" size="sm" className="gap-1.5 px-2 font-medium" aria-label="Switch project" />
        }
      >
        <span className="font-mono text-xs font-medium tracking-wide text-brand">
          {current?.key ?? "—"}
        </span>
        <span className="hidden sm:inline">{current?.name ?? standing[projects.at]}</span>
        <ChevronsUpDownIcon className="size-3.5 text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="min-w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="flex items-center justify-between">
            Projects
            <Kbd>P</Kbd>
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        {known.map((project) => (
          <DropdownMenuItem
            key={project.key}
            onClick={() => {
              rememberProject(project.key);
              void navigate(`/${project.key}/${viewPath}`);
            }}
          >
            <span className="w-14 font-mono text-xs text-muted-foreground">{project.key}</span>
            <span className="flex-1 truncate">{project.name}</span>
            {project.key === current?.key && <CheckIcon className="size-3.5" />}
          </DropdownMenuItem>
        ))}
        {projects.at === "asking" && (
          <div role="status" className="px-2 py-1.5 text-xs text-muted-foreground">
            Loading the projects…
          </div>
        )}
        {projects.at === "failed" && (
          <div className="space-y-1 px-2 py-1.5 text-xs">
            <p className="text-destructive">The projects could not be loaded.</p>
            <button
              type="button"
              className="text-brand underline-offset-4 hover:underline"
              onClick={() => void reload()}
            >
              Try again
            </button>
          </div>
        )}
        {projects.at === "known" && known.length === 0 && (
          <div className="px-2 py-1.5 text-xs text-muted-foreground">
            No project yet.
          </div>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={() => void navigate("/projects/new")}>
          <PlusIcon className="size-3.5" />
          <span>Create project</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/** What the trigger says when there is no current project to name. */
const standing: Record<Projects["at"], string> = {
  asking: "Loading…",
  failed: "Projects unavailable",
  known: "No project",
};
