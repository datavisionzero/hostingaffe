import { XIcon } from "lucide-react";
import { useId, useMemo, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/** One row of a picker, and what choosing it writes back. */
export type Choice = {
  /** What is written back when this row is chosen. */
  id: string;
  /** The name of it, drawn as the key or word it is. */
  name: string;
  /** What it is, beside the name. */
  hint?: string | null;
  /** A remark on the right of the row — a status, or what choosing replaces. */
  note?: ReactNode;
  /** The heading this row opens where it differs from the row above it. */
  group?: string | null;
};

type Option = { at: "choice"; choice: Choice } | { at: "create"; name: string };

/**
 * The one shape every choice in this application takes: chips for what is
 * chosen, a field that filters, a list that says what there is.
 *
 * Every field this replaced asked for a name that had to exist already and
 * showed nothing of what did — a word typed against a dictionary it would not
 * open, with the typo surfacing as a refusal over the whole form after saving.
 * The fillings differ in where the rows come from (the project's labels, its
 * epics, its members, a search across issues) and in nothing else, so the
 * keyboard, the chips and the refusal at the field are written once here.
 *
 * Plain elements and Tailwind, as ADR 0017 requires; the combobox roles are
 * what a reader with a screen reader is told.
 */
export function Picker({
  label,
  hint,
  className,
  multiple = false,
  value,
  onChange,
  choices,
  onQuery,
  onCreate,
  createLabel = (name) => `Create ${name}`,
  placeholder = "Choose…",
  error,
  busy = false,
  empty = "Nothing to choose.",
}: {
  /** The field's own label, drawn above the control. */
  label: string;
  hint?: string;
  className?: string;
  /** Several at a time, held as chips. A single choice replaces what was chosen. */
  multiple?: boolean;
  /** What is chosen, in the order it was chosen; one entry at most unless `multiple`. */
  value: string[];
  onChange: (ids: string[]) => void;
  /** What there is to choose from, in the order the list shows it. */
  choices: Choice[];
  /**
   * Where what was typed goes when the rows come from the instance rather than
   * from a list already held. Given, the rows arrive filtered and this control
   * does not filter them again.
   */
  onQuery?: (query: string) => void;
  /** Create what nobody offered, and hand back the row it became. */
  onCreate?: (name: string) => Promise<Choice>;
  createLabel?: (name: string) => string;
  placeholder?: string;
  /** A refusal that belongs to this field rather than over the form. */
  error?: ReactNode;
  /** An answer from the instance is on its way. */
  busy?: boolean;
  /** What an empty list says. */
  empty?: string;
}) {
  const id = useId();
  const listId = `${id}-list`;
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [creating, setCreating] = useState(false);
  const [why, setWhy] = useState<string>();

  const byId = useMemo(() => new Map(choices.map((choice) => [choice.id, choice] as const)), [choices]);
  const chosen = value.map((id) => byId.get(id) ?? { id, name: id });

  const options = useMemo<Option[]>(() => {
    const taken = new Set(value);
    const needle = query.trim().toLowerCase();
    const offered: Option[] = choices
      .filter((choice) => !taken.has(choice.id))
      .filter(
        (choice) =>
          onQuery !== undefined ||
          needle === "" ||
          choice.name.toLowerCase().includes(needle) ||
          (choice.hint ?? "").toLowerCase().includes(needle),
      )
      .map((choice) => ({ at: "choice", choice }));

    const named = query.trim();
    const exists = choices.some((choice) => choice.name.toLowerCase() === named.toLowerCase());

    return onCreate !== undefined && named !== "" && !exists ? [...offered, { at: "create", name: named }] : offered;
  }, [choices, onCreate, onQuery, query, value]);

  // A list that shrank under the cursor leaves it on whatever is now last.
  const cursor = options.length === 0 ? 0 : Math.min(active, options.length - 1);
  const headed = choices.some((choice) => (choice.group ?? "") !== "");

  function take(choice: Choice) {
    onChange(multiple ? [...value, choice.id] : [choice.id]);
    setQuery("");
    setActive(0);
    setWhy(undefined);
    onQuery?.("");

    if (!multiple) {
      setOpen(false);
    }
  }

  function remove(id: string) {
    onChange(value.filter((chosen) => chosen !== id));
    setWhy(undefined);
  }

  async function choose(option: Option) {
    if (option.at === "choice") {
      take(option.choice);
      return;
    }

    setCreating(true);
    setWhy(undefined);

    try {
      take(await onCreate!(option.name));
    } catch (problem) {
      setWhy(problem instanceof Error ? problem.message : "The instance did not answer.");
    } finally {
      setCreating(false);
    }
  }

  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();

      if (!open) {
        setOpen(true);
        return;
      }

      if (options.length > 0) {
        const from = Math.min(active, options.length - 1);
        setActive((from + (event.key === "ArrowDown" ? 1 : -1) + options.length) % options.length);
      }
    } else if (event.key === "Enter") {
      // Enter in a text field submits the form around it. While a choice is
      // open it belongs to the choice, and a name nobody offered is not a
      // reason to save.
      if (query.trim() !== "" || (open && options.length > 0)) {
        event.preventDefault();
      }

      if (open && options.length > 0 && !creating) {
        void choose(options[cursor]);
      }
    } else if (event.key === "Escape") {
      if (open) {
        // The form around this one closes on Escape as well; the list is what
        // Escape means while it is showing.
        event.preventDefault();
        event.stopPropagation();
        setOpen(false);
      }
    } else if (event.key === "Backspace" && query === "" && value.length > 0) {
      event.preventDefault();
      remove(value[value.length - 1]);
    }
  }

  const said = error ?? why;

  return (
    <div
      className={cn("grid min-w-0 gap-1 text-sm font-medium", className)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setOpen(false);
        }
      }}
    >
      <label htmlFor={id}>{label}</label>
      {hint !== undefined && <span className="text-xs font-normal text-muted-foreground">{hint}</span>}
      <div className="relative">
        <div
          className={cn(
            "flex min-h-8 w-full flex-wrap items-center gap-1 rounded-lg border border-input bg-transparent px-1.5 py-1 focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50 dark:bg-input/30",
            said != null && "border-destructive",
          )}
        >
          {chosen.map((choice) => (
            <span
              key={choice.id}
              className="inline-flex h-6 max-w-full items-center gap-1 rounded-4xl bg-secondary pr-1 pl-2 text-xs font-medium text-secondary-foreground"
            >
              <span className="truncate">{choice.name}</span>
              <button
                type="button"
                aria-label={`Remove ${choice.name}`}
                className="rounded-full p-0.5 hover:bg-foreground/10"
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => remove(choice.id)}
              >
                <XIcon className="size-3" />
              </button>
            </span>
          ))}
          <input
            id={id}
            role="combobox"
            aria-expanded={open}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-invalid={said != null || undefined}
            aria-activedescendant={open && options.length > 0 ? `${id}-option-${cursor}` : undefined}
            autoComplete="off"
            className="h-6 min-w-24 flex-1 bg-transparent px-1 text-base font-normal outline-none placeholder:text-muted-foreground md:text-sm"
            placeholder={value.length === 0 ? placeholder : ""}
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setActive(0);
              setOpen(true);
              onQuery?.(event.target.value);
            }}
            onFocus={() => setOpen(true)}
            onKeyDown={onKeyDown}
          />
        </div>
        {/* Below the field rather than over it: on a phone the list must not
            sit on top of what is being typed. */}
        {open && (
          <ul
            id={listId}
            role="listbox"
            aria-label={label}
            className="absolute inset-x-0 top-full z-20 mt-1 max-h-64 overflow-y-auto rounded-lg border bg-popover py-1 text-sm font-normal shadow-md"
          >
            {options.map((option, index) => (
              <Row
                key={option.at === "create" ? " create" : option.choice.id}
                id={`${id}-option-${index}`}
                option={option}
                heading={headed ? heading(options, index) : undefined}
                active={index === cursor}
                label={option.at === "create" ? createLabel(option.name) : undefined}
                creating={creating && option.at === "create"}
                onHover={() => setActive(index)}
                onChoose={() => void choose(option)}
              />
            ))}
            {options.length === 0 && (
              <li aria-disabled className="px-3 py-2 text-xs text-muted-foreground">
                {busy ? "Asking the instance…" : empty}
              </li>
            )}
          </ul>
        )}
      </div>
      {said != null && (
        <p role="alert" className="text-xs font-normal text-destructive">
          {said}
        </p>
      )}
    </div>
  );
}

