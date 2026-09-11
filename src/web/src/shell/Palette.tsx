import { ArrowRightIcon, SearchIcon } from "lucide-react";
import { useEffect, useId, useMemo, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router";
import { api, type Schemas } from "@/api/client";
import { useTheme } from "@/components/theme-provider";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useSession } from "@/session/useSession";
import { cn } from "@/lib/utils";
import { Keys } from "./ShortcutsDialog";
import { is } from "./shortcuts";
import { anchorPath, installationPath, machinePath, softwarePath } from "@/record/addresses";
import { pagePath, viewPath, views } from "./views";

type SearchHit = Schemas["SearchHit"];

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
 * The command palette — ⌘K, or Ctrl+K — over the views and the few acts the
 * shell itself has. Words typed into it ask the instance for a few full-text
 * matches.
 *
 * For the wiki this is more than a nicety: the pages are flat because the
 * search is what a hierarchy would have been, so this is how one is found at
 * all.
 *
 * Owned rather than imported (ADR 0017): a filtered list with a roving index
 * inside a Base UI dialog, which is what a palette is before it does more.
 */
type PaletteProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The overview of the keys, which the palette is one of the ways to. */
  onShortcuts: () => void;
};

export function Palette({ open, onOpenChange, onShortcuts }: PaletteProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="top-[20%] translate-y-0 gap-0 overflow-hidden p-0 sm:max-w-lg" showCloseButton={false}>
        <DialogHeader className="sr-only">
          <DialogTitle>Command palette</DialogTitle>
          <DialogDescription>Search the record, the views and the commands.</DialogDescription>
        </DialogHeader>
        {open && (
          <PaletteBody onOpenChange={onOpenChange} onShortcuts={onShortcuts} />
        )}
      </DialogContent>
    </Dialog>
  );
}

/** Mounted while the palette is open, so that its query starts empty every time. */
function PaletteBody({ onOpenChange, onShortcuts }: Omit<PaletteProps, "open">) {
  const navigate = useNavigate();
  const { setTheme } = useTheme();
  const { me, signOut } = useSession();
  const [query, setQuery] = useState("");
  const [index, setIndex] = useState(0);
  const [found, setFound] = useState<{ of: string; hits: SearchHit[] }>({ of: "", hits: [] });
  const searchId = useId();

  const needle = query.trim();
  // The instance's one search: every field, every Markdown body and every file
  // (`docs/api.md`, Searching). "Where was that again" is the question a host
  // record is asked most often, and this is where it is asked.
  const searching = needle.length >= shortest;

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
          const hits = await api.GET("/api/search", {
            params: { query: { q: needle, limit: matches } },
            signal: controller.signal,
          });

          setFound({ of: needle, hits: hits.data ?? [] });
        } catch {
          // Nothing found is what the palette shows; the commands remain.
        }
      })();
    }, settle);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [needle, searching]);

  const commands = useMemo<Command[]>(() => {
    const go = (to: string) => () => {
      onOpenChange(false);
      void navigate(to);
    };

    const list: Command[] = [];

    if (searching) {
      for (const hit of found.of === needle ? found.hits : []) {
        const to = where(hit);

        // A deployment is the one hit with no screen of its own: it is read
        // under its installation, which is where this sends whoever found it.
        if (to === undefined) {
          continue;
        }

        list.push({
          id: `found:${hit.kind}:${hit.key}:${hit.number ?? ""}`,
          label: named(hit),
          // What matched, not what it is called: the row is already the name,
          // and the surface is why this row is here at all.
          hint: `${hit.kind} · ${hit.where}`,
          group: "Found",
          run: go(to),
          found: true,
        });
      }
    }

    for (const view of views) {
      list.push({
        id: `view:${view.id}`,
        label: view.label,
        hint: view.hint,
        group: "Go to",
        run: go(viewPath(view)),
      });
    }

    // The palette is the other way to everything the screens offer, so what
    // can be created is reachable from it too.
    list.push({ id: "create:page", label: "Create page", group: "Create", run: go("/pages/new") });

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

    // The palette is the other way to everything the application offers, and
    // the administration is behind the account menu rather than the sidebar —
    // which makes it exactly the screen somebody looks for here first.
    if (me.administrator) {
      list.push({
        id: "admin",
        label: "Instance administration",
        hint: "Users, and transactional email.",
        group: "Account",
        run: go("/admin"),
      });
    }

    return list;
  }, [found, me.administrator, navigate, needle, onOpenChange, onShortcuts, searching, setTheme, signOut]);

  const matching = useMemo(() => {
    const lowered = needle.toLowerCase();

    if (lowered === "") {
      return commands;
    }

    // What the instance found is not filtered again: it matched on a body this
    // screen never saw.
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
            aria-label="Search the record, or type a command"
            placeholder="Search the record, or type a command"
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

/**
 * What a hit is called in the list. `name` is empty where the address is the
 * whole of it, which is every file — and a machine's file is called by the
 * whole place it lies, directory and path together, because the directory is
 * one of the surfaces the search reads (`docs/api.md`, Searching).
 */
function named(hit: SearchHit): string {
  if (hit.name !== "") {
    return hit.name;
  }
  return hit.directory === null || hit.directory === undefined || hit.directory === ""
    ? hit.key
    : `${hit.directory.replace(/\/+$/, "")}/${hit.key}`;
}

/**
 * Where a hit leads. `key` is the address of what was found — a key, a slug, or
 * the path of a file under its owner — and `owner` is what a file belongs to
 * (`docs/api.md`, Searching).
 */
function where(hit: SearchHit): string | undefined {
  switch (hit.kind) {
    case "machine": return machinePath(hit.key);
    case "software": return softwarePath(hit.key);
    case "installation": return installationPath(hit.key);
    case "page": return pagePath(hit.key);
    case "file": return hit.owner === null || hit.owner === undefined
      ? undefined
      : `${anchorPath(hit.owner)}/files/${hit.key.split("/").map(encodeURIComponent).join("/")}`;
    // A deployment is read under its installation, which `key` names.
    case "deployment": return installationPath(hit.key);
    default: return undefined;
  }
}
