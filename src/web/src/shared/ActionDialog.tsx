import { useId, useState, type FormEvent, type ReactElement } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";

type ActionDialogProps = {
  /**
   * The control that opens it. A dialog opened from a menu has none: the menu
   * closes on the click and would take its own trigger down with it, so the
   * caller keeps the open state and this stays mounted beside the menu.
   */
  trigger?: ReactElement;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  /** Destructive by default; `default` for a consequential move that removes nothing. */
  confirmVariant?: "destructive" | "default";
  onConfirm: () => Promise<void>;
};

/** A focused, reversible confirmation surface for consequential actions. */
export function ActionDialog({ trigger, open: controlled, onOpenChange, title, description, confirmLabel, confirmVariant = "destructive", onConfirm }: ActionDialogProps) {
  const [uncontrolled, setUncontrolled] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const open = controlled ?? uncontrolled;

  function changeOpen(next: boolean) {
    if (busy) return;
    setError(undefined);
    if (controlled === undefined) setUncontrolled(next);
    onOpenChange?.(next);
  }

  async function confirm() {
    setBusy(true);
    setError(undefined);
    try {
      await onConfirm();
      if (controlled === undefined) setUncontrolled(false);
      onOpenChange?.(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      {trigger !== undefined && <DialogTrigger render={trigger} />}
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
        <DialogFooter>
          <DialogClose render={<Button variant="outline" disabled={busy} />}>Cancel</DialogClose>
          <Button variant={confirmVariant} disabled={busy} onClick={() => void confirm()}>
            {busy ? "Working…" : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

type TextActionDialogProps = {
  /** Absent for a dialog opened from a menu; the menu closes on the click and
   *  would take its own trigger down with it, so the caller keeps the state. */
  trigger?: ReactElement;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  title: string;
  description?: string;
  label: string;
  initialValue: string;
  required?: boolean;
  submitLabel: string;
  onSubmit: (value: string) => Promise<void>;
};

/** An in-page replacement for single-value browser prompt dialogs. */
export function TextActionDialog({ trigger, open: controlled, onOpenChange, title, description, label, initialValue, required = true, submitLabel, onSubmit }: TextActionDialogProps) {
  const [uncontrolled, setUncontrolled] = useState(false);
  const [value, setValue] = useState(initialValue);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const id = useId();
  const open = controlled ?? uncontrolled;

  function changeOpen(next: boolean) {
    if (busy) return;
    if (next) setValue(initialValue);
    setError(undefined);
    if (controlled === undefined) setUncontrolled(next);
    onOpenChange?.(next);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = value.trim();
    if (required && !next) return;
    setBusy(true);
    setError(undefined);
    try {
      await onSubmit(next);
      changeOpen(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      {trigger !== undefined && <DialogTrigger render={trigger} />}
      <DialogContent>
        <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
            {description && <DialogDescription>{description}</DialogDescription>}
          </DialogHeader>
          <label className="grid gap-1 text-sm font-medium">
            {label}
            <Input id={id} value={value} onChange={(event) => setValue(event.target.value)} required={required} />
          </label>
          {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <DialogClose render={<Button variant="outline" disabled={busy} />}>Cancel</DialogClose>
            <Button type="submit" disabled={busy || (required && !value.trim())}>
              {busy ? "Saving…" : submitLabel}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
