import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Kbd, KbdGroup } from "@/components/ui/kbd";
import { drawn, groups, shortcuts, type ShortcutId } from "./shortcuts";

/**
 * A shortcut as it is drawn, wherever one is advertised: the palette button in
 * the header, a menu entry, a row of the overview. It reads the keys from
 * `shortcuts.ts` rather than spelling them again, so a rebinding moves the
 * label with it.
 */
export function Keys({ id, className }: { id: ShortcutId; className?: string }) {
  return (
    <KbdGroup className={className}>
      {drawn(id).map((cap) => (
        <Kbd key={cap}>{cap}</Kbd>
      ))}
    </KbdGroup>
  );
}

/**
 * The overview the `?` opens: every key the application binds, grouped by where
 * it applies. A dialog rather than a screen of its own, because looking a key
 * up is something done in the middle of the work whose keys are being looked
 * up — a route would leave the list it is about.
 */
export function ShortcutsDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Keyboard shortcuts</DialogTitle>
          <DialogDescription>
            The bare keys stay out of the way while something is being typed into.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          {groups.map((group) => {
            // An id, not a label: `aria-labelledby` reads a space as the end of
            // one, and two of the three groups are two words.
            const heading = `shortcuts-${group.toLowerCase().replace(/\s+/g, "-")}`;

            return (
              <section key={group} aria-labelledby={heading}>
                <h3 id={heading} className="mb-1.5 text-xs font-medium text-muted-foreground">
                  {group}
                </h3>
                <dl className="divide-y rounded-md ring-1 ring-foreground/10">
                  {shortcuts
                    .filter((shortcut) => shortcut.group === group)
                    .map((shortcut) => (
                      <div key={shortcut.id} className="flex items-center justify-between gap-4 px-3 py-2">
                        <dt>{shortcut.what}</dt>
                        <dd>
                          <Keys id={shortcut.id} />
                        </dd>
                      </div>
                    ))}
                </dl>
              </section>
            );
          })}
        </div>
      </DialogContent>
    </Dialog>
  );
}
