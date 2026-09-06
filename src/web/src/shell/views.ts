import { FileTextIcon, type LucideIcon } from "lucide-react";

/**
 * The views of the instance, as ADR 0006 lists them, in the order the
 * navigation shows them. Every view is a route of its own, so that a pasted
 * link says what it shows.
 */
export type View = {
  id: string;
  label: string;
  path: string;
  icon: LucideIcon;
  group: "views" | "structure";
  /** What the view is for, in one sentence the empty state and the palette use. */
  hint: string;
};

export const views: View[] = [
  {
    id: "pages",
    label: "Pages",
    path: "/pages",
    icon: FileTextIcon,
    group: "structure",
    hint: "What the team knows and no record holds.",
  },
];

export function viewPath(view: View): string {
  return view.path;
}

/**
 * The address of a page. A page is the one object reached by a name rather than
 * a key (ADR 0021), so the slug is what the path carries — escaped, because it
 * is the author's word and not a generated one.
 */
export function pagePath(slug: string): string {
  return `/pages/${encodeURIComponent(slug)}`;
}
