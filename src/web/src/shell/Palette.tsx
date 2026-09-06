import { ArrowRightIcon, SearchIcon } from "lucide-react";
import { useEffect, useId, useMemo, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router";
import { api, type IssueSummary, type Project, type Schemas } from "@/api/client";
import { useTheme } from "@/components/theme-provider";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { rememberProject } from "@/projects/useProjects";
import { useSession } from "@/session/useSession";
import { cn } from "@/lib/utils";
import { Keys } from "./ShortcutsDialog";
import { is } from "./shortcuts";
import { keyPath, keyPattern, pagePath, viewPath, views } from "./views";

type PageSummary = Schemas["PageSummary"];

type Command = {
  id: string;
  label: string;
  hint?: string;
  group: string;
  run: () => void;
  /** A row the instance found, or the way to all of them: never filtered again here. */
  found?: boolean;
};

/** Enough of a word to ask the instance about, and few enough answers to stay a palette. */
const shortest = 2;
const matches = 5;
const settle = 150;

/**
 * The command palette — ⌘K, or Ctrl+K — over the views, the projects and the
 * few acts the shell itself has. A key typed into it opens that issue or epic,
 * which is the fastest way from a chat to a ticket. Words rather than a key ask
 * the instance: a few full-text matches, and the row that opens all of them as
 * a filtered list.
 *
 * It asks about issues and about pages, under headings that say which is
 * which. A hit that does not say what kind of thing it is is a poor hit, and
 * for the wiki this is more than a nicety: the pages are flat because the
 * search is what a hierarchy would have been, so this is how one is found at
 * all.
 *
 * Owned rather than imported (ADR 0017): a filtered list with a roving index
 * inside a Base UI dialog, which is what a palette is before it does more.
 */
type PaletteProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  projects: Project[];
  current: Project | undefined;
  /** The overview of the keys, which the palette is one of the ways to. */
  onShortcuts: () => void;
};