function Row({
  id,
  option,
  heading,
  active,
  label,
  creating,
  onHover,
  onChoose,
}: {
  id: string;
  option: Option;
  heading?: string;
  active: boolean;
  label?: string;
  creating: boolean;
  onHover: () => void;
  onChoose: () => void;
}) {
  return (
    <>
      {heading !== undefined && (
        <li aria-hidden className="px-3 pt-2 pb-1 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
          {heading}
        </li>
      )}
      <li
        id={id}
        role="option"
        aria-selected={active}
        // Thumb-sized rows, and the focus stays in the field so that the next
        // one can be chosen without reaching for it again.
        className={cn("flex min-h-9 cursor-pointer items-center gap-2 px-3 py-1.5", active && "bg-accent")}
        onMouseMove={onHover}
        onMouseDown={(event) => event.preventDefault()}
        onClick={onChoose}
      >
        {option.at === "create" ? (
          <span>{creating ? "Creating…" : label}</span>
        ) : (
          <>
            <span className="shrink-0 font-mono text-xs">{option.choice.name}</span>
            {option.choice.hint != null && (
              <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">{option.choice.hint}</span>
            )}
            {option.choice.note != null && (
              <span className="ml-auto flex shrink-0 items-center gap-1 text-xs">{option.choice.note}</span>
            )}
          </>
        )}
      </li>
    </>
  );
}

/** The group heading this row opens, if it opens one. */
function heading(options: Option[], index: number): string | undefined {
  const option = options[index];

  if (option.at === "create") {
    return undefined;
  }

  const group = option.choice.group ?? "";
  const before = index === 0 ? undefined : options[index - 1];
  const previous = before === undefined || before.at === "create" ? null : (before.choice.group ?? "");

  return previous === group || group === "" ? undefined : group;
}
