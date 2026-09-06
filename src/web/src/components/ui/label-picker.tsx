import { useMemo, type ReactNode } from "react";
import { Picker, type Choice } from "./picker";

/** As much of a label as choosing one needs. */
export type PickableLabel = { name: string; group?: string | null; description?: string | null };

/**
 * Labels, as the {@link Picker} draws every choice — with the two things that
 * are the label set's own: the group each one belongs to, as the heading it is
 * offered under, and the exclusion that group carries. Choosing the sibling of
 * a group already on the issue replaces it here, visibly, rather than in the
 * refusal the instance would otherwise send back after saving.
 */
export function LabelPicker({
  label,
  hint,
  labels,
  value,
  onChange,
  onCreate,
  error,
  className,
}: {
  label: string;
  hint?: string;
  /** What the project has to choose from. */
  labels: PickableLabel[];
  /** The chosen names, in the order they were chosen. */
  value: string[];
  onChange: (names: string[]) => void;
  /**
   * Create a label that does not exist yet and hand it back. Omitted where
   * creating from here makes no sense — the list filter chooses among what is
   * there.
   */
  onCreate?: (name: string) => Promise<PickableLabel>;
  error?: ReactNode;
  className?: string;
}) {
  const groupOf = useMemo(() => {
    const known = new Map(labels.map((label) => [label.name, label.group ?? ""] as const));
    return (name: string) => known.get(name) ?? "";
  }, [labels]);

  const choices = useMemo<Choice[]>(() => {
    const carried = new Map(value.map((name) => [groupOf(name), name] as const));
    carried.delete("");

    return [...labels].sort(byGroupThenName).map((label) => ({
      id: label.name,
      name: label.name,
      hint: label.description,
      group: (label.group ?? "") === "" ? "Ungrouped" : `${label.group} · one of`,
      note: carried.has(label.group ?? "") ? (
        <span className="text-brand">replaces {carried.get(label.group ?? "")}</span>
      ) : undefined,
    }));
  }, [groupOf, labels, value]);

  return (
    <Picker
      label={label}
      hint={hint}
      className={className}
      multiple
      placeholder="Choose labels…"
      empty="No label of this project matches."
      error={error}
      value={value}
      choices={choices}
      onChange={(names) => onChange(withoutSiblings(names, groupOf))}
      onCreate={onCreate === undefined ? undefined : async (name) => asChoice(await onCreate(name))}
    />
  );
}

/** A group admits one label at a time; the one chosen last is the one meant. */
function withoutSiblings(names: string[], groupOf: (name: string) => string): string[] {
  return names.filter((name, index) => {
    const group = groupOf(name);
    return group === "" || !names.slice(index + 1).some((later) => groupOf(later) === group);
  });
}

function asChoice(label: PickableLabel): Choice {
  return { id: label.name, name: label.name, hint: label.description };
}

/** Grouped first, alphabetically, with the ungrouped ones after them. */
function byGroupThenName(a: PickableLabel, b: PickableLabel): number {
  const left = a.group ?? "";
  const right = b.group ?? "";

  if (left !== right) {
    return left === "" ? 1 : right === "" ? -1 : left.localeCompare(right);
  }

  return a.name.localeCompare(b.name);
}
