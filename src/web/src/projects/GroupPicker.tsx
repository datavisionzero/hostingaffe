import type { ReactNode } from "react";
import { Picker, type Choice } from "@/components/ui/picker";

/**
 * The group a label belongs to: chosen from the ones the project already has,
 * with no group as a row of its own and a new one as a deliberate last row.
 *
 * A free-text field made a typo into a new group nobody meant, silently, and
 * the group is what carries the exclusion — two labels that were supposed to
 * exclude one another quietly stopped doing so. Typing still names a new
 * group; it just takes the extra step of choosing to.
 */
export function GroupPicker({
  groups,
  value,
  onChange,
  error,
  className,
}: {
  /** The groups the project has, without the empty one. */
  groups: string[];
  /** The chosen group, empty for none. */
  value: string;
  onChange: (group: string) => void;
  error?: ReactNode;
  className?: string;
}) {
  const choices: Choice[] = [
    { id: "", name: "No group", hint: "Carried beside every other label" },
    ...groups.map((group) => ({ id: group, name: group, hint: "One of" })),
  ];

  return (
    <Picker
      label="Group"
      className={className}
      placeholder="No group"
      empty="No group of this project matches."
      createLabel={(name) => `New group ${name}`}
      error={error}
      choices={choices}
      value={value === "" ? [] : [value]}
      onChange={(chosen) => onChange(chosen[0] ?? "")}
      onCreate={(name) => Promise.resolve({ id: name, name })}
    />
  );
}
