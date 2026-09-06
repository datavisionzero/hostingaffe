import {
  BoldIcon,
  CodeIcon,
  Heading2Icon,
  ItalicIcon,
  LinkIcon,
  ListIcon,
  Maximize2Icon,
  QuoteIcon,
} from "lucide-react";
import { lazy, Suspense, useCallback, useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { Markdown } from "./Markdown";
import { linked, prefixed, wrapped, type Selected } from "./markdownCommands";
import type { EditorHandle } from "./Editor";

// The editor is the heaviest thing this application loads, and it is wanted on
// the screens where somebody writes rather than in the frame (ADR 0006, ADR
// 0023). The chunk is fetched when a field is first put on a screen, and the
// field is a quiet placeholder until it is there — not a second, plainer text
// area that would be swapped out from under whoever had started typing in it.
const Editor = lazy(() => import("./Editor"));

/**
 * The one Markdown editor of the application. Everything written in it goes
 * through here — an issue's description, a comment, a question, an answer, a
 * result, an epic's description, a page's body, a release's notes — so what is
 * decided here is decided in all of them at once.
 *
 * It edits Markdown as source and never as a document model of its own (ADR
 * 0007). What it adds around that is what somebody writing wants: room, the
 * structure of the text visible while it is typed, buttons and keys for the
 * marks nobody wants to spell out, a preview beside the text where the screen
 * is wide enough — and, where it is not enough, the whole window.
 */
export function MarkdownField({
  label,
  value,
  onChange,
  onSubmit,
  hint,
  size = "roomy",
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  /** What ⌘/Ctrl+Enter does from inside the text, where there is anything to do. */
  onSubmit?: () => void;
  hint?: string;
  /**
   * How much room the field takes to begin with. A description or a page is
   * the thing being written and starts as a surface; a comment is usually one
   * sentence, and grows if it turns out to be more than one.
   */
  size?: "roomy" | "compact";
}) {
  const [full, setFull] = useState(false);

  return (
    <>
      <Surface
        label={label}
        value={value}
        onChange={onChange}
        onSubmit={onSubmit}
        hint={hint}
        size={size}
        onExpand={() => setFull(true)}
      />
      {/* The same field over the whole window. It is a dialog, so Escape
          closes it and stops there: the form behind it answers Escape as well
          (`shared/abandon.tsx`), and an expanded field that took the whole form
          down on the way out would be a trap. */}
      <Dialog open={full} onOpenChange={setFull}>
        <DialogContent
          showCloseButton={false}
          className="inset-4 grid h-auto w-auto max-w-none translate-x-0 translate-y-0 grid-rows-[1fr] gap-0 sm:max-w-none"
        >
          <DialogTitle className="sr-only">{label}</DialogTitle>
          <Surface
            label={label}
            value={value}
            onChange={onChange}
            onSubmit={onSubmit === undefined ? undefined : () => { setFull(false); onSubmit(); }}
            hint={hint}
            size="full"
            autoFocus
            onCollapse={() => setFull(false)}
          />
        </DialogContent>
      </Dialog>
    </>
  );
}

/**
 * The field itself: its name and its actions, the text, and the preview.
 * Written once and drawn twice — in the form and over the window — because a
 * full-screen editor that is a second implementation of the same field is two
 * things to keep in step.
 */
function Surface({
  label,
  value,
  onChange,
  onSubmit,
  hint,
  size,
  autoFocus,
  onExpand,
  onCollapse,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  onSubmit?: () => void;
  hint?: string;
  size: "roomy" | "compact" | "full";
  autoFocus?: boolean;
  onExpand?: () => void;
  onCollapse?: () => void;
}) {
  const id = useId();
  const [preview, setPreview] = useState(false);
  // In state rather than in a ref: the toolbar has nothing to act on until the
  // editor is there, and it has to be drawn as much.
  const [editor, setEditor] = useState<EditorHandle | null>(null);
  const onReady = useCallback((handle: EditorHandle | null) => setEditor(handle), []);

  // Where there is room for both, the preview stands beside the text; where
  // there is not, it takes its place and a switch says which is showing. A
  // comment is narrow work and keeps the switch at every width.
  const beside = size !== "compact";
  const heights = {
    roomy: { min: "40vh", max: "60vh" },
    compact: { min: "6rem", max: "24rem" },
    full: { min: "100%", max: "100%" },
  }[size];

  /** A toolbar button, on the selection the editor currently holds. */
  function apply(command: (at: Selected) => Selected) {
    editor?.apply(command);
  }

  return (
    <div className={cn("grid gap-1 text-sm font-medium", size === "full" && "min-h-0 grid-rows-[auto_1fr]")}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span id={`${id}-label`}>{label}</span>
        {/* One tab stop, not nine. A toolbar in front of the text would
            otherwise put every mark between the keyboard and the field it acts
            on; the arrows move within it, which is what a toolbar is for. */}
        <div role="toolbar" aria-label={`${label}, formatting`} aria-controls={id} className="flex items-center gap-0.5" onKeyDown={roving}>
          <Mark first disabled={editor === null} what="Bold" icon={BoldIcon} onClick={() => apply((at) => wrapped(at, "**"))} />
          <Mark disabled={editor === null} what="Italic" icon={ItalicIcon} onClick={() => apply((at) => wrapped(at, "_"))} />
          <Mark disabled={editor === null} what="Code" icon={CodeIcon} onClick={() => apply((at) => wrapped(at, "`"))} />
          <Mark disabled={editor === null} what="Link" icon={LinkIcon} onClick={() => apply(linked)} />
          <Mark disabled={editor === null} what="Heading" icon={Heading2Icon} onClick={() => apply((at) => prefixed(at, "## "))} />
          <Mark disabled={editor === null} what="Quote" icon={QuoteIcon} onClick={() => apply((at) => prefixed(at, "> "))} />
          <Mark disabled={editor === null} what="List" icon={ListIcon} onClick={() => apply((at) => prefixed(at, "- "))} />
          <span aria-hidden className="mx-1 h-4 w-px bg-border" />
          <button
            type="button"
            tabIndex={-1}
            className={cn(
              "rounded-md px-1.5 py-0.5 text-xs font-normal text-brand hover:underline",
              beside && "md:hidden",
            )}
            aria-pressed={preview}
            onClick={() => setPreview((was) => !was)}
          >
            {preview ? "Edit" : "Preview"}
          </button>
          {onExpand !== undefined && <Mark what="Full screen" icon={Maximize2Icon} onClick={onExpand} />}
          {onCollapse !== undefined && (
            <Button type="button" tabIndex={-1} size="sm" variant="outline" onClick={onCollapse}>Done</Button>
          )}
        </div>
      </div>

      <div className={cn("grid min-h-0 gap-3", beside && "md:grid-cols-2")}>
        <div
          className={cn(
            "grid min-h-0 overflow-hidden rounded-lg border bg-background font-normal focus-within:ring-3 focus-within:ring-ring/50",
            preview && (beside ? "hidden md:grid" : "hidden"),
          )}
        >
          <Suspense
            fallback={
              <div
                aria-busy
                className="animate-pulse bg-muted/40"
                style={{ minHeight: heights.min }}
              />
            }
          >
            <Editor
              value={value}
              onChange={onChange}
              onSubmit={onSubmit}
              onReady={onReady}
              label={label}
              hint={hint}
              autoFocus={autoFocus}
              minHeight={heights.min}
              maxHeight={heights.max}
            />
          </Suspense>
        </div>

        <div
          aria-label={`${label}, preview`}
          className={cn(
            "min-h-0 overflow-auto rounded-lg border bg-muted/30 p-3 font-normal",
            preview ? "" : beside ? "hidden md:block" : "hidden",
          )}
          style={size === "full" ? undefined : { maxHeight: heights.max }}
        >
          <Markdown>{value || "_Nothing to preview._"}</Markdown>
        </div>
      </div>
    </div>
  );
}

function Mark({ what, icon: Icon, disabled, first, onClick }: { what: string; icon: typeof BoldIcon; disabled?: boolean; first?: boolean; onClick: () => void }) {
  return (
    <Button
      type="button"
      variant="ghost"
      size="icon-sm"
      // The first control is the toolbar's one tab stop; the arrows reach the
      // rest, and the focus moves the stop with it.
      tabIndex={first === true ? 0 : -1}
      aria-label={what}
      title={what}
      disabled={disabled}
      onClick={onClick}
    >
      <Icon />
    </Button>
  );
}

/**
 * The arrows within a toolbar, as the pattern asks: left and right move from
 * control to control and the tab stop moves with them, Home and End go to the
 * ends. Everything else is left alone.
 */
function roving(event: React.KeyboardEvent<HTMLDivElement>) {
  const keys: Record<string, number | "first" | "last"> = {
    ArrowLeft: -1,
    ArrowRight: 1,
    Home: "first",
    End: "last",
  };
  const step = keys[event.key];

  if (step === undefined) {
    return;
  }

  const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>("button:not([disabled])"));
  const at = controls.indexOf(document.activeElement as HTMLElement);

  if (at === -1) {
    return;
  }

  event.preventDefault();
  const to = step === "first" ? 0 : step === "last" ? controls.length - 1 : (at + step + controls.length) % controls.length;

  for (const control of controls) {
    control.tabIndex = control === controls[to] ? 0 : -1;
  }

  controls[to]!.focus();
}
