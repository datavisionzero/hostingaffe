import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { is, overlaid } from "@/shell/shortcuts";

/**
 * Leaving a form, both ways at once.
 *
 * A form that only offers Save leaves whoever changed their mind to find the
 * way out in the navigation. So every form has a Cancel button and answers
 * Escape, and the two are the same act rather than two behaviours that drift:
 * an untouched form is left at once, a form that was written in asks before
 * throwing the writing away.
 *
 * Escape is also what a picker's list and an open dialog answer to. Those are
 * nearer to the keyboard than the form is: a list stops the event itself, and
 * anything overlaid is skipped here.
 */
export function useAbandon(changed: boolean, onCancel: () => void): { leave: () => void; dialog: ReactNode } {
  const [asking, setAsking] = useState(false);

  const leave = useCallback(() => {
    if (changed) {
      setAsking(true);
    } else {
      onCancel();
    }
  }, [changed, onCancel]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (!is("form:cancel", event) || overlaid(event)) return;
      event.preventDefault();
      leave();
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [leave]);

  return {
    leave,
    dialog: (
      <Dialog open={asking} onOpenChange={setAsking}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Discard what you wrote?</DialogTitle>
            <DialogDescription>This form is not saved anywhere until you save it.</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setAsking(false)}>Keep writing</Button>
            <Button variant="destructive" onClick={() => { setAsking(false); onCancel(); }}>Discard</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    ),
  };
}