export function Palette({ open, onOpenChange, projects, current, onShortcuts }: PaletteProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="top-[20%] translate-y-0 gap-0 overflow-hidden p-0 sm:max-w-lg" showCloseButton={false}>
        <DialogHeader className="sr-only">
          <DialogTitle>Command palette</DialogTitle>
          <DialogDescription>Search views, projects and commands, or type an issue key.</DialogDescription>
        </DialogHeader>
        {open && (
          <PaletteBody
            onOpenChange={onOpenChange}
            projects={projects}
            current={current}
            onShortcuts={onShortcuts}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

/** Mounted while the palette is open, so that its query starts empty every time. */
function PaletteBody({ onOpenChange, projects, current, onShortcuts }: Omit<PaletteProps, "open">) {
  const navigate = useNavigate();
  const { setTheme } = useTheme();
  const { signOut } = useSession();
  const [query, setQuery] = useState("");
  const [index, setIndex] = useState(0);
  const [found, setFound] = useState<{ of: string; issues: IssueSummary[]; pages: PageSummary[] }>({ of: "", issues: [], pages: [] });
  const searchId = useId();

  const needle = query.trim();
  const projectKey = current?.key;
  // Words, not a key, and a project to search in. `q` on the issue list is the
  // same full-text search the list itself uses.
  const searching = projectKey !== undefined && needle.length >= shortest && !keyPattern.test(needle);

  useEffect(() => {
    if (!searching) {
      return;
    }

    const controller = new AbortController();
    // Typed words settle before the instance is asked; the palette answers
    // from its own commands the whole time, and a request that fails or is
    // overtaken costs them nothing.
    const timer = setTimeout(() => {
      void (async () => {
        try {
          // Two lists, one question. Neither waits for the other to fail: a
          // wiki that answers while the issue list is slow still shows up.
          const [issues, pages] = await Promise.all([
            api.GET("/issues", {
              params: { query: { project: projectKey, q: needle, limit: matches } },
              signal: controller.signal,
            }),
            api.GET("/projects/{key}/pages", {
              params: { path: { key: projectKey }, query: { q: needle } },
              signal: controller.signal,
            }),
          ]);

          setFound({
            of: needle,
            issues: issues.data?.items ?? [],
            pages: (pages.data ?? []).slice(0, matches),
          });
        } catch {
          // Nothing found is what the palette shows; the commands remain.
        }
      })();
    }, settle);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [needle, projectKey, searching]);

  const commands = useMemo<Command[]>(() => {
    const go = (to: string) => () => {
      onOpenChange(false);
      void navigate(to);
    };

    const list: Command[] = [];
    const key = needle.match(keyPattern);

    if (key !== null) {
      const project = key[1]!.toUpperCase();
      const number = key[2]!.toUpperCase();
      const isEpic = number.startsWith("E");

      list.push({
        id: `open:${project}-${number}`,
        label: `Open ${project}-${number}`,
        hint: isEpic ? "epic" : "issue",
        group: "Go to",
        run: go(keyPath(`${project}-${number}`)),
      });
    }

    if (searching && projectKey !== undefined) {
      const hits = found.of === needle ? found : { issues: [], pages: [] };

      for (const issue of hits.issues) {
        list.push({
          id: `found:${issue.key}`,
          label: issue.title,
          hint: issue.key,
          group: "Issues",
          run: go(keyPath(issue.key)),
          found: true,
        });
      }

      list.push({
        id: "found:all",
        label: `All issues matching “${needle}”`,
        group: "Issues",
        run: go(`/${projectKey}/issues?q=${encodeURIComponent(needle)}`),
        found: true,
      });

      for (const page of hits.pages) {
        list.push({
          id: `found:page:${page.slug}`,
          label: page.title,
          hint: page.slug,
          group: "Pages",
          run: go(pagePath(projectKey, page.slug)),
          found: true,
        });
      }
    }

    if (current !== undefined) {
      for (const view of views) {
        list.push({
          id: `view:${view.id}`,
          label: view.label,
          hint: view.hint,
          group: current.key,
          run: go(viewPath(current.key, view)),
        });
      }
    }

    // The palette is the other way to everything the screens offer, so the
    // four things that can be created are reachable from it too.
    if (current !== undefined) {
      list.push(
        { id: "create:issue", label: "Create issue", hint: current.key, group: "Create", run: go(`/${current.key}/issues/new`) },
        { id: "create:epic", label: "Create epic", hint: current.key, group: "Create", run: go(`/${current.key}/epics/new`) },
        { id: "create:page", label: "Create page", hint: current.key, group: "Create", run: go(`/${current.key}/pages/new`) },
      );
    }

    list.push({ id: "create:project", label: "Create project", group: "Create", run: go("/projects/new") });

    for (const project of projects) {
      if (project.key !== current?.key) {
        list.push({
          id: `project:${project.key}`,
          label: project.name,
          hint: project.key,
          group: "Switch project",
          run: () => {
            rememberProject(project.key);
            go(`/${project.key}/ready`)();
          },
        });
      }
    }

    list.push(
      { id: "theme:light", label: "Light theme", group: "Appearance", run: () => { onOpenChange(false); setTheme("light"); } },
      { id: "theme:dark", label: "Dark theme", group: "Appearance", run: () => { onOpenChange(false); setTheme("dark"); } },
      { id: "theme:system", label: "Follow the system", group: "Appearance", run: () => { onOpenChange(false); setTheme("system"); } },
      {
        id: "shortcuts",
        label: "Keyboard shortcuts",
        hint: "Every key the application binds.",
        group: "Help",
        run: () => { onOpenChange(false); onShortcuts(); },
      },
      { id: "settings", label: "Settings", group: "Account", run: go("/settings") },
      { id: "sign-out", label: "Sign out", group: "Account", run: () => { onOpenChange(false); signOut(); } },
    );

    return list;
  }, [current, found, navigate, needle, onOpenChange, onShortcuts, projectKey, projects, searching, setTheme, signOut]);

  const matching = useMemo(() => {
    const lowered = needle.toLowerCase();

    if (lowered === "" || keyPattern.test(lowered)) {
      return commands;
    }

    // What the instance found is not filtered again: it matched on a
    // description or a comment this screen never saw.
    return commands.filter(
      (command) =>
        command.found === true ||
        command.label.toLowerCase().includes(lowered) ||
        command.hint?.toLowerCase().includes(lowered) ||
        command.group.toLowerCase().includes(lowered),
    );
  }, [commands, needle]);

  const selected = matching[Math.min(index, Math.max(matching.length - 1, 0))];

  function onKeyDown(event: KeyboardEvent) {
    if (is("palette:next", event)) {
      event.preventDefault();
      setIndex((current) => Math.min(current + 1, matching.length - 1));
    } else if (is("palette:previous", event)) {
      event.preventDefault();
      setIndex((current) => Math.max(current - 1, 0));
    } else if (is("palette:run", event) && selected !== undefined) {
      event.preventDefault();
      selected.run();
    }
  }

  let lastGroup: string | undefined;

  return (
    <>
        <div className="flex items-center gap-2 border-b px-3">
          <SearchIcon className="size-4 text-muted-foreground" />
          <input
            id={searchId}
            autoFocus
            role="combobox"
            aria-expanded
            aria-controls="palette-commands"
            aria-activedescendant={selected ? `palette-${selected.id}` : undefined}
            aria-label="Search issues, or type a command"
            placeholder="Search issues, or type a command"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setIndex(0);
            }}
            onKeyDown={onKeyDown}
            className="h-11 flex-1 bg-transparent text-sm outline-hidden placeholder:text-muted-foreground"
          />
          <Keys id="palette:close" />
        </div>

        <ul id="palette-commands" role="listbox" className="max-h-80 overflow-y-auto p-1">
          {matching.length === 0 && (
            <li className="px-3 py-6 text-center text-sm text-muted-foreground">Nothing matches.</li>
          )}
          {matching.map((command) => {
            const heading = command.group !== lastGroup ? command.group : undefined;
            lastGroup = command.group;

            return (
              <li key={command.id} role="presentation">
                {heading !== undefined && (
                  <div className="px-2 pt-2 pb-1 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                    {heading}
                  </div>
                )}
                <div
                  id={`palette-${command.id}`}
                  role="option"
                  aria-selected={command === selected}
                  onMouseMove={() => setIndex(matching.indexOf(command))}
                  onClick={command.run}
                  className={cn(
                    "flex cursor-default items-center gap-3 rounded-md px-2 py-1.5 text-sm",
                    command === selected && "bg-accent text-accent-foreground",
                  )}
                >
                  <span className="flex-1 truncate">{command.label}</span>
                  {command.hint !== undefined && (
                    <span className="truncate text-xs text-muted-foreground">{command.hint}</span>
                  )}
                  {command === selected && <ArrowRightIcon className="size-3.5 text-muted-foreground" />}
                </div>
              </li>
            );
          })}
        </ul>
    </>
  );
}
