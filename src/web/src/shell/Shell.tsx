import { CommandIcon } from "lucide-react";
import { lazy, Suspense, useEffect, useState } from "react";
import { Link, Navigate, Route, Routes, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { PagesView } from "@/pages/PagesView";
import { MachinesView } from "@/record/MachinesView";
import { InstallationsView } from "@/record/InstallationsView";
import { SoftwareListView } from "@/record/SoftwareListView";
import { SettingsView } from "@/settings/SettingsView";
import { AdminView } from "@/settings/AdminView";
import { AccountMenu } from "./AccountMenu";
import { AppSidebar } from "./AppSidebar";
import { Palette } from "./Palette";
import { Keys, ShortcutsDialog } from "./ShortcutsDialog";
import { is, overlaid, typing } from "./shortcuts";

// The Markdown pipeline of ADR 0007 weighs more than the shell; it arrives
// with the first page opened, not with the frame.
const PageView = lazy(() => import("@/pages/PageView").then((module) => ({ default: module.PageView })));
const NewPageView = lazy(() => import("@/pages/PageView").then((module) => ({ default: module.NewPageView })));

// The detail screens of the record carry the same Markdown pipeline, and the
// file screen its own comparison; the three lists are the frame's neighbours
// and stay with it.
const MachineView = lazy(() => import("@/record/MachineView").then((module) => ({ default: module.MachineView })));
const SoftwareView = lazy(() => import("@/record/SoftwareView").then((module) => ({ default: module.SoftwareView })));
const InstallationView = lazy(() => import("@/record/InstallationView").then((module) => ({ default: module.InstallationView })));
const FileView = lazy(() => import("@/record/FileView").then((module) => ({ default: module.FileView })));

/**
 * The application shell of ADR 0006: the frame every screen sits in, rendered
 * before any data arrives and never remounted by navigation. One instance holds
 * one team's infrastructure and every user sees all of it (VISION 9), so the
 * frame stands in the instance and not in a part of it.
 */
export function Shell() {
  const navigate = useNavigate();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);

  // The keys the frame itself owns, read from `shortcuts.ts` so that this
  // handler and the overview it feeds cannot come apart. `?` and `c` are bare
  // keys on purpose: ⌘P is the browser's print, and bare keys are the alphabet
  // the screens already use rather than a fight with the browser for a
  // modifier.
  useEffect(() => {
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (is("global:palette", event)) {
        event.preventDefault();
        // One dialog at a time: the palette arrives over whatever the overview
        // was explaining, not behind it.
        setShortcutsOpen(false);
        setPaletteOpen((open) => !open);
        return;
      }

      // Not while something is being typed, and not while a menu or a dialog
      // has the focus — those close with Escape, as they always did.
      if (typing(event) || overlaid(event)) {
        return;
      }

      if (is("global:shortcuts", event)) {
        event.preventDefault();
        setShortcutsOpen(true);
      } else if (is("global:create", event)) {
        event.preventDefault();
        void navigate("/pages/new");
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate]);

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="flex h-12 shrink-0 items-center gap-2 border-b px-3">
          <SidebarTrigger className="md:hidden" />
          <Separator orientation="vertical" className="mr-1 h-4! md:hidden" />
          <div className="flex-1" />
          <Button
            variant="outline"
            size="sm"
            className="hidden gap-2 text-muted-foreground sm:flex"
            onClick={() => setPaletteOpen(true)}
          >
            <CommandIcon className="size-3.5" />
            <span className="text-xs">Search or jump…</span>
            <Keys id="global:palette" />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            className="sm:hidden"
            aria-label="Command palette"
            onClick={() => setPaletteOpen(true)}
          >
            <CommandIcon />
          </Button>
          <AccountMenu onShortcuts={() => setShortcutsOpen(true)} />
        </header>

        <Routes>
          <Route path="/" element={<Navigate to="/machines" replace />} />
          <Route path="/settings/*" element={<SettingsView />} />
          <Route path="/admin/*" element={<AdminView />} />
          <Route path="/machines" element={<MachinesView />} />
          <Route path="/machines/:key" element={<Screen><MachineView /></Screen>} />
          {/* The path of a file carries slashes, so it is the rest of the
              address and not one segment of it. */}
          <Route path="/machines/:key/files/*" element={<Screen><FileView owner="machine" /></Screen>} />
          <Route path="/software" element={<SoftwareListView />} />
          <Route path="/software/:key" element={<Screen><SoftwareView /></Screen>} />
          <Route path="/installations" element={<InstallationsView />} />
          <Route path="/installations/:key" element={<Screen><InstallationView /></Screen>} />
          <Route path="/installations/:key/files/*" element={<Screen><FileView owner="installation" /></Screen>} />
          <Route path="/pages" element={<PagesView />} />
          <Route path="/pages/new" element={<Suspense fallback={<Busy title="Loading the screen…" />}><NewPageView /></Suspense>} />
          <Route path="/pages/:slug" element={<Suspense fallback={<Busy title="Loading the screen…" />}><PageView /></Suspense>} />
          {/* A typed or stale address is answered inside the frame rather than
              redirected away: silently landing somewhere else hides the typo,
              and a blank page is what `docs/human-interface.md` refuses. */}
          <Route path="*" element={<Empty title="Nothing at this address."><Link className="text-brand underline-offset-4 hover:underline" to="/machines">Go to the machines</Link></Empty>} />
        </Routes>
      </SidebarInset>

      <Palette
        open={paletteOpen}
        onOpenChange={setPaletteOpen}
        onShortcuts={() => setShortcutsOpen(true)}
      />
      <ShortcutsDialog open={shortcutsOpen} onOpenChange={setShortcutsOpen} />
    </SidebarProvider>
  );
}

/** A screen that arrives in a chunk of its own, with the frame's own waiting under it. */
function Screen({ children }: { children: React.ReactNode }) {
  return <Suspense fallback={<Busy title="Loading the screen…" />}>{children}</Suspense>;
}

/**
 * What the frame shows while a screen or the list behind it is still on its
 * way. Never a blank page, which `docs/human-interface.md` asks for, and never
 * silent to a screen reader.
 */
export function Busy({ title }: { title: string }) {
  return (
    <div aria-busy className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <span aria-hidden className="size-4.5 animate-pulse rounded-sm bg-brand" />
      <p role="status" className="text-sm text-muted-foreground">
        {title}
      </p>
    </div>
  );
}

export function Empty({ title, children }: { title: string; children?: React.ReactNode }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <p className="font-medium">{title}</p>
      {children !== undefined && <p className="text-sm text-muted-foreground">{children}</p>}
    </div>
  );
}
