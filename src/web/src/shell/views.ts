import { BoxIcon, FileTextIcon, PackageIcon, ServerIcon, type LucideIcon } from "lucide-react";

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
    id: "machines",
    label: "Machines",
    path: "/machines",
    icon: ServerIcon,
    group: "views",
    hint: "The computers the team rents or owns.",
  },
  {
    id: "installations",
    label: "Installations",
    path: "/installations",
    icon: BoxIcon,
    group: "views",
    hint: "One software on one machine, and the version on it.",
  },
  {
    id: "software",
    label: "Software",
    path: "/software",
    icon: PackageIcon,
    group: "views",
    hint: "What the installations are installations of.",
  },
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
