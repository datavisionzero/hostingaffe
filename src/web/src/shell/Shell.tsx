import { CommandIcon } from "lucide-react";
import { lazy, Suspense, useEffect, useState } from "react";
import { Navigate, Route, Routes, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { PagesView } from "@/pages/PagesView";
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
          <Route path="/" element={<Navigate to="/pages" replace />} />
          <Route path="/settings/*" element={<SettingsView />} />
          <Route path="/admin/*" element={<AdminView />} />
          <Route path="/pages" element={<PagesView />} />
          <Route path="/pages/new" element={<Suspense fallback={<Busy title="Loading the screen…" />}><NewPageView /></Suspense>} />
          <Route path="/pages/:slug" element={<Suspense fallback={<Busy title="Loading the screen…" />}><PageView /></Suspense>} />
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
